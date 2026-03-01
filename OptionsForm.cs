using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AngryAudio
{
    public class ToggleSwitch : Control
    {
        private bool _checked;
        private bool _hovering;
        public event EventHandler CheckedChanged;
        public bool Checked {
            get { return _checked; }
            set { _checked = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }
        public ToggleSwitch() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            Size = Dpi.Size(40, 20);
            UpdateRegion();
        }
        /// <summary>Delegate to paint parent card background for true transparency on starfield cards.</summary>
        public Action<Graphics, Control> PaintParentBg;

        void UpdateRegion() {
            int w = Width, h = Height;
            var path = new GraphicsPath();
            if (h > 0 && w > 0) {
                path.AddArc(0, 0, h, h, 90, 180);
                path.AddArc(w - h, 0, h, h, 270, 180);
                path.CloseFigure();
            }
            Region = new Region(path);
            path.Dispose();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateRegion(); }
        protected override void OnMouseEnter(EventArgs e) { _hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovering = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (PaintParentBg != null) PaintParentBg(g, this);
            else { using (var b = new SolidBrush(DarkTheme.GlassFlat)) g.FillRectangle(b, ClientRectangle); }
            int w = Width, h = Height;
            Color bg = _checked
                ? (_hovering ? Color.FromArgb(130, 210, 255) : DarkTheme.Accent)
                : (_hovering ? Color.FromArgb(90, 90, 90) : Color.FromArgb(55, 55, 55));
            using (var b = new SolidBrush(bg)) {
                g.FillEllipse(b, 0, 0, h, h); g.FillEllipse(b, w - h, 0, h, h);
                g.FillRectangle(b, h / 2f, 0, w - h, h);
            }
            // Hover glow ring
            if (_hovering) {
                Color glowC = _checked ? DarkTheme.Accent : Color.FromArgb(120, 120, 120);
                using (var p = new Pen(Color.FromArgb(50, glowC.R, glowC.G, glowC.B), 2f)) {
                    g.DrawEllipse(p, -1, -1, h + 2, h + 2);
                    g.DrawEllipse(p, w - h - 1, -1, h + 2, h + 2);
                }
            }
            float kd = h - Dpi.S(4), kx = _checked ? w - kd - Dpi.S(2) : Dpi.S(2);
            using (var b = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                g.FillEllipse(b, kx + 1, Dpi.S(3), kd, kd);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, Dpi.S(2), kd, kd);
        }
        protected override void OnClick(EventArgs e) {
            _checked = !_checked; Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            base.OnClick(e);
        }
    }

    /// <summary>
    /// Premium custom slider — pill-shaped track, circle thumb, accent fill, hover glow.
    /// Drop-in replacement for TrackBar. Uses Value/Minimum/Maximum/ValueChanged.
    /// </summary>
    public class SlickSlider : Control
    {
        private int _min, _max = 100, _val = 50;
        private bool _dragging, _hovering;
        public event EventHandler ValueChanged;
        /// <summary>Fires once when the user releases the slider thumb — use for heavy operations like COM enforcement.</summary>
        public event EventHandler DragCompleted;
        public int Minimum { get { return _min; } set { _min = value; InvalidateWithParent(); } }
        public int Maximum { get { return _max; } set { _max = value; InvalidateWithParent(); } }
        public int Value {
            get { return _val; }
            set { int nv = Math.Max(_min, Math.Min(_max, value)); if (nv != _val) { _val = nv; InvalidateWithParent(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); } }
        }

        static readonly Color TRACK_BG = Color.FromArgb(36, 36, 36);
        static readonly Color FILL_CLR = DarkTheme.Accent;
        static readonly Color THUMB_CLR = Color.White;

        public SlickSlider() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            Size = Dpi.Size(200, 30);
            BackColor = DarkTheme.GlassFlat;
        }

        // Delegate to paint parent card background for seamless transparency
        public Action<Graphics, Control> PaintParentBg;

        void InvalidateWithParent() {
            Invalidate();
            if (Parent != null) Parent.Invalidate(Bounds, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e) {
            // Intentionally empty — all painting in OnPaint to prevent seam artifacts
        }

        float Pct { get { return _max > _min ? (float)(_val - _min) / (_max - _min) : 0f; } }
        int ThumbR { get { return Dpi.S(7); } }
        int TrackH { get { return Dpi.S(4); } }
        int TrackY { get { return Height / 2; } }
        int TrackLeft { get { return ThumbR + 2; } }
        int TrackRight { get { return Width - ThumbR - 2; } }
        int ThumbX { get { return TrackLeft + (int)((TrackRight - TrackLeft) * Pct); } }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            // Fill background with own BackColor — set to GlassFlat for card sliders, row bg for app sliders
            using (var b = new SolidBrush(BackColor))
                g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Track + thumb
            int tl = TrackLeft, tr = TrackRight, ty = TrackY, th = TrackH, thr = ThumbR;
            int tx = ThumbX;
            float dim = Enabled ? 1.0f : 0.35f; // Dim when disabled (unlocked app)

            // Track background (pill shape)
            using (var b = new SolidBrush(Color.FromArgb((int)(TRACK_BG.A * dim), TRACK_BG.R, TRACK_BG.G, TRACK_BG.B)))
                FillPill(g, b, tl, ty - th/2, tr - tl, th);

            // Filled portion
            if (tx > tl + 2) {
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * dim), FILL_CLR.R, FILL_CLR.G, FILL_CLR.B)))
                    FillPill(g, b, tl, ty - th/2, tx - tl, th);
            }

            // Thumb shadow
            if (Enabled) {
                using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    g.FillEllipse(b, tx - thr + 1, ty - thr + 1, thr * 2, thr * 2);
            }

            // Thumb hover glow — constrained to control height
            if (Enabled && (_hovering || _dragging)) {
                int maxGlow = Height / 2 - 1; // Stay within control bounds
                int glowR = Math.Min(thr + Dpi.S(4), maxGlow);
                using (var b = new SolidBrush(Color.FromArgb(60, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B)))
                    g.FillEllipse(b, tx - glowR, ty - glowR, glowR * 2, glowR * 2);
            }

            // Thumb
            int thumbAlpha = Enabled ? 255 : 90;
            using (var b = new SolidBrush(Color.FromArgb(thumbAlpha, THUMB_CLR.R, THUMB_CLR.G, THUMB_CLR.B)))
                g.FillEllipse(b, tx - thr, ty - thr, thr * 2, thr * 2);

            // Thumb accent dot
            if (Enabled) {
                int dotR = Dpi.S(2);
                using (var b = new SolidBrush(FILL_CLR))
                    g.FillEllipse(b, tx - dotR, ty - dotR, dotR * 2, dotR * 2);
            }
        }

        void FillPill(Graphics g, Brush b, int x, int y, int w, int h) {
            if (w < h) w = h;
            if (w <= 0 || h <= 0) return;
            var path = new GraphicsPath();
            path.AddArc(x, y, h, h, 90, 180);
            path.AddArc(x + w - h, y, h, h, 270, 180);
            path.CloseFigure();
            g.FillPath(b, path);
            path.Dispose();
        }

        int XToValue(int x) {
            float pct = (float)(x - TrackLeft) / Math.Max(1, TrackRight - TrackLeft);
            pct = Math.Max(0f, Math.Min(1f, pct));
            return _min + (int)(pct * (_max - _min));
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) { _dragging = true; Value = XToValue(e.X); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            if (_dragging) Value = XToValue(e.X);
            bool wasHover = _hovering;
            int dx = e.X - ThumbX, dy = e.Y - TrackY;
            _hovering = dx * dx + dy * dy <= (ThumbR + Dpi.S(4)) * (ThumbR + Dpi.S(4));
            if (_hovering != wasHover) Invalidate();
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { bool wasDragging = _dragging; _dragging = false; Invalidate(); if (wasDragging && DragCompleted != null) DragCompleted(this, EventArgs.Empty); base.OnMouseUp(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovering = false; Invalidate(); base.OnMouseLeave(e); }
    }

    /// <summary>
    /// <summary>Panel that scrolls content via mouse wheel without showing a scrollbar.</summary>
    class ScrollPanel : Panel
    {
        private int _scrollY;
        private int _maxScroll;

        public ScrollPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AutoScroll = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Compute true content height from original (unscrolled) positions
            int contentH = 0;
            if (_originalTops != null && _originalTops.Length == Controls.Count) {
                for (int i = 0; i < Controls.Count; i++) {
                    int bottom = _originalTops[i] + Controls[i].Height;
                    if (bottom > contentH) contentH = bottom;
                }
            } else {
                foreach (Control c in Controls) {
                    int bottom = c.Top + c.Height;
                    if (bottom > contentH) contentH = bottom;
                }
            }
            _maxScroll = Math.Max(0, contentH - ClientSize.Height);
            if (_maxScroll == 0) { _scrollY = 0; return; }
            _scrollY = Math.Max(0, Math.Min(_maxScroll, _scrollY - e.Delta));
            UpdateChildPositions();
            base.OnMouseWheel(e);
        }

        private int[] _originalTops;
        
        public void ResetScroll()
        {
            _scrollY = 0;
            _originalTops = null;
        }

        private void UpdateChildPositions()
        {
            if (_originalTops == null || _originalTops.Length != Controls.Count) {
                _originalTops = new int[Controls.Count];
                for (int i = 0; i < Controls.Count; i++)
                    _originalTops[i] = Controls[i].Top + _scrollY; // store absolute position
            }
            SuspendLayout();
            for (int i = 0; i < Controls.Count; i++)
                Controls[i].Top = _originalTops[i] - _scrollY;
            ResumeLayout();
        }
    }

    /// Panel with DoubleBuffered enabled to prevent flicker during animation repaints.
    /// Set TransparentBackground = true to let parent content show through.
    /// </summary>
    public class BufferedPanel : Panel
    {
        public bool TransparentBackground { get; set; }
        public BufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }
    }

    public class OptionsForm : Form
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        private Settings _settings;
        private Panel _contentPanel, _sidebar, _footer;
        private Panel[] _panes, _navPanels, _navAccents;

        private Label[] _navLabels;
        private int _activePane = 0;

        private ToggleSwitch _tglAfkMic, _tglAfkSpk, _tglPtt, _tglPtm, _tglPtToggle, _tglMicEnf, _tglSpkEnf, _tglAppEnf, _tglStartup, _tglNotifyCorr, _tglNotifyDev, _tglOverlay;
        private NumericUpDown _nudAfkMicSec, _nudAfkSpkSec;
        private Label _lblPttKey, _lblPttKey2, _lblPttKey3;
        private Label _lblKey2Label, _lblKey2Hint, _lblKey3Label, _lblKey3Hint;
        private Button _btnRemoveKey2, _btnAddKey2, _btnRemoveKey3, _btnAddKey3;
        private CheckBox _chkKey1Overlay, _chkKey2Overlay, _chkKey3Overlay;
        private bool _key1ShowOverlay = true, _key2ShowOverlay = true, _key3ShowOverlay = true;
        private Timer _pollTimer;
        private int _pttKeyCode = 0x14, _pttKeyCode2 = 0, _pttKeyCode3 = 0; private bool _capturingKey, _capturingKey2, _capturingKey3, _loading;
        public bool IsCapturingKey { get { return _capturingKey || _capturingKey2 || _capturingKey3; } }
        private SlickSlider _trkMicVol, _trkSpkVol, _sysVolSlider;
        private Label _lblSysVolPct;
        // Volume lock snapshot/restore
        private int _micPreLockVol = -1, _spkPreLockVol = -1;
        private Timer _sliderRestoreMicTimer, _sliderRestoreSpkTimer;
        private int _twinkleTick;
        private Timer _twinkleTimer;
        private ShootingStar _shootingStar;
        private CelestialEvents _celestialEvents;

        static readonly Color BG = DarkTheme.BG;
        static readonly Color SB_BG = Color.FromArgb(16, 16, 16);  // Sidebar slightly lighter — intentional
        static readonly Color CARD_BDR = DarkTheme.CardBdr;
        static readonly Color BDR = Color.FromArgb(30, 30, 30);    // Form-level border
        static readonly Color ACC = DarkTheme.Accent;
        static readonly Color TXT = DarkTheme.Txt;
        static readonly Color TXT2 = DarkTheme.Txt2;
        static readonly Color TXT3 = DarkTheme.Txt3;
        static readonly Color TXT4 = DarkTheme.Txt4;
        static readonly Color HOVER = Color.FromArgb(26, 26, 26);  // Sidebar hover
        static readonly Color INPUT_BG = DarkTheme.InputBG;
        static readonly Color INPUT_BDR = DarkTheme.InputBdr;
        static readonly Color GREEN = DarkTheme.Green;
        static readonly string[] NAV = { "Push-to-Talk", "AFK Protection", "Volume Lock", "Apps", "General" };
        const int SB_W = 155;

        private Action<string> _onToggle; // Callback for instant actuation

        public OptionsForm(Settings settings, Action<string> onToggle = null) {
            _settings = settings;
            _onToggle = onToggle;
            Text = AppVersion.FullName + " \u2014 Options";
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BG; ForeColor = TXT;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f);
            ClientSize = Dpi.Size(730, 410);
            MinimumSize = Size; // Lock minimum to current layout size (includes title bar + borders)
            _defaultSize = Size;
            DoubleBuffered = true;
            try { Icon = Mascot.CreateIcon(); } catch { }

            _sidebar = new Panel { Dock = DockStyle.Left, Width = Dpi.S(SB_W), BackColor = SB_BG };
            var sep = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = BDR };
            _contentPanel = new BufferedPanel { Dock = DockStyle.Fill, BackColor = BG, Padding = Dpi.Pad(20, 16, 20, 0) };
            _contentPanel.Paint += (s, e) => { PaintUnifiedStars(e.Graphics, _contentPanel); };

            BuildSidebar(); BuildPanes(); BuildFooter();
            Controls.Add(_contentPanel); Controls.Add(sep); Controls.Add(_sidebar);
            // Global click handler — any click outside a NUD steals focus, killing the cursor
            Application.AddMessageFilter(new NudDefocusFilter(this));
            LoadSettings(); SwitchPane(0);
            _pollTimer = new Timer { Interval = 1000 };
            _pollTimer.Tick += (s, e) => UpdateCurrent();
            _pollTimer.Start();
            // Twinkle timer — slowly animates card stars (150ms = ~6.6fps, gentle and efficient)
            _twinkleTimer = new Timer { Interval = 150 };
            _twinkleTimer.Tick += (s, e) => {
                _twinkleTick++;
                InvalidateCards();
            };
            _twinkleTimer.Start();
            // Shooting star animation — occasional streaks across card backgrounds
            _shootingStar = new ShootingStar(() => { InvalidateCards(); });
            _shootingStar.Start();
            _celestialEvents = new CelestialEvents(() => { InvalidateCards(); });
            _celestialEvents.Start();
            FormClosing += (s, e) => { _pollTimer?.Stop(); _pollTimer?.Dispose(); _twinkleTimer?.Stop(); _twinkleTimer?.Dispose(); _shootingStar?.Stop(); _shootingStar?.Dispose(); _celestialEvents?.Stop(); _celestialEvents?.Dispose(); _sliderRestoreMicTimer?.Stop(); _sliderRestoreMicTimer?.Dispose(); _sliderRestoreSpkTimer?.Stop(); _sliderRestoreSpkTimer?.Dispose(); _updateShimmerTimer?.Stop(); _updateShimmerTimer?.Dispose(); _saveOrbitTimer?.Stop(); _saveOrbitTimer?.Dispose(); _starCache?.Dispose(); _starCacheDim?.Dispose(); };
        }

        private Size _defaultSize;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCLBUTTONDBLCLK)
            {
                if (WindowState == FormWindowState.Maximized)
                    WindowState = FormWindowState.Normal;
                Size = _defaultSize;
                var screen = Screen.FromControl(this);
                Location = new Point(
                    screen.WorkingArea.X + (screen.WorkingArea.Width - Width) / 2,
                    screen.WorkingArea.Y + (screen.WorkingArea.Height - Height) / 2);
                return;
            }
            base.WndProc(ref m);
        }

        void InvalidateCards() {
            try {
                _contentPanel.Invalidate(false); // background stars + shooting stars
                // Only invalidate the active visible card, not all children
                for (int i = 0; i < 5; i++) {
                    if (_panes[i] != null && _panes[i].Visible) {
                        foreach (Control c in _panes[i].Controls) c.Invalidate(false);
                        break;
                    }
                }
                if (_footer != null) _footer.Invalidate(false);
            } catch { }
        }

        private DateTime _lastAppVolRefresh = DateTime.MinValue;

        void UpdateCurrent() {
            try {
                float mic = Audio.GetMicVolume(), spk = Audio.GetSpeakerVolume();
                if (mic >= 0 && _plMicCur != null) _plMicCur.Text = "Current: " + (int)mic + "%";
                if (spk >= 0 && _plSpkCur != null) _plSpkCur.Text = "Current: " + (int)spk + "%";
                if (_volCard != null) _volCard.Invalidate();

                // Live-update app row effective volumes (throttled to every 2s)
                if (_appRows != null && _appRows.Count > 0 && (DateTime.UtcNow - _lastAppVolRefresh).TotalMilliseconds >= 2000) {
                    _lastAppVolRefresh = DateTime.UtcNow;
                    float masterVol = spk >= 0 ? spk : 100f;
                    System.Collections.Generic.List<AudioSession> sessions = null;
                    try { sessions = Audio.GetAudioSessions(); } catch { }
                    if (sessions != null) {
                        var liveVols = new System.Collections.Generic.Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                        foreach (var ls in sessions)
                            if (!liveVols.ContainsKey(ls.ProcessName))
                                liveVols[ls.ProcessName] = ls.Volume;
                        foreach (var ar in _appRows) {
                            float sv;
                            int eff = liveVols.TryGetValue(ar.Name, out sv) ? (int)((sv / 100f) * masterVol) : -1;
                            if (eff != ar.CurrentVolume) {
                                ar.CurrentVolume = eff;
                                if (ar.CurrentLabel != null) {
                                    ar.CurrentLabel.Text = eff >= 0 ? eff + "%" : "\u2014";
                                    ar.CurrentLabel.ForeColor = eff >= 0 ? Color.FromArgb(80, 200, 120) : TXT4;
                                }
                            }
                            // Keep target label's master% portion current
                            if (ar.VolLabel != null && ar.Slider != null) {
                                ar.VolLabel.Text = ar.Slider.Value + "% / " + (int)masterVol + "%";
                            }
                        }
                        // Keep system vol slider in sync (if user changed vol externally)
                        if (_sysVolSlider != null && !_sysVolSlider.Capture) {
                            try { int sv = (int)masterVol; if (_sysVolSlider.Value != sv) { _loading = true; _sysVolSlider.Value = sv; _lblSysVolPct.Text = sv + "%"; _loading = false; } } catch {}
                        }
                    }
                }
            } catch { }
        }

        /// <summary>
        /// Smoothly animates the volume slider back to the pre-lock snapshot value.
        /// TrayApp handles the actual audio hardware restore separately.
        /// </summary>
        void AnimateSliderRestore(bool isMic)
        {
            int target = isMic ? _micPreLockVol : _spkPreLockVol;
            if (target < 0) return; // No snapshot

            var slider = isMic ? _trkMicVol : _trkSpkVol;
            if (slider == null) return;

            // Kill existing animation
            if (isMic) { _sliderRestoreMicTimer?.Stop(); _sliderRestoreMicTimer?.Dispose(); _sliderRestoreMicTimer = null; }
            else { _sliderRestoreSpkTimer?.Stop(); _sliderRestoreSpkTimer?.Dispose(); _sliderRestoreSpkTimer = null; }

            int start = slider.Value;
            if (start == target) { if (isMic) _micPreLockVol = -1; else _spkPreLockVol = -1; return; }

            const int steps = 16;
            const int intervalMs = 25; // 400ms total
            float stepSize = (float)(target - start) / steps;
            int step = 0;

            var timer = new Timer { Interval = intervalMs };
            timer.Tick += (s, e) => {
                step++;
                if (step >= steps || IsDisposed) {
                    timer.Stop(); timer.Dispose();
                    if (isMic) _sliderRestoreMicTimer = null; else _sliderRestoreSpkTimer = null;
                    if (!IsDisposed) {
                        slider.Value = target;
                        if (isMic) { _settings.MicVolumePercent = target; _micPreLockVol = -1; }
                        else { _settings.SpeakerVolumePercent = target; _spkPreLockVol = -1; }
                        _settings.Save();
                    }
                    return;
                }
                int newVal = start + (int)(stepSize * step);
                newVal = Math.Max(0, Math.Min(100, newVal));
                slider.Value = newVal;
            };

            if (isMic) _sliderRestoreMicTimer = timer; else _sliderRestoreSpkTimer = timer;
            timer.Start();
        }

        void BuildSidebar() {
            // =================================================================
            // WinForms dock rule: LAST added to Controls = docked FIRST.
            // So: Fill FIRST (docked last, gets remaining), Top LAST (docked first).
            // =================================================================

            // === 1. NAV (Dock.Fill) — added FIRST so it docks LAST ===
            _navPanels = new Panel[5]; _navLabels = new Label[5]; _navAccents = new Panel[5];
            var navBox = new Panel { Dock = DockStyle.Fill, BackColor = SB_BG, Padding = Dpi.Pad(0, 6, 0, 0) };
            for (int i = 4; i >= 0; i--) {
                int idx = i;
                var nav = new Panel { Dock = DockStyle.Top, Height = Dpi.S(34), BackColor = SB_BG };
                var ac = new Panel { Location = new Point(0, 0), Size = new Size(Dpi.S(3), Dpi.S(34)), BackColor = SB_BG };
                nav.Controls.Add(ac);
                var lbl = new Label { Text = NAV[idx], Font = new Font("Segoe UI", 9f), ForeColor = TXT3, AutoSize = false, Location = Dpi.Pt(20, 7), Size = Dpi.Size(SB_W - 24, 20) };
                nav.Controls.Add(lbl);
                nav.Click += (s2,e2) => SwitchPane(idx); lbl.Click += (s2,e2) => SwitchPane(idx);
                nav.MouseEnter += (s2,e2) => { if (idx != _activePane) nav.BackColor = HOVER; };
                nav.MouseLeave += (s2,e2) => { if (idx != _activePane) nav.BackColor = SB_BG; };
                lbl.MouseEnter += (s2,e2) => { if (idx != _activePane) nav.BackColor = HOVER; };
                lbl.MouseLeave += (s2,e2) => { if (idx != _activePane) nav.BackColor = SB_BG; };
                _navPanels[idx] = nav; _navLabels[idx] = lbl; _navAccents[idx] = ac;
                navBox.Controls.Add(nav);
            }
            _sidebar.Controls.Add(navBox);

            // === 2. FOOTER (Dock.Bottom) — added SECOND ===
            var foot = new Panel { Dock = DockStyle.Bottom, Height = Dpi.S(50), BackColor = SB_BG };
            foot.Paint += (s, e) => {
                var g = e.Graphics; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var p = new Pen(Color.FromArgb(20, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B))) g.DrawLine(p, 0, 0, foot.Width, 0);
                using (var f = new Font("Segoe UI", 7f)) using (var b = new SolidBrush(TXT4))
                    g.DrawString("by Andrew Ganter", f, b, Dpi.S(14), Dpi.S(12));
                using (var f = new Font("Segoe UI", 7f, FontStyle.Italic)) using (var b = new SolidBrush(Color.FromArgb(50, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B)))
                    g.DrawString("Your privacy, your rules", f, b, Dpi.S(14), Dpi.S(28));
            };
            foot.MouseDown += (s, e) => {
                int dividerY = Dpi.S(24); // split between the two text lines
                if (e.Y < dividerY) {
                    // Clicked "by Andrew Ganter" — spawn shooting star
                    if (_shootingStar != null) _shootingStar.ForceLaunchMeteor();
                } else {
                    // Clicked "Your privacy, your rules" — spawn rare event
                    if (_celestialEvents != null) _celestialEvents.ForceLaunch();
                }
            };
            _sidebar.Controls.Add(foot);

            // === 3. HEADER (Dock.Top) — added LAST so it docks FIRST ===
            var hdr = new Panel { Dock = DockStyle.Top, Height = Dpi.S(58), BackColor = SB_BG };
            hdr.Paint += (s, e) => {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int otSz = Dpi.S(30);
                Mascot.DrawMascot(g, Dpi.S(14), Dpi.S(14), otSz);
                int tx = Dpi.S(14) + otSz + Dpi.S(8);
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) using (var b = new SolidBrush(Color.FromArgb(200,200,200)))
                    g.DrawString("Angry Audio", f, b, tx, Dpi.S(12));
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("v" + AppVersion.Version, f, b, tx, Dpi.S(30));
                using (var p = new Pen(Color.FromArgb(25, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B))) g.DrawLine(p, 0, hdr.Height - 1, hdr.Width, hdr.Height - 1);
            };
            _sidebar.Controls.Add(hdr);
        }

        void SwitchPane(int idx) {
            _activePane = idx;
            for (int i = 0; i < 5; i++) {
                bool a = i == idx;
                _navLabels[i].ForeColor = a ? ACC : TXT3;
                _navLabels[i].Font = new Font("Segoe UI", 9f, a ? FontStyle.Bold : FontStyle.Regular);
                _navAccents[i].BackColor = a ? ACC : SB_BG;
                _navPanels[i].BackColor = a ? HOVER : SB_BG;
                _panes[i].Visible = a;
            }
        }

        public void NavigateToPane(int idx) { if (idx >= 0 && idx < 5) SwitchPane(idx); }

        void BuildPanes() {
            _panes = new Panel[5];
            for (int i = 0; i < 5; i++) { _panes[i] = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false, BackColor = BG }; _contentPanel.Controls.Add(_panes[i]); }
            BuildPttPane(_panes[0]); BuildAfkPane(_panes[1]); BuildVolLockPane(_panes[2]); BuildAppsPane(_panes[3]); BuildGeneralPane(_panes[4]);
        }

        // Title info for card painting
        private string[] _paneTitles = new string[5];
        private string[] _paneSubs = new string[5];
        private string[] _paneBadges = new string[5];

        // === PAINTED LABEL SYSTEM ===
        // All text is drawn in the card Paint handler — zero child controls, zero flicker.
        class PaintedLabel {
            public string Text;
            public int X, Y;
            public Font Font;
            public Color Color;
            public int MaxWidth; // 0 = auto
            public bool RightAlign; // true = X is right edge, text aligns right
        }
        Dictionary<Panel, List<PaintedLabel>> _cardLabels = new Dictionary<Panel, List<PaintedLabel>>();
        Dictionary<Panel, List<int>> _cardLines = new Dictionary<Panel, List<int>>(); // separator Y positions

        PaintedLabel AddText(Panel card, string text, int x, int y, float fontSize, Color color, FontStyle style = FontStyle.Regular, int maxW = 0) {
            if (!_cardLabels.ContainsKey(card)) _cardLabels[card] = new List<PaintedLabel>();
            var pl = new PaintedLabel { Text = text, X = Dpi.S(x), Y = Dpi.S(y), Font = new Font("Segoe UI", fontSize, style), Color = color, MaxWidth = maxW > 0 ? Dpi.S(maxW) : 0 };
            _cardLabels[card].Add(pl);
            return pl;
        }
        void AddLine(Panel card, int y) {
            if (!_cardLines.ContainsKey(card)) _cardLines[card] = new List<int>();
            _cardLines[card].Add(Dpi.S(y));
        }

        Panel MakeCard(int paneIdx, string title, string sub = null, string badge = null) {
            _paneTitles[paneIdx] = title;
            _paneSubs[paneIdx] = sub;
            _paneBadges[paneIdx] = badge;
            var c = new BufferedPanel { Dock = DockStyle.Fill, BackColor = BG };
            c.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int cardTop = Dpi.S(sub != null ? 46 : 38);
                int rad = Dpi.S(6);

                // 1) Unified starfield — same stars as entire form, seamless
                PaintUnifiedStars(g, c);

                // 2) Frosted glass card — tint + dimmed unified stars
                var cardRect = new Rectangle(0, cardTop, c.Width - 1, c.Height - cardTop - 1);
                var clipPath = new GraphicsPath();
                int d = rad * 2;
                clipPath.AddArc(cardRect.X, cardRect.Y, d, d, 180, 90);
                clipPath.AddArc(cardRect.Right - d, cardRect.Y, d, d, 270, 90);
                clipPath.AddArc(cardRect.Right - d, cardRect.Bottom - d, d, d, 0, 90);
                clipPath.AddArc(cardRect.X, cardRect.Bottom - d, d, d, 90, 90);
                clipPath.CloseFigure();
                // Dark tint over card area — visible grey for premium look
                using (var tint = new SolidBrush(Color.FromArgb(200, DarkTheme.CardBG.R, DarkTheme.CardBG.G, DarkTheme.CardBG.B)))
                    g.FillPath(tint, clipPath);
                // Dimmed unified stars through the glass
                var oldClip = g.Clip;
                g.SetClip(clipPath, CombineMode.Replace);
                PaintUnifiedStars(g, c, 0.25f, false);
                g.Clip = oldClip;
                clipPath.Dispose();

                // 3) Mascot watermark (painted ABOVE frosted glass)
                try {
                    int msz = Dpi.S(120);
                    Mascot.DrawMascotWithOpacity(g, c.Width - msz - Dpi.S(8), c.Height - msz - Dpi.S(8), msz, 0.25f);
                } catch { }

                // 4) Card border
                using (var pen = new Pen(CARD_BDR)) RoundRect(g, pen, cardRect, rad);
                using (var pen = new Pen(Color.FromArgb(20, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B), Dpi.PenW(1)))
                    g.DrawLine(pen, Dpi.S(10), cardTop, c.Width - Dpi.S(10), cardTop);

                // 5) Separator lines
                if (_cardLines.ContainsKey(c)) {
                    using (var pen = new Pen(CARD_BDR))
                        foreach (int ly in _cardLines[c])
                            g.DrawLine(pen, Dpi.S(16), ly, c.Width - Dpi.S(16), ly);
                }

                // 6) Title text (above card — bright starfield behind)
                using (var f = new Font("Segoe UI", 13f, FontStyle.Bold))
                using (var b = new SolidBrush(TXT))
                    g.DrawString(title, f, b, Dpi.S(16), Dpi.S(4));
                if (badge != null)
                {
                    float tw;
                    using (var f = new Font("Segoe UI", 13f, FontStyle.Bold))
                        tw = g.MeasureString(title, f).Width;
                    using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                    using (var b = new SolidBrush(DarkTheme.Amber))
                        g.DrawString(badge, f, b, Dpi.S(16) + tw + Dpi.S(8), Dpi.S(10));
                }
                if (sub != null)
                {
                    using (var f = new Font("Segoe UI", 8f)) {
                        using (var b = new SolidBrush(TXT4))
                            g.DrawString(sub, f, b, Dpi.S(16), Dpi.S(28));
                    }
                }

                // 7) All painted labels — drawn directly on glass (no backing rects)
                if (_cardLabels.ContainsKey(c)) {
                    foreach (var lbl in _cardLabels[c]) {
                        using (var b = new SolidBrush(lbl.Color)) {
                            if (lbl.MaxWidth > 0)
                                g.DrawString(lbl.Text, lbl.Font, b, new RectangleF(lbl.X, lbl.Y, lbl.MaxWidth, 100));
                            else if (lbl.RightAlign) {
                                var sz = g.MeasureString(lbl.Text, lbl.Font);
                                g.DrawString(lbl.Text, lbl.Font, b, lbl.X - sz.Width, lbl.Y);
                            }
                            else
                                g.DrawString(lbl.Text, lbl.Font, b, lbl.X, lbl.Y);
                        }
                    }
                }
            };
            return c;
        }
        static void RoundRect(Graphics g, Pen p, Rectangle r, int rad) { var path=new GraphicsPath(); int d=rad*2; path.AddArc(r.X,r.Y,d,d,180,90); path.AddArc(r.Right-d,r.Y,d,d,270,90); path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.X,r.Bottom-d,d,d,90,90); path.CloseFigure(); g.DrawPath(p,path); path.Dispose(); }

        // === UNIFIED STARFIELD ===
        // ONE starfield across the entire form — no zones, no seams, no wasted GPU
        const int STAR_SEED = 42;

        // Get control's offset from form client area
        Point FormOffset(Control c) {
            int x = 0, y = 0;
            Control cur = c;
            while (cur != null && cur != this) { x += cur.Left; y += cur.Top; cur = cur.Parent; }
            return new Point(x, y);
        }

        // Cached star field bitmaps for performance — avoids re-rendering 150+ stars per control per frame
        private Bitmap _starCache, _starCacheDim;
        private int _starCacheW, _starCacheH, _starCacheTick = -1;

        void EnsureStarCache() {
            int cw = ClientSize.Width, ch = ClientSize.Height;
            if (cw <= 0 || ch <= 0) return;
            if (_starCache != null && _starCacheW == cw && _starCacheH == ch && _starCacheTick == _twinkleTick) return;
            // Rebuild cache
            _starCacheW = cw; _starCacheH = ch; _starCacheTick = _twinkleTick;
            if (_starCache != null) _starCache.Dispose();
            if (_starCacheDim != null) _starCacheDim.Dispose();
            _starCache = new Bitmap(cw, ch, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _starCacheDim = new Bitmap(cw, ch, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var sg = Graphics.FromImage(_starCache)) {
                sg.SmoothingMode = SmoothingMode.AntiAlias;
                DarkTheme.PaintCardStars(sg, cw, ch, STAR_SEED, _twinkleTick, 1.0f);
            }
            using (var sg = Graphics.FromImage(_starCacheDim)) {
                sg.SmoothingMode = SmoothingMode.AntiAlias;
                DarkTheme.PaintCardStars(sg, cw, ch, STAR_SEED, _twinkleTick, 0.25f);
            }
        }

        // Paint the unified starfield at form-level coordinates
        void PaintUnifiedStars(Graphics g, Control c, float alphaMul = 1.0f, bool shootingStar = true) {
            try {
                var off = FormOffset(c);
                EnsureStarCache();
                // Blit cached stars instead of re-rendering
                var src = (alphaMul < 0.5f) ? _starCacheDim : _starCache;
                if (src != null) g.DrawImage(src, -off.X, -off.Y);
                // Shooting stars + celestial events are dynamic — render directly
                if (shootingStar) {
                    g.TranslateTransform(-off.X, -off.Y);
                    if (_shootingStar != null) DarkTheme.PaintShootingStar(g, ClientSize.Width, ClientSize.Height, _shootingStar);
                    if (_celestialEvents != null) DarkTheme.PaintCelestialEvent(g, ClientSize.Width, ClientSize.Height, _celestialEvents);
                    g.ResetTransform();
                }
            } catch { try { g.ResetTransform(); } catch { } }
        }

        /// <summary>Paints card bg (BG + stars + glass + dimmed stars) into a child control's Graphics for seamless transparency.</summary>
        void PaintCardBg(Graphics g, Control child) {
            using (var b = new SolidBrush(BG)) g.FillRectangle(b, 0, 0, child.Width, child.Height);
            PaintUnifiedStars(g, child);
            using (var tint = new SolidBrush(Color.FromArgb(200, DarkTheme.CardBG.R, DarkTheme.CardBG.G, DarkTheme.CardBG.B)))
                g.FillRectangle(tint, 0, 0, child.Width, child.Height);
            PaintUnifiedStars(g, child, 0.25f, false);
        }

        // Tgl now only creates ToggleSwitch — text is painted
        ToggleSwitch Tgl(string label, string sub, int y, Panel card) {
            var t = new ToggleSwitch { Location = Dpi.Pt(16, y) }; t.PaintParentBg = PaintCardBg; card.Controls.Add(t);
            AddText(card, label, 64, y, 9.5f, TXT);
            if (sub != null) AddText(card, sub, 64, y + 19, 7.5f, TXT3);
            return t;
        }
        NumericUpDown Nud(int min, int max, int val, int x, int y, int w) { var n = new PaddedNumericUpDown { Minimum=min,Maximum=max,Value=val, Location=Dpi.Pt(x,y), Size=Dpi.Size(w,24), BackColor=INPUT_BG, ForeColor=TXT, Font=new Font("Segoe UI",9f), BorderStyle=BorderStyle.FixedSingle, TextAlign=HorizontalAlignment.Center };
            n.Leave += (s,e) => { try { int v; if (int.TryParse(n.Text, out v)) { v = Math.Max(min, Math.Min(max, v)); n.Value = v; } } catch { n.Value = val; } };
            return n; }

        // Dynamic painted labels that update at runtime
        private PaintedLabel _plMicCur, _plSpkCur, _plMicVol, _plSpkVol;
        private Panel _volCard; // reference for invalidation on value change

        void BuildAfkPane(Panel pane) {
            var card = MakeCard(1, "AFK Protection", "When you return, audio fades back in over 2s so you're never startled.");
            int y = 64;
            _tglAfkMic = Tgl("Mute Microphone When Idle", "Automatically mutes your mic when you step away.", y, card);
            _tglAfkMic.CheckedChanged += (s,e) => { if (!_loading) {
                _settings.AfkMicMuteEnabled = _tglAfkMic.Checked;
                if (_onToggle != null) _onToggle(_tglAfkMic.Checked ? "afk_mic_on" : "afk_mic_off");
                if (_tglAfkMic.Checked) { _loading = true; if (_tglPtt.Checked) _tglPtt.Checked = false; if (_tglPtm.Checked) _tglPtm.Checked = false; if (_tglPtToggle.Checked) _tglPtToggle.Checked = false; _loading = false; }
            } };
            y += 52;
            AddText(card, "After", 64, y+3, 9f, TXT2);
            _nudAfkMicSec = Nud(5,3600,10,104,y,60); card.Controls.Add(_nudAfkMicSec);
            _nudAfkMicSec.ValueChanged += (s,e) => { if (!_loading) { _settings.AfkMicMuteSec = (int)_nudAfkMicSec.Value; _settings.Save(); } };
            AddText(card, "seconds of inactivity", 170, y+3, 9f, TXT2);
            y+=34;
            AddText(card, "\u26A0  Disable this during open mic or voice calls", 64, y+4, 7.5f, DarkTheme.Amber);
            AddText(card, "Mutes all microphones system-wide \u2014 headset, camera mic, USB devices.", 64, y+22, 7f, Color.FromArgb(90, ACC.R, ACC.G, ACC.B));
            y+=48; AddLine(card, y); y+=24;
            _tglAfkSpk = Tgl("Mute Speakers When Idle", "Fades out system audio when you're away.", y, card);
            _tglAfkSpk.CheckedChanged += (s,e) => { if (!_loading) { _settings.AfkSpeakerMuteEnabled = _tglAfkSpk.Checked; if (_onToggle != null) _onToggle(_tglAfkSpk.Checked ? "afk_spk_on" : "afk_spk_off"); } };
            y += 52;
            AddText(card, "After", 64, y+3, 9f, TXT2);
            _nudAfkSpkSec = Nud(5,3600,10,104,y,60); card.Controls.Add(_nudAfkSpkSec);
            _nudAfkSpkSec.ValueChanged += (s,e) => { if (!_loading) { _settings.AfkSpeakerMuteSec = (int)_nudAfkSpkSec.Value; _settings.Save(); } };
            AddText(card, "seconds of inactivity", 170, y+3, 9f, TXT2);
            pane.Controls.Add(card);
        }

        void BuildPttPane(Panel pane) {
            var card = MakeCard(0, "Push-to-Talk", "Set this same key as push-to-talk in Discord, Zoom, Teams, etc.");
            int y = 64;
            _tglPtt = Tgl("Enable Push-to-Talk", "Mic stays muted at the OS level until you hold the hotkey.", y, card);
            _tglPtt.CheckedChanged += (s,e) => { if (!_loading) {
                _settings.PushToTalkEnabled = _tglPtt.Checked; _settings.PushToTalkKey = _pttKeyCode; _settings.PushToTalkKey2 = _pttKeyCode2; _settings.PushToTalkKey3 = _pttKeyCode3;
                if (_onToggle != null) _onToggle(_tglPtt.Checked ? "ptt_on" : "ptt_off");
                if (_tglPtt.Checked) { _loading = true; if (_tglPtm.Checked) _tglPtm.Checked = false; if (_tglPtToggle.Checked) _tglPtToggle.Checked = false; if (_tglAfkMic.Checked) _tglAfkMic.Checked = false; _loading = false; }
            } };
            y += 42;
            _tglPtm = Tgl("Enable Push-to-Mute", "Mic stays open \u2014 hold the hotkey to mute for coughs and sneezes.", y, card);
            _tglPtm.CheckedChanged += (s,e) => { if (!_loading) {
                _settings.PushToMuteEnabled = _tglPtm.Checked; _settings.PushToTalkKey = _pttKeyCode; _settings.PushToTalkKey2 = _pttKeyCode2; _settings.PushToTalkKey3 = _pttKeyCode3;
                if (_onToggle != null) _onToggle(_tglPtm.Checked ? "ptm_on" : "ptm_off");
                if (_tglPtm.Checked) { _loading = true; if (_tglPtt.Checked) _tglPtt.Checked = false; if (_tglPtToggle.Checked) _tglPtToggle.Checked = false; if (_tglAfkMic.Checked) _tglAfkMic.Checked = false; _loading = false; }
            } };
            y += 42;
            _tglPtToggle = Tgl("Enable Push-to-Toggle", "Press the hotkey once to unmute, press again to mute.", y, card);
            _tglPtToggle.CheckedChanged += (s,e) => { if (!_loading) {
                _settings.PushToToggleEnabled = _tglPtToggle.Checked; _settings.PushToTalkKey = _pttKeyCode; _settings.PushToTalkKey2 = _pttKeyCode2; _settings.PushToTalkKey3 = _pttKeyCode3;
                if (_onToggle != null) _onToggle(_tglPtToggle.Checked ? "ptt_toggle_on" : "ptt_toggle_off");
                if (_tglPtToggle.Checked) { _loading = true; if (_tglPtt.Checked) _tglPtt.Checked = false; if (_tglPtm.Checked) _tglPtm.Checked = false; if (_tglAfkMic.Checked) _tglAfkMic.Checked = false; _loading = false; }
            } };
            y += 48;
            AddText(card, "Hotkey 1:", 20, y+3, 9f, TXT2);
            _lblPttKey = new Label{Text=PushToTalk.GetKeyName(0x14),Font=new Font("Segoe UI",9.5f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(90,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(120,y)};
            _lblPttKey.Paint += (s,e) => { using(var p=new Pen(INPUT_BDR)) e.Graphics.DrawRectangle(p,0,0,_lblPttKey.Width-1,_lblPttKey.Height-1); };
            _lblPttKey.MouseEnter += (s,e) => { if(!_capturingKey) _lblPttKey.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPttKey.MouseLeave += (s,e) => { if(!_capturingKey) _lblPttKey.BackColor = INPUT_BG; };
            _lblPttKey.Click += (s,e) => StartKeyCapture(); card.Controls.Add(_lblPttKey);
            _chkKey1Overlay = MakeOverlayCheck(y, card, _key1ShowOverlay, (v) => { _key1ShowOverlay = v; _settings.PttKey1ShowOverlay = v; if(!_loading && _onToggle!=null)_onToggle("eye1"); });
            AddText(card, "Click to change \u00B7 Esc cancels", 216, y+5, 7.5f, TXT4);
            y += 32;
            // Second hotkey row
            _lblKey2Label = new Label{Text="Hotkey 2:",Font=new Font("Segoe UI",9f),ForeColor=TXT2,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(20,y+3)};
            card.Controls.Add(_lblKey2Label);
            _lblPttKey2 = new Label{Text=PushToTalk.GetKeyName(_pttKeyCode2),Font=new Font("Segoe UI",9.5f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(90,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(120,y)};
            _lblPttKey2.Paint += (s,e) => { using(var p=new Pen(INPUT_BDR)) e.Graphics.DrawRectangle(p,0,0,_lblPttKey2.Width-1,_lblPttKey2.Height-1); };
            _lblPttKey2.MouseEnter += (s,e) => { if(!_capturingKey2) _lblPttKey2.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPttKey2.MouseLeave += (s,e) => { if(!_capturingKey2) _lblPttKey2.BackColor = INPUT_BG; };
            _lblPttKey2.Click += (s,e) => StartKeyCapture2(); card.Controls.Add(_lblPttKey2);
            _chkKey2Overlay = MakeOverlayCheck(y, card, _key2ShowOverlay, (v) => { _key2ShowOverlay = v; _settings.PttKey2ShowOverlay = v; if(!_loading && _onToggle!=null)_onToggle("eye2"); });
            _lblKey2Hint = new Label{Text="Click to change \u00B7 Esc cancels",Font=new Font("Segoe UI",7.5f),ForeColor=TXT4,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(216,y+5)};
            card.Controls.Add(_lblKey2Hint);
            // Remove button (small X)
            _btnRemoveKey2 = new Button{Text="\u00D7",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(24,24),ForeColor=Color.FromArgb(140,60,60),BackColor=Color.Transparent,Font=new Font("Segoe UI",10f),TabStop=false,Location=Dpi.Pt(500,y+1)};
            _btnRemoveKey2.FlatAppearance.BorderSize=0;
            _btnRemoveKey2.MouseEnter += (s,e) => _btnRemoveKey2.ForeColor = Color.FromArgb(220,60,60);
            _btnRemoveKey2.MouseLeave += (s,e) => _btnRemoveKey2.ForeColor = Color.FromArgb(140,60,60);
            _btnRemoveKey2.Click += (s,e) => {
                if (_pttKeyCode3 > 0) {
                    // Shift key 3 down to key 2
                    _pttKeyCode2 = _pttKeyCode3; _settings.PushToTalkKey2 = _pttKeyCode3;
                    _lblPttKey2.Text = KeyName(_pttKeyCode2);
                    _key2ShowOverlay = _key3ShowOverlay; _settings.PttKey2ShowOverlay = _key3ShowOverlay;
                    if(_chkKey2Overlay!=null) _chkKey2Overlay.Checked = _key2ShowOverlay;
                    // Clear key 3
                    _pttKeyCode3 = 0; _settings.PushToTalkKey3 = 0;
                    _key3ShowOverlay = true; _settings.PttKey3ShowOverlay = true;
                } else {
                    _pttKeyCode2 = 0; _settings.PushToTalkKey2 = 0;
                }
                UpdateKey2Visibility();
            };
            card.Controls.Add(_btnRemoveKey2);
            // Add hotkey button (shown when key2 is empty)
            _btnAddKey2 = new Button{Text="+ Add Hotkey",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(100,26),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Location=Dpi.Pt(120,y)};
            _btnAddKey2.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnAddKey2.MouseEnter += (s,e) => { _btnAddKey2.BackColor=Color.FromArgb(ACC.R/5,ACC.G/5,ACC.B/5); _btnAddKey2.ForeColor=Color.FromArgb(Math.Min(255,ACC.R+50),Math.Min(255,ACC.G+50),Math.Min(255,ACC.B+50)); };
            _btnAddKey2.MouseLeave += (s,e) => { _btnAddKey2.BackColor=Color.FromArgb(20,20,20); _btnAddKey2.ForeColor=ACC; };
            _btnAddKey2.Click += (s,e) => StartKeyCapture2(); card.Controls.Add(_btnAddKey2);
            UpdateKey2Visibility();
            y += 32;
            // Third hotkey row
            _lblKey3Label = new Label{Text="Hotkey 3:",Font=new Font("Segoe UI",9f),ForeColor=TXT2,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(20,y+3)};
            card.Controls.Add(_lblKey3Label);
            _lblPttKey3 = new Label{Text=PushToTalk.GetKeyName(_pttKeyCode3),Font=new Font("Segoe UI",9.5f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(90,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(120,y)};
            _lblPttKey3.Paint += (s,e) => { using(var p=new Pen(INPUT_BDR)) e.Graphics.DrawRectangle(p,0,0,_lblPttKey3.Width-1,_lblPttKey3.Height-1); };
            _lblPttKey3.MouseEnter += (s,e) => { if(!_capturingKey3) _lblPttKey3.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPttKey3.MouseLeave += (s,e) => { if(!_capturingKey3) _lblPttKey3.BackColor = INPUT_BG; };
            _lblPttKey3.Click += (s,e) => StartKeyCapture3(); card.Controls.Add(_lblPttKey3);
            _chkKey3Overlay = MakeOverlayCheck(y, card, _key3ShowOverlay, (v) => { _key3ShowOverlay = v; _settings.PttKey3ShowOverlay = v; if(!_loading && _onToggle!=null)_onToggle("eye3"); });
            _lblKey3Hint = new Label{Text="Click to change \u00B7 Esc cancels",Font=new Font("Segoe UI",7.5f),ForeColor=TXT4,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(216,y+5)};
            card.Controls.Add(_lblKey3Hint);
            _btnRemoveKey3 = new Button{Text="\u00D7",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(24,24),ForeColor=Color.FromArgb(140,60,60),BackColor=Color.Transparent,Font=new Font("Segoe UI",10f),TabStop=false,Location=Dpi.Pt(500,y+1)};
            _btnRemoveKey3.FlatAppearance.BorderSize=0;
            _btnRemoveKey3.MouseEnter += (s,e) => _btnRemoveKey3.ForeColor = Color.FromArgb(220,60,60);
            _btnRemoveKey3.MouseLeave += (s,e) => _btnRemoveKey3.ForeColor = Color.FromArgb(140,60,60);
            _btnRemoveKey3.Click += (s,e) => { _pttKeyCode3 = 0; _settings.PushToTalkKey3 = 0; UpdateKey3Visibility(); };
            card.Controls.Add(_btnRemoveKey3);
            _btnAddKey3 = new Button{Text="+ Add Hotkey",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(100,26),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Location=Dpi.Pt(120,y)};
            _btnAddKey3.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnAddKey3.MouseEnter += (s,e) => { _btnAddKey3.BackColor=Color.FromArgb(ACC.R/5,ACC.G/5,ACC.B/5); _btnAddKey3.ForeColor=Color.FromArgb(Math.Min(255,ACC.R+50),Math.Min(255,ACC.G+50),Math.Min(255,ACC.B+50)); };
            _btnAddKey3.MouseLeave += (s,e) => { _btnAddKey3.BackColor=Color.FromArgb(20,20,20); _btnAddKey3.ForeColor=ACC; };
            _btnAddKey3.Click += (s,e) => StartKeyCapture3(); card.Controls.Add(_btnAddKey3);
            UpdateKey3Visibility();
            // System-wide mic note
            y += 36;
            AddText(card, "Mutes all microphones system-wide \u2014 headset, camera mic, USB devices.", 20, y, 7f, Color.FromArgb(90, ACC.R, ACC.G, ACC.B));
            pane.Controls.Add(card);
        }

        void BuildVolLockPane(Panel pane) {
            var card = MakeCard(2, "Volume Lock", "Keep your mic and speaker levels exactly where you set them."); int y = 64;
            _volCard = card;
            _tglMicEnf = Tgl("Lock Microphone Volume", "Prevents apps from silently changing your mic level.", y, card);
            _tglMicEnf.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglMicEnf.Checked) {
                    // Snapshot for slider restore
                    try { _micPreLockVol = (int)Audio.GetMicVolume(); } catch { _micPreLockVol = -1; }
                    if (_sliderRestoreMicTimer != null) { _sliderRestoreMicTimer.Stop(); _sliderRestoreMicTimer.Dispose(); _sliderRestoreMicTimer = null; }
                    // Notify TrayApp BEFORE enforcing so it can snapshot the real pre-lock volume
                    _settings.MicEnforceEnabled = true;
                    if (_onToggle != null) _onToggle("mic_lock_on");
                    try { Audio.SetMicVolume(_trkMicVol.Value); UpdateCurrent(); } catch { }
                } else {
                    _settings.MicEnforceEnabled = false;
                    if (_onToggle != null) _onToggle("mic_lock_off");
                    AnimateSliderRestore(true);
                }
            } };
            y+=48;
            AddText(card, Audio.GetMicName()??"Mic: Unknown", 64, y, 7.5f, TXT4);
            _plMicCur = AddText(card, "Current: --%", 380, y, 7.5f, GREEN); _plMicCur.RightAlign = true;
            y+=22;
            AddText(card, "Lock at:", 64, y+2, 9f, TXT2);
            _trkMicVol = new SlickSlider{Minimum=0,Maximum=100,Value=100,Location=Dpi.Pt(120,y-8),Size=Dpi.Size(180,30)}; _trkMicVol.PaintParentBg = PaintCardBg; card.Controls.Add(_trkMicVol);
            _plMicVol = AddText(card, "100%", 380, y+2, 9.5f, ACC, FontStyle.Bold); _plMicVol.RightAlign = true;
            _trkMicVol.ValueChanged += (s,e) => { _plMicVol.Text=_trkMicVol.Value+"%"; card.Invalidate(); };
            _trkMicVol.DragCompleted += (s,e) => { if (!_loading) { _settings.MicVolumePercent=_trkMicVol.Value; _settings.Save(); if (_tglMicEnf.Checked) try { Audio.SetMicVolume(_trkMicVol.Value); } catch { } } };
            // Max button for mic
            var btnMicMax = new Button{Text="\u266B  Max All",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(82,24),ForeColor=ACC,BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8),Font=new Font("Segoe UI",7.5f,FontStyle.Bold),TabStop=false};
            btnMicMax.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            btnMicMax.MouseEnter+=(s,e)=>{btnMicMax.BackColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);btnMicMax.ForeColor=Color.FromArgb(Math.Min(255,ACC.R+50),Math.Min(255,ACC.G+50),Math.Min(255,ACC.B+50));};
            btnMicMax.MouseLeave+=(s,e)=>{btnMicMax.BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);btnMicMax.ForeColor=ACC;};
            btnMicMax.Click+=(s,e)=>{ AnimateSlider(_trkMicVol, 100, ()=>{ _settings.MicVolumePercent=100; _settings.Save(); if (_tglMicEnf.Checked) try { Audio.SetMicVolume(100); } catch { } UpdateCurrent(); }); AnimateSlider(_trkSpkVol, 100, ()=>{ _settings.SpeakerVolumePercent=100; _settings.Save(); if (_tglSpkEnf.Checked) try { Audio.SetSpeakerVolume(100); } catch { } UpdateCurrent(); }); };
            card.Controls.Add(btnMicMax);
            card.Resize += (s2,e2) => { btnMicMax.Left = card.Width - Dpi.S(82) - Dpi.S(16); btnMicMax.Top = Dpi.S(68); };
            y+=44; AddLine(card, y); y+=24;
            _tglSpkEnf = Tgl("Lock Speaker Volume", "Prevents apps from changing your system volume.", y, card);
            _tglSpkEnf.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglSpkEnf.Checked) {
                    try { _spkPreLockVol = (int)Audio.GetSpeakerVolume(); } catch { _spkPreLockVol = -1; }
                    if (_sliderRestoreSpkTimer != null) { _sliderRestoreSpkTimer.Stop(); _sliderRestoreSpkTimer.Dispose(); _sliderRestoreSpkTimer = null; }
                    _settings.SpeakerEnforceEnabled = true;
                    if (_onToggle != null) _onToggle("spk_lock_on");
                    try { Audio.SetSpeakerVolume(_trkSpkVol.Value); UpdateCurrent(); } catch { }
                } else {
                    _settings.SpeakerEnforceEnabled = false;
                    if (_onToggle != null) _onToggle("spk_lock_off");
                    AnimateSliderRestore(false);
                }
            } };
            y+=48;
            AddText(card, Audio.GetSpeakerName()??"Speaker: Unknown", 64, y, 7.5f, TXT4);
            _plSpkCur = AddText(card, "Current: --%", 380, y, 7.5f, GREEN); _plSpkCur.RightAlign = true;
            y+=22;
            AddText(card, "Lock at:", 64, y+2, 9f, TXT2);
            _trkSpkVol = new SlickSlider{Minimum=0,Maximum=100,Value=100,Location=Dpi.Pt(120,y-8),Size=Dpi.Size(180,30)}; _trkSpkVol.PaintParentBg = PaintCardBg; card.Controls.Add(_trkSpkVol);
            _plSpkVol = AddText(card, "100%", 380, y+2, 9.5f, ACC, FontStyle.Bold); _plSpkVol.RightAlign = true;
            _trkSpkVol.ValueChanged += (s,e) => { _plSpkVol.Text=_trkSpkVol.Value+"%"; card.Invalidate(); };
            _trkSpkVol.DragCompleted += (s,e) => { if (!_loading) { _settings.SpeakerVolumePercent=_trkSpkVol.Value; _settings.Save(); if (_tglSpkEnf.Checked) try { Audio.SetSpeakerVolume(_trkSpkVol.Value); } catch { } } };

            pane.Controls.Add(card);
        }

        private Panel _appListPanel;
        private List<AppRuleRow> _appRows = new List<AppRuleRow>();
        private static Dictionary<string, Image> _appIconCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        // Shared fonts for app rows — avoid per-row allocation
        static readonly Font _rowFont = new Font("Segoe UI", 9f);
        static readonly Font _rowVolFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        static readonly Font _rowBtnFont = new Font("Segoe UI", 9f);

        class AppRuleRow {
            public string Name;
            public Panel Row;
            public SlickSlider Slider;
            public Label VolLabel;
            public Label CurrentLabel;
            public bool Locked = false;
            public int InitialValue = 100;
            public int CurrentVolume = -1; // -1 = unknown, 0-100 = live from system
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder exeName, ref uint size);

        static Image GetAppIcon(string processName) {
            if (_appIconCache.ContainsKey(processName)) return _appIconCache[processName];
            Image icon = null;
            try {
                // Method 1: MainModule.FileName (works for standard processes)
                var procs = Process.GetProcessesByName(processName);
                if (procs.Length > 0) {
                    try {
                        string path = procs[0].MainModule.FileName;
                        var ico = Icon.ExtractAssociatedIcon(path);
                        if (ico != null) { icon = ico.ToBitmap(); ico.Dispose(); }
                    } catch {
                        // Method 2: QueryFullProcessImageName (works for elevated/UWP/access-denied)
                        try {
                            // PROCESS_QUERY_LIMITED_INFORMATION = 0x1000 (works even for elevated processes)
                            IntPtr hProc = OpenProcess(0x1000, false, (uint)procs[0].Id);
                            if (hProc != IntPtr.Zero) {
                                try {
                                    var sb = new StringBuilder(1024);
                                    uint sz = 1024;
                                    if (QueryFullProcessImageName(hProc, 0, sb, ref sz)) {
                                        string path2 = sb.ToString();
                                        if (System.IO.File.Exists(path2)) {
                                            var ico2 = Icon.ExtractAssociatedIcon(path2);
                                            if (ico2 != null) { icon = ico2.ToBitmap(); ico2.Dispose(); }
                                        }
                                    }
                                } finally { CloseHandle(hProc); }
                            }
                        } catch { }
                    }
                }
                foreach (var p in procs) p.Dispose();
            } catch { }
            _appIconCache[processName] = icon; // Cache null too to avoid retrying
            return icon;
        }

        void BuildAppsPane(Panel pane) {
            var card = MakeCard(3, "Apps", "Lock individual app volumes to specific levels."); int y = 64;
            _tglAppEnf = Tgl("Per-App Volume Enforcement", "Green = current  \u2022  Slider = target", y, card);
            _tglAppEnf.CheckedChanged += (s,e) => { if (!_loading) { _settings.AppVolumeEnforceEnabled = _tglAppEnf.Checked; _settings.AppVolumeRules = CollectAppRules(); if (_onToggle != null) _onToggle(_tglAppEnf.Checked ? "app_lock_on" : "app_lock_off"); } };

            // System volume slider — controls master speaker volume
            var lblSysVol = new Label{Text="System Vol",ForeColor=TXT3,BackColor=Color.Transparent,Font=new Font("Segoe UI",7.5f),Size=Dpi.Size(58,16),TextAlign=ContentAlignment.MiddleRight,Location=Dpi.Pt(0,y+4)};
            card.Controls.Add(lblSysVol);
            int initSpkVol = 100; try { initSpkVol = (int)Audio.GetSpeakerVolume(); } catch {}
            _sysVolSlider = new SlickSlider{Minimum=0,Maximum=100,Value=initSpkVol,Size=Dpi.Size(150,24),Location=Dpi.Pt(0,y+2)};
            _sysVolSlider.PaintParentBg = PaintCardBg;
            _sysVolSlider.ValueChanged += (s,e) => {
                if (_loading) return;
                int val = _sysVolSlider.Value;
                try { Audio.SetSpeakerVolume(val); } catch {}
                _lblSysVolPct.Text = val + "%";
                // Update all app row labels to reflect new system vol
                foreach (var row in _appRows) {
                    if (row.VolLabel != null && row.Slider != null)
                        row.VolLabel.Text = row.Slider.Value + "% / " + val + "%";
                }
            };
            card.Controls.Add(_sysVolSlider);
            _lblSysVolPct = new Label{Text=initSpkVol+"%",ForeColor=ACC,BackColor=Color.Transparent,Font=new Font("Segoe UI",8f,FontStyle.Bold),Size=Dpi.Size(38,16),TextAlign=ContentAlignment.MiddleLeft,Location=Dpi.Pt(0,y+6)};
            card.Controls.Add(_lblSysVolPct);
            // Position system vol slider to the right
            card.Resize += (s2,e2) => {
                int rEdge = card.Width - Dpi.S(16);
                _lblSysVolPct.Left = rEdge - Dpi.S(38);
                _sysVolSlider.Left = _lblSysVolPct.Left - Dpi.S(152);
                _sysVolSlider.Width = Dpi.S(150);
                lblSysVol.Left = _sysVolSlider.Left - Dpi.S(60);
            };
            y+=48;

            var btn = new Button{Text="\u25B6  Scan Running Apps",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(148,26),ForeColor=ACC,BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8),Location=Dpi.Pt(16,y),Font=new Font("Segoe UI",8.5f,FontStyle.Bold),TabStop=false};
            btn.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            btn.Click+=(s,e)=>ScanApps();
            btn.MouseEnter+=(s,e)=>btn.BackColor=Color.FromArgb(ACC.R/5,ACC.G/5,ACC.B/5);
            btn.MouseLeave+=(s,e)=>btn.BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);
            card.Controls.Add(btn);

            var btnClear = new Button{Text="Clear",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(50,26),ForeColor=TXT4,BackColor=Color.FromArgb(24,24,24),Location=Dpi.Pt(172,y),Font=new Font("Segoe UI",8f),TabStop=false};
            btnClear.FlatAppearance.BorderColor=Color.FromArgb(40,40,40);
            btnClear.Click+=(s,e)=>{_appRows.Clear();RebuildAppList();};
            btnClear.MouseEnter+=(s,e)=>btnClear.BackColor=Color.FromArgb(36,36,36);
            btnClear.MouseLeave+=(s,e)=>btnClear.BackColor=Color.FromArgb(24,24,24);
            card.Controls.Add(btnClear);

            // Max All button — accent blue, matches Save style
            var btnMaxAll = new Button{Text="\u266B  Max All",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(90,26),ForeColor=ACC,BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8),Location=Dpi.Pt(0,y),Font=new Font("Segoe UI",8.5f,FontStyle.Bold),TabStop=false};
            btnMaxAll.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            btnMaxAll.MouseEnter+=(s,e)=>{btnMaxAll.BackColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);btnMaxAll.ForeColor=Color.FromArgb(Math.Min(255,ACC.R+50),Math.Min(255,ACC.G+50),Math.Min(255,ACC.B+50));};
            btnMaxAll.MouseLeave+=(s,e)=>{btnMaxAll.BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);btnMaxAll.ForeColor=ACC;};
            btnMaxAll.Click+=(s,e)=>AnimateMaxAllApps();
            card.Controls.Add(btnMaxAll);
            // Position Max All button to the right — uses Resize event
            card.Resize += (s2,e2) => { btnMaxAll.Left = card.Width - Dpi.S(90) - Dpi.S(16); };
            y+=32;

            int listY = y; // logical Y=144
            _appListPanel = new ScrollPanel{Location=Dpi.Pt(16,listY),Size=Dpi.Size(100, 200),BackColor=Color.FromArgb(14,14,14)};
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(_appListPanel, true, null);
            _appListPanel.Paint += (s2,e2) => {
                var gp = e2.Graphics;
                gp.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                if (_appRows.Count == 0) {
                    using (var f = new Font("Segoe UI", 8.5f))
                    using (var b = new SolidBrush(TXT4)) {
                        string msg = "No app rules yet \u2014 click Scan to detect running Audio Apps";
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            gp.DrawString(msg, f, b, new RectangleF(0, 0, _appListPanel.Width, _appListPanel.Height), sf);
                    }
                }
                using (var p = new Pen(Color.FromArgb(32,32,32)))
                    gp.DrawRectangle(p, 0, 0, _appListPanel.Width - 1, _appListPanel.Height - 1);
            };
            card.Controls.Add(_appListPanel);

            // Size list to fill card; fires on initial layout AND on resize
            int scaledListY = Dpi.S(listY);
            int margin = Dpi.S(16);
            int lastListW = 0;
            bool rebuildGuard = false;
            // Cache last live sessions to avoid COM calls on every resize
            System.Collections.Generic.List<AudioSession> _lastLiveSessions = null;
            EventHandler sizeList = (s, e) => {
                int newW = card.Width - margin * 2;
                int newH = card.Height - scaledListY - margin;
                if (newW > Dpi.S(200)) _appListPanel.Width = newW;
                if (newH > Dpi.S(60)) _appListPanel.Height = newH;
                // Rebuild rows if panel width changed (rows need to match)
                if (!rebuildGuard && _appListPanel.Width != lastListW && _appRows.Count > 0) {
                    lastListW = _appListPanel.Width;
                    rebuildGuard = true;
                    try { RebuildAppList(_lastLiveSessions); } finally { rebuildGuard = false; }
                }
            };
            card.Resize += sizeList;
            card.Layout += (s2, e2) => sizeList(s2, e2); // fires on initial layout

            pane.Controls.Add(card);
        }

        void RebuildAppList(System.Collections.Generic.List<AudioSession> cachedSessions = null) {
            _appListPanel.SuspendLayout();
            _appListPanel.Controls.Clear();
            if (_appListPanel is ScrollPanel sp) sp.ResetScroll();
            int y = 0, rowH = Dpi.S(44);
            int iconSz = Dpi.S(20);
            int rowW = Math.Max(Dpi.S(300), _appListPanel.Width);

            // Batch-query current volumes (use cached if provided)
            var liveSessions = cachedSessions;
            if (liveSessions == null) try { liveSessions = Audio.GetAudioSessions(); } catch { }
            var liveVols = new System.Collections.Generic.Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (liveSessions != null)
                foreach (var ls in liveSessions)
                    if (!liveVols.ContainsKey(ls.ProcessName))
                        liveVols[ls.ProcessName] = ls.Volume;

            // Get master speaker volume to compute effective output level
            float masterVol = 100f;
            try { float mv = Audio.GetSpeakerVolume(); if (mv >= 0) masterVol = mv; } catch { }

            for (int i = 0; i < _appRows.Count; i++) {
                var ar = _appRows[i];
                float lv; 
                if (liveVols.TryGetValue(ar.Name, out lv))
                    ar.CurrentVolume = (int)((lv / 100f) * masterVol); // Effective output = session × master
                else
                    ar.CurrentVolume = -1;

                Color rowBg = ar.Locked ? Color.FromArgb(14, 18, 24) : Color.FromArgb(16, 16, 16);
                var row = new BufferedPanel{Location=new Point(0,y),Size=new Size(rowW,rowH),BackColor=rowBg};
                ar.Row = row;

                row.Paint += (s,e) => {
                    using (var p = new Pen(Color.FromArgb(30,30,30)))
                        e.Graphics.DrawLine(p, Dpi.S(6), 0, row.Width - Dpi.S(6), 0);
                };

                // Lock toggle
                string cName = ar.Name;
                var lockPanel = new Panel{Size=new Size(Dpi.S(30),Dpi.S(32)),Location=new Point(Dpi.S(4),Dpi.S(6)),BackColor=Color.Transparent};
                lockPanel.Paint += (s,e) => {
                    var g2 = e.Graphics; g2.SmoothingMode = SmoothingMode.AntiAlias;
                    var rw = _appRows.Find(x => x.Name == cName);
                    DrawLockIcon(g2, Dpi.S(3), Dpi.S(4), Dpi.S(24), rw != null && rw.Locked);
                };
                lockPanel.MouseEnter += (s,e) => { lockPanel.BackColor = Color.FromArgb(32, 32, 32); };
                lockPanel.MouseLeave += (s,e) => { lockPanel.BackColor = Color.Transparent; };
                lockPanel.MouseClick += (s,e) => {
                    var r = _appRows.Find(x => x.Name == cName);
                    if (r != null) { r.Locked = !r.Locked; RebuildAppList(); SyncAppRulesLive(); }
                };
                row.Controls.Add(lockPanel);

                // App icon
                Image appIcon = GetAppIcon(ar.Name);
                if (appIcon != null) {
                    var iconBox = new PictureBox{Size=new Size(iconSz,iconSz),Location=new Point(Dpi.S(38),Dpi.S(12)),SizeMode=PictureBoxSizeMode.Zoom,Image=appIcon,BackColor=Color.Transparent};
                    row.Controls.Add(iconBox);
                }

                // Layout: [lock 34] [icon 24] [name 92] [curVol 46] [slider...] [target 80] [X 28]
                int nameX = Dpi.S(62);
                int btnXx = rowW - Dpi.S(28);
                int targetX = btnXx - Dpi.S(80);
                int sliderEnd = targetX - Dpi.S(4);

                // App name — wider to handle "steamwebhelper" etc
                var lbl = new Label{Text=ar.Name,Font=_rowFont,ForeColor=ar.Locked?TXT:TXT4,AutoSize=false,Size=new Size(Dpi.S(92),rowH),TextAlign=ContentAlignment.MiddleLeft,Location=new Point(nameX,0)};
                row.Controls.Add(lbl);

                // Current volume — green text showing actual system level (wider to fit "100%")
                int curX = nameX + Dpi.S(92);
                string curText = ar.CurrentVolume >= 0 ? ar.CurrentVolume + "%" : "\u2014";
                Color curClr = ar.CurrentVolume >= 0 ? Color.FromArgb(80, 200, 120) : TXT4;
                var curLbl = new Label{Text=curText,Font=_rowVolFont,ForeColor=curClr,AutoSize=false,Size=new Size(Dpi.S(46),rowH),TextAlign=ContentAlignment.MiddleCenter,Location=new Point(curX,0)};
                ar.CurrentLabel = curLbl;
                row.Controls.Add(curLbl);

                // Slider
                int sliderX = curX + Dpi.S(48);
                int sliderW = Math.Max(Dpi.S(60), sliderEnd - sliderX);
                var slider = new SlickSlider{Minimum=0,Maximum=100,Value=ar.Slider!=null?ar.Slider.Value:ar.InitialValue,Location=new Point(sliderX,0),Size=new Size(sliderW,rowH),Enabled=ar.Locked,BackColor=rowBg};
                ar.Slider = slider;
                ar.InitialValue = slider.Value;
                row.Controls.Add(slider);

                // Target volume + master context (accent blue) — shows "app% / master%"
                string targetText = slider.Value + "% / " + (int)masterVol + "%";
                var volLbl = new Label{Text=targetText,Font=_rowVolFont,ForeColor=ar.Locked?ACC:TXT4,AutoSize=false,Size=new Size(Dpi.S(80),rowH),TextAlign=ContentAlignment.MiddleCenter,Location=new Point(targetX,0)};
                ar.VolLabel = volLbl;
                string sliderAppName = ar.Name;
                bool sliderLocked = ar.Locked;
                Label sliderCurLbl = curLbl;
                float cachedMaster = masterVol; // snapshot for drag feedback
                slider.ValueChanged += (s,e) => {
                    // Lightweight: update label text only (no COM calls during drag)
                    volLbl.Text = slider.Value + "% / " + (int)cachedMaster + "%";
                    if (sliderLocked) {
                        int eff = (int)((slider.Value / 100f) * cachedMaster);
                        sliderCurLbl.Text = eff + "%";
                        sliderCurLbl.ForeColor = Color.FromArgb(80, 200, 120);
                    }
                };
                slider.DragCompleted += (s,e) => {
                    // Heavy: push to system only on mouse release
                    if (sliderLocked) {
                        try { Audio.SetAppVolume(sliderAppName, slider.Value); } catch { }
                        SyncAppRulesLive();
                    }
                    // Refresh master volume cache
                    try { float mv = Audio.GetSpeakerVolume(); if (mv >= 0) cachedMaster = mv; } catch { }
                };
                row.Controls.Add(volLbl);

                // Remove button
                string capName = ar.Name;
                var btnX = new Button{Text="\u00D7",FlatStyle=FlatStyle.Flat,Size=new Size(Dpi.S(22),Dpi.S(22)),ForeColor=TXT4,BackColor=Color.Transparent,Font=_rowBtnFont,Location=new Point(btnXx,Dpi.S(11)),TabStop=false};
                btnX.FlatAppearance.BorderSize=0;
                btnX.MouseEnter+=(s,e)=>btnX.ForeColor=DarkTheme.ErrorRed;
                btnX.MouseLeave+=(s,e)=>btnX.ForeColor=TXT4;
                btnX.Click += (s,e) => { _appRows.RemoveAll(r=>r.Name==capName); RebuildAppList(); SyncAppRulesLive(); };
                row.Controls.Add(btnX);

                _appListPanel.Controls.Add(row);
                y += rowH;
            }
            _appListPanel.ResumeLayout();
            // Rows fill full width — no scrollbar to account for
            _appListPanel.Invalidate();
        }

        /// <summary>Draws a lock (locked) or unlock (open shackle) icon.</summary>
        static void DrawLockIcon(Graphics g, int x, int y, int sz, bool locked) {
            float s = sz / 16f;
            Color c = locked ? DarkTheme.Accent : Color.FromArgb(80, 80, 80);
            // Lock body (rounded rect)
            using (var b = new SolidBrush(c)) {
                g.FillRectangle(b, x + 3*s, y + 7*s, 10*s, 8*s);
            }
            // Keyhole
            using (var b = new SolidBrush(Color.FromArgb(20, 20, 20))) {
                g.FillEllipse(b, x + 6.5f*s, y + 9.5f*s, 3*s, 3*s);
                g.FillRectangle(b, x + 7.2f*s, y + 11.5f*s, 1.6f*s, 2*s);
            }
            // Shackle
            using (var p = new Pen(c, 2f*s)) {
                p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                if (locked) {
                    g.DrawArc(p, x + 4.5f*s, y + 1.5f*s, 7*s, 7*s, 180, 180);
                } else {
                    // Open shackle — shifted right and up
                    g.DrawArc(p, x + 6f*s, y + 0.5f*s, 7*s, 7*s, 180, 180);
                }
            }
        }

        Dictionary<string,int> CollectAppRules() {
            var d = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            foreach (var ar in _appRows) {
                if (!d.ContainsKey(ar.Name)) {
                    int vol = ar.Slider != null ? ar.Slider.Value : ar.InitialValue;
                    d[ar.Name] = ar.Locked ? vol : -(vol + 1);
                }
            }
            return d;
        }

        /// <summary>Push current app rules to live settings so enforcement picks them up immediately.</summary>
        void SyncAppRulesLive() {
            _settings.AppVolumeRules = CollectAppRules();
        }

        void BuildGeneralPane(Panel pane) {
            var card = MakeCard(4, "General", "Startup behavior, notifications, and legal information.");
            // Card glass top is at y=46, separator below this section at y=96.
            int y = 64;
            _tglStartup = Tgl("Start with Windows", null, y, card);
            _tglStartup.CheckedChanged += (s,e) => { if (!_loading) { _settings.StartWithWindows = _tglStartup.Checked; _settings.ApplyStartupSetting(); if (_onToggle != null) _onToggle(_tglStartup.Checked ? "startup_on" : "startup_off"); } };
            // Run Setup Wizard button — same row as Start with Windows, right-aligned
            int bwY = y;
            var bw = new Button{Text="Run Setup Wizard",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(120,24),ForeColor=TXT2,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f),TabStop=false};
            bw.FlatAppearance.BorderColor=INPUT_BDR; bw.Click+=(s,e)=>{DialogResult=DialogResult.Retry;Close();};
            bw.MouseEnter+=(s,e)=>bw.BackColor=Color.FromArgb(36,36,36);
            bw.MouseLeave+=(s,e)=>bw.BackColor=Color.FromArgb(20,20,20);
            card.Controls.Add(bw);
            // Check for Updates button — same row, to the left of Run Setup Wizard, blue theme
            var btnUpdate = new Button{Text="Check for Updates",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(155,24),ForeColor=ACC,BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Cursor=Cursors.Hand};
            btnUpdate.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            btnUpdate.MouseEnter+=(s,e)=>{if(btnUpdate.Enabled)btnUpdate.BackColor=Color.FromArgb(ACC.R/5,ACC.G/5,ACC.B/5);};
            btnUpdate.MouseLeave+=(s,e)=>{if(btnUpdate.Enabled)btnUpdate.BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);};
            _updateCheckHandler = (s,e) => { if (!_updateShimmering) CheckForUpdates(btnUpdate); };
            btnUpdate.Click += _updateCheckHandler;
            card.Controls.Add(btnUpdate);
            // Position both buttons — wizard at right edge, update to its left
            EventHandler posWiz = (s2, e2) => {
                if (card.Width > 0) {
                    bw.Location = new Point(card.Width - bw.Width - Dpi.S(16), Dpi.S(bwY));
                    btnUpdate.Location = new Point(bw.Left - btnUpdate.Width - Dpi.S(8), Dpi.S(bwY));
                }
            };
            card.Layout += (s2, e2) => posWiz(null, EventArgs.Empty);
            card.Resize += posWiz;
            y += 37; AddLine(card, y); y += 14;
            AddText(card, "NOTIFICATIONS", 16, y, 7.5f, TXT3, FontStyle.Bold);
            y += 22;
            _tglOverlay = Tgl("Show Mic Status Overlay", "Floating pill shows mic open/closed \u2014 drag to reposition, right-click to dismiss", y, card);
            _tglOverlay.CheckedChanged += (s,e) => { if (!_loading) { _settings.MicOverlayEnabled = _tglOverlay.Checked; if (_onToggle != null) _onToggle(_tglOverlay.Checked ? "overlay_on" : "overlay_off"); } };
            y += 42;
            _tglNotifyCorr = Tgl("Volume Correction Alerts", "Show a toast when Angry Audio resets your audio.", y, card);
            _tglNotifyCorr.CheckedChanged += (s,e) => { if (!_loading) { _settings.NotifyOnCorrection = _tglNotifyCorr.Checked; if (_onToggle != null) _onToggle(_tglNotifyCorr.Checked ? "notify_corr_on" : "notify_corr_off"); } };
            y += 42;
            _tglNotifyDev = Tgl("Device Change Alerts", "Notify when a new mic or speaker is detected.", y, card);
            _tglNotifyDev.CheckedChanged += (s,e) => { if (!_loading) { _settings.NotifyOnDeviceChange = _tglNotifyDev.Checked; if (_onToggle != null) _onToggle(_tglNotifyDev.Checked ? "notify_dev_on" : "notify_dev_off"); } };
            y += 42; AddLine(card, y); y += 16;
            // LEGAL section — vertically centered in remaining card space
            AddText(card, "LEGAL", 16, y, 7.5f, TXT3, FontStyle.Bold);
            y += 20;
            AddText(card, AppVersion.Copyright, 16, y, 8f, TXT2);
            y += 18;
            AddText(card, "Unauthorized copying, modification, or distribution prohibited.", 16, y, 7f, TXT4, FontStyle.Regular, 440);
            pane.Controls.Add(card);
        }

        void BuildFooter() {
            _footer = new BufferedPanel{Dock=DockStyle.Bottom,Height=Dpi.S(50),BackColor=BG};
            _footer.Paint += (s,e) => {
                PaintUnifiedStars(e.Graphics, _footer);
                using(var p=new Pen(BDR)) e.Graphics.DrawLine(p,0,0,_footer.Width,0);
            };
            Controls.Add(_footer);
            var bs = new Button{Text="Save",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(80,30),ForeColor=Color.White,BackColor=ACC,Font=new Font("Segoe UI",9.5f,FontStyle.Bold),Anchor=AnchorStyles.Top|AnchorStyles.Right,TabStop=false};
            _saveBtn = bs;
            bs.FlatAppearance.BorderSize=0; bs.Click+=(s,e)=>DoSave();
            bs.MouseEnter+=(s,e)=>bs.BackColor=Color.FromArgb(140,220,255);
            bs.MouseLeave+=(s,e)=>bs.BackColor=ACC;
            bs.Paint += (s, e) => { DarkTheme.PaintOrbitingStar(e.Graphics, bs.Width, bs.Height, _saveOrbitPhase, Dpi.S(6)); };
            _saveOrbitPhase = 0f;
            _saveOrbitTimer = new Timer { Interval = 30 };
            _saveOrbitTimer.Tick += (s, e) => {
                _saveOrbitPhase += 0.08f;
                if (_saveOrbitPhase > (float)(Math.PI * 2)) _saveOrbitPhase -= (float)(Math.PI * 2);
                float pulse = (float)((Math.Sin(_saveOrbitPhase * 0.6) + 1.0) / 2.0);
                int r = (int)(ACC.R + (180 - ACC.R) * pulse);
                int gb = (int)(ACC.G + (240 - ACC.G) * pulse);
                int bl = (int)(ACC.B + (255 - ACC.B) * pulse);
                if (!bs.ClientRectangle.Contains(bs.PointToClient(Cursor.Position)))
                    bs.BackColor = Color.FromArgb(r, gb, bl);
                bs.Invalidate();
            };
            _saveOrbitTimer.Start();
            _footer.Controls.Add(bs);
            var bc = new Button{Text="Cancel",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(80,30),ForeColor=TXT2,BackColor=Color.FromArgb(28,28,28),Anchor=AnchorStyles.Top|AnchorStyles.Right,TabStop=false};
            bc.FlatAppearance.BorderColor=INPUT_BDR; bc.Click+=(s,e)=>{DialogResult=DialogResult.Cancel;Close();};
            bc.MouseEnter+=(s,e)=>{bc.BackColor=Color.FromArgb(55,55,55);bc.ForeColor=TXT;};
            bc.MouseLeave+=(s,e)=>{bc.BackColor=Color.FromArgb(28,28,28);bc.ForeColor=TXT2;};
            _footer.Controls.Add(bc);
            bs.Location = new Point(ClientSize.Width - bs.Width - Dpi.S(16), Dpi.S(10));
            bc.Location = new Point(bs.Left - bc.Width - Dpi.S(10), Dpi.S(10));
        }

        void StartKeyCapture(){_capturingKey=true;_lblPttKey.Text="Press...";_lblPttKey.BackColor=ACC;_lblPttKey.ForeColor=Color.White;KeyPreview=true;KeyDown+=OnKeyCapture;}
        void StartKeyCapture2(){_capturingKey2=true;_lblPttKey2.Text="Press...";_lblPttKey2.BackColor=ACC;_lblPttKey2.ForeColor=Color.White;_btnAddKey2.Visible=false;_lblPttKey2.Visible=true;_lblKey2Label.Visible=true;_lblKey2Hint.Visible=true;KeyPreview=true;KeyDown+=OnKeyCapture2;}
        void OnKeyCapture2(object s,KeyEventArgs e){if(!_capturingKey2)return;e.Handled=true;e.SuppressKeyPress=true;if(e.KeyCode==Keys.Escape){if(_pttKeyCode2==0){UpdateKey2Visibility();}else{_lblPttKey2.Text=KeyName(_pttKeyCode2);}_lblPttKey2.BackColor=INPUT_BG;_lblPttKey2.ForeColor=ACC;_capturingKey2=false;KeyPreview=false;KeyDown-=OnKeyCapture2;return;}
            int vk=(int)e.KeyCode;
            if (vk == 0x10) vk = IsKeyDown(0xA1) ? 0xA1 : 0xA0;
            if (vk == 0x11) vk = IsKeyDown(0xA3) ? 0xA3 : 0xA2;
            if (vk == 0x12) vk = IsKeyDown(0xA5) ? 0xA5 : 0xA4;
            if(vk>=0xA0&&vk<=0xA5){try{if((GetAsyncKeyState(0xA1)&0x8000)!=0)vk=0xA1;else if((GetAsyncKeyState(0xA0)&0x8000)!=0)vk=0xA0;else if((GetAsyncKeyState(0xA3)&0x8000)!=0)vk=0xA3;else if((GetAsyncKeyState(0xA2)&0x8000)!=0)vk=0xA2;else if((GetAsyncKeyState(0xA5)&0x8000)!=0)vk=0xA5;else if((GetAsyncKeyState(0xA4)&0x8000)!=0)vk=0xA4;}catch{}}
            _pttKeyCode2=vk;
            // Duplicate check
            if(vk==_pttKeyCode || (_pttKeyCode3>0 && vk==_pttKeyCode3)){_capturingKey2=false;KeyPreview=false;KeyDown-=OnKeyCapture2;_pttKeyCode2=0;_settings.PushToTalkKey2=0;ShakeReject(_lblPttKey2, ()=>{UpdateKey2Visibility();});return;}
            _lblPttKey2.Text=KeyName(_pttKeyCode2);_lblPttKey2.BackColor=INPUT_BG;_lblPttKey2.ForeColor=ACC;_capturingKey2=false;KeyPreview=false;KeyDown-=OnKeyCapture2;_settings.PushToTalkKey2=_pttKeyCode2;_key2ShowOverlay=true;_settings.PttKey2ShowOverlay=true;if(_chkKey2Overlay!=null)_chkKey2Overlay.Checked=true;UpdateKey2Visibility();if(_onToggle!=null)_onToggle("ptt_key2:"+_pttKeyCode2);}
        void StartKeyCapture3(){_capturingKey3=true;_lblPttKey3.Text="Press...";_lblPttKey3.BackColor=ACC;_lblPttKey3.ForeColor=Color.White;_btnAddKey3.Visible=false;_lblPttKey3.Visible=true;_lblKey3Label.Visible=true;_lblKey3Hint.Visible=true;KeyPreview=true;KeyDown+=OnKeyCapture3;}
        void OnKeyCapture3(object s,KeyEventArgs e){if(!_capturingKey3)return;e.Handled=true;e.SuppressKeyPress=true;if(e.KeyCode==Keys.Escape){if(_pttKeyCode3==0){UpdateKey3Visibility();}else{_lblPttKey3.Text=KeyName(_pttKeyCode3);}_lblPttKey3.BackColor=INPUT_BG;_lblPttKey3.ForeColor=ACC;_capturingKey3=false;KeyPreview=false;KeyDown-=OnKeyCapture3;return;}
            int vk=(int)e.KeyCode;
            if (vk == 0x10) vk = IsKeyDown(0xA1) ? 0xA1 : 0xA0;
            if (vk == 0x11) vk = IsKeyDown(0xA3) ? 0xA3 : 0xA2;
            if (vk == 0x12) vk = IsKeyDown(0xA5) ? 0xA5 : 0xA4;
            if(vk>=0xA0&&vk<=0xA5){try{if((GetAsyncKeyState(0xA1)&0x8000)!=0)vk=0xA1;else if((GetAsyncKeyState(0xA0)&0x8000)!=0)vk=0xA0;else if((GetAsyncKeyState(0xA3)&0x8000)!=0)vk=0xA3;else if((GetAsyncKeyState(0xA2)&0x8000)!=0)vk=0xA2;else if((GetAsyncKeyState(0xA5)&0x8000)!=0)vk=0xA5;else if((GetAsyncKeyState(0xA4)&0x8000)!=0)vk=0xA4;}catch{}}
            _pttKeyCode3=vk;
            // Duplicate check
            if(vk==_pttKeyCode || (_pttKeyCode2>0 && vk==_pttKeyCode2)){_capturingKey3=false;KeyPreview=false;KeyDown-=OnKeyCapture3;_pttKeyCode3=0;_settings.PushToTalkKey3=0;ShakeReject(_lblPttKey3, ()=>{UpdateKey3Visibility();});return;}
            _lblPttKey3.Text=KeyName(_pttKeyCode3);_lblPttKey3.BackColor=INPUT_BG;_lblPttKey3.ForeColor=ACC;_capturingKey3=false;KeyPreview=false;KeyDown-=OnKeyCapture3;_settings.PushToTalkKey3=_pttKeyCode3;_key3ShowOverlay=true;_settings.PttKey3ShowOverlay=true;if(_chkKey3Overlay!=null)_chkKey3Overlay.Checked=true;UpdateKey3Visibility();if(_onToggle!=null)_onToggle("ptt_key3:"+_pttKeyCode3);}
        void UpdateKey3Visibility(){bool hasKey3=_pttKeyCode3>0;_lblPttKey3.Visible=hasKey3;_lblKey3Label.Visible=hasKey3;_lblKey3Hint.Visible=hasKey3;_btnRemoveKey3.Visible=hasKey3;_btnAddKey3.Visible=!hasKey3 && _pttKeyCode2 > 0;if(_chkKey3Overlay!=null)_chkKey3Overlay.Visible=hasKey3;}
        CheckBox MakeOverlayCheck(int y, Panel card, bool initialOn, Action<bool> onChange) {
            // Custom eye toggle — owner-drawn, no text
            var eye = new CheckBox{Checked=initialOn,Appearance=Appearance.Button,FlatStyle=FlatStyle.Flat,Size=Dpi.Size(38,26),Location=Dpi.Pt(405,y),BackColor=Color.Transparent,TabStop=false};
            eye.FlatAppearance.BorderSize=0;
            eye.FlatAppearance.CheckedBackColor=Color.Transparent;
            eye.FlatAppearance.MouseDownBackColor=Color.Transparent;
            eye.FlatAppearance.MouseOverBackColor=Color.Transparent;
            bool hover = false;
            eye.MouseEnter += (s,e) => { hover=true; eye.Invalidate(); };
            eye.MouseLeave += (s,e) => { hover=false; eye.Invalidate(); };
            eye.Paint += (s,e) => {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int w = eye.Width, h = eye.Height;
                float cx = w/2f, cy = h/2f;
                bool on = eye.Checked;
                Color col = on ? (hover ? Color.FromArgb(140,220,255) : ACC) : (hover ? Color.FromArgb(90,90,90) : Color.FromArgb(55,55,55));
                float sc = Dpi.S(1);
                // Eye shape using bezier curves for smooth almond
                using (var path = new System.Drawing.Drawing2D.GraphicsPath()) {
                    // Top lid
                    path.AddBezier(cx-9*sc, cy, cx-4*sc, cy-6*sc, cx+4*sc, cy-6*sc, cx+9*sc, cy);
                    // Bottom lid
                    path.AddBezier(cx+9*sc, cy, cx+4*sc, cy+6*sc, cx-4*sc, cy+6*sc, cx-9*sc, cy);
                    path.CloseFigure();
                    using (var p = new Pen(col, 1.4f*sc)) g.DrawPath(p, path);
                }
                if (on) {
                    // Simple filled pupil
                    float pr = 2.5f*sc;
                    using (var b = new SolidBrush(col)) g.FillEllipse(b, cx-pr, cy-pr, pr*2, pr*2);
                } else {
                    // Diagonal slash
                    using (var p = new Pen(col, 1.6f*sc)) {
                        p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        g.DrawLine(p, cx-7*sc, cy+5*sc, cx+7*sc, cy-5*sc);
                    }
                }
            };
            var tt = new ToolTip();
            tt.SetToolTip(eye, initialOn ? "Popup visible — click to hide" : "Popup hidden — click to show");
            eye.CheckedChanged += (s,e) => {
                tt.SetToolTip(eye, eye.Checked ? "Popup visible — click to hide" : "Popup hidden — click to show");
                onChange(eye.Checked);
            };
            card.Controls.Add(eye);
            return eye;
        }
        void UpdateKey2Visibility(){bool hasKey2=_pttKeyCode2>0;_lblPttKey2.Visible=hasKey2;_lblKey2Label.Visible=hasKey2;_lblKey2Hint.Visible=hasKey2;_btnRemoveKey2.Visible=hasKey2;_btnAddKey2.Visible=!hasKey2;if(_chkKey2Overlay!=null)_chkKey2Overlay.Visible=hasKey2;if(_btnAddKey3!=null)UpdateKey3Visibility();}
        void OnKeyCapture(object s,KeyEventArgs e){if(!_capturingKey)return;e.Handled=true;e.SuppressKeyPress=true;if(e.KeyCode==Keys.Escape){_lblPttKey.Text=KeyName(_pttKeyCode);_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;_capturingKey=false;KeyPreview=false;KeyDown-=OnKeyCapture;return;}
            // WinForms gives generic modifier VK codes (0x10=Shift, 0x11=Ctrl, 0x12=Alt)
            // but the low-level keyboard hook sees specific left/right codes (0xA0/0xA1, 0xA2/0xA3, 0xA4/0xA5).
            // Translate generic → left-specific so the hook can match.
            int vk = (int)e.KeyCode;
            if (vk == 0x10) vk = IsKeyDown(0xA1) ? 0xA1 : 0xA0; // ShiftKey → LShift or RShift
            if (vk == 0x11) vk = IsKeyDown(0xA3) ? 0xA3 : 0xA2; // ControlKey → LCtrl or RCtrl
            if (vk == 0x12) vk = IsKeyDown(0xA5) ? 0xA5 : 0xA4; // Menu → LAlt or RAlt
            _pttKeyCode=vk;
            // Duplicate check
            if((_pttKeyCode2>0 && vk==_pttKeyCode2)||(_pttKeyCode3>0 && vk==_pttKeyCode3)){_capturingKey=false;KeyPreview=false;KeyDown-=OnKeyCapture;_pttKeyCode=_settings.PushToTalkKey;_lblPttKey.Text=KeyName(_pttKeyCode);ShakeReject(_lblPttKey);return;}
            _lblPttKey.Text=KeyName(_pttKeyCode);_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;_capturingKey=false;KeyPreview=false;KeyDown-=OnKeyCapture;_settings.PushToTalkKey=_pttKeyCode;if(_onToggle!=null)_onToggle("ptt_key:"+_pttKeyCode);}
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        static bool IsKeyDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
        string KeyName(int c){return PushToTalk.GetKeyName(c);}

        /// <summary>Shake a label left-right with red flash to indicate rejected input. Premium rejection feel.</summary>
        void ShakeReject(Label lbl, Action onComplete = null)
        {
            var origLoc = lbl.Location;
            var origBg = lbl.BackColor;
            var origFg = lbl.ForeColor;
            lbl.BackColor = Color.FromArgb(60, 20, 20);
            lbl.ForeColor = Color.FromArgb(220, 55, 55);
            int tick = 0;
            int[] offsets = { 6, -6, 5, -5, 3, -3, 2, -1, 0 };
            var shakeTimer = new Timer { Interval = 25 };
            shakeTimer.Tick += (s, e) => {
                if (tick >= offsets.Length) {
                    shakeTimer.Stop(); shakeTimer.Dispose();
                    lbl.Location = origLoc;
                    var fadeTimer = new Timer { Interval = 400 };
                    fadeTimer.Tick += (s2, e2) => { fadeTimer.Stop(); fadeTimer.Dispose(); lbl.BackColor = origBg; lbl.ForeColor = origFg; if (onComplete != null) onComplete(); };
                    fadeTimer.Start();
                    return;
                }
                lbl.Location = new Point(origLoc.X + Dpi.S(offsets[tick]), origLoc.Y);
                tick++;
            };
            shakeTimer.Start();
        }
        void ScanApps(){
            try{
                var ss=Audio.GetAudioSessions();
                int totalSessions = Audio.LastScanTotalSessions;
                int skippedSessions = Audio.LastScanSkippedSessions;
                if(ss==null||ss.Count==0){
                    string diag = "No apps with active audio sessions detected.\n\n";
                    diag += "Total raw sessions: " + totalSessions + "\n";
                    diag += "Sessions skipped: " + skippedSessions + "\n";
                    if (!string.IsNullOrEmpty(Audio.LastScanDiagnostics))
                        diag += "\nSkip reasons:\n" + Audio.LastScanDiagnostics;
                    diag += "\nTip: Apps must be actively playing audio to appear.";
                    DarkMessage.Show(diag, "Scan Running Apps");
                    return;
                }
                var existing = CollectAppRules();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var a in ss){
                    string n = a.ProcessName;
                    if (seen.Contains(n)) continue;
                    seen.Add(n);
                    // existing[] returns encoded values: positive=locked vol, negative=unlocked -(vol+1)
                    int rawExisting;
                    int vol;
                    if (existing.TryGetValue(n, out rawExisting))
                        vol = rawExisting >= 0 ? rawExisting : -(rawExisting + 1);
                    else
                        vol = (int)a.Volume;
                    // Update existing row or add new
                    var existRow = _appRows.Find(r => string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase));
                    if (existRow != null) {
                        if (existRow.Slider != null) existRow.Slider.Value = vol;
                        else existRow.InitialValue = vol;
                    } else {
                        _appRows.Add(new AppRuleRow { Name = n, InitialValue = vol });
                    }
                }
                RebuildAppList(ss); // Reuse sessions we already fetched
                DarkMessage.Show("Found " + ss.Count + " Audio App(s)\nClick the lock icon next to an app to enforce its volume.", "Scan Complete");
            }catch(Exception err){
                Logger.Error("ScanApps failed.",err);
                DarkMessage.Show("Scan failed: "+err.Message+"\n\nCheck the log file for details.", "Scan Error");
            }
        }

        /// <summary>Refreshes the overlay toggle from current settings (called externally when overlay is dismissed).</summary>
        public void RefreshOverlayToggle() {
            try {
                if (InvokeRequired) { BeginInvoke((Action)RefreshOverlayToggle); return; }
                _loading = true;
                _tglOverlay.Checked = _settings.MicOverlayEnabled;
                _loading = false;
            } catch { _loading = false; }
        }

        /// <summary>Blinks the overlay toggle row 3 times to draw attention.</summary>
        /// <summary>Smoothly animate a slider to a target value, then call onDone.</summary>
        void AnimateSlider(SlickSlider slider, int target, Action onDone)
        {
            if (slider.Value == target) { onDone?.Invoke(); return; }
            var tmr = new Timer { Interval = 16 };
            tmr.Tick += (s, e) => {
                int diff = target - slider.Value;
                int step = Math.Max(1, Math.Abs(diff) / 5);
                if (Math.Abs(diff) <= step) {
                    slider.Value = target;
                    tmr.Stop(); tmr.Dispose();
                    onDone?.Invoke();
                } else {
                    slider.Value += diff > 0 ? step : -step;
                }
            };
            tmr.Start();
        }

        /// <summary>Max All button on Apps page — smoothly animates all app sliders to max.</summary>
        void AnimateMaxAllApps()
        {
            foreach (var row in _appRows)
            {
                if (row.Slider == null) continue;
                int max = row.Slider.Maximum;
                AnimateSlider(row.Slider, max, null);
            }
        }

        public void BlinkOverlayToggle() {
            try {
                if (InvokeRequired) { BeginInvoke((Action)BlinkOverlayToggle); return; }
                if (_tglOverlay == null || _tglOverlay.Parent == null) return;
                var card = _tglOverlay.Parent;
                int tglY = _tglOverlay.Top;
                // Create a highlight panel over the toggle row
                var highlight = new Panel {
                    Location = new Point(Dpi.S(4), tglY - Dpi.S(4)),
                    Size = new Size(card.Width - Dpi.S(8), Dpi.S(42)),
                    BackColor = Color.FromArgb(40, ACC.R, ACC.G, ACC.B)
                };
                highlight.Paint += (s, e) => {
                    using (var p = new Pen(Color.FromArgb(120, ACC.R, ACC.G, ACC.B), 2f))
                        e.Graphics.DrawRectangle(p, 1, 1, highlight.Width - 3, highlight.Height - 3);
                };
                card.Controls.Add(highlight);
                highlight.BringToFront();
                _tglOverlay.BringToFront();
                // Find and bring the label controls to front too
                foreach (Control c in card.Controls) {
                    if (c is Label && c.Top >= tglY - Dpi.S(2) && c.Top <= tglY + Dpi.S(30))
                        c.BringToFront();
                }
                // Blink 4 times then remove
                int blinks = 0;
                var blinkTimer = new Timer { Interval = 300 };
                blinkTimer.Tick += (s, e) => {
                    blinks++;
                    highlight.Visible = !highlight.Visible;
                    if (blinks >= 8) { // 4 on + 4 off
                        blinkTimer.Stop(); blinkTimer.Dispose();
                        try { card.Controls.Remove(highlight); highlight.Dispose(); } catch { }
                    }
                };
                blinkTimer.Start();
            } catch { }
        }

        void LoadSettings(){try{ _loading=true;
            _tglAfkMic.Checked=_settings.AfkMicMuteEnabled;_nudAfkMicSec.Value=Clamp(_settings.AfkMicMuteSec,5,3600);
            _tglAfkSpk.Checked=_settings.AfkSpeakerMuteEnabled;_nudAfkSpkSec.Value=Clamp(_settings.AfkSpeakerMuteSec,5,3600);
            _tglPtt.Checked=_settings.PushToTalkEnabled;_tglPtm.Checked=_settings.PushToMuteEnabled;_tglPtToggle.Checked=_settings.PushToToggleEnabled;_pttKeyCode=_settings.PushToTalkKey;_lblPttKey.Text=KeyName(_pttKeyCode);_pttKeyCode2=_settings.PushToTalkKey2;_pttKeyCode3=_settings.PushToTalkKey3;_key1ShowOverlay=_settings.PttKey1ShowOverlay;_key2ShowOverlay=_settings.PttKey2ShowOverlay;_key3ShowOverlay=_settings.PttKey3ShowOverlay;if(_chkKey1Overlay!=null)_chkKey1Overlay.Checked=_key1ShowOverlay;if(_chkKey2Overlay!=null)_chkKey2Overlay.Checked=_key2ShowOverlay;if(_chkKey3Overlay!=null)_chkKey3Overlay.Checked=_key3ShowOverlay;if(_lblPttKey2!=null){_lblPttKey2.Text=_pttKeyCode2>0?KeyName(_pttKeyCode2):"";UpdateKey2Visibility();}if(_lblPttKey3!=null){_lblPttKey3.Text=_pttKeyCode3>0?KeyName(_pttKeyCode3):"";UpdateKey3Visibility();}
            _tglOverlay.Checked=_settings.MicOverlayEnabled;
            _tglMicEnf.Checked=_settings.MicEnforceEnabled;_trkMicVol.Value=Clamp(_settings.MicVolumePercent,0,100);_plMicVol.Text=_trkMicVol.Value+"%";
            _tglSpkEnf.Checked=_settings.SpeakerEnforceEnabled;_trkSpkVol.Value=Clamp(_settings.SpeakerVolumePercent,0,100);_plSpkVol.Text=_trkSpkVol.Value+"%";
            _tglAppEnf.Checked=_settings.AppVolumeEnforceEnabled;
            if(_settings.AppVolumeRules!=null&&_settings.AppVolumeRules.Count>0){_appRows.Clear();foreach(var kv in _settings.AppVolumeRules){bool locked=kv.Value>=0;int vol=locked?kv.Value:-(kv.Value+1);_appRows.Add(new AppRuleRow{Name=kv.Key,Locked=locked,InitialValue=Math.Max(0,Math.Min(100,vol))});}RebuildAppList();}
            _tglStartup.Checked=_settings.StartWithWindows;_tglNotifyCorr.Checked=_settings.NotifyOnCorrection;_tglNotifyDev.Checked=_settings.NotifyOnDeviceChange;
        }catch(Exception ex){Logger.Error("Options load failed.",ex);}finally{_loading=false;}}

        void DoSave(){try{
            _settings.AfkMicMuteEnabled=_tglAfkMic.Checked;_settings.AfkMicMuteSec=(int)_nudAfkMicSec.Value;
            _settings.AfkSpeakerMuteEnabled=_tglAfkSpk.Checked;_settings.AfkSpeakerMuteSec=(int)_nudAfkSpkSec.Value;
            _settings.PushToTalkEnabled=_tglPtt.Checked;_settings.PushToMuteEnabled=_tglPtm.Checked;_settings.PushToToggleEnabled=_tglPtToggle.Checked;_settings.PushToTalkKey=_pttKeyCode;_settings.PushToTalkKey2=_pttKeyCode2;_settings.PushToTalkConsumeKey=false;
            _settings.MicOverlayEnabled=_tglOverlay.Checked;
            _settings.MicEnforceEnabled=_tglMicEnf.Checked;_settings.MicVolumePercent=_trkMicVol.Value;
            _settings.SpeakerEnforceEnabled=_tglSpkEnf.Checked;_settings.SpeakerVolumePercent=_trkSpkVol.Value;
            _settings.AppVolumeEnforceEnabled=_tglAppEnf.Checked;_settings.AppVolumeRules=CollectAppRules();
            _settings.StartWithWindows=_tglStartup.Checked;_settings.NotifyOnCorrection=_tglNotifyCorr.Checked;_settings.NotifyOnDeviceChange=_tglNotifyDev.Checked;
            _settings.Save();DialogResult=DialogResult.OK;Close();
        }catch(Exception ex){Logger.Error("Options save failed.",ex);MessageBox.Show("Save failed: "+ex.Message,"Error",MessageBoxButtons.OK,MessageBoxIcon.Error);}}

        Dictionary<string,int> ParseAppRules(){ return CollectAppRules(); }
        static int Clamp(int v,int min,int max){return v<min?min:v>max?max:v;}
        public void OnRunWizard(){DialogResult=DialogResult.Retry;Close();}
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (keyData == Keys.Space && !_capturingKey) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }
        // WS_EX_COMPOSITED: forces all child controls to paint in a single composited pass.
        // Eliminates the parent-then-child paint gap that causes black line artifacts on sliders.
        private Timer _updateShimmerTimer;
        private Timer _saveOrbitTimer;
        private float _saveOrbitPhase;
        private Button _saveBtn;
        private float _updateShimmerX;
        private Button _updateBtn;
        private bool _updateShimmering;
        private DateTime _updateStartTime;
        private int _pendingUpdateResult; // 0=pending, 1=up-to-date, 2=update-available, -1=error
        private string _pendingLatestVer;

        private void CheckForUpdates(Button btn)
        {
            btn.Text = "Checking...";
            btn.ForeColor = Color.White;
            btn.FlatAppearance.BorderColor = ACC;
            _updateBtn = btn;
            _pendingUpdateResult = 0;
            _updateStartTime = DateTime.UtcNow;

            // Start shimmer
            _updateShimmerX = -0.3f;
            _updateShimmering = true;
            btn.Paint -= UpdateBtnShimmerPaint;
            btn.Paint += UpdateBtnShimmerPaint;
            if (_updateShimmerTimer == null)
            {
                _updateShimmerTimer = new Timer { Interval = 10 };
                _updateShimmerTimer.Tick += (s, e) =>
                {
                    if (!_updateShimmering) { _updateShimmerTimer.Stop(); _updateBtn?.Invalidate(); return; }
                    _updateShimmerX += 0.02f;
                    if (_updateShimmerX > 1.3f)
                    {
                        // Check if result is in AND minimum time elapsed
                        if (_pendingUpdateResult != 0 && (DateTime.UtcNow - _updateStartTime).TotalMilliseconds >= 1500)
                        {
                            _updateShimmering = false;
                            _updateShimmerTimer.Stop();
                            ApplyUpdateResult();
                            return;
                        }
                        _updateShimmerX = -0.3f;
                    }
                    _updateBtn?.Invalidate();
                };
            }
            _updateShimmerTimer.Start();

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // GitHub requires TLS 1.2
                    System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                    using (var client = new System.Net.WebClient())
                    {
                        client.Headers.Add("User-Agent", "AngryAudio/" + AppVersion.Version);
                        string raw = client.DownloadString("https://raw.githubusercontent.com/Gantera2k/Angry-Audio/main/version.txt");
                        string latest = raw.Trim().Trim('\uFEFF', '\u200B'); // Strip BOM and zero-width spaces
                        int cmp = CompareVersions(latest, AppVersion.Version);
                        _pendingLatestVer = latest;
                        _pendingUpdateResult = cmp > 0 ? 2 : 1;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Update check failed.", ex);
                    _pendingUpdateResult = -1;
                }
            });
        }

        private void UpdateBtnShimmerPaint(object sender, PaintEventArgs e)
        {
            if (!_updateShimmering) return;
            var btn = (Button)sender;
            int w = btn.Width, h = btn.Height;
            int bandW = Math.Max(w / 3, 20);
            int cx = (int)(_updateShimmerX * (w + bandW)) - bandW / 2;
            var shimmerRect = new Rectangle(cx - bandW / 2, 0, bandW, h);
            try
            {
                using (var lgb = new LinearGradientBrush(
                    new Point(shimmerRect.Left, 0), new Point(shimmerRect.Right, 0),
                    Color.Transparent, Color.Transparent))
                {
                    var cb = new System.Drawing.Drawing2D.ColorBlend(3);
                    cb.Colors = new[] {
                        Color.FromArgb(0, 255, 255, 255),
                        Color.FromArgb(45, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255)
                    };
                    cb.Positions = new[] { 0f, 0.5f, 1f };
                    lgb.InterpolationColors = cb;
                    e.Graphics.FillRectangle(lgb, shimmerRect);
                }
            }
            catch { }
        }

        private void ApplyUpdateResult()
        {
            if (_updateBtn == null) return;
            _updateBtn.Paint -= UpdateBtnShimmerPaint;
            _updateBtn.Invalidate();

            if (_pendingUpdateResult == 2)
            {
                _updateBtn.Text = "\u2B06 Update v" + _pendingLatestVer;
                _updateBtn.ForeColor = DarkTheme.Green;
                _updateBtn.FlatAppearance.BorderColor = DarkTheme.Green;
                _updateBtn.BackColor = Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);

                _updateBtn.Click -= _updateCheckHandler;
                _updateBtn.Click -= _updateDownloadHandler;
                _updateDownloadHandler = (s2, e2) => {
                    UpdateDialog.ShowUpdate(_pendingLatestVer);
                };
                _updateBtn.Click += _updateDownloadHandler;
            }
            else if (_pendingUpdateResult == 1)
            {
                _updateBtn.Text = "\u2714 Up to Date";
                _updateBtn.ForeColor = DarkTheme.Green;
                _updateBtn.FlatAppearance.BorderColor = DarkTheme.Green;
                _updateBtn.BackColor = Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);

            }
            else
            {
                _updateBtn.Text = "\u26A0 Check Failed \u2014 Retry";
                _updateBtn.ForeColor = Color.FromArgb(255, 180, 80);
                _updateBtn.FlatAppearance.BorderColor = Color.FromArgb(255, 180, 80);

            }
        }

        private EventHandler _updateDownloadHandler;
        private EventHandler _updateCheckHandler;

        /// <summary>Compares version strings like "51.1" vs "50.9". Returns positive if a > b.</summary>
        private static int CompareVersions(string a, string b)
        {
            var pa = a.Split('.'); var pb = b.Split('.');
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                int va = i < pa.Length ? int.Parse(pa[i].Trim()) : 0;
                int vb = i < pb.Length ? int.Parse(pb[i].Trim()) : 0;
                if (va != vb) return va - vb;
            }
            return 0;
        }

        // Research: https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles
        protected override CreateParams CreateParams {
            get {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }
    }

    class NudDefocusFilter : IMessageFilter {
        private Form _form;
        public NudDefocusFilter(Form form) { _form = form; }
        public bool PreFilterMessage(ref Message m) {
            // WM_LBUTTONDOWN = 0x0201
            if (m.Msg == 0x0201) {
                var active = _form.ActiveControl;
                if (active is NumericUpDown || (active != null && active.Parent is NumericUpDown)) {
                    var ctrl = Control.FromHandle(m.HWnd);
                    if (ctrl != null && !(ctrl is NumericUpDown) && !(ctrl != null && ctrl.Parent is NumericUpDown)) {
                        _form.ActiveControl = null;
                    }
                }
            }
            return false;
        }
    }
}
