using System.Text.Json;

namespace OgDub.Core;

public sealed class CrateStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _libraryRoot;

    public CrateStore(string libraryRoot)
    {
        _libraryRoot = Path.GetFullPath(libraryRoot);
    }

    public string LibraryRoot => _libraryRoot;

    public string IndexPath => Path.Combine(_libraryRoot, "crate.json");

    public IReadOnlyList<CassetteRecord> Load()
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

    public void Save(IEnumerable<CassetteRecord> cassettes)
    {
        Directory.CreateDirectory(_libraryRoot);
        var index = new CrateIndex { Cassettes = cassettes.ToList() };
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(index, Json));
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
}
