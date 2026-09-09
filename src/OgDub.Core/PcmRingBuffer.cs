namespace OgDub.Core;

public sealed class PcmRingBuffer
{
    private readonly byte[] _buf;
    private readonly int _blockAlign;
    private readonly object _gate = new();
    private int _write;
    private int _count;

    public PcmRingBuffer(int capacityBytes, int blockAlign)
    {
        _blockAlign = Math.Max(1, blockAlign);
        var cap = capacityBytes < _blockAlign
            ? _blockAlign
            : (capacityBytes / _blockAlign) * _blockAlign;
        _buf = new byte[cap];
    }

    public int Capacity => _buf.Length;

    public int Count
    {
        get
        {
            lock (_gate)
                return _count;
        }
    }

    public void Write(ReadOnlySpan<byte> src)
    {
        var remaining = src.Length - (src.Length % _blockAlign);
        if (remaining <= 0)
            return;

        lock (_gate)
        {
            var offset = 0;
            while (remaining > 0)
            {
                var n = Math.Min(_buf.Length - _write, remaining);
                src.Slice(offset, n).CopyTo(_buf.AsSpan(_write, n));
                _write = (_write + n) % _buf.Length;
                _count = Math.Min(_buf.Length, _count + n);
                offset += n;
                remaining -= n;
            }
        }
    }

    public byte[] Snapshot()
    {
        lock (_gate)
        {
            var result = new byte[_count];
            if (_count == 0)
                return result;

            var start = (_write - _count + _buf.Length) % _buf.Length;
            var first = Math.Min(_count, _buf.Length - start);
            Buffer.BlockCopy(_buf, start, result, 0, first);
            if (first < _count)
                Buffer.BlockCopy(_buf, 0, result, first, _count - first);
            return result;
        }
    }

    public TimeSpan FilledDuration(int averageBytesPerSecond)
    {
        if (averageBytesPerSecond <= 0)
            return TimeSpan.Zero;

        lock (_gate)
            return TimeSpan.FromSeconds(_count / (double)averageBytesPerSecond);
    }
}
