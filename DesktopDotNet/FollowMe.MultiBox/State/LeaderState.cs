using FollowMe.Reader;

namespace FollowMe.MultiBox.State;

/// <summary>
/// Snapshot of everything the leader broadcasts to followers.
/// Immutable — all fields populated at capture time.
/// </summary>
public sealed record LeaderState(
    PlayerPositionSnapshot Position,
    MultiBoxStateSnapshot MultiBox,
    DateTimeOffset Timestamp)
{
    public bool IsStale(double staleThresholdSeconds) =>
        (DateTimeOffset.UtcNow - Timestamp).TotalSeconds > staleThresholdSeconds;

    public static LeaderState Synthetic() => new(
        new PlayerPositionSnapshot(100f, 0f, 200f),
        new MultiBoxStateSnapshot(0b00000110, "TargetMob"),  // HasTarget + TargetHostile
        DateTimeOffset.UtcNow);
}
