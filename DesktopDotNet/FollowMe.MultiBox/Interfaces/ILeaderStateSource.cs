using FollowMe.MultiBox.State;

namespace FollowMe.MultiBox.Interfaces;

/// <summary>
/// Abstraction over where leader state data originates (screen capture, mock, network replay, etc.)
/// </summary>
public interface ILeaderStateSource
{
    /// <summary>Most recent valid leader state; null until first frame received.</summary>
    LeaderState? Current { get; }

    /// <summary>Raised on the thread that receives a new decoded frame.</summary>
    event Action<LeaderState> Updated;
}
