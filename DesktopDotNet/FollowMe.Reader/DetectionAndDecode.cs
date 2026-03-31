namespace FollowMe.Reader;

public sealed record DetectionResult(
    int OriginX,
    int OriginY,
    double Pitch,
    double Scale,
    double ControlError,
    double LeftControlScore,
    double RightControlScore,
    double AnchorLumaDelta,
    string SearchMode,
    Bgr24Color BlackAnchor,
    Bgr24Color WhiteAnchor);

public sealed record SegmentProbe(
    int X,
    int Y,
    Bgr24Color SampleColor);

public sealed record SegmentSample(
    int SegmentIndex,
    byte Symbol,
    double Confidence,
    double Distance,
    byte SecondChoiceSymbol,
    double SecondChoiceDistance,
    Bgr24Color SampleColor,
    IReadOnlyList<SegmentProbe> Probes);

public sealed record FrameValidationResult(
    bool IsAccepted,
    string Reason,
    DetectionResult? Detection,
    IReadOnlyList<SegmentSample> Samples,
    TelemetryFrame? Frame,
    TransportParseResult? ParseResult);

internal readonly record struct NormalizedRgb(double R, double G, double B)
{
    public double Luma => (R * 0.299) + (G * 0.587) + (B * 0.114);

    public double DistanceTo(NormalizedRgb other)
    {
        var deltaR = R - other.R;
        var deltaG = G - other.G;
        var deltaB = B - other.B;
        return Math.Sqrt((deltaR * deltaR) + (deltaG * deltaG) + (deltaB * deltaB));
    }
}

internal readonly record struct ColorClassification(
    byte Symbol,
    double Confidence,
    double Distance,
    byte SecondChoiceSymbol,
    double SecondChoiceDistance);

public static class ColorStripAnalyzer
{
    private const double LeftControlAcceptThreshold = 0.28;
    private const double MinimumAnchorLumaDelta = 26.0;
    private const double PayloadConfidenceThreshold = 0.08;
    private const double PayloadDistanceThreshold = 0.52;
    private static readonly (double X, double Y)[] ProbeOffsets =
    {
        (0.18, 0.08),
        (0.50, 0.08),
        (0.82, 0.08),
        (0.32, 0.16),
        (0.68, 0.16),
        (0.50, 0.24)
    };

    public static FrameValidationResult Analyze(Bgr24Frame image, StripProfile? profile = null)
    {
        profile ??= StripProfiles.Default;

        if (!TryFindBestCandidate(image, profile, out var candidate))
        {
            return new FrameValidationResult(false, DescribeMissingStrip(image), null, Array.Empty<SegmentSample>(), null, null);
        }

        if (candidate is null)
        {
            return new FrameValidationResult(false, "Pitch mismatch.", null, Array.Empty<SegmentSample>(), null, null);
        }

        if (candidate.Frame is not null)
        {
            return new FrameValidationResult(true, "Accepted", candidate.Detection, candidate.Samples, candidate.Frame, candidate.ParseResult);
        }

        return new FrameValidationResult(false, candidate.Reason, candidate.Detection, candidate.Samples, null, candidate.ParseResult);
    }

