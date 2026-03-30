using System.Text.Json;
using System.Text.Json.Serialization;

namespace FollowMe.MultiBox.Config;

/// <summary>
/// All runtime configuration for the multibox subsystem.
/// Loaded from multibox.json in the working directory; defaults are safe to use without a config file.
/// </summary>
public sealed record MultiBoxConfig
{
    // ── Identity ──────────────────────────────────────────────────────────────
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MultiBoxMode Mode { get; init; } = MultiBoxMode.Off;

    /// <summary>Character name of the leader. Used for /follow and /assist commands.</summary>
    public string LeaderName { get; init; } = "Leader";

    // ── Network ───────────────────────────────────────────────────────────────
    /// <summary>TCP port used for leader→follower relay.</summary>
    public int TcpPort { get; init; } = 7742;

    /// <summary>Leader IP or hostname (follower mode only).</summary>
    public string LeaderHost { get; init; } = "127.0.0.1";

    // ── Movement ──────────────────────────────────────────────────────────────
    /// <summary>Start moving when distance to leader exceeds this (world units).</summary>
    public float FollowDistance { get; init; } = 8.0f;

    /// <summary>Stop moving when distance falls to or below this (world units).</summary>
    public float StopDistance { get; init; } = 3.5f;

    /// <summary>Re-issue /follow command every this many milliseconds (0 = disabled).</summary>
    public int FollowCommandIntervalMs { get; init; } = 2000;

    /// <summary>Enable coordinate-based movement override when /follow breaks.</summary>
    public bool EnableCoordinateMovement { get; init; } = true;

    // ── Target assist ─────────────────────────────────────────────────────────
    public bool EnableTargetAssist { get; init; } = true;

    /// <summary>Minimum ms between consecutive /assist injections.</summary>
    public int TargetAssistDebounceMs { get; init; } = 800;

    // ── Capture ───────────────────────────────────────────────────────────────
    /// <summary>Interval between screen-reader polls on the follower side (ms).</summary>
    public int FollowerPollIntervalMs { get; init; } = 200;

    // ── Telemetry ─────────────────────────────────────────────────────────────
    /// <summary>Consider leader state stale after this many seconds without update.</summary>
    public double LeaderStateStaleSeconds { get; init; } = 3.0;

    // ── Diagnostics ───────────────────────────────────────────────────────────
    public bool VerboseLogging { get; init; } = false;

    // ── Factory methods ───────────────────────────────────────────────────────

    public static MultiBoxConfig Default => new();

    public static MultiBoxConfig LoadOrDefault(string path = "multibox.json")
    {
        if (!File.Exists(path))
        {
            return Default;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MultiBoxConfig>(json, JsonOptions) ?? Default;
        }
        catch
        {
            return Default;
        }
    }

    public void Save(string path = "multibox.json")
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
