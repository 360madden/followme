using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FollowMe.MultiBox.Interfaces;
using FollowMe.MultiBox.State;

namespace FollowMe.MultiBox.Transport;

/// <summary>
/// Follower-side TCP client. Connects to the leader's TcpLeaderBroadcast.
/// Deserializes newline-delimited JSON LeaderState messages and raises LeaderStateReceived.
/// Automatically reconnects on disconnection.
/// </summary>
public sealed class TcpFollowerReceive : IMultiBoxTransport
{
    private readonly string _leaderHost;
    private readonly int _port;
    private readonly bool _verbose;
    private volatile bool _isConnected;

    public event Action<LeaderState>? LeaderStateReceived;

    public bool IsConnected => _isConnected;
    public string StatusDescription => _isConnected
        ? $"Connected to leader at {_leaderHost}:{_port}"
        : $"Connecting to {_leaderHost}:{_port}…";

    public TcpFollowerReceive(string leaderHost, int port, bool verbose = false)
    {
        _leaderHost = leaderHost;
        _port = port;
        _verbose = verbose;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                if (_verbose) Console.WriteLine($"[MultiBox Follower] Connecting to {_leaderHost}:{_port}…");

                await client.ConnectAsync(_leaderHost, _port, cancellationToken);
                _isConnected = true;
                if (_verbose) Console.WriteLine($"[MultiBox Follower] Connected to leader.");

                await ReadLoopAsync(client, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_verbose) Console.WriteLine($"[MultiBox Follower] Connection error: {ex.Message}. Retrying in 3s.");
            }
            finally
            {
                _isConnected = false;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Leader side never sends — no-op on follower
    public Task SendLeaderStateAsync(LeaderState state, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _isConnected = false;
        return ValueTask.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task ReadLoopAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
        while (!cancellationToken.IsCancellationRequested && client.Connected)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (IOException)
            {
                break;  // connection closed
            }

            if (line is null) break;  // clean disconnect
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var state = JsonSerializer.Deserialize<LeaderState>(line, JsonOptions);
                if (state is not null)
                {
                    LeaderStateReceived?.Invoke(state);
                }
            }
            catch (JsonException ex)
            {
                if (_verbose) Console.WriteLine($"[MultiBox Follower] Bad JSON: {ex.Message}");
            }
        }

        _isConnected = false;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
