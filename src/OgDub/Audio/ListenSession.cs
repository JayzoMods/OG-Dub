using System.IO;
using NAudio.Wave;
using OgDub.Core;

namespace OgDub.Audio;

internal sealed class ListenSession : IDisposable
{
    private readonly ProcessLoopbackCapture _process = new();
    private readonly MixLoopbackCapture _mix = new();
    private readonly object _recGate = new();
    private PcmRingBuffer? _ring;
    private WaveFormat? _format;
    private WaveFileWriter? _rec;
    private string? _pendingRecPath;
    private int _listenPid = int.MinValue;
    private bool _listenWholeMix;
    private float _takePeak;

    public float Peak => Math.Max(_process.Peak, _mix.Peak);
    public float TakePeak => _takePeak;
    public string Mode { get; private set; } = "";
    public int AimedProcessId => _listenPid;
    public bool AimedWholeMix => _listenWholeMix;
    public bool IsListening => _process.IsRunning || _mix.IsRunning;

    public TimeSpan RingFilled
    {
        get
        {
            var format = _format;
            var ring = _ring;
            if (format is null || ring is null)
                return TimeSpan.Zero;
            return ring.FilledDuration(format.AverageBytesPerSecond);
        }
    }

    public void Ensure(Station station)
    {
        if (IsListening && _listenPid == station.ProcessId && _listenWholeMix == station.IsWholeMix)
            return;

        StopListen();
        _listenPid = station.ProcessId;
        _listenWholeMix = station.IsWholeMix;

        if (!station.IsWholeMix)
        {
            try
            {
                _process.Start((uint)station.ProcessId, includeTree: true, OnFormat, OnSamples);
                Mode = _process.Mode;
                return;
            }
            catch (Exception)
            {
            }
        }

        try
        {
            _process.Start((uint)Environment.ProcessId, includeTree: false, OnFormat, OnSamples);
            Mode = _process.Mode;
        }
        catch (Exception)
        {
            _mix.Start(OnFormat, OnSamples);
            Mode = "MIX";
        }
    }

    public void BeginTape(string wavPath)
    {
        _takePeak = 0;
        lock (_recGate)
        {
            _rec?.Dispose();
            _rec = null;
            if (_format is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(wavPath)!);
                _rec = new WaveFileWriter(wavPath, _format);
                _pendingRecPath = null;
            }
            else
            {
                _pendingRecPath = wavPath;
            }
        }
    }

    public void EndTape()
    {
        lock (_recGate)
        {
            _rec?.Dispose();
            _rec = null;
            _pendingRecPath = null;
        }
    }

    public bool TrySaveReplay(string wavPath, out TimeSpan duration, out float peak)
    {
        duration = TimeSpan.Zero;
        peak = 0;
        var format = _format;
        var ring = _ring;
        if (format is null || ring is null)
            return false;

        var pcm = ring.Snapshot();
        if (format.AverageBytesPerSecond <= 0 || pcm.Length == 0)
            return false;

        duration = TimeSpan.FromSeconds(pcm.Length / (double)format.AverageBytesPerSecond);
        if (duration < InstantReplay.MinimumDump)
            return false;

        peak = PcmPeak.Max(pcm, pcm.Length, format);
        var dest = Path.GetFullPath(wavPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var writer = new WaveFileWriter(dest, format);
        writer.Write(pcm, 0, pcm.Length);
        return true;
    }

    public void Dispose()
    {
        EndTape();
        StopListen();
        _process.Dispose();
        _mix.Dispose();
    }

    private void StopListen()
    {
        _process.Stop();
        _mix.Stop();
        lock (_recGate)
        {
            _rec?.Dispose();
            _rec = null;
            _pendingRecPath = null;
        }

        _ring = null;
        _format = null;
        _listenPid = int.MinValue;
        Mode = "";
    }

    private void OnFormat(WaveFormat format)
    {
        _format = format;
        var cap = InstantReplay.CapacityBytes(format.AverageBytesPerSecond, format.BlockAlign);
        _ring = new PcmRingBuffer(cap, format.BlockAlign);
        lock (_recGate)
        {
            if (_pendingRecPath is null)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(_pendingRecPath)!);
            _rec?.Dispose();
            _rec = new WaveFileWriter(_pendingRecPath, format);
            _pendingRecPath = null;
            _takePeak = 0;
        }
    }

    private void OnSamples(byte[] buffer, int count)
    {
        _ring?.Write(buffer.AsSpan(0, count));
        lock (_recGate)
        {
            _rec?.Write(buffer, 0, count);
            if (_rec is not null && _format is not null)
            {
                var samplePeak = PcmPeak.Max(buffer, count, _format);
                if (samplePeak > _takePeak)
                    _takePeak = samplePeak;
            }
        }
    }
}
