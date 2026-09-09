namespace OgDub.Core;

public static class CassetteNaming
{
    private static readonly TimeZoneInfo Sydney =
        TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");

    public static string FileStem(string appName, DateTimeOffset when)
    {
        var stamp = TimeZoneInfo.ConvertTime(when, Sydney).ToString("yyyy-MM-dd HHmm");
        return Sanitize(appName) + " " + stamp;
    }

    public static string JCard(string appName, DateTimeOffset when, TimeSpan duration)
    {
        return Label(Sanitize(appName), null, when, duration);
    }

    public static string JCardFromTrack(string trackTitle, string? artist, DateTimeOffset when, TimeSpan duration)
    {
        return Label(Collapse(trackTitle), Collapse(artist), when, duration);
    }

    private static string Label(string primary, string? artist, DateTimeOffset when, TimeSpan duration)
    {
        var local = TimeZoneInfo.ConvertTime(when, Sydney);
        var mins = Math.Max(0, (int)Math.Round(duration.TotalMinutes));
        var whenBit = local.ToString("ddd d MMM") + " · " + mins + "m";
        if (string.IsNullOrEmpty(primary))
            primary = "Station";
        if (string.IsNullOrEmpty(artist))
            return primary + " · " + whenBit;
        return primary + " · " + artist + " · " + whenBit;
    }

    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Station";

        var chars = name.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(Path.GetInvalidFileNameChars(), chars[i]) >= 0)
                chars[i] = ' ';
        }

        var cleaned = string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(cleaned) ? "Station" : cleaned;
    }

    public static string Collapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var trimmed = text.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return string.Join(" ", trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static (string Date, string Time) BextStamp(DateTimeOffset when)
    {
        var local = TimeZoneInfo.ConvertTime(when, Sydney);
        return (local.ToString("yyyy-MM-dd"), local.ToString("HH:mm:ss"));
    }
}
