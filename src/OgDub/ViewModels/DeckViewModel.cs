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
    public bool IsFavorite { get; init; }
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
    public bool CanFavorite => !Record.Silent;
    public string FavoriteGlyph => IsFavorite ? "\u2605" : "\u2606";
}

public sealed class StationItem
{
    public required Station Station { get; init; }
    public string Label => Station.IsWholeMix ? "Whole mix" : Station.Name;
}

public partial class DeckViewModel : ObservableObject
{
    private static readonly string[] Shells = ["#C45C26", "#2F6F6A", "#6B3FA0", "#C9A227", "#3D5A80"];

    private CrateStore _crate;
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
    private bool _loadingMic;
    private bool _shuttingDown;
    private CancellationTokenSource? _leaderCts;
    private readonly List<TapeMarker> _pendingMarkers = [];

    public DeckViewModel(CrateStore crate, AppSettingsStore settings)
    {
        _crate = crate;
        _settings = settings;
        AlwaysOnTop = settings.Current.AlwaysOnTop;
        SplitOnSong = settings.Current.SplitOnSong;
        RecordMic = settings.Current.RecordMic;
        MicOn = settings.Current.MicOn;
        GlobalHotkeys = settings.Current.GlobalHotkeys ?? true;
        ReloadCrate();
        RefreshStations();
        RefreshRenderDevices();
        RefreshMicDevices();
        SyncMicMonitor();
        Edit = new EditViewModel(this, settings);
    }

    public EditViewModel Edit { get; }

