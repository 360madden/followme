// FollowMe Inspector | v0.2.1 | 9,841 chars
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Windows.Forms;
using FollowMe.Reader;
using Timer = System.Windows.Forms.Timer;

namespace FollowMe.Inspector;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InspectorForm(args));
    }
}

// ── Palette ──────────────────────────────────────────────────────────
internal static class Theme
{
    public static readonly Color Background  = Color.FromArgb(28,  28,  28);
    public static readonly Color Surface     = Color.FromArgb(37,  37,  38);
    public static readonly Color Toolbar     = Color.FromArgb(45,  45,  48);
    public static readonly Color Border      = Color.FromArgb(63,  63,  70);
    public static readonly Color Text        = Color.FromArgb(212, 212, 212);
    public static readonly Color Dim         = Color.FromArgb(110, 110, 118);
    public static readonly Color Accent      = Color.FromArgb(86,  156, 214);
    public static readonly Color Accepted    = Color.FromArgb(78,  201, 176);
    public static readonly Color Rejected    = Color.FromArgb(244,  71,  71);
    public static readonly Color Warning     = Color.FromArgb(220, 200,  80);
    public static readonly Color Hex         = Color.FromArgb(206, 145, 120);
    public static readonly Color Section     = Color.FromArgb(197, 134, 192);
}

// ── Dark ToolStrip renderer ──────────────────────────────────────────
internal sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    private sealed class DarkTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin   => Theme.Toolbar;
        public override Color ToolStripGradientMiddle  => Theme.Toolbar;
        public override Color ToolStripGradientEnd     => Theme.Toolbar;
        public override Color StatusStripGradientBegin => Theme.Surface;
        public override Color StatusStripGradientEnd   => Theme.Surface;
        public override Color SeparatorDark            => Theme.Border;
        public override Color SeparatorLight           => Theme.Border;
        public override Color ButtonSelectedHighlight  => Theme.Border;
        public override Color ButtonSelectedBorder     => Theme.Border;
    }

    public DarkRenderer() : base(new DarkTable()) { }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected && !e.Item.Pressed) return;
        using var b = new SolidBrush(Color.FromArgb(65, 65, 68));
        e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
    }
}

// ── Inspector form ───────────────────────────────────────────────────
internal sealed class InspectorForm : Form
{
    private readonly StripPreviewControl _preview = new();
    private readonly SegmentConfidenceBar _confidenceBar = new() { Height = 52, Dock = DockStyle.Bottom };
    private readonly RichTextBox _details = new()
    {
        Dock        = DockStyle.Fill,
        ReadOnly    = true,
        ScrollBars  = RichTextBoxScrollBars.Both,
        Font        = new Font("Consolas", 9.5f),
        BackColor   = Color.FromArgb(37, 37, 38),
        ForeColor   = Color.FromArgb(212, 212, 212),
        BorderStyle = BorderStyle.None,
        WordWrap    = false,
    };

