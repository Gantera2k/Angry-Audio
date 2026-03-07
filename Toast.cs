// Toast.cs — Unified notification popup for Angry Audio.
// ONE class, ONE layout, ONE set of spacing constants.
// Replaces: CorrectionToast, InfoToast, MicWarningToast, FadeOverlay.
//
// Usage:
//   Toast.Show("Title", "Subtitle");                          // Simple notification
//   Toast.Show("Title", "Subtitle", accent: ToastAccent.Amber); // AFK/warning style
//   Toast.Show("Title", "Subtitle", accent: ToastAccent.Red, holdMs: 10000,
//       buttons: new[] { new ToastButton("Open Settings", () => ShowOptions()) });
//   var t = Toast.ShowProgress("Going AFK…", "Muting Soon", ToastAccent.Amber);
//   t.UpdateProgress(0.5f, "50%", "Fading Out");
//
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AngryAudio
{
    public enum ToastAccent { Blue, Amber, Red }

    public class ToastButton
    {
        public string Text;
        public Action OnClick;
        public bool Primary; // true = accent color text, false = grey
        public ToastButton(string text, Action onClick, bool primary = false) { Text = text; OnClick = onClick; Primary = primary; }
    }

    public class Toast : Form
    {
        // --- Win32 for no-focus show ---
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        // =====================================================================
        //  LAYOUT CONSTANTS — defined ONCE, used everywhere
        // =====================================================================
        const int TOAST_W = 300;         // Total width
        const int MASCOT_SZ = 32;        // Mascot icon size
        const int MASCOT_X = 10;         // Mascot left margin
        const int MASCOT_Y = 8;          // Mascot top
        const int TEXT_GAP = 8;          // Gap between mascot and text
        const int TITLE_Y = 8;           // Title top
        const int SUB_Y = 28;            // Subtitle top
        const int CONTENT_Y = 50;        // Buttons or progress bar top
        const int BTN_H = 22;            // Button height
        const int BTN_GAP = 6;           // Gap between side-by-side buttons
        const int BAR_H = 8;             // Progress bar height
        const int MARGIN = 10;           // General horizontal margin
        const int TIMER_BAR_H = 3;       // Thin timer bar height
        const int TIMER_TXT_GAP = 4;     // Gap below timer bar to countdown text
        const int TIMER_ZONE_H = 18;     // Total height of timer bar + countdown
        const int BOTTOM_PAD = 4;        // Padding below countdown text
        const int CORNER_R = 10;         // Rounded corner radius

        // =====================================================================
        //  STANDARD HOLD TIMES — uniform across all toasts
        // =====================================================================
        public const int HOLD_STANDARD = 4000;   // Simple notifications (4s)
        public const int HOLD_WITH_BUTTONS = 6000; // Toasts with action buttons (6s — need time to read + click)
        public const int HOLD_WARNING = 10000;   // Red warnings (10s — intentionally alarming)
        // Progress toasts have no auto-dismiss — caller controls lifecycle

        // =====================================================================
        //  COLORS
        // =====================================================================
        static readonly Color BG = Color.FromArgb(20, 20, 20);
        static readonly Color BG_RED = Color.FromArgb(18, 12, 12);
        static readonly Color BDR = Color.FromArgb(44, 44, 44);
        static readonly Color TXT = Color.FromArgb(230, 230, 230);
        static readonly Color TXT2 = Color.FromArgb(130, 130, 130);
        static readonly Color BTN_BG = Color.FromArgb(34, 34, 34);
        static readonly Color BTN_HOVER = Color.FromArgb(48, 48, 48);
        static readonly Color BTN_BDR = Color.FromArgb(60, 60, 60);

        // =====================================================================
        //  STATE
        // =====================================================================
        private string _title, _subtitle;
        private ToastAccent _accent;
        private ToastButton[] _buttons;
        private bool _hasProgress;
        private float _progressPct;
        private string _progressLabel, _progressStatus;
        private int _holdMs;

        private Timer _fadeInTimer, _holdTimer, _fadeOutTimer, _tickTimer;
        private bool _isClosing;
        private DateTime _createdAt;
        private Rectangle[] _btnRects;
        private bool[] _btnHovers;
        private float _pulsePhase; // For red warning pulse

        // Public result properties — callers check after FormClosed
        public int ClickedButton { get; private set; } = -1; // -1 = dismissed, 0+ = button index

        // =====================================================================
        //  CONSTRUCTOR — private, use static Show/ShowProgress
        // =====================================================================
        private Toast(string title, string subtitle, ToastAccent accent, int holdMs,
                      ToastButton[] buttons, bool hasProgress)
        {
            _title = title;
            _subtitle = subtitle;
            _accent = accent;
            _holdMs = holdMs;
            _buttons = buttons ?? new ToastButton[0];
            _hasProgress = hasProgress;
            _progressPct = hasProgress ? 1f : 0f;
            _createdAt = DateTime.UtcNow;

            ClickedButton = -1;
            _btnRects = new Rectangle[_buttons.Length];
            _btnHovers = new bool[_buttons.Length];

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = accent == ToastAccent.Red ? BG_RED : BG;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            Opacity = 0;

            // Calculate height
            int h = CalculateHeight();
            ClientSize = Dpi.Size(TOAST_W, h);

            Resize += (s, e) => DarkTheme.ApplyRoundedRegion(this, Dpi.S(CORNER_R));
            Load += (s, e) => DarkTheme.ApplyRoundedRegion(this, Dpi.S(CORNER_R));

            // Timers
            _fadeInTimer = new Timer { Interval = 16 };
            _fadeInTimer.Tick += (s, e) => {
                Opacity = Math.Min(Opacity + 0.06, 0.96);
                if (Opacity >= 0.95) { _fadeInTimer.Stop(); if (!_hasProgress) _holdTimer.Start(); }
            };

            _holdTimer = new Timer { Interval = holdMs };
            _holdTimer.Tick += (s, e) => { _holdTimer.Stop(); StartFadeOut(); };

            _fadeOutTimer = new Timer { Interval = 16 };
            _fadeOutTimer.Tick += (s, e) => {
                Opacity -= 0.04;
                if (Opacity <= 0.02) { _fadeOutTimer.Stop(); _isClosing = true; Close(); }
            };

            _tickTimer = new Timer { Interval = 50 };
            _tickTimer.Tick += (s, e) => { if (!_isClosing) { if (accent == ToastAccent.Red) _pulsePhase += 0.08f; Invalidate(); } };

            MouseClick += OnClick;
            MouseMove += OnMove;
            MouseLeave += (s, e) => { for (int i = 0; i < _btnHovers.Length; i++) _btnHovers[i] = false; Invalidate(); };

            ToastStack.Register(this);
            _fadeInTimer.Start();
            _tickTimer.Start();
        }

        int CalculateHeight()
        {
            bool hasContent = _buttons.Length > 0 || _hasProgress;
            int contentH = 0;
            if (_buttons.Length > 0) contentH = BTN_H + 8;
            else if (_hasProgress) contentH = BAR_H + 6 + 14; // bar + gap + label line

            // Base: title zone + subtitle zone + optional content + timer zone + bottom padding
            int h = CONTENT_Y; // top area (mascot + title + subtitle)
            if (hasContent) h = CONTENT_Y + contentH;
            else if (_subtitle != null) h = SUB_Y + 18; // just title + subtitle
            else h = TITLE_Y + 24; // just title

            h += TIMER_ZONE_H + BOTTOM_PAD;

            // Auto-size for long text
            using (var tmp = Graphics.FromHwnd(IntPtr.Zero))
            {
                int textW = Dpi.S(TOAST_W) - Dpi.S(MASCOT_X) - Dpi.S(MASCOT_SZ) - Dpi.S(TEXT_GAP) - Dpi.S(MARGIN);
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
                {
                    var sz = tmp.MeasureString(_title ?? "", f, textW);
                    if (sz.Height > Dpi.S(18)) h += 12; // title wraps
                }
                if (_subtitle != null)
                {
                    using (var f = new Font("Segoe UI", 7.5f))
                    {
                        var sz = tmp.MeasureString(_subtitle, f, textW);
                        if (sz.Height > Dpi.S(16)) h += 12; // subtitle wraps
                    }
                }
            }

            return Math.Max(h, 60); // minimum height
        }

        Color AccentColor {
            get {
                if (_accent == ToastAccent.Amber) return DarkTheme.Amber;
                if (_accent == ToastAccent.Red) return Color.FromArgb(220, 55, 55);
                return DarkTheme.Accent;
            }
        }

        // =====================================================================
        //  STATIC FACTORY METHODS
        // =====================================================================

        /// <summary>Show a simple notification toast. Message split on em-dash for title/subtitle.</summary>
        public static Toast Show(string message, ToastAccent accent = ToastAccent.Blue, int holdMs = 0, ToastButton[] buttons = null)
        {
            string title, subtitle;
            SplitMessage(message, out title, out subtitle);
            if (holdMs <= 0) holdMs = (buttons != null && buttons.Length > 0) ? HOLD_WITH_BUTTONS : HOLD_STANDARD;
            var t = new Toast(title, subtitle, accent, holdMs, buttons, false);
            t.ShowNoFocus();
            return t;
        }

        /// <summary>Show a toast with explicit title and subtitle.</summary>
        public static Toast Show(string title, string subtitle, ToastAccent accent = ToastAccent.Blue, int holdMs = 0, ToastButton[] buttons = null)
        {
            if (holdMs <= 0) holdMs = (buttons != null && buttons.Length > 0) ? HOLD_WITH_BUTTONS : HOLD_STANDARD;
            var t = new Toast(title, subtitle, accent, holdMs, buttons, false);
            t.ShowNoFocus();
            return t;
        }

        /// <summary>Show a progress toast (no auto-dismiss — caller updates and closes).</summary>
        public static Toast ShowProgress(string title, string subtitle, ToastAccent accent = ToastAccent.Amber)
        {
            var t = new Toast(title, subtitle, accent, 999999, null, true);
            t.ShowNoFocus();
            return t;
        }

        static void SplitMessage(string message, out string title, out string subtitle)
        {
            int idx = message.IndexOf('\u2014');
            if (idx > 0) { title = message.Substring(0, idx).Trim(); subtitle = message.Substring(idx + 1).Trim(); }
            else { title = message; subtitle = null; }
        }

        // =====================================================================
        //  PUBLIC API
        // =====================================================================

        /// <summary>Update progress bar (0.0 to 1.0) with label and status text.</summary>
        public void UpdateProgress(float pct, string label = null, string status = null)
        {
            _progressPct = Math.Max(0, Math.Min(1, pct));
            if (label != null) _progressLabel = label;
            if (status != null) _progressStatus = status;
            Action a = () => { try { if (!IsDisposed) Invalidate(); } catch { } };
            if (InvokeRequired) try { BeginInvoke(a); } catch { } else a();
        }

        public void ShowNoFocus()
        {
            if (!IsHandleCreated) CreateHandle();
            ShowWindow(Handle, 4);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040);
            // Re-assert topmost after 100ms to ensure we're above MicStatusOverlay
            var reTop = new Timer { Interval = 100 };
            reTop.Tick += (s, e) => { reTop.Stop(); reTop.Dispose(); try { if (!IsDisposed) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040); } catch { } };
            reTop.Start();
        }

        public void Dismiss()
        {
            if (_isClosing) return;
            _isClosing = true;
            _holdTimer.Stop(); _fadeInTimer.Stop(); _fadeOutTimer.Stop();
            var fast = new Timer { Interval = 16 };
            fast.Tick += (s, e) => {
                try { Opacity -= 0.15; } catch { }
                if (Opacity <= 0.05) { fast.Stop(); fast.Dispose(); try { Close(); } catch { } }
            };
            fast.Start();
        }

        /// <summary>Graceful fade out then close (for progress toasts).</summary>
        public void FadeOutAndClose()
        {
            if (_isClosing) return;
            _fadeInTimer.Stop();
            _fadeOutTimer.Start();
        }

        void StartFadeOut() { if (!_isClosing) _fadeOutTimer.Start(); }

        // =====================================================================
        //  PAINT — ONE unified layout
        // =====================================================================
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;
            Color acc = AccentColor;

            // --- Background ---
            var rr = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), Dpi.S(CORNER_R));
            using (var b = new SolidBrush(BackColor)) g.FillPath(b, rr);
            DarkTheme.PaintStars(g, w, h, _accent == ToastAccent.Red ? 99 : 88);

            // Red pulse vignette
            if (_accent == ToastAccent.Red)
            {
                float pulse = (float)(0.4 + 0.25 * Math.Sin(_pulsePhase));
                using (var glow = new PathGradientBrush(rr))
                {
                    glow.CenterColor = Color.FromArgb((int)(pulse * 40), acc.R, acc.G, acc.B);
                    glow.SurroundColors = new[] { Color.FromArgb(0, acc.R, acc.G, acc.B) };
                    glow.CenterPoint = new PointF(Dpi.S(30), h / 2f);
                    g.FillPath(glow, rr);
                }
            }

            using (var p = new Pen(_accent == ToastAccent.Red ? Color.FromArgb(60, acc.R / 2, acc.G / 4, acc.B / 4) : BDR))
                g.DrawPath(p, rr);
            rr.Dispose();

            // --- Left accent bar ---
            using (var b = new SolidBrush(acc))
                g.FillRectangle(b, 0, Dpi.S(6), Dpi.S(3), h - Dpi.S(12));

            // --- Top accent line ---
            using (var p = new Pen(Color.FromArgb(20, acc.R, acc.G, acc.B)))
                g.DrawLine(p, Dpi.S(MARGIN), 0, w - Dpi.S(MARGIN), 0);

            // --- Mascot ---
            Mascot.DrawMascot(g, Dpi.S(MASCOT_X), Dpi.S(MASCOT_Y), Dpi.S(MASCOT_SZ));

            int textX = Dpi.S(MASCOT_X + MASCOT_SZ + TEXT_GAP);
            int textW = w - textX - Dpi.S(MARGIN);

            // --- Title ---
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var b = new SolidBrush(_accent == ToastAccent.Red ? acc : TXT))
            {
                var rect = new RectangleF(textX, Dpi.S(TITLE_Y), textW, Dpi.S(24));
                g.DrawString(_title ?? "", f, b, rect, new StringFormat { Trimming = StringTrimming.EllipsisCharacter });
            }

            // --- Subtitle ---
            if (_subtitle != null)
            {
                using (var f = new Font("Segoe UI", 7.5f))
                using (var b = new SolidBrush(TXT2))
                {
                    var rect = new RectangleF(textX, Dpi.S(SUB_Y), textW, Dpi.S(34));
                    g.DrawString(_subtitle, f, b, rect);
                }
            }

            // --- Content zone (buttons OR progress bar) ---
            if (_buttons.Length > 0)
            {
                int btnY = Dpi.S(CONTENT_Y);
                int btnArea = w - Dpi.S(MARGIN) * 2;
                int gap = Dpi.S(BTN_GAP);
                int n = _buttons.Length;
                int btnW = n > 1 ? (btnArea - gap * (n - 1)) / n : btnArea;
                int bx = Dpi.S(MARGIN);

                for (int i = 0; i < n; i++)
                {
                    _btnRects[i] = new Rectangle(bx, btnY, btnW, Dpi.S(BTN_H));
                    DrawButton(g, _btnRects[i], _buttons[i].Text, _btnHovers[i], _buttons[i].Primary);
                    bx += btnW + gap;
                }
            }
            else if (_hasProgress)
            {
                int barX = textX;
                int barY = Dpi.S(CONTENT_Y);
                int barW = w - barX - Dpi.S(MARGIN);
                int barH = Dpi.S(BAR_H);
                int barR = barH / 2;

                // Track
                var trackPath = MakeRoundBar(barX, barY, barW, barH, barR);
                using (var b = new SolidBrush(Color.FromArgb(30, 30, 30))) g.FillPath(b, trackPath);
                trackPath.Dispose();

                // Fill
                int fillW = Math.Max(barH, (int)(barW * _progressPct));
                var fillPath = MakeRoundBar(barX, barY, fillW, barH, barR);
                using (var b = new SolidBrush(acc)) g.FillPath(b, fillPath);
                fillPath.Dispose();

                // Label + status
                int infoY = barY + barH + Dpi.S(6);
                if (_progressLabel != null)
                {
                    using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                    using (var b = new SolidBrush(acc))
                        g.DrawString(_progressLabel, f, b, barX, infoY);
                }
                if (_progressStatus != null)
                {
                    using (var f = new Font("Segoe UI", 6.5f))
                    using (var b = new SolidBrush(TXT2))
                    {
                        var sz = g.MeasureString(_progressStatus, f);
                        g.DrawString(_progressStatus, f, b, w - sz.Width - Dpi.S(MARGIN), infoY);
                    }
                }
            }

            // --- Timer bar + countdown (always at bottom) ---
            if (!_hasProgress)
            {
                double elapsed = (DateTime.UtcNow - _createdAt).TotalMilliseconds;
                double remaining = Math.Max(0, _holdMs + 800 - elapsed); // +800 for fade time
                float progress = (float)Math.Max(0, Math.Min(1, remaining / (_holdMs + 800)));
                int sec = (int)Math.Ceiling(remaining / 1000.0);

                int barY = h - Dpi.S(TIMER_ZONE_H);
                using (var b = new SolidBrush(Color.FromArgb(30, 30, 30)))
                    g.FillRectangle(b, 0, barY, w, Dpi.S(TIMER_BAR_H));
                using (var b = new SolidBrush(acc))
                    g.FillRectangle(b, 0, barY, (int)(w * progress), Dpi.S(TIMER_BAR_H));

                int txtY = barY + Dpi.S(TIMER_BAR_H) + Dpi.S(TIMER_TXT_GAP);
                using (var fBold = new Font("Segoe UI", 6.5f, FontStyle.Bold))
                using (var fReg = new Font("Segoe UI", 6.5f))
                {
                    string secTxt = sec + "s";
                    string rest = " \u00B7 Click to Dismiss";
                    var secSz = g.MeasureString(secTxt, fBold);
                    var restSz = g.MeasureString(rest, fReg);
                    float totalW = secSz.Width + restSz.Width;
                    float tx = (w - totalW) / 2f;
                    using (var b = new SolidBrush(acc))
                        g.DrawString(secTxt, fBold, b, tx, txtY);
                    using (var b = new SolidBrush(Color.FromArgb(60, 60, 60)))
                        g.DrawString(rest, fReg, b, tx + secSz.Width - Dpi.S(4), txtY);
                }
            }
        }

        void DrawButton(Graphics g, Rectangle r, string text, bool hover, bool primary)
        {
            Color bg, bdr, fg;
            if (_accent == ToastAccent.Red)
            {
                bg = hover ? Color.FromArgb(180, 45, 45) : Color.FromArgb(140, 35, 35);
                bdr = Color.FromArgb(hover ? 90 : 60, 255, 80, 80);
                fg = hover ? TXT : Color.FromArgb(220, 220, 220);
            }
            else
            {
                bg = hover ? BTN_HOVER : BTN_BG;
                bdr = hover ? Color.FromArgb(80, 80, 80) : BTN_BDR;
                fg = primary ? (hover ? Color.FromArgb(140, 220, 255) : DarkTheme.Accent)
                             : (hover ? Color.FromArgb(210, 210, 210) : TXT2);
            }

            using (var path = DarkTheme.RoundedRect(r, Dpi.S(4)))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                using (var p = new Pen(bdr)) g.DrawPath(p, path);
            }
            using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using (var b = new SolidBrush(fg))
                    g.DrawString(text, f, b, r, sf);
            }
        }

        GraphicsPath MakeRoundBar(int x, int y, int w, int h, int r)
        {
            var p = new GraphicsPath();
            if (w < h) w = h;
            p.AddArc(x, y, h, h, 90, 180);
            p.AddArc(x + w - h, y, h, h, 270, 180);
            p.CloseFigure();
            return p;
        }

        // =====================================================================
        //  MOUSE
        // =====================================================================
        void OnMove(object sender, MouseEventArgs e)
        {
            bool changed = false;
            for (int i = 0; i < _btnRects.Length; i++)
            {
                bool h = _btnRects[i].Contains(e.Location);
                if (h != _btnHovers[i]) { _btnHovers[i] = h; changed = true; }
            }
            if (changed) Invalidate();
        }

        void OnClick(object sender, MouseEventArgs e)
        {
            if (_isClosing) return;
            for (int i = 0; i < _btnRects.Length; i++)
            {
                if (_btnRects[i].Contains(e.Location))
                {
                    ClickedButton = i;
                    if (_buttons[i].OnClick != null) try { _buttons[i].OnClick(); } catch { }
                    _isClosing = true;
                    Close();
                    return;
                }
            }
            _isClosing = true; Close();
        }

        // =====================================================================
        //  WINFORMS OVERRIDES
        // =====================================================================
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000008 | 0x00000080 | 0x08000000 | 0x00000020;
                return cp;
            }
        }

        protected override void Dispose(bool d)
        {
            if (d)
            {
                if (_fadeInTimer != null) _fadeInTimer.Dispose();
                if (_holdTimer != null) _holdTimer.Dispose();
                if (_fadeOutTimer != null) _fadeOutTimer.Dispose();
                if (_tickTimer != null) _tickTimer.Dispose();
            }
            base.Dispose(d);
        }
    }
}
