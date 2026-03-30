using System.Diagnostics;
using System.Text;
using FollowMe.MultiBox.Config;
using FollowMe.MultiBox.Session;
using FollowMe.Reader;

return new CliApp().Run(args);

internal sealed class CliApp
{
    private readonly StripProfile _profile = StripProfiles.Default;
    private static readonly CaptureBackend[] DefaultCaptureBackends = { CaptureBackend.DesktopDuplication, CaptureBackend.ScreenBitBlt };

    public int Run(string[] args)
    {
        var command = args.Length == 0 ? "smoke" : args[0].ToLowerInvariant();
        return command switch
        {
            "smoke" => RunSmoke(),
            "replay" => RunReplay(args),
            "live" => RunLive(args, watchMode: false),
            "watch" => RunLive(args, watchMode: true),
            "bench" => RunBench(),
            "capture-dump" => RunCaptureDump(args),
            "prepare-window" => RunPrepareWindow(args),
            "multibox-leader" => RunMultiBoxLeaderAsync(args).GetAwaiter().GetResult(),
            "multibox-follower" => RunMultiBoxFollowerAsync(args).GetAwaiter().GetResult(),
            "multibox-smoke" => RunMultiBoxSmoke(),
            "help" or "--help" or "-h" => PrintUsage(),
            _ => Fail($"Unsupported command: {command}")
        };
    }

    private int RunSmoke()
    {
        var snapshot = CoreStatusSnapshot.CreateSynthetic();
        var bytes = FrameProtocol.BuildCoreFrameBytes(_profile.NumericId, 10, snapshot);
        var image = ColorStripRenderer.Render(_profile, bytes);
        var fixtureRoot = PathProvider.EnsureFixtureDirectory();
        var path = Path.Combine(fixtureRoot, "followme-color-core.bmp");
        BmpIO.Save(path, image);

        var validation = ColorStripAnalyzer.Analyze(image, _profile);
        Console.WriteLine("FollowMe");
        Console.WriteLine("Mode: smoke");
        Console.WriteLine($"Success: {validation.IsAccepted.ToString().ToLowerInvariant()}");
        Console.WriteLine($"Fixture: {path}");
        Console.WriteLine($"Reason: {validation.Reason}");
        if (validation.Frame is not null)
        {
            WriteFrameSummary(validation.Frame);
        }

        return validation.IsAccepted ? 0 : 1;
    }

    private int RunReplay(string[] args)
    {
        if (args.Length < 2)
        {
            return Fail("replay requires a BMP path.");
        }

        var image = BmpIO.Load(args[1]);
        var validation = ColorStripAnalyzer.Analyze(image, _profile);
        Console.WriteLine("FollowMe");
        Console.WriteLine("Mode: replay");
        Console.WriteLine($"Accepted: {validation.IsAccepted.ToString().ToLowerInvariant()}");
        Console.WriteLine($"Reason: {validation.Reason}");
        WriteDetectionSummary(validation.Detection);
        if (validation.Frame is not null)
        {
            WriteFrameSummary(validation.Frame);
        }

        return validation.IsAccepted ? 0 : 1;
    }