    private readonly ToolStripStatusLabel _statusLabel = new()
    {
        Spring    = true,
        Text      = "No capture loaded",
        ForeColor = Theme.Dim,
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly ToolStripStatusLabel _pathLabel = new()
    {
        Text      = string.Empty,
        ForeColor = Theme.Dim,
        TextAlign = ContentAlignment.MiddleRight,
        AutoSize  = false,
        Width     = 580,
        Overflow  = ToolStripItemOverflow.Never,
    };
    private readonly ToolStripButton _autoRefreshBtn = new()
    {
        Text           = "Auto Refresh",
        CheckOnClick   = true,
        Checked        = false,
        ForeColor      = Theme.Text,
        DisplayStyle   = ToolStripItemDisplayStyle.Text,
    };

    private readonly Timer _timer = new() { Interval = 1000 };
    private string? _currentPath;
    private DateTime _currentWriteTime;

    public InspectorForm(string[] args)
    {
        Text        = "FollowMe Inspector";
        Width       = 1400;
        Height      = 820;
        MinimumSize = new Size(900, 600);
        BackColor   = Theme.Background;
        ForeColor   = Theme.Text;

        // Toolbar
        var toolbar = new ToolStrip
        {
            Renderer   = new DarkRenderer(),
            BackColor  = Theme.Toolbar,
            ForeColor  = Theme.Text,
            GripStyle  = ToolStripGripStyle.Hidden,
            Padding    = new Padding(6, 2, 6, 2),
        };
        var openBtn = new ToolStripButton("Open BMP…")   { ForeColor = Theme.Text, DisplayStyle = ToolStripItemDisplayStyle.Text };
        var latestBtn = new ToolStripButton("Load Latest") { ForeColor = Theme.Text, DisplayStyle = ToolStripItemDisplayStyle.Text };
        openBtn.Click   += (_, _) => OpenBmp();
        latestBtn.Click += (_, _) => LoadLatestCapture(force: true);
        toolbar.Items.AddRange(new ToolStripItem[]
        {
            openBtn,
            new ToolStripSeparator(),
            latestBtn,
            new ToolStripSeparator(),
            _autoRefreshBtn,
        });

        // Status bar
        var statusBar = new StatusStrip
        {
            BackColor  = Theme.Surface,
            ForeColor  = Theme.Text,
            Renderer   = new DarkRenderer(),
            SizingGrip = false,
        };
        statusBar.Items.Add(_statusLabel);
        statusBar.Items.Add(new ToolStripSeparator());
        statusBar.Items.Add(_pathLabel);

        // Left: preview + confidence bar
        var previewScroll = new Panel { AutoScroll = true, BackColor = Theme.Background, Dock = DockStyle.Fill };
        previewScroll.Controls.Add(_preview);
        var leftPanel = new Panel { BackColor = Theme.Background, Dock = DockStyle.Fill };
        leftPanel.Controls.Add(previewScroll);
        leftPanel.Controls.Add(_confidenceBar);

        // Splitter
        var split = new SplitContainer
        {
            Dock          = DockStyle.Fill,
            Orientation   = Orientation.Vertical,
            BackColor     = Theme.Border,
            Panel1MinSize = 200,
            Panel2MinSize = 300,
        };
        split.Panel1.BackColor = Theme.Background;
        split.Panel1.Controls.Add(leftPanel);
        split.Panel2.BackColor = Theme.Surface;
        split.Panel2.Controls.Add(_details);

        Controls.Add(split);
        Controls.Add(toolbar);
        Controls.Add(statusBar);

        Load += (_, _) =>
            split.SplitterDistance = Math.Max(split.Panel1MinSize, (int)(ClientSize.Width * 0.55));

        _timer.Tick += (_, _) => { if (_autoRefreshBtn.Checked) LoadLatestCapture(force: false); };
        _timer.Start();

        if (args.Length > 0 && File.Exists(args[0]))
            LoadFromPath(args[0]);
        else
            LoadLatestCapture(force: true);
    }

    private void OpenBmp()
    {
        using var dlg = new OpenFileDialog { Filter = "BMP files (*.bmp)|*.bmp|All files (*.*)|*.*", Title = "Open FollowMe Capture" };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadFromPath(dlg.FileName);
    }

    private void LoadLatestCapture(bool force)
    {
        var latest = PathProvider.GetLatestCapturePath();
        if (string.IsNullOrWhiteSpace(latest) || !File.Exists(latest)) return;
        var wt = File.GetLastWriteTimeUtc(latest);
        if (!force && string.Equals(latest, _currentPath, StringComparison.OrdinalIgnoreCase) && wt == _currentWriteTime) return;
        LoadFromPath(latest);
    }

    private void LoadFromPath(string path)
    {
        var frame    = BmpIO.Load(path);
        var analysis = ColorStripAnalyzer.Analyze(frame, StripProfiles.Default);
        _preview.SetFrame(frame, analysis);
        _confidenceBar.SetSamples(analysis.Samples, StripProfiles.Default.SegmentCount);
        BuildDetails(_details, path, frame, analysis);
        _currentPath      = path;
        _currentWriteTime = File.GetLastWriteTimeUtc(path);

        var name = Path.GetFileName(path);
        if (analysis.IsAccepted)
        {
            _statusLabel.Text      = $"✓  Accepted — {name}";
            _statusLabel.ForeColor = Theme.Accepted;
        }
        else
        {
            _statusLabel.Text      = $"✗  Rejected: {analysis.Reason} — {name}";
            _statusLabel.ForeColor = Theme.Rejected;
        }
        _pathLabel.Text = path;
    }

    // ── Details builder ──────────────────────────────────────────────

    private static void BuildDetails(RichTextBox rtb, string path, Bgr24Frame frame, FrameValidationResult analysis)
    {
        rtb.SuspendLayout();
        rtb.Clear();

        Sec(rtb, "CAPTURE");
        KV(rtb, "Path",     path);
        KV(rtb, "Size",     $"{frame.Width}x{frame.Height}");
        KV(rtb, "Accepted", analysis.IsAccepted ? "true" : "false", analysis.IsAccepted ? Theme.Accepted : Theme.Rejected);
        KV(rtb, "Reason",   analysis.Reason,                        analysis.IsAccepted ? Theme.Accepted : Theme.Rejected);

        var sidecar = Path.ChangeExtension(path, ".json");
        if (File.Exists(sidecar)) AppendSidecar(rtb, sidecar);

        if (analysis.Detection is { } det)
        {
            Nl(rtb); Sec(rtb, "DETECTION");
            KV(rtb, "SearchMode",        det.SearchMode);
            KV(rtb, "Origin",            $"{det.OriginX}, {det.OriginY}");
            KV(rtb, "Pitch",             $"{det.Pitch:F3}");
            KV(rtb, "Scale",             $"{det.Scale:F3}");
            KV(rtb, "ControlError",      $"{det.ControlError:F4}",      det.ControlError      < 0.05 ? Theme.Accepted : Theme.Warning);
            KV(rtb, "LeftControlScore",  $"{det.LeftControlScore:F4}",  det.LeftControlScore  < 0.05 ? Theme.Accepted : Theme.Warning);
            KV(rtb, "RightControlScore", $"{det.RightControlScore:F4}", det.RightControlScore < 0.05 ? Theme.Accepted : Theme.Warning);
            KV(rtb, "AnchorLumaDelta",   $"{det.AnchorLumaDelta:F2}");
            var lc = StripProfiles.Default.LeftControl;
            var rc = StripProfiles.Default.RightControl;
            KV(rtb, "LeftExpected",      Pat(lc));
            KV(rtb, "LeftObserved",      Obs(analysis.Samples, 0, lc.Length));
            KV(rtb, "RightExpected",     Pat(rc));
            KV(rtb, "RightObserved",     Obs(analysis.Samples, StripProfiles.Default.SegmentCount - rc.Length, rc.Length));
        }

        if (analysis.ParseResult is { } pr)
        {
            Nl(rtb); Sec(rtb, "TRANSPORT");
            KV(rtb, "ParseAccepted",         pr.IsAccepted ? "true" : "false", pr.IsAccepted ? Theme.Accepted : Theme.Rejected);
            KV(rtb, "ParseReason",           pr.Reason,                        pr.IsAccepted ? Theme.Accepted : Theme.Rejected);
            KVBool(rtb, "MagicValid",            pr.MagicValid);
            KVBool(rtb, "ProtocolProfileValid",  pr.ProtocolProfileValid);
            KVBool(rtb, "FrameSchemaValid",      pr.FrameSchemaValid);
            KVBool(rtb, "HeaderCrcValid",        pr.HeaderCrcValid);
            KVBool(rtb, "PayloadCrcValid",       pr.PayloadCrcValid);
            App(rtb, $"  {"TransportBytes",-22}", Theme.Dim);
            App(rtb, BitConverter.ToString(pr.TransportBytes) + "\n", Theme.Hex);
        }

        if (analysis.Frame is { } f)
        {
            Nl(rtb); Sec(rtb, "HEADER");
            KV(rtb, "Protocol",  f.Header.ProtocolVersion.ToString());
            KV(rtb, "Profile",   f.Header.ProfileId.ToString());
            KV(rtb, "FrameType", f.Header.FrameType.ToString());
            KV(rtb, "Schema",    f.Header.SchemaId.ToString());
            KV(rtb, "Sequence",  f.Header.Sequence.ToString());
            Nl(rtb); Sec(rtb, "PAYLOAD");
            AppendPayload(rtb, f);
        }

        Nl(rtb); Sec(rtb, $"SEGMENTS  ({analysis.Samples.Count})");
        foreach (var s in analysis.Samples)
        {
            var cc = s.Confidence >= 0.8 ? Theme.Accepted : s.Confidence >= 0.5 ? Theme.Warning : Theme.Rejected;
            App(rtb, $"  {s.SegmentIndex:00}  ",                                         Theme.Dim);
            App(rtb, $"sym={s.Symbol} ",                                                  Theme.Accent);
            App(rtb, "conf=",                                                              Theme.Dim);
            App(rtb, $"{s.Confidence:F3}  ",                                              cc);
            App(rtb, $"dist={s.Distance:F3}  2nd={s.SecondChoiceSymbol}/{s.SecondChoiceDistance:F3}  ", Theme.Dim);
            App(rtb, $"rgb=({s.SampleColor.R},{s.SampleColor.G},{s.SampleColor.B})\n",   Theme.Text);
            foreach (var p in s.Probes)
                App(rtb, $"       ({p.X},{p.Y})  rgb=({p.SampleColor.R},{p.SampleColor.G},{p.SampleColor.B})\n", Theme.Dim);
        }

        rtb.ResumeLayout();
        rtb.SelectionStart = 0;
        rtb.ScrollToCaret();
    }

    private static void AppendSidecar(RichTextBox rtb, string path)
    {
        Nl(rtb); Sec(rtb, "SIDECAR");
        using var doc  = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.TryGetProperty("backend",     out var be))   KV(rtb, "Backend",     be.GetString() ?? "");
        if (root.TryGetProperty("clientRect",  out var cr))   KV(rtb, "ClientRect",  RectStr(cr));
        if (root.TryGetProperty("captureRect", out var cap))  KV(rtb, "CaptureRect", RectStr(cap));
        static string RectStr(JsonElement e) =>
            $"{e.GetProperty("left").GetInt32()},{e.GetProperty("top").GetInt32()} {e.GetProperty("width").GetInt32()}x{e.GetProperty("height").GetInt32()}";
    }

