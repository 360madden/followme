// Version: 0.3.0
// Purpose: Live desktop HUD for FollowMe player vitals, stats, and MultiBox status.

using System.Drawing;
using System.Windows.Forms;
using FollowMe.MultiBox.Config;
using FollowMe.MultiBox.Session;
using FollowMe.MultiBox.Sources;
using FollowMe.Reader;
using Timer = System.Windows.Forms.Timer;

namespace FollowMe.Hud;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new HudForm());
    }
}

internal sealed class HudForm : Form
{
    private readonly StripProfile _profile = StripProfiles.Default;
    private readonly TelemetryAggregate _aggregate = new();
    private readonly Timer _refreshTimer = new() { Interval = 50 };
    private readonly Label _statusLabel = new();
    private readonly Label _playerHeaderLabel = new();
    private readonly CheckBox _alwaysOnTopCheckBox = new();
    private readonly Dictionary<string, Label> _valueLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GroupBox> _groups = new(StringComparer.OrdinalIgnoreCase);
    private bool _polling;
    private CaptureBackend _preferredBackend = CaptureBackend.DesktopDuplication;
    private string _lastBackend = "-";
    private string _lastReason = "Waiting for first accepted frame.";
    private int _acceptedFrames;
    private int _rejectedFrames;

    // ── MultiBox ──────────────────────────────────────────────────────────────
    private readonly MultiBoxConfig _multiBoxConfig = MultiBoxConfig.LoadOrDefault();
    private MultiBoxLeaderSession? _leaderSession;
    private MultiBoxFollowerSession? _followerSession;
    private TelemetryLeaderStateSource? _leaderSource;
    private TelemetryFollowerStateSource? _followerSource;
    private CancellationTokenSource? _multiBoxCts;

