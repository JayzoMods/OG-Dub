using System.IO;
using NAudio.Wave;

namespace OgDub.Audio;

internal sealed class MixLoopbackCapture : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private WaveFileWriter? _writer;
    private readonly ManualResetEventSlim _stopped = new(true);

    public bool IsRunning => _capture is not null;
    public float Peak { get; private set; }

    public void Start(string wavPath)
    {
        Stop();
        Peak = 0;
        Directory.CreateDirectory(Path.GetDirectoryName(wavPath)!);
        _stopped.Reset();
        _capture = new WasapiLoopbackCapture();
        _writer = new WaveFileWriter(wavPath, _capture.WaveFormat);
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
        var writer = _writer;
        var capture = _capture;
        if (writer is null || capture is null || e.BytesRecorded <= 0)
            return;

        writer.Write(e.Buffer, 0, e.BytesRecorded);
        var format = capture.WaveFormat;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (var i = 0; i + 4 <= e.BytesRecorded; i += 4)
            {
                var sample = Math.Abs(BitConverter.ToSingle(e.Buffer, i));
                if (sample > Peak)
                    Peak = sample;
            }
        }
        else if (format.BitsPerSample == 16)
        {
            for (var i = 0; i + 2 <= e.BytesRecorded; i += 2)
            {
                var sample = Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f);
                if (sample > Peak)
                    Peak = sample;
            }
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        _writer?.Dispose();
        _writer = null;
        _capture?.Dispose();
        _capture = null;
        _stopped.Set();
    }
}