    public ObservableCollection<StationItem> Stations { get; } = [];
    public ObservableCollection<CassetteItem> Cassettes { get; } = [];
    public ObservableCollection<RenderDeviceItem> RenderDevices { get; } = [];
    public ObservableCollection<MicDeviceItem> MicDevices { get; } = [];

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
    [ObservableProperty] private double micVu;
    [ObservableProperty] private double tapePack = 1;
    [ObservableProperty] private bool alwaysOnTop;
    [ObservableProperty] private bool splitOnSong;
    [ObservableProperty] private bool recordMic;
    [ObservableProperty] private bool micOn = true;
    [ObservableProperty] private bool globalHotkeys;
    [ObservableProperty] private bool leaderBusy;
    [ObservableProperty] private bool stationsMenuOpen;
    [ObservableProperty] private RenderDeviceItem? selectedRenderDevice;
    [ObservableProperty] private MicDeviceItem? selectedMicDevice;
    [ObservableProperty] private bool showEditTab;
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
        SyncMicMonitor();
        if (value)
        {
            Status = MicOn
                ? "Live voice armed. MIC ON — you should hear yourself in Play-to (use headphones). After Stop, Flip plays the voice take."
                : "Live voice armed. Punch MIC ON to hear yourself in Play-to. After Stop, Flip plays the voice take.";
        }
    }

    partial void OnMicOnChanged(bool value)
    {
        _settings.Current.MicOn = value;
        _settings.Save();
        SyncMicMonitor();
        if (RecordMic)
        {
            Status = value
                ? "MIC ON — live voice in Play-to. Use headphones to avoid feedback."
                : "MIC OFF — monitor muted. Rec still lays the voice take while Live voice is ticked.";
        }
    }

    partial void OnGlobalHotkeysChanged(bool value)
    {
        _settings.Current.GlobalHotkeys = value;
        _settings.Save();
    }

    partial void OnShowEditTabChanged(bool value)
    {
        _settings.Current.LastTab = value ? "edit" : "record";
        _settings.Save();
        if (value)
            _ = Edit.OnOpenedAsync();
    }

    [RelayCommand]
    private void ShowRecord() => ShowEditTab = false;

    [RelayCommand]
    private void ShowEdit() => ShowEditTab = true;

    partial void OnIsRecordingChanged(bool value) => NotifyTransport();

    partial void OnIsPlayingChanged(bool value) => NotifyTransport();

    partial void OnLeaderBusyChanged(bool value) => NotifyTransport();

    partial void OnSelectedRenderDeviceChanged(RenderDeviceItem? value)
    {
        if (_loadingRender)
            return;
        _settings.Current.PlaybackDeviceId = value?.Id ?? "";
        _settings.Save();
        SyncMicMonitor();
    }

    partial void OnSelectedMicDeviceChanged(MicDeviceItem? value)
    {
        if (_loadingMic)
            return;
        _settings.Current.MicDeviceId = value?.Id ?? "";
        _settings.Save();
        if (!_mic.IsTaping)
            SyncMicMonitor();
    }

    partial void OnSelectedCassetteChanged(CassetteItem? value)
    {
        _playSideB = false;
        NotifyTransport();
    }

    partial void OnSelectedStationChanged(StationItem? value)
    {
        NotifyTransport();
        var station = value?.Station;
        if (IsRecording || LeaderBusy || station is null)
            return;
        if (_listen.IsListening
            && _listen.AimedProcessId == station.ProcessId
            && _listen.AimedWholeMix == station.IsWholeMix)
            return;
        _ = EnsureListenAsync(station);
    }

    public void Tick()
    {
        if (LeaderBusy)
        {
            Vu = Math.Max(_listen.Peak, SelectedStation?.Station.Peak ?? 0);
            MicVu = _mic.Peak;
            ClipLit = ClipLight.IsOn((float)Vu);
            RecLit = DateTimeOffset.Now.Millisecond < 500;
            return;
        }

        if (!IsRecording)
        {
            RefreshStations();
            Vu = Math.Max(_listen.Peak, SelectedStation?.Station.Peak ?? 0);
            MicVu = _mic.Peak;
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
        MicVu = _mic.Peak;
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
        if (StationsMenuOpen)
            return;

        var next = WasapiStations.List().Select(s => new StationItem { Station = s }).ToList();
        if (StationsLookSame(next))
            return;

        var selectedId = SelectedStation?.Station.ProcessId;
        var selectedMix = SelectedStation?.Station.IsWholeMix ?? false;
        Stations.Clear();
        foreach (var station in next)
            Stations.Add(station);

        SelectedStation = Stations.FirstOrDefault(s =>
            selectedMix && s.Station.IsWholeMix
            || !selectedMix && s.Station.ProcessId == selectedId)
            ?? Stations.FirstOrDefault();
        NotifyTransport();
    }

    public void ReplaceCrate(CrateStore crate)
    {
        _crate = crate;
        ReloadCrate();
        Status = "Crate folder is now " + crate.LibraryRoot;
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

    public void RefreshMicDevices()
    {
        _loadingMic = true;
        try
        {
            var keep = SelectedMicDevice?.Id ?? _settings.Current.MicDeviceId ?? "";
            MicDevices.Clear();
            MicDevices.Add(new MicDeviceItem { Id = "", Label = "Windows default mic" });
            foreach (var device in MicInputs.List())
                MicDevices.Add(device);

            SelectedMicDevice = MicDevices.FirstOrDefault(d => d.Id == keep)
                ?? MicDevices[0];
        }
        finally
        {
            _loadingMic = false;
        }
    }

    public void SyncMicMonitor()
    {
        if (_shuttingDown)
            return;
        try
        {
            if (!_mic.IsTaping)
                _mic.StartMonitor(SelectedMicDevice?.Id);
            _mic.SetHearThrough(RecordMic && MicOn, SelectedRenderDevice?.Id);
        }
        catch (Exception)
        {
            Status = "Could not open that microphone. Pick another mic or check Windows privacy settings.";
            MicVu = 0;
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
                Artwork = TryLoadArtwork(_crate.ArtworkPath(record)),
                IsFavorite = _crate.IsFavorite(record)
            });
        }

        NotifyTransport();
    }

    private bool CanScan() => !LeaderBusy && Stations.Count > 0;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private void Scan()
    {
        if (LeaderBusy || Stations.Count == 0)
            return;
        var i = SelectedStation is null ? 0 : Stations.IndexOf(SelectedStation);
        SelectedStation = Stations[(i + 1) % Stations.Count];
        LcdLine = SelectedStation.Label.ToUpperInvariant();
        RefreshRenderDevices();
    }

    private bool CanRec() => !IsRecording && !LeaderBusy && SelectedStation is not null;

    [RelayCommand(CanExecute = nameof(CanRec))]
    private async Task RecAsync()
    {
        if (IsRecording || LeaderBusy || _shuttingDown)
            return;

        _player.Stop();
        IsPlaying = false;
        var station = SelectedStation?.Station;
        if (station is null)
        {
            Status = "Scan to a station first.";
            return;
        }

        LeaderBusy = true;
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
            if (_shuttingDown)
                throw new OperationCanceledException();

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
            LeaderBusy = false;
            _leaderCts?.Dispose();
            _leaderCts = null;
        }
    }

    private bool CanStop() => LeaderBusy || IsRecording || IsPlaying;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (LeaderBusy)
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
            await EjectCassetteAsync(wav, station, when, nowPlaying, takePeak < 0.0005f, TimeSpan.Zero, mode, sideB, TakePendingMarkers(), updateLcd: true);
    }

    private bool CanKeep() => SelectedStation is not null && !LeaderBusy;

    [RelayCommand(CanExecute = nameof(CanKeep))]
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

        await EjectCassetteAsync(wav, station, when, nowPlaying, peak < 0.0005f, duration, "REPLAY", null, null, updateLcd: !IsRecording);
        if (IsRecording)
            Status = "Kept the last 15 seconds. Still recording.";
    }

    private bool CanPlay() => !IsRecording && !LeaderBusy && SelectedCassette is not null;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
        if (IsRecording || LeaderBusy || SelectedCassette is null)
            return;
        try
        {
            _playSideB = false;
            _player.Play(SelectedCassette.WavPath, SelectedRenderDevice?.Id);
            IsPlaying = true;
            LcdLine = SelectedCassette.Title.ToUpperInvariant();
            LcdMode = SelectedCassette.HasSideB ? "PLAY A" : "PLAY";
            Status = SelectedCassette.HasSideB
                ? "Playing Side A. Flip for the live voice take."
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

    private bool CanFlip() => CanPlay() && SelectedCassette is { HasSideB: true };

    [RelayCommand(CanExecute = nameof(CanFlip))]
    private void Flip()
    {
        if (IsRecording || LeaderBusy || SelectedCassette is null)
            return;
        if (!SelectedCassette.HasSideB)
        {
            Status = "That cassette has no voice take. Tick Live voice before Rec.";
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
                ? "Playing live voice take (−12 dB). Cue is for Side A marks."
                : "Playing Side A.";
            NotifyTransport();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            LcdMode = "ERR";
        }
    }

    private bool CanMark() => IsRecording;

    [RelayCommand(CanExecute = nameof(CanMark))]
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

    private bool CanCue() => IsPlaying && !IsRecording && SelectedCassette is { HasMarkers: true } && !_playSideB;

    [RelayCommand(CanExecute = nameof(CanCue))]
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

        if (_playSideB)
        {
            LcdMode = "CUE A";
            Status = "Cue is for Side A marks. Flip back to Side A, then Cue.";
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

    private bool CanDub() => !IsRecording && !LeaderBusy && Cassettes.Count >= 2;

    [RelayCommand(CanExecute = nameof(CanDub))]
    private async Task DubAsync()
    {
        if (IsRecording || LeaderBusy)
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
            var measured = await Task.Run(() => MeasureCassette(dest, when));
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


    [RelayCommand]
    private void DeleteCassette(CassetteItem? item)
    {
        if (item is null)
            return;

        var ask = MessageBox.Show(
            "Delete this cassette from the crate?",
            "OG Dub",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (ask != MessageBoxResult.Yes)
            return;

        if (SelectedCassette?.Record.Id == item.Record.Id)
        {
            _player.Stop();
            IsPlaying = false;
            SelectedCassette = null;
        }

        if (!_crate.TryDeleteCassette(item.Record.Id))
        {
            Status = "Could not delete that cassette.";
            return;
        }

        ReloadCrate();
        NotifyTransport();
        Status = "Cassette deleted from the crate.";
    }

    [RelayCommand]
    private void FavoriteCassette(CassetteItem? item)
    {
        if (item is null || !item.CanFavorite)
            return;

        var id = item.Record.Id;
        if (item.IsFavorite)
        {
            if (!_crate.TryRemoveFromFavorites(id))
            {
                Status = "Could not remove that cassette from Favorites.";
                return;
            }

            ReloadCrate();
            SelectedCassette = Cassettes.FirstOrDefault(c => c.Record.Id == id);
            Status = "Removed from Favorites. The crate copy stays.";
            return;
        }

        if (!_crate.TryCopyToFavorites(item.Record))
        {
            Status = "Could not copy that cassette into Favorites.";
            return;
        }

        ReloadCrate();
        SelectedCassette = Cassettes.FirstOrDefault(c => c.Record.Id == id);
        Status = "Saved a copy into the Favorites folder.";
    }

    [RelayCommand]
    private void RenameCassette(CassetteItem? item)
    {
        if (item is null)
            return;

        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? Application.Current.MainWindow;
        var prompt = new Views.NamePromptWindow(item.Title);
        if (owner is not null)
            prompt.Owner = owner;
        if (prompt.ShowDialog() != true)
            return;

        if (!_crate.TryRename(item.Record.Id, prompt.Result))
        {
            Status = "Could not rename that cassette.";
            return;
        }

        var id = item.Record.Id;
        ReloadCrate();
        SelectedCassette = Cassettes.FirstOrDefault(c => c.Record.Id == id);
        Status = "J-card renamed.";
    }

    public void Shutdown()
    {
        _shuttingDown = true;
        Edit.CancelJob();
        Edit.DisposeProbe();
        _leaderCts?.Cancel();
        if (IsRecording)
            FlushRecording();
        _listen.Dispose();
        _mic.Dispose();
        _player.Dispose();
        _listenLock.Dispose();
        _leaderCts?.Dispose();
    }

    private void FlushRecording()
    {
        var wav = _pendingWav;
        var station = _pendingStation;
        var when = _recStarted;
        var nowPlaying = _takeNowPlaying;
        var mode = _pendingMode;
        var takePeak = _listen.TakePeak;
        var sideB = _pendingSideB;
        _pendingSideB = null;
        try
        {
            _listenLock.Wait();
            try
            {
                _listen.EndTape();
                _mic.StopTape();
            }
            finally
            {
                _listenLock.Release();
            }
        }
        catch (Exception)
        {
        }

        IsRecording = false;
        RecLit = false;
        _pendingWav = null;
        _pendingStation = null;
        _takeNowPlaying = null;
        if (wav is not null && station is not null)
            EjectCassetteSync(wav, station, when, nowPlaying, takePeak < 0.0005f, TimeSpan.Zero, mode, sideB, TakePendingMarkers(), updateLcd: false);
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
                _mic.StopTape();
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
        await EjectCassetteAsync(wav, station, when, prev, takePeak < 0.0005f, TimeSpan.Zero, mode, sideB, TakePendingMarkers(), updateLcd: false);

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

    private async Task EjectCassetteAsync(
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

        var measured = await Task.Run(() => MeasureCassette(wav, when));
        EjectCassetteApply(wav, station, when, nowPlaying, silent, duration, captureMode, sideBWav, markers, updateLcd, measured);
    }

    private void EjectCassetteSync(
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
        EjectCassetteApply(wav, station, when, nowPlaying, silent, duration, captureMode, sideBWav, markers, updateLcd, measured);
    }

    private void EjectCassetteApply(
        string wav,
        Station station,
        DateTimeOffset when,
        NowPlaying? nowPlaying,
        bool silent,
        TimeSpan duration,
        string captureMode,
        string? sideBWav,
        IReadOnlyList<TapeMarker>? markers,
        bool updateLcd,
        (TimeSpan Duration, LoudnessResult Loud, int SampleRate) measured)
    {
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
                Status += " Flip plays the live voice take.";
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
                    : "Cassette in the crate. Flip plays the live voice take (−12 dB).";
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
            _mic.StartTape(wav, SelectedMicDevice?.Id);
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
        if (RecordMic)
            return "Recording app tape + live voice (−12 dB). Hear-through follows MIC ON — use headphones. Stop or wait for the side to run out.";
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

    private void NotifyTransport()
    {
        ScanCommand.NotifyCanExecuteChanged();
        RecCommand.NotifyCanExecuteChanged();
        KeepCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        FlipCommand.NotifyCanExecuteChanged();
        MarkCommand.NotifyCanExecuteChanged();
        CueCommand.NotifyCanExecuteChanged();
        DubCommand.NotifyCanExecuteChanged();
    }

    private bool StationsLookSame(IReadOnlyList<StationItem> next)
    {
        if (Stations.Count != next.Count)
            return false;
        for (var i = 0; i < next.Count; i++)
        {
            var a = Stations[i].Station;
            var b = next[i].Station;
            if (a.ProcessId != b.ProcessId || a.IsWholeMix != b.IsWholeMix || a.Name != b.Name)
                return false;
        }

        return true;
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

    public async Task<bool> ImportEditedCassetteAsync(byte[] wavBytes, string sourceTitle, CancellationToken cancel)
    {
        if (wavBytes is null || wavBytes.Length < 12 || !LocalLoopback.IsRiffWave(wavBytes))
            return false;

        var when = DateTimeOffset.Now;
        var dest = UniqueWav(LocalLoopback.EditedStem(sourceTitle, when));
        var full = Path.GetFullPath(dest);
        if (!LibraryPaths.IsUnderLibrary(full, _crate.LibraryRoot))
            return false;

        try
        {
            await File.WriteAllBytesAsync(full, wavBytes, cancel);
            cancel.ThrowIfCancellationRequested();
            var measured = await Task.Run(() => MeasureCassette(full, when), cancel);
            cancel.ThrowIfCancellationRequested();
            var silent = measured.Duration <= TimeSpan.Zero;
            var name = CassetteNaming.Sanitize(sourceTitle);
            if (!name.EndsWith(" (edit)", StringComparison.OrdinalIgnoreCase))
                name += " (edit)";
            var station = new Station { ProcessId = 0, Name = name, Peak = 0, IsWholeMix = false };
            EjectCassetteApply(full, station, when, null, silent, measured.Duration, "EDIT", null, null, true, measured);
            return true;
        }
        catch (Exception)
        {
            TryDelete(full);
            throw;
        }
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