    private int RunCaptureDump(string[] args)
    {
        if (!TryParseCaptureInvocation(args, 1, out var invocation, out var error))
        {
            return Fail(error);
        }

        var hwnd = WindowCaptureService.FindRiftWindow();
        if (hwnd == nint.Zero)
        {
            return Fail("No likely RIFT window was found.");
        }

        var attempts = CaptureAndAnalyze(hwnd, invocation.Backends);
        var selectedAttempt = ChooseBestAttempt(attempts);
        if (selectedAttempt is null)
        {
            return Fail("capture-dump failed: no capture backend produced an image.");
        }

        var capture = selectedAttempt.Capture!;
        var path = Path.Combine(PathProvider.EnsureOutDirectory(), "followme-color-capture-dump.bmp");
        BmpIO.Save(path, capture.Image);
        DiagnosticsArtifacts.WriteArtifacts(
            path,
            _profile,
            capture,
            selectedAttempt.Validation!,
            attempts.Select(static attempt =>
                $"{attempt.Backend} | {(attempt.Validation?.IsAccepted == true ? "accepted" : "rejected")} | {attempt.Validation?.Reason ?? attempt.FailureReason ?? "capture failed"} | AvgLuma {(attempt.Signal?.AverageLuma ?? 0):F2} | LumaRange {(attempt.Signal?.LumaRange ?? 0):F2}")
                .ToArray());

        Console.WriteLine("FollowMe");
        Console.WriteLine("Mode: capture-dump");
        Console.WriteLine($"Path: {path}");
        Console.WriteLine($"Backend: {capture.Backend}");
        Console.WriteLine($"CaptureRect: {capture.CaptureLeft},{capture.CaptureTop} {capture.CaptureWidth}x{capture.CaptureHeight}");
        Console.WriteLine($"Accepted: {selectedAttempt.Validation!.IsAccepted.ToString().ToLowerInvariant()}");
        Console.WriteLine($"Reason: {selectedAttempt.Validation.Reason}");
        Console.WriteLine($"AverageLuma: {(selectedAttempt.Signal?.AverageLuma ?? 0):F2}");
        Console.WriteLine($"LumaRange: {(selectedAttempt.Signal?.LumaRange ?? 0):F2}");
        foreach (var attempt in attempts)
        {
            Console.WriteLine(
                $"Attempt: {attempt.Backend} | {(attempt.Validation?.IsAccepted == true ? "accepted" : "rejected")} | {attempt.Validation?.Reason ?? attempt.FailureReason ?? "capture failed"} | AvgLuma {(attempt.Signal?.AverageLuma ?? 0):F2} | LumaRange {(attempt.Signal?.LumaRange ?? 0):F2}");
        }

        WriteDetectionSummary(selectedAttempt.Validation.Detection);
        if (selectedAttempt.Validation.Frame is not null)
        {
            WriteFrameSummary(selectedAttempt.Validation.Frame);
        }

        return 0;
    }

    private int RunPrepareWindow(string[] args)
    {
        var hwnd = WindowCaptureService.FindRiftWindow();
        if (hwnd == nint.Zero)
        {
            return Fail("No likely RIFT window was found.");
        }

        var left = args.Length >= 2 ? int.Parse(args[1]) : 32;
        var top = args.Length >= 3 ? int.Parse(args[2]) : 32;
        var result = WindowControlService.EnsureClientSize(hwnd, _profile.WindowWidth, _profile.WindowHeight, left, top);

        Console.WriteLine("FollowMe");
        Console.WriteLine("Mode: prepare-window");
        Console.WriteLine($"RequestedClient: {_profile.WindowWidth}x{_profile.WindowHeight}");
        Console.WriteLine($"BeforeWindow: {result.Before.WindowLeft},{result.Before.WindowTop} {result.Before.WindowWidth}x{result.Before.WindowHeight}");
        Console.WriteLine($"BeforeClient: {result.Before.ClientLeft},{result.Before.ClientTop} {result.Before.ClientWidth}x{result.Before.ClientHeight}");
        Console.WriteLine($"BeforeMinimized: {result.Before.IsMinimized.ToString().ToLowerInvariant()}");
        Console.WriteLine($"AfterWindow: {result.After.WindowLeft},{result.After.WindowTop} {result.After.WindowWidth}x{result.After.WindowHeight}");
        Console.WriteLine($"AfterClient: {result.After.ClientLeft},{result.After.ClientTop} {result.After.ClientWidth}x{result.After.ClientHeight}");
        Console.WriteLine($"Success: {result.Succeeded.ToString().ToLowerInvariant()}");
        Console.WriteLine($"Reason: {result.Reason}");
        return result.Succeeded ? 0 : 1;
    }

