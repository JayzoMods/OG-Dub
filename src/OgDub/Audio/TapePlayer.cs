using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OgDub.Audio;

public sealed class TapePlayer : IDisposable
{
    private IWavePlayer? _out;
    private AudioFileReader? _reader;
    private MMDevice? _device;

    public bool IsPlaying => _out?.PlaybackState == PlaybackState.Playing;

    public void ReapIfIdle()
    {
        if (_out is not null && _out.PlaybackState == PlaybackState.Stopped)
            DisposeOutputs();
    }

    public TimeSpan CurrentTime => _reader?.CurrentTime ?? TimeSpan.Zero;

    public void Seek(TimeSpan position)
    {
        if (_reader is null)
            return;
        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;
        if (position > _reader.TotalTime)
            position = _reader.TotalTime;
        _reader.CurrentTime = position;
    }

    public void Play(string wavPath, string? deviceId)
    {
        Stop();
        if (!File.Exists(wavPath))
            throw new FileNotFoundException("That cassette file is missing.", wavPath);

        _reader = new AudioFileReader(wavPath);
        try
        {
            _out = CreateOutput(deviceId);
            _out.PlaybackStopped += OnPlaybackStopped;
            _out.Init(_reader);
            _out.Play();
        }
        catch (Exception)
        {
            DisposeOutputs();
            throw;
        }
    }

    public void Stop() => DisposeOutputs();

    public void Dispose() => DisposeOutputs();

    private IWavePlayer CreateOutput(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return new WasapiOut();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _device = enumerator.GetDevice(deviceId);
            return new WasapiOut(_device, AudioClientShareMode.Shared, useEventSync: true, latency: 200);
        }
        catch (Exception)
        {
            _device?.Dispose();
            _device = null;
            return new WasapiOut();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e) => DisposeOutputs();

    private void DisposeOutputs()
    {
        var output = _out;
        _out = null;
        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            output.Dispose();
        }

        var reader = _reader;
        _reader = null;
        reader?.Dispose();
        var device = _device;
        _device = null;
        device?.Dispose();
    }
}
