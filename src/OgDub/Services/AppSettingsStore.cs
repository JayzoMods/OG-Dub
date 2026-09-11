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
    public bool MicOn { get; set; } = true;
    public string PlaybackDeviceId { get; set; } = "";
    public string MicDeviceId { get; set; } = "";
    public bool? GlobalHotkeys { get; set; }
    public string LibraryRoot { get; set; } = "";
    public string LastTab { get; set; } = "";
    public string ExtraLocalBaseUrl { get; set; } = "";
    public string LastProviderId { get; set; } = "";
    public string LastModelId { get; set; } = "";
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
                loaded.MicDeviceId ??= "";
                loaded.LibraryRoot ??= "";
                loaded.LastTab ??= "";
                loaded.ExtraLocalBaseUrl ??= "";
                loaded.LastProviderId ??= "";
                loaded.LastModelId ??= "";
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
