using NAudio.Wave;

namespace OgDub.Audio;

internal static class PcmPeak
{
    public static float Max(byte[] buffer, int count, WaveFormat format)
    {
        var peak = 0f;
        var n = Math.Min(count, buffer.Length);
        if (IsFloat32(format))
        {
            for (var i = 0; i + 4 <= n; i += 4)
            {
                var sample = Math.Abs(BitConverter.ToSingle(buffer, i));
                if (sample > peak)
                    peak = sample;
            }
        }
        else if (format.BitsPerSample == 16)
        {
            for (var i = 0; i + 2 <= n; i += 2)
            {
                var sample = Math.Abs(BitConverter.ToInt16(buffer, i) / 32768f);
                if (sample > peak)
                    peak = sample;
            }
        }

        return peak;
    }

    public static bool IsFloat32(WaveFormat format) =>
        format.BitsPerSample == 32
        && (format.Encoding == WaveFormatEncoding.IeeeFloat
            || format.Encoding == WaveFormatEncoding.Extensible);
}
