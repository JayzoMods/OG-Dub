using System.Buffers.Binary;
using System.Text;

namespace OgDub.Core;

public sealed class BextFields
{
    public string Description { get; init; } = "";
    public string Originator { get; init; } = ProductInfo.Originator;
    public string OriginatorReference { get; init; } = "";
    public DateTimeOffset OriginatedAt { get; init; }
    public double? LoudnessLufs { get; init; }
    public double? PeakDbfs { get; init; }
}

public static class BwfFile
{
    public const short UnknownLoudness = 0x7FFF;

    public static bool HasBext(Stream stream)
    {
        if (stream.Length < 12)
            return false;

        Span<byte> header = stackalloc byte[12];
        ReadExact(stream, header);
        if (header[0] != (byte)'R' || header[8] != (byte)'W')
            return false;

        Span<byte> chunk = stackalloc byte[8];
        while (stream.Position + 8 <= stream.Length)
        {
            ReadExact(stream, chunk);
            var id = Encoding.ASCII.GetString(chunk[..4]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
            if (size < 0)
                return false;
            if (id == "bext")
                return true;

            var skip = size + (size & 1);
            if (stream.Position + skip > stream.Length)
                return false;
            stream.Seek(skip, SeekOrigin.Current);
        }

        return false;
    }

    public static bool HasCue(Stream stream)
    {
        if (stream.Length < 12)
            return false;

        Span<byte> header = stackalloc byte[12];
        ReadExact(stream, header);
        if (header[0] != (byte)'R' || header[8] != (byte)'W')
            return false;

        Span<byte> chunk = stackalloc byte[8];
        while (stream.Position + 8 <= stream.Length)
        {
            ReadExact(stream, chunk);
            var id = Encoding.ASCII.GetString(chunk[..4]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
            if (size < 0)
                return false;
            if (id == "cue ")
                return true;

            var skip = size + (size & 1);
            if (stream.Position + skip > stream.Length)
                return false;
            stream.Seek(skip, SeekOrigin.Current);
        }

        return false;
    }

    public static void AppendCues(Stream stream, IReadOnlyList<TapeMarker> markers, int sampleRate)
    {
        if (markers.Count == 0 || sampleRate <= 0)
            return;
        if (stream.Length < 12)
            throw new InvalidOperationException("Not a WAV cassette.");

        stream.Seek(0, SeekOrigin.Begin);
        if (HasCue(stream))
            return;

        var cuePayload = BuildCue(markers, sampleRate);
        var listPayload = BuildAdtl(markers);
        WriteChunk(stream, "cue "u8, cuePayload);
        WriteChunk(stream, "LIST"u8, listPayload);
        AddRiffSize(stream, ChunkBytes(cuePayload.Length) + ChunkBytes(listPayload.Length));
    }

    public static bool TryAppendCues(string path, IReadOnlyList<TapeMarker> markers, int sampleRate)
    {
        if (markers.Count == 0 || sampleRate <= 0)
            return true;

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            AppendCues(stream, markers, sampleRate);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void AppendBext(Stream stream, BextFields fields)
    {
        if (stream.Length < 12)
            throw new InvalidOperationException("Not a WAV cassette.");

        stream.Seek(0, SeekOrigin.Begin);
        if (HasBext(stream))
            return;

        var payload = BuildBext(fields);
        var size = payload.Length;
        var pad = (size & 1) == 1;
        stream.Seek(0, SeekOrigin.End);
        stream.Write("bext"u8);
        Span<byte> n = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(n, size);
        stream.Write(n);
        stream.Write(payload);
        if (pad)
            stream.WriteByte(0);

        stream.Seek(4, SeekOrigin.Begin);
        Span<byte> riff = stackalloc byte[4];
        ReadExact(stream, riff);
        var total = BinaryPrimitives.ReadInt32LittleEndian(riff) + 8 + size + (pad ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(riff, total);
        stream.Seek(4, SeekOrigin.Begin);
        stream.Write(riff);
    }

    public static bool TryAppend(string path, BextFields fields)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            AppendBext(stream, fields);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static short ToHundredths(double? lufs)
    {
        if (lufs is null || double.IsNaN(lufs.Value) || double.IsInfinity(lufs.Value))
            return UnknownLoudness;
        var scaled = (int)Math.Round(lufs.Value * 100);
        if (scaled >= UnknownLoudness || scaled < short.MinValue)
            return UnknownLoudness;
        return (short)scaled;
    }

    private static byte[] BuildBext(BextFields fields)
    {
        var history = Encoding.ASCII.GetBytes("A=PCM,T=OG Dub\r\n\0");
        var payload = new byte[602 + history.Length];
        WriteAscii(payload.AsSpan(0, 256), fields.Description);
        WriteAscii(payload.AsSpan(256, 32), string.IsNullOrWhiteSpace(fields.Originator) ? ProductInfo.Originator : fields.Originator);
        WriteAscii(payload.AsSpan(288, 32), fields.OriginatorReference);
        var (date, time) = CassetteNaming.BextStamp(fields.OriginatedAt);
        WriteAscii(payload.AsSpan(320, 10), date);
        WriteAscii(payload.AsSpan(330, 8), time);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(346, 2), 2);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(412, 2), ToHundredths(fields.LoudnessLufs));
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(414, 2), UnknownLoudness);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(416, 2), ToHundredths(fields.PeakDbfs));
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(418, 2), UnknownLoudness);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(420, 2), UnknownLoudness);
        history.CopyTo(payload.AsSpan(602));
        return payload;
    }

    private static byte[] BuildCue(IReadOnlyList<TapeMarker> markers, int sampleRate)
    {
        var n = Math.Min(markers.Count, TapeCounter.MaxMarks);
        var payload = new byte[4 + 24 * n];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)n);
        for (var i = 0; i < n; i++)
        {
            var point = payload.AsSpan(4 + i * 24, 24);
            var samples = SampleOffset(markers[i].OffsetSeconds, sampleRate);
            BinaryPrimitives.WriteUInt32LittleEndian(point, (uint)(i + 1));
            BinaryPrimitives.WriteUInt32LittleEndian(point[4..], samples);
            "data"u8.CopyTo(point[8..]);
            BinaryPrimitives.WriteUInt32LittleEndian(point[20..], samples);
        }

        return payload;
    }

    private static byte[] BuildAdtl(IReadOnlyList<TapeMarker> markers)
    {
        var n = Math.Min(markers.Count, TapeCounter.MaxMarks);
        var labels = new byte[n][];
        var inner = 4;
        for (var i = 0; i < n; i++)
        {
            var text = markers[i].Label;
            if (string.IsNullOrWhiteSpace(text))
                text = TapeCounter.Lcd(markers[i].Counter);
            var ascii = Encoding.ASCII.GetBytes(CassetteNaming.Collapse(text));
            if (ascii.Length > 32)
                ascii = ascii[..32];
            var body = 4 + ascii.Length + 1;
            if ((body & 1) == 1)
                body++;
            var chunk = new byte[8 + body];
            "labl"u8.CopyTo(chunk);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), body);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(8), (uint)(i + 1));
            ascii.CopyTo(chunk.AsSpan(12));
            labels[i] = chunk;
            inner += chunk.Length;
        }

        var payload = new byte[inner];
        "adtl"u8.CopyTo(payload);
        var o = 4;
        foreach (var label in labels)
        {
            label.CopyTo(payload.AsSpan(o));
            o += label.Length;
        }

        return payload;
    }

    private static uint SampleOffset(double seconds, int sampleRate)
    {
        if (seconds <= 0)
            return 0;
        var samples = Math.Round(seconds * sampleRate);
        if (samples <= 0)
            return 0;
        if (samples >= uint.MaxValue)
            return uint.MaxValue;
        return (uint)samples;
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> id, ReadOnlySpan<byte> payload)
    {
        var size = payload.Length;
        var pad = (size & 1) == 1;
        stream.Seek(0, SeekOrigin.End);
        stream.Write(id);
        Span<byte> n = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(n, size);
        stream.Write(n);
        stream.Write(payload);
        if (pad)
            stream.WriteByte(0);
    }

    private static void AddRiffSize(Stream stream, int extra)
    {
        stream.Seek(4, SeekOrigin.Begin);
        Span<byte> riff = stackalloc byte[4];
        ReadExact(stream, riff);
        var total = BinaryPrimitives.ReadInt32LittleEndian(riff) + extra;
        BinaryPrimitives.WriteInt32LittleEndian(riff, total);
        stream.Seek(4, SeekOrigin.Begin);
        stream.Write(riff);
    }

    private static int ChunkBytes(int payloadSize) => 8 + payloadSize + ((payloadSize & 1) == 1 ? 1 : 0);

    private static void WriteAscii(Span<byte> dest, string text)
    {
        dest.Clear();
        if (string.IsNullOrEmpty(text))
            return;
        var bytes = Encoding.ASCII.GetBytes(text);
        var n = Math.Min(bytes.Length, dest.Length);
        bytes.AsSpan(0, n).CopyTo(dest);
    }

    private static void ReadExact(Stream stream, Span<byte> dest)
    {
        var n = stream.Read(dest);
        if (n != dest.Length)
            throw new InvalidOperationException("WAV ended early.");
    }
}
