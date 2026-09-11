namespace OgDub.Core;

public static class LibraryPaths
{
    public const string PublisherFolder = "OG Digital Designs";
    public const string ProductFolder = "OG Dub";

    public static string DefaultLibrary()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            PublisherFolder,
            ProductFolder);
    }

    public static string FavoritesFolder(string libraryRoot)
    {
        var rootPath = Path.GetFullPath(libraryRoot);
        var fav = Path.GetFullPath(Path.Combine(rootPath, "Favorites"));
        return IsUnderLibrary(fav, rootPath) ? fav : Path.Combine(rootPath, "Favorites");
    }

    public static string SettingsFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            PublisherFolder,
            ProductFolder);
    }

    public static bool TryNormalizeChosen(string? path, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            full = Path.GetFullPath(path.Trim());
            if (full.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return false;
            var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name is "." or "..")
                return false;
            return true;
        }
        catch (ArgumentException)
        {
            full = "";
            return false;
        }
        catch (NotSupportedException)
        {
            full = "";
            return false;
        }
        catch (PathTooLongException)
        {
            full = "";
            return false;
        }
    }

    public static bool IsUnderLibrary(string path, string libraryRoot)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(libraryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
