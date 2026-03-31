using FollowMe.MultiBox.Config;
using FollowMe.MultiBox.State;

namespace FollowMe.MultiBox.Interfaces;

/// <summary>
/// Abstraction for driving the follower's movement toward the leader.
/// Implementations may use Win32 SendInput, named pipes, or be mocked for testing.
/// </summary>
public interface IMovementController : IDisposable
{
    /// <summary>
    /// Called periodically with current state. Implementation decides whether to move,
    /// steer, or stop based on distance and bearing.
    /// </summary>
    void Update(LeaderState leader, FollowerState follower, MultiBoxConfig config);

    /// <summary>Issue a /follow command targeting the leader (may do nothing if not Win32).</summary>
    void IssueFollowCommand(string leaderName);

    /// <summary>Immediately stop all movement.</summary>
    void StopMovement();

    MovementStatus CurrentStatus { get; }
}

public enum MovementStatus
{
    Idle,
    Following,
    Moving,
    Stopped
}
