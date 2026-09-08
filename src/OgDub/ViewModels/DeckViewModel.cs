using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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
    public string Title => Record.Title;
    public string ShellColour => Record.Silent ? "#4A5560" : Record.ShellColour;
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
    private readonly ProcessLoopbackCapture _process = new();
    private readonly MixLoopbackCapture _mix = new();
    private readonly TapePlayer _player = new();
    private DateTimeOffset _recStarted;
    private string? _pendingWav;
    private Station? _pendingStation;
    private string _pendingMode = "";
    private int _shellIndex;

    public DeckViewModel(CrateStore crate, AppSettingsStore settings)
    {
        _crate = crate;
        _settings = settings;
        AlwaysOnTop = settings.Current.AlwaysOnTop;
        ReloadCrate();
        RefreshStations();
    }

    public ObservableCollection<StationItem> Stations { get; } = [];
    public ObservableCollection<CassetteItem> Cassettes { get; } = [];

    [ObservableProperty] private StationItem? selectedStation;
    [ObservableProperty] private CassetteItem? selectedCassette;
    [ObservableProperty] private string lcdLine = "AIM AN APP";
    [ObservableProperty] private string lcdTime = "30:00";
    [ObservableProperty] private string lcdMode = "C-60";
    [ObservableProperty] private bool recLit;
    [ObservableProperty] private bool isRecording;
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private double vu;
    [ObservableProperty] private double tapePack = 1;
    [ObservableProperty] private bool alwaysOnTop;
    [ObservableProperty] private string status = "Punch Rec. C-60 Side A is 30 minutes.";

    partial void OnAlwaysOnTopChanged(bool value)
    {
        _settings.Current.AlwaysOnTop = value;
        _settings.Save();
    }

    public void Tick()
    {
        if (!IsRecording)
        {
            RefreshStations();
            Vu = SelectedStation?.Station.Peak ?? 0;
            IsPlaying = _player.IsPlaying;
            return;
        }

        var elapsed = DateTimeOffset.Now - _recStarted;
        var left = TapeSide.Remaining(elapsed, TapeSide.C60SideA);
        LcdTime = TapeSide.LcdCountdown(left);
        TapePack = left.TotalSeconds / TapeSide.C60SideA.TotalSeconds;
        Vu = Math.Max(_process.Peak, _mix.Peak);
        RecLit = DateTimeOffset.Now.Millisecond < 500;

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

    public void ReloadCrate()
    {
        Cassettes.Clear();
        foreach (var record in _crate.Load().OrderByDescending(c => c.RecordedAt))
        {
            Cassettes.Add(new CassetteItem
            {
                Record = record,
                WavPath = _crate.WavPath(record)
            });
        }
    }

    [RelayCommand]
    private void Scan()
    {
        if (Stations.Count == 0)
            return;
        var i = SelectedStation is null ? 0 : Stations.IndexOf(SelectedStation);
        SelectedStation = Stations[(i + 1) % Stations.Count];
        LcdLine = SelectedStation.Label.ToUpperInvariant();
    }

    [RelayCommand]
    private async Task RecAsync()
    {
        if (IsRecording)
            return;

        _player.Stop();
        IsPlaying = false;
        var station = SelectedStation?.Station;
        if (station is null)
        {
            Status = "Scan to a station first.";
            return;
        }

        var when = DateTimeOffset.Now;
        var stem = CassetteNaming.FileStem(station.Name, when);
        var wav = UniqueWav(stem);
        _pendingWav = wav;
        _pendingStation = station;
        _recStarted = when;
        LcdLine = station.Name.ToUpperInvariant();
        LcdMode = "REC";
        Status = "Recording. Stop or wait for the side to run out.";

        try
        {
            await Task.Run(() => ArmCapture(station, wav)).ConfigureAwait(true);
            IsRecording = true;
            RecLit = true;
            TapePack = 1;
        }
        catch (Exception ex)
        {
            TryDelete(wav);
            LcdMode = "ERR";
            LcdLine = "NO LOCK";
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (!IsRecording && !IsPlaying)
            return;

        if (IsPlaying)
        {
            _player.Stop();
            IsPlaying = false;
            LcdMode = "STOP";
            return;
        }

        await Task.Run(() =>
        {
            _process.Stop();
            _mix.Stop();
        }).ConfigureAwait(true);

        IsRecording = false;
        RecLit = false;
        LcdTime = "30:00";
        TapePack = 1;
        EjectPending();
    }

    [RelayCommand]
    private void Play()
    {
        if (IsRecording || SelectedCassette is null)
            return;
        try
        {
            _player.Play(SelectedCassette.WavPath);
            IsPlaying = true;
            LcdLine = SelectedCassette.Title.ToUpperInvariant();
            LcdMode = "PLAY";
            Status = "Playing the crate. Stop to halt.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            LcdMode = "ERR";
        }
    }

    [RelayCommand]
    private void Dub()
    {
        if (IsRecording)
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
            var record = new CassetteRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = CassetteNaming.JCard("Mixtape", when, TapeSide.C60SideA),
                AppName = "Mixtape",
                WavFileName = Path.GetFileName(dest),
                RecordedAt = when,
                DurationSeconds = 0,
                ShellColour = NextShell(),
                Silent = false,
                CaptureMode = "DUB"
            };
            var all = _crate.Load().ToList();
            all.Add(record);
            _crate.Save(all);
            ReloadCrate();
            LcdLine = "MIXTAPE";
            LcdMode = "DUB";
            Status = "Dubbed the two newest cassettes with 2 s gaps.";
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
        _process.Dispose();
        _mix.Dispose();
        _player.Dispose();
    }

    private void ArmCapture(Station station, string wav)
    {
        var limit = TapeSide.C60SideA;
        if (!station.IsWholeMix)
        {
            try
            {
                _process.Start((uint)station.ProcessId, includeTree: true, wav, limit);
                _pendingMode = _process.Mode;
                return;
            }
            catch (Exception)
            {
                TryDelete(wav);
            }
        }

        try
        {
            _process.Start((uint)Environment.ProcessId, includeTree: false, wav, limit);
            _pendingMode = _process.Mode;
        }
        catch (Exception)
        {
            TryDelete(wav);
            _mix.Start(wav);
            _pendingMode = "MIX";
        }
    }

    private void EjectPending()
    {
        var wav = _pendingWav;
        var station = _pendingStation;
        _pendingWav = null;
        _pendingStation = null;
        if (wav is null || station is null || !File.Exists(wav))
            return;

        var duration = TimeSpan.Zero;
        try
        {
            using var stream = File.OpenRead(wav);
            var pcm = WavPcm.Read(stream);
            if (pcm.AverageBytesPerSecond > 0)
                duration = TimeSpan.FromSeconds(pcm.Data.Length / (double)pcm.AverageBytesPerSecond);
        }
        catch (Exception)
        {
            duration = DateTimeOffset.Now - _recStarted;
        }

        var silent = Math.Max(_process.Peak, _mix.Peak) < 0.0005;
        var when = _recStarted;
        var title = silent
                ? "Nothing playing"
                : CassetteNaming.JCard(station.Name, when, duration);
        var record = new CassetteRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            AppName = station.Name,
            WavFileName = Path.GetFileName(wav),
            RecordedAt = when,
            DurationSeconds = duration.TotalSeconds,
            ShellColour = NextShell(),
            Silent = silent,
            CaptureMode = _pendingMode
        };

        var all = _crate.Load().ToList();
        all.Add(record);
        _crate.Save(all);
        ReloadCrate();
        SelectedCassette = Cassettes.FirstOrDefault(c => c.Record.Id == record.Id);

        if (silent)
        {
            LcdLine = "NOTHING PLAYING";
            LcdMode = "SILENT";
            Status = "That take was silence. Protected audio does that. The cassette is still in the crate.";
        }
        else
        {
            LcdLine = record.Title.ToUpperInvariant();
            LcdMode = "EJECT";
            Status = "Cassette in the crate. Play it, dub it, or drag the file out of the folder.";
        }
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
