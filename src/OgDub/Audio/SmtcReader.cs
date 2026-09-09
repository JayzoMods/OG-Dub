using System.Diagnostics;
using System.Runtime.InteropServices;
using OgDub.Core;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace OgDub.Audio;

public sealed class NowPlaying
{
    public required string Title { get; init; }
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public byte[]? Artwork { get; init; }
    public string ArtworkContentType { get; init; } = "";
}

public sealed class SmtcReader
{
    // Read-only: never pause, skip, or otherwise control another app's SMTC session.

    private const uint ArtworkCapBytes = 1_048_576;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private DateTimeOffset _retryAfter;

    public async Task<NowPlaying?> ReadAsync(Station? station)
    {
        if (station is null || DateTimeOffset.Now < _retryAfter)
            return null;

        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = PickSession(_manager, station);
            if (session is null)
                return null;

            var props = await session.TryGetMediaPropertiesAsync();
            if (props is null)
                return null;

            var title = CassetteNaming.Collapse(props.Title);
            if (title.Length == 0)
                return null;

            byte[]? artwork = null;
            var contentType = "";
            if (props.Thumbnail is not null)
            {
                try
                {
                    using var ras = await props.Thumbnail.OpenReadAsync();
                    contentType = ras.ContentType ?? "";
                    artwork = await ReadThumbAsync(ras);
                }
                catch (Exception)
                {
                    artwork = null;
                    contentType = "";
                }
            }

            return new NowPlaying
            {
                Title = title,
                Artist = CassetteNaming.Collapse(props.Artist),
                Album = CassetteNaming.Collapse(props.AlbumTitle),
                Artwork = artwork,
                ArtworkContentType = contentType
            };
        }
        catch (COMException)
        {
            _manager = null;
            _retryAfter = DateTimeOffset.Now.AddSeconds(10);
            return null;
        }
        catch (Exception)
        {
            _retryAfter = DateTimeOffset.Now.AddSeconds(10);
            return null;
        }
    }

    private static GlobalSystemMediaTransportControlsSession? PickSession(
        GlobalSystemMediaTransportControlsSessionManager manager,
        Station station)
    {
        if (station.IsWholeMix)
            return manager.GetCurrentSession();

        var processName = ProcessNameOf(station);
        GlobalSystemMediaTransportControlsSession? playing = null;
        GlobalSystemMediaTransportControlsSession? any = null;
        foreach (var session in manager.GetSessions())
        {
            if (!SmtcSessionMatch.Matches(session.SourceAppUserModelId, processName))
                continue;

            any ??= session;
            try
            {
                if (session.GetPlaybackInfo().PlaybackStatus
                    == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    playing = session;
                    break;
                }
            }
            catch (Exception)
            {
            }
        }

        return playing ?? any;
    }

    private static string ProcessNameOf(Station station)
    {
        try
        {
            using var proc = Process.GetProcessById(station.ProcessId);
            return proc.ProcessName;
        }
        catch (Exception)
        {
            return station.Name;
        }
    }

    private static async Task<byte[]?> ReadThumbAsync(IRandomAccessStream stream)
    {
        var size = stream.Size;
        if (size == 0 || size > ArtworkCapBytes)
            return null;

        stream.Seek(0);
        using var input = stream.GetInputStreamAt(0);
        var reader = new DataReader(input);
        try
        {
            await reader.LoadAsync((uint)size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        finally
        {
            reader.Dispose();
        }
    }
}
