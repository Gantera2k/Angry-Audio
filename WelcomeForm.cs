using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AngryAudio
{
    public class PaddedNumericUpDown : NumericUpDown
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        public PaddedNumericUpDown() {
            HandleCreated += (s, e) => {
                try {
                    SetWindowTheme(Handle, "DarkMode_Explorer", null);
                    foreach (Control c in Controls) {
                        try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
                    }
                } catch { }
            };
        }

        protected override void UpdateEditText()
        {
            int iv = (int)Value;
            Text = iv < 10 ? "0" + iv : iv.ToString();
        }
    }

    public class SplashForm : Form
    {
        private Timer _closeTimer, _fadeTimer;
        private float _progress = 1f;
        private bool _fadingOut;
        private Settings _settings;
        private int _displayMs;
        private DateTime _startTime;
        public int DisplayMs { get { return _displayMs; } set { _displayMs = value; } }

        public SplashForm(Settings settings)
        {
            _settings = settings ?? new Settings();
            _displayMs = 3000;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = true; TopMost = true;
            Activated += (s, e) => { if (TopMost) TopMost = false; };
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = DarkTheme.BG;
            DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
            ClientSize = Dpi.Size(300, 300);
            Opacity = 0; _startTime = DateTime.UtcNow;

            _fadeTimer = new Timer { Interval = 30 };
            _fadeTimer.Tick += (s, e) => {
                double elapsed = (DateTime.UtcNow - _startTime).TotalMilliseconds;
                _progress = Math.Max(0f, 1f - (float)(elapsed / _displayMs));
                if (_fadingOut)
                {
                    Opacity = Math.Max(Opacity - 0.06, 0);
                    if (Opacity <= 0.01) { _fadeTimer.Stop(); Hide(); Close(); }
                }
                else
                {
                    if (Opacity < 0.95) Opacity = Math.Min(Opacity + 0.1, 0.95);
                }
                Invalidate();
            };
            _closeTimer = new Timer { Interval = _displayMs };
            _closeTimer.Tick += (s, e) => { _closeTimer.Stop(); _fadingOut = true; };
            Click += (s, e) => { _closeTimer.Stop(); _fadingOut = true; };
            _fadeTimer.Start(); _closeTimer.Start();
        }

        // Mascot drawing now handled by Mascot.cs

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;

            using (var gb = new LinearGradientBrush(new Point(0, 0), new Point(0, h),
                DarkTheme.InputBG, DarkTheme.BG))
                g.FillRectangle(gb, 0, 0, w, h);
            using (var p = new Pen(Color.FromArgb(30, 30, 30), 1)) g.DrawRectangle(p, 0, 0, w - 1, h - 1);

            // Mascot — compact
            int mascotSz = Dpi.S(90);
            int mascotY = Dpi.S(14);
            Mascot.DrawMascot(g, (w - mascotSz) / 2f, mascotY, mascotSz);

            // Title
            int titleY = mascotY + mascotSz + Dpi.S(2);
            using (var f = new Font("Segoe UI", 13, FontStyle.Bold)) {
                var sz1 = g.MeasureString("Angry ", f); var sz2 = g.MeasureString("Audio", f);
                float totalW = sz1.Width + sz2.Width, tx = (w - totalW) / 2f;
                using (var b = new SolidBrush(Color.White)) g.DrawString("Angry", f, b, tx, titleY);
                using (var b = new SolidBrush(DarkTheme.Accent)) g.DrawString("Audio", f, b, tx + sz1.Width, titleY);
            }

            // Version pill + copyright on same line
            int infoY = titleY + Dpi.S(28);
            using (var f = new Font("Segoe UI", 6.5f)) {
                string vTxt = "v" + AppVersion.Version + "  \u00B7  " + AppVersion.Copyright;
                var vs = g.MeasureString(vTxt, f);
                float pillW = vs.Width + Dpi.S(12), pillH = vs.Height + Dpi.S(4);
                float vx = (w - pillW) / 2f;
                using (var rr = DarkTheme.RoundedRect(new Rectangle((int)vx, infoY, (int)pillW, (int)pillH), Dpi.S(4)))
                using (var b = new SolidBrush(Color.FromArgb(24, 24, 24)))
                    g.FillPath(b, rr);
                using (var b = new SolidBrush(DarkTheme.Txt4))
                    g.DrawString(vTxt, f, b, vx + Dpi.S(6), infoY + Dpi.S(2));
            }

            // PTT — golden shield + label
            int pttY = infoY + Dpi.S(26);
            bool pttActive = _settings.PushToTalkEnabled || _settings.PushToMuteEnabled || _settings.PushToToggleEnabled;
            string pttLabel = _settings.PushToTalkEnabled ? "Push-to-Talk" : _settings.PushToMuteEnabled ? "Push-to-Mute" : _settings.PushToToggleEnabled ? "Push-to-Toggle" : "Off";
            Color gold = Color.FromArgb(218, 175, 62);
            Color goldDim = Color.FromArgb(55, 50, 35);
            Color goldText = Color.FromArgb(235, 215, 145);
            Color goldTextDim = Color.FromArgb(60, 58, 45);
            float shSz = Dpi.S(22);
            float shShieldX = w / 2f - shSz / 2f;
            DarkTheme.DrawShield(g, shShieldX, pttY, shSz, pttActive ? gold : goldDim, pttActive);
            using (var f = new Font("Segoe UI", 8.5f, FontStyle.Bold)) using (var b = new SolidBrush(pttActive ? goldText : goldTextDim)) {
                var tsz = g.MeasureString(pttLabel, f);
                g.DrawString(pttLabel, f, b, (w - tsz.Width) / 2f, pttY + shSz + Dpi.S(2));
            }

            // Two-column status: AFK PROTECTION | ENFORCEMENT
            int colY = pttY + Dpi.S(42);
            int colMargin = Dpi.S(12);
            int colW = (w - colMargin * 2) / 2;
            int col1X = colMargin;
            int col2X = col1X + colW;
            int iconSz = Dpi.S(13);

            bool afkAny = _settings.AfkMicMuteEnabled || _settings.AfkSpeakerMuteEnabled;
            DrawColHeader(g, col1X, colY, colW, "AFK PROTECTION", afkAny);
            DrawStatusRow(g, col1X, colY + Dpi.S(16), colW, iconSz, "Mic", 0, _settings.AfkMicMuteEnabled);
            DrawStatusRow(g, col1X, colY + Dpi.S(34), colW, iconSz, "Spk", 1, _settings.AfkSpeakerMuteEnabled);

            using (var p = new Pen(Color.FromArgb(35, 35, 35), 1)) g.DrawLine(p, col2X, colY, col2X, colY + Dpi.S(48));

            bool enfAny = _settings.MicEnforceEnabled || _settings.SpeakerEnforceEnabled;
            DrawColHeader(g, col2X, colY, colW, "ENFORCEMENT", enfAny);
            DrawStatusRow(g, col2X, colY + Dpi.S(16), colW, iconSz, "Mic", 0, _settings.MicEnforceEnabled);
            DrawStatusRow(g, col2X, colY + Dpi.S(34), colW, iconSz, "Spk", 1, _settings.SpeakerEnforceEnabled);

            // Progress bar + dismiss text
            int barY = h - Dpi.S(30);
            using (var b = new SolidBrush(Color.FromArgb(26, 26, 26))) g.FillRectangle(b, 0, barY, w, Dpi.S(3));
            using (var b = new SolidBrush(DarkTheme.Accent)) g.FillRectangle(b, 0, barY, (int)(w * _progress), Dpi.S(3));
            double remaining = Math.Max(0, _displayMs - (DateTime.UtcNow - _startTime).TotalMilliseconds);
            int sec = (int)Math.Ceiling(remaining / 1000.0);
            using (var f = new Font("Segoe UI", 7f)) using (var b = new SolidBrush(Color.FromArgb(80, 80, 80))) {
                string txt = "Closing in " + sec + "s \u00B7 Click to Dismiss";
                var ts = g.MeasureString(txt, f); g.DrawString(txt, f, b, (w - ts.Width) / 2f, barY + Dpi.S(6));
            }
        }

        private void DrawCleanShield(Graphics g, float x, float y, float sz, Color fill, bool active) {
            float s = sz / 20f;
            var path = new GraphicsPath();
            // Clean symmetrical shield
            path.AddLine(x + 10*s, y, x + 18*s, y + 4*s);
            path.AddLine(x + 18*s, y + 4*s, x + 18*s, y + 10*s);
            path.AddBezier(x + 18*s, y + 10*s, x + 17*s, y + 15*s, x + 12*s, y + 18*s, x + 10*s, y + 20*s);
            path.AddBezier(x + 10*s, y + 20*s, x + 8*s, y + 18*s, x + 3*s, y + 15*s, x + 2*s, y + 10*s);
            path.AddLine(x + 2*s, y + 10*s, x + 2*s, y + 4*s);
            path.CloseFigure();
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            // Subtle highlight edge
            using (var p = new Pen(Color.FromArgb(active ? 50 : 20, 255, 255, 255), 0.8f * s)) g.DrawPath(p, path);
            if (active) {
                using (var p = new Pen(Color.White, 1.6f * s)) {
                    p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLine(p, x + 6.5f*s, y + 10.5f*s, x + 9*s, y + 13.5f*s);
                    g.DrawLine(p, x + 9*s, y + 13.5f*s, x + 14*s, y + 7*s);
                }
            }
            path.Dispose();
        }
        private void DrawColHeader(Graphics g, int x, int y, int colW, string heading, bool active) {
            using (var f = new Font("Segoe UI", 6.5f, FontStyle.Bold))
            using (var b = new SolidBrush(active ? DarkTheme.Accent : Color.FromArgb(60, 60, 60))) {
                var hs = g.MeasureString(heading, f);
                g.DrawString(heading, f, b, x + (colW - hs.Width) / 2f, y);
            }
        }
        // iconType: 0=mic, 1=speaker, 2=camera
        private void DrawStatusRow(Graphics g, int x, int y, int colW, int iconSz, string label, int iconType, bool active) {
            Color activeCol = Color.FromArgb(200, 200, 200), offCol = Color.FromArgb(55, 55, 55);
            Color iconCol = active ? activeCol : offCol;
            int cx = x + colW / 2;
            DarkTheme.DrawShield(g, cx - Dpi.S(32), y - Dpi.S(1), iconSz, active ? DarkTheme.Green : Color.FromArgb(42, 42, 42), active);
            if (iconType == 0) DrawCleanMic(g, cx - Dpi.S(14), y, iconSz, iconCol);
            else if (iconType == 1) DrawCleanSpeaker(g, cx - Dpi.S(14), y, iconSz, iconCol);
            using (var f = new Font("Segoe UI", 8f)) using (var br = new SolidBrush(iconCol))
                g.DrawString(label, f, br, cx + Dpi.S(2), y);
        }
        private void DrawCleanMic(Graphics g, float x, float y, float sz, Color c) {
            float s = sz / 16f;
            using (var b = new SolidBrush(c)) {
                // Mic body — rounded rect via path
                var body = new GraphicsPath();
                float bw = 4.5f*s, bh = 8*s, bx = x + 5.75f*s, by = y + 1*s, br = 2.2f*s;
                body.AddArc(bx, by, br*2, br*2, 180, 90); body.AddArc(bx+bw-br*2, by, br*2, br*2, 270, 90);
                body.AddArc(bx+bw-br*2, by+bh-br*2, br*2, br*2, 0, 90); body.AddArc(bx, by+bh-br*2, br*2, br*2, 90, 90);
                body.CloseFigure(); g.FillPath(b, body); body.Dispose();
                // Stand
                g.FillRectangle(b, x + 7.25f*s, y + 11.5f*s, 1.5f*s, 2.5f*s);
                g.FillRectangle(b, x + 5*s, y + 13.5f*s, 6*s, 1*s);
            }
            // Arc
            using (var p = new Pen(c, 1.2f*s)) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawArc(p, x + 3.5f*s, y + 4*s, 9*s, 8*s, 0, 180); }
        }
        private void DrawCleanSpeaker(Graphics g, float x, float y, float sz, Color c) {
            float s = sz / 16f;
            using (var b = new SolidBrush(c)) {
                g.FillRectangle(b, x + 3*s, y + 5.5f*s, 2.5f*s, 5*s);
                var cone = new GraphicsPath();
                cone.AddPolygon(new PointF[] { new PointF(x+5.5f*s,y+5.5f*s), new PointF(x+9*s,y+2.5f*s), new PointF(x+9*s,y+13.5f*s), new PointF(x+5.5f*s,y+10.5f*s) });
                g.FillPath(b, cone); cone.Dispose();
            }
            using (var p = new Pen(c, 1.2f*s)) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawArc(p, x + 10*s, y + 5*s, 3.5f*s, 6*s, -55, 110); }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00000008 | 0x08000000 | 0x00000080; return cp; } }
        protected override void Dispose(bool d) { if (d) { _fadeTimer?.Dispose(); _closeTimer?.Dispose(); } base.Dispose(d); }
    }

    // =====================================================================
    // WelcomeForm — 3-page wizard — v9.6 camera privacy
    // =====================================================================
    [System.Runtime.InteropServices.ComVisible(true)]
    public class WelcomeForm : Form
    {
        public bool ProtectMic { get; private set; }
        public bool ProtectSpeakers { get; private set; }
        public int MicVolPercent { get; private set; } = 100;
        public int SpkVolPercent { get; private set; } = 50;
        public bool AfkMicEnabled { get; private set; } = true;
        public int AfkMicSec { get; private set; } = 10;
        public bool AfkSpkEnabled { get; private set; }
        public int AfkSpkSec { get; private set; } = 10;
        public bool PttEnabled { get; private set; }
        public bool PtMuteEnabled { get; private set; }
        public bool PtToggleEnabled { get; private set; }
        public int PttKey { get; private set; } = 0x14;
        public bool StartupEnabled { get; private set; } = true;
        public bool NotifyCorrEnabled { get; private set; } = true;
        public bool NotifyDevEnabled { get; private set; } = true;

        private Panel _page1, _page2, _headerPanel;
        private Panel _card1, _card2;
        private SlickSlider _micSlider, _spkSlider;
        private ToggleSwitch _tglMicEnf, _tglSpkEnf, _tglAfkMic, _tglAfkSpk, _tglPtt, _tglPtm, _tglPtToggle;
        private ToggleSwitch _tglStartup, _tglNotifyCorr, _tglNotifyDev;
        private NumericUpDown _nudAfkMic, _nudAfkSpk;
        private Label _lblPttKey;
        private Timer _pollTimer;
        private Timer _micFlashTimer, _spkFlashTimer;
        private int _pttKeyCode = 0x14;
        private bool _capturingKey;
        private Button _btnNext, _btnBack, _btnSave;
        private int _currentPage = 1;
        private Timer _pulseTimer;
        private float _pulsePhase; // 0 to 2*PI, drives the glow animation
        private Panel _wizFooter; // stored ref for parent-level star painting
        private ShootingStar _shootingStar;
        private CelestialEvents _celestialEvents;

        // Dynamic painted text (no Labels — shooting stars pass through)
        private string _micNameText = "Detecting...", _spkNameText = "Detecting...";
        private string _micCurText = "Current: --%", _spkCurText = "Current: --%";
        private Color _micCurColor = DarkTheme.Green, _spkCurColor = DarkTheme.Green;
        // Volume lock snapshot/restore
        private int _micPreLockVol = -1, _spkPreLockVol = -1;
        private Timer _sliderRestoreMicTimer, _sliderRestoreSpkTimer;

        static readonly Color BG = DarkTheme.BG;
        static readonly Color ACC = DarkTheme.Accent;
        static readonly Color GREEN = DarkTheme.Green;
        static readonly Color CARD_BDR = DarkTheme.CardBdr;
        static readonly Color TXT = DarkTheme.Txt;
        static readonly Color TXT2 = DarkTheme.Txt2;
        static readonly Color TXT3 = DarkTheme.Txt3;
        static readonly Color INPUT_BG = DarkTheme.InputBG;
        static readonly Color INPUT_BDR = DarkTheme.InputBdr;

        static readonly string PAGE1_TIP =
            "Apps can silently listen through your microphone without your " +
            "knowledge. Angry Audio guards your privacy by keeping your mic " +
            "muted when you're not using it.";

        static readonly string PAGE2_TIP =
            "Apps can change your speaker and microphone volume at any time " +
            "without asking. Angry Audio locks your levels so nothing changes " +
            "without your permission.";

        private Action<string> _onToggle;
        const int STAR_SEED = 42;

        Point FormOffset(Control c) {
            int x = 0, y = 0;
            Control cur = c;
            while (cur != null && cur != this) { x += cur.Left; y += cur.Top; cur = cur.Parent; }
            return new Point(x, y);
        }

        void PaintUnifiedStars(Graphics g, Control c, float alphaMul = 1.0f, bool shootingStar = true) {
            try {
                var off = FormOffset(c);
                g.TranslateTransform(-off.X, -off.Y);
                DarkTheme.PaintCardStars(g, ClientSize.Width, ClientSize.Height, STAR_SEED, 0, alphaMul);
                if (shootingStar && _shootingStar != null)
                    DarkTheme.PaintShootingStar(g, ClientSize.Width, ClientSize.Height, _shootingStar);
                if (shootingStar && _celestialEvents != null)
                    DarkTheme.PaintCelestialEvent(g, ClientSize.Width, ClientSize.Height, _celestialEvents);
                g.ResetTransform();
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

        public WelcomeForm(Action<string> onToggle = null)
        {
            _onToggle = onToggle;
            Text = "Welcome to " + AppVersion.FullName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Shown += (s, e) => { Activate(); BringToFront(); };
            BackColor = BG; ForeColor = TXT;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f);
            ClientSize = Dpi.Size(480, 720);
            DoubleBuffered = true;
            try { Icon = Mascot.CreateIcon(); } catch { }

            // Footer
            var footer = new BufferedPanel { Dock = DockStyle.Bottom, Height = Dpi.S(80), BackColor = BG };
            footer.Paint += (s, e) => {
                var g = e.Graphics;
                PaintUnifiedStars(g, footer);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var p = new Pen(Color.FromArgb(30, 30, 30))) g.DrawLine(p, Dpi.S(16), 0, footer.Width - Dpi.S(32), 0);
                // Step indicator dots (2 pages)
                int dotY = Dpi.S(55);
                int cx = footer.Width / 2;
                for (int i = 1; i <= 2; i++) {
                    int dx = cx + (i - 2) * Dpi.S(16) + Dpi.S(4);
                    Color dc = (i == _currentPage) ? ACC : Color.FromArgb(50, 50, 50);
                    using (var b = new SolidBrush(dc)) g.FillEllipse(b, dx, dotY, Dpi.S(8), Dpi.S(8));
                }
                // Orbiting star — painted on PARENT so glow extends beyond button bounds
                Button activeBtn = _btnNext.Visible ? _btnNext : (_btnSave.Visible ? _btnSave : null);
                if (activeBtn != null)
                {
                    var saved = g.Save();
                    g.TranslateTransform(activeBtn.Left, activeBtn.Top);
                    DarkTheme.PaintOrbitingStar(g, activeBtn.Width, activeBtn.Height, _pulsePhase, Dpi.S(6));
                    g.Restore(saved);
                }
            };
            _wizFooter = footer;
            Controls.Add(footer);

            _btnBack = MakeBtn("\u2190 Back", TXT2, Color.FromArgb(28, 28, 28), false);
            _btnBack.Location = Dpi.Pt(16, 25); _btnBack.Visible = false;
            _btnBack.FlatAppearance.BorderColor = INPUT_BDR;
            _btnBack.Click += (s, e) => { if (_currentPage == 2) ShowPage1(); };
            footer.Controls.Add(_btnBack);

            _btnNext = MakeBtn("Next \u2192", Color.White, ACC, true);
            _btnNext.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnNext.Location = new Point(ClientSize.Width - Dpi.S(108), Dpi.S(25));
            _btnNext.Click += (s, e) => { if (_currentPage == 1) ShowPage2(); };
            footer.Controls.Add(_btnNext);

            _btnSave = MakeBtn("Save", Color.White, ACC, true);
            _btnSave.Size = Dpi.Size(105, 30);
            _btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnSave.Location = new Point(ClientSize.Width - Dpi.S(121), Dpi.S(25));
            _btnSave.Visible = false;
            _btnSave.Click += (s, e) => DoSave();
            footer.Controls.Add(_btnSave);

            // Pulse glow animation — impossible to miss
            _pulsePhase = 0f;
            _pulseTimer = new Timer { Interval = 30 };
            _pulseTimer.Tick += (s, e) => {
                _pulsePhase += 0.18f;
                if (_pulsePhase > (float)(Math.PI * 2)) _pulsePhase -= (float)(Math.PI * 2);
                // Dramatic flash — button goes from very dark to nearly white
                float pulse = (float)((Math.Sin(_pulsePhase * 0.8) + 1.0) / 2.0);
                // Dark phase: near black (20, 50, 80). Bright phase: near white (180, 240, 255)
                int r = (int)(20 + (180 - 20) * pulse);
                int gb = (int)(50 + (240 - 50) * pulse);
                int bl = (int)(80 + (255 - 80) * pulse);
                Color pulseBg = Color.FromArgb(r, gb, bl);
                if (_btnNext.Visible && !_btnNext.ClientRectangle.Contains(_btnNext.PointToClient(Cursor.Position)))
                    _btnNext.BackColor = pulseBg;
                if (_btnSave.Visible && !_btnSave.ClientRectangle.Contains(_btnSave.PointToClient(Cursor.Position)))
                    _btnSave.BackColor = pulseBg;
                if (_btnNext.Visible) _btnNext.Invalidate();
                if (_btnSave.Visible) _btnSave.Invalidate();
                if (_wizFooter != null) _wizFooter.Invalidate();
            };
            _pulseTimer.Start();

            // Header — consistent on both pages
            _headerPanel = new BufferedPanel { Dock = DockStyle.Top, Height = Dpi.S(180), BackColor = BG };
            _headerPanel.Paint += PaintHeader;
            Controls.Add(_headerPanel);

            // Pages
            _page1 = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = false, Padding = Dpi.Pad(16, 4, 16, 4), BackColor = BG };
            _page1.Paint += (s, e) => { PaintUnifiedStars(e.Graphics, _page1); };
            _page2 = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = false, Padding = Dpi.Pad(16, 4, 16, 4), BackColor = BG, Visible = false };
            _page2.Paint += (s, e) => { PaintUnifiedStars(e.Graphics, _page2); };
            // Shooting star animation
            _shootingStar = new ShootingStar(() => { try {
                var p = _currentPage == 1 ? _page1 : _page2;
                if (p.Visible) p.Invalidate();
                _headerPanel.Invalidate();
                footer.Invalidate();
            } catch { } });
            _shootingStar.Start();
            _celestialEvents = new CelestialEvents(() => { try {
                var p = _currentPage == 1 ? _page1 : _page2;
                if (p.Visible) p.Invalidate();
                _headerPanel.Invalidate();
                footer.Invalidate();
            } catch { } });
            _celestialEvents.Start();

            // ============================================================
            // Page 1: Single card — PTT + AFK (free features)
            // ============================================================
            _card1 = MakeRoundCard(); _card1.Dock = DockStyle.Top; _card1.Height = Dpi.S(444);
            int y = 14;

            // --- PTT toggles: 3 stacked with descriptions, matching Options panel ---
            _tglPtt = new ToggleSwitch { Location = Dpi.Pt(20, y + 44) };
            _tglPtt.CheckedChanged += (s2, e2) => {
                if (_onToggle != null) { _onToggle("ptt_key:" + _pttKeyCode); _onToggle(_tglPtt.Checked ? "ptt_on" : "ptt_off"); }
                if (_tglPtt.Checked) { if (_tglPtm != null && _tglPtm.Checked) _tglPtm.Checked = false; if (_tglPtToggle != null && _tglPtToggle.Checked) _tglPtToggle.Checked = false; if (_tglAfkMic != null && _tglAfkMic.Checked) _tglAfkMic.Checked = false; }
            };
            _tglPtt.PaintParentBg = PaintCardBg; _card1.Controls.Add(_tglPtt);

            _tglPtm = new ToggleSwitch { Location = Dpi.Pt(20, y + 86) };
            _tglPtm.CheckedChanged += (s2, e2) => {
                if (_onToggle != null) { _onToggle("ptt_key:" + _pttKeyCode); _onToggle(_tglPtm.Checked ? "ptm_on" : "ptm_off"); }
                if (_tglPtm.Checked) { if (_tglPtt != null && _tglPtt.Checked) _tglPtt.Checked = false; if (_tglPtToggle != null && _tglPtToggle.Checked) _tglPtToggle.Checked = false; if (_tglAfkMic != null && _tglAfkMic.Checked) _tglAfkMic.Checked = false; }
            };
            _tglPtm.PaintParentBg = PaintCardBg; _card1.Controls.Add(_tglPtm);

            _tglPtToggle = new ToggleSwitch { Location = Dpi.Pt(20, y + 128) };
            _tglPtToggle.CheckedChanged += (s2, e2) => {
                if (_onToggle != null) { _onToggle("ptt_key:" + _pttKeyCode); _onToggle(_tglPtToggle.Checked ? "ptt_toggle_on" : "ptt_toggle_off"); }
                if (_tglPtToggle.Checked) { if (_tglPtt != null && _tglPtt.Checked) _tglPtt.Checked = false; if (_tglPtm != null && _tglPtm.Checked) _tglPtm.Checked = false; if (_tglAfkMic != null && _tglAfkMic.Checked) _tglAfkMic.Checked = false; }
            };
            _tglPtToggle.PaintParentBg = PaintCardBg; _card1.Controls.Add(_tglPtToggle);

            // Hotkey row — below all 3 toggles, matching Options panel style
            _lblPttKey = new Label { Text = "Caps Lock", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = ACC, BackColor = INPUT_BG, Size = Dpi.Size(80, 26), TextAlign = ContentAlignment.MiddleCenter, Location = Dpi.Pt(118, y + 174) };
            _lblPttKey.Paint += (s, e) => { using (var p = new Pen(CARD_BDR)) e.Graphics.DrawRectangle(p, 0, 0, _lblPttKey.Width - 1, _lblPttKey.Height - 1); };
            _lblPttKey.MouseEnter += (s, e) => { if (!_capturingKey) _lblPttKey.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPttKey.MouseLeave += (s, e) => { if (!_capturingKey) _lblPttKey.BackColor = INPUT_BG; };
            _lblPttKey.Click += (s, e) => StartKeyCapture();
            _card1.Controls.Add(_lblPttKey);

            int afkY = 262;
            _tglAfkMic = new ToggleSwitch { Checked = true, Location = Dpi.Pt(20, afkY + 44) };
            _tglAfkMic.CheckedChanged += (s2, e2) => {
                if (_onToggle != null) { _onToggle("afk_mic_sec:" + (int)_nudAfkMic.Value); _onToggle(_tglAfkMic.Checked ? "afk_mic_on" : "afk_mic_off"); }
                if (_tglAfkMic.Checked) { if (_tglPtt != null && _tglPtt.Checked) _tglPtt.Checked = false; if (_tglPtm != null && _tglPtm.Checked) _tglPtm.Checked = false; if (_tglPtToggle != null && _tglPtToggle.Checked) _tglPtToggle.Checked = false; }
            };
            _tglAfkMic.PaintParentBg = PaintCardBg; _card1.Controls.Add(_tglAfkMic);

            _nudAfkMic = new PaddedNumericUpDown { Minimum = 5, Maximum = 3600, Value = 10, Location = Dpi.Pt(200, afkY + 42), Size = Dpi.Size(60, 24), BackColor = INPUT_BG, ForeColor = TXT, Font = new Font("Segoe UI", 9f), BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center };
            _nudAfkMic.Leave += (s2,e2) => { try { int v; if(int.TryParse(_nudAfkMic.Text,out v)){v=Math.Max(5,Math.Min(3600,v));_nudAfkMic.Value=v;} } catch { _nudAfkMic.Value=10; } };
            _card1.Controls.Add(_nudAfkMic);

            _tglAfkSpk = new ToggleSwitch { Location = Dpi.Pt(20, afkY + 102) };
            _tglAfkSpk.CheckedChanged += (s2, e2) => { if (_onToggle != null) { _onToggle("afk_spk_sec:" + (int)_nudAfkSpk.Value); _onToggle(_tglAfkSpk.Checked ? "afk_spk_on" : "afk_spk_off"); } };
            _tglAfkSpk.PaintParentBg = PaintCardBg; _card1.Controls.Add(_tglAfkSpk);

            _nudAfkSpk = new PaddedNumericUpDown { Minimum = 5, Maximum = 3600, Value = 10, Location = Dpi.Pt(200, afkY + 100), Size = Dpi.Size(60, 24), BackColor = INPUT_BG, ForeColor = TXT, Font = new Font("Segoe UI", 9f), BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center };
            _nudAfkSpk.Leave += (s2,e2) => { try { int v; if(int.TryParse(_nudAfkSpk.Text,out v)){v=Math.Max(5,Math.Min(3600,v));_nudAfkSpk.Value=v;} } catch { _nudAfkSpk.Value=10; } };
            _card1.Controls.Add(_nudAfkSpk);

            // ALL text painted — zero Labels, shooting star visible everywhere
            _card1.Paint += (s, e) => {
                var g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int py = Dpi.S(14);
                // PTT section header
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString("Push-to-Talk", f, b, Dpi.S(20), py);
                // Gold shield badge — matches splash screen style
                {
                    float shieldSz = Dpi.S(16);
                    float shieldX = Dpi.S(136);
                    float shieldY = py + Dpi.S(0);
                    DarkTheme.DrawShield(g, shieldX, shieldY, shieldSz, Color.FromArgb(218, 175, 62), true);
                    using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                    using (var b = new SolidBrush(Color.FromArgb(235, 215, 145)))
                        g.DrawString("Best Protection", f, b, shieldX + shieldSz + Dpi.S(4), py + Dpi.S(1));
                }
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Hold a key to control your mic.", f, b, Dpi.S(20), py + Dpi.S(20));
                // Toggle 1: Push-to-Talk
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Enable Push-to-Talk", f, b, Dpi.S(68), py + Dpi.S(45));
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Mic stays muted at the OS level until you hold the hotkey.", f, b, Dpi.S(68), py + Dpi.S(64));
                // Toggle 2: Push-to-Mute
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Enable Push-to-Mute", f, b, Dpi.S(68), py + Dpi.S(87));
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Mic stays open \u2014 hold the hotkey to mute.", f, b, Dpi.S(68), py + Dpi.S(106));
                // Toggle 3: Push-to-Toggle
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Enable Push-to-Toggle", f, b, Dpi.S(68), py + Dpi.S(129));
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Press once to unmute, press again to mute.", f, b, Dpi.S(68), py + Dpi.S(148));
                // Hotkey row — below all toggles, matching Options panel style
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Hotkey:", f, b, Dpi.S(20), py + Dpi.S(178));
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(DarkTheme.Txt4))
                    g.DrawString("Click to change \u00B7 Esc cancels", f, b, Dpi.S(206), py + Dpi.S(180));
                // System-wide mic note
                using (var f = new Font("Segoe UI", 7f)) using (var b = new SolidBrush(Color.FromArgb(90, ACC.R, ACC.G, ACC.B)))
                    g.DrawString("Mutes all microphones system-wide \u2014 headset, camera mic, USB devices.", f, b, Dpi.S(20), py + Dpi.S(210));
                // Separator
                int sepY = Dpi.S(248);
                using (var p = new Pen(CARD_BDR)) g.DrawLine(p, Dpi.S(20), sepY, _card1.Width - Dpi.S(20), sepY);
                // AFK section
                int ay = Dpi.S(262);
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString("AFK Protection", f, b, Dpi.S(20), ay);
                // Green shield badge — matches splash screen style
                {
                    float shieldSz = Dpi.S(16);
                    float shieldX = Dpi.S(152);
                    float shieldY = ay + Dpi.S(0);
                    DarkTheme.DrawShield(g, shieldX, shieldY, shieldSz, DarkTheme.Green, true);
                    using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                    using (var b = new SolidBrush(Color.FromArgb(140, 220, 140)))
                        g.DrawString("Better Than Nothing", f, b, shieldX + shieldSz + Dpi.S(4), ay + Dpi.S(1));
                }
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Auto-mute when you step away.", f, b, Dpi.S(20), ay + Dpi.S(20));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Mute mic after", f, b, Dpi.S(68), ay + Dpi.S(45));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("seconds", f, b, Dpi.S(266), ay + Dpi.S(45));
                // AFK speaker separator + labels
                int sep2Y = ay + Dpi.S(78);
                using (var p = new Pen(CARD_BDR)) g.DrawLine(p, Dpi.S(20), sep2Y, _card1.Width - Dpi.S(20), sep2Y);
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Mute speakers after", f, b, Dpi.S(68), ay + Dpi.S(103));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("seconds", f, b, Dpi.S(266), ay + Dpi.S(103));
                using (var f = new Font("Segoe UI", 8f)) using (var b = new SolidBrush(DarkTheme.Txt4))
                    g.DrawString("Angry Audio gradually fades your audio back when you return.", f, b, Dpi.S(20), ay + Dpi.S(132));
            };

            var tip1 = MakeTipPanel();
            var sp1 = new BufferedPanel { Dock = DockStyle.Top, Height = Dpi.S(4), BackColor = BG };
            sp1.Paint += (s, e) => { PaintUnifiedStars(e.Graphics, sp1); };
            _page1.Controls.Add(tip1); _page1.Controls.Add(sp1); _page1.Controls.Add(_card1);

            // ============================================================
            // Page 2: Single unified card — Volume Enforcement + General
            // ============================================================
            _card2 = MakeRoundCard(); _card2.Dock = DockStyle.Top; _card2.Height = Dpi.S(458);
            y = 14;

            // Interactive controls ONLY — all text is painted below
            _micSlider = new SlickSlider { Minimum = 0, Maximum = 100, Value = 100, Location = Dpi.Pt(80, y + 38), Size = Dpi.Size(260, 30) };
            
            _micSlider.ValueChanged += (s2, e2) => { _card2.Invalidate(); };
            _micSlider.DragCompleted += (s2, e2) => { if (_tglMicEnf.Checked) try { Audio.SetMicVolume(_micSlider.Value); } catch { } };
            _micSlider.PaintParentBg = PaintCardBg; _card2.Controls.Add(_micSlider);

            _tglMicEnf = new ToggleSwitch { Location = Dpi.Pt(20, y + 88) };
            
            _tglMicEnf.CheckedChanged += (s2, e2) => {
                if (_tglMicEnf.Checked) {
                    try { _micPreLockVol = (int)Audio.GetMicVolume(); } catch { _micPreLockVol = -1; }
                    if (_sliderRestoreMicTimer != null) { _sliderRestoreMicTimer.Stop(); _sliderRestoreMicTimer.Dispose(); _sliderRestoreMicTimer = null; }
                    try { Audio.SetMicVolume(_micSlider.Value); } catch { }
                    FlashApplied(true, "Mic locked at " + _micSlider.Value + "%.");
                } else {
                    FlashApplied(true, "Mic lock disabled.");
                    AnimateWizardSliderRestore(true);
                }
                if (_onToggle != null) { _onToggle("mic_vol:" + _micSlider.Value); _onToggle(_tglMicEnf.Checked ? "mic_lock_on" : "mic_lock_off"); }
            };
            _tglMicEnf.PaintParentBg = PaintCardBg; _card2.Controls.Add(_tglMicEnf);

            int y2 = y + 134;
            _spkSlider = new SlickSlider { Minimum = 0, Maximum = 100, Value = 50, Location = Dpi.Pt(80, y2 + 38), Size = Dpi.Size(260, 30) };
            
            _spkSlider.ValueChanged += (s2, e2) => { _card2.Invalidate(); };
            _spkSlider.DragCompleted += (s2, e2) => { if (_tglSpkEnf.Checked) try { Audio.SetSpeakerVolume(_spkSlider.Value); } catch { } };
            _spkSlider.PaintParentBg = PaintCardBg; _card2.Controls.Add(_spkSlider);

            _tglSpkEnf = new ToggleSwitch { Location = Dpi.Pt(20, y2 + 88) };
            
            _tglSpkEnf.CheckedChanged += (s2, e2) => {
                if (_tglSpkEnf.Checked) {
                    try { _spkPreLockVol = (int)Audio.GetSpeakerVolume(); } catch { _spkPreLockVol = -1; }
                    if (_sliderRestoreSpkTimer != null) { _sliderRestoreSpkTimer.Stop(); _sliderRestoreSpkTimer.Dispose(); _sliderRestoreSpkTimer = null; }
                    try { Audio.SetSpeakerVolume(_spkSlider.Value); } catch { }
                    FlashApplied(false, "Speaker locked at " + _spkSlider.Value + "%.");
                } else {
                    FlashApplied(false, "Speaker lock disabled.");
                    AnimateWizardSliderRestore(false);
                }
                if (_onToggle != null) { _onToggle("spk_vol:" + _spkSlider.Value); _onToggle(_tglSpkEnf.Checked ? "spk_lock_on" : "spk_lock_off"); }
            };
            _tglSpkEnf.PaintParentBg = PaintCardBg; _card2.Controls.Add(_tglSpkEnf);

            // General section toggles — same card, below enforcement
            int gy = 284; // General section start Y — centered between separators
            _tglStartup = new ToggleSwitch { Location = Dpi.Pt(20, gy + 36), Checked = true };
            _tglStartup.CheckedChanged += (s2, e2) => { if (_onToggle != null) _onToggle(_tglStartup.Checked ? "startup_on" : "startup_off"); };
            _tglStartup.PaintParentBg = PaintCardBg; _card2.Controls.Add(_tglStartup);

            _tglNotifyCorr = new ToggleSwitch { Location = Dpi.Pt(20, gy + 104), Checked = true };
            _tglNotifyCorr.CheckedChanged += (s2, e2) => { if (_onToggle != null) _onToggle(_tglNotifyCorr.Checked ? "notify_corr_on" : "notify_corr_off"); };
            _tglNotifyCorr.PaintParentBg = PaintCardBg; _card2.Controls.Add(_tglNotifyCorr);

            _tglNotifyDev = new ToggleSwitch { Location = Dpi.Pt(20, gy + 136), Checked = true };
            _tglNotifyDev.CheckedChanged += (s2, e2) => { if (_onToggle != null) _onToggle(_tglNotifyDev.Checked ? "notify_dev_on" : "notify_dev_off"); };
            _tglNotifyDev.PaintParentBg = PaintCardBg; _card2.Controls.Add(_tglNotifyDev);

            // ALL text painted — zero Labels, shooting star visible everywhere
            _card2.Paint += (s, e) => {
                var g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int py = Dpi.S(14);
                // -- Microphone section --
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString("Microphone", f, b, Dpi.S(20), py);
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString(_micNameText, f, b, Dpi.S(20), py + Dpi.S(18));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT2))
                    g.DrawString("Lock at", f, b, Dpi.S(20), py + Dpi.S(42));
                using (var f = new Font("Segoe UI", 9.5f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString(_micSlider.Value + "%", f, b, Dpi.S(348), py + Dpi.S(42));
                using (var f = new Font("Segoe UI", 8f)) using (var b = new SolidBrush(_micCurColor))
                    g.DrawString(_micCurText, f, b, Dpi.S(20), py + Dpi.S(66));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT2))
                    g.DrawString("Prevent apps from changing mic volume.", f, b, Dpi.S(68), py + Dpi.S(89));
                // Separator — mic/speaker
                int sepY = Dpi.S(14 + 122);
                using (var p = new Pen(CARD_BDR)) g.DrawLine(p, Dpi.S(20), sepY, _card2.Width - Dpi.S(20), sepY);
                // -- Speakers section --
                int sy = Dpi.S(14 + 134);
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString("Speakers", f, b, Dpi.S(20), sy);
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString(_spkNameText, f, b, Dpi.S(20), sy + Dpi.S(18));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT2))
                    g.DrawString("Lock at", f, b, Dpi.S(20), sy + Dpi.S(42));
                using (var f = new Font("Segoe UI", 9.5f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString(_spkSlider.Value + "%", f, b, Dpi.S(348), sy + Dpi.S(42));
                using (var f = new Font("Segoe UI", 8f)) using (var b = new SolidBrush(_spkCurColor))
                    g.DrawString(_spkCurText, f, b, Dpi.S(20), sy + Dpi.S(66));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT2))
                    g.DrawString("Prevent apps from changing speaker volume.", f, b, Dpi.S(68), sy + Dpi.S(89));

                // === Separator — enforcement / general ===
                int genSepY = Dpi.S(274);
                using (var p = new Pen(CARD_BDR)) g.DrawLine(p, Dpi.S(20), genSepY, _card2.Width - Dpi.S(20), genSepY);

                // -- General section --
                int genY = Dpi.S(284);
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString("General", f, b, Dpi.S(20), genY);
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Startup behavior and notifications.", f, b, Dpi.S(20), genY + Dpi.S(18));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT2))
                    g.DrawString("Start with Windows", f, b, Dpi.S(68), genY + Dpi.S(38));
                // Separator — general / notifications
                int genSep2 = genY + Dpi.S(70);
                using (var p = new Pen(CARD_BDR)) g.DrawLine(p, Dpi.S(20), genSep2, _card2.Width - Dpi.S(20), genSep2);
                using (var f = new Font("Segoe UI", 7f, FontStyle.Bold)) using (var b = new SolidBrush(DarkTheme.Txt4))
                    g.DrawString("NOTIFICATIONS", f, b, Dpi.S(20), genY + Dpi.S(82));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT2))
                    g.DrawString("Volume Correction Alerts", f, b, Dpi.S(68), genY + Dpi.S(106));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT2))
                    g.DrawString("Device Change Alerts", f, b, Dpi.S(68), genY + Dpi.S(138));
            };

            var tip2 = MakeTipPanel();
            var sp2 = new BufferedPanel { Dock = DockStyle.Top, Height = Dpi.S(4), BackColor = BG };
            sp2.Paint += (s, e) => { PaintUnifiedStars(e.Graphics, sp2); };

            _page2.Controls.Add(tip2); _page2.Controls.Add(sp2); _page2.Controls.Add(_card2);

            Controls.Add(_page2); Controls.Add(_page1);
            _page1.BringToFront();
            Application.AddMessageFilter(new NudDefocusFilter(this));
            UpdateDeviceInfo();
            _pollTimer = new Timer { Interval = 1000 };
            _pollTimer.Tick += (s, e) => UpdateDeviceInfo();
            _pollTimer.Start();
        }

        Panel MakeTipPanel() {
            var tip = new BufferedPanel { Dock = DockStyle.Top, Height = Dpi.S(24), BackColor = BG };
            tip.Paint += (s, e) => {
                var g = e.Graphics; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                PaintUnifiedStars(g, tip);
                using (var p = new Pen(Color.FromArgb(40, 40, 40))) g.DrawLine(p, Dpi.S(16), 0, tip.Width - Dpi.S(32), 0);
                string msg = "These settings can be changed anytime by double-clicking the tray kitty.";
                using (var f = new Font("Segoe UI", 8f))
                using (var b = new SolidBrush(DarkTheme.Txt4))
                {
                    var sz = g.MeasureString(msg, f);
                    float x = (tip.Width - sz.Width) / 2f;
                    float y = (tip.Height - sz.Height) / 2f + Dpi.S(2);
                    g.DrawString(msg, f, b, x, y);
                }
            };
            return tip;
        }

        Button MakeBtn(string text, Color fg, Color bg, bool bold) {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, Size = Dpi.Size(92, 30), ForeColor = fg, BackColor = bg, Font = new Font("Segoe UI", 9.5f, bold ? FontStyle.Bold : FontStyle.Regular), TabStop = false };
            b.FlatAppearance.BorderSize = bold ? 0 : 1;
            Color origBg = bg;
            b.MouseEnter += (s, e) => b.BackColor = bold ? Color.FromArgb(120, 200, 240) : Color.FromArgb(38, 38, 38);
            b.MouseLeave += (s, e) => b.BackColor = origBg;
            return b;
        }

        void PaintHeader(object sender, PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = _headerPanel.Width, h = _headerPanel.Height;

            // Unified starfield — same shooting star crosses entire form including header
            PaintUnifiedStars(g, _headerPanel);

            // Subtle gradient overlay (semi-transparent so stars show through)
            using (var gb = new LinearGradientBrush(new Point(0, 0), new Point(0, h),
                Color.FromArgb(140, DarkTheme.InputBG.R, DarkTheme.InputBG.G, DarkTheme.InputBG.B), Color.FromArgb(100, DarkTheme.BG.R, DarkTheme.BG.G, DarkTheme.BG.B)))
                g.FillRectangle(gb, 0, 0, w, h);

            // Bottom separator line
            using (var p = new Pen(Color.FromArgb(32, 32, 32))) g.DrawLine(p, Dpi.S(24), h - 1, w - Dpi.S(24), h - 1);

            int mascotSz = Dpi.S(100);
            Mascot.DrawMascot(g, (w - mascotSz) / 2f, Dpi.S(14), mascotSz);

            // Title with accent color split
            using (var f = new Font("Segoe UI", 12, FontStyle.Bold)) {
                var sz1 = g.MeasureString("Angry ", f); var sz2 = g.MeasureString("Audio", f);
                float totalW = sz1.Width + sz2.Width, tx = (w - totalW) / 2f, ty = Dpi.Sf(118);
                using (var b = new SolidBrush(Color.White)) g.DrawString("Angry", f, b, tx, ty);
                using (var b = new SolidBrush(ACC)) g.DrawString("Audio", f, b, tx + sz1.Width, ty);
            }

            // Page-specific subtitle
            string tipText = _currentPage == 2 ? PAGE2_TIP : PAGE1_TIP;
            using (var f = new Font("Segoe UI", 7.8f)) {
                var rect = new RectangleF(Dpi.Sf(24), Dpi.Sf(146), w - Dpi.Sf(48), Dpi.Sf(44));
                using (var b = new SolidBrush(TXT3)) using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString(tipText, f, b, rect, sf);
            }
        }

        Panel MakeRoundCard() {
            var c = new BufferedPanel { BackColor = BG, Padding = Dpi.Pad(0, 0, 0, 0) };
            c.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int rad = Dpi.S(6);

                // 1) Full unified starfield WITH shooting star — seamless across entire form
                PaintUnifiedStars(g, c);

                // 2) Frosted glass — visible grey tint for premium card look
                var cardRect = new Rectangle(0, 0, c.Width - 1, c.Height - 1);
                using (var tint = new SolidBrush(Color.FromArgb(200, DarkTheme.CardBG.R, DarkTheme.CardBG.G, DarkTheme.CardBG.B)))
                    g.FillRectangle(tint, cardRect);
                PaintUnifiedStars(g, c, 0.25f, false);

                // 3) Card border
                using (var pen = new Pen(CARD_BDR)) {
                    var rr = new System.Drawing.Drawing2D.GraphicsPath();
                    int d = rad * 2;
                    rr.AddArc(0, 0, d, d, 180, 90);
                    rr.AddArc(cardRect.Right - d, 0, d, d, 270, 90);
                    rr.AddArc(cardRect.Right - d, cardRect.Bottom - d, d, d, 0, 90);
                    rr.AddArc(0, cardRect.Bottom - d, d, d, 90, 90);
                    rr.CloseFigure();
                    g.DrawPath(pen, rr);
                    rr.Dispose();
                }
                // Blue accent line at top
                using (var pen = new Pen(Color.FromArgb(25, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B), Dpi.PenW(1)))
                    g.DrawLine(pen, Dpi.S(6), 0, c.Width - Dpi.S(6), 0);
            };
            c.Resize += (s, e) => DarkTheme.ApplyRoundedRegion(c, Dpi.S(6));
            c.Layout += (s, e) => DarkTheme.ApplyRoundedRegion(c, Dpi.S(6));
            return c;
        }

        void StartKeyCapture() { _capturingKey = true; _lblPttKey.Text = "Press..."; _lblPttKey.BackColor = ACC; _lblPttKey.ForeColor = Color.White; KeyPreview = true; KeyDown += OnKeyCapture; }
        void OnKeyCapture(object s, KeyEventArgs e) { if (!_capturingKey) return; e.Handled = true; e.SuppressKeyPress = true; if (e.KeyCode == Keys.Escape) { _lblPttKey.Text = KeyName(_pttKeyCode); _lblPttKey.BackColor = INPUT_BG; _lblPttKey.ForeColor = ACC; _capturingKey = false; KeyPreview = false; KeyDown -= OnKeyCapture; return; } _pttKeyCode = (int)e.KeyCode; _lblPttKey.Text = KeyName(_pttKeyCode); _lblPttKey.BackColor = INPUT_BG; _lblPttKey.ForeColor = ACC; _capturingKey = false; KeyPreview = false; KeyDown -= OnKeyCapture; }
        string KeyName(int c) { return PushToTalk.GetKeyName(c); }

        void HidePage(Panel p) {
            p.Visible = false;
            p.Dock = DockStyle.None;
            p.SetBounds(0, 0, 0, 0); // zero-size = truly invisible
        }
        void ShowPageFill(Panel p) {
            p.Dock = DockStyle.Fill;
            p.Visible = true;
            p.BringToFront();
        }

        void ShowPage1() {
            _currentPage = 1;
            HidePage(_page2);
            ShowPageFill(_page1);
            _btnNext.Visible = true; _btnSave.Visible = false; _btnBack.Visible = false;
            Invalidate(true);
        }
        void ShowPage2() {
            _currentPage = 2;
            HidePage(_page1);
            ShowPageFill(_page2);
            _btnNext.Visible = false; _btnSave.Visible = true; _btnBack.Visible = true;
            Invalidate(true);
        }
        void DoSave() {
            ProtectMic = _tglMicEnf.Checked; ProtectSpeakers = _tglSpkEnf.Checked;
            MicVolPercent = _micSlider.Value; SpkVolPercent = _spkSlider.Value;
            AfkMicEnabled = _tglAfkMic.Checked; AfkMicSec = (int)_nudAfkMic.Value; AfkSpkEnabled = _tglAfkSpk.Checked; AfkSpkSec = (int)_nudAfkSpk.Value;
            PttEnabled = _tglPtt.Checked; PtMuteEnabled = _tglPtm.Checked; PtToggleEnabled = _tglPtToggle.Checked; PttKey = _pttKeyCode;
            StartupEnabled = _tglStartup.Checked; NotifyCorrEnabled = _tglNotifyCorr.Checked; NotifyDevEnabled = _tglNotifyDev.Checked;
            DialogResult = DialogResult.OK; Close();
        }

        void AnimateWizardSliderRestore(bool isMic)
        {
            int target = isMic ? _micPreLockVol : _spkPreLockVol;
            if (target < 0) return;

            var slider = isMic ? _micSlider : _spkSlider;
            if (slider == null) return;

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
                    if (!IsDisposed) { slider.Value = target; _card2.Invalidate(); }
                    if (isMic) _micPreLockVol = -1; else _spkPreLockVol = -1;
                    return;
                }
                int newVal = start + (int)(stepSize * step);
                newVal = Math.Max(0, Math.Min(100, newVal));
                slider.Value = newVal;
                _card2.Invalidate();
            };

            if (isMic) _sliderRestoreMicTimer = timer; else _sliderRestoreSpkTimer = timer;
            timer.Start();
        }

        void FlashApplied(bool isMic, string msg) {
            // Cancel any existing flash for this channel
            if (isMic) { _micFlashTimer?.Stop(); _micFlashTimer?.Dispose(); _micFlashTimer = null; }
            else { _spkFlashTimer?.Stop(); _spkFlashTimer?.Dispose(); _spkFlashTimer = null; }

            if (isMic) { _micCurText = "\u2713 " + msg; _micCurColor = GREEN; }
            else { _spkCurText = "\u2713 " + msg; _spkCurColor = GREEN; }
            _card2.Invalidate();
            var t = new Timer { Interval = 1500 };
            t.Tick += (s, e) => {
                t.Stop(); t.Dispose();
                if (isMic) _micFlashTimer = null; else _spkFlashTimer = null;
                UpdateDeviceInfo(); // Refresh from actual device state instead of stale captured values
                try { _card2.Invalidate(); } catch { }
            };
            if (isMic) _micFlashTimer = t; else _spkFlashTimer = t;
            t.Start();
        }

        void UpdateDeviceInfo() {
            try {
                float micVol = Audio.GetMicVolume(), spkVol = Audio.GetSpeakerVolume();
                if (micVol >= 0) _micCurText = "Current: " + (int)micVol + "%";
                if (spkVol >= 0) _spkCurText = "Current: " + (int)spkVol + "%";
                string micName = Audio.GetMicName(), spkName = Audio.GetSpeakerName();
                if (micName != null) _micNameText = micName;
                if (spkName != null) _spkNameText = spkName;
                try { _card2.Invalidate(); } catch { }
            } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            _pulseTimer?.Stop(); _pulseTimer?.Dispose();
            _pollTimer?.Stop(); _pollTimer?.Dispose();
            _micFlashTimer?.Stop(); _micFlashTimer?.Dispose();
            _spkFlashTimer?.Stop(); _spkFlashTimer?.Dispose();
            _sliderRestoreMicTimer?.Stop(); _sliderRestoreMicTimer?.Dispose();
            _sliderRestoreSpkTimer?.Stop(); _sliderRestoreSpkTimer?.Dispose();
            _shootingStar?.Dispose();
            _celestialEvents?.Stop(); _celestialEvents?.Dispose();
            if (DialogResult != DialogResult.OK) { ProtectMic = false; ProtectSpeakers = false; }
            base.OnFormClosing(e);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (keyData == Keys.Space && !_capturingKey) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override CreateParams CreateParams {
            get {
                CreateParams cp = base.CreateParams;
                return cp;
            }
        }
    }
}
