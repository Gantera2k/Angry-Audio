using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AngryAudio
{
    public class MicStatusOverlay : Form
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOWNOACTIVATE = 4;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        // --- State ---
        private bool _micOpen;
        private bool _pttHeld;
        private float _opacity = 0f;
        private float _targetOpacity = 0f;

        // Drag
        private bool _dragging;
        private Point _dragStart;
        private bool _userPositioned;

        // Shimmer
        private float _shimmerX = -0.3f;
        private bool _shimmerActive; // explicit: only true during PTT hold or mic-closed flash
        private bool _shimmerFinishing; // true = finish current sweep then stop

        // Timers
        private Timer _fadeTimer;
        private Timer _topTimer;
        private Timer _shimmerTimer;
        private Timer _settleTimer;
        private Timer _dimTimer;

        // Opacity levels
        private const float BRIGHT = 0.92f;
        private const float DIM = 0.20f;
        private const float CLOSED_FLASH = 0.85f;
        private const int CLOSED_DISPLAY_MS = 1000;
        private const int DIM_DELAY_MS = 1000;
        private const int FIRST_SHOW_MS = 1500;
        private DateTime _extendedUntil = DateTime.MaxValue; // use 2s until this time passes
        private bool IsExtended { get { return DateTime.UtcNow < _extendedUntil; } }

        /// <summary>Fired when user clicks "Hide Overlay" in context menu.</summary>
        public event Action OnGoAway;
        /// <summary>Fired when user clicks "Open Options…" in context menu.</summary>
        public event Action OnOpenOptions;

        /// <summary>When false, all Show methods are no-ops. HideOverlay still works.</summary>
        public bool OverlayEnabled { get; set; } = true;
        /// <summary>When true, ShowMicClosed acts like PTT's ShowMicOpen (held shimmer) and ShowMicOpen fades to dim instead of disappearing.</summary>
        public bool PushToMuteMode { get; set; } = false;
        /// <summary>When true, both open and closed states flash then fade to dim — no held state.</summary>
        public bool ToggleMode { get; set; } = false;
        public bool IsMicOpen { get { return _micOpen; } }

        public MicStatusOverlay()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = Color.FromArgb(14, 14, 14);
            DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
            ClientSize = Dpi.Size(10, 30); Opacity = 0;
            Cursor = Cursors.SizeAll;
            ApplyPillRegion();
            Resize += (s2, e2) => ApplyPillRegion();

            // --- Drag support with real-time clamping ---
            MouseDown += (s2, e2) => {
                if (e2.Button == MouseButtons.Left) {
                    _dragging = true; _dragStart = e2.Location;
                    _fadeTimer.Stop(); _dimTimer.Stop(); _settleTimer.Stop();
                    // Ensure full opacity while dragging
                    if (_opacity < 0.5f) { _opacity = 0.7f; Opacity = _opacity; }
                }
            };
            MouseMove += (s2, e2) => {
                if (_dragging) {
                    _userPositioned = true;
                    int newX = Location.X + e2.X - _dragStart.X;
                    int newY = Location.Y + e2.Y - _dragStart.Y;
                    // Clamp BEFORE setting location to prevent fighting
                    var clamped = ClampPosition(newX, newY);
                    Location = clamped;
                }
            };
            MouseUp += (s2, e2) => {
                if (_dragging) {
                    _dragging = false;
                    Location = ClampPosition(Location.X, Location.Y);
                    if (_pttHeld) { /* held state — just reset dim timer */ _dimTimer.Stop(); _dimTimer.Start(); }
                    else if (_micOpen) { _targetOpacity = DIM; _fadeTimer.Start(); }
                    else { _settleTimer.Interval = IsExtended ? FIRST_SHOW_MS : CLOSED_DISPLAY_MS; _settleTimer.Start(); }
                }
            };

            // --- Right-click context menu ---
            var ctx = new ContextMenuStrip();
            ctx.BackColor = Color.FromArgb(24, 24, 24);
            ctx.ForeColor = Color.FromArgb(200, 200, 200);
            ctx.Renderer = new DarkContextRenderer();
            ctx.ShowImageMargin = false;
            ctx.Padding = new Padding(Dpi.S(2));
            ctx.Font = new Font("Segoe UI", 8.5f);

            var hideItem = new ToolStripMenuItem("Hide Overlay");
            hideItem.ForeColor = Color.FromArgb(220, 220, 220);
            hideItem.Click += (s2, e2) => { try { OnGoAway?.Invoke(); } catch { } };

            var optItem = new ToolStripMenuItem("Open Options\u2026");
            optItem.ForeColor = Color.FromArgb(200, 200, 200);
            optItem.Click += (s2, e2) => { try { OnOpenOptions?.Invoke(); } catch { } };

            ctx.Items.Add(hideItem);
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add(optItem);

            // Position menu below the pill, not overlapping it
            ctx.Opened += (s2, e2) => {
                try {
                    var screen = Screen.FromControl(this);
                    var pos = PointToScreen(new Point(0, Height + Dpi.S(4)));
                    // If it would go off bottom, show above instead
                    if (pos.Y + ctx.Height > screen.WorkingArea.Bottom)
                        pos = PointToScreen(new Point(0, -ctx.Height - Dpi.S(4)));
                    ctx.Location = pos;
                } catch { }
            };
            ContextMenuStrip = ctx;

            // --- Fade timer ---
            _fadeTimer = new Timer { Interval = 10 };
            _fadeTimer.Tick += FadeTick;

            // --- Topmost enforcer ---
            _topTimer = new Timer { Interval = 3000 };
            _topTimer.Tick += (s, e) => {
                if (Visible && _opacity > 0 && !IsScreenCaptureActive())
                    try { SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); } catch { }
            };
            _topTimer.Start();

            // --- Settle timer ---
            _settleTimer = new Timer();
            _settleTimer.Tick += (s, e) => {
                _settleTimer.Stop();
                // Let shimmer finish its current sweep gracefully
                _shimmerActive = false;
                _shimmerFinishing = true;
                if (!_pttHeld)
                {
                    _targetOpacity = 0f;
                    _fadeTimer.Start();
                }
            };

            // --- Dim timer ---
            _dimTimer = new Timer { Interval = FIRST_SHOW_MS };
            _dimTimer.Tick += (s, e) => {
                _dimTimer.Stop();
                // Don't fade if key is being held (PTT open or PTM closed)
                if (_pttHeld) return;
                // Fade to dim — used by toggle (both states), PTM (open state), idle
                _shimmerActive = false;
                _shimmerFinishing = true;
                _targetOpacity = DIM;
                _fadeTimer.Start();
            };

            // --- Shimmer timer ---
            _shimmerTimer = new Timer { Interval = 10 };
            _shimmerTimer.Tick += (s, e) => {
                if (!_shimmerActive && !_shimmerFinishing) { _shimmerTimer.Stop(); _shimmerX = -0.3f; Invalidate(); return; }
                _shimmerX += 0.032f;
                if (_shimmerX > 1.3f)
                {
                    if (_shimmerFinishing)
                    {
                        // Sweep complete — clean stop
                        _shimmerFinishing = false;
                        _shimmerActive = false;
                        _shimmerTimer.Stop();
                        _shimmerX = -0.3f;
                        Invalidate();
                        return;
                    }
                    _shimmerX = -0.3f;
                }
                Invalidate();
            };
        }

        // ====================
        // Screen clamping — keeps overlay FULLY on screen
        // ====================
        private Point ClampPosition(int x, int y)
        {
            int w = Width, h = Height;
            var center = new Point(x + w / 2, y + h / 2);
            Screen scr = Screen.FromPoint(center) ?? Screen.PrimaryScreen;
            var wa = scr.WorkingArea;

            // Fully contain: overlay must stay entirely within working area
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            if (x + w > wa.Right) x = wa.Right - w;
            if (y + h > wa.Bottom) y = wa.Bottom - h;

            return new Point(x, y);
        }

        // ====================
        // Positioning & layout
        // ====================
        void Position() {
            if (_userPositioned) return;
            var scr = Screen.PrimaryScreen;
            Location = new Point(Math.Max(0, scr.WorkingArea.Right - Width - Dpi.S(8)), Math.Max(0, scr.WorkingArea.Top + Dpi.S(8)));
        }

        void ApplyPillRegion() {
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;
            try {
                var path = DarkTheme.RoundedRect(new Rectangle(0, 0, w, h), h / 2);
                var old = Region; Region = new Region(path); if (old != null) old.Dispose(); path.Dispose();
            } catch { }
        }

        private void ForceShow() {
            if (!IsHandleCreated) CreateHandle();
            if (IsScreenCaptureActive()) return;
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }

        private void SizeToContentSafe() {
            // Always use the LONGER text ("Mic Closed") so the pill never changes size
            int h = Dpi.S(32);
            int dotSz = Dpi.S(10);
            int padL = Dpi.S(12);
            int gap = Dpi.S(7);
            int padR = Dpi.S(14);
            int textW;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font("Segoe UI", 9.2f, FontStyle.Bold))
                textW = (int)Math.Ceiling(Math.Max(g.MeasureString("Mic Open", f).Width, g.MeasureString("Mic Closed", f).Width));
            int w = padL + dotSz + gap + textW + padR;
            ClientSize = new Size(w, h);
            ApplyPillRegion();
        }

        // ====================
        // Public API
        // ====================

        /// <summary>PTT key held — mic is LIVE. Bright + shimmer. Stays bright until key released.</summary>
        /// <summary>Call when a feature is toggled on/off — next show uses 2s delay for clarity.</summary>
        public void UseExtendedDelay() { _extendedUntil = DateTime.UtcNow.AddSeconds(5); }

        public void ShowMicOpen() {
            if (!OverlayEnabled) return;
            _micOpen = true;
            _shimmerActive = true; _shimmerFinishing = false;
            _shimmerX = -0.3f;
            _settleTimer.Stop();
            _dimTimer.Stop();

            if (ToggleMode || PushToMuteMode) {
                // Toggle or PTM: no held state. Flash bright with shimmer, then fade to dim.
                _pttHeld = false;
                Action a = () => {
                    if (_opacity < 0.01f) { SizeToContentSafe(); Position(); _opacity = 0f; }
                    _targetOpacity = BRIGHT;
                    Opacity = Math.Max(_opacity, 0.01);
                    ForceShow(); _fadeTimer.Start(); _shimmerTimer.Start();
                    _dimTimer.Interval = IsExtended ? FIRST_SHOW_MS : DIM_DELAY_MS;
                    _dimTimer.Start();
                    Invalidate();
                };
                if (InvokeRequired) try { BeginInvoke(a); } catch { } else a();
                return;
            }

            // Normal PTT: held state — continuous shimmer, stays bright
            _pttHeld = true;
            Action a2 = () => {
                if (_opacity < 0.01f) { SizeToContentSafe(); Position(); _opacity = 0f; }
                _targetOpacity = BRIGHT;
                Opacity = Math.Max(_opacity, 0.01);
                ForceShow(); _fadeTimer.Start(); _shimmerTimer.Start();
                Invalidate();
            };
            if (InvokeRequired) try { BeginInvoke(a2); } catch { } else a2();
        }

        /// <summary>Mic closed. Behavior depends on mode:
        /// PTT: flash then fade away. PTM: held state with continuous shimmer. Toggle: flash then fade to dim.</summary>
        public void ShowMicClosed() {
            if (!OverlayEnabled) return;
            _micOpen = false;
            _shimmerActive = true; _shimmerFinishing = false;
            _shimmerX = -0.3f;
            _settleTimer.Stop();
            _dimTimer.Stop();

            if (PushToMuteMode) {
                // PTM: key is held — mic is muted. Continuous shimmer like PTT's open.
                _pttHeld = true;
                Action a = () => {
                    if (_opacity < 0.01f) { SizeToContentSafe(); Position(); _opacity = 0f; }
                    _targetOpacity = BRIGHT;
                    Opacity = Math.Max(_opacity, 0.01);
                    ForceShow(); _fadeTimer.Start(); _shimmerTimer.Start();
                    Invalidate();
                };
                if (InvokeRequired) try { BeginInvoke(a); } catch { } else a();
                return;
            }

            if (ToggleMode) {
                // Toggle: flash bright with shimmer, then fade to nothing.
                _pttHeld = false;
                Action a = () => {
                    if (_opacity < 0.01f) { SizeToContentSafe(); Position(); _opacity = 0f; }
                    _targetOpacity = CLOSED_FLASH;
                    Opacity = Math.Max(_opacity, 0.01);
                    ForceShow(); _fadeTimer.Start(); _shimmerTimer.Start();
                    _settleTimer.Interval = IsExtended ? FIRST_SHOW_MS : CLOSED_DISPLAY_MS;
                    _settleTimer.Start();
                    Invalidate();
                };
                if (InvokeRequired) try { BeginInvoke(a); } catch { } else a();
                return;
            }

            // Normal PTT: released — flash then fade to nothing.
            _pttHeld = false;
            Action a2 = () => {
                if (_opacity < 0.01f) { SizeToContentSafe(); Position(); _opacity = 0f; }
                _targetOpacity = CLOSED_FLASH;
                Opacity = Math.Max(_opacity, 0.01);
                ForceShow(); _fadeTimer.Start(); _shimmerTimer.Start();
                _settleTimer.Interval = IsExtended ? FIRST_SHOW_MS : CLOSED_DISPLAY_MS;
                _settleTimer.Start();
                Invalidate();
            };
            if (InvokeRequired) try { BeginInvoke(a2); } catch { } else a2();
        }

        /// <summary>Mic open in idle/dim state (non-PTT apps using mic). Appears bright with shimmer, fades to dim after 2s.</summary>
        public void ShowMicOpenIdle() {
            if (!OverlayEnabled) return;
            _micOpen = true; _pttHeld = false;
            _shimmerActive = true; _shimmerFinishing = false; // shimmer on entrance
            _settleTimer.Stop();
            _shimmerX = -0.3f;
            _dimTimer.Stop();
            Action a = () => {
                if (_opacity < 0.01f) { SizeToContentSafe(); Position(); _opacity = 0f; }
                _targetOpacity = BRIGHT;
                Opacity = Math.Max(_opacity, 0.01);
                ForceShow();
                _fadeTimer.Start();
                _shimmerTimer.Start();
                // After settle period, dimTimer will stop shimmer and fade to DIM
                _dimTimer.Interval = IsExtended ? FIRST_SHOW_MS : DIM_DELAY_MS;
                _dimTimer.Start();
                Invalidate();
            };
            if (InvokeRequired) try { BeginInvoke(a); } catch { } else a();
        }

        public void HideOverlay() {
            _pttHeld = false; _micOpen = false; _shimmerActive = false; _shimmerFinishing = false;
            _shimmerTimer.Stop(); _settleTimer.Stop(); _dimTimer.Stop();
            _shimmerX = -0.3f;
            Action a = () => { _targetOpacity = 0f; _fadeTimer.Start(); };
            if (InvokeRequired) try { BeginInvoke(a); } catch { } else a();
        }

        // ====================
        // Focus & modal-awareness
        // ====================

        /// <summary>
        /// Returns true if a system popup (Start Menu, etc.) is the foreground window.
        /// Used to make overlay click-through to prevent Windows error beep.
        /// </summary>
        private static bool IsSystemPopupActive()
        {
            try
            {
                var fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    var cls = new System.Text.StringBuilder(256);
                    GetClassName(fg, cls, 256);
                    string cn = cls.ToString();
                    if (cn == "Windows.UI.Core.CoreWindow" ||  // Start Menu, Search, Cortana
                        cn == "XamlExplorerHostIslandWindow" || // Win11 Start Menu
                        cn == "Xaml_WindowedPopupClass" ||      // Win11 popups
                        cn == "NotifyIconOverflowWindow")       // System tray overflow
                        return true;
                }
            }
            catch { }
            return false;
        }

        protected override void WndProc(ref Message m) {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_NOACTIVATE = 3;
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;

            // When a system popup is active, become click-through to prevent Windows error beep
            if (m.Msg == WM_NCHITTEST && IsSystemPopupActive())
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }

            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        // ====================
        // Fade engine
        // ====================
        void FadeTick(object sender, EventArgs e) {
            if (_dragging) return;
            float diff = _targetOpacity - _opacity;
            if (Math.Abs(diff) < 0.005f)
            {
                _opacity = _targetOpacity;
                Opacity = Math.Max(_opacity, 0);
                _fadeTimer.Stop();
                Invalidate();
                if (_opacity <= 0f) { ShowWindow(Handle, 0); return; }
                return;
            }
            // Exponential ease — lerp toward target. Feels buttery smooth.
            // Higher factor = snappier. Fade-in is fast, fade-out is gentle.
            float factor = diff > 0 ? 0.45f : 0.18f;
            _opacity += diff * factor;
            // Clamp to prevent overshooting
            if (diff > 0 && _opacity > _targetOpacity) _opacity = _targetOpacity;
            if (diff < 0 && _opacity < _targetOpacity) _opacity = _targetOpacity;
            Opacity = _opacity;
            Invalidate();
        }

        // ====================
        // Painting
        // ====================
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            Color accent = _micOpen ? DarkTheme.Green : Color.FromArgb(220, 55, 55);
            string text = _micOpen ? "Mic Open" : "Mic Closed";

            // Effect intensity scales with opacity so effects vanish as pill dims
            float fx = Math.Min(1f, _opacity / BRIGHT);

            var rr = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), h / 2);

            // Background: lighter, semi-transparent feel
            using (var grad = new LinearGradientBrush(
                new Point(0, 0), new Point(0, h),
                Color.FromArgb(38, 38, 42), Color.FromArgb(24, 24, 28)))
                g.FillPath(grad, rr);

            // Inner accent vignette — scales with fx
            if (fx > 0.1f)
            {
                try
                {
                    var oldClip = g.Clip;
                    g.SetClip(rr, CombineMode.Replace);
                    using (var glowPath = new GraphicsPath())
                    {
                        int glowR = (int)(h * 1.4);
                        glowPath.AddEllipse(-glowR / 4, -glowR / 4, glowR * 2, (int)(glowR * 1.4));
                        using (var pgb = new PathGradientBrush(glowPath))
                        {
                            int alpha = (int)(((_pttHeld ? 40 : 25) * fx));
                            pgb.CenterColor = Color.FromArgb(alpha, accent.R, accent.G, accent.B);
                            pgb.SurroundColors = new[] { Color.FromArgb(0, accent.R, accent.G, accent.B) };
                            pgb.CenterPoint = new PointF(Dpi.S(14), h / 2f);
                            g.FillPath(pgb, glowPath);
                        }
                    }
                    g.Clip = oldClip;
                }
                catch { }
            }

            // Shimmer sweep — renders during active AND finishing states
            if (_shimmerActive || _shimmerFinishing)
            {
                var oldClip = g.Clip;
                g.SetClip(rr, CombineMode.Replace);
                int bandW = Math.Max(w / 3, 36);
                int cx = (int)(_shimmerX * (w + bandW)) - bandW / 2;
                var shimmerRect = new Rectangle(cx - bandW / 2, 0, bandW, h);
                try
                {
                    using (var lgb = new LinearGradientBrush(
                        new Point(shimmerRect.Left, 0), new Point(shimmerRect.Right, 0),
                        Color.Transparent, Color.Transparent))
                    {
                        var cb = new ColorBlend(3);
                        // Shimmer alpha — brighter during held, still visible during fade
                        int shimAlpha = _shimmerFinishing ? (int)(50 * Math.Max(0.5f, fx)) : (int)(70 * Math.Max(0.5f, fx));
                        cb.Colors = new[] {
                            Color.FromArgb(0, 255, 255, 255),
                            Color.FromArgb(shimAlpha, 255, 255, 255),
                            Color.FromArgb(0, 255, 255, 255)
                        };
                        cb.Positions = new[] { 0f, 0.5f, 1f };
                        lgb.InterpolationColors = cb;
                        g.FillRectangle(lgb, shimmerRect);
                    }
                }
                catch { }
                g.Clip = oldClip;
            }

            // Border: clean accent line
            float outerWidth = _pttHeld ? 1.8f : 1.0f;
            int bdrAlpha = (int)((_pttHeld ? 180 : 120) * Math.Max(0.4f, fx));
            using (var p = new Pen(Color.FromArgb(bdrAlpha, accent.R, accent.G, accent.B), outerWidth))
                g.DrawPath(p, rr);

            // Top-half glass highlight — scales with fx
            if (fx > 0.2f)
            {
                try
                {
                    var oldClip = g.Clip;
                    g.SetClip(new Rectangle(0, 0, w, h / 2), CombineMode.Replace);
                    using (var p = new Pen(Color.FromArgb((int)(16 * fx), 255, 255, 255), 1f))
                        g.DrawPath(p, rr);
                    g.Clip = oldClip;
                }
                catch { }
            }
            rr.Dispose();

            // LED dot with glow — glow scales with fx
            int dotSz = Dpi.S(10);
            int padL = Dpi.S(12);
            int dotX = padL;
            int dotY = (h - dotSz) / 2;

            // Glow halo
            if (fx > 0.15f)
            {
                int glowSz = dotSz + Dpi.S(10);
                int glowX = dotX - Dpi.S(5);
                int glowY = dotY - Dpi.S(5);
                using (var glowPath = new GraphicsPath())
                {
                    glowPath.AddEllipse(glowX, glowY, glowSz, glowSz);
                    using (var pgb = new PathGradientBrush(glowPath))
                    {
                        int glowA = (int)((_pttHeld ? 80 : 50) * fx);
                        pgb.CenterColor = Color.FromArgb(glowA, accent.R, accent.G, accent.B);
                        pgb.SurroundColors = new[] { Color.FromArgb(0, accent.R, accent.G, accent.B) };
                        g.FillPath(pgb, glowPath);
                    }
                }
            }

            // Main dot
            using (var b = new SolidBrush(accent))
                g.FillEllipse(b, dotX, dotY, dotSz, dotSz);

            // Dot specular
            if (fx > 0.3f)
            {
                int hlSz = Math.Max(3, dotSz / 3);
                int hlOff = Math.Max(1, dotSz / 5);
                using (var b = new SolidBrush(Color.FromArgb((int)(80 * fx), 255, 255, 255)))
                    g.FillEllipse(b, dotX + hlOff, dotY + hlOff, hlSz, hlSz);
            }

            // Text with subtle shadow and state-tinted color
            int gapSz = Dpi.S(7);
            float textXPos = padL + dotSz + gapSz;
            using (var f = new Font("Segoe UI", 9.2f, FontStyle.Bold))
            {
                float textY = (h - f.Height) / 2f;
                // Shadow
                using (var sb = new SolidBrush(Color.FromArgb((int)(30 * fx), 0, 0, 0)))
                    g.DrawString(text, f, sb, textXPos + 1, textY + 1);
                // Main text — tinted toward accent color for cohesion
                Color textCol = _micOpen
                    ? Color.FromArgb(230, 255, 245)   // cool green-white
                    : Color.FromArgb(255, 235, 230);   // warm red-white
                using (var b = new SolidBrush(textCol))
                    g.DrawString(text, f, b, textXPos, textY);
            }
        }

        // ====================
        // Utility
        // ====================
        private static bool IsScreenCaptureActive() {
            try {
                var fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                var cls = new System.Text.StringBuilder(256);
                GetClassName(fg, cls, 256);
                string cn = cls.ToString();
                var title = new System.Text.StringBuilder(256);
                GetWindowText(fg, title, 256);
                string tt = title.ToString();
                if (cn == "Microsoft-Windows-SnipperToolbar" || cn == "ScreenClippingHost"
                    || tt.Contains("Snipping Tool") || tt.Contains("Snip & Sketch"))
                    return true;
            } catch { }
            return false;
        }

        private class DarkContextRenderer : ToolStripProfessionalRenderer
        {
            public DarkContextRenderer() : base(new DarkColorTable()) { }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var g = e.Graphics;
                var rect = new Rectangle(Dpi.S(3), 1, e.Item.Width - Dpi.S(6), e.Item.Height - 2);
                if (e.Item.Selected && e.Item.Enabled)
                {
                    int rad = Dpi.S(4);
                    using (var path = DarkTheme.RoundedRect(rect, rad))
                    using (var brush = new SolidBrush(Color.FromArgb(50, 50, 50)))
                        g.FillPath(brush, path);
                }
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var b = new SolidBrush(Color.FromArgb(24, 24, 24)))
                    e.Graphics.FillRectangle(b, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                int rad = Dpi.S(6);
                var rect = new Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
                using (var path = DarkTheme.RoundedRect(rect, rad))
                using (var p = new Pen(Color.FromArgb(50, 50, 50)))
                    e.Graphics.DrawPath(p, path);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (var p = new Pen(Color.FromArgb(40, 40, 40)))
                    e.Graphics.DrawLine(p, Dpi.S(10), y, e.Item.Width - Dpi.S(10), y);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Selected ? Color.White : Color.FromArgb(200, 200, 200);
                e.TextFormat &= ~TextFormatFlags.HidePrefix;
                base.OnRenderItemText(e);
            }

            private class DarkColorTable : ProfessionalColorTable
            {
                public override Color ImageMarginGradientBegin { get { return Color.FromArgb(24, 24, 24); } }
                public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(24, 24, 24); } }
                public override Color ImageMarginGradientEnd { get { return Color.FromArgb(24, 24, 24); } }
                public override Color MenuBorder { get { return Color.FromArgb(50, 50, 50); } }
                public override Color MenuItemBorder { get { return Color.Transparent; } }
                public override Color SeparatorDark { get { return Color.FromArgb(40, 40, 40); } }
                public override Color SeparatorLight { get { return Color.Transparent; } }
                public override Color ToolStripDropDownBackground { get { return Color.FromArgb(24, 24, 24); } }
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00000008 | 0x00000080 | 0x08000000; return cp; } }
        protected override void Dispose(bool d) {
            if (d) { _topTimer?.Dispose(); _fadeTimer?.Dispose(); _shimmerTimer?.Dispose(); _settleTimer?.Dispose(); _dimTimer?.Dispose(); }
            base.Dispose(d);
        }
    }
}
