namespace OgDub.Core;

public static class TapeSide
{
    public static readonly TimeSpan C60SideA = TimeSpan.FromMinutes(30);

    public static TimeSpan Remaining(TimeSpan elapsed, TimeSpan sideLength)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        var left = sideLength - elapsed;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    public static string LcdCountdown(TimeSpan remaining)
    {
        var t = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        return string.Format(
            System.Globalization.CultureInfo.GetCultureInfo("en-AU"),
            "{0:00}:{1:00}",
            (int)t.TotalMinutes,
            t.Seconds);
    }
}
