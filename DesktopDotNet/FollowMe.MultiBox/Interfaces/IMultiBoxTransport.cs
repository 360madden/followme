using FollowMe.MultiBox.State;

namespace FollowMe.MultiBox.Interfaces;

/// <summary>
/// Abstraction for the network transport layer (TCP, UDP, named pipe, mock, etc.)
/// Leader side: accepts outbound LeaderState; transmits to connected followers.
/// Follower side: receives inbound LeaderState; raises LeaderStateReceived.
/// </summary>
public interface IMultiBoxTransport : IAsyncDisposable
{
    /// <summary>Raised when a valid LeaderState message arrives (follower side only).</summary>
    event Action<LeaderState>? LeaderStateReceived;

    /// <summary>
    /// Start the transport. For leader: begin listening. For follower: begin connecting.
    /// Returns when the transport is stopped or the token is cancelled.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>Send the current leader state to all connected followers (leader side only).</summary>
    Task SendLeaderStateAsync(LeaderState state, CancellationToken cancellationToken = default);

    bool IsConnected { get; }
    string StatusDescription { get; }
}
