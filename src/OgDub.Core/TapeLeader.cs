namespace OgDub.Core;

public static class TapeLeader
{
    public const int Beats = 3;
    public static readonly TimeSpan Beat = TimeSpan.FromSeconds(1);

    public static string Lcd(int beat)
    {
        if (beat < 1)
            return "";
        return beat.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-AU"));
    }
}

public static class ClipLight
{
    public const float Threshold = 0.99f;

    public static bool IsOn(float peak) => peak >= Threshold;
}