    private static void AppendPayload(RichTextBox rtb, TelemetryFrame frame)
    {
        switch (frame)
        {
            case CoreStatusFrame c:
                KV(rtb, "PlayerFlags",    c.Payload.PlayerStateFlags.ToString());
                KV(rtb, "PlayerHealth%",  $"{c.Payload.PlayerHealthPctQ8 / 256.0 * 100:F1}%  (Q8={c.Payload.PlayerHealthPctQ8})");
                KV(rtb, "PlayerResource", $"kind={c.Payload.PlayerResourceKind}  {c.Payload.PlayerResourcePctQ8 / 256.0 * 100:F1}%");
                KV(rtb, "TargetFlags",    c.Payload.TargetStateFlags.ToString());
                KV(rtb, "TargetHealth%",  $"{c.Payload.TargetHealthPctQ8 / 256.0 * 100:F1}%  (Q8={c.Payload.TargetHealthPctQ8})");
                KV(rtb, "TargetResource", $"kind={c.Payload.TargetResourceKind}  {c.Payload.TargetResourcePctQ8 / 256.0 * 100:F1}%");
                KV(rtb, "PlayerLevel",    c.Payload.PlayerLevel.ToString());
                KV(rtb, "TargetLevel",    c.Payload.TargetLevel.ToString());
                KV(rtb, "PlayerCall/Role",$"{c.Payload.PlayerCallingRolePacked >> 4} / {c.Payload.PlayerCallingRolePacked & 0x0F}");
                KV(rtb, "TargetCall/Rel", $"{c.Payload.TargetCallingRelationPacked >> 4} / {c.Payload.TargetCallingRelationPacked & 0x0F}");
                break;

            case PlayerStatsPageFrame s:
                switch (s.Payload)
                {
                    case PlayerVitalsStatsPagePayload v:
                        KV(rtb, "Page",     "Vitals");
                        KV(rtb, "Health",   $"{v.Snapshot.HealthCurrent} / {v.Snapshot.HealthMax}");
                        KV(rtb, "Resource", $"{v.Snapshot.ResourceCurrent} / {v.Snapshot.ResourceMax}");
                        break;
                    case PlayerMainStatsPagePayload m:
                        KV(rtb, "Page",         "Main");
                        KV(rtb, "Armor",        m.Snapshot.Armor.ToString());
                        KV(rtb, "Strength",     m.Snapshot.Strength.ToString());
                        KV(rtb, "Dexterity",    m.Snapshot.Dexterity.ToString());
                        KV(rtb, "Intelligence", m.Snapshot.Intelligence.ToString());
                        KV(rtb, "Wisdom",       m.Snapshot.Wisdom.ToString());
                        KV(rtb, "Endurance",    m.Snapshot.Endurance.ToString());
                        break;
                    case PlayerOffenseStatsPagePayload o:
                        KV(rtb, "Page",         "Offense");
                        KV(rtb, "AttackPower",  o.Snapshot.AttackPower.ToString());
                        KV(rtb, "PhysicalCrit", o.Snapshot.PhysicalCrit.ToString());
                        KV(rtb, "Hit",          o.Snapshot.Hit.ToString());
                        KV(rtb, "SpellPower",   o.Snapshot.SpellPower.ToString());
                        KV(rtb, "SpellCrit",    o.Snapshot.SpellCrit.ToString());
                        KV(rtb, "CritPower",    o.Snapshot.CritPower.ToString());
                        break;
                    case PlayerDefenseStatsPagePayload d:
                        KV(rtb, "Page",  "Defense");
                        KV(rtb, "Dodge", d.Snapshot.Dodge.ToString());
                        KV(rtb, "Block", d.Snapshot.Block.ToString());
                        break;
                    case PlayerResistanceStatsPagePayload r:
                        KV(rtb, "Page",  "Resistances");
                        KV(rtb, "Life",  r.Snapshot.LifeResist.ToString());
                        KV(rtb, "Death", r.Snapshot.DeathResist.ToString());
                        KV(rtb, "Fire",  r.Snapshot.FireResist.ToString());
                        KV(rtb, "Water", r.Snapshot.WaterResist.ToString());
                        KV(rtb, "Earth", r.Snapshot.EarthResist.ToString());
                        KV(rtb, "Air",   r.Snapshot.AirResist.ToString());
                        break;
                }
                break;
        }
    }