    private static bool TryFindBestCandidate(Bgr24Frame image, StripProfile profile, out Candidate? candidate)
    {
        candidate = null;
        Candidate? bestAccepted = null;
        Candidate? bestRejected = null;

        foreach (var scale in EnumerateScales())
        {
            var pitch = profile.SegmentWidth * scale;
            var bandWidth = (int)Math.Ceiling(profile.SegmentCount * pitch);
            var bandHeight = (int)Math.Ceiling(profile.SegmentHeight * scale);
            if (bandWidth > image.Width || bandHeight > image.Height)
            {
                continue;
            }

            var maxX = Math.Min(Math.Max(0, image.Width - bandWidth), 4);
            var maxY = Math.Min(Math.Max(0, image.Height - bandHeight), 2);
            for (var originY = 0; originY <= maxY; originY++)
            {
                for (var originX = 0; originX <= maxX; originX++)
                {
                    var current = EvaluateCandidate(image, profile, originX, originY, scale);
                    if (current is null)
                    {
                        continue;
                    }

                    if (current.Frame is not null)
                    {
                        if (bestAccepted is null
                            || current.Detection.LeftControlScore < bestAccepted.Detection.LeftControlScore
                            || (Math.Abs(current.Detection.LeftControlScore - bestAccepted.Detection.LeftControlScore) < 0.0001
                                && current.Detection.RightControlScore < bestAccepted.Detection.RightControlScore))
                        {
                            bestAccepted = current;
                        }
                    }
                    else if (bestRejected is null
                             || current.Detection.LeftControlScore < bestRejected.Detection.LeftControlScore
                             || (Math.Abs(current.Detection.LeftControlScore - bestRejected.Detection.LeftControlScore) < 0.0001
                                 && current.Detection.RightControlScore < bestRejected.Detection.RightControlScore))
                    {
                        bestRejected = current;
                    }
                }
            }
        }

        candidate = bestAccepted ?? bestRejected;
        return candidate is not null;
    }

    private static IEnumerable<double> EnumerateScales()
    {
        for (var scaled = 96; scaled <= 106; scaled++)
        {
            yield return scaled / 100.0;
        }

        for (var scaled = 32; scaled <= 40; scaled++)
        {
            yield return scaled / 100.0;
        }
    }

    private static Candidate? EvaluateCandidate(Bgr24Frame image, StripProfile profile, int originX, int originY, double scale)
    {
        var pitch = profile.SegmentWidth * scale;
        var segmentHeight = profile.SegmentHeight * scale;
        var radius = Math.Max(0, (int)Math.Round(Math.Min(pitch, segmentHeight) * 0.05));

        var leftBlackWhite = new List<Bgr24Color>(4);
        for (var controlIndex = 0; controlIndex < 4; controlIndex++)
        {
            var measured = MeasureSegment(image, originX, originY, pitch, segmentHeight, controlIndex, radius);
            leftBlackWhite.Add(measured.SampleColor);
        }

        var blackAnchor = AverageColors(leftBlackWhite[0], leftBlackWhite[2]);
        var whiteAnchor = AverageColors(leftBlackWhite[1], leftBlackWhite[3]);
        var anchorLumaDelta = GetLuma(whiteAnchor) - GetLuma(blackAnchor);
        if (anchorLumaDelta < MinimumAnchorLumaDelta)
        {
            return null;
        }

        var detection = new DetectionResult(
            originX,
            originY,
            pitch,
            scale,
            0,
            0,
            0,
            anchorLumaDelta,
            "fixed-profile",
            blackAnchor,
            whiteAnchor);

        var samples = SampleAllSegments(image, profile, detection, radius);
        var leftControlScore = ComputeControlScore(profile.LeftControl, 0, samples, profile, blackAnchor, whiteAnchor);
        var rightControlStart = profile.SegmentCount - profile.RightControl.Length;
        var rightControlScore = ComputeControlScore(profile.RightControl, rightControlStart, samples, profile, blackAnchor, whiteAnchor);
        detection = detection with
        {
            ControlError = leftControlScore,
            LeftControlScore = leftControlScore,
            RightControlScore = rightControlScore
        };

        for (var grayscaleIndex = 0; grayscaleIndex < 4; grayscaleIndex++)
        {
            if (samples[grayscaleIndex].Symbol != profile.LeftControl[grayscaleIndex])
            {
                return new Candidate(detection, samples, null, null, "Control marker mismatch.");
            }
        }

        if (leftControlScore > LeftControlAcceptThreshold)
        {
            return new Candidate(detection, samples, null, null, "Control marker mismatch.");
        }

        var payloadSymbols = new byte[profile.PayloadSymbolCount];
        var hasLowConfidence = false;
        for (var index = 0; index < profile.PayloadSymbolCount; index++)
        {
            var sample = samples[profile.PayloadStartIndex + index];
            payloadSymbols[index] = sample.Symbol;
            if (sample.Confidence < PayloadConfidenceThreshold || sample.Distance > PayloadDistanceThreshold)
            {
                hasLowConfidence = true;
            }
        }

        var parseResult = FrameProtocol.AnalyzeFrameBytes(FrameProtocol.DecodePayloadSymbolsToBytes(payloadSymbols));
        if (parseResult.IsAccepted && parseResult.Frame is not null)
        {
            return new Candidate(detection, samples, parseResult.Frame, parseResult, "Accepted");
        }

        if (hasLowConfidence)
        {
            return new Candidate(detection, samples, null, parseResult, "Low color-classification confidence.");
        }

        return new Candidate(detection, samples, null, parseResult, parseResult.Reason);
    }

