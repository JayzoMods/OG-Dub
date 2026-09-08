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
        var local = TimeZoneInfo.ConvertTime(when, Sydney);
        var mins = Math.Max(0, (int)Math.Round(duration.TotalMinutes));
        return Sanitize(appName) + " · " + local.ToString("ddd d MMM") + " · " + mins + "m";
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
}
