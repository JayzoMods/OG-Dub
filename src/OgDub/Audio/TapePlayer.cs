using System.IO;
using NAudio.Wave;

namespace OgDub.Audio;

public sealed class TapePlayer : IDisposable
{
    private WaveOutEvent? _out;
    private AudioFileReader? _reader;

    public bool IsPlaying => _out?.PlaybackState == PlaybackState.Playing;

    public void Play(string wavPath)
    {
        Stop();
        if (!File.Exists(wavPath))
            throw new FileNotFoundException("That cassette file is missing.", wavPath);

        _reader = new AudioFileReader(wavPath);
        _out = new WaveOutEvent();
        _out.PlaybackStopped += (_, _) => DisposeOutputs();
        _out.Init(_reader);
        _out.Play();
    }

    public void Stop() => DisposeOutputs();

    public void Dispose() => DisposeOutputs();

    private void DisposeOutputs()
    {
        var output = _out;
        _out = null;
        output?.Dispose();
        var reader = _reader;
        _reader = null;
        reader?.Dispose();
    }
}