    // ── RTB helpers ──────────────────────────────────────────────────
    private static void Sec(RichTextBox r, string t)              => App(r, $"  {t}\n",               Theme.Section);
    private static void Nl(RichTextBox r)                         => App(r, "\n",                      Theme.Dim);
    private static void KV(RichTextBox r, string k, string v, Color? vc = null)
    { App(r, $"  {k,-22}", Theme.Dim); App(r, v + "\n", vc ?? Theme.Text); }
    private static void KVBool(RichTextBox r, string k, bool v)  => KV(r, k, v ? "true" : "false",   v ? Theme.Accepted : Theme.Rejected);
    private static void App(RichTextBox r, string t, Color c)
    { r.SelectionStart = r.TextLength; r.SelectionLength = 0; r.SelectionColor = c; r.AppendText(t); }
    private static string Pat(IEnumerable<byte> s) => string.Join(" ", s.Select(static b => b.ToString()));
    private static string Obs(IReadOnlyList<SegmentSample> s, int start, int len) =>
        string.Join(" ", s.Where(x => x.SegmentIndex >= start && x.SegmentIndex < start + len)
                          .OrderBy(x => x.SegmentIndex)
                          .Select(x => x.Symbol.ToString()));
}

// ── Strip preview ────────────────────────────────────────────────────
internal sealed class StripPreviewControl : Control
{
    private const int Zoom = 2;
    private Bitmap? _bmp;
    private FrameValidationResult? _analysis;

