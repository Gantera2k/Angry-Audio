using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AngryAudio
{
    /// <summary>
    /// Premium fade overlay popup — matches CorrectionToast visual style exactly.
    /// Shows volume fade progress (AFK fade-out or welcome-back fade-in).
    /// Runs on dedicated STA thread for cross-thread safety.
    /// </summary>
    public class FadeOverlay : Form
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private float _current, _target;
        private bool _isFadeOut;
        private Timer _fadeInTimer, _fadeOutTimer, _tickTimer;
        private bool _isClosing;

        static readonly Color BG = Color.FromArgb(20, 20, 20);
        static readonly Color BDR = Color.FromArgb(44, 44, 44);
        static readonly Color ACC = DarkTheme.Accent;
        static readonly Color AMBER = DarkTheme.Amber;
        static readonly Color TXT = Color.FromArgb(230, 230, 230);
        static readonly Color TXT2 = Color.FromArgb(130, 130, 130);
        static readonly Color GREEN = DarkTheme.Green;

        public FadeOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = BG;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            Opacity = 0;
            ClientSize = Dpi.Size(300, 88);

            Resize += (s, e2) => ApplyRoundedRegion();
            Load += (s, e2) => ApplyRoundedRegion();

            _fadeInTimer = new Timer { Interval = 16 };
            _fadeInTimer.Tick += (s, e) => {
                Opacity = Math.Min(Opacity + 0.06, 0.96);
                if (Opacity >= 0.95) _fadeInTimer.Stop();
            };

            _fadeOutTimer = new Timer { Interval = 16 };
            _fadeOutTimer.Tick += (s, e) => {
                Opacity -= 0.06;
                if (Opacity <= 0.02) { _fadeOutTimer.Stop(); _isClosing = true; Close(); }
            };

            _tickTimer = new Timer { Interval = 50 };
            _tickTimer.Tick += (s, e) => { if (!_isClosing) Invalidate(); };
        }

        void ApplyRoundedRegion() { DarkTheme.ApplyRoundedRegion(this, Dpi.S(10)); }

        public void SetFadeOut(bool fadeOut) { _isFadeOut = fadeOut; }

        public void ShowOverlay()
        {
            ToastStack.Register(this);
            if (!IsHandleCreated) CreateHandle();
            ShowWindow(Handle, 4);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040);
            var _reTop = new Timer { Interval = 100 };
            _reTop.Tick += (s, e) => { _reTop.Stop(); _reTop.Dispose(); try { if (!IsDisposed) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040); } catch { } };
            _reTop.Start();
            _fadeInTimer.Start();
            _tickTimer.Start();
        }

        public void UpdateProgress(float current, float target)
        {
            _current = current;
            _target = target;
            if (InvokeRequired)
                try { BeginInvoke((Action)Invalidate); } catch { }
            else
                Invalidate();
        }

        /// <summary>Fade out gracefully then close.</summary>
        public void FadeOutAndClose()
        {
            if (_isClosing) return;
            _fadeInTimer.Stop();
            _fadeOutTimer.Start();
        }

        /// <summary>Fast dismiss — matches CorrectionToast/InfoToast pattern.</summary>
        public void Dismiss()
        {
            if (_isClosing) return;
            _isClosing = true;
            _fadeInTimer.Stop(); _fadeOutTimer.Stop(); _tickTimer.Stop();
            var fastFade = new Timer { Interval = 16 };
            fastFade.Tick += (s, e) => {
                try { Opacity -= 0.15; } catch { }
                if (Opacity <= 0.05) { fastFade.Stop(); fastFade.Dispose(); try { Close(); } catch { } }
            };
            fastFade.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;

            // Background — rounded rect + stars (matching CorrectionToast exactly)
            var rr = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), Dpi.S(10));
            using (var b = new SolidBrush(BG)) g.FillPath(b, rr);
            DarkTheme.PaintStars(g, w, h, 88);
            using (var p = new Pen(BDR)) g.DrawPath(p, rr);
            rr.Dispose();

            Color accentColor = _isFadeOut ? AMBER : ACC;

            // Left accent bar
            using (var b = new SolidBrush(accentColor))
                g.FillRectangle(b, 0, Dpi.S(6), Dpi.S(3), h - Dpi.S(12));
            // Top accent line
            using (var p = new Pen(Color.FromArgb(30, accentColor.R, accentColor.G, accentColor.B)))
                g.DrawLine(p, Dpi.S(10), 0, w - Dpi.S(10), 0);

            // Mascot
            int mascotSz = Dpi.S(32);
            int mascotX = Dpi.S(10);
            Mascot.DrawMascot(g, mascotX, Dpi.S(8), mascotSz);

            // Title
            int textX = mascotX + mascotSz + Dpi.S(8);
            string title = _isFadeOut ? "Going AFK\u2026" : "Restoring Volume\u2026";
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var b = new SolidBrush(TXT))
                g.DrawString(title, f, b, textX, Dpi.S(8));

            // Subtitle
            string subtitle = _isFadeOut ? "Muting Soon" : "Welcome Back";
            using (var f = new Font("Segoe UI", 7f))
            using (var b = new SolidBrush(TXT2))
                g.DrawString(subtitle, f, b, textX, Dpi.S(23));

            // Volume progress bar — pill-shaped
            int barX = textX, barY = Dpi.S(42), barW = w - textX - Dpi.S(12), barH = Dpi.S(8);
            int barR = barH / 2;

            var trackPath = MakeRoundBar(barX, barY, barW, barH, barR);
            using (var b = new SolidBrush(Color.FromArgb(30, 30, 30))) g.FillPath(b, trackPath);
            trackPath.Dispose();

            float pct = _target > 0 ? _current / _target : (_isFadeOut ? 1f : 0f);
            int fillW = Math.Max(barH, (int)(barW * Math.Min(1f, pct)));
            var fillPath = MakeRoundBar(barX, barY, fillW, barH, barR);
            using (var b = new SolidBrush(accentColor)) g.FillPath(b, fillPath);
            fillPath.Dispose();

            // Volume percentage + status on same line with proper spacing
            int infoY = barY + barH + Dpi.S(6);
            Color pctColor = _isFadeOut ? AMBER : GREEN;
            string pctText = _isFadeOut ? (int)_current + "%" : (int)_current + "% / " + (int)_target + "%";
            using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var b = new SolidBrush(pctColor))
                g.DrawString(pctText, f, b, barX, infoY);

            // Status text at bottom-right
            string statusText = _isFadeOut ? "Fading Out" : "Fading In";
            using (var f = new Font("Segoe UI", 6.5f))
            using (var b = new SolidBrush(TXT2))
            {
                var sz = g.MeasureString(statusText, f);
                g.DrawString(statusText, f, b, w - sz.Width - Dpi.S(10), infoY);
            }
        }

        GraphicsPath MakeRoundBar(int x, int y, int w, int h, int r) {
            var p = new GraphicsPath();
            if (w < h) w = h;
            p.AddArc(x, y, h, h, 90, 180);
            p.AddArc(x + w - h, y, h, h, 270, 180);
            p.CloseFigure();
            return p;
        }

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
    }
}