    private static List<SegmentSample> SampleAllSegments(Bgr24Frame image, StripProfile profile, DetectionResult detection, int radius)
    {
        var segmentHeight = profile.SegmentHeight * detection.Scale;
        var samples = new List<SegmentSample>(profile.SegmentCount);
        for (var segmentIndex = 0; segmentIndex < profile.SegmentCount; segmentIndex++)
        {
            var measured = MeasureSegment(image, detection.OriginX, detection.OriginY, detection.Pitch, segmentHeight, segmentIndex, radius);
            var classification = Classify(measured.SampleColor, profile, detection.BlackAnchor, detection.WhiteAnchor);
            samples.Add(new SegmentSample(
                segmentIndex,
                classification.Symbol,
                classification.Confidence,
                classification.Distance,
                classification.SecondChoiceSymbol,
                classification.SecondChoiceDistance,
                measured.SampleColor,
                measured.Probes));
        }

        return samples;
    }

    private static MeasuredSegment MeasureSegment(
        Bgr24Frame image,
        int originX,
        int originY,
        double pitch,
        double segmentHeight,
        int segmentIndex,
        int radius)
    {
        var probes = new List<SegmentProbe>(ProbeOffsets.Length);
        foreach (var (probeX, probeY) in ProbeOffsets)
        {
            var x = originX + ((segmentIndex + probeX) * pitch);
            var y = originY + (probeY * segmentHeight);
            var roundedX = (int)Math.Round(x);
            var roundedY = (int)Math.Round(y);
            probes.Add(new SegmentProbe(roundedX, roundedY, image.SampleAverage(x, y, radius)));
        }

        return new MeasuredSegment(MedianColor(probes.Select(static probe => probe.SampleColor)), probes);
    }

    private static double ComputeControlScore(
        IReadOnlyList<byte> expectedSymbols,
        int startIndex,
        IReadOnlyList<SegmentSample> samples,
        StripProfile profile,
        Bgr24Color blackAnchor,
        Bgr24Color whiteAnchor)
    {
        double total = 0.0;
        for (var index = 0; index < expectedSymbols.Count; index++)
        {
            total += ComputeControlSegmentError(samples[startIndex + index], expectedSymbols[index], profile, blackAnchor, whiteAnchor);
        }

        return total / expectedSymbols.Count;
    }

    private static double ComputeControlSegmentError(
        SegmentSample sample,
        byte expectedSymbol,
        StripProfile profile,
        Bgr24Color blackAnchor,
        Bgr24Color whiteAnchor)
    {
        var normalizedSample = Normalize(sample.SampleColor, blackAnchor, whiteAnchor);
        var normalizedExpected = NormalizeIdeal(profile.GetPaletteColor(expectedSymbol));
        var colorError = normalizedSample.DistanceTo(normalizedExpected);
        var lumaError = Math.Abs(normalizedSample.Luma - normalizedExpected.Luma);
        var symbolPenalty = sample.Symbol == expectedSymbol ? 0.0 : 0.12;

        if (expectedSymbol == 0 || expectedSymbol == 1)
        {
            return (lumaError * 0.65) + (colorError * 0.25) + symbolPenalty;
        }

        return (colorError * 0.65) + (lumaError * 0.25) + symbolPenalty;
    }

