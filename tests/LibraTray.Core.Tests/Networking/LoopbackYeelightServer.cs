using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LibraTray.Core.Protocol;

namespace LibraTray.Core.Tests.Networking;

internal sealed class LoopbackYeelightServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private bool _disposed;

    public LoopbackYeelightServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
    }

    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

    public async Task<LoopbackYeelightConnection> AcceptAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TcpClient client = await _listener
            .AcceptTcpClientAsync(cancellationToken)
            .ConfigureAwait(false);
        client.NoDelay = true;
        return new LoopbackYeelightConnection(client);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _listener.Stop();
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

internal sealed class LoopbackYeelightConnection : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly CrlfMessageFramer _framer = new();
    private readonly Queue<byte[]> _receivedFrames = new();
    private bool _disposed;

    public LoopbackYeelightConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public async Task<ReceivedRequest> ReadRequestAsync(
        CancellationToken cancellationToken)
    {
        byte[] frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);

        using JsonDocument document = JsonDocument.Parse(frame);
        JsonElement root = document.RootElement;
        int id = root.GetProperty("id").GetInt32();
        string method = root.GetProperty("method").GetString()
            ?? throw new InvalidDataException("The request method is null.");

        return new ReceivedRequest(id, method);
    }

    public Task WriteTextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return WriteBytesAsync(bytes, cancellationToken);
    }

    public async Task WriteBytesAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _stream
            .WriteAsync(bytes, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _client.Dispose();
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken)
    {
        byte[] readBuffer = new byte[1024];

        while (_receivedFrames.Count == 0)
        {
            int bytesRead = await _stream
                .ReadAsync(readBuffer, cancellationToken)
                .ConfigureAwait(false);

            if (bytesRead == 0)
            {
                throw new EndOfStreamException(
                    "The client closed before sending a complete request.");
            }

            foreach (byte[] frame in _framer.Append(
                         readBuffer.AsSpan(0, bytesRead)))
            {
                _receivedFrames.Enqueue(frame);
            }
        }

        return _receivedFrames.Dequeue();
    }
}

internal sealed record ReceivedRequest(int Id, string Method);
