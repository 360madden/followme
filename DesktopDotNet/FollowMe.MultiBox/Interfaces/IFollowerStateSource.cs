using FollowMe.MultiBox.State;

namespace FollowMe.MultiBox.Interfaces;

/// <summary>
/// Abstraction over where the follower's own position is obtained (local screen capture, mock, etc.)
/// </summary>
public interface IFollowerStateSource
{
    FollowerState? Current { get; }
    event Action<FollowerState> Updated;
}