    public StripPreviewControl() { DoubleBuffered = true; BackColor = Theme.Background; }

    public void SetFrame(Bgr24Frame frame, FrameValidationResult analysis)
    {
        _bmp?.Dispose();
        _bmp      = ToBitmap(frame);
        _analysis = analysis;
        Width     = frame.Width  * Zoom;
        Height    = frame.Height * Zoom;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_bmp is null) return;

        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode   = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_bmp, new Rectangle(0, 0, _bmp.Width * Zoom, _bmp.Height * Zoom));

        if (_analysis?.Detection is null) return;

        var det  = _analysis.Detection;
        var prof = StripProfiles.Default;
        var bh   = (float)(prof.SegmentHeight * det.Scale * Zoom);
        var bw   = (float)(prof.SegmentCount  * det.Pitch * Zoom);
        var ox   = (float)(det.OriginX * Zoom);
        var oy   = (float)(det.OriginY * Zoom);

        using var roiPen  = new Pen(Color.FromArgb(160, 220, 180, 30), 1);
        using var divPen  = new Pen(Color.FromArgb(100, 80,  160, 255), 1);
        using var probeBr = new SolidBrush(Color.FromArgb(200, 255, 80, 180));

        e.Graphics.DrawRectangle(roiPen, ox, oy, bw, bh);
        for (var i = 0; i <= prof.SegmentCount; i++)
        {
            var x = ox + (float)(i * det.Pitch * Zoom);
            e.Graphics.DrawLine(divPen, x, oy, x, oy + bh);
        }
        foreach (var s in _analysis.Samples)
            foreach (var p in s.Probes)
                e.Graphics.FillEllipse(probeBr, p.X * Zoom - 2, p.Y * Zoom - 2, 4, 4);
    }

    private static Bitmap ToBitmap(Bgr24Frame frame)
    {
        var bmp = new Bitmap(frame.Width, frame.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < frame.Height; y++)
            for (var x = 0; x < frame.Width; x++)
            {
                var c = frame.GetColor(x, y);
                bmp.SetPixel(x, y, Color.FromArgb(c.R, c.G, c.B));
            }
        return bmp;
    }
}

