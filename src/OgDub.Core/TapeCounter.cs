namespace OgDub.Core;

public static class TapeCounter
{
    public const int Max = 999;
    public const int MaxMarks = 99;

    public static int FromElapsed(TimeSpan elapsed, TimeSpan? sideLength = null)
    {
        var side = sideLength ?? TapeSide.C60SideA;
        if (elapsed <= TimeSpan.Zero || side <= TimeSpan.Zero)
            return 0;

        var scaled = elapsed.TotalSeconds / side.TotalSeconds * Max;
        if (double.IsNaN(scaled) || double.IsInfinity(scaled) || scaled < 0)
            return 0;
        return (int)Math.Clamp(Math.Round(scaled), 0, Max);
    }

    public static int Clamp(int counter) => Math.Clamp(counter, 0, Max);

    public static string Lcd(int counter)
    {
        return Clamp(counter).ToString("000", System.Globalization.CultureInfo.GetCultureInfo("en-AU"));
    }
}

public static class TapeMarkers
{
    public static TapeMarker? Next(IReadOnlyList<TapeMarker> markers, double positionSeconds, double skipAhead = 0.15)
    {
        if (markers.Count == 0)
            return null;

        TapeMarker? first = null;
        TapeMarker? upcoming = null;
        foreach (var marker in markers.OrderBy(m => m.OffsetSeconds))
        {
            first ??= marker;
            if (upcoming is null && marker.OffsetSeconds > positionSeconds + skipAhead)
                upcoming = marker;
        }

        return upcoming ?? first;
    }
}
