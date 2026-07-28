namespace LibraTray.Core.Protocol;

/// <summary>
/// Incrementally extracts CRLF-delimited protocol frames from an arbitrary TCP
/// byte stream. Returned arrays are independent from the framer's buffer.
/// </summary>
public sealed class CrlfMessageFramer
{
    public const int DefaultMaximumFrameBytes = 64 * 1024;

    private readonly byte[] _frameBuffer;
    private int _frameLength;
    private bool _pendingCarriageReturn;

    public CrlfMessageFramer(int maximumFrameBytes = DefaultMaximumFrameBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFrameBytes);

        MaximumFrameBytes = maximumFrameBytes;
        _frameBuffer = new byte[maximumFrameBytes];
    }

    public int MaximumFrameBytes { get; }

    public int BufferedByteCount => _frameLength + (_pendingCarriageReturn ? 1 : 0);

    public IReadOnlyList<byte[]> Append(ReadOnlySpan<byte> bytes)
    {
        var frames = new List<byte[]>();

        foreach (byte value in bytes)
        {
            if (_pendingCarriageReturn)
            {
                if (value == (byte)'\n')
                {
                    frames.Add(_frameBuffer.AsSpan(0, _frameLength).ToArray());
                    _frameLength = 0;
                    _pendingCarriageReturn = false;
                    continue;
                }

                AppendPayloadByte((byte)'\r');
                _pendingCarriageReturn = false;
            }

            if (value == (byte)'\r')
            {
                _pendingCarriageReturn = true;
            }
            else
            {
                AppendPayloadByte(value);
            }
        }

        return frames;
    }

    public void Reset()
    {
        _frameLength = 0;
        _pendingCarriageReturn = false;
    }

    private void AppendPayloadByte(byte value)
    {
        if (_frameLength >= MaximumFrameBytes)
        {
            Reset();
            throw new YeelightFrameTooLargeException(MaximumFrameBytes);
        }

        _frameBuffer[_frameLength] = value;
        _frameLength++;
    }
}
