using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgDub.Audio;
using OgDub.Core;
using OgDub.Services;

namespace OgDub.ViewModels;

public sealed class CassetteItem
{
    public required CassetteRecord Record { get; init; }
    public required string WavPath { get; init; }
    public required string SideBPath { get; init; }
    public ImageSource? Artwork { get; init; }
    public string Title => Record.Title;
    public string ShellColour => Record.Silent ? "#4A5560" : Record.ShellColour;
    public bool HasArtwork => Artwork is not null;
    public bool HasSideB => !string.IsNullOrWhiteSpace(SideBPath) && File.Exists(SideBPath);
    public string LoudnessLabel => Loudness1770.Lcd(Record.LoudnessLufs);
    public bool HasLoudness => LoudnessLabel.Length > 0;
    public bool HasMarkers => Record.Markers is { Count: > 0 };
    public string MarkerLabel => HasMarkers
        ? string.Join("  ", Record.Markers.Take(6).Select(m => TapeCounter.Lcd(m.Counter)))
        : "";
}

public sealed class StationItem
{
    public required Station Station { get; init; }
    public string Label => Station.IsWholeMix ? "Whole mix" : Station.Name;
}

public partial class DeckViewModel : ObservableObject
{
    private static readonly string[] Shells = ["#C45C26", "#2F6F6A", "#6B3FA0", "#C9A227", "#3D5A80"];

    private readonly CrateStore _crate;
    private readonly AppSettingsStore _settings;
    private readonly ListenSession _listen = new();
    private readonly MicSideBCapture _mic = new();
    private readonly TapePlayer _player = new();
    private readonly SmtcReader _smtc = new();
    private readonly SemaphoreSlim _listenLock = new(1, 1);
    private DateTimeOffset _recStarted;
    private string? _pendingWav;
    private Station? _pendingStation;
    private string? _pendingSideB;
    private string _pendingMode = "";
    private int _shellIndex;
    private NowPlaying? _takeNowPlaying;
    private DateTimeOffset _smtcNext;
    private int _smtcBusy;
    private int _rollBusy;
    private bool _playSideB;
    private bool _loadingRender;
    private bool _leaderBusy;
    private CancellationTokenSource? _leaderCts;
    private readonly List<TapeMarker> _pendingMarkers = [];

    public DeckViewModel(CrateStore crate, AppSettingsStore settings)
    {
        _crate = crate;
        _settings = settings;
        AlwaysOnTop = settings.Current.AlwaysOnTop;
        SplitOnSong = settings.Current.SplitOnSong;
        RecordMic = settings.Current.RecordMic;
        ReloadCrate();
        RefreshStations();
        RefreshRenderDevices();
    }

    public ObservableCollection<StationItem> Stations { get; } = [];
    public ObservableCollection<CassetteItem> Cassettes { get; } = [];
    public ObservableCollection<RenderDeviceItem> RenderDevices { get; } = [];

    [ObservableProperty] private StationItem? selectedStation;
    [ObservableProperty] private CassetteItem? selectedCassette;
    [ObservableProperty] private string lcdLine = "AIM AN APP";
    [ObservableProperty] private string lcdTime = "00:00";
    [ObservableProperty] private string lcdCounter = "000";
    [ObservableProperty] private string lcdMode = "KEEP";
    [ObservableProperty] private bool recLit;
    [ObservableProperty] private bool clipLit;
    [ObservableProperty] private bool isRecording;
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private double vu;
    [ObservableProperty] private double tapePack = 1;
    [ObservableProperty] private bool alwaysOnTop;
    [ObservableProperty] private bool splitOnSong;
    [ObservableProperty] private bool recordMic;
    [ObservableProperty] private RenderDeviceItem? selectedRenderDevice;
    [ObservableProperty] private string status = "Punch Rec. KEEP dumps the last 15 seconds. C-60 Side A is 30 minutes.";

    partial void OnAlwaysOnTopChanged(bool value)
    {
        _settings.Current.AlwaysOnTop = value;
        _settings.Save();
    }

    partial void OnSplitOnSongChanged(bool value)
    {
        _settings.Current.SplitOnSong = value;
        _settings.Save();
    }

