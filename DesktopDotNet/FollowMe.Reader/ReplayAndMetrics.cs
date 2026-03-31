using System.Diagnostics;

namespace FollowMe.Reader;

public sealed record PerturbationOptions(
    string Name,
    int OffsetX = 0,
    int OffsetY = 0,
    int BlurRadius = 0,
    double RedGain = 1.0,
    double GreenGain = 1.0,
    double BlueGain = 1.0,
    double Gamma = 1.0,
    double Scale = 1.0);

public sealed record ReplayScenarioResult(
    PerturbationOptions Scenario,
    FrameValidationResult Result,
    double LocateDecodeMs);

public static class PathProvider
{
    public static string RootDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FollowMe",
            "DesktopDotNet");

    public static string EnsureOutDirectory()
    {
        var path = Path.Combine(RootDirectory, "out");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string EnsureFixtureDirectory()
    {
        var path = Path.Combine(RootDirectory, "fixtures");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string? GetLatestCapturePath()
    {
        var outDirectory = EnsureOutDirectory();
        return Directory
            .EnumerateFiles(outDirectory, "*.bmp")
            .Select(static path => new FileInfo(path))
            .OrderByDescending(static info => info.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }
}

public static class ReplayRunner
{
    public static IReadOnlyList<ReplayScenarioResult> Run(StripProfile profile, byte[] transportBytes, IEnumerable<PerturbationOptions> scenarios)
    {
        var baseFrame = ColorStripRenderer.Render(profile, transportBytes);
        var results = new List<ReplayScenarioResult>();
        foreach (var scenario in scenarios)
        {
            var replayFrame = ApplyScenario(profile, baseFrame, scenario);
            var timer = Stopwatch.StartNew();
            var result = ColorStripAnalyzer.Analyze(replayFrame, profile);
            timer.Stop();
            results.Add(new ReplayScenarioResult(scenario, result, timer.Elapsed.TotalMilliseconds));
        }

        return results;
    }

    private static Bgr24Frame ApplyScenario(StripProfile profile, Bgr24Frame baseFrame, PerturbationOptions scenario)
    {
        var strip = scenario.Scale == 1.0 ? baseFrame.Copy("scenario-base") : baseFrame.ScaleNearest(scenario.Scale, "scenario-scale");
        var canvasWidth = Math.Max(profile.BandWidth + scenario.OffsetX + 8, strip.Width + scenario.OffsetX);
        var canvasHeight = Math.Max(profile.CaptureHeight, strip.Height + scenario.OffsetY + 8);
        var canvas = Bgr24Frame.CreateSolid(canvasWidth, canvasHeight, profile.GetPaletteColor(0), "scenario-canvas");
        canvas.Paste(strip, Math.Max(0, scenario.OffsetX), Math.Max(0, scenario.OffsetY));

        if (scenario.RedGain != 1.0 || scenario.GreenGain != 1.0 || scenario.BlueGain != 1.0)
        {
            canvas = canvas.ApplyGain(scenario.RedGain, scenario.GreenGain, scenario.BlueGain, "scenario-gain");
        }

        if (scenario.Gamma != 1.0)
        {
            canvas = canvas.ApplyGamma(scenario.Gamma, "scenario-gamma");
        }

        if (scenario.BlurRadius > 0)
        {
            canvas = canvas.ApplyBoxBlur(scenario.BlurRadius, "scenario-blur");
        }

        return canvas;
    }
}

public sealed class LiveMetrics
{
    private readonly List<double> _captureMs = new();
    private readonly List<double> _decodeMs = new();

    public int AcceptedCount { get; private set; }

    public int RejectedCount { get; private set; }

    public string LastReason { get; private set; } = "-";

    public double AverageCaptureMs => _captureMs.Count == 0 ? 0 : _captureMs.Average();

    public double AverageDecodeMs => _decodeMs.Count == 0 ? 0 : _decodeMs.Average();

    public double MedianDecodeMs => Percentile(_decodeMs, 0.50);

    public double P95DecodeMs => Percentile(_decodeMs, 0.95);

    public void Add(bool accepted, double captureMs, double decodeMs, string reason)
    {
        if (accepted)
        {
            AcceptedCount++;
        }
        else
        {
            RejectedCount++;
        }

        _captureMs.Add(captureMs);
        _decodeMs.Add(decodeMs);
        LastReason = reason;
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var ordered = values.OrderBy(static value => value).ToArray();
        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }
}
