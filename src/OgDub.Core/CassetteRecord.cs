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
    public string TrackTitle { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string ArtworkFileName { get; set; } = "";
    public string SideBWavFileName { get; set; } = "";
    public double? LoudnessLufs { get; set; }
    public double? PeakDbfs { get; set; }
    public List<TapeMarker> Markers { get; set; } = [];
}

public sealed class TapeMarker
{
    public int Counter { get; set; }
    public double OffsetSeconds { get; set; }
    public string Label { get; set; } = "";
}

public sealed class CrateIndex
{
    [JsonPropertyName("cassettes")]
    public List<CassetteRecord> Cassettes { get; set; } = [];
}
