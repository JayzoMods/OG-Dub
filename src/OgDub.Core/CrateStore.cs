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
            return loaded?.Cassettes ?? [];
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
}
