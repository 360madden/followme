using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FollowMe.MultiBox.Interfaces;
using FollowMe.MultiBox.State;

namespace FollowMe.MultiBox.Transport;

/// <summary>
/// Leader-side TCP server. Listens for one follower connection at a time.
/// Sends LeaderState as newline-delimited JSON on each update.
/// Thread-safe: SendLeaderStateAsync may be called from any thread.
/// </summary>
public sealed class TcpLeaderBroadcast : IMultiBoxTransport
{
    private readonly int _port;
    private readonly bool _verbose;
    private TcpClient? _follower;
    private NetworkStream? _followerStream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _isConnected;

#pragma warning disable CS0067  // LeaderStateReceived never fired on leader side — interface requirement only
    public event Action<LeaderState>? LeaderStateReceived;
#pragma warning restore CS0067

    public bool IsConnected => _isConnected;
    public string StatusDescription => _isConnected ? $"Follower connected on port {_port}" : $"Listening on port {_port}";

    public TcpLeaderBroadcast(int port, bool verbose = false)
    {
        _port = port;
        _verbose = verbose;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        if (_verbose) Console.WriteLine($"[MultiBox Leader] TCP listening on port {_port}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (_verbose) Console.WriteLine($"[MultiBox Leader] Follower connected: {client.Client.RemoteEndPoint}");

                // Disconnect previous follower (v1 supports one follower)
                await DisconnectFollowerAsync();

                _follower = client;
                _followerStream = client.GetStream();
                _isConnected = true;

                // Keep connection alive — data is pushed by SendLeaderStateAsync
                try
                {
                    await WaitForDisconnectAsync(client, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    if (_verbose) Console.WriteLine($"[MultiBox Leader] Follower disconnected: {ex.Message}");
                }
                finally
                {
                    _isConnected = false;
                    await DisconnectFollowerAsync();
                }
            }
        }
        finally
        {
            listener.Stop();
            await DisconnectFollowerAsync();
        }
    }

    public async Task SendLeaderStateAsync(LeaderState state, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _followerStream is null) return;

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_followerStream is null) return;
            var json = JsonSerializer.Serialize(state, JsonOptions);
            var line = json + "\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            await _followerStream.WriteAsync(bytes, cancellationToken);
            await _followerStream.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            if (_verbose) Console.WriteLine($"[MultiBox Leader] Send failed: {ex.Message}");
            _isConnected = false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectFollowerAsync();
        _sendLock.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task WaitForDisconnectAsync(TcpClient client, CancellationToken cancellationToken)
    {
        // Read and discard any incoming bytes; used only to detect disconnection
        var buf = new byte[256];
        var stream = client.GetStream();
        while (!cancellationToken.IsCancellationRequested && client.Connected)
        {
            var n = await stream.ReadAsync(buf, cancellationToken);
            if (n == 0) break;  // clean disconnect
        }
    }

    private async Task DisconnectFollowerAsync()
    {
        var stream = Interlocked.Exchange(ref _followerStream, null);
        var client = Interlocked.Exchange(ref _follower, null);
        if (stream is not null) await stream.DisposeAsync();
        client?.Dispose();
        _isConnected = false;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
