namespace OgDub.Core;

public static class InstantReplay
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan MinimumDump = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan MinimumSongSplit = TimeSpan.FromSeconds(2);

    public static int CapacityBytes(int averageBytesPerSecond, int blockAlign)
    {
        if (averageBytesPerSecond <= 0 || blockAlign <= 0)
            return 0;

        var bytes = averageBytesPerSecond * (int)Window.TotalSeconds;
        return (bytes / blockAlign) * blockAlign;
    }
}
