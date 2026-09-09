using System.Buffers.Binary;

namespace OgDub.Core;

public static class SafetyPad
{
    public const double Decibels = -12;
    public static readonly float Linear = (float)Math.Pow(10.0, Decibels / 20.0);

    public static void ApplyIeeeFloat32(Span<byte> buffer)
    {
        for (var i = 0; i + 4 <= buffer.Length; i += 4)
        {
            var slice = buffer.Slice(i, 4);
            var sample = BitConverter.ToSingle(slice);
            BitConverter.TryWriteBytes(slice, sample * Linear);
        }
    }

    public static void ApplyPcm16(Span<byte> buffer)
    {
        for (var i = 0; i + 2 <= buffer.Length; i += 2)
        {
            var slice = buffer.Slice(i, 2);
            var sample = BinaryPrimitives.ReadInt16LittleEndian(slice);
            var scaled = (int)Math.Round(sample * (double)Linear);
            scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(slice, (short)scaled);
        }
    }
}
