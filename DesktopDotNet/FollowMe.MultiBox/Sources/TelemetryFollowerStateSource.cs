using FollowMe.MultiBox.Interfaces;
using FollowMe.MultiBox.State;
using FollowMe.Reader;

namespace FollowMe.MultiBox.Sources;

/// <summary>
/// Reads the follower's own position from a TelemetryAggregate on the follower machine.
/// Call Poll() after each screen decode cycle.
/// </summary>
public sealed class TelemetryFollowerStateSource : IFollowerStateSource
{
    private readonly TelemetryAggregate _aggregate;
    private byte _lastSequence = 0xFF;
    private FollowerState? _current;

    public event Action<FollowerState>? Updated;

    public FollowerState? Current => _current;

    public TelemetryFollowerStateSource(TelemetryAggregate aggregate)
    {
        _aggregate = aggregate;
    }

    public void Poll()
    {
        var posFrame = _aggregate.PositionFrame;
        if (posFrame is null) return;

        var seq = posFrame.Header.Sequence;
        if (seq == _lastSequence) return;

        _lastSequence = seq;
        var state = new FollowerState(
            posFrame.Payload,
            _aggregate.PositionUpdatedAtUtc ?? DateTimeOffset.UtcNow);

        _current = state;
        Updated?.Invoke(state);
    }
}
