using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OgDub.Core;

namespace OgDub.Audio;

internal sealed class MicSideBCapture : IDisposable
{
    private const float HearThroughGain = 0.55f;

    private WasapiCapture? _capture;
    private MMDevice? _device;
    private WaveFileWriter? _writer;
    private WaveFormat? _format;
    private readonly ManualResetEventSlim _stopped = new(true);
    private string? _deviceId;
    private bool _taping;

    private bool _hearThrough;
    private string? _renderDeviceId;
    private BufferedWaveProvider? _monitorBuffer;
    private IWavePlayer? _monitorPlayer;
    private MMDevice? _monitorDevice;

    public bool IsRunning => _capture is not null;
    public bool IsTaping => _taping && _writer is not null;
    public float Peak { get; private set; }

    /// <summary>Open the mic for live metering (no Side B file yet).</summary>
    public void StartMonitor(string? deviceId)
    {
        if (_capture is not null
            && string.Equals(_deviceId ?? "", deviceId ?? "", StringComparison.Ordinal)
            && !_taping)
            return;

        var hear = _hearThrough;
        var renderId = _renderDeviceId;
        Stop();
        OpenCapture(deviceId);
        if (hear)
            SetHearThrough(true, renderId);
    }

    /// <summary>Arm Side B WAV on the chosen mic. Restarts capture if the device changed.</summary>
    public void StartTape(string wavPath, string? deviceId)
    {
        if (_capture is null
            || !string.Equals(_deviceId ?? "", deviceId ?? "", StringComparison.Ordinal))
        {
            var hear = _hearThrough;
            var renderId = _renderDeviceId;
            Stop();
            OpenCapture(deviceId);
            if (hear)
                SetHearThrough(true, renderId);
        }

        if (_capture is null || _format is null)
            throw new InvalidOperationException("Microphone did not open.");

        EndTapeWriter();
        Directory.CreateDirectory(Path.GetDirectoryName(wavPath)!);
        _writer = new WaveFileWriter(wavPath, _format);
        _taping = true;
        Peak = 0;
    }

    public void StopTape()
    {
        EndTapeWriter();
        _taping = false;
    }

    /// <summary>
    /// Route the live mic to a render device so the operator can hear Side B input.
    /// Uses headphones if Play-to points at them — speakers can feedback.
    /// </summary>
    public void SetHearThrough(bool enabled, string? renderDeviceId)
    {
        _hearThrough = enabled;
        _renderDeviceId = renderDeviceId ?? "";
        RebuildMonitorOut();
    }

    public void Stop()
    {
        EndTapeWriter();
        _taping = false;
        StopMonitorOut();
        var capture = _capture;
        if (capture is null)
        {
            DisposeDevice();
            Peak = 0;
            return;
        }

        _stopped.Reset();
        try
        {
            capture.StopRecording();
        }
        catch (Exception)
        {
            // still tear down below
        }

        if (!_stopped.Wait(TimeSpan.FromSeconds(3)))
            ForceCloseCapture();
        Peak = 0;
    }

    public void Dispose()
    {
        _hearThrough = false;
        Stop();
        _stopped.Dispose();
    }

    private void OpenCapture(string? deviceId)
    {
        Peak = 0;
        _stopped.Reset();
        _deviceId = deviceId ?? "";
        _device = OpenDevice(deviceId);
        _capture = _device is null
            ? new WasapiCapture()
            : new WasapiCapture(_device);
        _format = _capture.WaveFormat;
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnStopped;
        _capture.StartRecording();
    }

    private static MMDevice? OpenDevice(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (string.IsNullOrWhiteSpace(deviceId))
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);

        try
        {
            return enumerator.GetDevice(deviceId);
        }
        catch (Exception)
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var format = _format;
        if (format is null || e.BytesRecorded <= 0)
            return;

        Peak = PcmPeak.Max(e.Buffer, e.BytesRecorded, format);
        PushHearThrough(e.Buffer, e.BytesRecorded, format);

        var writer = _writer;
        if (writer is null)
            return;

        var copy = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
        if (PcmPeak.IsFloat32(format))
            SafetyPad.ApplyIeeeFloat32(copy.AsSpan(0, e.BytesRecorded));
        else if (format.BitsPerSample == 16)
            SafetyPad.ApplyPcm16(copy.AsSpan(0, e.BytesRecorded));
        writer.Write(copy, 0, e.BytesRecorded);
    }

    private void PushHearThrough(byte[] buffer, int count, WaveFormat format)
    {
        var dest = _monitorBuffer;
        if (dest is null || count <= 0)
            return;

        try
        {
            var copy = new byte[count];
            Buffer.BlockCopy(buffer, 0, copy, 0, count);
            ApplyGain(copy.AsSpan(0, count), format, HearThroughGain);
            dest.AddSamples(copy, 0, count);
        }
        catch (Exception)
        {
            // overrun / disposed — drop the packet
        }
    }

    private static void ApplyGain(Span<byte> buffer, WaveFormat format, float gain)
    {
        if (PcmPeak.IsFloat32(format))
        {
            for (var i = 0; i + 4 <= buffer.Length; i += 4)
            {
                var slice = buffer.Slice(i, 4);
                var sample = BitConverter.ToSingle(slice);
                BitConverter.TryWriteBytes(slice, sample * gain);
            }
        }
        else if (format.BitsPerSample == 16)
        {
            for (var i = 0; i + 2 <= buffer.Length; i += 2)
            {
                var sample = BitConverter.ToInt16(buffer.Slice(i, 2));
                var scaled = (int)Math.Round(sample * (double)gain);
                scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
                BitConverter.TryWriteBytes(buffer.Slice(i, 2), (short)scaled);
            }
        }
    }

    private void RebuildMonitorOut()
    {
        StopMonitorOut();
        if (!_hearThrough || _format is null)
            return;

        try
        {
            _monitorBuffer = new BufferedWaveProvider(_format)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(300)
            };
            _monitorPlayer = CreateMonitorPlayer(_renderDeviceId);
            _monitorPlayer.Init(_monitorBuffer);
            _monitorPlayer.Play();
        }
        catch (Exception)
        {
            StopMonitorOut();
        }
    }

    private IWavePlayer CreateMonitorPlayer(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, latency: 60);

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _monitorDevice = enumerator.GetDevice(deviceId);
            return new WasapiOut(_monitorDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 60);
        }
        catch (Exception)
        {
            _monitorDevice?.Dispose();
            _monitorDevice = null;
            return new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, latency: 60);
        }
    }

    private void StopMonitorOut()
    {
        var player = _monitorPlayer;
        _monitorPlayer = null;
        try
        {
            player?.Stop();
        }
        catch (Exception)
        {
        }

        player?.Dispose();
        _monitorBuffer = null;

        var device = _monitorDevice;
        _monitorDevice = null;
        device?.Dispose();
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        EndTapeWriter();
        StopMonitorOut();
        ForceCloseCapture();
        _stopped.Set();
    }

    private void EndTapeWriter()
    {
        var writer = _writer;
        _writer = null;
        writer?.Dispose();
        _taping = false;
    }

    private void ForceCloseCapture()
    {
        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            capture.DataAvailable -= OnData;
            capture.RecordingStopped -= OnStopped;
            capture.Dispose();
        }

        DisposeDevice();
        _format = null;
        _deviceId = null;
    }

    private void DisposeDevice()
    {
        var device = _device;
        _device = null;
        device?.Dispose();
    }
}
