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
        Crate = new CrateStore(ResolveLibrary());
        Directory.CreateDirectory(Crate.LibraryRoot);
        Deck = new DeckViewModel(Crate, Settings);
    }

    public static bool TryChangeLibrary(string chosen, out string error)
    {
        error = "";
        if (!LibraryPaths.TryNormalizeChosen(chosen, out var full))
        {
            error = "That folder path was refused.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(full);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        Settings.Current.LibraryRoot = full;
        Settings.Save();
        Crate = new CrateStore(full);
        Directory.CreateDirectory(Crate.LibraryRoot);
        Deck.ReplaceCrate(Crate);
        return true;
    }

    private static string ResolveLibrary()
    {
        var saved = Settings.Current.LibraryRoot;
        if (LibraryPaths.TryNormalizeChosen(saved, out var full))
            return full;
        return LibraryPaths.DefaultLibrary();
    }
}
