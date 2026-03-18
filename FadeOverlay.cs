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

        private Label _lblTitle;
        private Label _lblSubtitle;
        private ProgressBar _progBar;
        private Label _lblPct;
        private Label _lblStatus;
        private PictureBox _pbIcon;

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

            _pbIcon = new PictureBox { Location = Dpi.Pt(10, 8), Size = Dpi.Size(32, 32), SizeMode = PictureBoxSizeMode.Zoom };
            Controls.Add(_pbIcon);

            int textX = 50;
            _lblTitle = new Label { Location = Dpi.Pt(textX, 8), Size = Dpi.Size(200, 18), Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = TXT };
            Controls.Add(_lblTitle);

            _lblSubtitle = new Label { Location = Dpi.Pt(textX, 26), Size = Dpi.Size(200, 14), Font = new Font("Segoe UI", 8f), ForeColor = TXT2 };
            Controls.Add(_lblSubtitle);

            _progBar = new ProgressBar { Location = Dpi.Pt(textX, 46), Size = Dpi.Size(238, 10), Minimum = 0, Maximum = 100 };
            Controls.Add(_progBar);

            _lblPct = new Label { Location = Dpi.Pt(textX, 60), Size = Dpi.Size(100, 14), Font = new Font("Segoe UI", 8f, FontStyle.Bold), ForeColor = GREEN };
            Controls.Add(_lblPct);

            _lblStatus = new Label { Location = Dpi.Pt(188, 60), Size = Dpi.Size(100, 14), Font = new Font("Segoe UI", 8f), ForeColor = TXT2, TextAlign = ContentAlignment.TopRight };
            Controls.Add(_lblStatus);

            try { using (var bmp = new Bitmap(32, 32)) using (var g = Graphics.FromImage(bmp)) { Mascot.DrawMascot(g, 0, 0, 32); _pbIcon.Image = (Bitmap)bmp.Clone(); } } catch { }

            _fadeInTimer = new Timer { Interval = 30 };
            _fadeInTimer.Tick += (s, e) => {
                Opacity = Math.Min(Opacity + 0.06, 0.96);
                if (Opacity >= 0.95) _fadeInTimer.Stop();
            };

            _fadeOutTimer = new Timer { Interval = 30 };
            _fadeOutTimer.Tick += (s, e) => {
                Opacity -= 0.06;
                if (Opacity <= 0.02) { _fadeOutTimer.Stop(); _isClosing = true; Close(); }
            };

            _tickTimer = new Timer { Interval = 30 };
            _tickTimer.Tick += (s, e) => { if (!_isClosing) Invalidate(); };
        }

        // ApplyRoundedRegion removed

        public void SetFadeOut(bool fadeOut) { 
            _isFadeOut = fadeOut;
            _lblTitle.Text = _isFadeOut ? "Going AFK..." : "Restoring Volume...";
            _lblSubtitle.Text = _isFadeOut ? "Muting Soon" : "Welcome Back";
            _lblStatus.Text = _isFadeOut ? "Fading Out" : "Fading In";
            _lblPct.ForeColor = _isFadeOut ? AMBER : GREEN;
        }

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
            Action updateUI = () => {
                _progBar.Value = Math.Max(0, Math.Min(100, (int)_current));
                _lblPct.Text = _isFadeOut ? (int)_current + "%" : (int)_current + "% / " + (int)_target + "%";
            };
            if (InvokeRequired) try { BeginInvoke(updateUI); } catch { }
            else updateUI();
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
            var fastFade = new Timer { Interval = 30 };
            fastFade.Tick += (s, e) => {
                try { Opacity -= 0.15; } catch { }
                if (Opacity <= 0.05) { fastFade.Stop(); fastFade.Dispose(); try { Close(); } catch { } }
            };
            fastFade.Start();
        }

        // OnPaint and MakeRoundBar removed

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
