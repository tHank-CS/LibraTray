namespace LibraTray.Core.Protocol;

public class YeelightProtocolException : Exception
{
    public YeelightProtocolException()
    {
    }

    public YeelightProtocolException(string message)
        : base(message)
    {
    }

    public YeelightProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class YeelightFrameTooLargeException : YeelightProtocolException
{
    public YeelightFrameTooLargeException(int maximumFrameBytes)
        : base($"The protocol frame exceeds the {maximumFrameBytes}-byte limit.")
    {
        MaximumFrameBytes = maximumFrameBytes;
    }

    public int MaximumFrameBytes { get; }
}
