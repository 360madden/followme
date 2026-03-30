using FollowMe.MultiBox.Config;
using FollowMe.MultiBox.Sources;
using FollowMe.MultiBox.State;
using FollowMe.Reader;
using Xunit;

namespace FollowMe.Tests;

public class MultiBoxProtocolTests
{
    private readonly StripProfile _profile = StripProfiles.Default;

    // ── PlayerPosition round-trips ────────────────────────────────────────────

    [Fact]
    public void PlayerPosition_RoundTrips_PositiveCoords()
    {
        var snapshot = new PlayerPositionSnapshot(123.45f, 67.89f, 234.56f);
        AssertPositionRoundTrip(snapshot);
    }

    [Fact]
    public void PlayerPosition_RoundTrips_NegativeCoords()
    {
        var snapshot = new PlayerPositionSnapshot(-123.45f, -0.5f, -999.99f);
        AssertPositionRoundTrip(snapshot);
    }

    [Fact]
    public void PlayerPosition_RoundTrips_ZeroCoords()
    {
        AssertPositionRoundTrip(PlayerPositionSnapshot.Zero);
    }

    [Fact]
    public void PlayerPosition_RoundTrips_LargeCoords()
    {
        // Typical MMO zones are ~1000–5000 units; verify no overflow
        var snapshot = new PlayerPositionSnapshot(6500f, 200f, -6500f);
        AssertPositionRoundTrip(snapshot);
    }

    [Fact]
    public void PlayerPosition_DecodedError_IsLessThan_0_01_Units()
    {
        var original = new PlayerPositionSnapshot(3.141f, 2.718f, -1.414f);
        var bytes = FrameProtocol.BuildPlayerPositionFrameBytes(_profile.NumericId, 1, original);
        var image = ColorStripRenderer.Render(_profile, bytes);
        var validation = ColorStripAnalyzer.Analyze(image, _profile);

        var frame = Assert.IsType<PlayerPositionFrame>(validation.Frame);
        Assert.True(Math.Abs(frame.Payload.X - original.X) < 0.01f, $"X error too large: {Math.Abs(frame.Payload.X - original.X)}");
        Assert.True(Math.Abs(frame.Payload.Y - original.Y) < 0.01f, $"Y error too large: {Math.Abs(frame.Payload.Y - original.Y)}");
        Assert.True(Math.Abs(frame.Payload.Z - original.Z) < 0.01f, $"Z error too large: {Math.Abs(frame.Payload.Z - original.Z)}");
    }

    // ── MultiBoxState round-trips ─────────────────────────────────────────────

    [Theory]
    [InlineData(0b00000000, "")]             // no target
    [InlineData(0b00000001, "")]             // inCombat only
    [InlineData(0b00000110, "TargetMob")]    // hasTarget + hostile
    [InlineData(0b00000111, "BossEnemy")]    // all flags + name
    [InlineData(0b00000010, "Friendly")]     // hasTarget, not hostile
    public void MultiBoxState_RoundTrips(byte flags, string targetName)
    {
        var snapshot = new MultiBoxStateSnapshot(flags, targetName);
        var bytes = FrameProtocol.BuildMultiBoxStateFrameBytes(_profile.NumericId, 5, snapshot);
        var image = ColorStripRenderer.Render(_profile, bytes);
        var validation = ColorStripAnalyzer.Analyze(image, _profile);

        Assert.True(validation.IsAccepted, validation.Reason);
        var frame = Assert.IsType<MultiBoxStateFrame>(validation.Frame);
        Assert.Equal(flags, frame.Payload.Flags);
        Assert.Equal(targetName, frame.Payload.TargetName);
    }

    [Fact]
    public void MultiBoxState_RoundTrips_MaxLengthTargetName()
    {
        var longName = "0123456789";  // exactly 10 chars (TransportConstants.TargetNameMaxBytes)
        var snapshot = new MultiBoxStateSnapshot(0b00000110, longName);
        var bytes = FrameProtocol.BuildMultiBoxStateFrameBytes(_profile.NumericId, 6, snapshot);
        var image = ColorStripRenderer.Render(_profile, bytes);
        var validation = ColorStripAnalyzer.Analyze(image, _profile);

        Assert.True(validation.IsAccepted);
        var frame = Assert.IsType<MultiBoxStateFrame>(validation.Frame);
        Assert.Equal(longName, frame.Payload.TargetName);
    }

    [Fact]
    public void MultiBoxState_Truncates_NameExceeding_10Chars()
    {
        var tooLong = "0123456789OVERFLOW";
        var snapshot = new MultiBoxStateSnapshot(0b00000110, tooLong);
        var bytes = FrameProtocol.BuildMultiBoxStateFrameBytes(_profile.NumericId, 7, snapshot);
        var image = ColorStripRenderer.Render(_profile, bytes);
        var validation = ColorStripAnalyzer.Analyze(image, _profile);

        Assert.True(validation.IsAccepted);
        var frame = Assert.IsType<MultiBoxStateFrame>(validation.Frame);
        Assert.Equal("0123456789", frame.Payload.TargetName);  // truncated to 10
    }

    // ── MultiBoxStateSnapshot flag helpers ────────────────────────────────────