// ── Segment confidence bar ───────────────────────────────────────────
internal sealed class SegmentConfidenceBar : Control
{
    private IReadOnlyList<SegmentSample>? _samples;
    private int _count;

    public SegmentConfidenceBar() { DoubleBuffered = true; BackColor = Theme.Surface; }

    public void SetSamples(IReadOnlyList<SegmentSample> samples, int count)
    {
        _samples = samples;
        _count   = count;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_samples is null || _count == 0) return;

        var g     = e.Graphics;
        var w     = ClientSize.Width;
        var h     = ClientSize.Height;
        var segW  = (float)w / _count;
        const int padT  = 16;
        const int padB  = 3;
        var barH  = h - padT - padB;
        if (barH <= 2) return;

        using var labelFont = new Font("Consolas", 7f);
        using var labelBr   = new SolidBrush(Theme.Dim);
        g.DrawString("CONFIDENCE", labelFont, labelBr, 3, 2);

        foreach (var s in _samples)
        {
            var fillH  = Math.Max(1f, (float)(s.Confidence * barH));
            var x      = s.SegmentIndex * segW;
            var y      = padT + barH - fillH;
            var barC   = s.Confidence >= 0.8f ? Theme.Accepted : s.Confidence >= 0.5f ? Theme.Warning : Theme.Rejected;
            using var br = new SolidBrush(Color.FromArgb(190, barC));
            g.FillRectangle(br, x + 0.5f, y, Math.Max(1f, segW - 1f), fillH);
        }

        using var linePen = new Pen(Theme.Border, 1);
        g.DrawLine(linePen, 0, padT + barH, w, padT + barH);
    }
}
// FollowMe Inspector | v0.2.1 | 9,841 chars