    private int RunBench()
    {
        var bytes = FrameProtocol.BuildCoreFrameBytes(_profile.NumericId, 42, CoreStatusSnapshot.CreateSynthetic());
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
        Console.WriteLine("FollowMe");
        Console.WriteLine("Mode: bench");
        foreach (var result in results)
        {
            Console.WriteLine($"{result.Scenario.Name}: {(result.Result.IsAccepted ? "accepted" : "rejected")} | {result.Result.Reason} | {result.LocateDecodeMs:F2} ms");
        }

        Console.WriteLine($"AverageReplayDecodeMs: {results.Average(static result => result.LocateDecodeMs):F2}");
        Console.WriteLine($"P95ReplayDecodeMs: {Percentile(results.Select(static result => result.LocateDecodeMs).ToArray(), 0.95):F2}");
        return results.All(static result => result.Result.IsAccepted) ? 0 : 1;
    }

    private int RunLive(string[] args, bool watchMode)
    {
        if (!TryParseCaptureInvocation(args, 1, out var invocation, out var error))
        {
            return Fail(error);
        }

        var hwnd = WindowCaptureService.FindRiftWindow();
        if (hwnd == nint.Zero)
        {
            return Fail("No likely RIFT window was found.");
        }

        var iterationLimit = watchMode
            ? int.MaxValue
            : (invocation.Positionals.Count >= 1 ? Math.Max(1, int.Parse(invocation.Positionals[0])) : 5);
        var sleepMs = invocation.Positionals.Count >= 2 ? Math.Max(0, int.Parse(invocation.Positionals[1])) : 100;
        var durationMs = watchMode && invocation.Positionals.Count >= 1
            ? Math.Max(1, int.Parse(invocation.Positionals[0])) * 1000
            : int.MaxValue;
        var metrics = new LiveMetrics();
        var started = Stopwatch.StartNew();
        FrameValidationResult? lastValidation = null;
        CaptureBackend? lastBackend = null;
        string? firstRejectPath = null;

        for (var iteration = 0; iteration < iterationLimit && started.ElapsedMilliseconds < durationMs; iteration++)
        {
            var bestAttempt = ChooseBestAttempt(CaptureAndAnalyze(hwnd, invocation.Backends));
            if (bestAttempt is not null)
            {
                metrics.Add(
                    bestAttempt.Validation!.IsAccepted,
                    bestAttempt.CaptureElapsedMs,
                    bestAttempt.DecodeElapsedMs,
                    bestAttempt.Validation.Reason);
                lastValidation = bestAttempt.Validation;
                lastBackend = bestAttempt.Backend;

                if (!bestAttempt.Validation.IsAccepted && firstRejectPath is null)
                {
                    firstRejectPath = Path.Combine(PathProvider.EnsureOutDirectory(), "followme-color-first-reject.bmp");
                    BmpIO.Save(firstRejectPath, bestAttempt.Capture!.Image);
                    DiagnosticsArtifacts.WriteArtifacts(
                        firstRejectPath,
                        _profile,
                        bestAttempt.Capture,
                        bestAttempt.Validation,
                        new[]
                        {
                            $"{bestAttempt.Backend} | {(bestAttempt.Validation.IsAccepted ? "accepted" : "rejected")} | {bestAttempt.Validation.Reason} | AvgLuma {(bestAttempt.Signal?.AverageLuma ?? 0):F2} | LumaRange {(bestAttempt.Signal?.LumaRange ?? 0):F2}"
                        });
                }
            }
            else
            {
                metrics.Add(false, 0, 0, "No capture backend produced an image.");
                lastValidation = new FrameValidationResult(false, "No capture backend produced an image.", null, Array.Empty<SegmentSample>(), null, null);
            }

            if (sleepMs > 0)
            {
                Thread.Sleep(sleepMs);
            }
        }

        Console.WriteLine("FollowMe");
        Console.WriteLine($"Mode: {(watchMode ? "watch" : "live")}");
        Console.WriteLine($"AcceptedSamples: {metrics.AcceptedCount}");
        Console.WriteLine($"RejectedSamples: {metrics.RejectedCount}");
        Console.WriteLine($"AverageCaptureMs: {metrics.AverageCaptureMs:F2}");
        Console.WriteLine($"AverageDecodeMs: {metrics.AverageDecodeMs:F2}");
        Console.WriteLine($"MedianDecodeMs: {metrics.MedianDecodeMs:F2}");
        Console.WriteLine($"P95DecodeMs: {metrics.P95DecodeMs:F2}");
        Console.WriteLine($"LastReason: {metrics.LastReason}");
        if (lastBackend is not null)
        {
            Console.WriteLine($"LastBackend: {lastBackend}");
        }
        WriteDetectionSummary(lastValidation?.Detection);
        if (lastValidation?.Frame is not null)
        {
            WriteFrameSummary(lastValidation.Frame);
        }

        if (firstRejectPath is not null)
        {
            Console.WriteLine($"FirstRejectBmp: {firstRejectPath}");
        }

        return metrics.AcceptedCount > 0 ? 0 : 1;
    }

