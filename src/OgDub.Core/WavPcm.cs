using System.Buffers.Binary;

namespace OgDub.Core;

public sealed class WavPcm
{
    public int SampleRate { get; init; }
    public short Channels { get; init; }
    public short BitsPerSample { get; init; }
    public short AudioFormat { get; init; }
    public byte[] Data { get; init; } = [];

    public short BlockAlign => (short)(Channels * (BitsPerSample / 8));
    public int AverageBytesPerSecond => SampleRate * BlockAlign;

    public bool SameFormat(WavPcm other) =>
        SampleRate == other.SampleRate
        && Channels == other.Channels
        && BitsPerSample == other.BitsPerSample
        && AudioFormat == other.AudioFormat;

    public static WavPcm Read(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        ReadExact(stream, header);
        if (header[0] != (byte)'R' || header[1] != (byte)'I' || header[2] != (byte)'F' || header[3] != (byte)'F')
            throw new InvalidOperationException("Not a WAV cassette.");
        if (header[8] != (byte)'W' || header[9] != (byte)'A' || header[10] != (byte)'V' || header[11] != (byte)'E')
            throw new InvalidOperationException("Not a WAV cassette.");

        var audioFormat = (short)1;
        var channels = (short)1;
        var sampleRate = 44100;
        var bits = (short)16;
        byte[]? data = null;

        Span<byte> chunk = stackalloc byte[8];
        while (stream.Position + 8 <= stream.Length)
        {
            ReadExact(stream, chunk);
            var id = System.Text.Encoding.ASCII.GetString(chunk[..4]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
            if (size < 0)
                throw new InvalidOperationException("Broken WAV chunk.");

            var payload = new byte[size];
            ReadExact(stream, payload);
            if ((size & 1) == 1 && stream.Position < stream.Length)
                stream.ReadByte();

            if (id == "fmt ")
            {
                audioFormat = BinaryPrimitives.ReadInt16LittleEndian(payload);
                channels = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4));
                bits = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(14));
            }
            else if (id == "data")
            {
                data = payload;
                break;
            }
        }

        if (data is null)
            throw new InvalidOperationException("WAV has no data chunk.");

        return new WavPcm
        {
            AudioFormat = audioFormat,
            Channels = channels,
            SampleRate = sampleRate,
            BitsPerSample = bits,
            Data = data
        };
    }

    public static void Write(Stream stream, WavPcm format, byte[] pcm)
    {
        var fmtSize = 16;
        var riffSize = 4 + 8 + fmtSize + 8 + pcm.Length;
        Span<byte> buf = stackalloc byte[4];

        stream.Write("RIFF"u8);
        BinaryPrimitives.WriteInt32LittleEndian(buf, riffSize);
        stream.Write(buf);
        stream.Write("WAVE"u8);
        stream.Write("fmt "u8);
        BinaryPrimitives.WriteInt32LittleEndian(buf, fmtSize);
        stream.Write(buf);
        BinaryPrimitives.WriteInt16LittleEndian(buf[..2], format.AudioFormat);
        stream.Write(buf[..2]);
        BinaryPrimitives.WriteInt16LittleEndian(buf[..2], format.Channels);
        stream.Write(buf[..2]);
        BinaryPrimitives.WriteInt32LittleEndian(buf, format.SampleRate);
        stream.Write(buf);
        BinaryPrimitives.WriteInt32LittleEndian(buf, format.AverageBytesPerSecond);
        stream.Write(buf);
        BinaryPrimitives.WriteInt16LittleEndian(buf[..2], format.BlockAlign);
        stream.Write(buf[..2]);
        BinaryPrimitives.WriteInt16LittleEndian(buf[..2], format.BitsPerSample);
        stream.Write(buf[..2]);
        stream.Write("data"u8);
        BinaryPrimitives.WriteInt32LittleEndian(buf, pcm.Length);
        stream.Write(buf);
        stream.Write(pcm);
    }

    private static void ReadExact(Stream stream, Span<byte> dest)
    {
        var n = stream.Read(dest);
        if (n != dest.Length)
            throw new InvalidOperationException("WAV ended early.");
    }
}
