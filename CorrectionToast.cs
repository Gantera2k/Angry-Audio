using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AngryAudio
{
    public class CorrectionToast : Form
    {
        public bool MicToggled { get; private set; }
        public bool SpkToggled { get; private set; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public void ShowNoFocus()
        {
            if (!IsHandleCreated) CreateHandle();
            Visible = true;
            ShowWindow(Handle, 4);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040);
            var _reTop = new Timer { Interval = 100 };
            _reTop.Tick += (s, e) => { _reTop.Stop(); _reTop.Dispose(); try { if (!IsDisposed) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040); } catch { } };
            _reTop.Start();
        }

        private string _message;
        private string _title, _body;
        private Timer _fadeInTimer, _holdTimer, _fadeOutTimer, _tickTimer;
        private bool _micEnf, _spkEnf;
        private bool _isClosing;
        private DateTime _createdAt;
        private const int TotalLifeMs = 3700;

        private Button _btnMic;
        private Button _btnSpk;

        static readonly Color BG = Color.FromArgb(20, 20, 20);
        static readonly Color BDR = Color.FromArgb(44, 44, 44);
        static readonly Color ACC = DarkTheme.Accent;
        static readonly Color AMBER = DarkTheme.Amber;
        static readonly Color TXT = Color.FromArgb(230, 230, 230);
        static readonly Color TXT2 = Color.FromArgb(140, 140, 140);
        static readonly Color BTN_BG = Color.FromArgb(34, 34, 34);
        static readonly Color BTN_HOVER = Color.FromArgb(48, 48, 48);
        static readonly Color BTN_BDR = Color.FromArgb(60, 60, 60);
        private const int CORNER = 10;

        public CorrectionToast(string message, bool micEnf, bool spkEnf)
        {
            _message = message; _micEnf = micEnf; _spkEnf = spkEnf;
            _createdAt = DateTime.UtcNow;

            // Split on em-dash for title/subtitle layout
            int dashIdx = message.IndexOf('\u2014');
            if (dashIdx > 0) {
                _title = message.Substring(0, dashIdx).Trim();
                _body = message.Substring(dashIdx + 1).Trim();
            } else {
                _title = message;
                _body = null;
            }

            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = BG;
            DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;

            bool hasButtons = _micEnf || _spkEnf;
            int baseH = hasButtons ? 120 : (_body != null ? 90 : 78);
            // Auto-size for long text
            using (var tmp = Graphics.FromHwnd(IntPtr.Zero))
            {
                int textW = Dpi.S(240);
                using (var fTitle = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                {
                    var tsz = tmp.MeasureString(_title, fTitle, textW);
                    if (tsz.Height > Dpi.S(20)) baseH = Math.Max(baseH, hasButtons ? 130 : (_body != null ? 108 : 96));
                }
                if (_body != null)
                {
                    using (var fBody = new Font("Segoe UI", 8f))
                    {
                        var bsz = tmp.MeasureString(_body, fBody, textW);
                        if (bsz.Height > Dpi.S(18)) baseH = Math.Max(baseH, hasButtons ? 130 : 108);
                    }
                }
            }
            if (_micEnf || _spkEnf) {
                int btnY = _body != null ? 68 : 48;
                if (_micEnf) {
                    _btnMic = new Button { Text = "Pause Mic", Location = Dpi.Pt(12, btnY), Size = Dpi.Size(125, 26), FlatStyle = FlatStyle.Flat, BackColor = BTN_BG, ForeColor = TXT, Font = new Font("Segoe UI", 8f) };
                    _btnMic.FlatAppearance.BorderColor = BTN_BDR;
                    _btnMic.Click += (s,e) => { MicToggled = true; _isClosing = true; Close(); };
                    Controls.Add(_btnMic);
                }
                if (_spkEnf) {
                    _btnSpk = new Button { Text = "Pause Speaker", Location = Dpi.Pt(_micEnf ? 145 : 12, _body != null ? 68 : 48), Size = Dpi.Size(125, 26), FlatStyle = FlatStyle.Flat, BackColor = BTN_BG, ForeColor = TXT, Font = new Font("Segoe UI", 8f) };
                    _btnSpk.FlatAppearance.BorderColor = BTN_BDR;
                    _btnSpk.Click += (s,e) => { SpkToggled = true; _isClosing = true; Close(); };
                    Controls.Add(_btnSpk);
                }
            }
            ClientSize = Dpi.Size(300, baseH); Opacity = 0;
            ApplyRoundedRegion();

            _fadeInTimer = new Timer { Interval = 16 };
            _fadeInTimer.Tick += (s, e) => {
                Opacity = Math.Min(Opacity + 0.08, 0.88);
                if (Opacity >= 0.87) { _fadeInTimer.Stop(); _holdTimer.Start(); }
            };

            _holdTimer = new Timer { Interval = 3500 };
            _holdTimer.Tick += (s, e) => { _holdTimer.Stop(); StartFadeOut(); };

            _fadeOutTimer = new Timer { Interval = 16 };
            _fadeOutTimer.Tick += (s, e) => {
                Opacity -= 0.03;
                if (Opacity <= 0.01) { _fadeOutTimer.Stop(); _isClosing = true; Close(); }
            };

            _tickTimer = new Timer { Interval = 16 };
            _tickTimer.Tick += (s, e) => { if (!_isClosing) Invalidate(); };

            MouseClick += OnMouseClick;
            ToastStack.Register(this);
            _fadeInTimer.Start();
            _tickTimer.Start();
        }

        void ApplyRoundedRegion() { DarkTheme.ApplyRoundedRegion(this, Dpi.S(CORNER)); }

        /// <summary>0=normal, 1=mic open (green), 2=mic closed (red). Overrides accent color and draws mic icon.</summary>
        public int MicStatusMode { get; set; }
        static readonly Color GREEN = Color.FromArgb(50, 205, 100);
        static readonly Color RED_STATUS = Color.FromArgb(220, 50, 50);

        private Color GetAccentColor()
        {
            if (MicStatusMode == 1) return GREEN;
            if (MicStatusMode == 2) return RED_STATUS;
            bool isAfk = _message != null && (_message.IndexOf("AFK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          _message.IndexOf("fading", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          _message.IndexOf("paused", StringComparison.OrdinalIgnoreCase) >= 0);
            return isAfk ? AMBER : ACC;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;
            int r = Dpi.S(CORNER);
            Color accent = GetAccentColor();

            // --- Subtle top-edge accent glow ---
            using (var glowBr = new LinearGradientBrush(new Rectangle(0, 0, w, Dpi.S(4)),
                Color.FromArgb(40, accent), Color.FromArgb(0, accent), 90f))
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

            // --- Rounded border with glow ---
            using (var path = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), r)) {
                // Specular glass shine (subtle top-half gradient)
                using (var shineBr = new LinearGradientBrush(new Rectangle(0, 0, w, h / 2 + 1), Color.FromArgb(12, 255, 255, 255), Color.Transparent, 90f)) {
                    g.SetClip(path);
                    g.FillRectangle(shineBr, 0, 0, w, h / 2 + 1);
                    g.ResetClip();
                }

                using (var glowPen = new Pen(Color.FromArgb(25, accent), 3f))
                    g.DrawPath(glowPen, path);
                using (var pen = new Pen(Color.FromArgb(110, accent), 1.2f))
                    g.DrawPath(pen, path);
            }

            // --- Accent stripe ---
            using (var stripeBr = new LinearGradientBrush(new Rectangle(0, Dpi.S(10), Dpi.S(3), Dpi.S(32)),
                accent, Color.FromArgb(0, accent), 90f))
            using (var stripe = new Pen(stripeBr, Dpi.S(2)))
                g.DrawLine(stripe, Dpi.S(2), Dpi.S(12), Dpi.S(2), Dpi.S(42));

            // --- Title ---
            int textX = Dpi.S(16);
            using (var fTitle = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            {
                var titleRect = new Rectangle(textX, Dpi.S(10), w - textX - Dpi.S(10), Dpi.S(22));
                TextRenderer.DrawText(g, _title, fTitle, titleRect, TXT, TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            // --- Body with accent-colored parenthesized values ---
            if (_body != null)
            {
                int bodyY = Dpi.S(32);
                using (var fNorm = new Font("Segoe UI", 8f))
                using (var fBold = new Font("Segoe UI", 8f, FontStyle.Bold))
                {
                    int x = textX;
                    string txt = _body;
                    int idx = 0;
                    var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                    while (idx < txt.Length)
                    {
                        int open = txt.IndexOf('(', idx);
                        if (open < 0) {
                            TextRenderer.DrawText(g, txt.Substring(idx), fNorm, new Point(x, bodyY), TXT2, flags);
                            break;
                        }
                        if (open > idx) {
                            string before = txt.Substring(idx, open - idx);
                            var sz = TextRenderer.MeasureText(g, before, fNorm, new Size(999, 30), flags);
                            TextRenderer.DrawText(g, before, fNorm, new Point(x, bodyY), TXT2, flags);
                            x += sz.Width;
                        }
                        int close = txt.IndexOf(')', open);
                        if (close < 0) {
                            TextRenderer.DrawText(g, txt.Substring(open), fNorm, new Point(x, bodyY), TXT2, flags);
                            break;
                        }
                        string paren = txt.Substring(open, close - open + 1);
                        var psz = TextRenderer.MeasureText(g, paren, fBold, new Size(999, 30), flags);
                        TextRenderer.DrawText(g, paren, fBold, new Point(x, bodyY), accent, flags);
                        x += psz.Width;
                        idx = close + 1;
                    }
                }
            }

            // --- Premium progress bar ---
            double elapsed = (DateTime.UtcNow - _createdAt).TotalMilliseconds;
            double pct = Math.Max(0, 1.0 - elapsed / TotalLifeMs);
            int barY = h - Dpi.S(16);
            int barH = Dpi.S(3);
            int barMaxW = w - Dpi.S(24);
            int barX = Dpi.S(12);

            // Track (rounded)
            using (var trackPath = RoundedBar(barX, barY, barMaxW, barH))
            using (var trackBr = new SolidBrush(Color.FromArgb(32, 32, 32)))
                g.FillPath(trackBr, trackPath);
            // Active fill (gradient + rounded)
            int barW = Math.Max(barH, (int)(barMaxW * pct));
            if (barW > barH) {
                using (var barPath = RoundedBar(barX, barY, barW, barH)) {
                    // Glow behind bar
                    using (var glwBr = new SolidBrush(Color.FromArgb(20, accent)))
                        g.FillPath(glwBr, RoundedBar(barX, barY - 1, barW, barH + 2));
                    // Gradient fill
                    using (var barGrad = new LinearGradientBrush(new Rectangle(barX, barY, barW, barH),
                        accent, Color.FromArgb(accent.R * 3 / 4, accent.G * 3 / 4, accent.B * 3 / 4), 0f))
                        g.FillPath(barGrad, barPath);
                    // Leading bright dot
                    int dotR = barH + Dpi.S(2);
                    int dotX2 = barX + barW - dotR / 2;
                    int dotY2 = barY - Dpi.S(1);
                    using (var dotBr = new SolidBrush(Color.FromArgb(180, accent)))
                        g.FillEllipse(dotBr, dotX2, dotY2, dotR, dotR);
                }
            }

            // Countdown text
            int secs = (int)Math.Ceiling(Math.Max(0, TotalLifeMs - elapsed) / 1000.0);
            string timerTxt = secs + "s \u2013 Click dismiss";
            using (var fTimer = new Font("Segoe UI", 6.8f))
            {
                var timerSz = TextRenderer.MeasureText(timerTxt, fTimer);
                int tx = (w - timerSz.Width) / 2;
                TextRenderer.DrawText(g, timerTxt, fTimer, new Point(tx, barY - Dpi.S(13)), Color.FromArgb(80, 80, 80), TextFormatFlags.NoPrefix);
            }
        }

        internal static GraphicsPath RoundedBar(int x, int y, int w, int h) {
            var p = new GraphicsPath();
            p.AddArc(x, y, h, h, 90, 180);
            p.AddArc(x + w - h, y, h, h, 270, 180);
            p.CloseFigure();
            return p;
        }

        public void Dismiss()
        {
            if (_isClosing) return;
            _isClosing = true;
            _holdTimer.Stop(); _fadeInTimer.Stop(); _fadeOutTimer.Stop();
            var fastFade = new Timer { Interval = 30 };
            fastFade.Tick += (s, e) => {
                try { Opacity -= 0.15; } catch { }
                if (Opacity <= 0.05) { fastFade.Stop(); fastFade.Dispose(); try { Close(); } catch { } }
            };
            fastFade.Start();
        }

        private void StartFadeOut()
        {
            if (_isClosing) return;
            _fadeOutTimer.Start();
        }

        void OnMouseClick(object sender, MouseEventArgs e) {
            if (_isClosing) return;
            _isClosing = true; Close();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00000008 | 0x08000000 | 0x00000080; return cp; } }
        protected override void Dispose(bool d)
        {
            if (d) { if (_fadeInTimer != null) _fadeInTimer.Dispose(); if (_holdTimer != null) _holdTimer.Dispose(); if (_fadeOutTimer != null) _fadeOutTimer.Dispose(); if (_tickTimer != null) _tickTimer.Dispose(); }
            base.Dispose(d);
        }
    }

    /// <summary>
    /// Info toast with mascot. Timer bar + countdown. Stacks via ToastStack.
    /// </summary>
    public class InfoToast : Form
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private string _title;
        private string _body;
        private string _btn1Text, _btn2Text;
        public event EventHandler Btn1Clicked, Btn2Clicked;
        private Button _btn1;
        private Button _btn2;
        private Timer _fadeInTimer, _holdTimer, _fadeOutTimer, _tickTimer;
        private bool _isClosing;
        private DateTime _createdAt;
        private const int TotalLifeMs = 4200;
        private const int CORNER = 10;

        static readonly Color BG = Color.FromArgb(20, 20, 20);
        static readonly Color ACC = DarkTheme.Accent;
        static readonly Color TXT = Color.FromArgb(230, 230, 230);
        static readonly Color TXT2 = Color.FromArgb(140, 140, 140);

        public InfoToast(string title, string body, string btn1Text = null, string btn2Text = null)
        {
            _title = title; _body = body; _btn1Text = btn1Text; _btn2Text = btn2Text;
            _createdAt = DateTime.UtcNow;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = BG;
            DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;

            bool hasButtons = btn1Text != null || btn2Text != null;
            int baseH = hasButtons ? 120 : 90;
            using (var tmp = Graphics.FromHwnd(IntPtr.Zero))
            {
                int textW = Dpi.S(240);
                using (var fTitle = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                {
                    var tsz = tmp.MeasureString(title, fTitle, textW);
                    if (tsz.Height > Dpi.S(20)) baseH = Math.Max(baseH, hasButtons ? 130 : 108);
                }
                using (var fBody = new Font("Segoe UI", 8f))
                {
                    var bsz = tmp.MeasureString(body, fBody, textW);
                    if (bsz.Height > Dpi.S(18)) baseH = Math.Max(baseH, hasButtons ? 130 : 108);
                }
            }
            ClientSize = Dpi.Size(300, baseH); Opacity = 0;
            DarkTheme.ApplyRoundedRegion(this, Dpi.S(CORNER));

            if (_btn1Text != null || _btn2Text != null) {
                int btnY = 55;
                if (_btn1Text != null) {
                    _btn1 = new Button { Text = _btn1Text, Location = Dpi.Pt(12, btnY), Size = Dpi.Size(128, 26), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(34, 34, 34), ForeColor = ACC, Font = new Font("Segoe UI", 8f) };
                    _btn1.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
                    _btn1.Click += (s,e) => { _isClosing = true; if (Btn1Clicked != null) Btn1Clicked(this, EventArgs.Empty); Close(); };
                    Controls.Add(_btn1);
                }
                if (_btn2Text != null) {
                    _btn2 = new Button { Text = _btn2Text, Location = Dpi.Pt(_btn1Text != null ? 148 : 12, btnY), Size = Dpi.Size(128, 26), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(34, 34, 34), ForeColor = Color.FromArgb(160, 160, 160), Font = new Font("Segoe UI", 8f) };
                    _btn2.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
                    _btn2.Click += (s,e) => { _isClosing = true; if (Btn2Clicked != null) Btn2Clicked(this, EventArgs.Empty); Close(); };
                    Controls.Add(_btn2);
                }
            }

            _fadeInTimer = new Timer { Interval = 16 };
            _fadeInTimer.Tick += (s, e) => {
                Opacity = Math.Min(Opacity + 0.08, 0.96);
                if (Opacity >= 0.95) { _fadeInTimer.Stop(); _holdTimer.Start(); }
            };

            _holdTimer = new Timer { Interval = 4000 };
            _holdTimer.Tick += (s, e) => { _holdTimer.Stop(); _fadeOutTimer.Start(); };

            _fadeOutTimer = new Timer { Interval = 16 };
            _fadeOutTimer.Tick += (s, e) => {
                Opacity -= 0.03;
                if (Opacity <= 0.01) { _fadeOutTimer.Stop(); _isClosing = true; Close(); }
            };

            _tickTimer = new Timer { Interval = 16 };
            _tickTimer.Tick += (s, e) => { if (!_isClosing) Invalidate(); };

            MouseClick += (s, e) => { if (!_isClosing) { _isClosing = true; Close(); } };

            ToastStack.Register(this);
            _fadeInTimer.Start();
            _tickTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;
            int r = Dpi.S(CORNER);

            // Top-edge accent glow
            using (var glowBr = new LinearGradientBrush(new Rectangle(0, 0, w, Dpi.S(4)),
                Color.FromArgb(40, ACC), Color.FromArgb(0, ACC), 90f))
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

            // Rounded border with glow
            using (var path = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), r)) {
                using (var glowPen = new Pen(Color.FromArgb(25, ACC), 3f))
                    g.DrawPath(glowPen, path);
                using (var pen = new Pen(Color.FromArgb(110, ACC), 1.2f))
                    g.DrawPath(pen, path);
            }

            // Accent stripe (gradient)
            using (var stripeBr = new LinearGradientBrush(new Rectangle(0, Dpi.S(10), Dpi.S(3), Dpi.S(32)),
                ACC, Color.FromArgb(0, ACC), 90f))
            using (var stripe = new Pen(stripeBr, Dpi.S(2)))
                g.DrawLine(stripe, Dpi.S(2), Dpi.S(12), Dpi.S(2), Dpi.S(42));

            // Title
            int textX = Dpi.S(16);
            using (var fTitle = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, _title, fTitle, new Rectangle(textX, Dpi.S(10), w - textX - Dpi.S(10), Dpi.S(22)), TXT, TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            // Body
            using (var fBody = new Font("Segoe UI", 8f))
                TextRenderer.DrawText(g, _body, fBody, new Rectangle(textX, Dpi.S(32), w - textX - Dpi.S(10), Dpi.S(20)), TXT2, TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.WordEllipsis);


            // Premium progress bar
            double elapsed = (DateTime.UtcNow - _createdAt).TotalMilliseconds;
            double pct = Math.Max(0, 1.0 - elapsed / TotalLifeMs);
            int barY = h - Dpi.S(16);
            int barH = Dpi.S(3);
            int barMaxW = w - Dpi.S(24);
            int barX = Dpi.S(12);
            using (var trackPath = CorrectionToast.RoundedBar(barX, barY, barMaxW, barH))
            using (var trackBr = new SolidBrush(Color.FromArgb(32, 32, 32)))
                g.FillPath(trackBr, trackPath);
            int barW = Math.Max(barH, (int)(barMaxW * pct));
            if (barW > barH) {
                using (var barPath = CorrectionToast.RoundedBar(barX, barY, barW, barH)) {
                    using (var glwBr = new SolidBrush(Color.FromArgb(20, ACC)))
                        g.FillPath(glwBr, CorrectionToast.RoundedBar(barX, barY - 1, barW, barH + 2));
                    using (var barGrad = new LinearGradientBrush(new Rectangle(barX, barY, barW, barH),
                        ACC, Color.FromArgb(ACC.R * 3 / 4, ACC.G * 3 / 4, ACC.B * 3 / 4), 0f))
                        g.FillPath(barGrad, barPath);
                    int dotR = barH + Dpi.S(2);
                    using (var dotBr = new SolidBrush(Color.FromArgb(180, ACC)))
                        g.FillEllipse(dotBr, barX + barW - dotR / 2, barY - Dpi.S(1), dotR, dotR);
                }
            }
            int secs = (int)Math.Ceiling(Math.Max(0, TotalLifeMs - elapsed) / 1000.0);
            using (var fTimer = new Font("Segoe UI", 6.8f))
            {
                string timerTxt = secs + "s \u2013 Click dismiss";
                var tsz = TextRenderer.MeasureText(timerTxt, fTimer);
                TextRenderer.DrawText(g, timerTxt, fTimer, new Point((w - tsz.Width) / 2, barY - Dpi.S(13)), Color.FromArgb(80, 80, 80), TextFormatFlags.NoPrefix);
            }
        }

        public void Dismiss()
        {
            if (_isClosing) return;
            _isClosing = true;
            _holdTimer.Stop(); _fadeOutTimer.Stop(); _fadeInTimer.Stop(); _tickTimer.Stop();
            var fastFade = new Timer { Interval = 30 };
            fastFade.Tick += (s, e) => {
                try { Opacity -= 0.15; } catch { }
                if (Opacity <= 0.05) { fastFade.Stop(); fastFade.Dispose(); try { Close(); } catch { } }
            };
            fastFade.Start();
        }

        public void ShowNoFocus()
        {
            if (!IsHandleCreated) CreateHandle();
            Visible = true;
            ShowWindow(Handle, 4);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040);
            var _reTop = new Timer { Interval = 100 };
            _reTop.Tick += (s, e) => { _reTop.Stop(); _reTop.Dispose(); try { if (!IsDisposed) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040); } catch { } };
            _reTop.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00000008 | 0x08000000 | 0x00000080; return cp; } }
        protected override void Dispose(bool d)
        {
            if (d) { if (_fadeInTimer != null) _fadeInTimer.Dispose(); if (_holdTimer != null) _holdTimer.Dispose(); if (_fadeOutTimer != null) _fadeOutTimer.Dispose(); if (_tickTimer != null) _tickTimer.Dispose(); }
            base.Dispose(d);
        }
    }

    /// <summary>
    /// Scary red warning toast when ALL mic protections are disabled.
    /// </summary>
    public class MicWarningToast : Form
    {
        public bool OpenSettings { get; private set; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private Timer _fadeInTimer, _holdTimer, _fadeOutTimer, _tickTimer;
        private bool _isClosing;
        private DateTime _createdAt;
        private const int TotalLifeMs = 10200;
        private const int CORNER = 10;

        private Button _btnSettings;
        private PictureBox _pbIcon;

        static readonly Color BG = Color.FromArgb(18, 12, 12);
        static readonly Color RED = Color.FromArgb(220, 55, 55);
        static readonly Color RED_BRIGHT = Color.FromArgb(240, 70, 70);
        static readonly Color TXT = Color.FromArgb(235, 235, 235);
        static readonly Color BTN_BG = Color.FromArgb(140, 35, 35);

        public MicWarningToast()
        {
            _createdAt = DateTime.UtcNow;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = BG;
            DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
            ClientSize = Dpi.Size(300, 118); Opacity = 0;
            DarkTheme.ApplyRoundedRegion(this, Dpi.S(CORNER));

            _pbIcon = new PictureBox { Location = Dpi.Pt(14, 12), Size = Dpi.Size(32, 32), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            Controls.Add(_pbIcon);

            _btnSettings = new Button { Location = Dpi.Pt(14, 54), Size = Dpi.Size(272, 26), FlatStyle = FlatStyle.Flat, BackColor = BTN_BG, ForeColor = TXT, Font = new Font("Segoe UI", 7.8f, FontStyle.Bold), Text = "\u2699  Open Microphone Settings" };
            _btnSettings.FlatAppearance.BorderColor = RED;
            _btnSettings.Click += (s, e) => { OpenSettings = true; _isClosing = true; Close(); };
            Controls.Add(_btnSettings);
            
            try { int ms = Dpi.S(32); using (var bmp = new Bitmap(ms, ms)) using (var g = Graphics.FromImage(bmp)) { g.SmoothingMode = SmoothingMode.AntiAlias; Mascot.DrawMascot(g, 0, 0, ms); _pbIcon.Image = (Bitmap)bmp.Clone(); } } catch { }

            _fadeInTimer = new Timer { Interval = 16 };
            _fadeInTimer.Tick += (s, e) => {
                Opacity = Math.Min(Opacity + 0.05, 0.80);
                if (Opacity >= 0.79) { _fadeInTimer.Stop(); _holdTimer.Start(); }
            };

            _holdTimer = new Timer { Interval = 10000 };
            _holdTimer.Tick += (s, e) => { _holdTimer.Stop(); StartFadeOut(); };

            _fadeOutTimer = new Timer { Interval = 16 };
            _fadeOutTimer.Tick += (s, e) => {
                Opacity -= 0.025;
                if (Opacity <= 0.01) { _fadeOutTimer.Stop(); _isClosing = true; Close(); }
            };

            _tickTimer = new Timer { Interval = 16 };
            _tickTimer.Tick += (s, e) => { if (!_isClosing) Invalidate(); };

            MouseClick += (s, e) => { if (!_isClosing) { _isClosing = true; Close(); } };

            ToastStack.Register(this);
            _fadeInTimer.Start();
            _tickTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;
            int r = Dpi.S(CORNER);

            // Top-edge RED glow
            using (var glowBr = new LinearGradientBrush(new Rectangle(0, 0, w, Dpi.S(4)),
                Color.FromArgb(50, RED), Color.FromArgb(0, RED), 90f))
                g.FillRectangle(glowBr, 0, 0, w, Dpi.S(4));

            // Red rounded border with glow
            using (var path = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), r)) {
                using (var glowPen = new Pen(Color.FromArgb(30, RED), 3f))
                    g.DrawPath(glowPen, path);
                using (var pen = new Pen(Color.FromArgb(130, RED), 1.2f))
                    g.DrawPath(pen, path);
            }

            // Red accent stripe (gradient)
            using (var stripeBr = new LinearGradientBrush(new Rectangle(0, Dpi.S(10), Dpi.S(3), Dpi.S(32)),
                RED, Color.FromArgb(0, RED), 90f))
            using (var stripe = new Pen(stripeBr, Dpi.S(2)))
                g.DrawLine(stripe, Dpi.S(2), Dpi.S(12), Dpi.S(2), Dpi.S(42));

            // Title
            int textX = Dpi.S(54);
            using (var fTitle = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, "\u26A0  Microphone Unprotected", fTitle, new Rectangle(textX, Dpi.S(10), w - textX - Dpi.S(8), Dpi.S(22)), RED_BRIGHT, TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            // Subtitle
            using (var fSub = new Font("Segoe UI", 7.5f))
                TextRenderer.DrawText(g, "No Microphone Protections Active \u2014 Mic Is Open", fSub, new Rectangle(textX, Dpi.S(30), w - textX - Dpi.S(8), Dpi.S(18)), Color.FromArgb(145, 145, 145), TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            // Premium red progress bar
            double elapsed = (DateTime.UtcNow - _createdAt).TotalMilliseconds;
            double pct = Math.Max(0, 1.0 - elapsed / TotalLifeMs);
            int barY = h - Dpi.S(16);
            int barH = Dpi.S(3);
            int barMaxW = w - Dpi.S(24);
            int barX = Dpi.S(12);
            using (var trackPath = CorrectionToast.RoundedBar(barX, barY, barMaxW, barH))
            using (var trackBr = new SolidBrush(Color.FromArgb(35, 20, 20)))
                g.FillPath(trackBr, trackPath);
            int barW = Math.Max(barH, (int)(barMaxW * pct));
            if (barW > barH) {
                using (var barPath = CorrectionToast.RoundedBar(barX, barY, barW, barH)) {
                    using (var glwBr = new SolidBrush(Color.FromArgb(25, RED)))
                        g.FillPath(glwBr, CorrectionToast.RoundedBar(barX, barY - 1, barW, barH + 2));
                    using (var barGrad = new LinearGradientBrush(new Rectangle(barX, barY, barW, barH),
                        RED, Color.FromArgb(RED.R * 3 / 4, RED.G * 3 / 4, RED.B * 3 / 4), 0f))
                        g.FillPath(barGrad, barPath);
                    int dotR = barH + Dpi.S(2);
                    using (var dotBr = new SolidBrush(Color.FromArgb(180, RED)))
                        g.FillEllipse(dotBr, barX + barW - dotR / 2, barY - Dpi.S(1), dotR, dotR);
                }
            }
            int secs = (int)Math.Ceiling(Math.Max(0, TotalLifeMs - elapsed) / 1000.0);
            using (var fTimer = new Font("Segoe UI", 6.8f))
            {
                string timerTxt = secs + "s \u2013 Click dismiss";
                var tsz = TextRenderer.MeasureText(timerTxt, fTimer);
                TextRenderer.DrawText(g, timerTxt, fTimer, new Point((w - tsz.Width) / 2, barY - Dpi.S(13)), Color.FromArgb(80, 80, 80), TextFormatFlags.NoPrefix);
            }
        }


        void StartFadeOut() { if (!_isClosing) _fadeOutTimer.Start(); }

        public void ShowNoFocus()
        {
            if (!IsHandleCreated) CreateHandle();
            Visible = true;
            ShowWindow(Handle, 4);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040);
            var _reTop = new Timer { Interval = 100 };
            _reTop.Tick += (s, e) => { _reTop.Stop(); _reTop.Dispose(); try { if (!IsDisposed) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040); } catch { } };
            _reTop.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00000008 | 0x08000000 | 0x00000080; return cp; } }
        protected override void Dispose(bool d)
        {
            if (d) { if (_fadeInTimer != null) _fadeInTimer.Dispose(); if (_holdTimer != null) _holdTimer.Dispose(); if (_fadeOutTimer != null) _fadeOutTimer.Dispose(); if (_tickTimer != null) _tickTimer.Dispose(); }
            base.Dispose(d);
        }
    }
}
