using FollowMe.MultiBox.Config;
using FollowMe.MultiBox.Interfaces;
using FollowMe.MultiBox.State;

namespace FollowMe.MultiBox.Session;

/// <summary>
/// Orchestrates the leader side:
///   ILeaderStateSource → IMultiBoxTransport (TCP broadcast)
///
/// Leader reads its own position/state from the screen-capture pipeline,
/// then relays it to connected followers via TCP.
/// </summary>
public sealed class MultiBoxLeaderSession : IAsyncDisposable
{
    private readonly ILeaderStateSource _source;
    private readonly IMultiBoxTransport _transport;
    private readonly MultiBoxConfig _config;
    private CancellationTokenSource? _cts;
    private Task? _transportTask;

    public MultiBoxLeaderSession(
        ILeaderStateSource source,
        IMultiBoxTransport transport,
        MultiBoxConfig config)
    {
        _source = source;
        _transport = transport;
        _config = config;
    }

    public string TransportStatus => _transport.StatusDescription;
    public bool IsFollowerConnected => _transport.IsConnected;
    public LeaderState? LastBroadcastState => _source.Current;

    public Task StartAsync(CancellationToken externalCt = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var token = _cts.Token;

        _source.Updated += OnLeaderStateUpdated;
        _transportTask = _transport.RunAsync(token);

        return _transportTask;
    }

    public async ValueTask DisposeAsync()
    {
        _source.Updated -= OnLeaderStateUpdated;
        _cts?.Cancel();
        if (_transportTask is not null)
        {
            try { await _transportTask; }
            catch (OperationCanceledException) { }
        }
        await _transport.DisposeAsync();
        _cts?.Dispose();
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void OnLeaderStateUpdated(LeaderState state)
    {
        if (_cts?.IsCancellationRequested == true) return;
        _ = _transport.SendLeaderStateAsync(state, _cts?.Token ?? default);
    }
}
