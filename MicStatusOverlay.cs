using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AngryAudio
{
    public class MicStatusOverlay : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOWNOACTIVATE = 4;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        // Per-pixel alpha via UpdateLayeredWindow
        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE pSize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObj);
        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObj);
        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }
        const int ULW_ALPHA = 0x00000002;
        const int AC_SRC_OVER = 0x00;
        const int AC_SRC_ALPHA = 0x01;

        // Blur/feather padding around pill
        private const int BLUR_PAD = 24;

        // --- State ---
        private volatile bool _isMicOpenQueued; // Real-time atomic answer for background safety timers
        private bool _micOpen; // UI-thread ONLY visual state
        private bool _pttHeld;
        private bool _processing;  // blue "Processing..." state
        private float _opacity = 0f;
        private float _targetOpacity = 0f;

        // Drag
        private bool _dragging;
        private Point _dragStart;
        private bool _userPositioned;

        // Shimmer
        private float _shimmerX = -0.3f;
        private bool _shimmerActive;
        private bool _shimmerFinishing;
        private float _shimmerFadeTarget;
        private bool _shimmerWaiting = false;
        private DateTime _shimmerStartTime;
        private int _lastShowTick; // debounce guard — ignore rapid-fire calls within 50ms

        // Timers
        private Timer _fadeTimer;
        private Timer _topTimer;
        private System.Threading.Timer _shimmerTimer;
        private System.Threading.Timer _shimmerThreadTimer;
        private Timer _settleTimer;
        private Timer _dimTimer;

        // Opacity levels
        private const float BRIGHT = 1.0f;
        private const float DIM = 0.20f;
        private const float CLOSED_FLASH = 0.80f;
        private const int CLOSED_DISPLAY_MS = 1000;
        private const int DIM_DELAY_MS = 1000;

        // Colors
        static readonly Color BG_DARK  = Color.FromArgb(16, 16, 18);
        static readonly Color BG_LIGHT = Color.FromArgb(28, 28, 32);
        static readonly Color MIC_GREEN = Color.FromArgb(50, 210, 110);
        static readonly Color MIC_RED   = Color.FromArgb(225, 55, 55);
        static readonly Color PROC_BLUE = Color.FromArgb(90, 160, 255);

        // Pill geometry
        private int _pillW, _pillH;
        private int _dotSz, _padL, _gap, _padR;

        /// <summary>Fired when user clicks "Hide Overlay" in context menu.</summary>
        public event Action OnGoAway;
        /// <summary>Fired when user clicks "Open Options…" in context menu.</summary>
        public event Action OnOpenOptions;

        /// <summary>When false, all Show methods are no-ops.</summary>
        public bool OverlayEnabled { get; set; }
        /// <summary>When true, ShowMicClosed acts like PTT's ShowMicOpen.</summary>
        public bool PushToMuteMode { get; set; }
        /// <summary>When true, both open and closed states flash then fade to dim.</summary>
        public bool ToggleMode { get; set; }
        /// <summary>When true, forces held-state behavior.</summary>
        public bool MultiModeHeld { get; set; }
        public bool IsMicOpen { get { return _isMicOpenQueued; } }

        public MicStatusOverlay()
        {
            OverlayEnabled = true;
            PushToMuteMode = false;
            ToggleMode = false;

            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = BG_DARK;
            DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
            Cursor = Cursors.SizeAll;
            // Note: don't set Opacity here — it uses SetLayeredWindowAttributes which
            // conflicts with UpdateLayeredWindow. _opacity = 0f handles initial invisibility.

            // Calculate pill geometry
            _pillH = Dpi.S(34);
            _dotSz = Dpi.S(10);
            _padL = Dpi.S(14);
            _gap = Dpi.S(8);
            _padR = Dpi.S(16);

            SizeToContentSafe();

            // --- Drag support ---
            MouseDown += (s2, e2) => {
                if (e2.Button == MouseButtons.Left) {
                    _dragging = true; _dragStart = e2.Location;
                    _fadeTimer.Stop(); _dimTimer.Stop(); _settleTimer.Stop();
                    if (_opacity < 0.5f) { _opacity = 0.7f; PushLayered(); }
                }
            };
            MouseMove += (s2, e2) => {
                if (_dragging) {
                    _userPositioned = true;
                    int newX = Location.X + e2.X - _dragStart.X;
                    int newY = Location.Y + e2.Y - _dragStart.Y;
                    var clamped = ClampPosition(newX, newY);
                    Location = clamped;
                }
            };
            MouseUp += (s2, e2) => {
                if (_dragging) {
                    _dragging = false;
                    Location = ClampPosition(Location.X, Location.Y);
                    if (_pttHeld) { _dimTimer.Stop(); _dimTimer.Start(); }
                    else if (_micOpen) { _targetOpacity = DIM; _fadeTimer.Start(); }
                    else { _settleTimer.Interval = CLOSED_DISPLAY_MS; _settleTimer.Start(); }
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
            hideItem.Click += (s2, e2) => { if (OnGoAway != null) { try { OnGoAway(); } catch { } } };

            var optItem = new ToolStripMenuItem("Open Options\u2026");
            optItem.ForeColor = Color.FromArgb(200, 200, 200);
            optItem.Click += (s2, e2) => { if (OnOpenOptions != null) { try { OnOpenOptions(); } catch { } } };

            ctx.Items.Add(hideItem);
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add(optItem);

            ctx.Opened += (s2, e2) => {
                try {
                    var screen = Screen.FromControl(this);
                    var pos = PointToScreen(new Point(0, Height + Dpi.S(4)));
                    if (pos.Y + ctx.Height > screen.WorkingArea.Bottom)
                        pos = PointToScreen(new Point(0, -ctx.Height - Dpi.S(4)));
                    ctx.Location = pos;
                } catch { }
            };
            ContextMenuStrip = ctx;

            // --- Timers ---
            _fadeTimer = new Timer { Interval = 10 };
            _fadeTimer.Tick += FadeTick;

            _topTimer = new Timer { Interval = 3000 };
            _topTimer.Tick += (s, e) => {
                if (Visible && _opacity > 0 && !IsScreenCaptureActive())
                    try { SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); } catch { }
            };
            _topTimer.Start();

            _settleTimer = new Timer();
            _settleTimer.Tick += (s, e) => {
                _settleTimer.Stop();
                _shimmerActive = false;
                _shimmerFinishing = true;
                _shimmerFadeTarget = 0f;
                if (!_pttHeld)
                {
                    _targetOpacity = 0f;
                    _fadeTimer.Start();
                }
            };

            _dimTimer = new Timer { Interval = DIM_DELAY_MS };
            _dimTimer.Tick += (s, e) => {
                _dimTimer.Stop();
                if (_pttHeld) return;
                _shimmerActive = false;
                _shimmerFinishing = true;
                _shimmerFadeTarget = DIM;
                _targetOpacity = DIM;
                _fadeTimer.Start();
            };

            // Shimmer uses System.Threading.Timer for smooth ~120fps rendering.
            // WinForms Timer (WM_TIMER) is low-priority and causes visible stutter.
            _shimmerTimer = new System.Threading.Timer(_ => {
                if (_shimmerWaiting) return;
                if (!_shimmerActive && !_shimmerFinishing)
                {
                    _shimmerX = -0.3f;
                    try { if (IsHandleCreated) BeginInvoke(new Action(() => { _shimmerThreadTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite); PushLayered(); })); } catch { }
                    return;
                }

                // Time-based shimmer: 800ms per sweep (faster = smoother perceived animation)
                double elapsed = (DateTime.UtcNow - _shimmerStartTime).TotalMilliseconds;
                const double sweepMs = 950.0; // Just under 1 second — single sweep then fade
                double phase = (elapsed % sweepMs) / sweepMs; // 0.0 to 1.0
                float newX = -0.3f + (float)(phase * 1.6); // range -0.3 to 1.3

                // Detect sweep completion (X wrapped past 1.3)
                if (newX < _shimmerX && (_shimmerActive || _shimmerFinishing))
                {
                    if (_shimmerFinishing)
                    {
                        // Single sweep done — trigger fade immediately
                        _shimmerFinishing = false;
                        _shimmerActive = false;
                        try { if (IsHandleCreated) BeginInvoke(new Action(() => {
                            _shimmerThreadTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                            if (!_pttHeld)
                            {
                                _settleTimer.Stop();
                                _dimTimer.Stop();
                                _targetOpacity = _shimmerFadeTarget;
                                _fadeTimer.Start();
                            }
                            PushLayered();
                        })); } catch { }
                        return;
                    }
                    // _shimmerActive loops continuously
                }
                _shimmerX = newX;
                try { if (IsHandleCreated) BeginInvoke(new Action(PushLayered)); } catch { }
            }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _shimmerThreadTimer = _shimmerTimer;
        }

        // ====================
        // Clamping
        // ====================
        private Point ClampPosition(int x, int y)
        {
            int w = Width, h = Height;
            var center = new Point(x + w / 2, y + h / 2);
            Screen scr = Screen.FromPoint(center) ?? Screen.PrimaryScreen;
            var wa = scr.WorkingArea;
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
            Location = new Point(Math.Max(0, scr.WorkingArea.Right - Width - Dpi.S(8)), scr.WorkingArea.Top + Dpi.S(50));
        }

        private void ForceShow() {
            if (!IsHandleCreated) {
                CreateHandle();
                try { Application.DoEvents(); } catch { } // Force handle flush
            }
            if (IsScreenCaptureActive()) return;
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }

        private void SizeToContentSafe() {
            int textW;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font("Segoe UI", 9.2f, FontStyle.Bold))
            {
                float w1 = g.MeasureString("Mic Open", f).Width;
                float w2 = g.MeasureString("Mic Closed", f).Width;
                float w3 = g.MeasureString("Processing\u2026", f).Width;
                textW = (int)Math.Ceiling(Math.Max(w1, Math.Max(w2, w3)));
            }
            _pillW = _padL + _dotSz + _gap + textW + _padR;
            int pad = Dpi.S(BLUR_PAD);
            ClientSize = new Size(_pillW + pad * 2, _pillH + pad * 2);
        }

        // ====================
        // Public API
        // ====================


        public void ShowMicOpen() {
            if (!OverlayEnabled) return;
            _isMicOpenQueued = true;
            Action a = () => {
                _lastShowTick = Environment.TickCount;
                _micOpen = true;
                _processing = false;
                _shimmerX = -0.3f;
                _shimmerWaiting = false;

                float targetOp;
                if (ToggleMode && !MultiModeHeld) {
                    // Toggle tap: single sweep then fade to 0
                    _shimmerActive = false; _shimmerFinishing = true; _shimmerFadeTarget = 0f;
                    _pttHeld = false;
                    targetOp = BRIGHT;
                } else if (PushToMuteMode && !MultiModeHeld) {
                    // PTM release (mic opens): single sweep then dim
                    _shimmerActive = false; _shimmerFinishing = true; _shimmerFadeTarget = DIM;
                    _pttHeld = false;
                    targetOp = BRIGHT;
                } else {
                    // PTT/MM held: continuous sweep while held
                    _shimmerActive = true; _shimmerFinishing = false; _shimmerFadeTarget = BRIGHT;
                    _pttHeld = true;
                    targetOp = BRIGHT;
                }

                _settleTimer.Stop(); _dimTimer.Stop();
                if (_opacity < 0.01f) { SizeToContentSafe(); Position(); }
                // INSTANT appear — no fade-in ever.
                _opacity = targetOp;
                _targetOpacity = targetOp;
                _fadeTimer.Stop();
                ForceShow(); _shimmerStartTime = DateTime.UtcNow; _shimmerThreadTimer.Change(8, 8);

                PushLayered();
            };
            if (InvokeRequired) { try { BeginInvoke(a); } catch (Exception ex) { Logger.Error("MicOverlay ShowMicOpen Invoke failed: " + ex.Message); } } else a();
        }

        public void ShowMicClosed() {
            if (!OverlayEnabled) return;
            _isMicOpenQueued = false;
            Action a = () => {
                _lastShowTick = Environment.TickCount;
                _micOpen = false;
                _processing = false;
                _shimmerX = -0.3f;
                _shimmerWaiting = false;

                float targetOp;
                if (PushToMuteMode || MultiModeHeld) {
                    // PTM/MM held: continuous sweep while held
                    _shimmerActive = true; _shimmerFinishing = false;
                    _pttHeld = true;
                    targetOp = BRIGHT;
                } else if (ToggleMode) {
                    // Toggle tap: single sweep then fade to 0
                    _shimmerActive = false; _shimmerFinishing = true; _shimmerFadeTarget = 0f;
                    _pttHeld = false;
                    targetOp = CLOSED_FLASH;
                } else {
                    // PTT release: single sweep then fade to 0
                    _shimmerActive = false; _shimmerFinishing = true; _shimmerFadeTarget = 0f;
                    _pttHeld = false;
                    targetOp = CLOSED_FLASH;
                }

                _settleTimer.Stop(); _dimTimer.Stop();
                if (_opacity < 0.01f) { SizeToContentSafe(); Position(); }
                // INSTANT appear — no fade-in ever.
                _opacity = targetOp;
                _targetOpacity = targetOp;
                _fadeTimer.Stop();
                ForceShow(); _shimmerStartTime = DateTime.UtcNow; _shimmerThreadTimer.Change(8, 8);

                PushLayered();
            };
            if (InvokeRequired) { try { BeginInvoke(a); } catch (Exception ex) { Logger.Error("MicOverlay ShowMicClosed Invoke failed: " + ex.Message); } } else a();
        }

        public void ShowMicOpenIdle() {
            if (!OverlayEnabled) return;
            _isMicOpenQueued = true;
            Action a = () => {
                _micOpen = true; _pttHeld = false;
                _processing = false;
                _shimmerActive = false; _shimmerFinishing = true; _shimmerFadeTarget = DIM;
                _shimmerX = -0.3f;
                _shimmerWaiting = false;
                _settleTimer.Stop(); _dimTimer.Stop();
                if (_opacity < 0.01f) { SizeToContentSafe(); Position(); }
                // INSTANT appear — no fade-in. Snap to BRIGHT immediately.
                _opacity = BRIGHT;
                _targetOpacity = BRIGHT;
                _fadeTimer.Stop();
                ForceShow(); _shimmerStartTime = DateTime.UtcNow; _shimmerThreadTimer.Change(8, 8);
                PushLayered();
            };
            if (InvokeRequired) { try { BeginInvoke(a); } catch (Exception ex) { Logger.Error("MicOverlay ShowMicOpenIdle Invoke failed: " + ex.Message); } } else a();
        }

        public void HideOverlay() {
            _isMicOpenQueued = false;
            Action a = () => { 
                _pttHeld = false; _micOpen = false; _processing = false;
                _shimmerActive = false; _shimmerFinishing = false;
                MultiModeHeld = false;
                _shimmerX = -0.3f;
                _shimmerWaiting = false;
                _shimmerThreadTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite); 
                _settleTimer.Stop(); _dimTimer.Stop(); 
                _targetOpacity = 0f; _fadeTimer.Start(); 
            };
            if (InvokeRequired) { try { BeginInvoke(a); } catch (Exception ex) { Logger.Error("MicOverlay HideOverlay Invoke failed: " + ex.Message); } } else { if (IsHandleCreated) a(); }
        }

        public void ShowProcessing() {
            if (!OverlayEnabled) return;
            _isMicOpenQueued = true;
            Action a = () => {
                _processing = true;
                _micOpen = true;
                _pttHeld = false;
                _shimmerX = -0.3f;
                _shimmerWaiting = false;
                _shimmerActive = false;
                _shimmerFinishing = true;
                _shimmerFadeTarget = 0f;
                _settleTimer.Stop(); _dimTimer.Stop();
                if (_opacity < 0.01f) { SizeToContentSafe(); Position(); _opacity = 0f; }
                _targetOpacity = BRIGHT;
                ForceShow(); _fadeTimer.Start(); _shimmerStartTime = DateTime.UtcNow; _shimmerThreadTimer.Change(8, 8);
                PushLayered();
            };
            if (InvokeRequired) try { BeginInvoke(a); } catch { } else a();
        }

        // ====================
        // Focus & modal-awareness
        // ====================

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
                    if (cn == "Windows.UI.Core.CoreWindow" ||
                        cn == "XamlExplorerHostIslandWindow" ||
                        cn == "Xaml_WindowedPopupClass" ||
                        cn == "NotifyIconOverflowWindow")
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
                _fadeTimer.Stop();
                if (_opacity <= 0f) { ShowWindow(Handle, 0); return; }
                PushLayered();
                return;
            }
            // Fixed step: 0.1f per tick × 10ms interval = full 0→1 or 1→0 in exactly 100ms (0.1s)
            // No fade-in (Show methods snap opacity directly), so this only runs for fade-out/dim.
            float step = diff > 0 ? 0.1f : -0.1f;
            _opacity += step;
            if (diff > 0 && _opacity > _targetOpacity) _opacity = _targetOpacity;
            if (diff < 0 && _opacity < _targetOpacity) _opacity = _targetOpacity;
            PushLayered();
        }

        // ====================
        // Painting — premium fully custom-drawn pill with soft blurred edges
        // ====================
        protected override void OnPaint(PaintEventArgs e) {
            // All rendering is done in PushLayered via UpdateLayeredWindow
        }

        private void RenderPill(Graphics g, int totalW, int totalH) {
            int pad = Dpi.S(BLUR_PAD);
            int w = _pillW;
            int h = _pillH;

            // --- True Gaussian-Approximated Drop Shadow ---
            // Draw many concentric expanding arcs with exponentially decaying alpha.
            // This natively composites onto the transparent UpdateLayeredWindow background without bounds clipping.
            int maxRadius = Dpi.S(16);
            for (int r = maxRadius; r >= 1; r--)
            {
                // Exponential decay approximates a Gaussian distribution curve
                float t = (float)r / maxRadius;
                double alphaCurve = Math.Exp(-3.5f * t * t);
                int a = (int)(alphaCurve * 60); // Peak shadow intensity
                if (a <= 0) continue;
                
                using (var featherPath = new GraphicsPath()) {
                    featherPath.AddArc(pad - r, pad - r, h + r * 2, h + r * 2, 90, 180);
                    featherPath.AddArc(pad + w - h - r, pad - r, h + r * 2, h + r * 2, 270, 180);
                    featherPath.CloseFigure();
                    using (var pen = new Pen(Color.FromArgb(a, 0, 0, 0), Dpi.Sf(1.5f)))
                        g.DrawPath(pen, featherPath);
                }
            }

            // --- Pill background: translucent dark grey ---
            using (var pillPath = new GraphicsPath()) {
                pillPath.AddArc(pad, pad, h, h, 90, 180);
                pillPath.AddArc(pad + w - h, pad, h, h, 270, 180);
                pillPath.CloseFigure();
                using (var bgBrush = new SolidBrush(Color.FromArgb(200, 24, 24, 24)))
                    g.FillPath(bgBrush, pillPath);
            }

            // --- Premium White Shimmer sweep (when active) ---
            if ((_shimmerActive || _shimmerFinishing) && _shimmerX >= -0.3f && _shimmerX <= 1.3f) {
                using (var pillPath = new GraphicsPath()) {
                    pillPath.AddArc(pad, pad, h, h, 90, 180);
                    pillPath.AddArc(pad + w - h, pad, h, h, 270, 180);
                    pillPath.CloseFigure();
                    g.SetClip(pillPath);
                    int sweepW = w / 3;
                    int sweepX = pad + (int)(_shimmerX * w) - sweepW / 2;
                    using (var shimBrush = new LinearGradientBrush(
                        new Rectangle(sweepX, pad, sweepW, h),
                        Color.Transparent, Color.FromArgb(50, 255, 255, 255), 0f))
                    {
                        var blend = new ColorBlend(3);
                        blend.Colors = new[] { Color.Transparent, Color.FromArgb(90, 255, 255, 255), Color.Transparent };
                        blend.Positions = new[] { 0f, 0.5f, 1f };
                        shimBrush.InterpolationColors = blend;
                        g.FillRectangle(shimBrush, sweepX, pad, sweepW, h);
                    }
                    g.ResetClip();
                }
            }

            // --- Status dot with glow ---
            Color stateColor = _processing ? PROC_BLUE : (_micOpen ? MIC_GREEN : MIC_RED);
            int dotX = pad + _padL;
            int dotY = pad + (h - _dotSz) / 2;
            int glowSz = _dotSz + Dpi.S(8);
            int glowX2 = dotX - Dpi.S(4);
            int glowY2 = dotY - Dpi.S(4);
            using (var glowBr = new SolidBrush(Color.FromArgb(25, stateColor)))
                g.FillEllipse(glowBr, glowX2, glowY2, glowSz, glowSz);
            int glow2Sz = _dotSz + Dpi.S(4);
            int glow2X = dotX - Dpi.S(2);
            int glow2Y = dotY - Dpi.S(2);
            using (var glow2Br = new SolidBrush(Color.FromArgb(40, stateColor)))
                g.FillEllipse(glow2Br, glow2X, glow2Y, glow2Sz, glow2Sz);
            // Solid dot
            using (var dotBr = new SolidBrush(stateColor))
                g.FillEllipse(dotBr, dotX, dotY, _dotSz, _dotSz);
            // Specular highlight
            int specSz = _dotSz / 3;
            using (var specBr = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                g.FillEllipse(specBr, dotX + Dpi.S(2), dotY + Dpi.S(1), specSz, specSz);

            // --- Text with shadow ---
            string txt = _processing ? "Processing\u2026" : (_micOpen ? "Mic Open" : "Mic Closed");
            int textX = pad + _padL + _dotSz + _gap;
            int textY = pad + (h - Dpi.S(16)) / 2;
            using (var f = new Font("Segoe UI", 9.2f, FontStyle.Bold)) {
                TextRenderer.DrawText(g, txt, f, new Point(textX + 1, textY + 1), Color.FromArgb(40, 0, 0, 0), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, txt, f, new Point(textX, textY), Color.White, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }

            // --- Pill border with subtle colored glow ---
            using (var pillPath = new GraphicsPath()) {
                int bx = pad + 1, by = pad + 1, bw = w - 2, bh = h - 2;
                pillPath.AddArc(bx, by, bh, bh, 90, 180);
                pillPath.AddArc(bx + bw - bh, by, bh, bh, 270, 180);
                pillPath.CloseFigure();
                using (var glowPen = new Pen(Color.FromArgb(25, stateColor), 2.5f))
                    g.DrawPath(glowPen, pillPath);
                using (var borderPen = new Pen(Color.FromArgb(90, stateColor), 1f))
                    g.DrawPath(borderPen, pillPath);
            }
        }

        // ====================
        // State update — per-pixel alpha rendering
        // ====================
        private void PushLayered() {
            if (!IsHandleCreated) return;
            try {
                int w = ClientSize.Width, h = ClientSize.Height;
                if (w <= 0 || h <= 0) return;
                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        RenderPill(g, w, h);
                    }
                    // Apply master opacity by premultiplying alpha
                    byte masterA = (byte)Math.Max(0, Math.Min(255, (int)(_opacity * 255)));
                    if (masterA == 0) { ShowWindow(Handle, 0); return; }
                    var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                    unsafe {
                        byte* scan = (byte*)data.Scan0;
                        int stride = data.Stride;
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++) {
                                int i = y * stride + x * 4;
                                byte a = scan[i + 3];
                                if (a == 0) continue;
                                int na = (a * masterA + 127) / 255;
                                scan[i + 0] = (byte)((scan[i + 0] * na + 127) / 255); // B
                                scan[i + 1] = (byte)((scan[i + 1] * na + 127) / 255); // G
                                scan[i + 2] = (byte)((scan[i + 2] * na + 127) / 255); // R
                                scan[i + 3] = (byte)na;
                            }
                    }
                    bmp.UnlockBits(data);
                    IntPtr screenDC = IntPtr.Zero;
                    IntPtr memDC = IntPtr.Zero;
                    IntPtr hBmp = IntPtr.Zero;
                    IntPtr oldBmp = IntPtr.Zero;
                    try {
                        screenDC = GetDC(IntPtr.Zero);
                        memDC = CreateCompatibleDC(screenDC);
                        hBmp = bmp.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
                        oldBmp = SelectObject(memDC, hBmp);
                        var ptSrc = new POINT { x = 0, y = 0 };
                        var ptDst = new POINT { x = Location.X, y = Location.Y };
                        var size = new SIZE { cx = w, cy = h };
                        var blend = new BLENDFUNCTION {
                            BlendOp = AC_SRC_OVER, BlendFlags = 0,
                            SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA
                        };
                        UpdateLayeredWindow(Handle, screenDC, ref ptDst, ref size, memDC, ref ptSrc, 0, ref blend, ULW_ALPHA);
                    } finally {
                        if (oldBmp != IntPtr.Zero) SelectObject(memDC, oldBmp);
                        if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
                        if (memDC != IntPtr.Zero) DeleteDC(memDC);
                        if (screenDC != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDC);
                    }
                }
            } catch { }
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

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
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00000008 | 0x00000080 | 0x08000000 | 0x00080000; return cp; } }
        protected override void Dispose(bool d) {
            if (d) { if (_topTimer != null) _topTimer.Dispose(); if (_fadeTimer != null) _fadeTimer.Dispose(); if (_shimmerThreadTimer != null) _shimmerThreadTimer.Dispose(); if (_settleTimer != null) _settleTimer.Dispose(); if (_dimTimer != null) _dimTimer.Dispose(); }
            base.Dispose(d);
        }
    }
}
