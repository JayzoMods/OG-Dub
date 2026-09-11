using System.IO;
using System.Text.Json;
using OgDub.Core;

namespace OgDub.Services;

public sealed class AppSettings
{
    public string TermsAcceptedVersion { get; set; } = "";
    public bool AlwaysOnTop { get; set; } = true;
    public bool SplitOnSong { get; set; } = true;
    public bool RecordMic { get; set; }
    public string PlaybackDeviceId { get; set; } = "";
    public bool? GlobalHotkeys { get; set; }
    public string LibraryRoot { get; set; } = "";
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    public static string RootFolder => LibraryPaths.SettingsFolder();

    public static string SettingsPath => Path.Combine(RootFolder, "settings.json");

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return;
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
            if (loaded is not null)
            {
                loaded.TermsAcceptedVersion ??= "";
                loaded.PlaybackDeviceId ??= "";
                loaded.LibraryRoot ??= "";
                Current = loaded;
            }
        }
        catch (JsonException)
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(RootFolder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, Json));
    }
}
