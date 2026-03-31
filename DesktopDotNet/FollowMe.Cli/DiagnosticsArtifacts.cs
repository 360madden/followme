using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;
using FollowMe.Reader;

internal static class DiagnosticsArtifacts
{
    public static void WriteArtifacts(
        string rawBmpPath,
        StripProfile profile,
        CaptureResult capture,
        FrameValidationResult validation,
        IReadOnlyList<string> attemptSummaries)
    {
        WriteAnnotatedBmp(rawBmpPath, profile, capture, validation);
        WriteSidecar(rawBmpPath, profile, capture, validation, attemptSummaries);
    }

    private static void WriteAnnotatedBmp(
        string rawBmpPath,
        StripProfile profile,
        CaptureResult capture,
        FrameValidationResult validation)
    {
        var annotatedPath = Path.Combine(
            Path.GetDirectoryName(rawBmpPath) ?? ".",
            $"{Path.GetFileNameWithoutExtension(rawBmpPath)}-annotated.bmp");

        using var bitmap = new Bitmap(rawBmpPath);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;

        using var segmentPen = new Pen(Color.LimeGreen, 1);
        using var roiPen = new Pen(Color.Gold, 1);
        using var sampleBrush = new SolidBrush(Color.DeepPink);
        using var textBrush = new SolidBrush(Color.White);
        using var bgBrush = new SolidBrush(Color.FromArgb(185, 0, 0, 0));
        using var font = new Font("Consolas", 8.0f, FontStyle.Regular, GraphicsUnit.Pixel);

        if (validation.Detection is not null)
        {
            var detection = validation.Detection;
            var bandHeight = profile.SegmentHeight * detection.Scale;
            var bandWidth = profile.SegmentCount * detection.Pitch;
            graphics.DrawRectangle(
                roiPen,
                (float)detection.OriginX,
                (float)detection.OriginY,
                (float)bandWidth,
                (float)bandHeight);

            for (var segmentIndex = 0; segmentIndex <= profile.SegmentCount; segmentIndex++)
            {
                var x = (float)(detection.OriginX + (segmentIndex * detection.Pitch));
                graphics.DrawLine(segmentPen, x, (float)detection.OriginY, x, (float)(detection.OriginY + bandHeight));
            }

            foreach (var sample in validation.Samples)
            {
                foreach (var probe in sample.Probes)
                {
                    graphics.FillEllipse(sampleBrush, probe.X - 1, probe.Y - 1, 3, 3);
                }
            }
        }

        var summaryLines = BuildOverlayLines(profile, capture, validation);
        var overlayWidth = 430;
        var overlayHeight = (summaryLines.Count * 11) + 8;
        graphics.FillRectangle(bgBrush, 4, 4, overlayWidth, overlayHeight);
        for (var index = 0; index < summaryLines.Count; index++)
        {
            graphics.DrawString(summaryLines[index], font, textBrush, 8, 8 + (index * 11));
        }

        bitmap.Save(annotatedPath);
    }

