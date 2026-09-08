namespace OgDub.Core;

public static class WavConcat
{
    public static void Dub(IReadOnlyList<string> sources, string dest, TimeSpan gap)
    {
        if (sources.Count == 0)
            throw new InvalidOperationException("Pick at least one cassette to dub.");

        WavPcm first;
        using (var stream = File.OpenRead(sources[0]))
            first = WavPcm.Read(stream);

        var gapBytes = SilenceBytes(first, gap);
        using var output = File.Create(dest);
        var data = new MemoryStream();
        data.Write(first.Data, 0, first.Data.Length);

        for (var i = 1; i < sources.Count; i++)
        {
            WavPcm next;
            using (var stream = File.OpenRead(sources[i]))
                next = WavPcm.Read(stream);
            if (!next.SameFormat(first))
                throw new InvalidOperationException("Those cassettes are different formats. Dub matching takes only.");

            data.Write(gapBytes, 0, gapBytes.Length);
            data.Write(next.Data, 0, next.Data.Length);
        }

        WavPcm.Write(output, first, data.ToArray());
    }

    private static byte[] SilenceBytes(WavPcm format, TimeSpan gap)
    {
        if (gap <= TimeSpan.Zero)
            return [];

        var bytes = (int)(format.AverageBytesPerSecond * gap.TotalSeconds);
        bytes -= bytes % format.BlockAlign;
        if (bytes < 0)
            bytes = 0;
        return new byte[bytes];
    }
}
