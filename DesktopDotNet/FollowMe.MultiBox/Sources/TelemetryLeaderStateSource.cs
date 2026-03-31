using FollowMe.MultiBox.Interfaces;
using FollowMe.MultiBox.State;
using FollowMe.Reader;

namespace FollowMe.MultiBox.Sources;

/// <summary>
/// Reads leader state from a TelemetryAggregate populated by the existing screen-capture pipeline.
/// Call Poll() periodically (e.g. after each screen decode) to update Current.
/// Thread-safe: Updated event is raised on the calling thread.
/// </summary>
public sealed class TelemetryLeaderStateSource : ILeaderStateSource
{
    private readonly TelemetryAggregate _aggregate;
    private byte _lastPositionSequence = 0xFF;
    private byte _lastMultiBoxSequence = 0xFF;
    private LeaderState? _current;

    public event Action<LeaderState>? Updated;

    public LeaderState? Current => _current;

    public TelemetryLeaderStateSource(TelemetryAggregate aggregate)
    {
        _aggregate = aggregate;
    }

    /// <summary>
    /// Check aggregate for new frames. If both position and multibox state are present
    /// and at least one has a new sequence number, build and emit a LeaderState.
    /// </summary>
    public void Poll()
    {
        var posFrame = _aggregate.PositionFrame;
        var mbFrame  = _aggregate.MultiBoxFrame;

        if (posFrame is null) return;

        var posSeq = posFrame.Header.Sequence;
        var mbSeq  = mbFrame?.Header.Sequence ?? 0xFF;

        var posChanged = posSeq != _lastPositionSequence;
        var mbChanged  = mbFrame is not null && mbSeq != _lastMultiBoxSequence;

        if (!posChanged && !mbChanged) return;

        _lastPositionSequence = posSeq;
        if (mbFrame is not null) _lastMultiBoxSequence = mbSeq;

        var state = new LeaderState(
            posFrame.Payload,
            mbFrame?.Payload ?? MultiBoxStateSnapshot.Empty,
            _aggregate.PositionUpdatedAtUtc ?? DateTimeOffset.UtcNow);

        _current = state;
        Updated?.Invoke(state);
    }
}