    [Fact]
    public void MultiBoxStateSnapshot_FlagHelpers_AreCorrect()
    {
        var empty = MultiBoxStateSnapshot.Empty;
        Assert.False(empty.InCombat);
        Assert.False(empty.HasTarget);
        Assert.False(empty.TargetHostile);

        var combat = new MultiBoxStateSnapshot(0b00000001, "");
        Assert.True(combat.InCombat);
        Assert.False(combat.HasTarget);

        var hostile = new MultiBoxStateSnapshot(0b00000110, "Mob");
        Assert.False(hostile.InCombat);
        Assert.True(hostile.HasTarget);
        Assert.True(hostile.TargetHostile);
    }

    // ── PlayerPositionSnapshot helpers ───────────────────────────────────────

    [Fact]
    public void PlayerPositionSnapshot_DistanceTo_IsCorrect()
    {
        var a = new PlayerPositionSnapshot(0f, 0f, 0f);
        var b = new PlayerPositionSnapshot(3f, 100f, 4f);  // Y ignored in horizontal distance
        Assert.Equal(5f, a.DistanceTo(b), 3);
    }

    [Fact]
    public void PlayerPositionSnapshot_BearingTo_IsCorrect()
    {
        var origin = new PlayerPositionSnapshot(0f, 0f, 0f);

        // Due north (positive Z): bearing should be 0°
        var north = new PlayerPositionSnapshot(0f, 0f, 10f);
        Assert.Equal(0f, origin.BearingTo(north), 1);

        // Due east (positive X): bearing should be 90°
        var east = new PlayerPositionSnapshot(10f, 0f, 0f);
        Assert.Equal(90f, origin.BearingTo(east), 1);

        // Due south: bearing should be 180°
        var south = new PlayerPositionSnapshot(0f, 0f, -10f);
        Assert.Equal(180f, origin.BearingTo(south), 1);

        // Due west: bearing should be 270°
        var west = new PlayerPositionSnapshot(-10f, 0f, 0f);
        Assert.Equal(270f, origin.BearingTo(west), 1);
    }

    // ── TelemetryLeaderStateSource ────────────────────────────────────────────

    [Fact]
    public void TelemetryLeaderStateSource_EmitsUpdate_WhenPositionFrameChanges()
    {
        var aggregate = new TelemetryAggregate();
        var source = new TelemetryLeaderStateSource(aggregate);

        LeaderState? received = null;
        source.Updated += s => received = s;

        // First frame
        var posBytes = FrameProtocol.BuildPlayerPositionFrameBytes(
            StripProfiles.Default.NumericId, 1,
            new PlayerPositionSnapshot(10f, 0f, 20f));
        aggregate.Apply(new PlayerPositionFrame(
            new TelemetryFrameHeader(1, 1, FrameType.PlayerPosition, 1, 1, 0, 0),
            new PlayerPositionSnapshot(10f, 0f, 20f),
            0,
            posBytes));

        source.Poll();
        Assert.NotNull(received);
        Assert.Equal(10f, received!.Position.X, 3);
        Assert.Equal(20f, received.Position.Z, 3);
    }

    [Fact]
    public void TelemetryLeaderStateSource_DoesNotEmit_WhenSequenceUnchanged()
    {
        var aggregate = new TelemetryAggregate();
        var source = new TelemetryLeaderStateSource(aggregate);

        var callCount = 0;
        source.Updated += _ => callCount++;

        var frame = new PlayerPositionFrame(
            new TelemetryFrameHeader(1, 1, FrameType.PlayerPosition, 1, 42, 0, 0),
            new PlayerPositionSnapshot(1f, 0f, 2f),
            0,
            Array.Empty<byte>());

        aggregate.Apply(frame);
        source.Poll();  // sequence 42 → emit
        source.Poll();  // sequence 42 again → no emit
        source.Poll();  // still 42 → no emit

        Assert.Equal(1, callCount);
    }

    // ── Non-regression: existing frame types still work ───────────────────────

    [Fact]
    public void ExistingCoreFrame_StillParsesCorrectly_AfterProtocolExtension()
    {
        var bytes = FrameProtocol.BuildCoreFrameBytes(
            StripProfiles.Default.NumericId, 99, CoreStatusSnapshot.CreateSynthetic());
        var image = ColorStripRenderer.Render(StripProfiles.Default, bytes);
        var validation = ColorStripAnalyzer.Analyze(image, StripProfiles.Default);

        Assert.True(validation.IsAccepted, validation.Reason);
        Assert.IsType<CoreStatusFrame>(validation.Frame);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AssertPositionRoundTrip(PlayerPositionSnapshot snapshot)
    {
        var bytes = FrameProtocol.BuildPlayerPositionFrameBytes(_profile.NumericId, 10, snapshot);
        var image = ColorStripRenderer.Render(_profile, bytes);
        var validation = ColorStripAnalyzer.Analyze(image, _profile);

        Assert.True(validation.IsAccepted, validation.Reason);
        var frame = Assert.IsType<PlayerPositionFrame>(validation.Frame);
        Assert.Equal(FrameType.PlayerPosition, frame.Header.FrameType);
        Assert.Equal(snapshot.X, frame.Payload.X, 2);
        Assert.Equal(snapshot.Y, frame.Payload.Y, 2);
        Assert.Equal(snapshot.Z, frame.Payload.Z, 2);
    }
}
