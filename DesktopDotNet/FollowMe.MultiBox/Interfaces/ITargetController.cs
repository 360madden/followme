using FollowMe.MultiBox.State;

namespace FollowMe.MultiBox.Interfaces;

/// <summary>
/// Abstraction for issuing target-assist commands on the follower.
/// Win32 implementation injects keystrokes; mock implementation records calls.
/// </summary>
public interface ITargetController : IDisposable
{
    /// <summary>
    /// Called when new leader state arrives. Implementation decides whether to
    /// fire /assist based on debounce and target change detection.
    /// </summary>
    void Update(LeaderState leader);

    string? LastAssistedTarget { get; }
    DateTimeOffset? LastAssistTime { get; }
}
