using NAudio.Wave;

namespace OgDub.Audio;

internal sealed class MixLoopbackCapture : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly ManualResetEventSlim _stopped = new(true);
    private Action<WaveFormat>? _onFormat;
    private Action<byte[], int>? _onSamples;

    public bool IsRunning => _capture is not null;
    public float Peak { get; private set; }

    public void Start(Action<WaveFormat> onFormat, Action<byte[], int> onSamples)
    {
        Stop();
        Peak = 0;
        _onFormat = onFormat;
        _onSamples = onSamples;
        _stopped.Reset();
        _capture = new WasapiLoopbackCapture();
        _onFormat.Invoke(_capture.WaveFormat);
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnStopped;
        _capture.StartRecording();
    }

    public void Stop()
    {
        _capture?.StopRecording();
        _stopped.Wait(TimeSpan.FromSeconds(3));
        _onFormat = null;
        _onSamples = null;
    }

    public void Dispose()
    {
        Stop();
        _stopped.Dispose();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var capture = _capture;
        if (capture is null || e.BytesRecorded <= 0)
            return;

        Peak = Math.Max(Peak, PcmPeak.Max(e.Buffer, e.BytesRecorded, capture.WaveFormat));
        _onSamples?.Invoke(e.Buffer, e.BytesRecorded);
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        _capture?.Dispose();
        _capture = null;
        _stopped.Set();
    }
}