    private static void WriteSidecar(
        string rawBmpPath,
        StripProfile profile,
        CaptureResult capture,
        FrameValidationResult validation,
        IReadOnlyList<string> attemptSummaries)
    {
        var payload = new
        {
            artifactKind = Path.GetFileNameWithoutExtension(rawBmpPath),
            generatedAtUtc = DateTime.UtcNow,
            backend = capture.Backend.ToString(),
            clientRect = new
            {
                left = capture.ClientX,
                top = capture.ClientY,
                width = capture.ClientWidth,
                height = capture.ClientHeight
            },
            captureRect = new
            {
                left = capture.CaptureLeft,
                top = capture.CaptureTop,
                width = capture.CaptureWidth,
                height = capture.CaptureHeight
            },
            accepted = validation.IsAccepted,
            reason = validation.Reason,
            controlPatterns = new
            {
                leftExpected = FormatPattern(profile.LeftControl),
                leftObserved = FormatObservedPattern(validation.Samples, 0, profile.LeftControl.Length),
                rightExpected = FormatPattern(profile.RightControl),
                rightObserved = FormatObservedPattern(validation.Samples, profile.SegmentCount - profile.RightControl.Length, profile.RightControl.Length)
            },
            detection = validation.Detection is null
                ? null
                : new
                {
                    originX = validation.Detection.OriginX,
                    originY = validation.Detection.OriginY,
                    pitch = validation.Detection.Pitch,
                    scale = validation.Detection.Scale,
                    controlError = validation.Detection.ControlError,
                    leftControlScore = validation.Detection.LeftControlScore,
                    rightControlScore = validation.Detection.RightControlScore,
                    anchorLumaDelta = validation.Detection.AnchorLumaDelta,
                    searchMode = validation.Detection.SearchMode
                },
            parse = validation.ParseResult is null
                ? null
                : new
                {
                    accepted = validation.ParseResult.IsAccepted,
                    validation.ParseResult.Reason,
                    validation.ParseResult.MagicValid,
                    validation.ParseResult.ProtocolProfileValid,
                    validation.ParseResult.FrameSchemaValid,
                    validation.ParseResult.HeaderCrcValid,
                    validation.ParseResult.PayloadCrcValid,
                    transportBytesHex = BitConverter.ToString(validation.ParseResult.TransportBytes).Replace("-", string.Empty)
                },
            samples = validation.Samples.Select(sample => new
            {
                segmentIndex = sample.SegmentIndex,
                symbol = sample.Symbol,
                confidence = sample.Confidence,
                distance = sample.Distance,
                secondChoiceSymbol = sample.SecondChoiceSymbol,
                secondChoiceDistance = sample.SecondChoiceDistance,
                sampleColor = new
                {
                    r = sample.SampleColor.R,
                    g = sample.SampleColor.G,
                    b = sample.SampleColor.B
                },
                probes = sample.Probes.Select(probe => new
                {
                    probe.X,
                    probe.Y,
                    sampleColor = new
                    {
                        r = probe.SampleColor.R,
                        g = probe.SampleColor.G,
                        b = probe.SampleColor.B
                    }
                }).ToArray()
            }).ToArray(),
            attempts = attemptSummaries.ToArray()
        };

        var jsonPath = Path.ChangeExtension(rawBmpPath, ".json");
        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    private static List<string> BuildOverlayLines(StripProfile profile, CaptureResult capture, FrameValidationResult validation)
    {
        var lines = new List<string>
        {
            $"Backend: {capture.Backend}",
            $"Accepted: {validation.IsAccepted} | Reason: {validation.Reason}"
        };

        if (validation.Detection is not null)
        {
            lines.Add(
                $"Origin {validation.Detection.OriginX},{validation.Detection.OriginY} | Pitch {validation.Detection.Pitch:F3} | Scale {validation.Detection.Scale:F3}");
            lines.Add(
                $"LeftScore {validation.Detection.LeftControlScore:F4} | RightScore {validation.Detection.RightControlScore:F4} | AnchorDelta {validation.Detection.AnchorLumaDelta:F2}");
        }

        lines.Add($"Left:  {FormatPattern(profile.LeftControl)}");
        lines.Add($"Left': {FormatObservedPattern(validation.Samples, 0, profile.LeftControl.Length)}");
        lines.Add($"Right: {FormatPattern(profile.RightControl)}");
        lines.Add($"Right':{FormatObservedPattern(validation.Samples, profile.SegmentCount - profile.RightControl.Length, profile.RightControl.Length)}");

        if (validation.ParseResult is not null)
        {
            lines.Add(
                $"Parse magic={validation.ParseResult.MagicValid} version={validation.ParseResult.ProtocolProfileValid} schema={validation.ParseResult.FrameSchemaValid}");
            lines.Add(
                $"Parse headerCrc={validation.ParseResult.HeaderCrcValid} payloadCrc={validation.ParseResult.PayloadCrcValid}");
        }

        return lines;
    }

    private static string FormatPattern(IEnumerable<byte> symbols)
    {
        return string.Join(" ", symbols.Select(static symbol => symbol.ToString()));
    }

    private static string FormatObservedPattern(IReadOnlyList<SegmentSample> samples, int start, int length)
    {
        if (samples.Count == 0)
        {
            return "-";
        }

        var selected = samples
            .Where(sample => sample.SegmentIndex >= start && sample.SegmentIndex < start + length)
            .OrderBy(sample => sample.SegmentIndex)
            .Select(sample => sample.Symbol.ToString());

        return string.Join(" ", selected);
    }
}