    private static void WriteDetectionSummary(DetectionResult? detection)
    {
        if (detection is null)
        {
            return;
        }

        Console.WriteLine($"Origin: {detection.OriginX},{detection.OriginY}");
        Console.WriteLine($"Pitch: {detection.Pitch:F3}");
        Console.WriteLine($"Scale: {detection.Scale:F3}");
        Console.WriteLine($"ControlError: {detection.ControlError:F4}");
        Console.WriteLine($"LeftControlScore: {detection.LeftControlScore:F4}");
        Console.WriteLine($"RightControlScore: {detection.RightControlScore:F4}");
        Console.WriteLine($"AnchorLumaDelta: {detection.AnchorLumaDelta:F2}");
        Console.WriteLine($"SearchMode: {detection.SearchMode}");
    }

    private static void WriteFrameSummary(TelemetryFrame frame)
    {
        Console.WriteLine($"FrameType: {frame.Header.FrameType}");
        Console.WriteLine($"Schema: {frame.Header.SchemaId}");
        Console.WriteLine($"Sequence: {frame.Header.Sequence}");

        switch (frame)
        {
            case CoreStatusFrame core:
                Console.WriteLine($"PlayerFlags: {core.Payload.PlayerStateFlags}");
                Console.WriteLine($"PlayerHealthPctQ8: {core.Payload.PlayerHealthPctQ8}");
                Console.WriteLine($"PlayerResourceKind: {core.Payload.PlayerResourceKind}");
                Console.WriteLine($"PlayerResourcePctQ8: {core.Payload.PlayerResourcePctQ8}");
                Console.WriteLine($"TargetFlags: {core.Payload.TargetStateFlags}");
                Console.WriteLine($"TargetHealthPctQ8: {core.Payload.TargetHealthPctQ8}");
                Console.WriteLine($"TargetResourceKind: {core.Payload.TargetResourceKind}");
                Console.WriteLine($"TargetResourcePctQ8: {core.Payload.TargetResourcePctQ8}");
                Console.WriteLine($"PlayerLevel: {core.Payload.PlayerLevel}");
                Console.WriteLine($"TargetLevel: {core.Payload.TargetLevel}");
                Console.WriteLine($"PlayerCalling: {core.Payload.PlayerCallingRolePacked >> 4}");
                Console.WriteLine($"PlayerRole: {core.Payload.PlayerCallingRolePacked & 0x0F}");
                Console.WriteLine($"TargetCalling: {core.Payload.TargetCallingRelationPacked >> 4}");
                Console.WriteLine($"TargetRelation: {core.Payload.TargetCallingRelationPacked & 0x0F}");
                break;

            case PlayerStatsPageFrame stats:
                WritePlayerStatsPageSummary(stats);
                break;
        }
    }

