// Toast.cs — Unified notification popup for Angry Audio.
// ONE class, ONE layout, ONE set of spacing constants.
// Fully custom-painted border/glow/stripe/progress bar for premium look.
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
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace AngryAudio
{
    /// <summary>Dedicated STA thread with its own message pump for popup forms.
    /// Toasts and splash screen run here so they never skip frames when main UI is busy.</summary>
    public static class PopupThread
    {
        private static Thread _thread;
        private static Form _hidden;          // invisible form that owns the message pump
        private static readonly object _lock = new object();
        private static readonly ManualResetEvent _ready = new ManualResetEvent(false);

        /// <summary>Ensure the popup thread is running.</summary>
        public static void EnsureRunning()
        {
            lock (_lock)
            {
                if (_thread != null && _thread.IsAlive) return;
                _ready.Reset();
                _thread = new Thread(Run) { Name = "PopupThread", IsBackground = true };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                _ready.WaitOne(3000); // wait for message pump to start
            }
        }

        /// <summary>Run an action on the popup thread (thread-safe).</summary>
        public static void Invoke(Action action)
        {
            EnsureRunning();
            if (_hidden != null && !_hidden.IsDisposed && _hidden.IsHandleCreated)
            {
                try { _hidden.BeginInvoke(action); } catch { }
            }
        }

        private static void Run()
        {
            _hidden = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized, FormBorderStyle = FormBorderStyle.None, Size = new Size(1, 1), StartPosition = FormStartPosition.Manual, Location = new Point(-9999, -9999) };
            _hidden.Load += (s, e) => { _hidden.Visible = false; _ready.Set(); };
            Application.Run(_hidden);
        }
    }

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

        private Label _lblTitle;
        private PictureBox _pbIcon;
        private Button[] _uiButtons;
        private ProgressBar _progBar;
        private Label _lblProgText;
        private Label _lblStatus;

        // Public result properties — callers check after FormClosed
        public int ClickedButton { get; private set; } // -1 = dismissed, 0+ = button index

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

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = accent == ToastAccent.Red ? BG_RED : BG;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            Opacity = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

            // Calculate height
            int h = CalculateHeight();
            ClientSize = Dpi.Size(TOAST_W, h);
            DarkTheme.ApplyRoundedRegion(this, Dpi.S(CORNER_R));

            Color acc = accent == ToastAccent.Red ? Color.FromArgb(220, 55, 55) : (accent == ToastAccent.Amber ? DarkTheme.Amber : DarkTheme.Accent);

            _pbIcon = new PictureBox { Location = Dpi.Pt(MASCOT_X, MASCOT_Y), Size = Dpi.Size(MASCOT_SZ, MASCOT_SZ), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            Controls.Add(_pbIcon);

            int textX = MASCOT_X + MASCOT_SZ + TEXT_GAP;
            int textW = TOAST_W - textX - MARGIN;

            _lblTitle = new Label { Location = Dpi.Pt(textX, TITLE_Y), Size = Dpi.Size(textW, 20), Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = accent == ToastAccent.Red ? acc : TXT, BackColor = Color.Transparent };
            _lblTitle.Text = _title;
            Controls.Add(_lblTitle);

            if (_subtitle != null) {
                // Subtitle is now custom-drawn in OnPaint to support accent-colored parentheses
            }

            if (_buttons.Length > 0) {
                _uiButtons = new Button[_buttons.Length];
                int btnY = CONTENT_Y;
                int btnArea = TOAST_W - MARGIN * 2;
                int n = _buttons.Length;
                int btnW = n > 1 ? (btnArea - BTN_GAP * (n - 1)) / n : btnArea;
                int bx = MARGIN;

                for (int i = 0; i < n; i++) {
                    int btnIndex = i;
                    var ub = new Button { Text = _buttons[i].Text, Location = Dpi.Pt(bx, btnY), Size = Dpi.Size(btnW, BTN_H), FlatStyle = FlatStyle.Flat, BackColor = BTN_BG, ForeColor = _buttons[i].Primary ? acc : TXT };
                    ub.Click += (s, e) => { ClickedButton = btnIndex; if (_buttons[btnIndex].OnClick != null) _buttons[btnIndex].OnClick(); _isClosing = true; Close(); };
                    _uiButtons[i] = ub;
                    Controls.Add(ub);
                    bx += btnW + BTN_GAP;
                }
            } else if (_hasProgress) {
                int barY = CONTENT_Y;
                int barW = TOAST_W - textX - MARGIN;
                _progBar = new ProgressBar { Location = Dpi.Pt(textX, barY), Size = Dpi.Size(barW, BAR_H), Minimum = 0, Maximum = 100 };
                Controls.Add(_progBar);

                int infoY = barY + BAR_H + 6;
                _lblProgText = new Label { Location = Dpi.Pt(textX, infoY), Size = Dpi.Size(barW / 2, 16), Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), ForeColor = acc };
                Controls.Add(_lblProgText);

                _lblStatus = new Label { Location = Dpi.Pt(textX + barW / 2, infoY), Size = Dpi.Size(barW / 2, 16), Font = new Font("Segoe UI", 6.5f), ForeColor = TXT2, TextAlign = ContentAlignment.TopRight };
                Controls.Add(_lblStatus);
            }
            
            try { using (var bmp = new Bitmap(32, 32)) using (var g = Graphics.FromImage(bmp)) { Mascot.DrawMascot(g, 0, 0, 32); _pbIcon.Image = (Bitmap)bmp.Clone(); } } catch { }

            // Timers
            _fadeInTimer = new Timer { Interval = 10 };
            _fadeInTimer.Tick += (s, e) => {
                Opacity = Math.Min(Opacity + 0.20, 0.88);
                if (Opacity >= 0.87) { _fadeInTimer.Stop(); if (!_hasProgress) _holdTimer.Start(); }
            };

            _holdTimer = new Timer { Interval = holdMs };
            _holdTimer.Tick += (s, e) => { _holdTimer.Stop(); StartFadeOut(); };

            _fadeOutTimer = new Timer { Interval = 10 };
            _fadeOutTimer.Tick += (s, e) => {
                Opacity -= 0.20;
                if (Opacity <= 0.02) { _fadeOutTimer.Stop(); _isClosing = true; Close(); }
            };

            _tickTimer = new Timer { Interval = 16 };
            _tickTimer.Tick += (s, e) => { if (!_isClosing) Invalidate(); };

            MouseClick += (s, e) => { if (!_isClosing) { _isClosing = true; Close(); } };

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
            Toast t = null;
            int hm = holdMs;
            PopupThread.Invoke(() => { t = new Toast(title, subtitle, accent, hm, buttons, false); t.ShowNoFocus(); });
            return t;
        }

        /// <summary>Show a toast with explicit title and subtitle.</summary>
        public static Toast Show(string title, string subtitle, ToastAccent accent = ToastAccent.Blue, int holdMs = 0, ToastButton[] buttons = null)
        {
            if (holdMs <= 0) holdMs = (buttons != null && buttons.Length > 0) ? HOLD_WITH_BUTTONS : HOLD_STANDARD;
            Toast t = null;
            int hm = holdMs;
            PopupThread.Invoke(() => { t = new Toast(title, subtitle, accent, hm, buttons, false); t.ShowNoFocus(); });
            return t;
        }

        /// <summary>Show a progress toast (no auto-dismiss — caller updates and closes).</summary>
        public static Toast ShowProgress(string title, string subtitle, ToastAccent accent = ToastAccent.Amber)
        {
            Toast t = null;
            PopupThread.Invoke(() => { t = new Toast(title, subtitle, accent, 999999, null, true); t.ShowNoFocus(); });
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
            Action a = () => { 
                try { 
                    if (!IsDisposed && _progBar != null) {
                        _progBar.Value = (int)(_progressPct * 100);
                        if (_progressLabel != null) _lblProgText.Text = _progressLabel;
                        if (_progressStatus != null) _lblStatus.Text = _progressStatus;
                    } 
                } catch { } 
            };
            if (InvokeRequired) try { BeginInvoke(a); } catch { } else a();
        }

        public void ShowNoFocus()
        {
            if (!IsHandleCreated) CreateHandle();
            Visible = true;
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

        // ── Custom painting for uniform look ──
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;
            int r = Dpi.S(CORNER_R);
            Color acc = AccentColor;

            // Top-edge glow
            using (var glowBr = new LinearGradientBrush(new Rectangle(0, 0, w, Dpi.S(4)),
                Color.FromArgb(40, acc), Color.FromArgb(0, acc), 90f))
                g.FillRectangle(glowBr, 0, 0, w, Dpi.S(4));

            int maxRadius = Dpi.S(16);
            for (int r2 = maxRadius; r2 >= 1; r2--)
            {
                float t = (float)r2 / maxRadius;
                double alphaCurve = Math.Exp(-3.5f * t * t);
                int a = (int)(alphaCurve * 60);
                if (a <= 0) continue;
                using (var featherPath = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), r + r2))
                using (var pen = new Pen(Color.FromArgb(a, 0, 0, 0), Dpi.Sf(1.5f)))
                    g.DrawPath(pen, featherPath);
            }

            using (var path = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), r)) {
                // Specular glass shine (subtle top-half gradient)
                using (var shineBr = new LinearGradientBrush(new Rectangle(0, 0, w, h / 2 + 1), Color.FromArgb(12, 255, 255, 255), Color.Transparent, 90f)) {
                    g.SetClip(path);
                    g.FillRectangle(shineBr, 0, 0, w, h / 2 + 1);
                    g.ResetClip();
                }

                using (var glowPen = new Pen(Color.FromArgb(25, acc), 3f))
                    g.DrawPath(glowPen, path);
                using (var pen = new Pen(Color.FromArgb(110, acc), 1.2f))
                    g.DrawPath(pen, path);
            }

            // Accent stripe (gradient)
            using (var stripeBr = new LinearGradientBrush(new Rectangle(0, Dpi.S(6), Dpi.S(3), Dpi.S(30)),
                acc, Color.FromArgb(0, acc), 90f))
            using (var stripe = new Pen(stripeBr, Dpi.S(2)))
                g.DrawLine(stripe, Dpi.S(2), Dpi.S(8), Dpi.S(2), Dpi.S(38));

            // Custom subtitle with accent-colored parenthesized values
            if (_subtitle != null)
            {
                int tx = Dpi.S(MASCOT_X + MASCOT_SZ + TEXT_GAP);
                int ty = Dpi.S(SUB_Y);
                using (var fNorm = new Font("Segoe UI", 7.5f))
                using (var fBold = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                {
                    int x = tx;
                    string txt = _subtitle;
                    int idx = 0;
                    var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                    while (idx < txt.Length)
                    {
                        int open = txt.IndexOf('(', idx);
                        if (open < 0) {
                            TextRenderer.DrawText(g, txt.Substring(idx), fNorm, new Point(x, ty), TXT2, flags);
                            break;
                        }
                        if (open > idx) {
                            string before = txt.Substring(idx, open - idx);
                            var sz = TextRenderer.MeasureText(g, before, fNorm, new Size(999, 30), flags);
                            TextRenderer.DrawText(g, before, fNorm, new Point(x, ty), TXT2, flags);
                            x += sz.Width;
                        }
                        int close = txt.IndexOf(')', open);
                        if (close < 0) {
                            TextRenderer.DrawText(g, txt.Substring(open), fNorm, new Point(x, ty), TXT2, flags);
                            break;
                        }
                        string paren = txt.Substring(open, close - open + 1);
                        var psz = TextRenderer.MeasureText(g, paren, fBold, new Size(999, 30), flags);
                        TextRenderer.DrawText(g, paren, fBold, new Point(x, ty), acc, flags);
                        x += psz.Width;
                        idx = close + 1;
                    }
                }
            }

            // Timer bar + countdown (only for non-progress toasts)
            if (!_hasProgress)
            {
                double elapsed = (DateTime.UtcNow - _createdAt).TotalMilliseconds;
                double totalMs = _holdMs + 800; // account for fade-in + fade-out
                double pct = Math.Max(0, 1.0 - elapsed / totalMs);
                int barY = h - Dpi.S(16);
                int barH = Dpi.S(3);
                int barMaxW = w - Dpi.S(24);
                int barX = Dpi.S(12);

                // Track
                Color trackCol = _accent == ToastAccent.Red ? Color.FromArgb(35, 20, 20) : Color.FromArgb(32, 32, 32);
                using (var trackPath = CorrectionToast.RoundedBar(barX, barY, barMaxW, barH))
                using (var trackBr = new SolidBrush(trackCol))
                    g.FillPath(trackBr, trackPath);
                // Active bar
                int barW = Math.Max(barH, (int)(barMaxW * pct));
                if (barW > barH) {
                    using (var barPath = CorrectionToast.RoundedBar(barX, barY, barW, barH)) {
                        using (var glwBr = new SolidBrush(Color.FromArgb(20, acc)))
                            g.FillPath(glwBr, CorrectionToast.RoundedBar(barX, barY - 1, barW, barH + 2));
                        using (var barGrad = new LinearGradientBrush(new Rectangle(barX, barY, barW, barH),
                            acc, Color.FromArgb(acc.R * 3 / 4, acc.G * 3 / 4, acc.B * 3 / 4), 0f))
                            g.FillPath(barGrad, barPath);
                        int dotR = barH + Dpi.S(2);
                        using (var dotBr = new SolidBrush(Color.FromArgb(180, acc)))
                            g.FillEllipse(dotBr, barX + barW - dotR / 2, barY - Dpi.S(1), dotR, dotR);
                    }
                }
                // Countdown text
                int secs = (int)Math.Ceiling(Math.Max(0, totalMs - elapsed) / 1000.0);
                string timerTxt = secs + "s \u2013 Click dismiss";
                using (var fTimer = new Font("Segoe UI", 6.8f))
                {
                    var tsz = TextRenderer.MeasureText(timerTxt, fTimer);
                    TextRenderer.DrawText(g, timerTxt, fTimer,
                        new Point((w - tsz.Width) / 2, barY - Dpi.S(13)),
                        Color.FromArgb(80, 80, 80), TextFormatFlags.NoPrefix);
                }
            }
        }

        // =====================================================================
        //  MOUSE
        // =====================================================================
        // OnMove and OnClick for custom drawn buttons removed.

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
