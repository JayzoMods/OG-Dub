namespace OgDub.Core;

public readonly struct LoudnessResult
{
    public double? LoudnessLufs { get; init; }
    public double? PeakDbfs { get; init; }
}

public static class Loudness1770
{
    public const double AbsoluteGateLufs = -70;
    public const double RelativeGateLu = -10;
    private const double Offset = -0.691;

    public static LoudnessResult Measure(WavPcm pcm)
    {
        if (pcm.SampleRate < 8000 || pcm.Channels <= 0 || pcm.Data.Length == 0)
            return default;

        var frames = pcm.Data.Length / Math.Max(1, (int)pcm.BlockAlign);
        if (frames <= 0)
            return default;

        var channels = pcm.Channels;
        var pre = new Biquad[channels];
        var rlb = new Biquad[channels];
        for (var c = 0; c < channels; c++)
        {
            pre[c] = HighShelf(pcm.SampleRate, 1681.974450955533, 0.7071752369554193, 4);
            rlb[c] = HighPass(pcm.SampleRate, 38.13547087602444, 0.5003270373238773);
        }

        var hop = Math.Max(1, pcm.SampleRate / 10);
        var hops = new List<double>(Math.Max(4, frames / hop));
        double hopEnergy = 0;
        var hopCount = 0;
        var peak = 0.0;

        for (var i = 0; i < frames; i++)
        {
            double power = 0;
            for (var c = 0; c < channels; c++)
            {
                var x = Sample(pcm, i, c);
                var ax = Math.Abs(x);
                if (ax > peak)
                    peak = ax;
                var y = rlb[c].Process(pre[c].Process(x));
                power += y * y;
            }

            hopEnergy += power / channels;
            hopCount++;
            if (hopCount == hop)
            {
                hops.Add(hopEnergy / hop);
                hopEnergy = 0;
                hopCount = 0;
            }
        }

        double? lufs = null;
        if (hops.Count >= 4)
        {
            var absPassed = new List<double>();
            for (var i = 3; i < hops.Count; i++)
            {
                var z = (hops[i - 3] + hops[i - 2] + hops[i - 1] + hops[i]) / 4.0;
                if (BlockLufs(z) >= AbsoluteGateLufs)
                    absPassed.Add(z);
            }

            if (absPassed.Count > 0)
            {
                var ungated = Mean(absPassed);
                var relative = BlockLufs(ungated) + RelativeGateLu;
                var gated = new List<double>();
                foreach (var z in absPassed)
                {
                    if (BlockLufs(z) >= relative)
                        gated.Add(z);
                }

                if (gated.Count > 0)
                    lufs = BlockLufs(Mean(gated));
            }
        }
        else if (frames > 0)
        {
            lufs = AbsoluteGateLufs;
        }

        double? peakDbfs = peak > 0 ? 20.0 * Math.Log10(peak) : -96;
        return new LoudnessResult { LoudnessLufs = lufs, PeakDbfs = peakDbfs };
    }

    public static string Lcd(double? lufs)
    {
        if (lufs is null || double.IsNaN(lufs.Value) || double.IsInfinity(lufs.Value))
            return "";
        return lufs.Value.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("en-AU")) + " LUFS";
    }

    private static double BlockLufs(double meanSquare)
    {
        if (meanSquare <= 1e-12)
            return AbsoluteGateLufs;
        return Offset + 10.0 * Math.Log10(meanSquare);
    }

    private static double Mean(List<double> values)
    {
        double s = 0;
        foreach (var v in values)
            s += v;
        return s / values.Count;
    }

    private static double Sample(WavPcm pcm, int frame, int channel)
    {
        var index = frame * pcm.Channels + channel;
        if (pcm.BitsPerSample == 32
            && (pcm.AudioFormat == 3 || pcm.AudioFormat == unchecked((short)0xFFFE)))
        {
            var offset = index * 4;
            if (offset + 4 > pcm.Data.Length)
                return 0;
            return BitConverter.ToSingle(pcm.Data, offset);
        }

        if (pcm.BitsPerSample == 16)
        {
            var offset = index * 2;
            if (offset + 2 > pcm.Data.Length)
                return 0;
            return BitConverter.ToInt16(pcm.Data, offset) / 32768.0;
        }

        return 0;
    }

    private static Biquad HighPass(int fs, double f0, double q)
    {
        var w0 = 2 * Math.PI * f0 / fs;
        var cos = Math.Cos(w0);
        var sin = Math.Sin(w0);
        var alpha = sin / (2 * q);
        var b0 = (1 + cos) / 2;
        var b1 = -(1 + cos);
        var b2 = (1 + cos) / 2;
        var a0 = 1 + alpha;
        var a1 = -2 * cos;
        var a2 = 1 - alpha;
        return new Biquad(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    private static Biquad HighShelf(int fs, double f0, double q, double dbGain)
    {
        var a = Math.Pow(10, dbGain / 40);
        var w0 = 2 * Math.PI * f0 / fs;
        var cos = Math.Cos(w0);
        var sin = Math.Sin(w0);
        var alpha = sin / (2 * q);
        var twoSqrtAAlpha = 2 * Math.Sqrt(a) * alpha;
        var b0 = a * ((a + 1) + (a - 1) * cos + twoSqrtAAlpha);
        var b1 = -2 * a * ((a - 1) + (a + 1) * cos);
        var b2 = a * ((a + 1) + (a - 1) * cos - twoSqrtAAlpha);
        var a0 = (a + 1) - (a - 1) * cos + twoSqrtAAlpha;
        var a1 = 2 * ((a - 1) - (a + 1) * cos);
        var a2 = (a + 1) - (a - 1) * cos - twoSqrtAAlpha;
        return new Biquad(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    private sealed class Biquad
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;
        private double _z1, _z2;

        public Biquad(double b0, double b1, double b2, double a1, double a2)
        {
            _b0 = b0;
            _b1 = b1;
            _b2 = b2;
            _a1 = a1;
            _a2 = a2;
        }

        public double Process(double x)
        {
            var y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }
    }
}