    private static void WritePlayerStatsPageSummary(PlayerStatsPageFrame frame)
    {
        switch (frame.Payload)
        {
            case PlayerVitalsStatsPagePayload vitals:
                Console.WriteLine($"Page: Vitals");
                Console.WriteLine($"HealthCurrent: {vitals.Snapshot.HealthCurrent}");
                Console.WriteLine($"HealthMax: {vitals.Snapshot.HealthMax}");
                Console.WriteLine($"ResourceCurrent: {vitals.Snapshot.ResourceCurrent}");
                Console.WriteLine($"ResourceMax: {vitals.Snapshot.ResourceMax}");
                break;

            case PlayerMainStatsPagePayload main:
                Console.WriteLine($"Page: Main");
                Console.WriteLine($"Armor: {main.Snapshot.Armor}");
                Console.WriteLine($"Strength: {main.Snapshot.Strength}");
                Console.WriteLine($"Dexterity: {main.Snapshot.Dexterity}");
                Console.WriteLine($"Intelligence: {main.Snapshot.Intelligence}");
                Console.WriteLine($"Wisdom: {main.Snapshot.Wisdom}");
                Console.WriteLine($"Endurance: {main.Snapshot.Endurance}");
                break;

            case PlayerOffenseStatsPagePayload offense:
                Console.WriteLine($"Page: Offense");
                Console.WriteLine($"AttackPower: {offense.Snapshot.AttackPower}");
                Console.WriteLine($"PhysicalCrit: {offense.Snapshot.PhysicalCrit}");
                Console.WriteLine($"Hit: {offense.Snapshot.Hit}");
                Console.WriteLine($"SpellPower: {offense.Snapshot.SpellPower}");
                Console.WriteLine($"SpellCrit: {offense.Snapshot.SpellCrit}");
                Console.WriteLine($"CritPower: {offense.Snapshot.CritPower}");
                break;

            case PlayerDefenseStatsPagePayload defense:
                Console.WriteLine($"Page: Defense");
                Console.WriteLine($"Dodge: {defense.Snapshot.Dodge}");
                Console.WriteLine($"Block: {defense.Snapshot.Block}");
                break;

            case PlayerResistanceStatsPagePayload resist:
                Console.WriteLine($"Page: Resistances");
                Console.WriteLine($"LifeResist: {resist.Snapshot.LifeResist}");
                Console.WriteLine($"DeathResist: {resist.Snapshot.DeathResist}");
                Console.WriteLine($"FireResist: {resist.Snapshot.FireResist}");
                Console.WriteLine($"WaterResist: {resist.Snapshot.WaterResist}");
                Console.WriteLine($"EarthResist: {resist.Snapshot.EarthResist}");
                Console.WriteLine($"AirResist: {resist.Snapshot.AirResist}");
                break;
        }
    }

    private List<CaptureAttempt> CaptureAndAnalyze(nint hwnd, IReadOnlyList<CaptureBackend> backends)
    {
        var attempts = new List<CaptureAttempt>(backends.Count);
        foreach (var backend in backends)
        {
            try
            {
                var captureTimer = Stopwatch.StartNew();
                var capture = WindowCaptureService.CaptureTopSlice(hwnd, _profile, _profile.CaptureHeight - _profile.BandHeight, backend);
                captureTimer.Stop();

                var decodeTimer = Stopwatch.StartNew();
                var validation = ColorStripAnalyzer.Analyze(capture.Image, _profile);
                decodeTimer.Stop();

                attempts.Add(new CaptureAttempt(
                    backend,
                    capture,
                    validation,
                    capture.Image.MeasureSignal(),
                    captureTimer.Elapsed.TotalMilliseconds,
                    decodeTimer.Elapsed.TotalMilliseconds,
                    null));
            }
            catch (Exception ex)
            {
                attempts.Add(new CaptureAttempt(backend, null, null, null, 0, 0, ex.Message));
            }
        }

        return attempts;
    }

    private static CaptureAttempt? ChooseBestAttempt(IEnumerable<CaptureAttempt> attempts)
    {
        return attempts
            .Where(static attempt => attempt.Capture is not null && attempt.Validation is not null)
            .OrderByDescending(static attempt => attempt.Validation!.IsAccepted)
            .ThenBy(static attempt => attempt.Validation!.Detection is null ? 1 : 0)
            .ThenBy(static attempt => attempt.Validation!.Detection?.ControlError ?? double.MaxValue)
            .ThenByDescending(static attempt => attempt.Signal?.LumaRange ?? 0)
            .ThenByDescending(static attempt => attempt.Signal?.AverageLuma ?? 0)
            .FirstOrDefault();
    }