    public HudForm()
    {
        Text = "FollowMe HUD";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 620;
        Height = 780;
        MinimumSize = new Size(560, 640);
        BackColor = Color.FromArgb(17, 17, 20);
        ForeColor = Color.Gainsboro;
        TopMost = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            BackColor = BackColor,
            Padding = new Padding(10)
        };
        root.RowCount = 9;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 16));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 16));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 16));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 12));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 16));

        _playerHeaderLabel.AutoSize = true;
        _playerHeaderLabel.Font = new Font("Segoe UI", 14.0f, FontStyle.Bold);
        _playerHeaderLabel.Text = "Player Stats HUD";
        _playerHeaderLabel.Margin = new Padding(3, 0, 3, 6);

        _statusLabel.AutoSize = true;
        _statusLabel.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);
        _statusLabel.Text = "Status: waiting for RIFT window.";
        _statusLabel.Margin = new Padding(3, 0, 3, 8);

        _alwaysOnTopCheckBox.AutoSize = true;
        _alwaysOnTopCheckBox.Text = "Always on top";
        _alwaysOnTopCheckBox.Checked = true;
        _alwaysOnTopCheckBox.ForeColor = Color.Gainsboro;
        _alwaysOnTopCheckBox.Margin = new Padding(3, 0, 3, 10);
        _alwaysOnTopCheckBox.CheckedChanged += (_, _) => TopMost = _alwaysOnTopCheckBox.Checked;

        root.Controls.Add(_playerHeaderLabel, 0, 0);
        root.Controls.Add(_statusLabel, 0, 1);
        root.Controls.Add(_alwaysOnTopCheckBox, 0, 2);
        root.Controls.Add(BuildVitalsGroup(), 0, 3);
        root.Controls.Add(BuildMainGroup(), 0, 4);
        root.Controls.Add(BuildOffenseGroup(), 0, 5);
        root.Controls.Add(BuildDefenseGroup(), 0, 6);
        root.Controls.Add(BuildResistanceGroup(), 0, 7);
        root.Controls.Add(BuildMultiBoxGroup(), 0, 8);
        Controls.Add(root);

        _refreshTimer.Tick += async (_, _) => await PollTelemetryAsync();
        _refreshTimer.Start();

        StartMultiBoxSession();
        UpdateHud();

        FormClosing += async (_, _) => await StopMultiBoxSessionAsync();
    }

    // ── MultiBox session management ───────────────────────────────────────────

    private void StartMultiBoxSession()
    {
        if (_multiBoxConfig.Mode == MultiBoxMode.Off) return;

        _multiBoxCts = new CancellationTokenSource();

        if (_multiBoxConfig.Mode == MultiBoxMode.Leader)
        {
            var (session, source) = MultiBoxSessionFactory.CreateLeader(_multiBoxConfig, _aggregate);
            _leaderSession = session;
            _leaderSource = source;
            _ = session.StartAsync(_multiBoxCts.Token);
        }
        else if (_multiBoxConfig.Mode == MultiBoxMode.Follower)
        {
            var (session, source) = MultiBoxSessionFactory.CreateFollower(_multiBoxConfig, _aggregate);
            _followerSession = session;
            _followerSource = source;
            _ = session.StartAsync(_multiBoxCts.Token);
        }
    }

    private async Task StopMultiBoxSessionAsync()
    {
        _multiBoxCts?.Cancel();
        if (_leaderSession is not null) await _leaderSession.DisposeAsync();
        if (_followerSession is not null) await _followerSession.DisposeAsync();
    }

    private Control BuildMultiBoxGroup()
    {
        return BuildGroup(
            "MultiBox",
            ("Mode", "mbMode"),
            ("TCP Status", "mbTcp"),
            ("Leader Pos", "mbLeaderPos"),
            ("Follower Pos", "mbFollowerPos"),
            ("Distance", "mbDistance"),
            ("Target", "mbTarget"),
            ("Last Assist", "mbLastAssist"));
    }

    private Control BuildVitalsGroup()
    {
        return BuildGroup(
            "Vitals",
            ("Level", "level"),
            ("Health", "health"),
            ("Resource", "resource"),
            ("Core Age", "coreAge"),
            ("Vitals Age", "vitalsAge"));
    }

    private Control BuildMainGroup()
    {
        return BuildGroup(
            "Main",
            ("Armor", "armor"),
            ("Strength", "strength"),
            ("Dexterity", "dexterity"),
            ("Intelligence", "intelligence"),
            ("Wisdom", "wisdom"),
            ("Endurance", "endurance"));
    }

    private Control BuildOffenseGroup()
    {
        return BuildGroup(
            "Offense",
            ("Attack Power", "attackPower"),
            ("Physical Crit", "physicalCrit"),
            ("Hit", "hit"),
            ("Spell Power", "spellPower"),
            ("Spell Crit", "spellCrit"),
            ("Crit Power", "critPower"));
    }

    private Control BuildDefenseGroup()
    {
        return BuildGroup(
            "Defense",
            ("Dodge", "dodge"),
            ("Block", "block"),
            ("Guard", "guard"),
            ("Page Age", "defenseAge"));
    }

    private Control BuildResistanceGroup()
    {
        return BuildGroup(
            "Resistances",
            ("Life Resist", "lifeResist"),
            ("Death Resist", "deathResist"),
            ("Fire Resist", "fireResist"),
            ("Water Resist", "waterResist"),
            ("Earth Resist", "earthResist"),
            ("Air Resist", "airResist"),
            ("Page Age", "resistAge"));
    }

    private GroupBox BuildGroup(string title, params (string Label, string Key)[] rows)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(24, 24, 28),
            Margin = new Padding(0, 0, 0, 10)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rows.Length,
            BackColor = group.BackColor
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows.Length));

            var nameLabel = new Label
            {
                Text = rows[rowIndex].Label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = Color.Silver,
                BackColor = group.BackColor
            };

            var valueLabel = new Label
            {
                Text = "-",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                AutoSize = false,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = group.BackColor
            };

            _valueLabels[rows[rowIndex].Key] = valueLabel;
            grid.Controls.Add(nameLabel, 0, rowIndex);
            grid.Controls.Add(valueLabel, 1, rowIndex);
        }

        group.Controls.Add(grid);
        _groups[title] = group;
        return group;
    }

    private async Task PollTelemetryAsync()
    {
        if (_polling)
        {
            return;
        }

        _polling = true;
        try
        {
            var result = await Task.Run(ReadTelemetry);

            _lastBackend = result.Backend;
            _lastReason = result.Reason;

            if (result.Frame is not null)
            {
                _acceptedFrames++;
                _aggregate.Apply(result.Frame);
                if (Enum.TryParse<CaptureBackend>(result.Backend, ignoreCase: true, out var acceptedBackend))
                {
                    _preferredBackend = acceptedBackend;
                }

                // Poll multibox sources after each frame decode
                _leaderSource?.Poll();
                _followerSource?.Poll();
            }
            else
            {
                _rejectedFrames++;
            }

            UpdateHud();
        }
        finally
        {
            _polling = false;
        }
    }

    private TelemetryReadResult ReadTelemetry()
    {
        var hwnd = WindowCaptureService.FindRiftWindow();
        if (hwnd == nint.Zero)
        {
            return new TelemetryReadResult(null, "-", "RIFT window not found.");
        }

        return TryReadFrame(hwnd);
    }

    private TelemetryReadResult TryReadFrame(nint hwnd)
    {
        var fallback = _preferredBackend == CaptureBackend.DesktopDuplication
            ? CaptureBackend.ScreenBitBlt
            : CaptureBackend.DesktopDuplication;

        var backends = new[] { _preferredBackend, fallback };

        TelemetryReadResult? bestReject = null;
        foreach (var backend in backends)
        {
            try
            {
                var capture = WindowCaptureService.CaptureTopSlice(hwnd, _profile, _profile.CaptureHeight - _profile.BandHeight, backend);
                var validation = ColorStripAnalyzer.Analyze(capture.Image, _profile);
                if (validation.IsAccepted && validation.Frame is not null)
                {
                    return new TelemetryReadResult(validation.Frame, backend.ToString(), validation.Reason);
                }

                bestReject ??= new TelemetryReadResult(null, backend.ToString(), validation.Reason);
            }
            catch (Exception ex)
            {
                bestReject ??= new TelemetryReadResult(null, backend.ToString(), ex.Message);
            }
        }

        return bestReject ?? new TelemetryReadResult(null, _preferredBackend.ToString(), "No capture backend produced a result.");
    }

    private void UpdateHud()
    {
        var snapshot = _aggregate.CreateSnapshot();

        _playerHeaderLabel.Text = snapshot.PlayerLevel is null
            ? "Player Stats HUD"
            : $"Player Stats HUD - Level {snapshot.PlayerLevel}";

        _statusLabel.Text = $"Status: {_lastReason} | Backend: {_lastBackend} | Accepted: {_acceptedFrames} | Rejected: {_rejectedFrames}";
        _statusLabel.ForeColor = _aggregate.CoreFrame is null ? Color.Goldenrod : Color.Silver;

        SetValue("level", snapshot.PlayerLevel?.ToString() ?? "-");
        SetValue("health", FormatPair(snapshot.HealthCurrent, snapshot.HealthMax));
        SetValue("resource", $"{snapshot.ResourceLabel}: {FormatPair(snapshot.ResourceCurrent, snapshot.ResourceMax)}");
        SetValue("coreAge", FormatAge(snapshot.CoreAgeSeconds));
        SetValue("vitalsAge", FormatAge(snapshot.VitalsAgeSeconds));

        SetValue("armor", FormatValue(snapshot.Armor));
        SetValue("strength", FormatValue(snapshot.Strength));
        SetValue("dexterity", FormatValue(snapshot.Dexterity));
        SetValue("intelligence", FormatValue(snapshot.Intelligence));
        SetValue("wisdom", FormatValue(snapshot.Wisdom));
        SetValue("endurance", FormatValue(snapshot.Endurance));

        SetValue("attackPower", FormatValue(snapshot.AttackPower));
        SetValue("physicalCrit", FormatValue(snapshot.PhysicalCrit));
        SetValue("hit", FormatValue(snapshot.Hit));
        SetValue("spellPower", FormatValue(snapshot.SpellPower));
        SetValue("spellCrit", FormatValue(snapshot.SpellCrit));
        SetValue("critPower", FormatValue(snapshot.CritPower));

        SetValue("dodge", FormatValue(snapshot.Dodge));
        SetValue("block", FormatValue(snapshot.Block));
        SetValue("guard", "N/A (not exposed via Inspect.Stat)");
        SetValue("defenseAge", FormatAge(snapshot.DefenseAgeSeconds));

        SetValue("lifeResist", FormatValue(snapshot.LifeResist));
        SetValue("deathResist", FormatValue(snapshot.DeathResist));
        SetValue("fireResist", FormatValue(snapshot.FireResist));
        SetValue("waterResist", FormatValue(snapshot.WaterResist));
        SetValue("earthResist", FormatValue(snapshot.EarthResist));
        SetValue("airResist", FormatValue(snapshot.AirResist));
        SetValue("resistAge", FormatAge(snapshot.ResistanceAgeSeconds));

        UpdateMultiBoxPanel();
        ApplyGroupAge("Vitals", snapshot.VitalsAgeSeconds);
        ApplyGroupAge("Main", snapshot.MainAgeSeconds);
        ApplyGroupAge("Offense", snapshot.OffenseAgeSeconds);
        ApplyGroupAge("Defense", snapshot.DefenseAgeSeconds);
        ApplyGroupAge("Resistances", snapshot.ResistanceAgeSeconds);
    }

    private void UpdateMultiBoxPanel()
    {
        var mode = _multiBoxConfig.Mode.ToString();
        SetValue("mbMode", mode);

        if (_multiBoxConfig.Mode == MultiBoxMode.Off)
        {
            SetValue("mbTcp", "Off");
            SetValue("mbLeaderPos", "-");
            SetValue("mbFollowerPos", "-");
            SetValue("mbDistance", "-");
            SetValue("mbTarget", "-");
            SetValue("mbLastAssist", "-");
            return;
        }

        if (_leaderSession is not null)
        {
            SetValue("mbTcp", _leaderSession.TransportStatus);
            var lp = _leaderSession.LastBroadcastState?.Position;
            SetValue("mbLeaderPos", lp is null ? "-" : $"({lp.Value.X:F1}, {lp.Value.Y:F1}, {lp.Value.Z:F1})");
            SetValue("mbFollowerPos", "N/A (leader)");
            SetValue("mbDistance", "N/A (leader)");
            SetValue("mbTarget", _leaderSession.LastBroadcastState?.MultiBox.TargetName ?? "-");
            SetValue("mbLastAssist", "-");
        }
        else if (_followerSession is not null)
        {
            SetValue("mbTcp", _followerSession.TransportStatus);
            var lp = _followerSession.LastLeaderState?.Position;
            var fp = _followerSession.CurrentFollowerState?.Position;
            SetValue("mbLeaderPos", lp is null ? "-" : $"({lp.Value.X:F1}, {lp.Value.Y:F1}, {lp.Value.Z:F1})");
            SetValue("mbFollowerPos", fp is null ? "-" : $"({fp.Value.X:F1}, {fp.Value.Y:F1}, {fp.Value.Z:F1})");

            var dist = lp is not null && fp is not null
                ? fp.Value.DistanceTo(lp.Value).ToString("F1") + " u"
                : "-";
            SetValue("mbDistance", dist);
            SetValue("mbTarget", _followerSession.LastLeaderState?.MultiBox.TargetName ?? "-");
            SetValue("mbLastAssist", _followerSession.LastAssistedTarget ?? "-");

            // Color the MultiBox group by connection state
            if (_groups.TryGetValue("MultiBox", out var mbGroup))
            {
                mbGroup.ForeColor = _followerSession.IsConnectedToLeader ? Color.LightGreen : Color.Goldenrod;
            }
        }
    }

    private void ApplyGroupAge(string groupName, double? ageSeconds)
    {
        if (!_groups.TryGetValue(groupName, out var group))
        {
            return;
        }

        group.Text = ageSeconds is null ? groupName : $"{groupName} ({ageSeconds.Value:F1}s)";
        group.ForeColor = ageSeconds is not null && ageSeconds.Value > 2.0 ? Color.Goldenrod : Color.Gainsboro;
    }

    private void SetValue(string key, string text)
    {
        if (_valueLabels.TryGetValue(key, out var label))
        {
            label.Text = text;
        }
    }

    private static string FormatPair(uint? current, uint? maximum)
    {
        return current is null || maximum is null ? "-" : $"{current} / {maximum}";
    }

    private static string FormatPair(ushort? current, ushort? maximum)
    {
        return current is null || maximum is null ? "-" : $"{current} / {maximum}";
    }

    private static string FormatValue(ushort? value)
    {
        return value?.ToString() ?? "-";
    }

    private static string FormatAge(double? ageSeconds)
    {
        return ageSeconds is null ? "-" : $"{ageSeconds.Value:F1}s";
    }

    private sealed record TelemetryReadResult(TelemetryFrame? Frame, string Backend, string Reason);
}

// End of script.
