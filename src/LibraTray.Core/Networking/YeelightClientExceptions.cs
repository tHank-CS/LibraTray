namespace LibraTray.Core.Networking;

public sealed class YeelightDisconnectedException : IOException
{
    public YeelightDisconnectedException()
        : base("The Yeelight connection is not available.")
    {
    }

    public YeelightDisconnectedException(string message)
        : base(message)
    {
    }

    public YeelightDisconnectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class YeelightCommandException : Exception
{
    public YeelightCommandException()
        : this("The device rejected the command.")
    {
    }

    public YeelightCommandException(string message)
        : base(message)
    {
    }

    public YeelightCommandException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public YeelightCommandException(long requestId, int errorCode, string message)
        : base($"Yeelight command {requestId} failed with error {errorCode}: {message}")
    {
        RequestId = requestId;
        ErrorCode = errorCode;
        DeviceMessage = message;
    }

    public long RequestId { get; }

    public int ErrorCode { get; }

    public string? DeviceMessage { get; }
}
