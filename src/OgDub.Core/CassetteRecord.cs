using System.Text.Json.Serialization;

namespace OgDub.Core;

public sealed class CassetteRecord
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string AppName { get; set; } = "";
    public string WavFileName { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
    public double DurationSeconds { get; set; }
    public string ShellColour { get; set; } = "#C45C26";
    public bool Silent { get; set; }
    public string CaptureMode { get; set; } = "";
}

public sealed class CrateIndex
{
    [JsonPropertyName("cassettes")]
    public List<CassetteRecord> Cassettes { get; set; } = [];
}
