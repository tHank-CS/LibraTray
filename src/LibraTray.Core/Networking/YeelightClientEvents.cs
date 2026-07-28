using LibraTray.Core.Protocol;

namespace LibraTray.Core.Networking;

public sealed class YeelightNotificationEventArgs : EventArgs
{
    public YeelightNotificationEventArgs(YeelightNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        Notification = notification;
    }

    public YeelightNotification Notification { get; }
}

public sealed class YeelightProtocolErrorEventArgs : EventArgs
{
    public YeelightProtocolErrorEventArgs(
        YeelightProtocolException exception,
        ReadOnlyMemory<byte> frame)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Exception = exception;
        Frame = frame;
    }

    public YeelightProtocolException Exception { get; }

    public ReadOnlyMemory<byte> Frame { get; }
}

public sealed class YeelightUnmatchedResponseEventArgs : EventArgs
{
    public YeelightUnmatchedResponseEventArgs(YeelightResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Response = response;
    }

    public YeelightResponse Response { get; }
}

public sealed class YeelightSubscriberErrorEventArgs : EventArgs
{
    public YeelightSubscriberErrorEventArgs(string eventName, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(exception);
        EventName = eventName;
        Exception = exception;
    }

    public string EventName { get; }

    public Exception Exception { get; }
}
