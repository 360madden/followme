using FollowMe.MultiBox.Config;
using FollowMe.MultiBox.Interfaces;
using FollowMe.MultiBox.State;

namespace FollowMe.MultiBox.Session;

/// <summary>
/// Orchestrates the follower side:
///   IMultiBoxTransport (TCP receive) → IMovementController + ITargetController
///   IFollowerStateSource (local screen) feeds position to movement controller.
///
/// Separation of concerns:
///   - Transport: receives LeaderState from network
///   - FollowerSource: reads follower's own position from local screen
///   - MovementController: drives W/A/D keys and /follow commands
///   - TargetController: injects /assist on target change
/// </summary>
public sealed class MultiBoxFollowerSession : IAsyncDisposable
{
    private readonly IMultiBoxTransport _transport;
    private readonly IFollowerStateSource _followerSource;
    private readonly IMovementController _movement;
    private readonly ITargetController _target;
    private readonly MultiBoxConfig _config;
    private CancellationTokenSource? _cts;
    private Task? _transportTask;
    private LeaderState? _lastLeaderState;
    private DateTimeOffset _lastUpdateTime;

    public string TransportStatus => _transport.StatusDescription;
    public bool IsConnectedToLeader => _transport.IsConnected;
    public LeaderState? LastLeaderState => _lastLeaderState;
    public FollowerState? CurrentFollowerState => _followerSource.Current;
    public string? LastAssistedTarget => _target.LastAssistedTarget;

    public MultiBoxFollowerSession(
        IMultiBoxTransport transport,
        IFollowerStateSource followerSource,
        IMovementController movement,
        ITargetController target,
        MultiBoxConfig config)
    {
        _transport = transport;
        _followerSource = followerSource;
        _movement = movement;
        _target = target;
        _config = config;
    }

    public Task StartAsync(CancellationToken externalCt = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var token = _cts.Token;

        _transport.LeaderStateReceived += OnLeaderStateReceived;
        _followerSource.Updated += OnFollowerStateUpdated;

        _transportTask = _transport.RunAsync(token);
        return _transportTask;
    }

    public async ValueTask DisposeAsync()
    {
        _transport.LeaderStateReceived -= OnLeaderStateReceived;
        _followerSource.Updated -= OnFollowerStateUpdated;

        _movement.StopMovement();
        _cts?.Cancel();

        if (_transportTask is not null)
        {
            try { await _transportTask; }
            catch (OperationCanceledException) { }
        }

        await _transport.DisposeAsync();
        _movement.Dispose();
        _target.Dispose();
        _cts?.Dispose();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnLeaderStateReceived(LeaderState state)
    {
        _lastLeaderState = state;
        _lastUpdateTime = DateTimeOffset.UtcNow;

        // Fire target assist immediately on receiving new leader state
        _target.Update(state);

        // Trigger movement update if we have follower position
        var follower = _followerSource.Current;
        if (follower is not null)
        {
            _movement.Update(state, follower, _config);
        }
    }

    private void OnFollowerStateUpdated(FollowerState follower)
    {
        // Re-trigger movement update with latest follower position
        var leader = _lastLeaderState;
        if (leader is null || leader.IsStale(_config.LeaderStateStaleSeconds)) return;

        _movement.Update(leader, follower, _config);
    }
}