    partial void OnRecordMicChanged(bool value)
    {
        _settings.Current.RecordMic = value;
        _settings.Save();
    }

    partial void OnSelectedRenderDeviceChanged(RenderDeviceItem? value)
    {
        if (_loadingRender)
            return;
        _settings.Current.PlaybackDeviceId = value?.Id ?? "";
        _settings.Save();
    }

    partial void OnSelectedCassetteChanged(CassetteItem? value)
    {
        _playSideB = false;
    }

    partial void OnSelectedStationChanged(StationItem? value)
    {
        var station = value?.Station;
        if (IsRecording || _leaderBusy || station is null)
            return;
        if (_listen.IsListening
            && _listen.AimedProcessId == station.ProcessId
            && _listen.AimedWholeMix == station.IsWholeMix)
            return;
        _ = EnsureListenAsync(station);
    }

    public void Tick()
    {
        if (_leaderBusy)
        {
            Vu = Math.Max(_listen.Peak, SelectedStation?.Station.Peak ?? 0);
            ClipLit = ClipLight.IsOn((float)Vu);
            RecLit = DateTimeOffset.Now.Millisecond < 500;
            return;
        }

        if (!IsRecording)
        {
            RefreshStations();
            Vu = Math.Max(_listen.Peak, SelectedStation?.Station.Peak ?? 0);
            ClipLit = ClipLight.IsOn((float)Vu);
            _player.ReapIfIdle();
            IsPlaying = _player.IsPlaying;
            if (IsPlaying)
            {
                ClipLit = false;
                LcdCounter = TapeCounter.Lcd(TapeCounter.FromElapsed(_player.CurrentTime));
                return;
            }

            LcdCounter = "000";
            var filled = _listen.RingFilled;
            if (filled > InstantReplay.Window)
                filled = InstantReplay.Window;
            LcdTime = TapeSide.LcdCountdown(filled);
            LcdMode = "KEEP";
            return;
        }

        var elapsed = DateTimeOffset.Now - _recStarted;
        var left = TapeSide.Remaining(elapsed, TapeSide.C60SideA);
        LcdTime = TapeSide.LcdCountdown(left);
        LcdCounter = TapeCounter.Lcd(TapeCounter.FromElapsed(elapsed));
        TapePack = left.TotalSeconds / TapeSide.C60SideA.TotalSeconds;
        Vu = Math.Max(_listen.Peak, _mic.Peak);
        ClipLit = ClipLight.IsOn((float)Vu);
        RecLit = DateTimeOffset.Now.Millisecond < 500;
        MaybeRefreshSmtc(_pendingStation);
        if (_takeNowPlaying is { Title.Length: > 0 } np)
            LcdLine = FormatNowPlayingLcd(np);

        if (left <= TimeSpan.Zero)
            _ = StopAsync();
    }

    public void RefreshStations()
    {
        var selectedId = SelectedStation?.Station.ProcessId;
        var selectedMix = SelectedStation?.Station.IsWholeMix ?? false;
        Stations.Clear();
        foreach (var station in WasapiStations.List())
            Stations.Add(new StationItem { Station = station });

        SelectedStation = Stations.FirstOrDefault(s =>
            selectedMix && s.Station.IsWholeMix
            || !selectedMix && s.Station.ProcessId == selectedId)
            ?? Stations.FirstOrDefault();
    }

    public void RefreshRenderDevices()
    {
        _loadingRender = true;
        try
        {
            var keep = SelectedRenderDevice?.Id ?? _settings.Current.PlaybackDeviceId ?? "";
            RenderDevices.Clear();
            RenderDevices.Add(new RenderDeviceItem { Id = "", Label = "Windows default" });
            foreach (var device in RenderOutputs.List())
                RenderDevices.Add(device);

            SelectedRenderDevice = RenderDevices.FirstOrDefault(d => d.Id == keep)
                ?? RenderDevices[0];
        }
        finally
        {
            _loadingRender = false;
        }
    }

    public void ReloadCrate()
    {
        Cassettes.Clear();
        foreach (var record in _crate.Load().OrderByDescending(c => c.RecordedAt))
        {
            Cassettes.Add(new CassetteItem
            {
                Record = record,
                WavPath = _crate.WavPath(record),
                SideBPath = _crate.SideBPath(record),
                Artwork = TryLoadArtwork(_crate.ArtworkPath(record))
            });
        }
    }

