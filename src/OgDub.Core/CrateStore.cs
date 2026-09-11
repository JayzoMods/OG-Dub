using System.Text.Json;

namespace OgDub.Core;

public sealed class CrateStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _libraryRoot;
    private readonly object _gate = new();

    public CrateStore(string libraryRoot)
    {
        _libraryRoot = Path.GetFullPath(libraryRoot);
    }

    public string LibraryRoot => _libraryRoot;

    public string IndexPath => Path.Combine(_libraryRoot, "crate.json");

    public IReadOnlyList<CassetteRecord> Load()
    {
        lock (_gate)
            return LoadUnlocked();
    }

    public void Save(IEnumerable<CassetteRecord> cassettes)
    {
        lock (_gate)
            SaveUnlocked(cassettes);
    }

    public string WavPath(CassetteRecord cassette)
    {
        var name = Path.GetFileName(cassette.WavFileName);
        return Path.Combine(_libraryRoot, name);
    }

    public string ArtworkPath(CassetteRecord cassette)
    {
        if (string.IsNullOrWhiteSpace(cassette.ArtworkFileName))
            return "";

        var name = Path.GetFileName(cassette.ArtworkFileName);
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            return "";

        var full = Path.GetFullPath(Path.Combine(_libraryRoot, name));
        return LibraryPaths.IsUnderLibrary(full, _libraryRoot) ? full : "";
    }

    public string SideBPath(CassetteRecord cassette)
    {
        if (string.IsNullOrWhiteSpace(cassette.SideBWavFileName))
            return "";

        var name = Path.GetFileName(cassette.SideBWavFileName);
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            return "";

        var full = Path.GetFullPath(Path.Combine(_libraryRoot, name));
        return LibraryPaths.IsUnderLibrary(full, _libraryRoot) ? full : "";
    }

    public string FavoritesRoot => LibraryPaths.FavoritesFolder(_libraryRoot);

    public string FavoritesIndexPath => Path.Combine(FavoritesRoot, "favorites.json");

    public bool IsFavorite(CassetteRecord cassette)
    {
        if (cassette is null || string.IsNullOrWhiteSpace(cassette.Id))
            return false;
        return LoadFavorites().Any(c => string.Equals(c.Id, cassette.Id, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<CassetteRecord> LoadFavorites()
    {
        lock (_gate)
            return LoadFavoritesUnlocked();
    }

    public void SaveFavorites(IEnumerable<CassetteRecord> cassettes)
    {
        lock (_gate)
            SaveFavoritesUnlocked(cassettes);
    }

    public bool TryRename(string id, string title)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var cleaned = CassetteNaming.Collapse(title);
        if (string.IsNullOrWhiteSpace(cleaned))
            return false;

        lock (_gate)
        {
            var all = LoadUnlocked().ToList();
            var hit = all.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (hit is null)
                return false;

            hit.Title = cleaned;
            SaveUnlocked(all);

            var favs = LoadFavoritesUnlocked().ToList();
            var fav = favs.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (fav is not null)
            {
                fav.Title = cleaned;
                SaveFavoritesUnlocked(favs);
            }

            return true;
        }
    }

    public bool TryDeleteCassette(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        lock (_gate)
        {
            var all = LoadUnlocked().ToList();
            var hit = all.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (hit is null)
                return false;

            all.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            SaveUnlocked(all);

            TryDeleteUnderLibrary(WavPath(hit));
            TryDeleteUnderLibrary(SideBPath(hit));
            TryDeleteUnderLibrary(ArtworkPath(hit));

            var favs = LoadFavoritesUnlocked().ToList();
            if (favs.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                SaveFavoritesUnlocked(favs);
                TryDeleteUnderLibrary(Path.Combine(FavoritesRoot, Path.GetFileName(hit.WavFileName)));
                if (!string.IsNullOrWhiteSpace(hit.SideBWavFileName))
                    TryDeleteUnderLibrary(Path.Combine(FavoritesRoot, Path.GetFileName(hit.SideBWavFileName)));
                if (!string.IsNullOrWhiteSpace(hit.ArtworkFileName))
                    TryDeleteUnderLibrary(Path.Combine(FavoritesRoot, Path.GetFileName(hit.ArtworkFileName)));
            }

            return true;
        }
    }

    public bool TryCopyToFavorites(CassetteRecord cassette)
    {
        if (cassette is null || cassette.Silent || string.IsNullOrWhiteSpace(cassette.Id))
            return false;

        lock (_gate)
        {
            Directory.CreateDirectory(FavoritesRoot);
            var favs = LoadFavoritesUnlocked().ToList();
            if (favs.Any(c => string.Equals(c.Id, cassette.Id, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (!TryCopyIntoFavorites(WavPath(cassette), cassette.WavFileName))
                return false;
            if (!string.IsNullOrWhiteSpace(cassette.SideBWavFileName))
                TryCopyIntoFavorites(SideBPath(cassette), cassette.SideBWavFileName);
            if (!string.IsNullOrWhiteSpace(cassette.ArtworkFileName))
                TryCopyIntoFavorites(ArtworkPath(cassette), cassette.ArtworkFileName);

            favs.Add(CloneRecord(cassette));
            SaveFavoritesUnlocked(favs);
            return true;
        }
    }

    public bool TryRemoveFromFavorites(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        lock (_gate)
        {
            var favs = LoadFavoritesUnlocked().ToList();
            var hit = favs.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (hit is null)
                return false;

            favs.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            SaveFavoritesUnlocked(favs);
            TryDeleteUnderLibrary(Path.Combine(FavoritesRoot, Path.GetFileName(hit.WavFileName)));
            if (!string.IsNullOrWhiteSpace(hit.SideBWavFileName))
                TryDeleteUnderLibrary(Path.Combine(FavoritesRoot, Path.GetFileName(hit.SideBWavFileName)));
            if (!string.IsNullOrWhiteSpace(hit.ArtworkFileName))
                TryDeleteUnderLibrary(Path.Combine(FavoritesRoot, Path.GetFileName(hit.ArtworkFileName)));
            return true;
        }
    }

    private IReadOnlyList<CassetteRecord> LoadUnlocked()
    {
        Directory.CreateDirectory(_libraryRoot);
        if (!File.Exists(IndexPath))
            return [];

        try
        {
            var loaded = JsonSerializer.Deserialize<CrateIndex>(File.ReadAllText(IndexPath));
            if (loaded?.Cassettes is null)
                return [];

            foreach (var cassette in loaded.Cassettes)
                cassette.Markers ??= [];

            return loaded.Cassettes;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveUnlocked(IEnumerable<CassetteRecord> cassettes)
    {
        Directory.CreateDirectory(_libraryRoot);
        var index = new CrateIndex { Cassettes = cassettes.ToList() };
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(index, Json));
    }

    private IReadOnlyList<CassetteRecord> LoadFavoritesUnlocked()
    {
        Directory.CreateDirectory(FavoritesRoot);
        if (!File.Exists(FavoritesIndexPath))
            return [];

        try
        {
            var loaded = JsonSerializer.Deserialize<CrateIndex>(File.ReadAllText(FavoritesIndexPath));
            if (loaded?.Cassettes is null)
                return [];
            foreach (var cassette in loaded.Cassettes)
                cassette.Markers ??= [];
            return loaded.Cassettes;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveFavoritesUnlocked(IEnumerable<CassetteRecord> cassettes)
    {
        Directory.CreateDirectory(FavoritesRoot);
        var index = new CrateIndex { Cassettes = cassettes.ToList() };
        File.WriteAllText(FavoritesIndexPath, JsonSerializer.Serialize(index, Json));
    }

    private bool TryCopyIntoFavorites(string sourcePath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return false;
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            return false;
        var dest = Path.GetFullPath(Path.Combine(FavoritesRoot, name));
        if (!LibraryPaths.IsUnderLibrary(dest, _libraryRoot))
            return false;
        File.Copy(sourcePath, dest, overwrite: true);
        return true;
    }

    private void TryDeleteUnderLibrary(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var full = Path.GetFullPath(path);
        if (!LibraryPaths.IsUnderLibrary(full, _libraryRoot) || !File.Exists(full))
            return;
        try { File.Delete(full); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static CassetteRecord CloneRecord(CassetteRecord src) => new()
    {
        Id = src.Id,
        Title = src.Title,
        AppName = src.AppName,
        WavFileName = Path.GetFileName(src.WavFileName),
        RecordedAt = src.RecordedAt,
        DurationSeconds = src.DurationSeconds,
        ShellColour = src.ShellColour,
        Silent = src.Silent,
        CaptureMode = src.CaptureMode,
        TrackTitle = src.TrackTitle,
        Artist = src.Artist,
        Album = src.Album,
        ArtworkFileName = string.IsNullOrWhiteSpace(src.ArtworkFileName) ? "" : Path.GetFileName(src.ArtworkFileName),
        SideBWavFileName = string.IsNullOrWhiteSpace(src.SideBWavFileName) ? "" : Path.GetFileName(src.SideBWavFileName),
        LoudnessLufs = src.LoudnessLufs,
        PeakDbfs = src.PeakDbfs,
        Markers = src.Markers?.Select(m => new TapeMarker
        {
            Counter = m.Counter,
            OffsetSeconds = m.OffsetSeconds,
            Label = m.Label
        }).ToList() ?? []
    };
}
