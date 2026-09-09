using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OgDub.Core;

namespace OgDub.Audio;

internal sealed class MicSideBCapture : IDisposable
{
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private WaveFormat? _format;
    private readonly ManualResetEventSlim _stopped = new(true);

    public bool IsRunning => _capture is not null;
    public float Peak { get; private set; }

    public void Start(string wavPath)
    {
        Stop();
        Peak = 0;
        _stopped.Reset();
        _capture = new WasapiCapture();
        _format = _capture.WaveFormat;
        Directory.CreateDirectory(Path.GetDirectoryName(wavPath)!);
        _writer = new WaveFileWriter(wavPath, _format);
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnStopped;
        _capture.StartRecording();
    }

    public void Stop()
    {
        _capture?.StopRecording();
        _stopped.Wait(TimeSpan.FromSeconds(3));
    }

    public void Dispose()
    {
        Stop();
        _stopped.Dispose();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var format = _format;
        var writer = _writer;
        if (format is null || writer is null || e.BytesRecorded <= 0)
            return;

        Peak = Math.Max(Peak, PcmPeak.Max(e.Buffer, e.BytesRecorded, format));
        var copy = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
        if (PcmPeak.IsFloat32(format))
            SafetyPad.ApplyIeeeFloat32(copy.AsSpan(0, e.BytesRecorded));
        else if (format.BitsPerSample == 16)
            SafetyPad.ApplyPcm16(copy.AsSpan(0, e.BytesRecorded));
        writer.Write(copy, 0, e.BytesRecorded);
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        _writer?.Dispose();
        _writer = null;
        _capture?.Dispose();
        _capture = null;
        _format = null;
        _stopped.Set();
    }
}