    // ── MultiBox commands ─────────────────────────────────────────────────────

    private async Task<int> RunMultiBoxLeaderAsync(string[] args)
    {
        var configPath = args.Length >= 2 ? args[1] : "multibox.json";
        var config = MultiBoxConfig.LoadOrDefault(configPath);

        if (config.Mode != MultiBoxMode.Leader)
        {
            config = config with { Mode = MultiBoxMode.Leader };
        }

        Console.WriteLine("FollowMe");
        Console.WriteLine("Mode: multibox-leader");
        Console.WriteLine($"Config: {configPath}");
        Console.WriteLine($"LeaderName: {config.LeaderName}");
        Console.WriteLine($"TcpPort: {config.TcpPort}");
        Console.WriteLine($"FollowDistance: {config.FollowDistance}");
        Console.WriteLine("Press Ctrl+C to stop.");

        var aggregate = new TelemetryAggregate();
        var (session, leaderSource) = MultiBoxSessionFactory.CreateLeader(config, aggregate);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var sessionTask = session.StartAsync(cts.Token);
        var hwnd = WindowCaptureService.FindRiftWindow();
        var statusTimer = Stopwatch.StartNew();

        try
        {
            while (!cts.IsCancellationRequested)
            {
                if (hwnd != nint.Zero)
                {
                    try
                    {
                        var capture = WindowCaptureService.CaptureTopSlice(hwnd, _profile,
                            _profile.CaptureHeight - _profile.BandHeight, CaptureBackend.DesktopDuplication);
                        var validation = ColorStripAnalyzer.Analyze(capture.Image, _profile);
                        if (validation.Frame is not null)
                        {
                            aggregate.Apply(validation.Frame);
                            leaderSource.Poll();
                        }
                    }
                    catch { /* RIFT window may not be visible */ }
                }

                if (statusTimer.ElapsedMilliseconds >= 2000)
                {
                    var pos = session.LastBroadcastState?.Position;
                    Console.WriteLine($"[Leader] Connected={session.IsFollowerConnected} " +
                        $"Pos=({pos?.X:F1},{pos?.Y:F1},{pos?.Z:F1}) " +
                        $"Target={session.LastBroadcastState?.MultiBox.TargetName ?? "-"} " +
                        $"Transport={session.TransportStatus}");
                    statusTimer.Restart();
                    hwnd = WindowCaptureService.FindRiftWindow();  // re-find in case of restart
                }

                await Task.Delay(50, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            await session.DisposeAsync();
        }

        Console.WriteLine("MultiBox leader stopped.");
        return 0;
    }

    private async Task<int> RunMultiBoxFollowerAsync(string[] args)
    {
        var configPath = args.Length >= 2 ? args[1] : "multibox.json";
        var config = MultiBoxConfig.LoadOrDefault(configPath);

        if (config.Mode != MultiBoxMode.Follower)
        {
            config = config with { Mode = MultiBoxMode.Follower };
        }

        Console.WriteLine("FollowMe");
        Console.WriteLine("Mode: multibox-follower");
        Console.WriteLine($"Config: {configPath}");
        Console.WriteLine($"LeaderHost: {config.LeaderHost}:{config.TcpPort}");
        Console.WriteLine($"LeaderName: {config.LeaderName}");
        Console.WriteLine("Press Ctrl+C to stop.");

        var aggregate = new TelemetryAggregate();
        var (session, followerSource) = MultiBoxSessionFactory.CreateFollower(config, aggregate);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var sessionTask = session.StartAsync(cts.Token);
        var hwnd = WindowCaptureService.FindRiftWindow();
        var statusTimer = Stopwatch.StartNew();

        try
        {
            while (!cts.IsCancellationRequested)
            {
                if (hwnd != nint.Zero)
                {
                    try
                    {
                        var capture = WindowCaptureService.CaptureTopSlice(hwnd, _profile,
                            _profile.CaptureHeight - _profile.BandHeight, CaptureBackend.DesktopDuplication);
                        var validation = ColorStripAnalyzer.Analyze(capture.Image, _profile);
                        if (validation.Frame is not null)
                        {
                            aggregate.Apply(validation.Frame);
                            followerSource.Poll();
                        }
                    }
                    catch { }
                }

                if (statusTimer.ElapsedMilliseconds >= 2000)
                {
                    var lp = session.LastLeaderState?.Position;
                    var fp = session.CurrentFollowerState?.Position;
                    var dist = lp is not null && fp is not null
                        ? fp.Value.DistanceTo(lp.Value).ToString("F1")
                        : "-";
                    Console.WriteLine($"[Follower] Connected={session.IsConnectedToLeader} " +
                        $"LeaderPos=({lp?.X:F1},{lp?.Y:F1},{lp?.Z:F1}) " +
                        $"MyPos=({fp?.X:F1},{fp?.Y:F1},{fp?.Z:F1}) " +
                        $"Dist={dist} " +
                        $"LastAssist={session.LastAssistedTarget ?? "-"}");
                    statusTimer.Restart();
                    hwnd = WindowCaptureService.FindRiftWindow();
                }

                await Task.Delay(50, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            await session.DisposeAsync();
        }

        Console.WriteLine("MultiBox follower stopped.");
        return 0;
    }

    private int RunMultiBoxSmoke()
    {
        Console.WriteLine("FollowMe");
        Console.WriteLine("Mode: multibox-smoke");

        // Round-trip PlayerPosition frame
        var posSnapshot = new PlayerPositionSnapshot(123.45f, 67.89f, -234.56f);
        var posBytes = FrameProtocol.BuildPlayerPositionFrameBytes(_profile.NumericId, 1, posSnapshot);
        var posImage = ColorStripRenderer.Render(_profile, posBytes);
        var posValidation = ColorStripAnalyzer.Analyze(posImage, _profile);
        var posOk = posValidation.IsAccepted && posValidation.Frame is PlayerPositionFrame pf
            && Math.Abs(pf.Payload.X - posSnapshot.X) < 0.02f
            && Math.Abs(pf.Payload.Y - posSnapshot.Y) < 0.02f
            && Math.Abs(pf.Payload.Z - posSnapshot.Z) < 0.02f;
        Console.WriteLine($"PlayerPosition round-trip: {(posOk ? "PASS" : "FAIL")} ({posValidation.Reason})");
        if (posValidation.Frame is PlayerPositionFrame ppf)
        {
            Console.WriteLine($"  In:  ({posSnapshot.X:F2}, {posSnapshot.Y:F2}, {posSnapshot.Z:F2})");
            Console.WriteLine($"  Out: ({ppf.Payload.X:F2}, {ppf.Payload.Y:F2}, {ppf.Payload.Z:F2})");
        }

        // Round-trip MultiBoxState frame
        var mbSnapshot = new MultiBoxStateSnapshot(0b00000110, "TargetMob");
        var mbBytes = FrameProtocol.BuildMultiBoxStateFrameBytes(_profile.NumericId, 2, mbSnapshot);
        var mbImage = ColorStripRenderer.Render(_profile, mbBytes);
        var mbValidation = ColorStripAnalyzer.Analyze(mbImage, _profile);
        var mbOk = mbValidation.IsAccepted && mbValidation.Frame is MultiBoxStateFrame mf
            && mf.Payload.TargetName == mbSnapshot.TargetName
            && mf.Payload.Flags == mbSnapshot.Flags;
        Console.WriteLine($"MultiBoxState round-trip: {(mbOk ? "PASS" : "FAIL")} ({mbValidation.Reason})");
        if (mbValidation.Frame is MultiBoxStateFrame mmf)
        {
            Console.WriteLine($"  In:  flags={mbSnapshot.Flags} name={mbSnapshot.TargetName}");
            Console.WriteLine($"  Out: flags={mmf.Payload.Flags} name={mmf.Payload.TargetName} " +
                $"hostile={mmf.Payload.TargetHostile} hasTarget={mmf.Payload.HasTarget}");
        }

        // Distance/bearing helpers
        var a = new PlayerPositionSnapshot(0f, 0f, 0f);
        var b = new PlayerPositionSnapshot(3f, 0f, 4f);
        var expectedDist = 5f;
        var actualDist = a.DistanceTo(b);
        var distOk = Math.Abs(actualDist - expectedDist) < 0.01f;
        Console.WriteLine($"DistanceTo: {(distOk ? "PASS" : "FAIL")} expected={expectedDist} actual={actualDist:F4}");

        var allPassed = posOk && mbOk && distOk;
        Console.WriteLine($"Overall: {(allPassed ? "PASS" : "FAIL")}");
        return allPassed ? 0 : 1;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("FollowMe CLI");
        Console.WriteLine("  smoke");
        Console.WriteLine("  replay <bmpPath>");
        Console.WriteLine("  live [sampleCount] [sleepMs] [--backend desktopdup|screen|printwindow]");
        Console.WriteLine("  watch [durationSeconds] [sleepMs] [--backend desktopdup|screen|printwindow]");
        Console.WriteLine("  bench");
        Console.WriteLine("  capture-dump [--backend desktopdup|screen|printwindow]");
        Console.WriteLine("  prepare-window [left] [top]");
        Console.WriteLine("  multibox-leader [configPath]   — run as multibox leader");
        Console.WriteLine("  multibox-follower [configPath] — run as multibox follower");
        Console.WriteLine("  multibox-smoke                 — protocol round-trip smoke test (no game needed)");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static double Percentile(double[] samples, double percentile)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        Array.Sort(samples);
        var index = (int)Math.Ceiling(percentile * samples.Length) - 1;
        return samples[Math.Clamp(index, 0, samples.Length - 1)];
    }

    private static bool TryParseCaptureInvocation(string[] args, int startIndex, out CaptureInvocation invocation, out string error)
    {
        var positionals = new List<string>();
        CaptureBackend? backendOverride = null;

        for (var index = startIndex; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("--backend", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    invocation = default;
                    error = "--backend requires a value.";
                    return false;
                }

                if (!TryParseBackendName(args[index + 1], out var parsedBackend))
                {
                    invocation = default;
                    error = $"Unsupported backend: {args[index + 1]}.";
                    return false;
                }

                backendOverride = parsedBackend;
                index++;
                continue;
            }

            if (argument.StartsWith("--backend=", StringComparison.OrdinalIgnoreCase))
            {
                var backendText = argument.Substring("--backend=".Length);
                if (!TryParseBackendName(backendText, out var parsedBackend))
                {
                    invocation = default;
                    error = $"Unsupported backend: {backendText}.";
                    return false;
                }

                backendOverride = parsedBackend;
                continue;
            }

            positionals.Add(argument);
        }

        invocation = new CaptureInvocation(
            positionals,
            backendOverride is null ? DefaultCaptureBackends : new[] { backendOverride.Value });
        error = string.Empty;
        return true;
    }

    private static bool TryParseBackendName(string text, out CaptureBackend backend)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "desktopdup":
            case "desktop-dup":
            case "desktopduplication":
                backend = CaptureBackend.DesktopDuplication;
                return true;
            case "screen":
            case "bitblt":
                backend = CaptureBackend.ScreenBitBlt;
                return true;
            case "printwindow":
            case "print-window":
                backend = CaptureBackend.PrintWindow;
                return true;
            default:
                backend = default;
                return false;
        }
    }

    private sealed record CaptureAttempt(
        CaptureBackend Backend,
        CaptureResult? Capture,
        FrameValidationResult? Validation,
        FrameSignalStats? Signal,
        double CaptureElapsedMs,
        double DecodeElapsedMs,
        string? FailureReason);

    private readonly record struct CaptureInvocation(
        IReadOnlyList<string> Positionals,
        IReadOnlyList<CaptureBackend> Backends);
}
