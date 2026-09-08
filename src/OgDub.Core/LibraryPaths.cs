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

    public static string SettingsFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            PublisherFolder,
            ProductFolder);
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
