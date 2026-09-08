using System.IO;
using OgDub.Core;
using OgDub.Services;
using OgDub.ViewModels;

namespace OgDub;

public static class AppHost
{
    public static AppSettingsStore Settings { get; } = new();
    public static TermsService Terms { get; private set; } = null!;
    public static DeckViewModel Deck { get; private set; } = null!;
    public static CrateStore Crate { get; private set; } = null!;

    public static void Start()
    {
        Settings.Load();
        Terms = new TermsService(Settings);
        Crate = new CrateStore(LibraryPaths.DefaultLibrary());
        Directory.CreateDirectory(Crate.LibraryRoot);
        Deck = new DeckViewModel(Crate, Settings);
    }
}
