namespace OgDub.Core;

public static class SmtcSongChange
{
    public static bool IsNewSong(string? previousTitle, string? nextTitle)
    {
        var prev = CassetteNaming.Collapse(previousTitle);
        var next = CassetteNaming.Collapse(nextTitle);
        if (prev.Length == 0 || next.Length == 0)
            return false;

        return !prev.Equals(next, StringComparison.OrdinalIgnoreCase);
    }
}
