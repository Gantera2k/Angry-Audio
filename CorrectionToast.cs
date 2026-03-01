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
            ShowWindow(Handle, 4);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040);
        }

        private string _message;
        private Timer _fadeInTimer, _holdTimer, _fadeOutTimer, _tickTimer;
        private bool _micEnf, _spkEnf;
        private Rectangle _micBtnRect, _spkBtnRect;
        private bool _hoverMic, _hoverSpk;
        private bool _isClosing;
        private DateTime _createdAt;
        private const int TotalLifeMs = 4300;

        static readonly Color BG = Color.FromArgb(20, 20, 20);
        static readonly Color BDR = Color.FromArgb(44, 44, 44);
        static readonly Color ACC = DarkTheme.Accent;
        static readonly Color AMBER = DarkTheme.Amber;
        static readonly Color TXT = Color.FromArgb(230, 230, 230);
        static readonly Color TXT2 = Color.FromArgb(100, 100, 100);
        static readonly Color BTN_BG = Color.FromArgb(34, 34, 34);
        static readonly Color BTN_HOVER = Color.FromArgb(48, 48, 48);
        static readonly Color BTN_BDR = Color.FromArgb(60, 60, 60);

        public CorrectionToast(string message, bool micEnf, bool spkEnf)
        {
            _message = message; _micEnf = micEnf; _spkEnf = spkEnf;
            _createdAt = DateTime.UtcNow;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = BG;
            DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;

            bool hasButtons = _micEnf || _spkEnf;
            int baseH = hasButtons ? 100 : 70;
            if (!hasButtons)
            {
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
                using (var tmp = Graphics.FromHwnd(IntPtr.Zero))
                {
                    int textW = Dpi.S(300) - Dpi.S(10) - Dpi.S(32) - Dpi.S(8) - Dpi.S(10);
                    var sz = tmp.MeasureString(_message, f, textW);
                    if (sz.Height > Dpi.S(20)) baseH = 88; // needs two lines
                }
            }
            ClientSize = Dpi.Size(300, baseH); Opacity = 0;

            Resize += (s, e2) => ApplyRoundedRegion();
            Load += (s, e2) => ApplyRoundedRegion();

            _fadeInTimer = new Timer { Interval = 16 };
            _fadeInTimer.Tick += (s, e) => {
                Opacity = Math.Min(Opacity + 0.06, 0.96);
                if (Opacity >= 0.95) { _fadeInTimer.Stop(); _holdTimer.Start(); }
            };

            _holdTimer = new Timer { Interval = 3500 };
            _holdTimer.Tick += (s, e) => { _holdTimer.Stop(); StartFadeOut(); };

            _fadeOutTimer = new Timer { Interval = 16 };
            _fadeOutTimer.Tick += (s, e) => {
                Opacity -= 0.04;
                if (Opacity <= 0.02) { _fadeOutTimer.Stop(); _isClosing = true; Close(); }
            };

            _tickTimer = new Timer { Interval = 50 };
            _tickTimer.Tick += (s, e) => { if (!_isClosing) Invalidate(); };

            MouseClick += OnMouseClick;
            MouseMove += OnMouseMove;

            // Register with toast stack for positioning
            ToastStack.Register(this);

            _fadeInTimer.Start();
            _tickTimer.Start();
        }

        void ApplyRoundedRegion() { DarkTheme.ApplyRoundedRegion(this, Dpi.S(10)); }

        public void Dismiss()
        {
            if (_isClosing) return;
            _isClosing = true;
            _holdTimer.Stop(); _fadeInTimer.Stop(); _fadeOutTimer.Stop();
            // Fast fade — premium feel, no zombies
            var fastFade = new Timer { Interval = 16 };
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

        private Color GetAccentColor()
        {
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

            var rr = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), Dpi.S(10));
            using (var b = new SolidBrush(BG)) g.FillPath(b, rr);
            DarkTheme.PaintStars(g, w, h, 77);
            using (var p = new Pen(BDR)) g.DrawPath(p, rr);
            rr.Dispose();

            Color accentColor = GetAccentColor();

            using (var b = new SolidBrush(accentColor))
                g.FillRectangle(b, 0, Dpi.S(6), Dpi.S(3), h - Dpi.S(12));
            using (var p = new Pen(Color.FromArgb(30, accentColor.R, accentColor.G, accentColor.B)))
                g.DrawLine(p, Dpi.S(10), 0, w - Dpi.S(10), 0);

            int mascotSz = Dpi.S(32);
            int mascotX = Dpi.S(10);
            Mascot.DrawMascot(g, mascotX, Dpi.S(8), mascotSz);

            int textX = mascotX + mascotSz + Dpi.S(8);
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var b = new SolidBrush(TXT))
            {
                var rect = new RectangleF(textX, Dpi.S(8), w - textX - Dpi.S(10), Dpi.S(44));
                g.DrawString(_message, f, b, rect);
            }

            if (_micEnf || _spkEnf)
            {
                int btnMargin = Dpi.S(10);
                int btnY = Dpi.S(48);
                int btnH = Dpi.S(22);
                int btnAreaW = w - btnMargin * 2;
                int gap = Dpi.S(6);
                int btnCount = (_micEnf ? 1 : 0) + (_spkEnf ? 1 : 0);
                int btnW = btnCount == 2 ? (btnAreaW - gap) / 2 : btnAreaW;
                int btnX = btnMargin;

                if (_micEnf) {
                    _micBtnRect = new Rectangle(btnX, btnY, btnW, btnH);
                    DrawActionButton(g, _micBtnRect, "Pause Mic Lock", _hoverMic);
                    btnX += btnW + gap;
                }
                if (_spkEnf) {
                    _spkBtnRect = new Rectangle(btnX, btnY, btnCount == 2 ? btnW : btnAreaW, btnH);
                    DrawActionButton(g, _spkBtnRect, "Pause Speaker Lock", _hoverSpk);
                }
            }

            // Timer bar + countdown
            double elapsed = (DateTime.UtcNow - _createdAt).TotalMilliseconds;
            double remaining = Math.Max(0, TotalLifeMs - elapsed);
            float progress = (float)Math.Max(0, Math.Min(1, remaining / TotalLifeMs));
            int sec = (int)Math.Ceiling(remaining / 1000.0);

            int barY = h - Dpi.S(18);
            int barH = Dpi.S(3);
            using (var b = new SolidBrush(Color.FromArgb(30, 30, 30)))
                g.FillRectangle(b, 0, barY, w, barH);
            using (var b = new SolidBrush(accentColor))
                g.FillRectangle(b, 0, barY, (int)(w * progress), barH);

            int txtY = barY + Dpi.S(4);
            using (var fBold = new Font("Segoe UI", 6.5f, FontStyle.Bold))
            using (var fReg = new Font("Segoe UI", 6.5f))
            {
                string secTxt = sec + "s";
                string rest = " \u00B7 Click to Dismiss";
                var secSz = g.MeasureString(secTxt, fBold);
                var restSz = g.MeasureString(rest, fReg);
                float totalTxtW = secSz.Width + restSz.Width;
                float tx = (w - totalTxtW) / 2f;
                using (var b = new SolidBrush(accentColor))
                    g.DrawString(secTxt, fBold, b, tx, txtY);
                using (var b = new SolidBrush(Color.FromArgb(60, 60, 60)))
                    g.DrawString(rest, fReg, b, tx + secSz.Width - Dpi.S(4), txtY);
            }
        }

        void DrawActionButton(Graphics g, Rectangle r, string text, bool hover)
        {
            Color bg = hover ? BTN_HOVER : BTN_BG;
            Color bdr = hover ? Color.FromArgb(80, 80, 80) : BTN_BDR;
            Color fg = hover ? TXT : TXT2;
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

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            bool wasMic = _hoverMic, wasSpk = _hoverSpk;
            _hoverMic = _micEnf && _micBtnRect.Contains(e.Location);
            _hoverSpk = _spkEnf && _spkBtnRect.Contains(e.Location);
            // Keep default cursor — hover effect handles visual feedback
            if (wasMic != _hoverMic || wasSpk != _hoverSpk) Invalidate();
        }

        void OnMouseClick(object sender, MouseEventArgs e) {
            if (_isClosing) return;
            if (_micEnf && _micBtnRect.Contains(e.Location)) { MicToggled = true; _isClosing = true; Close(); return; }
            if (_spkEnf && _spkBtnRect.Contains(e.Location)) { SpkToggled = true; _isClosing = true; Close(); return; }
            _isClosing = true; Close();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00000008 | 0x08000000 | 0x00000080; return cp; } }
        protected override void Dispose(bool d)
        {
            if (d) { _fadeInTimer?.Dispose(); _holdTimer?.Dispose(); _fadeOutTimer?.Dispose(); _tickTimer?.Dispose(); }
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
        private Rectangle _btn1Rect, _btn2Rect;
        private bool _hoverBtn1, _hoverBtn2;
        private Timer _fadeInTimer, _holdTimer, _fadeOutTimer, _tickTimer;
        private bool _isClosing;
        private DateTime _createdAt;
        private const int TotalLifeMs = 4800;

        static readonly Color BG = Color.FromArgb(20, 20, 20);
        static readonly Color BDR = Color.FromArgb(44, 44, 44);
        static readonly Color ACC = DarkTheme.Accent;

        public InfoToast(string title, string body, string btn1Text = null, string btn2Text = null)
        {
            _title = title; _body = body; _btn1Text = btn1Text; _btn2Text = btn2Text;
            _createdAt = DateTime.UtcNow;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = BG;
            DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;

            // Measure BOTH title and body to auto-size height
            bool hasButtons = btn1Text != null || btn2Text != null;
            int baseH = hasButtons ? 106 : 80;
            using (var tmp = Graphics.FromHwnd(IntPtr.Zero))
            {
                int textW = Dpi.S(300) - Dpi.S(10) - Dpi.S(32) - Dpi.S(8) - Dpi.S(10);
                using (var fTitle = new Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold))
                {
                    var tsz = tmp.MeasureString(title, fTitle, textW);
                    if (tsz.Height > Dpi.S(18)) baseH = Math.Max(baseH, hasButtons ? 118 : 92);
                }
                using (var fBody = new Font("Segoe UI", 7.5f))
                {
                    var bsz = tmp.MeasureString(body, fBody, textW);
                    if (bsz.Height > Dpi.S(18)) baseH = Math.Max(baseH, hasButtons ? 118 : 92);
                }
            }
            ClientSize = Dpi.Size(300, baseH); Opacity = 0;

            Resize += (s, e2) => ApplyRoundedRegion();
            Load += (s, e2) => ApplyRoundedRegion();

            _fadeInTimer = new Timer { Interval = 16 };
            _fadeInTimer.Tick += (s, e) => {
                Opacity = Math.Min(Opacity + 0.07, 0.96);
                if (Opacity >= 0.95) { _fadeInTimer.Stop(); _holdTimer.Start(); }
            };

            _holdTimer = new Timer { Interval = 4000 };
            _holdTimer.Tick += (s, e) => { _holdTimer.Stop(); _fadeOutTimer.Start(); };

            _fadeOutTimer = new Timer { Interval = 16 };
            _fadeOutTimer.Tick += (s, e) => {
                Opacity -= 0.04;
                if (Opacity <= 0.02) { _fadeOutTimer.Stop(); _isClosing = true; Close(); }
            };

            _tickTimer = new Timer { Interval = 50 };
            _tickTimer.Tick += (s, e) => { if (!_isClosing) Invalidate(); };

            MouseClick += (s, e) => {
                if (_isClosing) return;
                if (_btn1Text != null && !_btn1Rect.IsEmpty && _btn1Rect.Contains(e.Location)) {
                    _isClosing = true;
                    if (Btn1Clicked != null) Btn1Clicked(this, EventArgs.Empty);
                    Close(); return;
                }
                if (_btn2Text != null && !_btn2Rect.IsEmpty && _btn2Rect.Contains(e.Location)) {
                    _isClosing = true;
                    if (Btn2Clicked != null) Btn2Clicked(this, EventArgs.Empty);
                    Close(); return;
                }
                _isClosing = true; Close();
            };
            MouseMove += (s, e) => {
                bool h1 = _btn1Text != null && !_btn1Rect.IsEmpty && _btn1Rect.Contains(e.Location);
                bool h2 = _btn2Text != null && !_btn2Rect.IsEmpty && _btn2Rect.Contains(e.Location);
                if (h1 != _hoverBtn1 || h2 != _hoverBtn2) { _hoverBtn1 = h1; _hoverBtn2 = h2; Invalidate(); }
            };
            MouseLeave += (s, e) => { if (_hoverBtn1 || _hoverBtn2) { _hoverBtn1 = _hoverBtn2 = false; Invalidate(); } };

            ToastStack.Register(this);

            _fadeInTimer.Start();
            _tickTimer.Start();
        }

        void ApplyRoundedRegion() { DarkTheme.ApplyRoundedRegion(this, Dpi.S(10)); }

        public void Dismiss()
        {
            if (_isClosing) return;
            _isClosing = true;
            _holdTimer.Stop(); _fadeOutTimer.Stop(); _fadeInTimer.Stop(); _tickTimer.Stop();
            // Fast fade — premium feel, no zombies
            var fastFade = new Timer { Interval = 16 };
            fastFade.Tick += (s, e) => {
                try { Opacity -= 0.15; } catch { }
                if (Opacity <= 0.05) { fastFade.Stop(); fastFade.Dispose(); try { Close(); } catch { } }
            };
            fastFade.Start();
        }

        public void ShowNoFocus()
        {
            if (!IsHandleCreated) CreateHandle();
            ShowWindow(Handle, 4);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;

            var rr = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), Dpi.S(10));
            using (var b = new SolidBrush(BG)) g.FillPath(b, rr);
            DarkTheme.PaintStars(g, w, h, 88);
            using (var p = new Pen(BDR)) g.DrawPath(p, rr);
            rr.Dispose();

            using (var b = new SolidBrush(ACC))
                g.FillRectangle(b, 0, Dpi.S(6), Dpi.S(3), h - Dpi.S(12));
            using (var p = new Pen(Color.FromArgb(20, ACC.R, ACC.G, ACC.B)))
                g.DrawLine(p, Dpi.S(10), 0, w - Dpi.S(10), 0);

            int mascotSz = Dpi.S(32);
            int mascotX = Dpi.S(10);
            Mascot.DrawMascot(g, mascotX, Dpi.S(8), mascotSz);

            int textX = mascotX + mascotSz + Dpi.S(8);
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var b = new SolidBrush(Color.FromArgb(230, 230, 230)))
            {
                var rect = new RectangleF(textX, Dpi.S(8), w - textX - Dpi.S(10), Dpi.S(24));
                g.DrawString(_title, f, b, rect, new StringFormat { Trimming = StringTrimming.EllipsisCharacter });
            }

            using (var f = new Font("Segoe UI", 7.5f))
            using (var b = new SolidBrush(Color.FromArgb(130, 130, 130)))
            {
                var rect = new RectangleF(textX, Dpi.S(28), w - textX - Dpi.S(10), Dpi.S(34));
                g.DrawString(_body, f, b, rect);
            }

            // Action buttons (optional, side by side)
            if (_btn1Text != null || _btn2Text != null)
            {
                int btnMargin = Dpi.S(10);
                int abY = Dpi.S(52);
                int abH = Dpi.S(22);
                int gap = Dpi.S(6);
                int totalW = w - btnMargin * 2;
                bool hasBoth = _btn1Text != null && _btn2Text != null;
                int btnW = hasBoth ? (totalW - gap) / 2 : totalW;
                int bx = btnMargin;

                if (_btn1Text != null) {
                    _btn1Rect = new Rectangle(bx, abY, btnW, abH);
                    DrawToastBtn(g, _btn1Rect, _btn1Text, _hoverBtn1, true);
                    bx += btnW + gap;
                }
                if (_btn2Text != null) {
                    _btn2Rect = new Rectangle(bx, abY, hasBoth ? btnW : totalW, abH);
                    DrawToastBtn(g, _btn2Rect, _btn2Text, _hoverBtn2, false);
                }
            }

            // Timer bar + countdown
            double elapsed = (DateTime.UtcNow - _createdAt).TotalMilliseconds;
            double remaining = Math.Max(0, TotalLifeMs - elapsed);
            float progress = (float)Math.Max(0, Math.Min(1, remaining / TotalLifeMs));
            int sec = (int)Math.Ceiling(remaining / 1000.0);

            int barY = h - Dpi.S(18);
            int barH = Dpi.S(3);
            using (var b = new SolidBrush(Color.FromArgb(30, 30, 30)))
                g.FillRectangle(b, 0, barY, w, barH);
            using (var b = new SolidBrush(ACC))
                g.FillRectangle(b, 0, barY, (int)(w * progress), barH);

            int txtY = barY + Dpi.S(4);
            using (var fBold = new Font("Segoe UI", 6.5f, FontStyle.Bold))
            using (var fReg = new Font("Segoe UI", 6.5f))
            {
                string secTxt = sec + "s";
                string rest = " \u00B7 Click to Dismiss";
                var secSz = g.MeasureString(secTxt, fBold);
                var restSz = g.MeasureString(rest, fReg);
                float totalTxtW = secSz.Width + restSz.Width;
                float tx = (w - totalTxtW) / 2f;
                using (var b = new SolidBrush(ACC))
                    g.DrawString(secTxt, fBold, b, tx, txtY);
                using (var b = new SolidBrush(Color.FromArgb(60, 60, 60)))
                    g.DrawString(rest, fReg, b, tx + secSz.Width - Dpi.S(4), txtY);
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00000008 | 0x08000000 | 0x00000080; return cp; } }
        void DrawToastBtn(Graphics g, Rectangle rect, string text, bool hover, bool primary)
        {
            Color btnBg = hover ? Color.FromArgb(48, 48, 48) : Color.FromArgb(34, 34, 34);
            Color btnBdr = hover ? Color.FromArgb(80, 80, 80) : Color.FromArgb(60, 60, 60);
            Color txtClr = primary ? ACC : Color.FromArgb(160, 160, 160);
            if (hover) txtClr = primary ? Color.FromArgb(140, 220, 255) : Color.FromArgb(210, 210, 210);
            using (var path = DarkTheme.RoundedRect(rect, Dpi.S(4)))
            {
                using (var b = new SolidBrush(btnBg)) g.FillPath(b, path);
                using (var p = new Pen(btnBdr)) g.DrawPath(p, path);
            }
            using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var b = new SolidBrush(txtClr))
            {
                var sz = g.MeasureString(text, f);
                g.DrawString(text, f, b, rect.X + (rect.Width - sz.Width) / 2, rect.Y + (rect.Height - sz.Height) / 2);
            }
        }

        protected override void Dispose(bool d)
        {
            if (d) { _fadeInTimer?.Dispose(); _holdTimer?.Dispose(); _fadeOutTimer?.Dispose(); _tickTimer?.Dispose(); }
            base.Dispose(d);
        }
    }

    /// <summary>
    /// Scary red warning toast when ALL mic protections are disabled.
    /// Designed to make users immediately want to turn something on.
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
        private Rectangle _btnRect;
        private bool _hoverBtn;
        private float _pulsePhase;
        private const int TotalLifeMs = 10800;

        static readonly Color BG = Color.FromArgb(18, 12, 12);
        static readonly Color RED = Color.FromArgb(220, 55, 55);
        static readonly Color RED_BRIGHT = Color.FromArgb(240, 70, 70);
        static readonly Color TXT = Color.FromArgb(235, 235, 235);
        static readonly Color BTN_BG = Color.FromArgb(140, 35, 35);
        static readonly Color BTN_HOVER = Color.FromArgb(180, 45, 45);

        public MicWarningToast()
        {
            _createdAt = DateTime.UtcNow;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = BG;
            DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
            ClientSize = Dpi.Size(300, 106); Opacity = 0;

            Resize += (s, e2) => ApplyRoundedRegion();
            Load += (s, e2) => ApplyRoundedRegion();

            _fadeInTimer = new Timer { Interval = 16 };
            _fadeInTimer.Tick += (s, e) => {
                Opacity = Math.Min(Opacity + 0.06, 0.96);
                if (Opacity >= 0.95) { _fadeInTimer.Stop(); _holdTimer.Start(); }
            };

            _holdTimer = new Timer { Interval = 10000 };
            _holdTimer.Tick += (s, e) => { _holdTimer.Stop(); StartFadeOut(); };

            _fadeOutTimer = new Timer { Interval = 16 };
            _fadeOutTimer.Tick += (s, e) => {
                Opacity -= 0.04;
                if (Opacity <= 0.02) { _fadeOutTimer.Stop(); _isClosing = true; Close(); }
            };

            _tickTimer = new Timer { Interval = 40 };
            _tickTimer.Tick += (s, e) => { if (!_isClosing) { _pulsePhase += 0.06f; Invalidate(); } };

            MouseClick += OnMouseClick;
            MouseMove += OnMouseMove;

            ToastStack.Register(this);
            _fadeInTimer.Start();
            _tickTimer.Start();
        }

        void ApplyRoundedRegion() { DarkTheme.ApplyRoundedRegion(this, Dpi.S(10)); }
        void StartFadeOut() { if (!_isClosing) _fadeOutTimer.Start(); }

        public void ShowNoFocus()
        {
            if (!IsHandleCreated) CreateHandle();
            ShowWindow(Handle, 4);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;

            // Background with rounded rect
            var rr = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), Dpi.S(10));
            using (var b = new SolidBrush(BG)) g.FillPath(b, rr);
            DarkTheme.PaintStars(g, w, h, 99);

            // Pulsing red vignette glow — breathes danger
            float pulse = (float)(0.4 + 0.25 * Math.Sin(_pulsePhase));
            int glowAlpha = (int)(pulse * 40);
            using (var glow = new System.Drawing.Drawing2D.PathGradientBrush(rr))
            {
                glow.CenterColor = Color.FromArgb(glowAlpha, RED.R, RED.G, RED.B);
                glow.SurroundColors = new[] { Color.FromArgb(0, RED.R, RED.G, RED.B) };
                glow.CenterPoint = new PointF(Dpi.S(30), h / 2f);
                g.FillPath(glow, rr);
            }

            // Border — red tinted, subtle inner glow
            int bdrPulse = (int)(30 + 20 * Math.Sin(_pulsePhase));
            using (var p = new Pen(Color.FromArgb(60 + bdrPulse, RED.R / 2, RED.G / 4, RED.B / 4)))
                g.DrawPath(p, rr);
            rr.Dispose();

            // Left accent bar — thick, rounded feel via gradient
            int barW = Dpi.S(3);
            using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Point(0, Dpi.S(8)), new Point(0, h - Dpi.S(8)),
                RED_BRIGHT, Color.FromArgb(160, 30, 30)))
                g.FillRectangle(b, 0, Dpi.S(8), barW, h - Dpi.S(16));

            // Mascot
            int mascotSz = Dpi.S(32);
            int mascotX = Dpi.S(12);
            int mascotY = Dpi.S(10);
            Mascot.DrawMascot(g, mascotX, mascotY, mascotSz);

            // Title: "⚠  Microphone Unprotected"
            int textX = mascotX + mascotSz + Dpi.S(8);
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var b = new SolidBrush(RED_BRIGHT))
            {
                var rect = new RectangleF(textX, Dpi.S(9), w - textX - Dpi.S(12), Dpi.S(18));
                g.DrawString("\u26A0  Microphone Unprotected", f, b, rect);
            }

            // Subtitle
            using (var f = new Font("Segoe UI", 7.5f))
            using (var b = new SolidBrush(Color.FromArgb(145, 145, 145)))
                g.DrawString("No Microphone Protections Active \u2014 Mic Is Open", f, b, textX, Dpi.S(28));

            // Button — prominent, full width, gradient fill
            int btnMargin = Dpi.S(12);
            int btnY = Dpi.S(50);
            int btnH = Dpi.S(24);
            int btnW = w - btnMargin * 2;
            _btnRect = new Rectangle(btnMargin, btnY, btnW, btnH);

            Color cTop = _hoverBtn ? BTN_HOVER : BTN_BG;
            Color cBot = _hoverBtn ? Color.FromArgb(140, 35, 35) : Color.FromArgb(100, 25, 25);
            using (var path = DarkTheme.RoundedRect(_btnRect, Dpi.S(5)))
            {
                using (var grad = new System.Drawing.Drawing2D.LinearGradientBrush(_btnRect, cTop, cBot, 90f))
                    g.FillPath(grad, path);
                using (var p = new Pen(Color.FromArgb(_hoverBtn ? 90 : 60, 255, 80, 80)))
                    g.DrawPath(p, path);
            }
            using (var f = new Font("Segoe UI", 7.8f, FontStyle.Bold))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using (var b = new SolidBrush(_hoverBtn ? TXT : Color.FromArgb(220, 220, 220)))
                    g.DrawString("\u2699  Open Microphone Settings", f, b, _btnRect, sf);
            }

            // Timer bar + countdown
            double elapsed = (DateTime.UtcNow - _createdAt).TotalMilliseconds;
            double remaining = Math.Max(0, TotalLifeMs - elapsed);
            float progress = (float)Math.Max(0, Math.Min(1, remaining / TotalLifeMs));
            int sec = (int)Math.Ceiling(remaining / 1000.0);

            int barY = h - Dpi.S(18);
            int barH = Dpi.S(3);
            using (var b = new SolidBrush(Color.FromArgb(25, 25, 25)))
                g.FillRectangle(b, 0, barY, w, barH);
            int fillW = (int)(w * progress);
            if (fillW > 0)
            {
                using (var grad = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, barY, Math.Max(1, fillW), barH), RED_BRIGHT, RED, 0f))
                    g.FillRectangle(grad, 0, barY, fillW, barH);
            }

            int txtY = barY + Dpi.S(4);
            using (var fBold = new Font("Segoe UI", 6.5f, FontStyle.Bold))
            using (var fReg = new Font("Segoe UI", 6.5f))
            {
                string secTxt = sec + "s";
                string rest = " \u00B7 Click to Dismiss";
                var secSz = g.MeasureString(secTxt, fBold);
                var restSz = g.MeasureString(rest, fReg);
                float totalTxtW = secSz.Width + restSz.Width;
                float tx = (w - totalTxtW) / 2f;
                using (var b = new SolidBrush(RED))
                    g.DrawString(secTxt, fBold, b, tx, txtY);
                using (var b = new SolidBrush(Color.FromArgb(55, 55, 55)))
                    g.DrawString(rest, fReg, b, tx + secSz.Width - Dpi.S(4), txtY);
            }
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            bool was = _hoverBtn;
            _hoverBtn = _btnRect.Contains(e.Location);
            // Keep default cursor — hover effect handles visual feedback
            if (was != _hoverBtn) Invalidate();
        }

        void OnMouseClick(object sender, MouseEventArgs e) {
            if (_isClosing) return;
            if (_btnRect.Contains(e.Location)) { OpenSettings = true; _isClosing = true; Close(); return; }
            _isClosing = true; Close();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00000008 | 0x08000000 | 0x00000080; return cp; } }
        protected override void Dispose(bool d)
        {
            if (d) { _fadeInTimer?.Dispose(); _holdTimer?.Dispose(); _fadeOutTimer?.Dispose(); _tickTimer?.Dispose(); }
            base.Dispose(d);
        }
    }
}
