// WelcomeForm.cs — First-run welcome wizard.
// Uses StarBackground for star rendering, Controls.cs for shared UI controls.
// SplashForm and PaddedNumericUpDown have been moved to Controls.cs.
//
using System;
using System.Collections.Generic;
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
        public int MicVolPercent { get; private set; }
        public int SpkVolPercent { get; private set; }
        public bool AfkMicEnabled { get; private set; }
        public int AfkMicSec { get; private set; }
        public bool AfkSpkEnabled { get; private set; }
        public int AfkSpkSec { get; private set; }
        public bool PttEnabled { get; private set; }
        public bool PtMuteEnabled { get; private set; }
        public bool PtToggleEnabled { get; private set; }
        public int PttKey { get; private set; }
        public int PtMuteKey { get; private set; }
        public int PtToggleKey { get; private set; }
        public bool StartupEnabled { get; private set; }
        public bool NotifyCorrEnabled { get; private set; }
        public bool NotifyDevEnabled { get; private set; }
        public bool VoiceActivityEnabled { get; private set; }
        public int VoiceActivityThreshold { get; private set; }
        public int VoiceActivityHoldoverMs { get; private set; }

        // Voice Activity wizard controls
        private ToggleSwitch _tglWizVA;
        private SlickSlider _trkWizVAThreshold;
        private Timer _wizVAMeterTimer;
        private PushToTalk _wizPtt; // reference for peak monitoring
        private int _wizMeterPixelX, _wizMeterPixelY, _wizMeterPixelW, _wizMeterPixelH;
        private bool _wizMeterDragging;

        private Panel _page1, _page2, _headerPanel;
        private Panel _card1, _card2;
        private Dictionary<Panel, List<CardIcon>> _cardIconMap = new Dictionary<Panel, List<CardIcon>>();
        private Dictionary<Panel, List<CardToggle>> _cardToggleMap = new Dictionary<Panel, List<CardToggle>>();
        private Dictionary<Panel, List<CardSlider>> _cardSliderMap = new Dictionary<Panel, List<CardSlider>>();
        private SlickSlider _micSlider, _spkSlider;
        private ToggleSwitch _tglMicEnf, _tglSpkEnf, _tglAfkMic, _tglAfkSpk, _tglPtt, _tglPtm, _tglPtToggle;
        private ToggleSwitch _tglStartup, _tglNotifyCorr, _tglNotifyDev;
        private NumericUpDown _nudAfkMic, _nudAfkSpk, _nudWizHoldover;
        private Label _lblPttKey, _lblPtmKey, _lblPtToggleKey;
        private Timer _pollTimer;
        private Timer _micFlashTimer, _spkFlashTimer;
        private int _pttKeyCode = 0, _ptmKeyCode = 0, _ptToggleKeyCode = 0;
        private bool _wizToggleRejecting; // true while rejecting a toggle-on without hotkey
        private Action _showPage1Extras, _showPage2Extras, _showPage2SaveReady;
        private int _currentPage = 1;
        private Timer _pulseTimer;
        private float _pulsePhase; // 0 to 2*PI, drives the glow animation
        private int _flickerFrame; // counts up from 0, drives 1-2-3 intro flicker then stops
        private Timer _micHeroTimer;
        private int _micHeroFrame;
        private Timer _flickerNextTimer;
        private bool _heroResetting;
        private bool _heroToggleVisualOff; // true = paint mic lock toggle as OFF during hero animation
        private bool _showButtonStar;
        private int _modeGlowIndex = -1;
        private int _modeGlowFrame;
        private int _tglGlistenFrame;
        private ToggleSwitch _tglGlistenTarget;
        private bool[] _modeActive = new bool[3];
        private bool[] _prevModeKeys = new bool[3];
        private bool _wizMuteToggleState = true;
        private Label _captureGlowLabel;
        private int _captureGlowFrame;
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
            "Choose how you want to control your mic. Pick a hotkey mode, " +
            "or try Voice Activity \u2014 it listens and unmutes automatically.";

        static readonly string PAGE2_TIP =
            "Lock your volume, set AFK protection, and choose your startup preferences. " +
            "Hit Save when you're ready.";

        private AudioSettings _audio;
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
            using (var tint = new SolidBrush(DarkTheme.GlassTint))
                g.FillRectangle(tint, 0, 0, child.Width, child.Height);
            PaintUnifiedStars(g, child, 0.35f, false);
        }

        public WelcomeForm(AudioSettings audio, PushToTalk ptt = null)
        {
            MicVolPercent = 100;
            SpkVolPercent = 50;
            AfkMicEnabled = true;
            AfkMicSec = 10;
            AfkSpkSec = 10;
            PttKey = 0;
            PtMuteKey = 0;
            PtToggleKey = 0;
            StartupEnabled = true;
            NotifyCorrEnabled = true;
            NotifyDevEnabled = true;
            VoiceActivityThreshold = 15;
            VoiceActivityHoldoverMs = 2000;

            _audio = audio;
            _wizPtt = ptt;
            Text = "Welcome to " + AppVersion.FullName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Shown += (s, e) => {
                // Force to foreground — needed when launched via explorer.exe de-elevation
                // from the installer, because Windows doesn't grant foreground rights to
                // indirectly spawned processes. TopMost trick reliably steals focus.
                TopMost = true;
                Activate();
                BringToFront();
                TopMost = false;
                ActiveControl = null;
            };
            BackColor = BG; ForeColor = TXT;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f);
            ClientSize = Dpi.Size(480, 720);
            DoubleBuffered = true;
            try { Icon = Mascot.CreateIcon(); } catch { }
            DarkTheme.DarkTitleBar(Handle);

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
                // Step indicator dots — 2 pages
                int dotY = Dpi.S(20); int cx = footer.Width / 2;
                for (int i = 1; i <= 2; i++) {
                    int dx = cx + (i * 2 - 3) * Dpi.S(8);
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
                    Color nbg;
                    if (_nextHover) { nbg = Color.FromArgb(140, 220, 255); }
                    else if (_showButtonStar) {
                        float pulse = (float)((Math.Sin(_pulsePhase * 0.8) + 1.0) / 2.0);
                        int pr = (int)(20 + (180 - 20) * pulse), pg2 = (int)(50 + (240 - 50) * pulse), pb = (int)(80 + (255 - 80) * pulse);
                        nbg = Color.FromArgb(pr, pg2, pb);
                    } else { nbg = Color.FromArgb(50, 120, 180); }
                    using (var path = DarkTheme.RoundedRect(_nextRect, cr))
                    using (var b = new SolidBrush(nbg)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, "Next \u2192", DarkTheme.BtnFontBold, _nextRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    if (_showButtonStar) {
                        var saved = g.Save();
                        g.TranslateTransform(_nextRect.X, _nextRect.Y);
                        DarkTheme.PaintOrbitingStar(g, _nextRect.Width, _nextRect.Height, _pulsePhase, cr);
                        g.Restore(saved);
                    }
                }
                // Save button
                if (_saveVisible) {
                    _saveRect = new Rectangle(footer.Width - Dpi.S(121), Dpi.S(10), Dpi.S(105), Dpi.S(30));
                    Color sbg;
                    if (_saveHover) { sbg = Color.FromArgb(140, 220, 255); }
                    else if (_showButtonStar) {
                        float pulse = (float)((Math.Sin(_pulsePhase * 0.8) + 1.0) / 2.0);
                        int pr = (int)(20 + (180 - 20) * pulse), pg2 = (int)(50 + (240 - 50) * pulse), pb = (int)(80 + (255 - 80) * pulse);
                        sbg = Color.FromArgb(pr, pg2, pb);
                    } else { sbg = Color.FromArgb(50, 120, 180); }
                    using (var path = DarkTheme.RoundedRect(_saveRect, cr))
                    using (var b = new SolidBrush(sbg)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, "Save", DarkTheme.BtnFontBold, _saveRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    if (_showButtonStar) {
                        var saved = g.Save();
                        g.TranslateTransform(_saveRect.X, _saveRect.Y);
                        DarkTheme.PaintOrbitingStar(g, _saveRect.Width, _saveRect.Height, _pulsePhase, cr);
                        g.Restore(saved);
                    }
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
            _showPage2SaveReady = () => { footer.Invalidate(); };
            Controls.Add(footer);

            // Pulse animation — only invalidates the single footer panel
            _pulsePhase = 0f;
            _pulseTimer = new Timer { Interval = 30 };
            _pulseTimer.Tick += (s, e) => {
                _pulsePhase += 0.08f;
                if (_pulsePhase > (float)(Math.PI * 2)) _pulsePhase -= (float)(Math.PI * 2);
                footer.Invalidate();
                // 1-2-3 flicker intro: runs for ~90 frames (2.7s at 30ms) then stops
                if (_flickerFrame < 110) {
                    _flickerFrame++;
                    if (_flickerFrame == 110 && _currentPage == 1) _showButtonStar = true;
                    if (_card1 != null && _card1.Visible) _card1.Invalidate(false);
                }
                // Invalidate visible tips for zip line animation
                if (_tipHotkey != null && _tipHotkey.Visible) _tipHotkey.Invalidate();
                if (_tipFunCallout != null && _tipFunCallout.Visible) _tipFunCallout.Invalidate();
                // Hotkey capture star spin
                if (_captureGlowLabel != null) { _captureGlowFrame++; if (_card1 != null) _card1.Invalidate(); }
                // Single mode glow + toggle glisten animation
                if (_modeGlowIndex >= 0 && _modeGlowFrame < 40) {
                    _modeGlowFrame++;
                    if (_modeGlowFrame >= 40) _modeGlowIndex = -1;
                    if (_card1 != null && _card1.Visible) _card1.Invalidate(false);
                }
                if (_tglGlistenTarget != null && _tglGlistenFrame < 35) {
                    _tglGlistenFrame++;
                    if (_tglGlistenFrame >= 35) _tglGlistenTarget = null;
                    if (_card1 != null && _card1.Visible) _card1.Invalidate(false);
                }
                if (_tipMicLock != null && _tipMicLock.Visible) _tipMicLock.Invalidate();
                // Invalidate help circles for zip line animation
                foreach (var hc in _helpCircles) if (hc.Visible) hc.Invalidate();

                // Hotkey Actuation (Wizard Page 1)
                if (_currentPage == 1 && !IsCapturingKey) {
                    bool changed = false;
                    int[] keys = { _pttKeyCode, _ptmKeyCode, _ptToggleKeyCode };
                    for (int i = 0; i < 3; i++) {
                        bool down = (keys[i] > 0) && _IsKeyHeld(keys[i]);
                        if (down != _modeActive[i]) { _modeActive[i] = down; changed = true; }
                        
                        // Actuation logic
                        if (i == 0 && down != _prevModeKeys[0]) { // PTT
                             Audio.SetMicMute(!down);
                             if (down) Logger.Info("Wizard PTT: Unmuted (Key Down)");
                             else Logger.Info("Wizard PTT: Muted (Key Up)");
                        }
                        else if (i == 1 && down != _prevModeKeys[1]) { // PTM
                             Audio.SetMicMute(down);
                             if (down) Logger.Info("Wizard PTM: Muted (Key Down)");
                             else Logger.Info("Wizard PTM: Unmuted (Key Up)");
                        }
                        else if (i == 2 && down && !_prevModeKeys[2]) { // Toggle (Press)
                             _wizMuteToggleState = !_wizMuteToggleState;
                             Audio.SetMicMute(_wizMuteToggleState);
                             Logger.Info("Wizard Toggle: " + (_wizMuteToggleState ? "Muted" : "Unmuted"));
                        }
                        _prevModeKeys[i] = down;
                    }
                    if (changed && _card1 != null) _card1.Invalidate();
                }
            };
            _pulseTimer.Start();

            // Header — consistent on both pages
            _headerPanel = new BufferedPanel { Dock = DockStyle.Top, Height = Dpi.S(180), BackColor = BG };
            _headerPanel.Paint += PaintHeader;
            Controls.Add(_headerPanel);

            // Pages
            _page1 = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = false, Padding = Dpi.Pad(16, 8, 16, 4), BackColor = BG };
            _page1.Paint += (s, e) => { PaintUnifiedStars(e.Graphics, _page1); };
            _page2 = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = false, Padding = Dpi.Pad(16, 8, 16, 4), BackColor = BG, Visible = false };
            _page2.Paint += (s, e) => { PaintUnifiedStars(e.Graphics, _page2); };
            // Shooting star animation — shared StarBackground system
            _stars = new StarBackground(() => { try {
                var p = _currentPage == 1 ? _page1 : _page2;
                if (p.Visible) p.Invalidate();
                _headerPanel.Invalidate();
                footer.Invalidate();
            } catch { } });

            // ============================================================
            // Page 1: Single card — Mic Modes (PTT/PTM/Toggle/VA)
            // ============================================================
            _card1 = MakeRoundCard(); _card1.Dock = DockStyle.Top; _card1.Height = Dpi.S(452);
            int y = 14;

            // --- PTT row: toggle + inline key ---
            _tglPtt = new ToggleSwitch { Location = Dpi.Pt(20, y + 40) };
            _tglPtt.CheckedChanged += (s2, e2) => OnWizardToggle(_tglPtt, "ptt_on", "ptt_off");
            _tglPtt.PaintParentBg = PaintCardBg; _card1.Controls.Add(_tglPtt);
            _tglPtt.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(20), PixelY = Dpi.S(y + 40), Source = _tglPtt, Card = _card1 };
              if (!_cardToggleMap.ContainsKey(_card1)) _cardToggleMap[_card1] = new List<CardToggle>(); _cardToggleMap[_card1].Add(ct); }
            _lblPttKey = new Label { Text = "Add Key", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = ACC, BackColor = INPUT_BG, Size = Dpi.Size(72, 22), TextAlign = ContentAlignment.MiddleCenter, Location = Dpi.Pt(270, y + 42) };
            _lblPttKey.Paint += (s, e) => { using (var p = new Pen(CARD_BDR)) e.Graphics.DrawRectangle(p, 0, 0, _lblPttKey.Width - 1, _lblPttKey.Height - 1); };
            _lblPttKey.MouseEnter += (s, e) => { if (!IsCapturingKey) _lblPttKey.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPttKey.MouseLeave += (s, e) => { if (!IsCapturingKey) _lblPttKey.BackColor = INPUT_BG; };
            _lblPttKey.Click += (s, e) => BeginCapture(CaptureTarget.PttKey1, _lblPttKey);
            _card1.Controls.Add(_lblPttKey);

            // --- PTM row: toggle + inline key ---
            _tglPtm = new ToggleSwitch { Location = Dpi.Pt(20, y + 90) };
            _tglPtm.CheckedChanged += (s2, e2) => OnWizardToggle(_tglPtm, "ptm_on", "ptm_off");
            _tglPtm.PaintParentBg = PaintCardBg; _card1.Controls.Add(_tglPtm);
            _tglPtm.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(20), PixelY = Dpi.S(y + 90), Source = _tglPtm, Card = _card1 };
              _cardToggleMap[_card1].Add(ct); }
            _lblPtmKey = new Label { Text = "Add Key", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = ACC, BackColor = INPUT_BG, Size = Dpi.Size(72, 22), TextAlign = ContentAlignment.MiddleCenter, Location = Dpi.Pt(270, y + 92) };
            _lblPtmKey.Paint += (s, e) => { using (var p = new Pen(CARD_BDR)) e.Graphics.DrawRectangle(p, 0, 0, _lblPtmKey.Width - 1, _lblPtmKey.Height - 1); };
            _lblPtmKey.MouseEnter += (s, e) => { if (!IsCapturingKey) _lblPtmKey.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPtmKey.MouseLeave += (s, e) => { if (!IsCapturingKey) _lblPtmKey.BackColor = INPUT_BG; };
            _lblPtmKey.Click += (s, e) => BeginCapture(CaptureTarget.PtmKey1, _lblPtmKey);
            _card1.Controls.Add(_lblPtmKey);

            // --- Toggle row: toggle + inline key ---
            _tglPtToggle = new ToggleSwitch { Location = Dpi.Pt(20, y + 140) };
            _tglPtToggle.CheckedChanged += (s2, e2) => OnWizardToggle(_tglPtToggle, "ptt_toggle_on", "ptt_toggle_off");
            _tglPtToggle.PaintParentBg = PaintCardBg; _card1.Controls.Add(_tglPtToggle);
            _tglPtToggle.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(20), PixelY = Dpi.S(y + 140), Source = _tglPtToggle, Card = _card1 };
              _cardToggleMap[_card1].Add(ct); }
            _lblPtToggleKey = new Label { Text = "Add Key", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = ACC, BackColor = INPUT_BG, Size = Dpi.Size(72, 22), TextAlign = ContentAlignment.MiddleCenter, Location = Dpi.Pt(270, y + 142) };
            _lblPtToggleKey.Paint += (s, e) => { using (var p = new Pen(CARD_BDR)) e.Graphics.DrawRectangle(p, 0, 0, _lblPtToggleKey.Width - 1, _lblPtToggleKey.Height - 1); };
            _lblPtToggleKey.MouseEnter += (s, e) => { if (!IsCapturingKey) _lblPtToggleKey.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPtToggleKey.MouseLeave += (s, e) => { if (!IsCapturingKey) _lblPtToggleKey.BackColor = INPUT_BG; };
            _lblPtToggleKey.Click += (s, e) => BeginCapture(CaptureTarget.ToggleKey1, _lblPtToggleKey);
            _card1.Controls.Add(_lblPtToggleKey);

            // --- Help circles ---
            int helpX = 350;
            _card1.Controls.Add(MakeHelpCircle(helpX, y + 42,
                "Set this to the same key you use\nin Discord, Zoom, or your game.\nAngry Audio works alongside them\n\u2014 system-wide protection.", _card1));
            _card1.Controls.Add(MakeHelpCircle(helpX, y + 92,
                "Hold this when you need to cough,\nsneeze, or step away. Your mic\nstays open the rest of the time.", _card1));
            _card1.Controls.Add(MakeHelpCircle(helpX, y + 142,
                "One tap to go silent, another to\ngo live. No key holding required\n\u2014 keeps your hands free for\ngameplay or streaming.", _card1));

            // Eye (overlay) + Speaker (sound) toggles
            MakeWizEyeToggle(y + 42, _card1, true, (v) => { if (_audio != null) _audio.PttShowOverlay = v; });
            MakeWizSpeakerToggle(y + 42, _card1, false, (v) => { if (_audio != null) _audio.PttSoundFeedback = v; });

            MakeWizEyeToggle(y + 92, _card1, true, (v) => { if (_audio != null) _audio.PtmShowOverlay = v; });
            MakeWizSpeakerToggle(y + 92, _card1, false, (v) => { if (_audio != null) _audio.PtmSoundFeedback = v; });

            MakeWizEyeToggle(y + 142, _card1, true, (v) => { if (_audio != null) _audio.PtToggleShowOverlay = v; });
            MakeWizSpeakerToggle(y + 142, _card1, false, (v) => { if (_audio != null) _audio.PtToggleSoundFeedback = v; });

            CreateTips();

            // --- Voice Activity row: toggle only (no hotkey, tuning in Options) ---
            _tglWizVA = new ToggleSwitch { Visible = false };
            _tglWizVA.CheckedChanged += (s2, e2) => {
                if (_wizToggleRejecting) return;
                if (_tglWizVA.Checked) {
                    CancelAllCaptures();
                    _wizToggleRejecting = true;
                    if (_tglPtt != null && _tglPtt.Checked) _tglPtt.Checked = false;
                    if (_tglPtm != null && _tglPtm.Checked) _tglPtm.Checked = false;
                    if (_tglPtToggle != null && _tglPtToggle.Checked) _tglPtToggle.Checked = false;
                    _wizToggleRejecting = false;
                    _pttKeyCode=0; if(_lblPttKey!=null){_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;}
                    _ptmKeyCode=0; if(_lblPtmKey!=null){_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;}
                    _ptToggleKeyCode=0; if(_lblPtToggleKey!=null){_lblPtToggleKey.Text="Add Key";_lblPtToggleKey.BackColor=INPUT_BG;_lblPtToggleKey.ForeColor=ACC;}
                    if (_audio != null) { _audio.DisablePttMode(); _audio.DisablePtmMode(); _audio.DisablePtToggleMode(); }
                    if (_audio != null) _audio.VoiceActivityEnabled = true;
                } else {
                    if (_audio != null) _audio.VoiceActivityEnabled = false;
                }
                _card1.Invalidate();
            };
            _tglWizVA.PaintParentBg = PaintCardBg; _card1.Controls.Add(_tglWizVA);
            { var ct = new CardToggle { PixelX = Dpi.S(20), PixelY = Dpi.S(y + 192), Source = _tglWizVA, Card = _card1 };
              _cardToggleMap[_card1].Add(ct); }

            // VA help circle
            _card1.Controls.Add(MakeHelpCircle(helpX, y + 194,
                "Like Discord's voice detection but\nsystem-wide. No hotkey needed \u2014\njust talk. Pairs great with AFK\nprotection on the next page.", _card1));

            // VA eye (overlay) icon only — no speaker for VA
            MakeWizEyeToggle(y + 194, _card1, true, (v) => { if (_audio != null) _audio.VoiceActivityShowOverlay = v; });

            // === VA CONFIGURATION — bottom half of card ===
            // Hidden slider backing threshold value
            _trkWizVAThreshold = new SlickSlider { Minimum = 1, Maximum = 100, Value = 15, Visible = false };
            _card1.Controls.Add(_trkWizVAThreshold);
            _trkWizVAThreshold.ValueChanged += (s2, e2) => { _card1.Invalidate(); if (_wizPtt != null) _wizPtt.SetVoiceThreshold(_trkWizVAThreshold.Value / 100f); };

            // Holdover NUD — aligned with painted "Stay open for" text
            _nudWizHoldover = new PaddedNumericUpDown {
                Minimum = 200, Maximum = 5000, Increment = 100, Value = 2000,
                Location = Dpi.Pt(200, 400), Size = Dpi.Size(70, 24),
                BackColor = INPUT_BG, ForeColor = TXT, Font = new Font("Segoe UI", 9f),
                BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center
            };
            _nudWizHoldover.Leave += (s2,e2) => { try { int v; if(int.TryParse(_nudWizHoldover.Text,out v)){v=Math.Max(200,Math.Min(5000,v));_nudWizHoldover.Value=v;} } catch { _nudWizHoldover.Value=2000; } };
            _nudWizHoldover.ValueChanged += (s2,e2) => { if (_audio != null) _audio.VoiceActivityHoldoverMs = (int)_nudWizHoldover.Value; if (_wizPtt != null) _wizPtt.SetVoiceHoldover((int)_nudWizHoldover.Value); };
            _card1.Controls.Add(_nudWizHoldover);

            // VA tuning help circle — next to holdover section
            _card1.Controls.Add(MakeHelpCircle(helpX, y + 360,
                "Prevents cutting you off between\nsentences or during short pauses.\nHigher values = more forgiving\nfor slow or thoughtful speakers.", _card1));

            // ALL text painted — zero Labels, shooting star visible everywhere
            _card1.Paint += (s, e) => {
                var g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int py = Dpi.S(14);
                // PTT section header
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString("Mic Protection", f, b, Dpi.S(20), py);
                // Gold shield badge
                {
                    float shieldSz = Dpi.S(16); float shieldX;
                    using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) shieldX = Dpi.S(20) + g.MeasureString("Mic Protection", f).Width + Dpi.S(6);
                    DarkTheme.DrawShield(g, shieldX, py, shieldSz, Color.FromArgb(218, 175, 62), true);
                    using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                    using (var b = new SolidBrush(Color.FromArgb(235, 215, 145)))
                        g.DrawString("Recommended", f, b, shieldX + shieldSz + Dpi.S(4), py + Dpi.S(1));
                }
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Pick your style, then set your hotkey below.", f, b, Dpi.S(20), py + Dpi.S(18));
                // Toggle 1: Push-to-Talk (50px row spacing, 6px extra top gap)
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Push-to-Talk", f, b, Dpi.S(68), py + Dpi.S(41));
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Silent until you hold the key to open.", f, b, Dpi.S(68), py + Dpi.S(58));
                // Toggle 2: Push-to-Mute
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Push-to-Mute", f, b, Dpi.S(68), py + Dpi.S(91));
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Open until you hold the key to silence.", f, b, Dpi.S(68), py + Dpi.S(108));
                // Toggle 3: Push-to-Toggle
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Push-to-Toggle", f, b, Dpi.S(68), py + Dpi.S(141));
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Tap to mute, tap again to unmute.", f, b, Dpi.S(68), py + Dpi.S(158));
                // Inline key hints
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(DarkTheme.Txt4)) {
                    g.DrawString("Hotkey:", f, b, Dpi.S(226), py + Dpi.S(46));
                    g.DrawString("Hotkey:", f, b, Dpi.S(226), py + Dpi.S(96));
                    g.DrawString("Hotkey:", f, b, Dpi.S(226), py + Dpi.S(146));
                }
                // Toggle 4: Voice Activity — no separator, just 52px spacing
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Voice Activity", f, b, Dpi.S(68), py + Dpi.S(193));
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Auto-unmute when you speak, re-mute when you stop.", f, b, Dpi.S(68), py + Dpi.S(210));
                // System-wide mic note
                using (var f = new Font("Segoe UI", 7f)) using (var b = new SolidBrush(Color.FromArgb(100, 180, 255)))
                    g.DrawString("Controls every mic on your system \u2014 headset, camera mic, USB devices.", f, b, Dpi.S(20), py + Dpi.S(232));

                // === Separator — modes / VA tuning ===
                int vaSepY = py + Dpi.S(250);
                using (var p = new Pen(CARD_BDR)) g.DrawLine(p, Dpi.S(20), vaSepY, _card1.Width - Dpi.S(20), vaSepY);

                // === VA TUNING SECTION ===
                int vaY = py + Dpi.S(260);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Level & Threshold", f, b, Dpi.S(20), vaY);

                bool speaking = _wizPtt != null && _wizPtt.IsVoiceActive;
                float peak = _wizPtt != null ? _wizPtt.CurrentPeakLevel : 0f;
                float threshold = _trkWizVAThreshold.Value / 100f;

                // Status pill — Speaking/Silent
                {
                    string stStr = speaking ? "Speaking" : "Silent";
                    Color pillBg = speaking ? Color.FromArgb(20, DarkTheme.Green.R, DarkTheme.Green.G, DarkTheme.Green.B) : Color.FromArgb(15, 15, 15);
                    Color dotCol = speaking ? DarkTheme.Green : Color.FromArgb(50, 50, 50);
                    Color txtCol = speaking ? Color.FromArgb(160, 255, 160) : Color.FromArgb(65, 65, 65);
                    using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold)) {
                        var ssz = g.MeasureString(stStr, f);
                        float pillW = ssz.Width + Dpi.S(22);
                        float pillH = ssz.Height + Dpi.S(4);
                        float pillX = _card1.Width - Dpi.S(20) - pillW;
                        float pillY2 = vaY + (Dpi.S(14) - pillH) / 2f;
                        using (var path = DarkTheme.RoundedRect(new Rectangle((int)pillX, (int)pillY2, (int)pillW, (int)pillH), Dpi.S(8)))
                        using (var b = new SolidBrush(pillBg)) g.FillPath(b, path);
                        if (speaking) {
                            using (var path = DarkTheme.RoundedRect(new Rectangle((int)pillX, (int)pillY2, (int)pillW, (int)pillH), Dpi.S(8)))
                            using (var p = new Pen(Color.FromArgb(40, DarkTheme.Green.R, DarkTheme.Green.G, DarkTheme.Green.B))) g.DrawPath(p, path);
                        }
                        float dSz = Dpi.S(6);
                        float dotX = pillX + Dpi.S(6);
                        float dotY = pillY2 + (pillH - dSz) / 2f;
                        if (speaking) {
                            using (var b = new SolidBrush(Color.FromArgb(40, DarkTheme.Green.R, DarkTheme.Green.G, DarkTheme.Green.B)))
                                g.FillEllipse(b, dotX - Dpi.S(2), dotY - Dpi.S(2), dSz + Dpi.S(4), dSz + Dpi.S(4));
                        }
                        using (var b = new SolidBrush(dotCol)) g.FillEllipse(b, dotX, dotY, dSz, dSz);
                        using (var b = new SolidBrush(txtCol))
                            g.DrawString(stStr, f, b, dotX + dSz + Dpi.S(4), pillY2 + Dpi.S(2));
                    }
                }

                // Threshold + Peak readout
                int readoutY = vaY + Dpi.S(18);
                using (var f = new Font("Segoe UI", 7.5f)) {
                    using (var b = new SolidBrush(Color.FromArgb(140, 140, 140)))
                        g.DrawString("Threshold: " + _trkWizVAThreshold.Value + "%", f, b, Dpi.S(20), readoutY);
                    string pkTxt = "Peak: " + ((int)(peak * 100)) + "%";
                    var pksz = g.MeasureString(pkTxt, f);
                    Color pkCol = peak >= threshold ? ACC : Color.FromArgb(100, 100, 100);
                    using (var b = new SolidBrush(pkCol))
                        g.DrawString(pkTxt, f, b, _card1.Width - Dpi.S(20) - pksz.Width, readoutY);
                }

                // ── METER BAR ──
                int mTop = readoutY + Dpi.S(16);
                int mH = Dpi.S(30);
                int mx = Dpi.S(20), mw = _card1.Width - Dpi.S(40);
                _wizMeterPixelX = mx; _wizMeterPixelY = mTop; _wizMeterPixelW = mw; _wizMeterPixelH = mH;

                // Track background
                using (var path = DarkTheme.RoundedRect(new Rectangle(mx, mTop, mw, mH), Dpi.S(4)))
                using (var b = new SolidBrush(Color.FromArgb(22, 22, 22))) g.FillPath(b, path);
                using (var b = new SolidBrush(Color.FromArgb(12, 0, 0, 0)))
                    g.FillRectangle(b, mx + 1, mTop + 1, mw - 2, Dpi.S(2));

                // Graduation marks
                for (int pct = 25; pct <= 75; pct += 25) {
                    int gx = mx + (int)(mw * pct / 100f);
                    int ga = pct == 50 ? 18 : 10;
                    using (var p = new Pen(Color.FromArgb(ga, 255, 255, 255), 1))
                        g.DrawLine(p, gx, mTop + Dpi.S(3), gx, mTop + mH - Dpi.S(3));
                }

                // Peak fill
                int fillW = Math.Max(0, (int)(mw * Math.Min(1f, peak)));
                if (fillW > 4) {
                    Color barCol = peak >= threshold ? ACC : DarkTheme.Green;
                    if (peak >= threshold) {
                        for (int gl = 0; gl < 2; gl++) {
                            using (var b = new SolidBrush(Color.FromArgb(8 - gl * 3, barCol.R, barCol.G, barCol.B)))
                                g.FillRectangle(b, mx - gl - 1, mTop - gl - 1, mw + (gl + 1) * 2, mH + (gl + 1) * 2);
                        }
                    }
                    var fillRect = new Rectangle(mx, mTop, fillW, mH);
                    using (var path = DarkTheme.RoundedRect(fillRect, Dpi.S(4))) {
                        var oldClip2 = g.Clip;
                        g.SetClip(path, CombineMode.Intersect);
                        Color topCol = Color.FromArgb(200, barCol.R, barCol.G, barCol.B);
                        Color botCol = Color.FromArgb(120, barCol.R, barCol.G, barCol.B);
                        using (var lgb = new LinearGradientBrush(new Point(0, mTop), new Point(0, mTop + mH), topCol, botCol))
                            g.FillRectangle(lgb, fillRect);
                        using (var b = new SolidBrush(Color.FromArgb(35, 255, 255, 255)))
                            g.FillRectangle(b, mx, mTop + 1, fillW, Dpi.S(2));
                        g.Clip = oldClip2;
                    }
                }

                // Threshold handle
                int thX = mx + (int)(mw * threshold);
                using (var b = new SolidBrush(Color.FromArgb(25, 255, 255, 255)))
                    g.FillRectangle(b, thX - Dpi.S(3), mTop, Dpi.S(6), mH);
                using (var p = new Pen(Color.FromArgb(240, 255, 255, 255), Dpi.PenW(2))) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, thX, mTop + Dpi.S(3), thX, mTop + mH - Dpi.S(3));
                }
                float hCy = mTop + mH / 2f;
                int thr = Dpi.S(6);
                using (var b = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                    g.FillEllipse(b, thX - thr + 1, hCy - thr + 1, thr * 2, thr * 2);
                if (_wizMeterDragging) {
                    int glR = thr + Dpi.S(3);
                    using (var b = new SolidBrush(Color.FromArgb(50, ACC.R, ACC.G, ACC.B)))
                        g.FillEllipse(b, thX - glR, hCy - glR, glR * 2, glR * 2);
                }
                using (var b = new SolidBrush(Color.White))
                    g.FillEllipse(b, thX - thr, hCy - thr, thr * 2, thr * 2);
                int dotR = Dpi.S(2);
                using (var b = new SolidBrush(ACC))
                    g.FillEllipse(b, thX - dotR, hCy - dotR, dotR * 2, dotR * 2);

                // Scale labels below meter
                using (var f = new Font("Segoe UI", 6.5f)) using (var b = new SolidBrush(Color.FromArgb(55, 55, 55))) {
                    g.DrawString("0", f, b, mx, mTop + mH + Dpi.S(1));
                    var sz50 = g.MeasureString("50", f);
                    g.DrawString("50", f, b, mx + mw / 2f - sz50.Width / 2f, mTop + mH + Dpi.S(1));
                    var sz100 = g.MeasureString("100", f);
                    g.DrawString("100", f, b, mx + mw - sz100.Width, mTop + mH + Dpi.S(1));
                }

                // ── Separator — meter / holdover ──
                int holdSepY = mTop + mH + Dpi.S(18);
                using (var p = new Pen(CARD_BDR)) g.DrawLine(p, Dpi.S(20), holdSepY, _card1.Width - Dpi.S(20), holdSepY);

                // ── HOLDOVER SECTION ──
                int hY = holdSepY + Dpi.S(8);
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Holdover Delay", f, b, Dpi.S(20), hY);
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Keeps the mic open briefly after you stop speaking.", f, b, Dpi.S(20), hY + Dpi.S(18));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Stay open for", f, b, Dpi.S(68), hY + Dpi.S(42));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("ms after speech", f, b, Dpi.S(278), hY + Dpi.S(42));
                using (var f = new Font("Segoe UI", 7f)) using (var b = new SolidBrush(DarkTheme.Txt4))
                    g.DrawString("2000ms recommended. Lower = responsive but choppy. Higher = smooth but slower.", f, b, Dpi.S(20), hY + Dpi.S(64));

                // === 1-2-3-4 intro glow — soft bloom on each MODE SEGMENT ===
                if (_flickerFrame > 0 && _flickerFrame < 110) {
                    int[] segTops = { 36, 86, 136, 188 };
                    int[] segBots = { 72, 122, 172, 224 };
                    int[] starts = { 10, 35, 60, 80 };
                    int dur = 30;
                    // On replay (Back from page 2), only glow modes that are active/have key
                    bool[] showSeg = { true, true, true, true };
                    if (_pttKeyCode > 0 || _ptmKeyCode > 0 || _ptToggleKeyCode > 0 || (_tglWizVA != null && _tglWizVA.Checked)) {
                        showSeg[0] = _pttKeyCode > 0;
                        showSeg[1] = _ptmKeyCode > 0;
                        showSeg[2] = _ptToggleKeyCode > 0;
                        showSeg[3] = _tglWizVA != null && _tglWizVA.Checked;
                    }
                    for (int i = 0; i < 4; i++) {
                        if (!showSeg[i]) continue;
                        int localF = _flickerFrame - starts[i];
                        if (localF < 0 || localF >= dur) continue;
                        float t = (float)Math.Sin(localF * Math.PI / dur);
                        int segTop = py + Dpi.S(segTops[i]);
                        int segBot = py + Dpi.S(segBots[i]);
                        int segLeft = Dpi.S(10);
                        int segRight = _card1.Width - Dpi.S(10);
                        for (int layer = 0; layer < 3; layer++) {
                            int expand = Dpi.S(layer * 3);
                            int alpha = (int)(t * (45 - layer * 12));
                            if (alpha <= 0) continue;
                            using (var brush = new SolidBrush(Color.FromArgb(alpha, ACC.R, ACC.G, ACC.B)))
                                g.FillRectangle(brush, segLeft - expand, segTop - expand, segRight - segLeft + expand * 2, segBot - segTop + expand * 2);
                        }
                    }
                }

                // === Single mode glow — plays when a hotkey is captured ===
                if (_modeGlowIndex >= 0 && _modeGlowFrame > 0 && _modeGlowFrame <= 40) {
                    int[] segTops2 = { 36, 86, 136 };
                    int[] segBots2 = { 72, 122, 172 };
                    if (_modeGlowIndex < 3) {
                        int idx = _modeGlowIndex;
                        float t2 = (float)Math.Sin(_modeGlowFrame * Math.PI / 40f);
                        int sTop = py + Dpi.S(segTops2[idx]);
                        int sBot = py + Dpi.S(segBots2[idx]);
                        int sLeft = Dpi.S(10);
                        int sRight = _card1.Width - Dpi.S(10);
                        for (int layer = 0; layer < 3; layer++) {
                            int expand = Dpi.S(layer * 3);
                            int alpha = (int)(t2 * (50 - layer * 14));
                            if (alpha <= 0) continue;
                            using (var brush = new SolidBrush(Color.FromArgb(alpha, ACC.R, ACC.G, ACC.B)))
                                g.FillRectangle(brush, sLeft - expand, sTop - expand, sRight - sLeft + expand * 2, sBot - sTop + expand * 2);
                        }
                    }
                }

                // === Toggle glisten — fast spin + sweep after hotkey auto-enables toggle ===
                if (_tglGlistenTarget != null && _tglGlistenFrame > 0 && _tglGlistenFrame <= 35) {
                    float ct = _tglGlistenFrame / 35f;
                    float tglW = _tglGlistenTarget.Width, tglH = _tglGlistenTarget.Height;
                    float tglCx = _tglGlistenTarget.Left + tglW / 2f;
                    float tglCy = _tglGlistenTarget.Top + tglH / 2f;
                    // Fast spinning ring
                    float ringR = Dpi.S(13) + (float)Math.Sin(ct * Math.PI) * Dpi.S(3);
                    float baseAngle = _tglGlistenFrame * 50f * (float)(Math.PI / 180.0);
                    float ringFade = ct < 0.8f ? 1f : (1f - ct) / 0.2f;
                    for (int rd = 0; rd < 4; rd++) {
                        float dotAngle = baseAngle + rd * (float)(Math.PI * 2.0 / 4);
                        for (int tail = 0; tail < 4; tail++) {
                            float tailAngle = dotAngle - tail * 0.22f;
                            float tx = tglCx + (float)Math.Cos(tailAngle) * ringR;
                            float ty = tglCy + (float)Math.Sin(tailAngle) * ringR;
                            float tr = Dpi.S(2) * (1f - tail * 0.2f);
                            int ta2 = (int)(ringFade * (240 - tail * 55));
                            if (ta2 <= 0) continue;
                            Color dc = tail == 0 ? Color.FromArgb(ta2, 255, 255, 255) : Color.FromArgb(ta2, 100, 200, 255);
                            using (var br = new SolidBrush(dc))
                                g.FillEllipse(br, tx - tr, ty - tr, tr*2, tr*2);
                        }
                    }
                    // Glisten sweep across toggle
                    if (ct > 0.2f && ct < 0.8f) {
                        float sweepT = (ct - 0.2f) / 0.6f;
                        float sweepX = _tglGlistenTarget.Left - Dpi.S(3) + sweepT * (tglW + Dpi.S(6));
                        float sweepW2 = Dpi.S(5);
                        int sweepA = (int)((sweepT < 0.5f ? sweepT / 0.5f : (1f - sweepT) / 0.5f) * 200);
                        if (sweepA > 0) {
                            using (var lgb = new System.Drawing.Drawing2D.LinearGradientBrush(
                                new PointF(sweepX - sweepW2, 0), new PointF(sweepX + sweepW2, 0),
                                Color.FromArgb(0, 255, 255, 255), Color.FromArgb(sweepA, 255, 255, 255))) {
                                var blend = new System.Drawing.Drawing2D.ColorBlend(3);
                                blend.Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(sweepA, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                                blend.Positions = new[] { 0f, 0.5f, 1f };
                                lgb.InterpolationColors = blend;
                                g.FillRectangle(lgb, sweepX - sweepW2, _tglGlistenTarget.Top - Dpi.S(1), sweepW2 * 2, tglH + Dpi.S(2));
                            }
                        }
                    }
                }

                // === Hotkey held highlight — draws a thick glowing border and soft inner bloom ===
                for (int i = 0; i < 3; i++) {
                    if (!_modeActive[i]) continue;
                    int[] segTops3 = { 36, 86, 136 };
                    int[] segBots3 = { 72, 122, 172 };
                    int sTop = py + Dpi.S(segTops3[i]);
                    int sBot = py + Dpi.S(segBots3[i]);
                    int sLeft = Dpi.S(10);
                    int sRight = _card1.Width - Dpi.S(10);
                    DarkTheme.PaintBreathingGlow(g, new Rectangle(sLeft, sTop, sRight - sLeft, sBot - sTop), ACC, Dpi.S(6));
                }

                // === VA row highlight — breathing accent glow ===
                if (_tglWizVA != null && _tglWizVA.Checked && _wizPtt != null && _wizPtt.IsVoiceActive) {
                    int vaTop = py + Dpi.S(188);
                    int vaBot = py + Dpi.S(224);
                    int vaLeft = Dpi.S(10);
                    int vaRight = _card1.Width - Dpi.S(10);
                    DarkTheme.PaintBreathingGlow(g, new Rectangle(vaLeft, vaTop, vaRight - vaLeft, vaBot - vaTop), ACC, Dpi.S(6));
                }

                // Card icons — eye/speaker painted directly, zero compositing
                List<CardIcon> icons;
                if (_cardIconMap.TryGetValue(_card1, out icons)) {
                    foreach (var icon in icons) icon.Paint(g, ACC);
                }
                // Card toggles — painted directly, real ToggleSwitch hidden
                List<CardToggle> ctgls;
                if (_cardToggleMap.TryGetValue(_card1, out ctgls)) {
                    foreach (var ct in ctgls) ct.Paint(g, ACC);
                }
            };

            var tip1 = MakeTipPanel();
            // Mouse handlers for card icons + card toggles + meter drag
            _card1.MouseDown += (s, e2) => {
                if (e2.Button != MouseButtons.Left) return;
                if (_wizMeterPixelW > 0 && e2.X >= _wizMeterPixelX && e2.X <= _wizMeterPixelX + _wizMeterPixelW &&
                    e2.Y >= _wizMeterPixelY - Dpi.S(8) && e2.Y <= _wizMeterPixelY + _wizMeterPixelH + Dpi.S(8)) {
                    _wizMeterDragging = true;
                    float pct = Math.Max(0.01f, Math.Min(1f, (float)(e2.X - _wizMeterPixelX) / Math.Max(1, _wizMeterPixelW)));
                    _trkWizVAThreshold.Value = Math.Max(1, Math.Min(100, (int)(pct * 100)));
                    _card1.Invalidate(); return;
                }
            };
            _card1.MouseUp += (s, e2) => {
                if (_wizMeterDragging) {
                    _wizMeterDragging = false;
                    if (_audio != null) _audio.VoiceActivityThreshold = _trkWizVAThreshold.Value;
                    _card1.Invalidate();
                }
            };
            _card1.MouseClick += (s, e2) => {
                if (_wizMeterPixelW > 0 && e2.X >= _wizMeterPixelX && e2.X <= _wizMeterPixelX + _wizMeterPixelW &&
                    e2.Y >= _wizMeterPixelY - Dpi.S(8) && e2.Y <= _wizMeterPixelY + _wizMeterPixelH + Dpi.S(8))
                    return; // meter handled in MouseDown
                List<CardToggle> ctgls2;
                if (_cardToggleMap.TryGetValue(_card1, out ctgls2)) {
                    foreach (var ct in ctgls2) {
                        if (ct.HitTest(e2.Location)) { ct.Click(); return; }
                    }
                }
                List<CardIcon> icons2;
                if (_cardIconMap.TryGetValue(_card1, out icons2)) {
                    foreach (var icon in icons2) {
                        if (icon.HitTest(e2.Location)) { icon.Toggle(); break; }
                    }
                }
            };
            _card1.MouseMove += (s, e2) => {
                bool anyHover = false;
                if (_wizMeterDragging) {
                    float pct = Math.Max(0.01f, Math.Min(1f, (float)(e2.X - _wizMeterPixelX) / Math.Max(1, _wizMeterPixelW)));
                    _trkWizVAThreshold.Value = Math.Max(1, Math.Min(100, (int)(pct * 100)));
                    _card1.Invalidate();
                }
                if (_wizMeterPixelW > 0 && e2.X >= _wizMeterPixelX && e2.X <= _wizMeterPixelX + _wizMeterPixelW &&
                    e2.Y >= _wizMeterPixelY - Dpi.S(8) && e2.Y <= _wizMeterPixelY + _wizMeterPixelH + Dpi.S(8))
                    anyHover = true;
                List<CardIcon> icons2;
                if (_cardIconMap.TryGetValue(_card1, out icons2)) {
                    foreach (var icon in icons2) {
                        bool wasHover = icon.Hover;
                        icon.Hover = icon.HitTest(e2.Location);
                        if (icon.Hover) anyHover = true;
                        if (wasHover != icon.Hover) _card1.Invalidate();
                    }
                }
                List<CardToggle> ctgls2;
                if (_cardToggleMap.TryGetValue(_card1, out ctgls2)) {
                    foreach (var ct in ctgls2) {
                        bool wasH = ct.Hover;
                        ct.Hover = ct.HitTest(e2.Location);
                        if (ct.Hover) anyHover = true;
                        if (wasH != ct.Hover) _card1.Invalidate();
                    }
                }
                _card1.Cursor = anyHover ? Cursors.Hand : Cursors.Default;
            };
            _card1.MouseLeave += (s, e2) => {
                if (_wizMeterDragging) {
                    _wizMeterDragging = false;
                    if (_audio != null) _audio.VoiceActivityThreshold = _trkWizVAThreshold.Value;
                }
                List<CardIcon> icons2;
                if (_cardIconMap.TryGetValue(_card1, out icons2)) {
                    foreach (var icon in icons2) { if (icon.Hover) { icon.Hover = false; _card1.Invalidate(); } }
                }
                List<CardToggle> ctgls2;
                if (_cardToggleMap.TryGetValue(_card1, out ctgls2)) {
                    foreach (var ct in ctgls2) { if (ct.Hover) { ct.Hover = false; _card1.Invalidate(); } }
                }
            };
            var sp1 = new BufferedPanel { Dock = DockStyle.Top, Height = Dpi.S(4), BackColor = BG };
            sp1.Paint += (s, e) => { PaintUnifiedStars(e.Graphics, sp1); };
            _page1.Controls.Add(tip1); _page1.Controls.Add(sp1); _page1.Controls.Add(_card1);

            // ============================================================
            // Page 2: Single unified card — Volume Lock + AFK + General
            // ============================================================
            _card2 = MakeRoundCard(); _card2.Dock = DockStyle.Top; _card2.Height = Dpi.S(452);
            y = 14;

            // Volume Lock header takes y=14..48, Microphone sub-section starts at y=50
            int micY = 50;

            // Interactive controls ONLY — all text is painted below
            _micSlider = new SlickSlider { Minimum = 0, Maximum = 100, Value = _audio != null ? _audio.MicLockVolume : 100, Location = Dpi.Pt(80, micY + 30), Size = Dpi.Size(260, 30) };
            
            _micSlider.ValueChanged += (s2, e2) => { _card2.Invalidate(); };
            _micSlider.DragCompleted += (s2, e2) => { if (_tglMicEnf.Checked) try { Audio.SetMicVolume(_micSlider.Value); } catch { } };
            _micSlider.PaintParentBg = PaintCardBg; _card2.Controls.Add(_micSlider);
            _micSlider.Visible = false;
            { var cs = new CardSlider { PixelX = Dpi.S(80), PixelY = Dpi.S(micY + 30), PixelW = Dpi.S(260), PixelH = Dpi.S(30), Source = _micSlider, Card = _card2 };
              if (!_cardSliderMap.ContainsKey(_card2)) _cardSliderMap[_card2] = new List<CardSlider>(); _cardSliderMap[_card2].Add(cs); }

            _tglMicEnf = new ToggleSwitch { Checked = true, Location = Dpi.Pt(20, micY + 78) };
            
            _tglMicEnf.CheckedChanged += (s2, e2) => {
                if (_heroResetting) return;
                if (_tglMicEnf.Checked) {
                    try { _micPreLockVol = (int)Audio.GetMicVolume(); } catch { _micPreLockVol = -1; }
                    if (_sliderRestoreMicTimer != null) { _sliderRestoreMicTimer.Stop(); _sliderRestoreMicTimer.Dispose(); _sliderRestoreMicTimer = null; }
                    try { Audio.SetMicVolume(_micSlider.Value); } catch { }
                    FlashApplied(true, "Mic locked at " + _micSlider.Value + "%.");
                   
                } else {
                    FlashApplied(true, "Mic lock disabled.");
                    AnimateWizardSliderRestore(true);
                }
                if (_audio != null) { _audio.MicLockVolume = _micSlider.Value; _audio.MicLockEnabled = _tglMicEnf.Checked; }
            };
            _tglMicEnf.PaintParentBg = PaintCardBg; _card2.Controls.Add(_tglMicEnf);
            _tglMicEnf.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(20), PixelY = Dpi.S(micY + 78), Source = _tglMicEnf, Card = _card2 };
              if (!_cardToggleMap.ContainsKey(_card2)) _cardToggleMap[_card2] = new List<CardToggle>(); _cardToggleMap[_card2].Add(ct); }

            int spkY = micY + 118;
            int spkSliderVal = 50;
            if (_audio != null && _audio.SpeakerLockEnabled) spkSliderVal = _audio.SpeakerLockVolume;
            else { try { spkSliderVal = Math.Max(0, Math.Min(100, (int)Audio.GetSpeakerVolume())); } catch { } }
            _spkSlider = new SlickSlider { Minimum = 0, Maximum = 100, Value = spkSliderVal, Location = Dpi.Pt(80, spkY + 30), Size = Dpi.Size(260, 30) };
            
            _spkSlider.ValueChanged += (s2, e2) => { _card2.Invalidate(); };
            _spkSlider.DragCompleted += (s2, e2) => { if (_tglSpkEnf.Checked) try { Audio.SetSpeakerVolume(_spkSlider.Value); } catch { } };
            _spkSlider.PaintParentBg = PaintCardBg; _card2.Controls.Add(_spkSlider);
            _spkSlider.Visible = false;
            { var cs = new CardSlider { PixelX = Dpi.S(80), PixelY = Dpi.S(spkY + 30), PixelW = Dpi.S(260), PixelH = Dpi.S(30), Source = _spkSlider, Card = _card2 };
              _cardSliderMap[_card2].Add(cs); }

            _tglSpkEnf = new ToggleSwitch { Location = Dpi.Pt(20, spkY + 78) };
            
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
                if (_audio != null) { _audio.SpeakerLockVolume = _spkSlider.Value; _audio.SpeakerLockEnabled = _tglSpkEnf.Checked; }
            };
            _tglSpkEnf.PaintParentBg = PaintCardBg; _card2.Controls.Add(_tglSpkEnf);
            _tglSpkEnf.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(20), PixelY = Dpi.S(spkY + 78), Source = _tglSpkEnf, Card = _card2 };
              _cardToggleMap[_card2].Add(ct); }

            // Help circles for page 2
            _card2.Controls.Add(MakeHelpCircle(350, micY + 80,
                "Zoom, Discord, and games silently\nchange your mic volume mid-call.\nThis locks it exactly where you\nset it \u2014 they can't touch it.", _card2));
            _card2.Controls.Add(MakeHelpCircle(350, spkY + 80,
                "Games and media apps love to max\nout your speakers without asking.\nThis stops them cold \u2014 your volume\nstays exactly where you want it.", _card2));

            // AFK Protection section — between Volume Lock and General
            int afkY = 286;
            _tglAfkMic = new ToggleSwitch { Checked = false, Location = Dpi.Pt(20, afkY + 36) };
            _tglAfkMic.CheckedChanged += (s2, e2) => {
                if (_audio != null) { _audio.AfkMicSec = (int)_nudAfkMic.Value; _audio.AfkMicEnabled = _tglAfkMic.Checked; }
            };
            _tglAfkMic.PaintParentBg = PaintCardBg; _card2.Controls.Add(_tglAfkMic);
            _tglAfkMic.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(20), PixelY = Dpi.S(afkY + 36), Source = _tglAfkMic, Card = _card2 };
              _cardToggleMap[_card2].Add(ct); }

            _nudAfkMic = new PaddedNumericUpDown { Minimum = 5, Maximum = 3600, Value = 60, Location = Dpi.Pt(200, afkY + 34), Size = Dpi.Size(60, 24), BackColor = INPUT_BG, ForeColor = TXT, Font = new Font("Segoe UI", 9f), BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center };
            _nudAfkMic.Leave += (s2,e2) => { try { int v; if(int.TryParse(_nudAfkMic.Text,out v)){v=Math.Max(5,Math.Min(3600,v));_nudAfkMic.Value=v;} } catch { _nudAfkMic.Value=60; } };
            _card2.Controls.Add(_nudAfkMic);

            _tglAfkSpk = new ToggleSwitch { Location = Dpi.Pt(20, afkY + 64) };
            _tglAfkSpk.CheckedChanged += (s2, e2) => { if (_audio != null) { _audio.AfkSpeakerSec = (int)_nudAfkSpk.Value; _audio.AfkSpeakerEnabled = _tglAfkSpk.Checked; } };
            _tglAfkSpk.PaintParentBg = PaintCardBg; _card2.Controls.Add(_tglAfkSpk);
            _tglAfkSpk.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(20), PixelY = Dpi.S(afkY + 64), Source = _tglAfkSpk, Card = _card2 };
              _cardToggleMap[_card2].Add(ct); }

            _nudAfkSpk = new PaddedNumericUpDown { Minimum = 5, Maximum = 3600, Value = 60, Location = Dpi.Pt(200, afkY + 62), Size = Dpi.Size(60, 24), BackColor = INPUT_BG, ForeColor = TXT, Font = new Font("Segoe UI", 9f), BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center };
            _nudAfkSpk.Leave += (s2,e2) => { try { int v; if(int.TryParse(_nudAfkSpk.Text,out v)){v=Math.Max(5,Math.Min(3600,v));_nudAfkSpk.Value=v;} } catch { _nudAfkSpk.Value=60; } };
            _card2.Controls.Add(_nudAfkSpk);

            // AFK help circle
            _card2.Controls.Add(MakeHelpCircle(350, afkY + 64,
                "Forgot to mute before walking to\nthe kitchen? This has your back.\nKeyboard or mouse movement\ncancels it automatically.", _card2));

            // General section toggles — below AFK
            int gy = 388; // General section start Y
            _tglStartup = new ToggleSwitch { Location = Dpi.Pt(20, gy + 36), Checked = true };
            _tglStartup.CheckedChanged += (s2, e2) => { if (_audio != null) _audio.StartWithWindows = _tglStartup.Checked; };
            _tglStartup.PaintParentBg = PaintCardBg; _card2.Controls.Add(_tglStartup);
            _tglStartup.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(20), PixelY = Dpi.S(gy + 36), Source = _tglStartup, Card = _card2 };
              _cardToggleMap[_card2].Add(ct); }

            // Notification toggles — defaults only, no UI (removed from wizard)
            _tglNotifyCorr = new ToggleSwitch { Checked = true };
            _tglNotifyDev = new ToggleSwitch { Checked = true };
            // Mic lock hero animation painted in card2.Paint below

            // ALL text painted — zero Labels, shooting star visible everywhere
            _card2.Paint += (s, e) => {
                var g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int py = Dpi.S(14);
                // Volume Lock section header + gold shield badge
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString("Volume Lock", f, b, Dpi.S(20), py);
                {
                    float shieldSz = Dpi.S(16);
                    float shieldX;
                    using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) shieldX = Dpi.S(20) + g.MeasureString("Volume Lock", f).Width + Dpi.S(6);
                    float shieldY = py + Dpi.S(0);
                    DarkTheme.DrawShield(g, shieldX, shieldY, shieldSz, Color.FromArgb(218, 175, 62), true);
                    using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                    using (var b = new SolidBrush(Color.FromArgb(235, 215, 145)))
                        g.DrawString("Best Protection", f, b, shieldX + shieldSz + Dpi.S(4), py + Dpi.S(1));
                }
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Lock your levels so apps can\u2019t change them.", f, b, Dpi.S(20), py + Dpi.S(18));
                // -- Microphone section --
                int micPy = Dpi.S(50);
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Microphone", f, b, Dpi.S(20), micPy);
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString(_micNameText, f, b, Dpi.S(20), micPy + Dpi.S(16));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT2))
                    g.DrawString("Lock at", f, b, Dpi.S(20), micPy + Dpi.S(34));
                using (var f = new Font("Segoe UI", 9.5f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString(_micSlider.Value + "%", f, b, Dpi.S(348), micPy + Dpi.S(34));
                using (var f = new Font("Segoe UI", 8f)) using (var b = new SolidBrush(_micCurColor))
                    g.DrawString(_micCurText, f, b, Dpi.S(20), micPy + Dpi.S(58));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT2))
                    g.DrawString("Lock mic volume", f, b, Dpi.S(68), micPy + Dpi.S(79));
                // Separator — mic/speaker
                int sepY = micPy + Dpi.S(108);
                using (var p = new Pen(CARD_BDR)) g.DrawLine(p, Dpi.S(20), sepY, _card2.Width - Dpi.S(20), sepY);
                // -- Speakers section -- with green shield badge
                int spkPy = micPy + Dpi.S(118);
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Speakers", f, b, Dpi.S(20), spkPy);
                {
                    float shieldSz = Dpi.S(14);
                    float shieldX;
                    using (var f = new Font("Segoe UI", 9f, FontStyle.Bold)) shieldX = Dpi.S(20) + g.MeasureString("Speakers", f).Width + Dpi.S(6);
                    float shieldY = spkPy + Dpi.S(1);
                    DarkTheme.DrawShield(g, shieldX, shieldY, shieldSz, DarkTheme.Green, true);
                    using (var f = new Font("Segoe UI", 7f, FontStyle.Bold))
                    using (var b = new SolidBrush(Color.FromArgb(140, 220, 140)))
                        g.DrawString("Extra Protection", f, b, shieldX + shieldSz + Dpi.S(4), spkPy + Dpi.S(2));
                }
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString(_spkNameText, f, b, Dpi.S(20), spkPy + Dpi.S(16));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT2))
                    g.DrawString("Lock at", f, b, Dpi.S(20), spkPy + Dpi.S(34));
                using (var f = new Font("Segoe UI", 9.5f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString(_spkSlider.Value + "%", f, b, Dpi.S(348), spkPy + Dpi.S(34));
                using (var f = new Font("Segoe UI", 8f)) using (var b = new SolidBrush(_spkCurColor))
                    g.DrawString(_spkCurText, f, b, Dpi.S(20), spkPy + Dpi.S(58));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT2))
                    g.DrawString("Lock speaker volume", f, b, Dpi.S(68), spkPy + Dpi.S(79));

                // === Separator — enforcement / AFK ===
                int afkSepY = Dpi.S(276);
                using (var p = new Pen(CARD_BDR)) g.DrawLine(p, Dpi.S(20), afkSepY, _card2.Width - Dpi.S(20), afkSepY);

                // -- AFK Protection section --
                int afkPy = Dpi.S(286);
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString("AFK Protection", f, b, Dpi.S(20), afkPy);
                {
                    float shieldSz = Dpi.S(14); float shieldX;
                    using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) shieldX = Dpi.S(20) + g.MeasureString("AFK Protection", f).Width + Dpi.S(6);
                    DarkTheme.DrawShield(g, shieldX, afkPy + Dpi.S(1), shieldSz, DarkTheme.Green, true);
                    using (var f = new Font("Segoe UI", 7f, FontStyle.Bold))
                    using (var b = new SolidBrush(Color.FromArgb(140, 220, 140)))
                        g.DrawString("Extra Safety", f, b, shieldX + shieldSz + Dpi.S(4), afkPy + Dpi.S(2));
                }
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Auto-mute when you walk away from your desk.", f, b, Dpi.S(20), afkPy + Dpi.S(18));
                // Mic AFK row
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Mute mic after", f, b, Dpi.S(68), afkPy + Dpi.S(38));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("sec", f, b, Dpi.S(266), afkPy + Dpi.S(38));
                // Speaker AFK row
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT))
                    g.DrawString("Mute speakers after", f, b, Dpi.S(68), afkPy + Dpi.S(66));
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("sec", f, b, Dpi.S(266), afkPy + Dpi.S(66));

                // === Separator — AFK / General ===
                int genSepY = Dpi.S(378);
                using (var p = new Pen(CARD_BDR)) g.DrawLine(p, Dpi.S(20), genSepY, _card2.Width - Dpi.S(20), genSepY);

                // -- General section --
                int genY = Dpi.S(388);
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) using (var b = new SolidBrush(ACC))
                    g.DrawString("General", f, b, Dpi.S(20), genY);
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("Startup and alerts.", f, b, Dpi.S(20), genY + Dpi.S(18));
                // Startup toggle at gy+36=424
                using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(TXT2))
                    g.DrawString("Launch when Windows starts", f, b, Dpi.S(68), genY + Dpi.S(38));

                // === MIC LOCK HERO — shimmer + glisten + toggle activation ===
                if (_micHeroFrame > 0 && _micHeroFrame <= 75) {
                    int pad = Dpi.S(8);
                    int boxL = pad, boxT = Dpi.S(48), boxR = _card2.Width - pad, boxB = Dpi.S(156);
                    int boxW = boxR - boxL, boxH = boxB - boxT;

                    // Phase 1 (frames 1-30): Shimmer sweeps across mic section left-to-right
                    if (_micHeroFrame <= 30) {
                        float t = _micHeroFrame / 30f;
                        float sweepX = boxL + t * boxW * 1.2f - boxW * 0.1f;
                        float sweepW = Dpi.S(30);
                        float intensity = (float)Math.Sin(t * Math.PI); // peaks in middle
                        int sweepA = (int)(intensity * 100);
                        if (sweepA > 0) {
                            using (var lgb = new System.Drawing.Drawing2D.LinearGradientBrush(
                                new PointF(sweepX - sweepW, 0), new PointF(sweepX + sweepW, 0),
                                Color.FromArgb(0, 255, 255, 255), Color.FromArgb(sweepA, 200, 240, 255))) {
                                var blend = new System.Drawing.Drawing2D.ColorBlend(3);
                                blend.Colors = new[] { Color.FromArgb(0, 200, 240, 255), Color.FromArgb(sweepA, 255, 255, 255), Color.FromArgb(0, 200, 240, 255) };
                                blend.Positions = new[] { 0f, 0.5f, 1f };
                                lgb.InterpolationColors = blend;
                                g.FillRectangle(lgb, boxL, boxT, boxW, boxH);
                            }
                        }
                        // Thin border glow builds up
                        int borderA = (int)(t * 60);
                        using (var p = new Pen(Color.FromArgb(borderA, 80, 200, 255), Dpi.S(1)))
                            g.DrawRectangle(p, boxL, boxT, boxW, boxH);
                    }

                    // Phase 2 (frames 28-45): Flash + toggle ON + border pulse
                    if (_micHeroFrame > 28 && _micHeroFrame <= 45) {
                        float t = (_micHeroFrame - 28) / 17f;

                        // Toggle ON at frame 32 — visual only, backend already enabled on page entry
                        if (_micHeroFrame == 32 && !_tglMicEnf.Checked) {
                            _tglMicEnf.Checked = true;
                        }

                        // Quick bright flash
                        float flashI = t < 0.2f ? t / 0.2f : (1f - t) / 0.8f;
                        int fa = (int)(flashI * 80);
                        if (fa > 0) {
                            using (var br = new SolidBrush(Color.FromArgb(fa, 180, 235, 255)))
                                g.FillRectangle(br, boxL, boxT, boxW, boxH);
                        }

                        // Border glow fading
                        int ba = (int)((1f - t) * 80);
                        for (int layer = 0; layer < 2; layer++) {
                            int expand = Dpi.S(layer + 1);
                            int la = Math.Max(0, ba - layer * 25);
                            using (var p = new Pen(Color.FromArgb(la, 80, 200, 255), Dpi.S(2)))
                                g.DrawRectangle(p, boxL - expand, boxT - expand, boxW + expand*2, boxH + expand*2);
                        }
                    }

                    // Phase 3 (frames 32-65): Toggle glisten — fast spin + sweep
                    if (_micHeroFrame > 32 && _micHeroFrame <= 65) {
                        float ct = (_micHeroFrame - 32) / 33f;
                        float tglW = Dpi.S(40), tglH = Dpi.S(20);
                        float tglCx = _tglMicEnf.Left + tglW / 2f;
                        float tglCy = _tglMicEnf.Top + tglH / 2f;

                        // FAST spinning ring — glisten speed
                        float ringR = Dpi.S(13) + (float)Math.Sin(ct * Math.PI) * Dpi.S(3);
                        float baseAngle = (_micHeroFrame - 32) * 50f * (float)(Math.PI / 180.0);
                        float ringFade = ct < 0.8f ? 1f : (1f - ct) / 0.2f;
                        int ringDots = 4;
                        for (int rd = 0; rd < ringDots; rd++) {
                            float dotAngle = baseAngle + rd * (float)(Math.PI * 2.0 / ringDots);
                            for (int tail = 0; tail < 4; tail++) {
                                float tailAngle = dotAngle - tail * 0.22f;
                                float tx = tglCx + (float)Math.Cos(tailAngle) * ringR;
                                float ty = tglCy + (float)Math.Sin(tailAngle) * ringR;
                                float tr = Dpi.S(2) * (1f - tail * 0.2f);
                                int ta2 = (int)(ringFade * (255 - tail * 55));
                                if (ta2 <= 0) continue;
                                Color dc = tail == 0 ? Color.FromArgb(ta2, 255, 255, 255) : Color.FromArgb(ta2, 100, 200, 255);
                                using (var br = new SolidBrush(dc))
                                    g.FillEllipse(br, tx - tr, ty - tr, tr*2, tr*2);
                            }
                        }

                        // White glisten sweep across toggle
                        if (ct > 0.15f && ct < 0.75f) {
                            float sweepT = (ct - 0.15f) / 0.6f;
                            float sweepX = _tglMicEnf.Left - Dpi.S(3) + sweepT * (tglW + Dpi.S(6));
                            float sweepW = Dpi.S(5);
                            int sweepA = (int)((sweepT < 0.5f ? sweepT / 0.5f : (1f - sweepT) / 0.5f) * 220);
                            if (sweepA > 0) {
                                using (var lgb = new System.Drawing.Drawing2D.LinearGradientBrush(
                                    new PointF(sweepX - sweepW, 0), new PointF(sweepX + sweepW, 0),
                                    Color.FromArgb(0, 255, 255, 255), Color.FromArgb(sweepA, 255, 255, 255))) {
                                    var blend = new System.Drawing.Drawing2D.ColorBlend(3);
                                    blend.Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(sweepA, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                                    blend.Positions = new[] { 0f, 0.5f, 1f };
                                    lgb.InterpolationColors = blend;
                                    g.FillRectangle(lgb, sweepX - sweepW, _tglMicEnf.Top - Dpi.S(1), sweepW * 2, tglH + Dpi.S(2));
                                }
                            }
                        }
                    }

                    // Animation complete
                }

                // Card toggles — painted directly on card2
                // During hero animation, temporarily override mic lock toggle to paint as OFF
                bool micEnfWasChecked = false;
                if (_heroToggleVisualOff && _tglMicEnf.Checked) {
                    micEnfWasChecked = true;
                    _heroResetting = true; _tglMicEnf.Checked = false; _heroResetting = false;
                }
                List<CardToggle> ctgls;
                if (_cardToggleMap.TryGetValue(_card2, out ctgls)) {
                    foreach (var ct in ctgls) ct.Paint(g, ACC);
                }
                // Restore after painting
                if (micEnfWasChecked) {
                    _heroResetting = true; _tglMicEnf.Checked = true; _heroResetting = false;
                }
                // Card sliders — painted directly on card2
                List<CardSlider> cslds;
                if (_cardSliderMap.TryGetValue(_card2, out cslds)) {
                    foreach (var cs in cslds) cs.Paint(g, ACC);
                }
            };

            // Mouse handlers for card2 toggles + sliders
            _card2.MouseDown += (s, e2) => {
                if (e2.Button != MouseButtons.Left) return;
                List<CardSlider> cslds2;
                if (_cardSliderMap.TryGetValue(_card2, out cslds2)) {
                    foreach (var cs in cslds2) {
                        if (cs.HitTest(e2.Location) && cs.Source != null && cs.Source.Enabled) {
                            cs.Dragging = true; cs.Source.Value = cs.XToValue(e2.X); _card2.Invalidate(); return;
                        }
                    }
                }
            };
            _card2.MouseUp += (s, e2) => {
                List<CardSlider> cslds2;
                if (_cardSliderMap.TryGetValue(_card2, out cslds2)) {
                    foreach (var cs in cslds2) {
                        if (cs.Dragging) { cs.Dragging = false; _card2.Invalidate(); if (cs.Source != null) cs.Source.FireDragCompleted(); }
                    }
                }
            };
            _card2.MouseClick += (s, e2) => {
                List<CardSlider> cslds2;
                if (_cardSliderMap.TryGetValue(_card2, out cslds2)) {
                    foreach (var cs in cslds2) { if (cs.HitTest(e2.Location)) return; }
                }
                List<CardToggle> ctgls2;
                if (_cardToggleMap.TryGetValue(_card2, out ctgls2)) {
                    foreach (var ct in ctgls2) {
                        if (ct.HitTest(e2.Location)) { ct.Click(); return; }
                    }
                }
            };
            _card2.MouseMove += (s, e2) => {
                bool anyHover = false;
                List<CardSlider> cslds2;
                if (_cardSliderMap.TryGetValue(_card2, out cslds2)) {
                    foreach (var cs in cslds2) {
                        if (cs.Dragging && cs.Source != null) { cs.Source.Value = cs.XToValue(e2.X); _card2.Invalidate(); }
                        bool wasH = cs.Hover;
                        cs.Hover = cs.ThumbHitTest(e2.Location) || cs.Dragging;
                        if (cs.Hover) anyHover = true;
                        if (wasH != cs.Hover) _card2.Invalidate();
                    }
                }
                List<CardToggle> ctgls2;
                if (_cardToggleMap.TryGetValue(_card2, out ctgls2)) {
                    foreach (var ct in ctgls2) {
                        bool wasH = ct.Hover;
                        ct.Hover = ct.HitTest(e2.Location);
                        if (ct.Hover) anyHover = true;
                        if (wasH != ct.Hover) _card2.Invalidate();
                    }
                }
                _card2.Cursor = anyHover ? Cursors.Hand : Cursors.Default;
            };
            _card2.MouseLeave += (s, e2) => {
                List<CardToggle> ctgls2;
                if (_cardToggleMap.TryGetValue(_card2, out ctgls2)) {
                    foreach (var ct in ctgls2) { if (ct.Hover) { ct.Hover = false; _card2.Invalidate(); } }
                }
                List<CardSlider> cslds2;
                if (_cardSliderMap.TryGetValue(_card2, out cslds2)) {
                    foreach (var cs in cslds2) { if (cs.Hover) { cs.Hover = false; _card2.Invalidate(); } }
                }
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
            // Start VA peak monitoring — page 1 has the live meter
            if (_wizPtt != null) _wizPtt.StartPeakMonitor();
            _wizVAMeterTimer = new Timer { Interval = 30 };
            _wizVAMeterTimer.Tick += (s, e) => { if (_currentPage == 1 && _card1 != null) _card1.Invalidate(); };
            _wizVAMeterTimer.Start();
        }


        Panel MakeTipPanel() {
            var tip = new BufferedPanel { Dock = DockStyle.Top, Height = Dpi.S(24), BackColor = BG };
            tip.Paint += (s, e) => {
                var g = e.Graphics; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                PaintUnifiedStars(g, tip);
                using (var p = new Pen(Color.FromArgb(40, 40, 40))) g.DrawLine(p, Dpi.S(16), 0, tip.Width - Dpi.S(32), 0);
                string msg = "You can change these anytime \u2014 just double-click the kitty in your system tray.";
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

                // 4) Star spin around "Press..." hotkey label (if capture active on this card)
                if (_captureGlowLabel != null && _captureGlowLabel.Parent == c) {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float glCx = _captureGlowLabel.Left + _captureGlowLabel.Width / 2f;
                    float glCy = _captureGlowLabel.Top + _captureGlowLabel.Height / 2f;
                    float ringR = Dpi.S(20) + (float)Math.Sin(_captureGlowFrame * 0.3) * Dpi.S(3);
                    float baseAngle2 = _captureGlowFrame * 50f * (float)(Math.PI / 180.0);
                    for (int rd = 0; rd < 4; rd++) {
                        float dotAngle = baseAngle2 + rd * (float)(Math.PI * 2.0 / 4);
                        for (int tail = 0; tail < 4; tail++) {
                            float tailAngle = dotAngle - tail * 0.22f;
                            float tx = glCx + (float)Math.Cos(tailAngle) * ringR;
                            float ty = glCy + (float)Math.Sin(tailAngle) * ringR;
                            float tr = Dpi.S(2) * (1f - tail * 0.2f);
                            int ta2 = 240 - tail * 55;
                            if (ta2 <= 0) continue;
                            Color dc = tail == 0 ? Color.FromArgb(ta2, 255, 255, 255) : Color.FromArgb(ta2, 100, 200, 255);
                            using (var br = new SolidBrush(dc))
                                g.FillEllipse(br, tx - tr, ty - tr, tr*2, tr*2);
                        }
                    }
                }
            };
            c.Resize += (s, e) => DarkTheme.ApplyRoundedRegion(c, Dpi.S(6));
            c.Layout += (s, e) => DarkTheme.ApplyRoundedRegion(c, Dpi.S(6));
            return c;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        static bool _IsKeyHeld(int vk) {
            if (vk == 0x14 || vk == 0x91 || vk == 0x90) return PushToTalk.HookHeldKey == vk;
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }
        private DateTime _lastCaptureComplete = DateTime.MinValue;
        private bool CaptureCooldownActive() { return (DateTime.Now - _lastCaptureComplete).TotalMilliseconds < 500; }
        private bool IsCapturingKey { get { return _audio != null && _audio.IsCapturing; } }

        // =====================================================================
        //  UNIFIED CAPTURE — mirrors OptionsForm, uses AudioSettings state machine
        // =====================================================================

        void BeginCapture(CaptureTarget target, Label label) {
            if (_audio.IsCapturing || CaptureCooldownActive()) return;
            label.Text = "Press..."; label.BackColor = ACC; label.ForeColor = Color.White;
            _captureGlowLabel = label; _captureGlowFrame = 0;
            Logger.Info("Welcome: BeginCapture " + target);
            _audio.StartCapture(target, vk => OnCaptureComplete(target, label, vk));
        }

        void OnCaptureComplete(CaptureTarget target, Label label, int vk) {
            _captureGlowLabel = null;
            _lastCaptureComplete = DateTime.Now;

            if (vk == 0) { // Escape/cancel
                HandleWizCaptureCancel(target, label);
                return;
            }

            // Check cross-mode duplicate
            int excl = AudioSettings.ExcludeModeFor(target);
            if (_audio.IsKeyInUse(vk, excl)) {
                label.Text = GetLocalKeyCode(target) > 0 ? KeyName(GetLocalKeyCode(target)) : "Add Key";
                label.BackColor = INPUT_BG; label.ForeColor = ACC;
                ShakeWizLabel(label, null);
                return;
            }

            // === SUCCESS ===
            SetLocalKeyCode(target, vk);
            label.Text = KeyName(vk); label.BackColor = INPUT_BG; label.ForeColor = ACC;
            Logger.Info("Welcome: captured " + target + " vk=" + vk + " (" + KeyName(vk) + ")");

            int modeIdx = -1;
            switch (target) {
                case CaptureTarget.PttKey1:
                    if (!_tglPtt.Checked && !_tglPtm.Checked && !_tglPtToggle.Checked) { _wizToggleRejecting=true; _tglPtt.Checked=true; _wizToggleRejecting=false; }
                    // Mutual exclusivity: PTT on kills PTM (unless Toggle is on)
                    if (!_tglPtToggle.Checked && _tglPtm.Checked) {
                        _wizToggleRejecting=true; _tglPtm.Checked=false; _wizToggleRejecting=false;
                        _ptmKeyCode=0; _lblPtmKey.Text="Add Key"; _lblPtmKey.BackColor=INPUT_BG; _lblPtmKey.ForeColor=ACC; _audio.DisablePtmMode();
                    }
                    if (_audio != null) _audio.SetPttKeyAndEnable(vk);
                    modeIdx = 0; break;
                case CaptureTarget.PtmKey1:
                    if (!_tglPtm.Checked) { _wizToggleRejecting=true; _tglPtm.Checked=true; _wizToggleRejecting=false; }
                    // Mutual exclusivity: PTM on kills PTT (unless Toggle is on)
                    if (!_tglPtToggle.Checked && _tglPtt.Checked) {
                        _wizToggleRejecting=true; _tglPtt.Checked=false; _wizToggleRejecting=false;
                        _pttKeyCode=0; _lblPttKey.Text="Add Key"; _lblPttKey.BackColor=INPUT_BG; _lblPttKey.ForeColor=ACC; _audio.DisablePttMode();
                    }
                    if (_audio != null) _audio.SetPtmKeyAndEnable(vk);
                    modeIdx = 1; break;
                case CaptureTarget.ToggleKey1:
                    if (!_tglPtToggle.Checked) { _wizToggleRejecting=true; _tglPtToggle.Checked=true; _wizToggleRejecting=false; }
                    if (_audio != null) _audio.SetPtToggleKeyAndEnable(vk);
                    modeIdx = 2; break;
            }
            if (modeIdx >= 0) PlayModeGlow(modeIdx);
            if (_tipHotkey != null) _tipHotkey.Visible = false;
            _card1.Invalidate(false);
        }

        void HandleWizCaptureCancel(CaptureTarget target, Label label) {
            SetLocalKeyCode(target, 0);
            label.Text = "Add Key"; label.BackColor = INPUT_BG; label.ForeColor = ACC;
            // Bounce toggle off if no key
            _wizToggleRejecting = true;
            if (target == CaptureTarget.PttKey1 && _tglPtt.Checked) { _tglPtt.Checked = false; _audio.DisablePttMode(); }
            else if (target == CaptureTarget.PtmKey1 && _tglPtm.Checked) { _tglPtm.Checked = false; _audio.DisablePtmMode(); }
            else if (target == CaptureTarget.ToggleKey1 && _tglPtToggle.Checked) { _tglPtToggle.Checked = false; _audio.DisablePtToggleMode(); }
            _wizToggleRejecting = false;
        }

        void SetLocalKeyCode(CaptureTarget target, int vk) {
            switch (target) {
                case CaptureTarget.PttKey1: _pttKeyCode = vk; break;
                case CaptureTarget.PtmKey1: _ptmKeyCode = vk; break;
                case CaptureTarget.ToggleKey1: _ptToggleKeyCode = vk; break;
            }
        }

        int GetLocalKeyCode(CaptureTarget target) {
            switch (target) {
                case CaptureTarget.PttKey1: return _pttKeyCode;
                case CaptureTarget.PtmKey1: return _ptmKeyCode;
                case CaptureTarget.ToggleKey1: return _ptToggleKeyCode;
            }
            return 0;
        }

        void CancelAllCaptures() {
            if (_audio != null && _audio.IsCapturing) _audio.CancelCapture();
            _captureGlowLabel = null;
            _lastCaptureComplete = DateTime.Now;
            _wizToggleRejecting = true;
            if (_pttKeyCode <= 0) { _tglPtt.Checked = false; }
            if (_ptmKeyCode <= 0) { _tglPtm.Checked = false; }
            if (_ptToggleKeyCode <= 0) { _tglPtToggle.Checked = false; }
            _wizToggleRejecting = false;
            _lblPttKey.Text = _pttKeyCode > 0 ? KeyName(_pttKeyCode) : "Add Key";
            _lblPttKey.BackColor = INPUT_BG; _lblPttKey.ForeColor = ACC;
            _lblPtmKey.Text = _ptmKeyCode > 0 ? KeyName(_ptmKeyCode) : "Add Key";
            _lblPtmKey.BackColor = INPUT_BG; _lblPtmKey.ForeColor = ACC;
            _lblPtToggleKey.Text = _ptToggleKeyCode > 0 ? KeyName(_ptToggleKeyCode) : "Add Key";
            _lblPtToggleKey.BackColor = INPUT_BG; _lblPtToggleKey.ForeColor = ACC;
        }

        void OnWizardToggle(ToggleSwitch sender, string onMsg, string offMsg) {
            if (_wizToggleRejecting) return;
            if (sender.Checked && (IsCapturingKey || CaptureCooldownActive())) {
                _wizToggleRejecting = true; sender.Checked = false; _wizToggleRejecting = false; return;
            }
            try {
                if (sender.Checked) {
                    bool needsKey = false;
                    if (sender == _tglPtt && _pttKeyCode <= 0) needsKey = true;
                    else if (sender == _tglPtm && _ptmKeyCode <= 0) needsKey = true;
                    else if (sender == _tglPtToggle && _ptToggleKeyCode <= 0) needsKey = true;
                    if (needsKey) {
                        _wizToggleRejecting = true; sender.Checked = false; _wizToggleRejecting = false;
                        if (sender == _tglPtt) BeginCapture(CaptureTarget.PttKey1, _lblPttKey);
                        else if (sender == _tglPtm) BeginCapture(CaptureTarget.PtmKey1, _lblPtmKey);
                        else if (sender == _tglPtToggle) BeginCapture(CaptureTarget.ToggleKey1, _lblPtToggleKey);
                        return;
                    }
                }
                if (sender.Checked) { _card1.Invalidate(false);
                    // Disable Voice Activity — mutually exclusive with PTT modes
                    if (_tglWizVA != null && _tglWizVA.Checked) {
                        _wizToggleRejecting = true; _tglWizVA.Checked = false; _wizToggleRejecting = false;
                        if (_audio != null) _audio.VoiceActivityEnabled = false;
                    }
                    // Mutual exclusion: PTT and PTM cannot coexist unless Toggle is on
                    if (sender == _tglPtt && !_tglPtToggle.Checked && _tglPtm.Checked) {
                        _wizToggleRejecting=true; _tglPtm.Checked=false; _wizToggleRejecting=false;
                        _ptmKeyCode=0; _lblPtmKey.Text="Add Key"; _lblPtmKey.BackColor=INPUT_BG; _lblPtmKey.ForeColor=ACC; _audio.DisablePtmMode();
                    }
                    if (sender == _tglPtm && !_tglPtToggle.Checked && _tglPtt.Checked) {
                        _wizToggleRejecting=true; _tglPtt.Checked=false; _wizToggleRejecting=false;
                        _pttKeyCode=0; _lblPttKey.Text="Add Key"; _lblPttKey.BackColor=INPUT_BG; _lblPttKey.ForeColor=ACC; _audio.DisablePttMode();
                    }
                    if (_audio != null) {
                        if (sender == _tglPtt) _audio.PttEnabled = true;
                        else if (sender == _tglPtm) _audio.PtmEnabled = true;
                        else if (sender == _tglPtToggle) _audio.PtToggleEnabled = true;
                    }
                }
                else {
                    CancelAllCaptures();
                    if (sender == _tglPtt) { _pttKeyCode=0; _lblPttKey.Text="Add Key"; _lblPttKey.BackColor=INPUT_BG; _lblPttKey.ForeColor=ACC; _audio.DisablePttMode(); }
                    else if (sender == _tglPtm) { _ptmKeyCode=0; _lblPtmKey.Text="Add Key"; _lblPtmKey.BackColor=INPUT_BG; _lblPtmKey.ForeColor=ACC; _audio.DisablePtmMode(); }
                    else if (sender == _tglPtToggle) {
                        _ptToggleKeyCode=0; _lblPtToggleKey.Text="Add Key"; _lblPtToggleKey.BackColor=INPUT_BG; _lblPtToggleKey.ForeColor=ACC; _audio.DisablePtToggleMode();
                        // Toggle OFF: PTT and PTM can no longer coexist — keep PTT, kill PTM
                        if (_tglPtt.Checked && _tglPtm.Checked) {
                            _wizToggleRejecting=true; _tglPtm.Checked=false; _wizToggleRejecting=false;
                            _ptmKeyCode=0; _lblPtmKey.Text="Add Key"; _lblPtmKey.BackColor=INPUT_BG; _lblPtmKey.ForeColor=ACC; _audio.DisablePtmMode();
                        }
                    }
                }
            } catch (Exception ex) { Logger.Error("Wizard toggle failed: " + onMsg, ex); }
        }

        void ShakeWizLabel(Label lbl, Action onComplete) {
            var origLoc = lbl.Location;
            var origBg = lbl.BackColor;
            var origFg = lbl.ForeColor;
            lbl.BackColor = Color.FromArgb(60, 20, 20);
            lbl.ForeColor = Color.FromArgb(255, 80, 80);
            int tick = 0;
            int[] offsets = { -6, 6, -4, 4, -2, 2, 0 };
            var shakeTimer = new Timer { Interval = 35 };
            shakeTimer.Tick += (s, e) => {
                if (tick < offsets.Length) {
                    lbl.Location = new Point(origLoc.X + Dpi.S(offsets[tick]), origLoc.Y);
                    tick++;
                } else {
                    shakeTimer.Stop(); shakeTimer.Dispose();
                    lbl.Location = origLoc;
                    lbl.BackColor = origBg;
                    lbl.ForeColor = origFg;
                    if (onComplete != null) onComplete();
                }
            };
            shakeTimer.Start();
        }

        // (toggles are always enabled — user picks mode FIRST in the new guided flow)

        // --- Toggle flicker animation (draws attention to mode selection) ---
        private Timer _flashTimer;
        private List<Panel> _helpCircles = new List<Panel>();
        void FlashToggles() { }

        void PlayModeGlow(int modeIndex) {
            _modeGlowIndex = modeIndex;
            _modeGlowFrame = 0;
            // Start toggle glisten after a short delay (when toggle has flipped on)
            ToggleSwitch[] tgls = { _tglPtt, _tglPtm, _tglPtToggle };
            if (modeIndex >= 0 && modeIndex < 3 && tgls[modeIndex] != null && tgls[modeIndex].Checked) {
                _tglGlistenTarget = tgls[modeIndex];
                _tglGlistenFrame = 0;
            }
        }

        // --- Tip panels — styled as mini-cards matching the app's visual language ---
        private Panel _tipHotkey, _tipFunCallout, _tipMicLock;
        private Timer _tipDismissTimer;

        // --- Help circle (?) with zip line animation and hover tooltip ---
        Panel MakeHelpCircle(int x, int y, string tooltipText, Panel parentCard) {
            int sz = Dpi.S(20);
            int _hcTick = 0;
            PointF[] _hcPts = null;
            float[] _hcDist = null;
            float _hcTotal = 0;
            bool _hcHover = false;

            // Simple tooltip — just a painted panel, no timers, no fade, no MakeTipCard
            var tooltip = new BufferedPanel { Visible = false, BackColor = Color.Transparent };
            tooltip.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int cr = Dpi.S(6);
                using (var path = DarkTheme.RoundedRect(new Rectangle(0, 0, tooltip.Width - 1, tooltip.Height - 1), cr)) {
                    using (var b = new SolidBrush(Color.FromArgb(240, 16, 20, 28)))
                        g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb(70, ACC.R, ACC.G, ACC.B), 1f))
                        g.DrawPath(p, path);
                }
                // Left accent bar
                int barW = Dpi.S(3);
                using (var barPath = DarkTheme.RoundedRect(new Rectangle(0, 0, barW + cr, tooltip.Height - 1), cr)) {
                    var oldClip = g.Clip;
                    g.SetClip(new Rectangle(0, 0, barW + 2, tooltip.Height));
                    using (var b = new SolidBrush(Color.FromArgb(200, ACC.R, ACC.G, ACC.B)))
                        g.FillPath(b, barPath);
                    g.Clip = oldClip;
                }
                using (var f = new Font(DarkTheme.Caption.FontFamily, 8f))
                using (var b = new SolidBrush(Color.FromArgb(210, 220, 235))) {
                    var rect = new RectangleF(Dpi.S(12), Dpi.S(6), tooltip.Width - Dpi.S(18), tooltip.Height - Dpi.S(12));
                    g.DrawString(tooltipText, f, b, rect);
                }
            };

            // Measure tooltip size based on text
            using (var g = parentCard.CreateGraphics())
            using (var f = new Font(DarkTheme.Caption.FontFamily, 8f)) {
                var sz2 = g.MeasureString(tooltipText, f, Dpi.S(200));
                tooltip.Size = new Size((int)sz2.Width + Dpi.S(22), (int)sz2.Height + Dpi.S(14));
            }
            parentCard.Controls.Add(tooltip);

            var circle = new BufferedPanel { Size = new Size(sz, sz), Location = new Point(Dpi.S(x), Dpi.S(y) + 1), BackColor = Color.Transparent, Cursor = Cursors.Hand };
            circle.Paint += (s, e) => {
                _hcTick++;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                int alpha = _hcHover ? 120 : 50;
                using (var b = new SolidBrush(Color.FromArgb(alpha, ACC.R, ACC.G, ACC.B)))
                    g.FillEllipse(b, 1, 1, sz - 3, sz - 3);
                using (var p = new Pen(Color.FromArgb(_hcHover ? 200 : 100, ACC.R, ACC.G, ACC.B), 1.5f))
                    g.DrawEllipse(p, 1, 1, sz - 3, sz - 3);

                // Zip line
                if (_hcPts == null) {
                    using (var path = new GraphicsPath()) {
                        path.AddEllipse(1, 1, sz - 3, sz - 3);
                        path.Flatten(null, 0.3f);
                        _hcPts = (PointF[])path.PathPoints.Clone();
                    }
                    _hcDist = new float[_hcPts.Length];
                    _hcDist[0] = 0;
                    for (int i = 1; i < _hcPts.Length; i++) {
                        float dx = _hcPts[i].X - _hcPts[i-1].X;
                        float dy = _hcPts[i].Y - _hcPts[i-1].Y;
                        _hcDist[i] = _hcDist[i-1] + (float)Math.Sqrt(dx*dx + dy*dy);
                    }
                    float cx2 = _hcPts[0].X - _hcPts[_hcPts.Length-1].X;
                    float cy2 = _hcPts[0].Y - _hcPts[_hcPts.Length-1].Y;
                    _hcTotal = _hcDist[_hcPts.Length-1] + (float)Math.Sqrt(cx2*cx2 + cy2*cy2);
                }
                if (_hcPts.Length > 4 && _hcTotal > 1f) {
                    float speed = 3f;
                    float headPos = (_hcTick * speed) % _hcTotal;
                    float trailLen = _hcTotal * 0.3f;
                    for (int i = 0; i < _hcPts.Length; i++) {
                        int next = (i + 1) % _hcPts.Length;
                        float d0 = _hcDist[i];
                        float d1 = (i < _hcPts.Length - 1) ? _hcDist[i+1] : _hcTotal;
                        float mid = (d0 + d1) * 0.5f;
                        float dist = headPos - mid;
                        if (dist < 0) dist += _hcTotal;
                        if (dist > trailLen) continue;
                        float t = 1f - (dist / trailLen);
                        t = t * t;
                        int za = (int)(255 * t);
                        if (za > 10) {
                            float pw = 1.2f + t * 1.5f;
                            using (var zp = new Pen(Color.FromArgb(za, ACC.R, ACC.G, ACC.B), pw))
                                g.DrawLine(zp, _hcPts[i], _hcPts[next]);
                        }
                    }
                }

                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.FromArgb(_hcHover ? 255 : 200, 220, 230, 245))) {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("?", f, b, sz / 2f, sz / 2f - 1, sf);
                }
            };

            // Hover — just show/hide, zero timers
            circle.MouseEnter += (s, e) => {
                _hcHover = true;
                circle.Invalidate();
                tooltip.Location = new Point(circle.Left - tooltip.Width - Dpi.S(8), circle.Top - Dpi.S(4));
                tooltip.BringToFront();
                tooltip.Visible = true;
            };
            circle.MouseLeave += (s, e) => {
                _hcHover = false;
                circle.Invalidate();
                tooltip.Visible = false;
            };

            _helpCircles.Add(circle);
            return circle;
        }

        /// <summary>Simple speaker overlay icon for wizard page 1 — matches Options page style</summary>
        CardIcon MakeWizEyeToggle(int y, Panel card, bool initialOn, Action<bool> onChange) {
            var icon = new CardIcon { W = 20, H = 20, Checked = initialOn, IsEye = true, OnChange = onChange, Card = card };
            icon.SetPos(378, y + 1);
            if (!_cardIconMap.ContainsKey(card)) _cardIconMap[card] = new List<CardIcon>();
            _cardIconMap[card].Add(icon);
            return icon;
        }

        CardIcon MakeWizSpeakerToggle(int y, Panel card, bool initialOn, Action<bool> onChange) {
            var icon = new CardIcon { W = 28, H = 20, Checked = initialOn, IsEye = false, OnChange = onChange, Card = card };
            icon.SetPos(406, y + 1);
            if (!_cardIconMap.ContainsKey(card)) _cardIconMap[card] = new List<CardIcon>();
            _cardIconMap[card].Add(icon);
            return icon;
        }

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
                    if (_fadeIn != null) { _fadeIn.Stop(); _fadeIn.Dispose(); _fadeIn = null; }
                    _fadeIn = new Timer { Interval = 30 };
                    int fadeStep = 0;
                    _fadeIn.Tick += (s2, e2) => {
                        if (_fadeIn == null || tip == null || tip.IsDisposed) return;
                        fadeStep++;
                        _fadeAlpha = Math.Min(1f, fadeStep * 0.12f);
                        tip.Invalidate();
                        if (_fadeAlpha >= 1f) { _fadeIn.Stop(); _fadeIn.Dispose(); _fadeIn = null; }
                    };
                    _fadeIn.Start();
                } else {
                    // Stop fade animation on hide
                    if (_fadeIn != null) { _fadeIn.Stop(); _fadeIn.Dispose(); _fadeIn = null; }
                }
            };
            return tip;
        }

        /// <summary>Get a point along the perimeter of a rectangle at a given distance from top-left corner, going clockwise.</summary>
        static PointF GetPerimeterPoint(int left, int top, int width, int height, float dist, float perimeter) {
            dist = dist % perimeter;
            if (dist < 0) dist += perimeter;
            // Top edge
            if (dist <= width) return new PointF(left + dist, top);
            dist -= width;
            // Right edge
            if (dist <= height) return new PointF(left + width, top + dist);
            dist -= height;
            // Bottom edge (right to left)
            if (dist <= width) return new PointF(left + width - dist, top + height);
            dist -= width;
            // Left edge (bottom to top)
            return new PointF(left, top + height - dist);
        }

        void CreateTips() {
            // STEP 2 TIP: After mode chosen → guide to hotkey
            _tipHotkey = MakeTipCard("Now click Add Key and press the same key you use in Discord, Zoom, or your game.", 300, 44, arrowUp: false);
            _tipHotkey.Location = Dpi.Pt(20, 210);
            _card1.Controls.Add(_tipHotkey); _tipHotkey.BringToFront();

            // STEP 3 TIP: After key captured → confirm what happened
            // Fun callout removed — mode glow + toggle glisten provides feedback

            // PAGE 2 TIP: Mic lock explanation
            _tipMicLock = MakeTipCard("Lock your mic volume so apps can't change it behind your back. Adjust the slider and hit Save when you're ready.", 360, 48, arrowUp: false);
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
            if (IsCapturingKey) CancelAllCaptures();
            _currentPage = 1;
            _showButtonStar = false;
            HidePage(_page2);
            ShowPageFill(_page1);
            // Reset 1-2-3-4 flicker so it replays
            _flickerFrame = 0;
            if (_showPage1Extras != null) _showPage1Extras();
            // Start peak monitoring for the live meter
            if (_wizPtt != null) _wizPtt.StartPeakMonitor();
            if (_wizVAMeterTimer == null) {
                _wizVAMeterTimer = new Timer { Interval = 30 };
                _wizVAMeterTimer.Tick += (s, e) => { if (_currentPage == 1 && _card1 != null) _card1.Invalidate(); };
            }
            _wizVAMeterTimer.Start();
            Invalidate(true);
        }
        void ShowPage2() {
            if (IsCapturingKey) CancelAllCaptures();
            _currentPage = 2;
            _showButtonStar = false;
            // Stop VA peak monitor when leaving page 1
            if (_wizVAMeterTimer != null) _wizVAMeterTimer.Stop();
            if (_wizPtt != null) _wizPtt.StopPeakMonitor();
            
            HidePage(_page1);
            ShowPageFill(_page2);
            if (_showPage2Extras != null) _showPage2Extras();
            _micHeroFrame = 0;
            // Enable mic lock immediately in the backend so fast save is safe
            _heroResetting = true;
            if (!_tglMicEnf.Checked) _tglMicEnf.Checked = true;
            _heroResetting = false;
            // But visually hide the toggle state until frame 32 — animation shows it turning on
            _heroToggleVisualOff = true;
            // Dispose old timer and create fresh one each time
            if (_micHeroTimer != null) { _micHeroTimer.Stop(); _micHeroTimer.Dispose(); _micHeroTimer = null; }
            _micHeroTimer = new Timer { Interval = 30 };
            _micHeroTimer.Tick += (s, e) => { 
                _micHeroFrame++; 
                // Frame 32: visual toggle flips ON
                if (_micHeroFrame == 32) _heroToggleVisualOff = false;
                if (_micHeroFrame > 75) { 
                    _micHeroTimer.Stop();
                    _showButtonStar = true;
                    if (_showPage2SaveReady != null) _showPage2SaveReady();
                } 
                _card2.Invalidate(); 
            };
            _micHeroTimer.Start();
            Invalidate(true);
        }
        private bool _saved;
        void DoSave() {
            if (IsCapturingKey) CancelAllCaptures();
            if (_saved) return; _saved = true;
            // Stop VA meter
            if (_wizVAMeterTimer != null) { _wizVAMeterTimer.Stop(); _wizVAMeterTimer.Dispose(); _wizVAMeterTimer = null; }
            if (_wizPtt != null) _wizPtt.StopPeakMonitor();
            Logger.Info("Welcome: DoSave — PTT=" + _tglPtt.Checked + " PTM=" + _tglPtm.Checked + " Toggle=" + _tglPtToggle.Checked + " Key=" + _pttKeyCode + " MicLock=" + _tglMicEnf.Checked + " SpkLock=" + _tglSpkEnf.Checked + " VA=" + _tglWizVA.Checked);
            ProtectMic = _tglMicEnf.Checked; ProtectSpeakers = _tglSpkEnf.Checked;
            MicVolPercent = _micSlider.Value; SpkVolPercent = _spkSlider.Value;
            AfkMicEnabled = _tglAfkMic.Checked; AfkMicSec = (int)_nudAfkMic.Value; AfkSpkEnabled = _tglAfkSpk.Checked; AfkSpkSec = (int)_nudAfkSpk.Value;
            PttEnabled = _tglPtt.Checked; PtMuteEnabled = _tglPtm.Checked; PtToggleEnabled = _tglPtToggle.Checked; PttKey = _pttKeyCode; PtMuteKey = _ptmKeyCode; PtToggleKey = _ptToggleKeyCode;
            StartupEnabled = _tglStartup.Checked; NotifyCorrEnabled = _tglNotifyCorr.Checked; NotifyDevEnabled = _tglNotifyDev.Checked;
            VoiceActivityEnabled = _tglWizVA.Checked;
            VoiceActivityThreshold = _trkWizVAThreshold.Value;
            VoiceActivityHoldoverMs = (int)(_nudWizHoldover != null ? _nudWizHoldover.Value : 2000);
            // Force-sync ALL toggle states to settings — ensures defaults are overwritten
            // even if the user never touched a toggle (e.g. MicEnforceEnabled defaults to true)
            if (_audio != null) {
                _audio.MicLockEnabled = _tglMicEnf.Checked;
                _audio.MicLockVolume = _micSlider.Value;
                _audio.SpeakerLockEnabled = _tglSpkEnf.Checked;
                _audio.SpeakerLockVolume = _spkSlider.Value;
                _audio.AfkMicEnabled = _tglAfkMic.Checked;
                _audio.AfkMicSec = (int)_nudAfkMic.Value;
                _audio.AfkSpeakerEnabled = _tglAfkSpk.Checked;
                _audio.AfkSpeakerSec = (int)_nudAfkSpk.Value;
                _audio.StartWithWindows = _tglStartup.Checked;
                _audio.NotifyOnCorrection = _tglNotifyCorr.Checked;
                _audio.NotifyOnDeviceChange = _tglNotifyDev.Checked;
                _audio.VoiceActivityEnabled = _tglWizVA.Checked;
                _audio.VoiceActivityThreshold = _trkWizVAThreshold.Value;
                _audio.VoiceActivityHoldoverMs = (int)(_nudWizHoldover != null ? _nudWizHoldover.Value : 2000);
            }
            DialogResult = DialogResult.OK; Close();
        }

        void AnimateWizardSliderRestore(bool isMic)
        {
            int target = isMic ? _micPreLockVol : _spkPreLockVol;
            if (target < 0) return;

            var slider = isMic ? _micSlider : _spkSlider;
            if (slider == null) return;

            if (isMic) { if (_sliderRestoreMicTimer != null) { _sliderRestoreMicTimer.Stop(); _sliderRestoreMicTimer.Dispose(); _sliderRestoreMicTimer = null; } }
            else { if (_sliderRestoreSpkTimer != null) { _sliderRestoreSpkTimer.Stop(); _sliderRestoreSpkTimer.Dispose(); _sliderRestoreSpkTimer = null; } }

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
            if (isMic) { if (_micFlashTimer != null) { _micFlashTimer.Stop(); _micFlashTimer.Dispose(); _micFlashTimer = null; } }
            else { if (_spkFlashTimer != null) { _spkFlashTimer.Stop(); _spkFlashTimer.Dispose(); _spkFlashTimer = null; } }

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
            if (_pulseTimer != null) { _pulseTimer.Stop(); _pulseTimer.Dispose(); }
            if (_pollTimer != null) { _pollTimer.Stop(); _pollTimer.Dispose(); }
            // No local capture timer to dispose — AudioSettings owns it
            if (_flashTimer != null) { _flashTimer.Stop(); _flashTimer.Dispose(); }
            if (_micHeroTimer != null) { _micHeroTimer.Stop(); _micHeroTimer.Dispose(); }
            if (_flickerNextTimer != null) { _flickerNextTimer.Stop(); _flickerNextTimer.Dispose(); }
            if (_tipDismissTimer != null) { _tipDismissTimer.Stop(); _tipDismissTimer.Dispose(); }
            if (_micFlashTimer != null) { _micFlashTimer.Stop(); _micFlashTimer.Dispose(); }
            if (_spkFlashTimer != null) { _spkFlashTimer.Stop(); _spkFlashTimer.Dispose(); }
            if (_sliderRestoreMicTimer != null) { _sliderRestoreMicTimer.Stop(); _sliderRestoreMicTimer.Dispose(); }
            if (_sliderRestoreSpkTimer != null) { _sliderRestoreSpkTimer.Stop(); _sliderRestoreSpkTimer.Dispose(); }
            if (_stars != null) _stars.Dispose();
            if (_wizVAMeterTimer != null) { _wizVAMeterTimer.Stop(); _wizVAMeterTimer.Dispose(); }
            if (_wizPtt != null) _wizPtt.StopPeakMonitor();
            if (_tipHotkey != null) _tipHotkey.Visible = false;
            if (_tipFunCallout != null) _tipFunCallout.Visible = false;
            if (_tipMicLock != null) _tipMicLock.Visible = false;
            // Auto-save on close — whether they hit Save or X, preserve their choices
            if (DialogResult != DialogResult.OK && !_saved) {
                DoSave();
            }
            base.OnFormClosing(e);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (IsCapturingKey) return base.ProcessCmdKey(ref msg, keyData);
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
