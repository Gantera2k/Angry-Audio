// WelcomeForm.cs — First-run welcome wizard.
// Uses StarBackground for star rendering, Controls.cs for shared UI controls.
// SplashForm and PaddedNumericUpDown have been moved to Controls.cs.
//
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AngryAudio
{

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
        public int PttKey { get; private set; } = 0;
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
        private int _pttKeyCode = 0;
        private bool _capturingKey;
        private bool _modeChosen; // true once user picks PTT/PTM/Toggle — guides star to hotkey
        private Action _showPage1Extras, _showPage2Extras;
        private int _currentPage = 1;
        private Timer _pulseTimer;
        private float _pulsePhase; // 0 to 2*PI, drives the glow animation
        private StarBackground _stars;

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

        static readonly string PAGE1_TIP =
            "Apps can silently listen through your microphone without your " +
            "knowledge. Angry Audio guards your privacy by keeping your mic " +
            "muted when you're not using it.";

        static readonly string PAGE2_TIP =
            "Apps can change your speaker and microphone volume at any time " +
            "without asking. Angry Audio locks your levels so nothing changes " +
            "without your permission.";

        private Action<string> _onToggle;
        // Star rendering now handled by shared StarBackground class

        Point FormOffset(Control c) {
            int x = 0, y = 0;
            Control cur = c;
            while (cur != null && cur != this) { x += cur.Left; y += cur.Top; cur = cur.Parent; }
            return new Point(x, y);
        }

        void PaintUnifiedStars(Graphics g, Control c, float alphaMul = 1.0f, bool shootingStar = true) {
            var off = FormOffset(c);
            int w = ClientSize.Width, h = ClientSize.Height;
            _stars.Paint(g, w, h, off.X, off.Y, dim: alphaMul < 0.5f, shootingStar: shootingStar);
        }

        void PaintCardBg(Graphics g, Control child) {
            var off = FormOffset(child);
            int w = ClientSize.Width, h = ClientSize.Height;
            _stars.PaintChildBg(g, w, h, off.X, off.Y, child.Width, child.Height);
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
            var footer = new BufferedPanel { Dock = DockStyle.Bottom, Height = Dpi.S(48), BackColor = BG };
            // Owner-drawn "buttons" — no child controls, no clipping issues
            Rectangle _nextRect = Rectangle.Empty, _saveRect = Rectangle.Empty, _backRect = Rectangle.Empty;
            bool _nextHover = false, _saveHover = false, _backHover = false;
            bool _nextVisible = true, _saveVisible = false, _backVisible = false;

            footer.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                PaintUnifiedStars(g, footer);
                using (var p = new Pen(Color.FromArgb(30, 30, 30))) g.DrawLine(p, Dpi.S(16), 0, footer.Width - Dpi.S(32), 0);
                // Step indicator dots
                int dotY = Dpi.S(20); int cx = footer.Width / 2;
                for (int i = 1; i <= 2; i++) {
                    int dx = cx + (i - 2) * Dpi.S(16) + Dpi.S(4);
                    Color dc = (i == _currentPage) ? ACC : Color.FromArgb(50, 50, 50);
                    using (var b = new SolidBrush(dc)) g.FillEllipse(b, dx, dotY, Dpi.S(8), Dpi.S(8));
                }
                int cr = Dpi.S(6);
                // Back button
                if (_backVisible) {
                    _backRect = new Rectangle(Dpi.S(16), Dpi.S(10), Dpi.S(92), Dpi.S(30));
                    Color bbg = _backHover ? Color.FromArgb(45, 45, 45) : Color.FromArgb(28, 28, 28);
                    using (var path = DarkTheme.RoundedRect(_backRect, cr))
                    using (var b = new SolidBrush(bbg)) g.FillPath(b, path);
                    using (var path = DarkTheme.RoundedRect(_backRect, cr))
                    using (var p = new Pen(Color.FromArgb(50, 50, 50))) g.DrawPath(p, path);
                    TextRenderer.DrawText(g, "\u2190 Back", DarkTheme.BtnFont, _backRect, Color.FromArgb(170, 170, 170), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                // Next button
                if (_nextVisible) {
                    _nextRect = new Rectangle(footer.Width - Dpi.S(108), Dpi.S(10), Dpi.S(92), Dpi.S(30));
                    float pulse = (float)((Math.Sin(_pulsePhase * 0.8) + 1.0) / 2.0);
                    int pr = (int)(20 + (180 - 20) * pulse), pg = (int)(50 + (240 - 50) * pulse), pb = (int)(80 + (255 - 80) * pulse);
                    Color nbg = _nextHover ? Color.FromArgb(140, 220, 255) : Color.FromArgb(pr, pg, pb);
                    using (var path = DarkTheme.RoundedRect(_nextRect, cr))
                    using (var b = new SolidBrush(nbg)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, "Next \u2192", DarkTheme.BtnFontBold, _nextRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    // Star — painted on same surface, extends freely beyond the "button" rect
                    var saved = g.Save();
                    g.TranslateTransform(_nextRect.X, _nextRect.Y);
                    DarkTheme.PaintOrbitingStar(g, _nextRect.Width, _nextRect.Height, _pulsePhase, cr);
                    g.Restore(saved);
                }
                // Save button
                if (_saveVisible) {
                    _saveRect = new Rectangle(footer.Width - Dpi.S(121), Dpi.S(10), Dpi.S(105), Dpi.S(30));
                    float pulse = (float)((Math.Sin(_pulsePhase * 0.8) + 1.0) / 2.0);
                    int pr = (int)(20 + (180 - 20) * pulse), pg = (int)(50 + (240 - 50) * pulse), pb = (int)(80 + (255 - 80) * pulse);
                    Color sbg = _saveHover ? Color.FromArgb(140, 220, 255) : Color.FromArgb(pr, pg, pb);
                    using (var path = DarkTheme.RoundedRect(_saveRect, cr))
                    using (var b = new SolidBrush(sbg)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, "Save", DarkTheme.BtnFontBold, _saveRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    var saved = g.Save();
                    g.TranslateTransform(_saveRect.X, _saveRect.Y);
                    DarkTheme.PaintOrbitingStar(g, _saveRect.Width, _saveRect.Height, _pulsePhase, cr);
                    g.Restore(saved);
                }
            };
            footer.MouseMove += (s, e) => {
                bool nh = _nextVisible && _nextRect.Contains(e.Location);
                bool sh = _saveVisible && _saveRect.Contains(e.Location);
                bool bh = _backVisible && _backRect.Contains(e.Location);
                if (nh != _nextHover || sh != _saveHover || bh != _backHover) {
                    _nextHover = nh; _saveHover = sh; _backHover = bh;
                    footer.Cursor = (nh || sh || bh) ? Cursors.Hand : Cursors.Default;
                    footer.Invalidate();
                }
            };
            footer.MouseLeave += (s, e) => { _nextHover = _saveHover = _backHover = false; footer.Cursor = Cursors.Default; footer.Invalidate(); };
            footer.MouseClick += (s, e) => {
                if (_nextVisible && _nextRect.Contains(e.Location)) { if (_currentPage == 1) ShowPage2(); }
                else if (_saveVisible && _saveRect.Contains(e.Location)) DoSave();
                else if (_backVisible && _backRect.Contains(e.Location)) { if (_currentPage == 2) ShowPage1(); }
            };
            // Wire up visibility controls for page switching
            _showPage1Extras = () => { _nextVisible = true; _saveVisible = false; _backVisible = false; footer.Invalidate(); };
            _showPage2Extras = () => { _nextVisible = false; _saveVisible = true; _backVisible = true; footer.Invalidate(); };
            Controls.Add(footer);

            // Pulse animation — only invalidates the single footer panel
            _pulsePhase = 0f;
            _pulseTimer = new Timer { Interval = 30 };
            _pulseTimer.Tick += (s, e) => {
                _pulsePhase += 0.08f;
                if (_pulsePhase > (float)(Math.PI * 2)) _pulsePhase -= (float)(Math.PI * 2);
                footer.Invalidate();
                // Invalidate card1 for orbiting star (either toggle section or hotkey)
                if (_card1 != null && _card1.Visible && (!_modeChosen || (_modeChosen && _pttKeyCode <= 0)))
                    _card1.Invalidate(false);
                // Invalidate visible tips for zip line animation
                if (_tipHotkey != null && _tipHotkey.Visible) _tipHotkey.Invalidate();
                if (_tipFunCallout != null && _tipFunCallout.Visible) _tipFunCallout.Invalidate();
                if (_tipMicLock != null && _tipMicLock.Visible) _tipMicLock.Invalidate();
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
            // Shooting star animation — shared StarBackground system
            _stars = new StarBackground(() => { try {
                var p = _currentPage == 1 ? _page1 : _page2;
                if (p.Visible) p.Invalidate();
                _headerPanel.Invalidate();
                footer.Invalidate();
            } catch { } });

            // ============================================================
            // Page 1: Single card — PTT + AFK (free features)
            // ============================================================
            _card1 = MakeRoundCard(); _card1.Dock = DockStyle.Top; _card1.Height = Dpi.S(444);
            int y = 14;

            // --- PTT toggles: 3 stacked with descriptions, matching Options panel ---
            _tglPtt = new ToggleSwitch { Location = Dpi.Pt(20, y + 44) };
            _tglPtt.CheckedChanged += (s2, e2) => {
                if (_onToggle != null) { _onToggle("ptt_key:" + _pttKeyCode); _onToggle(_tglPtt.Checked ? "ptt_on" : "ptt_off"); }
                if (_tglPtt.Checked) { if (_tglPtm != null && _tglPtm.Checked) _tglPtm.Checked = false; if (_tglPtToggle != null && _tglPtToggle.Checked) _tglPtToggle.Checked = false; if (_tglAfkMic != null && _tglAfkMic.Checked) _tglAfkMic.Checked = false; _modeChosen = true; if (_pttKeyCode <= 0) ShowTip(_tipHotkey); _card1.Invalidate(false); }
            };
            _tglPtt.PaintParentBg = PaintCardBg; _card1.Controls.Add(_tglPtt);

            _tglPtm = new ToggleSwitch { Location = Dpi.Pt(20, y + 86) };
            _tglPtm.CheckedChanged += (s2, e2) => {
                if (_onToggle != null) { _onToggle("ptt_key:" + _pttKeyCode); _onToggle(_tglPtm.Checked ? "ptm_on" : "ptm_off"); }
                if (_tglPtm.Checked) { if (_tglPtt != null && _tglPtt.Checked) _tglPtt.Checked = false; if (_tglPtToggle != null && _tglPtToggle.Checked) _tglPtToggle.Checked = false; if (_tglAfkMic != null && _tglAfkMic.Checked) _tglAfkMic.Checked = false; _modeChosen = true; if (_pttKeyCode <= 0) ShowTip(_tipHotkey); _card1.Invalidate(false); }
            };
            _tglPtm.PaintParentBg = PaintCardBg; _card1.Controls.Add(_tglPtm);

            _tglPtToggle = new ToggleSwitch { Location = Dpi.Pt(20, y + 128) };
            _tglPtToggle.CheckedChanged += (s2, e2) => {
                if (_onToggle != null) { _onToggle("ptt_key:" + _pttKeyCode); _onToggle(_tglPtToggle.Checked ? "ptt_toggle_on" : "ptt_toggle_off"); }
                if (_tglPtToggle.Checked) { if (_tglPtt != null && _tglPtt.Checked) _tglPtt.Checked = false; if (_tglPtm != null && _tglPtm.Checked) _tglPtm.Checked = false; if (_tglAfkMic != null && _tglAfkMic.Checked) _tglAfkMic.Checked = false; _modeChosen = true; if (_pttKeyCode <= 0) ShowTip(_tipHotkey); _card1.Invalidate(false); }
            };
            _tglPtToggle.PaintParentBg = PaintCardBg; _card1.Controls.Add(_tglPtToggle);
            // Toggles ENABLED from the start — user picks mode FIRST
            CreateTips();

            // Hotkey row — below all 3 toggles, matching Options panel style
            _lblPttKey = new Label { Text = "Add Key", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = ACC, BackColor = INPUT_BG, Size = Dpi.Size(80, 26), TextAlign = ContentAlignment.MiddleCenter, Location = Dpi.Pt(118, y + 174) };
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

                // === Guided orbiting star ===
                // Phase 1: No mode chosen → orbit the toggle section (draws eye to "pick one")
                // Phase 2: Mode chosen, no key → orbit the hotkey label (draws eye to "set key")
                // Phase 3: Both done → no star
                if (!_modeChosen && _tglPtt != null) {
                    // Orbit around all 3 toggles as a group
                    int tTop = _tglPtt.Top - Dpi.S(6);
                    int tBot = _tglPtToggle.Bottom + Dpi.S(6);
                    int tLeft = _tglPtt.Left - Dpi.S(6);
                    int tRight = _card1.Width - Dpi.S(14);
                    var saved = g.Save();
                    g.TranslateTransform(tLeft, tTop);
                    DarkTheme.PaintOrbitingStar(g, tRight - tLeft, tBot - tTop, _pulsePhase, Dpi.S(4));
                    g.Restore(saved);
                }
                else if (_modeChosen && _pttKeyCode <= 0 && _lblPttKey != null) {
                    // Orbit around hotkey label
                    var r = _lblPttKey.Bounds;
                    int pad = Dpi.S(6);
                    var saved = g.Save();
                    g.TranslateTransform(r.X - pad, r.Y - pad);
                    DarkTheme.PaintOrbitingStar(g, r.Width + pad * 2, r.Height + pad * 2, _pulsePhase, Dpi.S(4));
                    g.Restore(saved);
                }
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
            // Mic lock tip added to card2 after all controls
            if (_tipMicLock != null) { _card2.Controls.Add(_tipMicLock); _tipMicLock.BringToFront(); }

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
            var c = new BufferedPanel { BackColor = Color.Transparent, Padding = Dpi.Pad(0, 0, 0, 0) };
            c.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int rad = Dpi.S(6);

                // 1) Full unified starfield — seamless across form
                PaintUnifiedStars(g, c);

                // 2) Frosted glass — tint + dim stars (same pattern as Options MakeCard)
                var cardRect = new Rectangle(0, 0, c.Width - 1, c.Height - 1);
                var clipPath = new GraphicsPath();
                int d = rad * 2;
                clipPath.AddArc(0, 0, d, d, 180, 90);
                clipPath.AddArc(cardRect.Right - d, 0, d, d, 270, 90);
                clipPath.AddArc(cardRect.Right - d, cardRect.Bottom - d, d, d, 0, 90);
                clipPath.AddArc(0, cardRect.Bottom - d, d, d, 90, 90);
                clipPath.CloseFigure();
                using (var tint = new SolidBrush(DarkTheme.GlassTint))
                    g.FillPath(tint, clipPath);
                var oldClip = g.Clip;
                g.SetClip(clipPath, CombineMode.Replace);
                PaintUnifiedStars(g, c, 0.35f, false);
                g.Clip = oldClip;
                clipPath.Dispose();

                // 3) Card border
                using (var pen = new Pen(CARD_BDR)) {
                    var rr = new GraphicsPath();
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

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        private Timer _captureTimer;
        private bool[] _prevKeyState = new bool[256];
        void StartKeyCapture() { _capturingKey = true; _lblPttKey.Text = "Press..."; _lblPttKey.BackColor = ACC; _lblPttKey.ForeColor = Color.White; StartCapturePolling(); }
        void StartCapturePolling() {
            if (_onToggle != null) _onToggle("capture_start");
            for (int i = 0; i < 256; i++) _prevKeyState[i] = (GetAsyncKeyState(i) & 0x8000) != 0;
            if (_captureTimer == null) { _captureTimer = new Timer { Interval = 30 }; _captureTimer.Tick += CaptureTimerTick; }
            _captureTimer.Start();
        }
        void StopCapturePolling() { if (_captureTimer != null) _captureTimer.Stop(); if (_onToggle != null) _onToggle("capture_stop"); }
        void CaptureTimerTick(object s, EventArgs e) {
            if (!_capturingKey) { StopCapturePolling(); return; }
            for (int vk = 1; vk < 256; vk++) {
                if (vk >= 1 && vk <= 3) continue; // skip mouse buttons
                bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                bool wasDown = _prevKeyState[vk];
                _prevKeyState[vk] = down;
                if (down && !wasDown) {
                    StopCapturePolling();
                    OnKeyCapture(this, new KeyEventArgs((Keys)vk));
                    return;
                }
            }
        }
        void OnKeyCapture(object s, KeyEventArgs e) { if (!_capturingKey) return; if (e.KeyCode == Keys.Escape) { _lblPttKey.Text = KeyName(_pttKeyCode); _lblPttKey.BackColor = INPUT_BG; _lblPttKey.ForeColor = ACC; _capturingKey = false; Logger.Info("Welcome: key capture cancelled"); return; } _pttKeyCode = (int)e.KeyCode; _lblPttKey.Text = KeyName(_pttKeyCode); _lblPttKey.BackColor = INPUT_BG; _lblPttKey.ForeColor = ACC; _capturingKey = false; Logger.Info("Welcome: captured key vk=" + _pttKeyCode + " (" + KeyName(_pttKeyCode) + ")"); if (_tipHotkey != null) _tipHotkey.Visible = false; ShowTip(_tipFunCallout); _card1.Invalidate(false); }
        // (toggles are always enabled — user picks mode FIRST in the new guided flow)

        // --- Toggle flicker animation (draws attention to mode selection) ---
        private Timer _flashTimer;
        private int _flashStep;
        private Panel[] _flashHighlights;
        void FlashToggles() {
            if (_flashTimer != null && _flashTimer.Enabled) return;
            _flashStep = 0;

            // Create highlight panels behind toggle rows (same pattern as Options EnforceToggleSelection)
            if (_flashHighlights == null && _card1 != null) {
                var toggles = new ToggleSwitch[] { _tglPtt, _tglPtm, _tglPtToggle };
                Color flashBorder = Color.FromArgb(180, ACC.R, ACC.G, ACC.B);
                _flashHighlights = new Panel[3];
                for (int i = 0; i < 3; i++) {
                    var tgl = toggles[i];
                    if (tgl == null) continue;
                    _flashHighlights[i] = new BufferedPanel {
                        Location = new Point(Dpi.S(4), tgl.Top - Dpi.S(4)),
                        Size = new Size(_card1.Width - Dpi.S(8), Dpi.S(42)),
                        BackColor = Color.Transparent, Visible = false
                    };
                    var hl = _flashHighlights[i];
                    hl.Paint += (s, e) => {
                        var p2 = (Panel)s;
                        if (p2.BackColor != Color.Transparent) {
                            using (var pen = new Pen(flashBorder, 2f))
                                e.Graphics.DrawRectangle(pen, 1, 1, p2.Width - 3, p2.Height - 3);
                        }
                    };
                    _card1.Controls.Add(hl);
                    hl.BringToFront();
                    tgl.BringToFront();
                }
            }

            Color flashColor = Color.FromArgb(60, ACC.R, ACC.G, ACC.B);
            if (_flashTimer == null) {
                _flashTimer = new Timer { Interval = 180 };
                _flashTimer.Tick += (s2, e2) => {
                    if (_flashHighlights == null) { _flashTimer.Stop(); return; }
                    if (_flashStep < 6) {
                        int idx = _flashStep % 3;
                        for (int i = 0; i < 3; i++) {
                            if (_flashHighlights[i] == null) continue;
                            _flashHighlights[i].BackColor = (i == idx) ? flashColor : Color.Transparent;
                            _flashHighlights[i].Visible = true;
                            _flashHighlights[i].Invalidate();
                        }
                        _flashStep++;
                    } else {
                        for (int i = 0; i < 3; i++)
                            if (_flashHighlights[i] != null) _flashHighlights[i].Visible = false;
                        _flashTimer.Stop();
                    }
                };
            }
            _flashTimer.Start();
        }

        // --- Tip panels — styled as mini-cards matching the app's visual language ---
        private Panel _tipHotkey, _tipFunCallout, _tipMicLock;
        private Timer _tipDismissTimer;

        Panel MakeTipCard(string text, int w, int h, bool arrowUp = false) {
            int arrowH = arrowUp ? Dpi.S(8) : 0;
            var tip = new BufferedPanel { Size = Dpi.Size(w, h + (arrowUp ? 8 : 0)), Visible = false, BackColor = Color.Transparent };
            float _fadeAlpha = 0f;
            Timer _fadeIn = null;
            // Zip line cache — per tip, captured by closure
            PointF[] _zipPts = null;
            float[] _zipDist = null;
            float _zipTotal = 0;
            int _zipTick = 0;
            tip.Paint += (s, e) => {
                _zipTick++;
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int cr = Dpi.S(8);
                int bodyTop = arrowH;
                var bodyRect = new Rectangle(0, bodyTop, tip.Width - 1, tip.Height - bodyTop - 1);

                // Glow shadow behind
                using (var glowPath = DarkTheme.RoundedRect(new Rectangle(2, bodyTop + 2, tip.Width - 3, tip.Height - bodyTop - 3), cr))
                using (var glowBrush = new SolidBrush(Color.FromArgb((int)(20 * _fadeAlpha), ACC.R, ACC.G, ACC.B)))
                    g.FillPath(glowBrush, glowPath);

                // Background fill — dark, slightly frosted
                using (var path = DarkTheme.RoundedRect(bodyRect, cr))
                using (var b = new SolidBrush(Color.FromArgb((int)(240 * _fadeAlpha), 16, 20, 28)))
                    g.FillPath(b, path);

                // Border — accent blue, subtle
                using (var path = DarkTheme.RoundedRect(bodyRect, cr))
                using (var p = new Pen(Color.FromArgb((int)(70 * _fadeAlpha), ACC.R, ACC.G, ACC.B), 1f))
                    g.DrawPath(p, path);

                // === ZIP LINE — blue data pulse racing around the border ===
                if (_fadeAlpha >= 0.8f) {
                    // Flatten the border path into points (once, on first use)
                    if (_zipPts == null || _zipPts.Length == 0) {
                        using (var path = DarkTheme.RoundedRect(bodyRect, cr)) {
                            path.Flatten(null, 0.5f);
                            _zipPts = (PointF[])path.PathPoints.Clone();
                        }
                        // Pre-compute cumulative distances
                        _zipDist = new float[_zipPts.Length];
                        _zipDist[0] = 0;
                        for (int i = 1; i < _zipPts.Length; i++) {
                            float dx = _zipPts[i].X - _zipPts[i-1].X;
                            float dy = _zipPts[i].Y - _zipPts[i-1].Y;
                            _zipDist[i] = _zipDist[i-1] + (float)Math.Sqrt(dx*dx + dy*dy);
                        }
                        // Close the loop distance
                        float cx = _zipPts[0].X - _zipPts[_zipPts.Length-1].X;
                        float cy = _zipPts[0].Y - _zipPts[_zipPts.Length-1].Y;
                        _zipTotal = _zipDist[_zipPts.Length-1] + (float)Math.Sqrt(cx*cx + cy*cy);
                    }

                    if (_zipPts.Length > 4 && _zipTotal > 1f) {
                        // _zipTick increments every frame, drives position continuously
                        float speed = 5f; // pixels per frame — fast electric feel
                        float headPos = (_zipTick * speed) % _zipTotal;
                        float trailLen = _zipTotal * 0.25f; // 25% of perimeter — longer trail

                        for (int i = 0; i < _zipPts.Length; i++) {
                            int next = (i + 1) % _zipPts.Length;
                            float d0 = _zipDist[i];
                            float d1 = (i < _zipPts.Length - 1) ? _zipDist[i+1] : _zipTotal;

                            // Distance from head BACKWARDS along the trail (modular)
                            float mid = (d0 + d1) * 0.5f;
                            float dist = headPos - mid;
                            if (dist < 0) dist += _zipTotal; // wrap
                            // dist is now how far BEHIND the head this segment is
                            // Only draw if within trail length (behind the head)
                            if (dist > trailLen) continue;

                            float t = 1f - (dist / trailLen); // 1 at head, 0 at tail
                            t = t * t; // ease — sharper falloff
                            int za = (int)(255 * t * _fadeAlpha);
                            if (za > 8) {
                                float pw = 1.5f + t * 2f; // thicker at head, max ~3.5px
                                using (var zp = new Pen(Color.FromArgb(za, ACC.R, ACC.G, ACC.B), pw))
                                    g.DrawLine(zp, _zipPts[i], _zipPts[next]);
                            }
                        }
                    }
                }

                // Arrow pointing up (if enabled)
                if (arrowUp) {
                    int ax = tip.Width / 4; // offset left
                    var arrowPts = new Point[] {
                        new Point(ax, 0),
                        new Point(ax - Dpi.S(6), arrowH),
                        new Point(ax + Dpi.S(6), arrowH)
                    };
                    using (var b = new SolidBrush(Color.FromArgb((int)(240 * _fadeAlpha), 16, 20, 28)))
                        g.FillPolygon(b, arrowPts);
                    using (var p = new Pen(Color.FromArgb((int)(70 * _fadeAlpha), ACC.R, ACC.G, ACC.B), 1f)) {
                        g.DrawLine(p, arrowPts[0], arrowPts[1]);
                        g.DrawLine(p, arrowPts[0], arrowPts[2]);
                    }
                }

                // Left accent bar — vibrant, eye-catching
                int barW = Dpi.S(3);
                using (var barPath = DarkTheme.RoundedRect(new Rectangle(0, bodyTop, barW + cr, tip.Height - bodyTop - 1), cr))
                {
                    var oldClip2 = g.Clip;
                    g.SetClip(new Rectangle(0, bodyTop, barW + 2, tip.Height - bodyTop));
                    using (var b = new SolidBrush(Color.FromArgb((int)(200 * _fadeAlpha), ACC.R, ACC.G, ACC.B)))
                        g.FillPath(b, barPath);
                    g.Clip = oldClip2;
                }

                // Text — bright and readable
                var textRect = new Rectangle(Dpi.S(14), bodyTop + Dpi.S(8), tip.Width - Dpi.S(28), tip.Height - bodyTop - Dpi.S(16));
                using (var tb = new SolidBrush(Color.FromArgb((int)(255 * _fadeAlpha), 210, 220, 235)))
                    g.DrawString(text, DarkTheme.Caption, tb, new RectangleF(textRect.X, textRect.Y, textRect.Width, textRect.Height),
                        new StringFormat { LineAlignment = StringAlignment.Center });
            };
            tip.Click += (s, e) => { tip.Visible = false; if (_fadeIn != null) { _fadeIn.Stop(); _fadeIn.Dispose(); _fadeIn = null; } };
            tip.VisibleChanged += (s, e) => {
                if (tip.Visible) {
                    _fadeAlpha = 0f;
                    _zipPts = null; _zipDist = null; _zipTotal = 0; _zipTick = 0; // reset zip cache
                    _fadeIn = new Timer { Interval = 30 };
                    int fadeStep = 0;
                    _fadeIn.Tick += (s2, e2) => {
                        fadeStep++;
                        _fadeAlpha = Math.Min(1f, fadeStep * 0.12f);
                        tip.Invalidate();
                        if (_fadeAlpha >= 1f) { _fadeIn.Stop(); _fadeIn.Dispose(); _fadeIn = null; }
                    };
                    _fadeIn.Start();
                }
            };
            return tip;
        }

        void CreateTips() {
            // STEP 2 TIP: After mode chosen → guide to hotkey
            _tipHotkey = MakeTipCard("Pick the same key you use in Discord or your game. That's it \u2014 you're done!", 280, 44, arrowUp: false);
            _tipHotkey.Location = Dpi.Pt(20, 210);
            _card1.Controls.Add(_tipHotkey); _tipHotkey.BringToFront();

            // STEP 3 TIP: After key captured → confirm what just happened
            _tipFunCallout = MakeTipCard("ALL connected mics are now controlled by your hotkey \u2014 not just one. Every mic. Hit Next!", 360, 42, arrowUp: false);
            _tipFunCallout.Location = Dpi.Pt(14, 248);
            _card1.Controls.Add(_tipFunCallout); _tipFunCallout.BringToFront();

            // PAGE 2 TIP: Mic lock explanation
            _tipMicLock = MakeTipCard("Toggle this on so you never have to yell at your computer. Your mic stays at 100%, fully controlled by your hotkey.", 340, 48, arrowUp: false);
            _tipMicLock.Location = Dpi.Pt(20, 126);
        }

        void ShowTip(Panel tip) {
            if (tip == null) return;
            tip.Visible = true;
            // Reset auto-dismiss
            if (_tipDismissTimer != null) { _tipDismissTimer.Stop(); _tipDismissTimer.Dispose(); }
            _tipDismissTimer = new Timer { Interval = 12000 };
            _tipDismissTimer.Tick += (s, e) => {
                if (_tipHotkey != null) _tipHotkey.Visible = false;
                if (_tipFunCallout != null) _tipFunCallout.Visible = false;
                if (_tipMicLock != null) _tipMicLock.Visible = false;
                _tipDismissTimer.Stop();
            };
            _tipDismissTimer.Start();
        }
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
            _showPage1Extras?.Invoke();
            Invalidate(true);
        }
        void ShowPage2() {
            _currentPage = 2;
            
            HidePage(_page1);
            ShowPageFill(_page2);
            _showPage2Extras?.Invoke();
            ShowTip(_tipMicLock);
            Invalidate(true);
        }
        private bool _saved;
        void DoSave() {
            if (_saved) return; _saved = true;
            Logger.Info("Welcome: DoSave — PTT=" + _tglPtt.Checked + " PTM=" + _tglPtm.Checked + " Toggle=" + _tglPtToggle.Checked + " Key=" + _pttKeyCode + " MicLock=" + _tglMicEnf.Checked + " SpkLock=" + _tglSpkEnf.Checked);
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
            _captureTimer?.Stop(); _captureTimer?.Dispose();
            _flashTimer?.Stop(); _flashTimer?.Dispose();
            _tipDismissTimer?.Stop(); _tipDismissTimer?.Dispose();
            _micFlashTimer?.Stop(); _micFlashTimer?.Dispose();
            _spkFlashTimer?.Stop(); _spkFlashTimer?.Dispose();
            _sliderRestoreMicTimer?.Stop(); _sliderRestoreMicTimer?.Dispose();
            _sliderRestoreSpkTimer?.Stop(); _sliderRestoreSpkTimer?.Dispose();
            _stars?.Dispose();
            if (DialogResult != DialogResult.OK) { ProtectMic = false; ProtectSpeakers = false; }
            base.OnFormClosing(e);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (_capturingKey) return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == Keys.Space) return true;
            if (keyData == Keys.Escape) { Close(); return true; }
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
