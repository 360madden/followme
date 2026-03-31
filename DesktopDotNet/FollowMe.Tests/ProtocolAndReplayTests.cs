using FollowMe.Reader;
using Xunit;

namespace FollowMe.Tests;

public class ProtocolAndReplayTests
{
    private readonly StripProfile _profile = StripProfiles.Default;

    [Fact]
    public void CoreFrame_RoundTripsThroughRendererAndAnalyzer()
    {
        var bytes = FrameProtocol.BuildCoreFrameBytes(_profile.NumericId, 7, CoreStatusSnapshot.CreateSynthetic());
        var image = ColorStripRenderer.Render(_profile, bytes);
        var validation = ColorStripAnalyzer.Analyze(image, _profile);

        Assert.True(validation.IsAccepted, validation.Reason);
        var frame = Assert.IsType<CoreStatusFrame>(validation.Frame);
        Assert.Equal(FrameType.CoreStatus, frame.Header.FrameType);
        Assert.Equal(198, frame.Payload.PlayerHealthPctQ8);
        Assert.Equal(91, frame.Payload.TargetHealthPctQ8);
    }

    [Fact]
    public void PlayerVitalsPage_RoundTripsThroughRendererAndAnalyzer()
    {
        var bytes = FrameProtocol.BuildPlayerVitalsFrameBytes(_profile.NumericId, 8, PlayerVitalsSnapshot.CreateSynthetic());
        var image = ColorStripRenderer.Render(_profile, bytes);
        var validation = ColorStripAnalyzer.Analyze(image, _profile);

        Assert.True(validation.IsAccepted, validation.Reason);
        var frame = Assert.IsType<PlayerStatsPageFrame>(validation.Frame);
        var vitals = Assert.IsType<PlayerVitalsStatsPagePayload>(frame.Payload);
        Assert.Equal(FrameType.PlayerStatsPage, frame.Header.FrameType);
        Assert.Equal(PlayerStatsPageSchema.Vitals, vitals.Schema);
        Assert.Equal<uint>(3260, vitals.Snapshot.HealthCurrent);
        Assert.Equal<ushort>(100, vitals.Snapshot.ResourceMax);
    }

    [Fact]
    public void ReplayRunner_AcceptsConfiguredBenchScenarios()
    {
        var bytes = FrameProtocol.BuildCoreFrameBytes(_profile.NumericId, 9, CoreStatusSnapshot.CreateSynthetic());
        var scenarios = new[]
        {
            new PerturbationOptions("baseline"),
            new PerturbationOptions("offset-2px", OffsetX: 2),
            new PerturbationOptions("blur-1", BlurRadius: 1),
            new PerturbationOptions("gain-plus10", RedGain: 1.1, GreenGain: 1.1, BlueGain: 1.1),
            new PerturbationOptions("gain-minus10", RedGain: 0.9, GreenGain: 0.9, BlueGain: 0.9),
            new PerturbationOptions("gamma-0.9", Gamma: 0.9),
            new PerturbationOptions("gamma-1.1", Gamma: 1.1),
            new PerturbationOptions("scale-1.02", Scale: 1.02)
        };

        var results = ReplayRunner.Run(_profile, bytes, scenarios);
        Assert.All(results, static result => Assert.True(result.Result.IsAccepted, result.Result.Reason));
    }

    [Fact]
    public void Analyzer_RejectsControlMarkerMismatch()
    {
        var bytes = FrameProtocol.BuildCoreFrameBytes(_profile.NumericId, 3, CoreStatusSnapshot.CreateSynthetic());
        var image = ColorStripRenderer.Render(_profile, bytes);
        image.FillRect(0, 0, _profile.SegmentWidth, _profile.SegmentHeight, _profile.GetPaletteColor(7));

        var validation = ColorStripAnalyzer.Analyze(image, _profile);
        Assert.False(validation.IsAccepted);
        Assert.Equal("Control marker mismatch.", validation.Reason);
    }

    [Fact]
    public void Analyzer_CanFallbackWhenOnlyRightControlIsCorrupted()
    {
        var bytes = FrameProtocol.BuildCoreFrameBytes(_profile.NumericId, 12, CoreStatusSnapshot.CreateSynthetic());
        var image = ColorStripRenderer.Render(_profile, bytes);
        var rightStart = (_profile.SegmentCount - _profile.RightControl.Length) * _profile.SegmentWidth;
        image.FillRect(rightStart, 0, _profile.RightControl.Length * _profile.SegmentWidth, _profile.SegmentHeight, _profile.GetPaletteColor(6));

        var validation = ColorStripAnalyzer.Analyze(image, _profile);

        Assert.True(validation.IsAccepted, validation.Reason);
        Assert.NotNull(validation.Detection);
        Assert.Equal("fixed-profile", validation.Detection!.SearchMode);
        Assert.True(validation.Detection.RightControlScore > validation.Detection.LeftControlScore);
    }

    [Fact]
    public void Analyzer_FlatBlackFrame_UsesHelpfulMissingStripReason()
    {
        var image = Bgr24Frame.CreateSolid(_profile.BandWidth, _profile.CaptureHeight, Bgr24Color.Black, "flat");

        var validation = ColorStripAnalyzer.Analyze(image, _profile);

        Assert.False(validation.IsAccepted);
        Assert.Contains("blank surface", validation.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyzer_Accepts_RealClientScaledFixture()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "live-client-scale-035.bmp");
        var image = BmpIO.Load(fixturePath);

        var validation = ColorStripAnalyzer.Analyze(image, _profile);

        Assert.True(validation.IsAccepted, validation.Reason);
        Assert.NotNull(validation.Detection);
        Assert.Equal("fixed-profile", validation.Detection!.SearchMode);
        Assert.Equal(0.35, validation.Detection.Scale, 2);
        Assert.Equal(2.8, validation.Detection.Pitch, 1);
        Assert.NotNull(validation.ParseResult);
        Assert.True(validation.ParseResult!.HeaderCrcValid);
        Assert.True(validation.ParseResult.PayloadCrcValid);
    }
}
