using FollowMe.Reader;

namespace FollowMe.MultiBox.State;

/// <summary>
/// Snapshot of the follower's own position, read from the follower's local screen.
/// </summary>
public sealed record FollowerState(
    PlayerPositionSnapshot Position,
    DateTimeOffset Timestamp)
{
    public bool IsStale(double staleThresholdSeconds) =>
        (DateTimeOffset.UtcNow - Timestamp).TotalSeconds > staleThresholdSeconds;

    public static FollowerState Synthetic() => new(
        new PlayerPositionSnapshot(115f, 0f, 205f),
        DateTimeOffset.UtcNow);
}