    [RelayCommand]
    private void Scan()
    {
        if (_leaderBusy || Stations.Count == 0)
            return;
        var i = SelectedStation is null ? 0 : Stations.IndexOf(SelectedStation);
        SelectedStation = Stations[(i + 1) % Stations.Count];
        LcdLine = SelectedStation.Label.ToUpperInvariant();
        RefreshRenderDevices();
    }

    [RelayCommand]
    private async Task RecAsync()
    {
        if (IsRecording || _leaderBusy)
            return;

        _player.Stop();
        IsPlaying = false;
        var station = SelectedStation?.Station;
        if (station is null)
        {
            Status = "Scan to a station first.";
            return;
        }

        _leaderBusy = true;
        _leaderCts?.Dispose();
        _leaderCts = new CancellationTokenSource();
        var token = _leaderCts.Token;
        string? wav = null;
        try
        {
            LcdLine = station.Name.ToUpperInvariant();
            Status = "Stand by. Tape rolls after 3-2-1. Stop cancels.";
            for (var beat = TapeLeader.Beats; beat >= 1; beat--)
            {
                LcdMode = TapeLeader.Lcd(beat);
                RecLit = true;
                await Task.Delay(TapeLeader.Beat, token);
            }

            token.ThrowIfCancellationRequested();

            var when = DateTimeOffset.Now;
            var stem = CassetteNaming.FileStem(station.Name, when);
            wav = UniqueWav(stem);
            _pendingWav = wav;
            _pendingStation = station;
            _pendingSideB = null;
            _pendingMarkers.Clear();
            LcdCounter = "000";
            _takeNowPlaying = null;
            _smtcNext = DateTimeOffset.MinValue;
            _recStarted = when;
            LcdMode = RecordMic ? "REC B" : "REC";
            Status = RecStatus();

            await ArmListenAndTapeAsync(station, wav);
            if (token.IsCancellationRequested)
            {
                await EndTapeAsync();
                TryDelete(wav);
                TryDelete(_pendingSideB);
                _pendingWav = null;
                _pendingSideB = null;
                _pendingMarkers.Clear();
                LcdMode = "KEEP";
                RecLit = false;
                Status = "Leader cancelled. Tape did not roll.";
                return;
            }

            if (RecordMic)
                StartMicSideB(when);
            LcdMode = _pendingSideB is not null ? "REC B" : "REC";
            if (RecordMic && _pendingSideB is null)
                Status = RecStatus() + " Mic did not arm. Side A only.";
            IsRecording = true;
            RecLit = true;
            TapePack = 1;
        }
        catch (OperationCanceledException)
        {
            TryDelete(wav);
            TryDelete(_pendingSideB);
            _pendingWav = null;
            _pendingSideB = null;
            _pendingMarkers.Clear();
            LcdMode = "KEEP";
            RecLit = false;
            Status = "Leader cancelled. Tape did not roll.";
        }
        catch (Exception ex)
        {
            TryDelete(wav);
            TryDelete(_pendingSideB);
            _pendingWav = null;
            _pendingSideB = null;
            _pendingMarkers.Clear();
            LcdMode = "ERR";
            LcdLine = "NO LOCK";
            Status = ex.Message;
        }
        finally
        {
            _leaderBusy = false;
            _leaderCts?.Dispose();
            _leaderCts = null;
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (_leaderBusy)
        {
            _leaderCts?.Cancel();
            return;
        }

        if (!IsRecording && !IsPlaying)
            return;

        if (IsPlaying)
        {
            _player.Stop();
            IsPlaying = false;
            _playSideB = false;
            LcdMode = "KEEP";
            return;
        }

        var wav = _pendingWav;
        var station = _pendingStation;
        var when = _recStarted;
        var nowPlaying = _takeNowPlaying;
        var mode = _pendingMode;
        var takePeak = _listen.TakePeak;
        var sideB = _pendingSideB;
        _pendingSideB = null;
        await EndTapeAsync();
        IsRecording = false;
        RecLit = false;
        TapePack = 1;
        _pendingWav = null;
        _pendingStation = null;
        _takeNowPlaying = null;
        if (wav is not null && station is not null)
            EjectCassette(wav, station, when, nowPlaying, takePeak < 0.0005f, TimeSpan.Zero, mode, sideB, TakePendingMarkers(), updateLcd: true);
    }

    [RelayCommand]
    private async Task KeepAsync()
    {
        var station = SelectedStation?.Station;
        if (station is null)
        {
            Status = "Scan to a station first.";
            return;
        }

        try
        {
            await EnsureListenAsync(station);
        }
        catch (Exception ex)
        {
            LcdMode = "ERR";
            LcdLine = "NO LOCK";
            Status = ex.Message;
            return;
        }

        var when = DateTimeOffset.Now;
        var wav = UniqueWav(CassetteNaming.FileStem("Replay " + station.Name, when));
        var dest = Path.GetFullPath(wav);
        if (!LibraryPaths.IsUnderLibrary(dest, _crate.LibraryRoot))
        {
            Status = "Replay path was refused.";
            return;
        }

        if (!_listen.TrySaveReplay(wav, out var duration, out var peak))
        {
            TryDelete(wav);
            LcdMode = "KEEP";
            Status = "Replay buffer is still filling. Leave the station playing.";
            return;
        }

        NowPlaying? nowPlaying = null;
        try
        {
            nowPlaying = await _smtc.ReadAsync(station);
        }
        catch (Exception)
        {
            nowPlaying = null;
        }

        EjectCassette(wav, station, when, nowPlaying, peak < 0.0005f, duration, "REPLAY", null, null, updateLcd: !IsRecording);
        if (IsRecording)
            Status = "Kept the last 15 seconds. Still recording.";
    }

    [RelayCommand]
    private void Play()
    {
        if (IsRecording || _leaderBusy || SelectedCassette is null)
            return;
        try
        {
            _playSideB = false;
            _player.Play(SelectedCassette.WavPath, SelectedRenderDevice?.Id);
            IsPlaying = true;
            LcdLine = SelectedCassette.Title.ToUpperInvariant();
            LcdMode = SelectedCassette.HasSideB ? "PLAY A" : "PLAY";
            Status = SelectedCassette.HasSideB
                ? "Playing Side A. Flip for Side B (mic)."
                : "Playing the crate. Stop to halt.";
            if (SelectedCassette.HasMarkers)
                Status += " Cue jumps marks.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            LcdMode = "ERR";
        }
    }

    [RelayCommand]
    private void Flip()
    {
        if (IsRecording || _leaderBusy || SelectedCassette is null)
            return;
        if (!SelectedCassette.HasSideB)
        {
            Status = "That cassette has no Side B. Tick Mic before Rec.";
            return;
        }

        try
        {
            _playSideB = !_playSideB;
            var path = _playSideB ? SelectedCassette.SideBPath : SelectedCassette.WavPath;
            _player.Play(path, SelectedRenderDevice?.Id);
            IsPlaying = true;
            LcdLine = SelectedCassette.Title.ToUpperInvariant();
            LcdMode = _playSideB ? "PLAY B" : "PLAY A";
            Status = _playSideB
                ? "Playing Side B (mic, −12 dB)."
                : "Playing Side A.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            LcdMode = "ERR";
        }
    }

    [RelayCommand]
    private void Mark()
    {
        if (!IsRecording)
        {
            Status = "Punch Rec first, then Mark.";
            return;
        }

        var elapsed = DateTimeOffset.Now - _recStarted;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        if (_pendingMarkers.Count >= TapeCounter.MaxMarks)
        {
            Status = "That tape already has " + TapeCounter.MaxMarks + " marks.";
            return;
        }

        if (_pendingMarkers.Count > 0
            && elapsed.TotalSeconds - _pendingMarkers[^1].OffsetSeconds < 0.25)
        {
            Status = "Already marked " + _pendingMarkers[^1].Label + ".";
            return;
        }

        var counter = TapeCounter.FromElapsed(elapsed);
        var mark = new TapeMarker
        {
            Counter = counter,
            OffsetSeconds = elapsed.TotalSeconds,
            Label = TapeCounter.Lcd(counter)
        };
        _pendingMarkers.Add(mark);
        LcdCounter = mark.Label;
        LcdMode = "MARK";
        Status = "Marked " + mark.Label + ".";
    }

    [RelayCommand]
    private void Cue()
    {
        if (IsRecording)
        {
            Status = "Cue is for playback.";
            return;
        }

        if (!IsPlaying || SelectedCassette is null)
        {
            Status = "Play a cassette, then Cue.";
            return;
        }

        var next = TapeMarkers.Next(SelectedCassette.Record.Markers ?? [], _player.CurrentTime.TotalSeconds);
        if (next is null)
        {
            Status = "That cassette has no marks. Punch Mark while Rec is on.";
            return;
        }

        _player.Seek(TimeSpan.FromSeconds(Math.Max(0, next.OffsetSeconds)));
        LcdCounter = TapeCounter.Lcd(TapeCounter.FromElapsed(TimeSpan.FromSeconds(next.OffsetSeconds)));
        LcdMode = "CUE";
        Status = "Cued to " + (string.IsNullOrWhiteSpace(next.Label) ? TapeCounter.Lcd(next.Counter) : next.Label) + ".";
    }

    [RelayCommand]
    private void Dub()
    {
        if (IsRecording || _leaderBusy)
            return;
        if (Cassettes.Count < 2)
        {
            Status = "Need two cassettes in the crate to dub.";
            return;
        }

        var ordered = Cassettes.Take(2).Reverse().ToList();
        var when = DateTimeOffset.Now;
        var dest = UniqueWav(CassetteNaming.FileStem("Mixtape", when));
        try
        {
            WavConcat.Dub(ordered.Select(c => c.WavPath).ToList(), dest, TimeSpan.FromSeconds(2));
            var id = Guid.NewGuid().ToString("N");
            var measured = MeasureCassette(dest, when);
            var title = CassetteNaming.JCard("Mixtape", when, measured.Duration);
            TagCassette(dest, title, id, when, measured.Loud);
            var record = new CassetteRecord
            {
                Id = id,
                Title = title,
                AppName = "Mixtape",
                WavFileName = Path.GetFileName(dest),
                RecordedAt = when,
                DurationSeconds = measured.Duration.TotalSeconds,
                ShellColour = NextShell(),
                Silent = false,
                CaptureMode = "DUB",
                LoudnessLufs = measured.Loud.LoudnessLufs,
                PeakDbfs = measured.Loud.PeakDbfs
            };
            var all = _crate.Load().ToList();
            all.Add(record);
            _crate.Save(all);
            ReloadCrate();
            LcdLine = "MIXTAPE";
            LcdMode = "DUB";
            Status = WithLoudness("Dubbed the two newest cassettes with 2 s gaps.", record.LoudnessLufs);
        }
        catch (Exception ex)
        {
            TryDelete(dest);
            Status = ex.Message;
            LcdMode = "ERR";
        }
    }

    [RelayCommand]
    private void Terms()
    {
        var window = Application.Current.Windows.OfType<Window>().FirstOrDefault();
        if (window is not null)
            AppHost.Terms.Show(window);
    }

    public void Shutdown()
    {
        _leaderCts?.Cancel();
        _listen.Dispose();
        _mic.Dispose();
        _player.Dispose();
        _listenLock.Dispose();
        _leaderCts?.Dispose();
    }

    private async Task EnsureListenAsync(Station station)
    {
        try
        {
            await _listenLock.WaitAsync();
            try
            {
                if (IsRecording)
                    return;
                await Task.Run(() => _listen.Ensure(station));
            }
            finally
            {
                _listenLock.Release();
            }
        }
        catch (Exception)
        {
            // Rec / KEEP retry and show NO LOCK if the station still will not arm.
        }
    }

    private async Task ArmListenAndTapeAsync(Station station, string wav)
    {
        await _listenLock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                _listen.Ensure(station);
                _listen.BeginTape(wav);
            });
            _pendingMode = _listen.Mode;
        }
        finally
        {
            _listenLock.Release();
        }
    }

    private async Task EndTapeAsync()
    {
        await _listenLock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                _listen.EndTape();
                _mic.Stop();
            });
        }
        finally
        {
            _listenLock.Release();
        }
    }

    private async Task RollNewSongAsync(NowPlaying next)
    {
        var wav = _pendingWav;
        var station = _pendingStation;
        var when = _recStarted;
        var prev = _takeNowPlaying;
        var mode = _pendingMode;
        var takePeak = _listen.TakePeak;
        var sideB = _pendingSideB;
        if (wav is null || station is null)
            return;

        _pendingSideB = null;
        await EndTapeAsync();
        _pendingWav = null;
        EjectCassette(wav, station, when, prev, takePeak < 0.0005f, TimeSpan.Zero, mode, sideB, TakePendingMarkers(), updateLcd: false);

        var newWhen = DateTimeOffset.Now;
        var newWav = UniqueWav(CassetteNaming.FileStem(station.Name, newWhen));
        _pendingWav = newWav;
        _pendingStation = station;
        _recStarted = newWhen;
        LcdCounter = "000";
        _takeNowPlaying = next;
        TapePack = 1;
        LcdTime = TapeSide.LcdCountdown(TapeSide.C60SideA);
        LcdMode = RecordMic ? "REC B" : "REC";
        LcdLine = FormatNowPlayingLcd(next);
        Status = "New song, new tape.";
        try
        {
            await ArmListenAndTapeAsync(station, newWav);
            if (RecordMic)
                StartMicSideB(newWhen);
        }
        catch (Exception ex)
        {
            TryDelete(newWav);
            TryDelete(_pendingSideB);
            _pendingWav = null;
            _pendingSideB = null;
            IsRecording = false;
            RecLit = false;
            LcdMode = "ERR";
            LcdLine = "NO LOCK";
            Status = ex.Message;
        }
    }

    private void EjectCassette(
        string wav,
        Station station,
        DateTimeOffset when,
        NowPlaying? nowPlaying,
        bool silent,
        TimeSpan duration,
        string captureMode,
        string? sideBWav,
        IReadOnlyList<TapeMarker>? markers,
        bool updateLcd)
    {
        if (!File.Exists(wav))
            return;

        var measured = MeasureCassette(wav, when);
        if (measured.Duration > TimeSpan.Zero)
            duration = measured.Duration;

        var id = Guid.NewGuid().ToString("N");
        var title = "Nothing playing";
        var trackTitle = "";
        var artist = "";
        var album = "";
        var artworkName = "";
        if (!silent)
        {
            if (nowPlaying is { Title.Length: > 0 })
            {
                title = CassetteNaming.JCardFromTrack(nowPlaying.Title, nowPlaying.Artist, when, duration);
                trackTitle = nowPlaying.Title;
                artist = nowPlaying.Artist;
                album = nowPlaying.Album;
                artworkName = WriteArtwork(id, nowPlaying) ?? "";
            }
            else
            {
                title = captureMode == "REPLAY"
                    ? CassetteNaming.JCard("Replay " + station.Name, when, duration)
                    : CassetteNaming.JCard(station.Name, when, duration);
            }
        }

        var record = new CassetteRecord
        {
            Id = id,
            Title = title,
            AppName = station.Name,
            WavFileName = Path.GetFileName(wav),
            RecordedAt = when,
            DurationSeconds = duration.TotalSeconds,
            ShellColour = NextShell(),
            Silent = silent,
            CaptureMode = captureMode,
            TrackTitle = trackTitle,
            Artist = artist,
            Album = album,
            ArtworkFileName = artworkName,
            SideBWavFileName = SideBFileName(sideBWav),
            LoudnessLufs = measured.Loud.LoudnessLufs,
            PeakDbfs = measured.Loud.PeakDbfs,
            Markers = CopyMarkers(markers)
        };

        TagCassette(wav, title, id, when, measured.Loud, record.Markers, measured.SampleRate);
        if (!string.IsNullOrWhiteSpace(sideBWav) && File.Exists(sideBWav))
        {
            var sideB = MeasureCassette(sideBWav, when);
            TagCassette(sideBWav, "Side B", id, when, sideB.Loud);
        }

        var all = _crate.Load().ToList();
        all.Add(record);
        _crate.Save(all);
        ReloadCrate();
        SelectedCassette = Cassettes.FirstOrDefault(c => c.Record.Id == record.Id);

        if (!updateLcd)
            return;

        if (silent)
        {
            if (nowPlaying is { Title.Length: > 0 })
            {
                LcdLine = "PROTECTED";
                LcdMode = "SILENT";
                Status = "Windows named a track but the tape is silence. Protected audio does that. The cassette is still in the crate.";
            }
            else
            {
                LcdLine = "NOTHING PLAYING";
                LcdMode = "SILENT";
                Status = "That take was silence. Protected audio does that. The cassette is still in the crate.";
            }

            if (!string.IsNullOrWhiteSpace(record.SideBWavFileName))
                Status += " Flip plays Side B (mic).";
            Status = WithLoudness(Status, record.LoudnessLufs);
            Status = WithMarks(Status, record.Markers.Count);
        }
        else
        {
            LcdLine = record.Title.ToUpperInvariant();
            LcdMode = captureMode == "REPLAY" ? "KEEP" : "EJECT";
            Status = captureMode == "REPLAY"
                ? "Kept the last 15 seconds in the crate."
                : string.IsNullOrWhiteSpace(record.SideBWavFileName)
                    ? "Cassette in the crate. Play it, dub it, or drag the file out of the folder."
                    : "Cassette in the crate. Flip plays Side B (mic, −12 dB).";
            Status = WithLoudness(Status, record.LoudnessLufs);
            Status = WithMarks(Status, record.Markers.Count);
        }
    }

    private void StartMicSideB(DateTimeOffset when)
    {
        var wav = UniqueWav(CassetteNaming.FileStem("Mic", when));
        var dest = Path.GetFullPath(wav);
        if (!LibraryPaths.IsUnderLibrary(dest, _crate.LibraryRoot))
            return;

        try
        {
            _mic.Start(wav);
            _pendingSideB = wav;
        }
        catch (Exception)
        {
            TryDelete(wav);
            _pendingSideB = null;
        }
    }

    private string RecStatus()
    {
        if (SplitOnSong && RecordMic)
            return "Recording Side A + mic Side B (−12 dB). A new song starts a new tape.";
        if (RecordMic)
            return "Recording Side A + mic Side B (−12 dB). Stop or wait for the side to run out.";
        if (SplitOnSong)
            return "Recording. A new song starts a new tape. Stop or wait for the side to run out.";
        return "Recording. Stop or wait for the side to run out.";
    }

    private static string SideBFileName(string? sideBWav)
    {
        if (string.IsNullOrWhiteSpace(sideBWav) || !File.Exists(sideBWav))
            return "";
        return Path.GetFileName(sideBWav);
    }

    private (TimeSpan Duration, LoudnessResult Loud, int SampleRate) MeasureCassette(string wav, DateTimeOffset started)
    {
        try
        {
            using var stream = File.OpenRead(wav);
            var pcm = WavPcm.Read(stream);
            var duration = pcm.AverageBytesPerSecond > 0
                ? TimeSpan.FromSeconds(pcm.Data.Length / (double)pcm.AverageBytesPerSecond)
                : DateTimeOffset.Now - started;
            return (duration, Loudness1770.Measure(pcm), pcm.SampleRate);
        }
        catch (Exception)
        {
            return (DateTimeOffset.Now - started, default, 0);
        }
    }

    private void TagCassette(
        string wav,
        string description,
        string id,
        DateTimeOffset when,
        LoudnessResult loud,
        IReadOnlyList<TapeMarker>? markers = null,
        int sampleRate = 0)
    {
        var dest = Path.GetFullPath(wav);
        if (!LibraryPaths.IsUnderLibrary(dest, _crate.LibraryRoot))
            return;

        var reference = "OGDub" + id;
        if (reference.Length > 32)
            reference = reference[..32];

        BwfFile.TryAppend(dest, new BextFields
        {
            Description = CassetteNaming.Collapse(description),
            OriginatorReference = reference,
            OriginatedAt = when,
            LoudnessLufs = loud.LoudnessLufs,
            PeakDbfs = loud.PeakDbfs
        });
        BwfFile.TryAppendCues(dest, markers ?? [], sampleRate);
    }

    private List<TapeMarker> TakePendingMarkers()
    {
        var copy = CopyMarkers(_pendingMarkers);
        _pendingMarkers.Clear();
        return copy;
    }

    private static List<TapeMarker> CopyMarkers(IReadOnlyList<TapeMarker>? markers)
    {
        if (markers is null || markers.Count == 0)
            return [];

        var copy = new List<TapeMarker>(Math.Min(markers.Count, TapeCounter.MaxMarks));
        foreach (var marker in markers)
        {
            if (copy.Count >= TapeCounter.MaxMarks)
                break;
            var counter = TapeCounter.Clamp(marker.Counter);
            copy.Add(new TapeMarker
            {
                Counter = counter,
                OffsetSeconds = Math.Max(0, marker.OffsetSeconds),
                Label = string.IsNullOrWhiteSpace(marker.Label) ? TapeCounter.Lcd(counter) : CassetteNaming.Collapse(marker.Label)
            });
        }

        return copy;
    }

    private static string WithLoudness(string status, double? lufs)
    {
        var lcd = Loudness1770.Lcd(lufs);
        if (lcd.Length == 0)
            return status;
        return status + " " + lcd + ".";
    }

    private static string WithMarks(string status, int count)
    {
        if (count <= 0)
            return status;
        return status + " " + count + (count == 1 ? " mark." : " marks.");
    }

    private string UniqueWav(string stem)
    {
        var dest = Path.Combine(_crate.LibraryRoot, stem + ".wav");
        var n = 2;
        while (File.Exists(dest))
        {
            dest = Path.Combine(_crate.LibraryRoot, stem + " " + n + ".wav");
            n++;
        }

        return dest;
    }

    private string NextShell()
    {
        var colour = Shells[_shellIndex % Shells.Length];
        _shellIndex++;
        return colour;
    }

    private void MaybeRefreshSmtc(Station? station)
    {
        if (station is null || DateTimeOffset.Now < _smtcNext)
            return;

        _smtcNext = DateTimeOffset.Now.AddSeconds(1);
        if (Interlocked.CompareExchange(ref _smtcBusy, 1, 0) != 0)
            return;

        _ = RefreshSmtcAsync(station);
    }

    private async Task RefreshSmtcAsync(Station station)
    {
        try
        {
            var snap = await _smtc.ReadAsync(station);
            if (snap is null)
                return;

            if (IsRecording
                && SplitOnSong
                && _takeNowPlaying is not null
                && SmtcSongChange.IsNewSong(_takeNowPlaying.Title, snap.Title)
                && DateTimeOffset.Now - _recStarted >= InstantReplay.MinimumSongSplit
                && Interlocked.CompareExchange(ref _rollBusy, 1, 0) == 0)
            {
                try
                {
                    await RollNewSongAsync(snap);
                }
                finally
                {
                    Interlocked.Exchange(ref _rollBusy, 0);
                }

                return;
            }

            _takeNowPlaying = snap;
        }
        finally
        {
            Interlocked.Exchange(ref _smtcBusy, 0);
        }
    }

    private string? WriteArtwork(string cassetteId, NowPlaying nowPlaying)
    {
        if (nowPlaying.Artwork is not { Length: > 0 })
            return null;

        var name = cassetteId + ".art" + ArtworkExtension(nowPlaying.ArtworkContentType);
        var dest = Path.GetFullPath(Path.Combine(_crate.LibraryRoot, name));
        if (!LibraryPaths.IsUnderLibrary(dest, _crate.LibraryRoot))
            return null;

        try
        {
            File.WriteAllBytes(dest, nowPlaying.Artwork);
            return name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string FormatNowPlayingLcd(NowPlaying nowPlaying)
    {
        var line = nowPlaying.Artist.Length == 0
            ? nowPlaying.Title
            : nowPlaying.Title + " · " + nowPlaying.Artist;
        return line.ToUpperInvariant();
    }

    private static string ArtworkExtension(string contentType)
    {
        if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            return ".png";
        if (contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase))
            return ".gif";
        if (contentType.StartsWith("image/bmp", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("image/x-bmp", StringComparison.OrdinalIgnoreCase))
            return ".bmp";
        return ".jpg";
    }

    private static ImageSource? TryLoadArtwork(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = stream;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
