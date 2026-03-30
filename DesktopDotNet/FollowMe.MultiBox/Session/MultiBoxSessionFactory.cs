using FollowMe.MultiBox.Config;
using FollowMe.MultiBox.Controllers;
using FollowMe.MultiBox.Sources;
using FollowMe.MultiBox.Transport;
using FollowMe.Reader;

namespace FollowMe.MultiBox.Session;

/// <summary>
/// Wires together all MultiBox components according to MultiBoxConfig.Mode.
/// Returns the concrete session; callers use it via StartAsync / DisposeAsync.
/// </summary>
public static class MultiBoxSessionFactory
{
    /// <summary>
    /// Create a leader session. The caller must also run the existing TelemetryAggregate
    /// polling loop (screen capture) and call leaderSource.Poll() after each frame.
    /// </summary>
    public static (MultiBoxLeaderSession Session, TelemetryLeaderStateSource Source)
        CreateLeader(MultiBoxConfig config, TelemetryAggregate aggregate)
    {
        var source = new TelemetryLeaderStateSource(aggregate);
        var transport = new TcpLeaderBroadcast(config.TcpPort, config.VerboseLogging);
        var session = new MultiBoxLeaderSession(source, transport, config);
        return (session, source);
    }

    /// <summary>
    /// Create a follower session. The caller must also run the local screen-capture loop
    /// and call followerSource.Poll() after each frame to keep follower position current.
    /// </summary>
    public static (MultiBoxFollowerSession Session, TelemetryFollowerStateSource FollowerSource)
        CreateFollower(MultiBoxConfig config, TelemetryAggregate localAggregate)
    {
        var followerSource = new TelemetryFollowerStateSource(localAggregate);
        var transport = new TcpFollowerReceive(config.LeaderHost, config.TcpPort, config.VerboseLogging);
        var movement = new Win32MovementController();
        var target = new Win32TargetController(config);
        var session = new MultiBoxFollowerSession(transport, followerSource, movement, target, config);
        return (session, followerSource);
    }
}