    private static ColorClassification Classify(Bgr24Color sampleColor, StripProfile profile, Bgr24Color blackAnchor, Bgr24Color whiteAnchor)
    {
        var normalizedSample = Normalize(sampleColor, blackAnchor, whiteAnchor);
        var bestDistance = double.MaxValue;
        var secondDistance = double.MaxValue;
        byte bestSymbol = 0;
        byte secondSymbol = 0;

        foreach (var entry in profile.Palette)
        {
            var normalizedIdeal = NormalizeIdeal(entry.Color);
            var distance = normalizedSample.DistanceTo(normalizedIdeal);
            if (distance < bestDistance)
            {
                secondDistance = bestDistance;
                secondSymbol = bestSymbol;
                bestDistance = distance;
                bestSymbol = entry.Symbol;
            }
            else if (distance < secondDistance)
            {
                secondDistance = distance;
                secondSymbol = entry.Symbol;
            }
        }

        var separation = secondDistance <= 0.0001 ? 1.0 : Math.Clamp((secondDistance - bestDistance) / secondDistance, 0.0, 1.0);
        var absolute = Math.Clamp(1.0 - (bestDistance / 0.90), 0.0, 1.0);
        return new ColorClassification(bestSymbol, separation * absolute, bestDistance, secondSymbol, secondDistance);
    }

    private static string DescribeMissingStrip(Bgr24Frame image)
    {
        var signal = image.MeasureSignal();
        if (signal.LumaRange < 6.0 && signal.AverageLuma < 24.0)
        {
            return "Top band looks flat and near-black. The addon strip may not be loaded, or the capture backend returned a blank surface.";
        }

        if (signal.LumaRange < 12.0)
        {
            return "Top band looks too flat to locate control markers.";
        }

        return "Pitch mismatch.";
    }

    private static NormalizedRgb Normalize(Bgr24Color color, Bgr24Color blackAnchor, Bgr24Color whiteAnchor)
    {
        return new NormalizedRgb(
            NormalizeChannel(color.R, blackAnchor.R, whiteAnchor.R),
            NormalizeChannel(color.G, blackAnchor.G, whiteAnchor.G),
            NormalizeChannel(color.B, blackAnchor.B, whiteAnchor.B));
    }

    private static NormalizedRgb NormalizeIdeal(Bgr24Color color)
    {
        return new NormalizedRgb(
            NormalizeChannel(color.R, Bgr24Color.Black.R, Bgr24Color.White.R),
            NormalizeChannel(color.G, Bgr24Color.Black.G, Bgr24Color.White.G),
            NormalizeChannel(color.B, Bgr24Color.Black.B, Bgr24Color.White.B));
    }

    private static double NormalizeChannel(byte value, byte black, byte white)
    {
        var denominator = Math.Max(8.0, white - black);
        return Math.Clamp((value - black) / denominator, 0.0, 1.0);
    }

    private static double GetLuma(Bgr24Color color)
    {
        return (color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114);
    }

    private static Bgr24Color AverageColors(Bgr24Color left, Bgr24Color right)
    {
        return new Bgr24Color(
            (byte)((left.B + right.B) / 2),
            (byte)((left.G + right.G) / 2),
            (byte)((left.R + right.R) / 2));
    }

    private static Bgr24Color MedianColor(IEnumerable<Bgr24Color> colors)
    {
        var materialized = colors.ToArray();
        Array.Sort(materialized, static (left, right) => left.B.CompareTo(right.B));
        var medianB = materialized[materialized.Length / 2].B;
        Array.Sort(materialized, static (left, right) => left.G.CompareTo(right.G));
        var medianG = materialized[materialized.Length / 2].G;
        Array.Sort(materialized, static (left, right) => left.R.CompareTo(right.R));
        var medianR = materialized[materialized.Length / 2].R;
        return new Bgr24Color(medianB, medianG, medianR);
    }

    private sealed record Candidate(
        DetectionResult Detection,
        IReadOnlyList<SegmentSample> Samples,
        TelemetryFrame? Frame,
        TransportParseResult? ParseResult,
        string Reason);

    private sealed record MeasuredSegment(
        Bgr24Color SampleColor,
        IReadOnlyList<SegmentProbe> Probes);
}
