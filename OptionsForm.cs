// OptionsForm.cs — Main settings/options window.
// Uses StarBackground for star rendering, Controls.cs for shared UI controls.
// Private inner classes: PaintedLabel (text rendering), AppRuleRow (volume rules).
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AngryAudio
{
    public class OptionsForm : Form
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        private Settings _settings;
        private Panel _contentPanel, _sidebar, _footer;
        private Panel[] _panes, _navPanels, _navAccents;

        private Label[] _navLabels;
        private int _activePane = 0;
        private bool _starShowMode = false;
        private int _savedMascotX, _savedMascotY;

        private ToggleSwitch _tglAfkMic, _tglAfkSpk, _tglPtt, _tglPtm, _tglPtToggle, _tglMicEnf, _tglSpkEnf, _tglAppEnf, _tglStartup, _tglNotifyCorr, _tglNotifyDev, _tglOverlay;
        private string _pttWarningText = "";
        private int _pttWarningY = 0;
        private string _spkWarningText = "";
        private int _spkWarningY = 0;
        private NumericUpDown _nudAfkMicSec, _nudAfkSpkSec;
        private Label _lblPttKey, _lblPttKey2, _lblPttKey3, _lblPtmKey, _lblPtmKey2, _lblPtmKey3, _lblPtToggleKey, _lblPtToggleKey2, _lblPtToggleKey3;
        private Label _lblKey2Label, _lblKey2Hint, _lblKey3Label, _lblKey3Hint;
        private Button _btnRemoveKey2, _btnAddKey2, _btnRemoveKey3, _btnAddKey3;
        private Button _btnPtmAddKey2, _btnPtmRemKey2, _btnPtmAddKey3, _btnPtmRemKey3, _btnToggleAddKey2, _btnToggleRemKey2, _btnToggleAddKey3, _btnToggleRemKey3;
        private CardIcon _chkKey1Overlay, _chkKey2Overlay, _chkKey3Overlay;
        private Dictionary<Panel, List<CardIcon>> _cardIconMap = new Dictionary<Panel, List<CardIcon>>();
        private Dictionary<Panel, List<CardToggle>> _cardToggleMap = new Dictionary<Panel, List<CardToggle>>();
        private Dictionary<Panel, List<CardSlider>> _cardSliderMap = new Dictionary<Panel, List<CardSlider>>();
        private bool _key1ShowOverlay = true, _key2ShowOverlay = true, _key3ShowOverlay = true;
        private Timer _pollTimer;
        private int _pttKeyCode = 0, _pttKeyCode2 = 0, _pttKeyCode3 = 0, _ptmKeyCode = 0, _ptmKeyCode2 = 0, _ptmKeyCode3 = 0, _ptToggleKeyCode = 0, _ptToggleKeyCode2 = 0, _ptToggleKeyCode3 = 0; private bool _loading;
        public bool IsCapturingKey { get { return _audio != null && _audio.IsCapturing; } }
        private SlickSlider _trkMicVol, _trkSpkVol, _sysVolSlider;
        private SlickSlider _micCurVolSlider, _spkCurVolSlider;
        private PaintedLabel _lblMicCurVolPct, _lblSpkCurVolPct;
        private CardIcon _micMuteIcon, _spkMuteIcon;
        private CardIcon _micLockIcon, _spkLockIcon;
        private CardIcon _appsSysMuteIcon;
        private DateTime _lastLockClick = DateTime.MinValue;
        private PaintedLabel _lblSysVolPct;
        // Volume lock snapshot/restore
        private int _micPreLockVol = -1, _spkPreLockVol = -1;
        private Timer _sliderRestoreMicTimer, _sliderRestoreSpkTimer;
        private Timer _twinkleTimer;
        private StarBackground _stars;

        static readonly Color BG = DarkTheme.BG;
        static readonly Color SB_BG = Color.FromArgb(16, 16, 16);  // Sidebar slightly lighter — intentional
        static readonly Color CARD_BDR = DarkTheme.CardBdr;
        static readonly Color BDR = Color.FromArgb(30, 30, 30);    // Form-level border
        static readonly Color ACC = DarkTheme.Accent;
        static readonly Color TXT = DarkTheme.Txt;
        static readonly Color TXT2 = DarkTheme.Txt2;
        static readonly Color TXT3 = DarkTheme.Txt3;
        static readonly Color TXT4 = DarkTheme.Txt4;
        static readonly Color HOVER = Color.FromArgb(26, 26, 26);  // Sidebar hover
        static readonly Color INPUT_BG = DarkTheme.InputBG;
        static readonly Color INPUT_BDR = DarkTheme.InputBdr;
        static readonly Color GREEN = DarkTheme.Green;
        static readonly string[] NAV = { "Mic", "Voice", "Volume", "AFK", "Apps", "General" };
        const int SB_W = 155;

        private AudioSettings _audio; // Unified state controller
        private PushToTalk _pushToTalk; // For voice activity peak monitor

        public OptionsForm(Settings settings, AudioSettings audio, PushToTalk ptt = null) {
            _settings = settings;
            _audio = audio;
            _pushToTalk = ptt;
            Text = AppVersion.FullName + " \u2014 Options";
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BG; ForeColor = TXT;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f);
            ClientSize = Dpi.Size(730, 410);
            MinimumSize = Size; // Lock minimum to current layout size (includes title bar + borders)
            _defaultSize = Size;
            DoubleBuffered = true;
            try { Icon = Mascot.CreateIcon(); } catch { }
            // Restore window position
            if (_settings.LastWindowX >= 0 && _settings.LastWindowY >= 0) {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(_settings.LastWindowX, _settings.LastWindowY);
                // Clamp to screen bounds
                var screen = Screen.FromPoint(Location);
                if (!screen.WorkingArea.IntersectsWith(new Rectangle(Location, Size)))
                    StartPosition = FormStartPosition.CenterScreen;
            }

            _sidebar = new Panel { Dock = DockStyle.Left, Width = Dpi.S(SB_W), BackColor = SB_BG };
            var sep = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = BDR };
            _contentPanel = new BufferedPanel { Dock = DockStyle.Fill, BackColor = BG, Padding = Dpi.Pad(20, 16, 20, 0) };
            _contentPanel.Paint += (s, e) => {
                PaintUnifiedStars(e.Graphics, _contentPanel);
                // Mascot watermark — in star show, use exact position captured from card before hiding
                try {
                    if (_starShowMode && _savedMascotY > 0) {
                        int msz = Dpi.S(120); // same size as card mascot
                        Mascot.DrawMascotWithOpacity(e.Graphics, _savedMascotX, _savedMascotY, msz, 0.25f);
                    } else {
                        int msz = Dpi.S(135);
                        Mascot.DrawMascotWithOpacity(e.Graphics, _contentPanel.Width - msz - Dpi.S(8), _contentPanel.Height - msz - Dpi.S(8), msz, 0.22f);
                    }
                } catch { }
            };

            BuildSidebar(); BuildPanes(); BuildFooter();
            Controls.Add(_contentPanel); Controls.Add(sep); Controls.Add(_sidebar);
            // Global click handler — any click outside a NUD steals focus, killing the cursor
            Application.AddMessageFilter(new NudDefocusFilter(this));
            LoadSettings(); SwitchPane(0);
            _pollTimer = new Timer { Interval = 1000 };
            _pollTimer.Tick += (s, e) => UpdateCurrent();
            _pollTimer.Start();
            // Twinkle timer — slowly animates card stars (150ms = ~6.6fps, gentle and efficient)
            _twinkleTimer = new Timer { Interval = 150 };
            _twinkleTimer.Tick += (s, e) => {
                if (_isResizing) return;
                bool isLarge = (Width * Height) > 1200000; // ~1100x1100 — freezes at maximize, not normal size
                if (!isLarge) {
                    _stars.Tick();
                    InvalidateCardsDeep(); // Full invalidation only on twinkle change
                }
                // Hotkey test detection — flash only the specific mode when user presses its assigned hotkey
                if (!IsCapturingKey) {
                    bool pttDown = false, ptmDown = false, toggleDown = false;
                    if (_pttKeyCode > 0) pttDown = IsKeyHeld(_pttKeyCode);
                    if (!pttDown && _pttKeyCode2 > 0) pttDown = IsKeyHeld(_pttKeyCode2);
                    if (!pttDown && _pttKeyCode3 > 0) pttDown = IsKeyHeld(_pttKeyCode3);
                    if (_ptmKeyCode > 0) ptmDown = IsKeyHeld(_ptmKeyCode);
                    if (!ptmDown && _ptmKeyCode2 > 0) ptmDown = IsKeyHeld(_ptmKeyCode2);
                    if (!ptmDown && _ptmKeyCode3 > 0) ptmDown = IsKeyHeld(_ptmKeyCode3);
                    if (_ptToggleKeyCode > 0) toggleDown = IsKeyHeld(_ptToggleKeyCode);
                    if (!toggleDown && _ptToggleKeyCode2 > 0) toggleDown = IsKeyHeld(_ptToggleKeyCode2);
                    if (!toggleDown && _ptToggleKeyCode3 > 0) toggleDown = IsKeyHeld(_ptToggleKeyCode3);
                    bool anyDown = pttDown || ptmDown || toggleDown;
                    // Continuous held-key highlight — stays lit while key is held
                    bool[] downs = { pttDown, ptmDown, toggleDown };
                    bool changed = false;
                    for (int i = 0; i < 3; i++) {
                        if (downs[i] != _optModeActive[i]) { _optModeActive[i] = downs[i]; changed = true; }
                    }
                    if (changed && _pttCard != null) _pttCard.Invalidate();
                    // Glisten on rising edge
                    if (anyDown && !_hotkeyWasDown) {
                        if (pttDown && _tglPtt.Checked) { StartGlisten(_tglPtt); }
                        else if (ptmDown && _tglPtm.Checked) { StartGlisten(_tglPtm); }
                        else if (toggleDown && _tglPtToggle.Checked) { StartGlisten(_tglPtToggle); }
                        if (!_tglPtt.Checked && !_tglPtm.Checked && !_tglPtToggle.Checked) {
                            if (pttDown) { StartGlisten(_tglPtt); }
                            else if (ptmDown) { StartGlisten(_tglPtm); }
                            else if (toggleDown) { StartGlisten(_tglPtToggle); }
                        }
                    }
                    _hotkeyWasDown = anyDown;
                }
                // Fix 2: Star spin around "Press..." — advance frame continuously while capturing
                if (_captureGlowLabel != null) { _captureGlowFrame++; if (_pttCard != null) _pttCard.Invalidate(); }
                // Fix 3: Toggle glisten — driven by dedicated _glistenTimer (30ms, matches wizard speed)
            };
            _twinkleTimer.Start();
            // Shooting star animation — occasional streaks across card backgrounds
            _stars = new StarBackground(() => { InvalidateCards(); });
            FormClosing += (s, e) => { CancelAllCaptures(); if (WindowState == FormWindowState.Normal) { _settings.LastWindowX = Location.X; _settings.LastWindowY = Location.Y; } _settings.Save(); CleanupEnforcement(); if (_pollTimer != null) { _pollTimer.Stop(); _pollTimer.Dispose(); } if (_twinkleTimer != null) { _twinkleTimer.Stop(); _twinkleTimer.Dispose(); } if (_hotkeyFlashTimer != null) { _hotkeyFlashTimer.Stop(); _hotkeyFlashTimer.Dispose(); } if (_flashFadeTimer != null) { _flashFadeTimer.Stop(); _flashFadeTimer.Dispose(); } if (_stars != null) _stars.Dispose(); if (_sliderRestoreMicTimer != null) { _sliderRestoreMicTimer.Stop(); _sliderRestoreMicTimer.Dispose(); } if (_sliderRestoreSpkTimer != null) { _sliderRestoreSpkTimer.Stop(); _sliderRestoreSpkTimer.Dispose(); } if (_updateShimmerTimer != null) { _updateShimmerTimer.Stop(); _updateShimmerTimer.Dispose(); } if (_saveOrbitTimer != null) { _saveOrbitTimer.Stop(); _saveOrbitTimer.Dispose(); } if (_glistenTimer != null) { _glistenTimer.Stop(); _glistenTimer.Dispose(); } if (_voiceMeterTimer != null) { _voiceMeterTimer.Stop(); _voiceMeterTimer.Dispose(); } if (_pushToTalk != null) _pushToTalk.StopPeakMonitor(); };
        }

        private Size _defaultSize;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
        private Timer _hotkeyFlashTimer; // Used in FlashModeToggles guard check
        private bool _hotkeyWasDown;
        private bool[] _optModeActive = new bool[3]; // persistent held-key highlight: [ptt, ptm, toggle]
        // Fix 2: Star spin around "Press..." hotkey label
        private Label _captureGlowLabel;
        private int _captureGlowFrame;
        // Fix 3: Toggle-on glisten animation (ported from WelcomeForm)
        private ToggleSwitch _tglGlistenTarget;
        private int _tglGlistenFrame;
        private Timer _glistenTimer;

        // === Key capture is handled entirely by AudioSettings.StartCapture() ===
        // No local timer, no local polling, no boolean flags.
        // BeginCapture() and OnCaptureComplete() below are the single entry/exit points.

        protected override void WndProc(ref Message m)
        {
            // Title bar double-click: reset to default size instead of maximize
            if (m.Msg == WM_NCLBUTTONDBLCLK)
            {
                if (WindowState == FormWindowState.Maximized)
                    WindowState = FormWindowState.Normal;
                Size = _defaultSize;
                var screen = Screen.FromControl(this);
                Location = new Point(
                    screen.WorkingArea.X + (screen.WorkingArea.Width - Width) / 2,
                    screen.WorkingArea.Y + (screen.WorkingArea.Height - Height) / 2);
                return;
            }
            base.WndProc(ref m);
        }

        void InvalidateCards() {
            try {
                _contentPanel.Invalidate(false); // background stars + shooting stars
                // Only invalidate the active card panel itself, not every child control
                // Children inherit the double-buffered background via their own Paint handlers
                // which only need refreshing when twinkle changes (not shooting star movement)
                if (_footer != null) _footer.Invalidate(false);
            } catch { }
        }

        // Full invalidation — used by twinkle timer when stars change
        // Only invalidates the active card panel — CardToggle/CardSlider/CardIcon/PaintedLabel
        // are all drawn in the card's Paint handler. Real child controls (buttons, labels, NUDs)
        // have opaque backgrounds and don't need star bg updates every tick.
        void InvalidateCardsDeep() {
            try {
                _contentPanel.Invalidate(false);
                for (int i = 0; i < 6; i++) {
                    if (_panes[i] != null && _panes[i].Visible) {
                        foreach (Control c in _panes[i].Controls) {
                            if (c is BufferedPanel) c.Invalidate(false); // card panels only
                        }
                        break;
                    }
                }
                if (_footer != null) _footer.Invalidate(false);
            } catch { }
        }

        private DateTime _lastAppVolRefresh = DateTime.MinValue;

        void UpdateCurrent() {
            try {
                float mic = Audio.GetMicVolume(), spk = Audio.GetSpeakerVolume();

                // Keep mic current volume slider in sync (if user changed vol externally)
                if (_micCurVolSlider != null && !_micCurVolSlider.Capture && mic >= 0) {
                    int mv = (int)mic;
                    if (_micCurVolSlider.Value != mv) { _loading = true; _micCurVolSlider.Value = mv; _lblMicCurVolPct.Text = mv + "%"; _loading = false; }
                }
                // Keep speaker current volume slider in sync
                if (_spkCurVolSlider != null && !_spkCurVolSlider.Capture && spk >= 0) {
                    int sv = (int)spk;
                    if (_spkCurVolSlider.Value != sv) { _loading = true; _spkCurVolSlider.Value = sv; _lblSpkCurVolPct.Text = sv + "%"; _loading = false; }
                }
                // Keep mute icons in sync
                try { if (_micMuteIcon != null) { bool muted = Audio.GetMicMute(); if (_micMuteIcon.Checked == muted) { _micMuteIcon.Checked = !muted; } } } catch {}
                try { if (_spkMuteIcon != null) { bool muted = Audio.GetSpeakerMute(); if (_spkMuteIcon.Checked == muted) { _spkMuteIcon.Checked = !muted; } } } catch {}

                if (_volCard != null) _volCard.Invalidate();

                // Live-update app row effective volumes (throttled to every 2s)
                if (_appRows != null && _appRows.Count > 0 && (DateTime.UtcNow - _lastAppVolRefresh).TotalMilliseconds >= 2000) {
                    _lastAppVolRefresh = DateTime.UtcNow;
                    float masterVol = spk >= 0 ? spk : 100f;
                    System.Collections.Generic.List<AudioSession> sessions = null;
                    try { sessions = Audio.GetAudioSessions(); } catch { }
                    if (sessions != null) {
                        var liveVols = new System.Collections.Generic.Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                        foreach (var ls in sessions)
                            if (!liveVols.ContainsKey(ls.ProcessName))
                                liveVols[ls.ProcessName] = ls.Volume;
                        foreach (var ar in _appRows) {
                            float sv;
                            int eff = liveVols.TryGetValue(ar.Name, out sv) ? (int)((sv / 100f) * masterVol) : -1;
                            if (eff != ar.CurrentVolume) {
                                ar.CurrentVolume = eff;
                                if (ar.CurrentLabel != null) {
                                    ar.CurrentLabel.Text = eff >= 0 ? eff + "%" : "\u2014";
                                    ar.CurrentLabel.ForeColor = eff >= 0 ? Color.FromArgb(80, 200, 120) : TXT4;
                                }
                            }
                            // Keep target label's master% portion current
                            if (ar.VolLabel != null && ar.Slider != null) {
                                ar.VolLabel.Text = ar.Slider.Value + "% / " + (int)masterVol + "%";
                            }
                        }
                        // Keep system vol slider in sync (if user changed vol externally)
                        if (_sysVolSlider != null && !_sysVolSlider.Capture) {
                            try { int sv = (int)masterVol; if (_sysVolSlider.Value != sv) { _loading = true; _sysVolSlider.Value = sv; _lblSysVolPct.Text = sv + "%"; _loading = false; } } catch {}
                        }
                        // Keep Apps page mute icon in sync
                        try { if (_appsSysMuteIcon != null) { bool muted = Audio.GetSpeakerMute(); if (_appsSysMuteIcon.Checked == muted) { _appsSysMuteIcon.Checked = !muted; } } } catch {}
                    }
                }
            } catch { }
        }

        /// <summary>
        /// Smoothly animates the volume slider back to the pre-lock snapshot value.
        /// TrayApp handles the actual audio hardware restore separately.
        /// </summary>
        void AnimateSliderRestore(bool isMic)
        {
            int target = isMic ? _micPreLockVol : _spkPreLockVol;
            if (target < 0) return; // No snapshot

            var slider = isMic ? _trkMicVol : _trkSpkVol;
            if (slider == null) return;

            // Kill existing animation
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
                    if (!IsDisposed) {
                        slider.Value = target;
                        if (isMic) { _audio.MicLockVolume = target; _micPreLockVol = -1; try { Audio.SetMicVolume(target); } catch { } }
                        else { _audio.SpeakerLockVolume = target; _spkPreLockVol = -1; try { Audio.SetSpeakerVolume(target); } catch { } }
                    }
                    return;
                }
                int newVal = start + (int)(stepSize * step);
                newVal = Math.Max(0, Math.Min(100, newVal));
                slider.Value = newVal;
            };

            if (isMic) _sliderRestoreMicTimer = timer; else _sliderRestoreSpkTimer = timer;
            timer.Start();
        }

        void BuildSidebar() {
            // =================================================================
            // WinForms dock rule: LAST added to Controls = docked FIRST.
            // So: Fill FIRST (docked last, gets remaining), Top LAST (docked first).
            // =================================================================

            // === 1. NAV (Dock.Fill) — added FIRST so it docks LAST ===
            _navPanels = new Panel[6]; _navLabels = new Label[6]; _navAccents = new Panel[6];
            var navBox = new Panel { Dock = DockStyle.Fill, BackColor = SB_BG, Padding = Dpi.Pad(0, 6, 0, 0) };
            for (int i = 5; i >= 0; i--) {
                int idx = i;
                bool isSub = (idx == 1); // Voice is a sub-item under Mic
                int navH = isSub ? Dpi.S(28) : Dpi.S(34);
                var nav = new Panel { Dock = DockStyle.Top, Height = navH, BackColor = SB_BG };
                int acW = isSub ? Dpi.S(2) : Dpi.S(3); // sub-item gets thinner accent bar
                var ac = new Panel { Location = new Point(0, 0), Size = new Size(acW, navH), BackColor = SB_BG };
                nav.Controls.Add(ac);
                int lblX = isSub ? 32 : 20; // sub-item indented
                float lblSz = isSub ? 8.25f : 9f;
                var lbl = new Label { Text = NAV[idx], Font = new Font("Segoe UI", lblSz), ForeColor = TXT3, AutoSize = false, Location = Dpi.Pt(lblX, isSub ? 5 : 7), Size = Dpi.Size(SB_W - lblX - 4, 20) };
                nav.Controls.Add(lbl);
                nav.Click += (s2,e2) => SwitchPane(idx); lbl.Click += (s2,e2) => SwitchPane(idx);
                nav.MouseEnter += (s2,e2) => { if (idx != _activePane) nav.BackColor = HOVER; };
                nav.MouseLeave += (s2,e2) => { if (idx != _activePane) nav.BackColor = SB_BG; };
                lbl.MouseEnter += (s2,e2) => { if (idx != _activePane) nav.BackColor = HOVER; };
                lbl.MouseLeave += (s2,e2) => { if (idx != _activePane) nav.BackColor = SB_BG; };
                _navPanels[idx] = nav; _navLabels[idx] = lbl; _navAccents[idx] = ac;
                navBox.Controls.Add(nav);
            }
            // Voice sub-item starts collapsed — only visible when Mic or Voice is active
            _navPanels[1].Visible = false;
            _sidebar.Controls.Add(navBox);

            // === 2. FOOTER (Dock.Bottom) — added SECOND ===
            var foot = new Panel { Dock = DockStyle.Bottom, Height = Dpi.S(50), BackColor = SB_BG };
            foot.Paint += (s, e) => {
                var g = e.Graphics; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var p = new Pen(Color.FromArgb(20, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B))) g.DrawLine(p, 0, 0, foot.Width, 0);
                using (var f = new Font("Segoe UI", 7f)) using (var b = new SolidBrush(TXT4))
                    g.DrawString("by Andrew Ganter", f, b, Dpi.S(14), Dpi.S(12));
                using (var f = new Font("Segoe UI", 7f, FontStyle.Italic)) using (var b = new SolidBrush(Color.FromArgb(50, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B)))
                    g.DrawString("Your privacy, your rules", f, b, Dpi.S(14), Dpi.S(28));
            };
            foot.MouseDown += (s, e) => {
                int dividerY = Dpi.S(24);
                if (e.Button == MouseButtons.Right) {
                    // Right-click anywhere on footer — toggle star show mode
                    ToggleStarShow();
                    return;
                }
                if (e.Y < dividerY) {
                    // Left-click "by Andrew Ganter" — spawn burst of shooting stars
                    if (_stars.Shooting != null) for (int i = 0; i < 5; i++) _stars.Shooting.ForceLaunchMeteor();
                } else {
                    // Left-click "Your privacy, your rules" — spawn burst of celestial events
                    if (_stars.Celestial != null) for (int i = 0; i < 5; i++) _stars.Celestial.ForceLaunch();
                }
            };
            _sidebar.Controls.Add(foot);

            // === 3. HEADER (Dock.Top) — added LAST so it docks FIRST ===
            var hdr = new Panel { Dock = DockStyle.Top, Height = Dpi.S(58), BackColor = SB_BG };
            hdr.Paint += (s, e) => {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int otSz = Dpi.S(30);
                Mascot.DrawMascot(g, Dpi.S(14), Dpi.S(14), otSz);
                int tx = Dpi.S(14) + otSz + Dpi.S(8);
                using (var f = new Font("Segoe UI", 10f, FontStyle.Bold)) using (var b = new SolidBrush(Color.FromArgb(200,200,200)))
                    g.DrawString("Angry Audio", f, b, tx, Dpi.S(12));
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT3))
                    g.DrawString("v" + AppVersion.Version, f, b, tx, Dpi.S(30));
                using (var p = new Pen(Color.FromArgb(25, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B))) g.DrawLine(p, 0, hdr.Height - 1, hdr.Width, hdr.Height - 1);
            };
            _sidebar.Controls.Add(hdr);
        }

        void SwitchPane(int idx) {
            if (IsCapturingKey) { CancelAllCaptures(); } // Cancel capture and let them navigate
            if (_starShowMode) ToggleStarShow(); // exit star show if active
            _activePane = idx;
            _settings.LastActivePane = idx; _settings.Save();
            // Show/hide Voice sub-item — visible only when Mic or Voice is selected
            bool showVoiceSub = (idx == 0 || idx == 1);
            _navPanels[1].Visible = showVoiceSub;
            for (int i = 0; i < 6; i++) {
                bool a = i == idx;
                bool isSub = (i == 1);
                // Parent semi-highlight: when Voice is active, Mic gets a brighter label
                bool isParent = (i == 0 && idx == 1);
                Color lblCol = a ? ACC : (isParent ? TXT2 : TXT3);
                _navLabels[i].ForeColor = lblCol;
                _navLabels[i].Font = new Font("Segoe UI", isSub ? 8.25f : 9f, a ? FontStyle.Bold : FontStyle.Regular);
                _navAccents[i].BackColor = a ? ACC : (isParent ? Color.FromArgb(40, ACC.R, ACC.G, ACC.B) : SB_BG);
                _navPanels[i].BackColor = a ? HOVER : SB_BG;
                _panes[i].Visible = a;
            }
            // Start/stop peak monitor when Voice pane is shown/hidden
            if (_pushToTalk != null) {
                if (idx == 1) _pushToTalk.StartPeakMonitor();
                else _pushToTalk.StopPeakMonitor();
            }
        }

        void ToggleStarShow() {
            _starShowMode = !_starShowMode;
            if (_starShowMode) {
                // Capture the card mascot's position in contentPanel coordinates before hiding
                var activeCard = _panes[_activePane].Controls.Count > 0 ? _panes[_activePane].Controls[0] : null;
                if (activeCard != null) {
                    var cardScreen = activeCard.PointToScreen(Point.Empty);
                    var cpScreen = _contentPanel.PointToScreen(Point.Empty);
                    int cardInCpX = cardScreen.X - cpScreen.X;
                    int cardInCpY = cardScreen.Y - cpScreen.Y;
                    int msz = Dpi.S(120); // same size as card mascot
                    _savedMascotX = cardInCpX + activeCard.Width - msz - Dpi.S(8);
                    _savedMascotY = cardInCpY + activeCard.Height - msz - Dpi.S(8);
                }
                // Hide all panes and footer — just stars + sidebar
                for (int i = 0; i < 6; i++) _panes[i].Visible = false;
                _footer.Visible = false;
                // Launch a burst of meteors for the show
                if (_stars.Shooting != null) for (int i = 0; i < 8; i++) _stars.Shooting.ForceLaunchMeteor();
                if (_stars.Celestial != null) _stars.Celestial.ForceLaunch();
            } else {
                // Restore active pane and footer
                _panes[_activePane].Visible = true;
                _footer.Visible = true;
            }
            _contentPanel.Invalidate();
        }

        public void NavigateToPane(int idx) { if (idx >= 0 && idx < 6) SwitchPane(idx); }

        void BuildPanes() {
            _panes = new Panel[6];
            for (int i = 0; i < 6; i++) { _panes[i] = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false, BackColor = BG }; _contentPanel.Controls.Add(_panes[i]); }
            BuildPttPane(_panes[0]); BuildVoiceActivityPane(_panes[1]); BuildVolLockPane(_panes[2]); BuildAfkPane(_panes[3]); BuildAppsPane(_panes[4]); BuildGeneralPane(_panes[5]);
            // Restore last active pane
            int lastPane = _settings.LastActivePane;
            if (lastPane >= 0 && lastPane < 6) SwitchPane(lastPane); else SwitchPane(0);
        }

        // Title info for card painting
        private string[] _paneTitles = new string[6];
        private string[] _paneSubs = new string[6];
        private string[] _paneBadges = new string[6];

        // === PAINTED LABEL SYSTEM ===
        // All text is drawn in the card Paint handler — zero child controls, zero flicker.
        class PaintedLabel {
            public string Text;
            public int X, Y;
            public Font Font;
            public Color Color;
            public int MaxWidth; // 0 = auto
            public bool RightAlign; // true = X is right edge, text aligns right
            public bool Visible = true; // false = skip painting
        }
        Dictionary<Panel, List<PaintedLabel>> _cardLabels = new Dictionary<Panel, List<PaintedLabel>>();
        Dictionary<Panel, List<int>> _cardLines = new Dictionary<Panel, List<int>>(); // separator Y positions
        Dictionary<Panel, int> _cardBottomLimit = new Dictionary<Panel, int>(); // optional max card bottom (0 = use panel height)

        PaintedLabel AddText(Panel card, string text, int x, int y, float fontSize, Color color, FontStyle style = FontStyle.Regular, int maxW = 0) {
            if (!_cardLabels.ContainsKey(card)) _cardLabels[card] = new List<PaintedLabel>();
            var pl = new PaintedLabel { Text = text, X = Dpi.S(x), Y = Dpi.S(y), Font = new Font("Segoe UI", fontSize, style), Color = color, MaxWidth = maxW > 0 ? Dpi.S(maxW) : 0 };
            _cardLabels[card].Add(pl);
            return pl;
        }
        void AddLine(Panel card, int y) {
            if (!_cardLines.ContainsKey(card)) _cardLines[card] = new List<int>();
            _cardLines[card].Add(Dpi.S(y));
        }

        Panel MakeCard(int paneIdx, string title, string sub = null, string badge = null) {
            _paneTitles[paneIdx] = title;
            _paneSubs[paneIdx] = sub;
            _paneBadges[paneIdx] = badge;
            var c = new BufferedPanel { Dock = DockStyle.Fill, BackColor = BG };
            c.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int cardTop = Dpi.S(sub != null ? 46 : 38);
                int cardBottom = c.Height - 1;
                if (_cardBottomLimit.ContainsKey(c) && _cardBottomLimit[c] > 0)
                    cardBottom = Math.Min(cardBottom, _cardBottomLimit[c]);
                int rad = Dpi.S(6);

                // 1) Unified starfield — same stars as entire form, seamless
                PaintUnifiedStars(g, c);

                // 2) Frosted glass card — tint + dimmed unified stars
                var cardRect = new Rectangle(0, cardTop, c.Width - 1, cardBottom - cardTop);
                var clipPath = new GraphicsPath();
                int d = rad * 2;
                clipPath.AddArc(cardRect.X, cardRect.Y, d, d, 180, 90);
                clipPath.AddArc(cardRect.Right - d, cardRect.Y, d, d, 270, 90);
                clipPath.AddArc(cardRect.Right - d, cardRect.Bottom - d, d, d, 0, 90);
                clipPath.AddArc(cardRect.X, cardRect.Bottom - d, d, d, 90, 90);
                clipPath.CloseFigure();
                // Dark tint over card area — visible grey for premium look
                using (var tint = new SolidBrush(DarkTheme.GlassTint))
                    g.FillPath(tint, clipPath);
                // Dimmed unified stars through the glass
                var oldClip = g.Clip;
                g.SetClip(clipPath, CombineMode.Replace);
                PaintUnifiedStars(g, c, 0.35f, false);
                g.Clip = oldClip;
                clipPath.Dispose();

                // 3) Mascot watermark (painted ABOVE frosted glass)
                try {
                    int msz = Dpi.S(120);
                    Mascot.DrawMascotWithOpacity(g, c.Width - msz - Dpi.S(8), c.Height - msz - Dpi.S(8), msz, 0.25f);
                } catch { }

                // 4) Card border
                using (var pen = new Pen(CARD_BDR)) RoundRect(g, pen, cardRect, rad);
                using (var pen = new Pen(Color.FromArgb(20, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B), Dpi.PenW(1)))
                    g.DrawLine(pen, Dpi.S(10), cardTop, c.Width - Dpi.S(10), cardTop);

                // 5) Separator lines
                if (_cardLines.ContainsKey(c)) {
                    using (var pen = new Pen(CARD_BDR))
                        foreach (int ly in _cardLines[c])
                            g.DrawLine(pen, Dpi.S(16), ly, c.Width - Dpi.S(16), ly);
                }

                // 6) Title text (above card — bright starfield behind)
                using (var f = new Font("Segoe UI", 13f, FontStyle.Bold))
                using (var b = new SolidBrush(TXT))
                    g.DrawString(title, f, b, Dpi.S(16), Dpi.S(4));
                if (badge != null)
                {
                    float tw;
                    using (var f = new Font("Segoe UI", 13f, FontStyle.Bold))
                        tw = g.MeasureString(title, f).Width;
                    using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                    using (var b = new SolidBrush(DarkTheme.Amber))
                        g.DrawString(badge, f, b, Dpi.S(16) + tw + Dpi.S(8), Dpi.S(10));
                }
                if (sub != null)
                {
                    using (var f = new Font("Segoe UI", 8f)) {
                        using (var b = new SolidBrush(TXT4))
                            g.DrawString(sub, f, b, Dpi.S(16), Dpi.S(29));
                    }
                }

                // 7) All painted labels — drawn directly on glass, clipped to card area
                if (_cardLabels.ContainsKey(c)) {
                    var oldClip2 = g.Clip;
                    g.SetClip(cardRect);
                    foreach (var lbl in _cardLabels[c]) {
                        if (!lbl.Visible) continue;
                        using (var b = new SolidBrush(lbl.Color)) {
                            if (lbl.MaxWidth > 0)
                                g.DrawString(lbl.Text, lbl.Font, b, new RectangleF(lbl.X, lbl.Y, lbl.MaxWidth, 100));
                            else if (lbl.RightAlign) {
                                var sz = g.MeasureString(lbl.Text, lbl.Font);
                                g.DrawString(lbl.Text, lbl.Font, b, lbl.X - sz.Width, lbl.Y);
                            }
                            else
                                g.DrawString(lbl.Text, lbl.Font, b, lbl.X, lbl.Y);
                        }
                    }
                    g.Clip = oldClip2;
                }

                // PTT/Speaker warning text — drawn directly in card paint handler
                if (c == _volCard) {
                    var oldClip3 = g.Clip;
                    g.SetClip(cardRect);
                    using (var warnFont = new Font("Segoe UI", 7.5f))
                    using (var warnBrush = new SolidBrush(Color.FromArgb(220, 180, 80))) {
                        if (_pttWarningY > 0)
                            g.DrawString(_pttWarningText != null && _pttWarningText.Length > 0 ? _pttWarningText : "\u26A0  No active mic protections.", warnFont, warnBrush, Dpi.S(64), _pttWarningY);
                        if (_spkWarningY > 0)
                            g.DrawString(_spkWarningText != null && _spkWarningText.Length > 0 ? _spkWarningText : "\u26A0  No active speaker protections.", warnFont, warnBrush, Dpi.S(64), _spkWarningY);
                    }
                    g.Clip = oldClip3;
                }

                // Fix 2: Star spin around "Press..." hotkey label
                if (_captureGlowLabel != null && _captureGlowLabel.Parent == c) {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
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

                // Fix 3: Toggle-on glisten animation
                if (_tglGlistenTarget != null && _tglGlistenTarget.Parent == c && _tglGlistenFrame > 0 && _tglGlistenFrame <= 35) {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    float ct = _tglGlistenFrame / 35f;
                    float tglW = _tglGlistenTarget.Width, tglH = _tglGlistenTarget.Height;
                    float tglCx2 = _tglGlistenTarget.Left + tglW / 2f;
                    float tglCy2 = _tglGlistenTarget.Top + tglH / 2f;
                    float glRingR = Dpi.S(13) + (float)Math.Sin(ct * Math.PI) * Dpi.S(3);
                    float glBaseAngle2 = _tglGlistenFrame * 50f * (float)(Math.PI / 180.0);
                    float ringFade = ct < 0.8f ? 1f : (1f - ct) / 0.2f;
                    for (int rd = 0; rd < 4; rd++) {
                        float dotAngle = glBaseAngle2 + rd * (float)(Math.PI * 2.0 / 4);
                        for (int tail = 0; tail < 4; tail++) {
                            float tailAngle = dotAngle - tail * 0.22f;
                            float tx = tglCx2 + (float)Math.Cos(tailAngle) * glRingR;
                            float ty = tglCy2 + (float)Math.Sin(tailAngle) * glRingR;
                            float tr = Dpi.S(2) * (1f - tail * 0.2f);
                            int ta2 = (int)(ringFade * (240 - tail * 55));
                            if (ta2 <= 0) continue;
                            Color dc = tail == 0 ? Color.FromArgb(ta2, 255, 255, 255) : Color.FromArgb(ta2, 100, 200, 255);
                            using (var br = new SolidBrush(dc))
                                g.FillEllipse(br, tx - tr, ty - tr, tr*2, tr*2);
                        }
                    }
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

                // Hotkey flash highlight — painted directly on card, no child panel
                if (_flashTarget != null && _flashTarget.Parent == c && _flashAlpha > 0) {
                    int hlX = Dpi.S(4), hlY = _flashTarget.Top - Dpi.S(4);
                    int hlW = c.Width - Dpi.S(8), hlH = Dpi.S(42);
                    using (var b = new SolidBrush(Color.FromArgb(_flashAlpha, ACC.R, ACC.G, ACC.B)))
                        g.FillRectangle(b, hlX, hlY, hlW, hlH);
                    int borderA = (int)(_flashAlpha * 3.5); if (borderA > 255) borderA = 255;
                    using (var pen = new Pen(Color.FromArgb(borderA, ACC.R, ACC.G, ACC.B), 2f))
                        g.DrawRectangle(pen, hlX + 1, hlY + 1, hlW - 3, hlH - 3);
                }

                // Persistent held-key highlight — stays lit while hotkey is held (matches WelcomeForm)
                if (c == _pttCard) {
                    ToggleSwitch[] modeTgls = { _tglPtt, _tglPtm, _tglPtToggle };
                    for (int mi = 0; mi < 3; mi++) {
                        if (!_optModeActive[mi] || modeTgls[mi] == null) continue;
                        int sTop = modeTgls[mi].Top - Dpi.S(4);
                        int sBot = modeTgls[mi].Top + Dpi.S(38);
                        int sLeft = Dpi.S(10), sRight = c.Width - Dpi.S(10);
                        DarkTheme.PaintBreathingGlow(g, new Rectangle(sLeft, sTop, sRight - sLeft, sBot - sTop), ACC, Dpi.S(6));
                    }
                }

                // AFK card — split-color PAUSE warnings
                if (c == _afkCard) {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    if (_afkWarn1Y > 0) PaintPauseWarning(g, Dpi.S(64), Dpi.S(_afkWarn1Y), 7.5f);
                    if (_afkWarn2Y > 0) PaintPauseWarning(g, Dpi.S(64), Dpi.S(_afkWarn2Y), 7.5f);
                }

                // Card icons — eye/speaker painted directly, zero compositing
                List<CardIcon> icons;
                if (_cardIconMap.TryGetValue(c, out icons)) {
                    foreach (var icon in icons) icon.Paint(g, ACC);
                }
                // Card toggles — painted directly, real ToggleSwitch hidden
                List<CardToggle> ctgls;
                if (_cardToggleMap.TryGetValue(c, out ctgls)) {
                    foreach (var ct in ctgls) ct.Paint(g, ACC);
                }
                // Card sliders — painted directly, real SlickSlider hidden
                List<CardSlider> cslds;
                if (_cardSliderMap.TryGetValue(c, out cslds)) {
                    foreach (var cs in cslds) cs.Paint(g, ACC);
                }
            };
            // Mouse handlers for card icons + card toggles + card sliders
            c.MouseDown += (s, e2) => {
                if (e2.Button != MouseButtons.Left) return;
                List<CardSlider> cslds2;
                if (_cardSliderMap.TryGetValue(c, out cslds2)) {
                    foreach (var cs in cslds2) {
                        if (cs.HitTest(e2.Location) && cs.Source != null && cs.Source.Enabled) {
                            cs.Dragging = true; cs.Source.Value = cs.XToValue(e2.X); c.Invalidate(); return;
                        }
                    }
                }
            };
            c.MouseUp += (s, e2) => {
                List<CardSlider> cslds2;
                if (_cardSliderMap.TryGetValue(c, out cslds2)) {
                    foreach (var cs in cslds2) {
                        if (cs.Dragging) { cs.Dragging = false; c.Invalidate(); if (cs.Source != null) cs.Source.FireDragCompleted(); }
                    }
                }
            };
            c.MouseClick += (s, e2) => {
                // Don't fire click if a slider was just dragged
                List<CardSlider> cslds2;
                if (_cardSliderMap.TryGetValue(c, out cslds2)) {
                    foreach (var cs in cslds2) { if (cs.HitTest(e2.Location)) return; }
                }
                List<CardToggle> ctgls2;
                if (_cardToggleMap.TryGetValue(c, out ctgls2)) {
                    foreach (var ct in ctgls2) {
                        if (ct.HitTest(e2.Location)) { ct.Click(); return; }
                    }
                }
                List<CardIcon> icons2;
                if (_cardIconMap.TryGetValue(c, out icons2)) {
                    foreach (var icon in icons2) {
                        if (icon.HitTest(e2.Location)) { icon.Toggle(); break; }
                    }
                }
            };
            c.MouseMove += (s, e2) => {
                // Slider drag
                List<CardSlider> cslds2;
                if (_cardSliderMap.TryGetValue(c, out cslds2)) {
                    foreach (var cs in cslds2) {
                        if (cs.Dragging && cs.Source != null) { cs.Source.Value = cs.XToValue(e2.X); c.Invalidate(); }
                    }
                }
                List<CardIcon> icons2;
                bool anyHover = false;
                if (_cardIconMap.TryGetValue(c, out icons2)) {
                    foreach (var icon in icons2) {
                        bool wasHover = icon.Hover;
                        icon.Hover = icon.HitTest(e2.Location);
                        if (icon.Hover) anyHover = true;
                        if (wasHover != icon.Hover) c.Invalidate();
                    }
                }
                List<CardToggle> ctgls2;
                if (_cardToggleMap.TryGetValue(c, out ctgls2)) {
                    foreach (var ct in ctgls2) {
                        bool wasH = ct.Hover;
                        ct.Hover = ct.HitTest(e2.Location);
                        if (ct.Hover) anyHover = true;
                        if (wasH != ct.Hover) c.Invalidate();
                    }
                }
                if (_cardSliderMap.TryGetValue(c, out cslds2)) {
                    foreach (var cs in cslds2) {
                        bool wasH = cs.Hover;
                        cs.Hover = cs.ThumbHitTest(e2.Location) || cs.Dragging;
                        if (cs.Hover) anyHover = true;
                        if (wasH != cs.Hover) c.Invalidate();
                    }
                }
                c.Cursor = anyHover ? Cursors.Hand : Cursors.Default;
            };
            c.MouseLeave += (s, e2) => {
                List<CardIcon> icons2;
                if (_cardIconMap.TryGetValue(c, out icons2)) {
                    foreach (var icon in icons2) { if (icon.Hover) { icon.Hover = false; c.Invalidate(); } }
                }
                List<CardToggle> ctgls2;
                if (_cardToggleMap.TryGetValue(c, out ctgls2)) {
                    foreach (var ct in ctgls2) { if (ct.Hover) { ct.Hover = false; c.Invalidate(); } }
                }
                List<CardSlider> cslds2;
                if (_cardSliderMap.TryGetValue(c, out cslds2)) {
                    foreach (var cs in cslds2) { if (cs.Hover) { cs.Hover = false; c.Invalidate(); } }
                }
            };
            return c;
        }
        static void RoundRect(Graphics g, Pen p, Rectangle r, int rad) { var path=new GraphicsPath(); int d=rad*2; path.AddArc(r.X,r.Y,d,d,180,90); path.AddArc(r.Right-d,r.Y,d,d,270,90); path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.X,r.Bottom-d,d,d,90,90); path.CloseFigure(); g.DrawPath(p,path); path.Dispose(); }

        // === UNIFIED STARFIELD ===
        // ONE starfield across the entire form — no zones, no seams, no wasted GPU
        // Star rendering — ALL surfaces use the shared StarBackground class
        // No per-form star cache, no per-form ShootingStar/CelestialEvents

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
            // Paint frosted glass tint so controls match the card background, not raw stars
            using (var tint = new SolidBrush(DarkTheme.GlassTint))
                g.FillRectangle(tint, 0, 0, child.Width, child.Height);
            // Dimmed stars through the glass — matches card's 3rd paint layer
            PaintUnifiedStars(g, child, 0.35f, false);
            // Flash highlight — if this child is within the flash region, tint it
            if (_flashTarget != null && _flashAlpha > 0 && child.Parent == _flashTarget.Parent) {
                int hlY = _flashTarget.Top - Dpi.S(4);
                int hlH = Dpi.S(42);
                int childBottom = child.Top + child.Height;
                if (child.Top >= hlY && childBottom <= hlY + hlH) {
                    using (var b = new SolidBrush(Color.FromArgb(_flashAlpha, ACC.R, ACC.G, ACC.B)))
                        g.FillRectangle(b, 0, 0, child.Width, child.Height);
                }
            }
        }

        // Tgl now only creates ToggleSwitch — text is painted
        ToggleSwitch Tgl(string label, string sub, int y, Panel card) {
            var t = new ToggleSwitch { Location = Dpi.Pt(16, y) }; t.PaintParentBg = PaintCardBg; card.Controls.Add(t);
            t.Visible = false; // Hidden — CardToggle paints instead
            var ct = new CardToggle { PixelX = Dpi.S(16), PixelY = Dpi.S(y), Source = t, Card = card };
            if (!_cardToggleMap.ContainsKey(card)) _cardToggleMap[card] = new List<CardToggle>();
            _cardToggleMap[card].Add(ct);
            AddText(card, label, 64, y, 9.5f, TXT);
            if (sub != null) AddText(card, sub, 64, y + 19, 7.5f, TXT3);
            return t;
        }
        NumericUpDown Nud(int min, int max, int val, int x, int y, int w) { var n = new PaddedNumericUpDown { Minimum=min,Maximum=max,Value=val, Location=Dpi.Pt(x,y), Size=Dpi.Size(w,24), BackColor=INPUT_BG, ForeColor=TXT, Font=new Font("Segoe UI",9f), BorderStyle=BorderStyle.FixedSingle, TextAlign=HorizontalAlignment.Center };
            n.Leave += (s,e) => { try { int v; if (int.TryParse(n.Text, out v)) { v = Math.Max(min, Math.Min(max, v)); n.Value = v; } } catch { n.Value = val; } };
            return n; }

        // Dynamic painted labels that update at runtime
        private PaintedLabel _plMicVol, _plSpkVol;
        private Panel _volCard; // reference for invalidation on value change
        private Panel _pttCard; // reference for animation invalidation
        private Panel _afkCard; // reference for split-color warning paint
        private int _afkWarn1Y, _afkWarn2Y; // Y positions of PAUSE warnings on AFK card

        void BuildAfkPane(Panel pane) {
            var card = MakeCard(3, "AFK Protection", "When you return, audio fades back in over 2s so you're never startled.");
            _afkCard = card;
            int y = 56;

            // ── MICROPHONE SECTION ──
            _tglAfkMic = Tgl("Microphone Auto-Mute", "Automatically mutes your mic when you step away.", y, card);
            _tglAfkMic.CheckedChanged += (s,e) => { if (!_loading) {
                _audio.AfkMicEnabled = _tglAfkMic.Checked;
                _pttWarningText = BuildPttWarningText();
                if (_tglAfkMic.Checked) { _loading = true; if (_tglPtt.Checked) _tglPtt.Checked = false; if (_tglPtm.Checked) _tglPtm.Checked = false; if (_tglPtToggle.Checked) _tglPtToggle.Checked = false; _loading = false; }
            } };
            y += 48;
            AddText(card, "After", 64, y+3, 9f, TXT2);
            _nudAfkMicSec = Nud(5,3600,60,104,y,60); card.Controls.Add(_nudAfkMicSec);
            _nudAfkMicSec.ValueChanged += (s,e) => { if (!_loading) { _audio.AfkMicSec = (int)_nudAfkMicSec.Value; _pttWarningText = BuildPttWarningText(); } };
            AddText(card, "seconds of inactivity", 170, y+3, 9f, TXT2);
            y+=32;
            _afkWarn1Y = y + 4; // painted with split colors in MakeCard handler
            y += 18;
            AddText(card, "Mutes all microphones system-wide \u2014 headset, camera mic, USB devices.", 64, y+4, 7.5f, Color.FromArgb(100, 180, 255));
            y+=34; AddLine(card, y); y+=10;

            // ── SPEAKERS SECTION ──
            _tglAfkSpk = Tgl("Speaker Auto-Mute", "Fades out system audio when you're away.", y, card);
            _tglAfkSpk.CheckedChanged += (s,e) => { if (!_loading) { _audio.AfkSpeakerEnabled = _tglAfkSpk.Checked; _spkWarningText = BuildSpkWarningText(); } };
            y += 48;
            AddText(card, "After", 64, y+3, 9f, TXT2);
            _nudAfkSpkSec = Nud(5,3600,60,104,y,60); card.Controls.Add(_nudAfkSpkSec);
            _nudAfkSpkSec.ValueChanged += (s,e) => { if (!_loading) { _audio.AfkSpeakerSec = (int)_nudAfkSpkSec.Value; _spkWarningText = BuildSpkWarningText(); } };
            AddText(card, "seconds of inactivity", 170, y+3, 9f, TXT2);
            y+=32;
            _afkWarn2Y = y + 4; // painted with split colors in MakeCard handler
            y += 18;
            AddText(card, "Mutes all speakers system-wide \u2014 headphones, monitors, Bluetooth devices.", 64, y+4, 7.5f, Color.FromArgb(100, 180, 255));

            pane.Controls.Add(card);
        }

        void BuildPttPane(Panel pane) {
            var card = MakeCard(0, "Mic Protection", "Control when your mic is live. Enable one or more modes below.");
            _pttCard = card;
            int ki = 64;

            // Mic Security button — opens Windows microphone permissions, right-aligned in header
            var btnMicSec = new Button{Text="Mic Security",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(90,26),ForeColor=ACC,BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false};
            btnMicSec.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            btnMicSec.MouseEnter+=(s,e)=>{btnMicSec.BackColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);btnMicSec.ForeColor=Color.FromArgb(Math.Min(255,ACC.R+50),Math.Min(255,ACC.G+50),Math.Min(255,ACC.B+50));};
            btnMicSec.MouseLeave+=(s,e)=>{btnMicSec.BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);btnMicSec.ForeColor=ACC;};
            btnMicSec.Click+=(s,e)=>{ try { System.Diagnostics.Process.Start("ms-settings:privacy-microphone"); } catch { } };
            card.Controls.Add(btnMicSec);
            card.Resize += (s2,e2) => { btnMicSec.Location = new Point(card.Width - Dpi.S(90) - Dpi.S(2), Dpi.S(18)); };

            // MODE 1: PUSH-TO-TALK
            int y = 56;
            _tglPtt = new ToggleSwitch { Location = Dpi.Pt(16, y) }; _tglPtt.PaintParentBg = PaintCardBg; card.Controls.Add(_tglPtt);
            _tglPtt.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(16), PixelY = Dpi.S(y), Source = _tglPtt, Card = card };
              if (!_cardToggleMap.ContainsKey(card)) _cardToggleMap[card] = new List<CardToggle>(); _cardToggleMap[card].Add(ct); }
            AddText(card, "Push-to-Talk", 64, y, 10f, TXT, FontStyle.Bold);
            AddText(card, "Silent until you hold the key to open.", 64, y+20, 7.5f, TXT3);
            _tglPtt.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglPtt.Checked && (IsCapturingKey || CaptureCooldownActive())) { _loading=true; _tglPtt.Checked=false; _loading=false; return; }
                if (_tglPtt.Checked && _pttKeyCode <= 0) { _loading=true; _tglPtt.Checked=false; _loading=false; BeginCapture(CaptureTarget.PttKey1, _lblPttKey); return; }
                if (!_tglPtt.Checked) { CancelAllCaptures();_pttKeyCode=0;_pttKeyCode2=0;_pttKeyCode3=0;_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;CompactKeys();LayoutPttKeys();_audio.DisablePttMode(); }
                else {
                    // PTT ON: if Toggle is off, force PTM off (mutually exclusive without Toggle)
                    if (!_tglPtToggle.Checked && _tglPtm.Checked) {
                        _loading=true; _tglPtm.Checked=false; _loading=false;
                        _ptmKeyCode=0;_ptmKeyCode2=0;_ptmKeyCode3=0;_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;LayoutPtmKeys();_audio.DisablePtmMode();
                    }
                    _audio.PttEnabled = true; _audio.PttKey = _pttKeyCode; _audio.PttKey2 = _pttKeyCode2; _audio.PttKey3 = _pttKeyCode3;
                    // Disable VA — mutually exclusive (must set backend too, not just visual)
                    if (_tglVoiceActivity != null && _tglVoiceActivity.Checked) { _loading=true; _tglVoiceActivity.Checked=false; _loading=false; _audio.VoiceActivityEnabled = false; }
                }
                RefreshPttHotkeyLabel();
            } };
            int hk1Y = y + 46;
            AddText(card, "Hotkey:", ki, hk1Y+4, 8f, TXT3);
            _lblPttKey = new Label{Text=_pttKeyCode > 0 ? PushToTalk.GetKeyName(_pttKeyCode) : "Add Key",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(80,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(ki+50,hk1Y)};
            _lblPttKey.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPttKey.Width-1,_lblPttKey.Height-1); };
            _lblPttKey.MouseEnter += (s,e) => { if(!IsCapturingKey) _lblPttKey.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPttKey.MouseLeave += (s,e) => { if(!IsCapturingKey) _lblPttKey.BackColor = INPUT_BG; };
            _lblPttKey.Click += (s,e) => BeginCapture(CaptureTarget.PttKey1, _lblPttKey); card.Controls.Add(_lblPttKey);
            _chkKey1Overlay = MakeOverlayCheck(y, card, _key1ShowOverlay, (v) => { _key1ShowOverlay = v; _audio.PttShowOverlay = v; });
            var _chkPttSound = MakeSoundCheck(y, card, _settings.PttSoundFeedback, (v) => { _audio.PttSoundFeedback = v; });
            var _drpPttSound = MakeSoundDropdown(y, card, _settings.SoundFeedbackType, (v) => { _audio.SoundFeedbackType = v; });
            // --- HORIZONTAL KEY ROW: Key1 [x] Key2 [x] Key3 [x] [Add Key] ---
            // All keys on a single row. "Add Key" appears after the last assigned key.
            int kbW = 80; // key box width (base)
            int rmW = 18; // remove button width

            _btnAddKey2 = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false};
            _btnAddKey2.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnAddKey2.MouseEnter += (s,e) => { _btnAddKey2.BackColor=Color.FromArgb(ACC.R/5,ACC.G/5,ACC.B/5); };
            _btnAddKey2.MouseLeave += (s,e) => { _btnAddKey2.BackColor=Color.FromArgb(20,20,20); };
            _btnAddKey2.Click += (s,e) => BeginCapture(CaptureTarget.PttKey2, _lblPttKey2); card.Controls.Add(_btnAddKey2);
            // Key 2 box (inline, right of key 1)
            _lblKey2Label = new Label{Text="",Font=new Font("Segoe UI",8f),ForeColor=TXT3,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(0,0),Visible=false}; card.Controls.Add(_lblKey2Label); // hidden, kept for compat
            _lblPttKey2 = new Label{Text=PushToTalk.GetKeyName(_pttKeyCode2),Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(kbW,26),TextAlign=ContentAlignment.MiddleCenter};
            _lblPttKey2.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPttKey2.Width-1,_lblPttKey2.Height-1); };
            _lblPttKey2.MouseEnter += (s,e) => { if(!IsCapturingKey) _lblPttKey2.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPttKey2.MouseLeave += (s,e) => { if(!IsCapturingKey) _lblPttKey2.BackColor = INPUT_BG; };
            _lblPttKey2.Click += (s,e) => BeginCapture(CaptureTarget.PttKey2, _lblPttKey2); card.Controls.Add(_lblPttKey2);
            _chkKey2Overlay = null;
            _lblKey2Hint = new Label{Text="",Font=new Font("Segoe UI",7f),ForeColor=TXT4,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(0,0),Visible=false}; card.Controls.Add(_lblKey2Hint);
            _btnRemoveKey2 = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false};
            _btnRemoveKey2.FlatAppearance.BorderSize=0; _btnRemoveKey2.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnRemoveKey2.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverRm2 = false;
            _btnRemoveKey2.MouseEnter += (s,e) => { _hoverRm2=true; _btnRemoveKey2.Invalidate(); };
            _btnRemoveKey2.MouseLeave += (s,e) => { _hoverRm2=false; _btnRemoveKey2.Invalidate(); };
            _btnRemoveKey2.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnRemoveKey2.ClientRectangle, _hoverRm2); };
            _btnRemoveKey2.Click += (s,e) => { if (_pttKeyCode3 > 0) { _pttKeyCode2 = _pttKeyCode3; _lblPttKey2.Text = KeyName(_pttKeyCode2); _pttKeyCode3 = 0; } else { _pttKeyCode2 = 0; } LayoutPttKeys(); _audio.PttKey = _pttKeyCode; _audio.PttKey2 = _pttKeyCode2; _audio.PttKey3 = _pttKeyCode3; };
            card.Controls.Add(_btnRemoveKey2);
            // Key 3 box (inline, right of key 2)
            _lblKey3Label = new Label{Text="",Font=new Font("Segoe UI",8f),ForeColor=TXT3,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(0,0),Visible=false}; card.Controls.Add(_lblKey3Label);
            _lblPttKey3 = new Label{Text=PushToTalk.GetKeyName(_pttKeyCode3),Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(kbW,26),TextAlign=ContentAlignment.MiddleCenter};
            _lblPttKey3.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPttKey3.Width-1,_lblPttKey3.Height-1); };
            _lblPttKey3.MouseEnter += (s,e) => { if(!IsCapturingKey) _lblPttKey3.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPttKey3.MouseLeave += (s,e) => { if(!IsCapturingKey) _lblPttKey3.BackColor = INPUT_BG; };
            _lblPttKey3.Click += (s,e) => BeginCapture(CaptureTarget.PttKey3, _lblPttKey3); card.Controls.Add(_lblPttKey3);
            _chkKey3Overlay = null;
            _btnRemoveKey3 = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false};
            _btnRemoveKey3.FlatAppearance.BorderSize=0; _btnRemoveKey3.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnRemoveKey3.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverRm3 = false;
            _btnRemoveKey3.MouseEnter += (s,e) => { _hoverRm3=true; _btnRemoveKey3.Invalidate(); };
            _btnRemoveKey3.MouseLeave += (s,e) => { _hoverRm3=false; _btnRemoveKey3.Invalidate(); };
            _btnRemoveKey3.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnRemoveKey3.ClientRectangle, _hoverRm3); };
            _btnRemoveKey3.Click += (s,e) => { _pttKeyCode3 = 0; LayoutPttKeys(); _audio.PttKey3 = 0; };
            card.Controls.Add(_btnRemoveKey3);
            _btnAddKey3 = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false};
            _btnAddKey3.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnAddKey3.MouseEnter += (s,e) => { _btnAddKey3.BackColor=Color.FromArgb(ACC.R/5,ACC.G/5,ACC.B/5); };
            _btnAddKey3.MouseLeave += (s,e) => { _btnAddKey3.BackColor=Color.FromArgb(20,20,20); };
            _btnAddKey3.Click += (s,e) => BeginCapture(CaptureTarget.PttKey3, _lblPttKey3); card.Controls.Add(_btnAddKey3);
            _lblKey3Hint = new Label{Text="",Font=new Font("Segoe UI",7f),ForeColor=TXT4,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(0,0),Visible=false}; card.Controls.Add(_lblKey3Hint);
            LayoutPttKeys();

            y = hk1Y + 34;
            AddLine(card, y); y += 10;

            // MODE 2: PUSH-TO-MUTE — with overlay icon
            _tglPtm = new ToggleSwitch { Location = Dpi.Pt(16, y) }; _tglPtm.PaintParentBg = PaintCardBg; card.Controls.Add(_tglPtm);
            _tglPtm.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(16), PixelY = Dpi.S(y), Source = _tglPtm, Card = card };
              _cardToggleMap[card].Add(ct); }
            AddText(card, "Push-to-Mute", 64, y, 10f, TXT, FontStyle.Bold);
            AddText(card, "Open until you hold the key to silence.", 64, y+20, 7.5f, TXT3);
            _tglPtm.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglPtm.Checked && (IsCapturingKey || CaptureCooldownActive())) { _loading=true; _tglPtm.Checked=false; _loading=false; return; }
                if (_tglPtm.Checked && _ptmKeyCode <= 0) { _loading=true; _tglPtm.Checked=false; _loading=false; BeginCapture(CaptureTarget.PtmKey1, _lblPtmKey); return; }
                if (!_tglPtm.Checked) { CancelAllCaptures();_ptmKeyCode=0;_ptmKeyCode2=0;_ptmKeyCode3=0;_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;LayoutPtmKeys();_audio.DisablePtmMode(); }
                else {
                    // PTM ON: if Toggle is off, force PTT off (mutually exclusive without Toggle)
                    if (!_tglPtToggle.Checked && _tglPtt.Checked) {
                        _loading=true; _tglPtt.Checked=false; _loading=false;
                        _pttKeyCode=0;_pttKeyCode2=0;_pttKeyCode3=0;_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;CompactKeys();LayoutPttKeys();_audio.DisablePttMode();
                    }
                    _audio.PtmEnabled = true; _audio.PtmKey = _ptmKeyCode; _audio.PtmKey2 = _ptmKeyCode2; _audio.PtmKey3 = _ptmKeyCode3;
                    if (_tglVoiceActivity != null && _tglVoiceActivity.Checked) { _loading=true; _tglVoiceActivity.Checked=false; _loading=false; _audio.VoiceActivityEnabled = false; }
                }
                RefreshPttHotkeyLabel();
            } };
            int hk2Y = y + 46;
            AddText(card, "Hotkey:", ki, hk2Y+4, 8f, TXT3);
            _lblPtmKey = new Label{Text=_ptmKeyCode > 0 ? PushToTalk.GetKeyName(_ptmKeyCode) : "Add Key",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(80,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(ki+50,hk2Y)};
            _lblPtmKey.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPtmKey.Width-1,_lblPtmKey.Height-1); };
            _lblPtmKey.MouseEnter += (s,e) => { if(!IsCapturingKey) _lblPtmKey.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPtmKey.MouseLeave += (s,e) => { if(!IsCapturingKey) _lblPtmKey.BackColor = INPUT_BG; };
            _lblPtmKey.Click += (s,e) => BeginCapture(CaptureTarget.PtmKey1, _lblPtmKey); card.Controls.Add(_lblPtmKey);
            // PTM Add Key button for key2
            _btnPtmAddKey2 = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Location=Dpi.Pt(ki+152,hk2Y)};
            _btnPtmAddKey2.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnPtmAddKey2.Click += (s,e) => BeginCapture(CaptureTarget.PtmKey2, _lblPtmKey2);
            card.Controls.Add(_btnPtmAddKey2);
            // PTM key2 row
            int ptmK2Y = hk2Y + 30;
            _lblPtmKey2 = new Label{Text=_ptmKeyCode2>0?KeyName(_ptmKeyCode2):"",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(80,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(ki+50,ptmK2Y),Visible=_ptmKeyCode2>0};
            _lblPtmKey2.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPtmKey2.Width-1,_lblPtmKey2.Height-1); };
            _lblPtmKey2.Click += (s,e) => BeginCapture(CaptureTarget.PtmKey2, _lblPtmKey2);
            card.Controls.Add(_lblPtmKey2);
            _btnPtmRemKey2 = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(24,24),BackColor=Color.Transparent,TabStop=false,Location=Dpi.Pt(ki+150,ptmK2Y+1),Visible=_ptmKeyCode2>0};
            _btnPtmRemKey2.FlatAppearance.BorderSize=0; _btnPtmRemKey2.Paint+=(s,e)=>{using(var p=new Pen(Color.FromArgb(120,120,120),2f)){var g2=e.Graphics;g2.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float cx=_btnPtmRemKey2.Width/2f,cy=_btnPtmRemKey2.Height/2f;g2.DrawLine(p,cx-4,cy-4,cx+4,cy+4);g2.DrawLine(p,cx+4,cy-4,cx-4,cy+4);}};
            _btnPtmRemKey2.Click += (s,e) => { _ptmKeyCode2=0;_audio.PtmKey2=0;CompactKeys(); };
            card.Controls.Add(_btnPtmRemKey2);
            // PTM key3 row
            int ptmK3Y = ptmK2Y + 30;
            _lblPtmKey3 = new Label{Text=_ptmKeyCode3>0?KeyName(_ptmKeyCode3):"",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(80,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(ki+50,ptmK3Y),Visible=_ptmKeyCode3>0};
            _lblPtmKey3.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPtmKey3.Width-1,_lblPtmKey3.Height-1); };
            _lblPtmKey3.Click += (s,e) => BeginCapture(CaptureTarget.PtmKey3, _lblPtmKey3);
            card.Controls.Add(_lblPtmKey3);
            _btnPtmRemKey3 = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(24,24),BackColor=Color.Transparent,TabStop=false,Location=Dpi.Pt(ki+150,ptmK3Y+1),Visible=_ptmKeyCode3>0};
            _btnPtmRemKey3.FlatAppearance.BorderSize=0; _btnPtmRemKey3.Paint+=(s,e)=>{using(var p=new Pen(Color.FromArgb(120,120,120),2f)){var g2=e.Graphics;g2.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float cx=_btnPtmRemKey3.Width/2f,cy=_btnPtmRemKey3.Height/2f;g2.DrawLine(p,cx-4,cy-4,cx+4,cy+4);g2.DrawLine(p,cx+4,cy-4,cx-4,cy+4);}};
            _btnPtmRemKey3.Click += (s,e) => { _ptmKeyCode3=0;_audio.PtmKey3=0;CompactKeys(); };
            card.Controls.Add(_btnPtmRemKey3);
            // PTM Add Key button for key3
            _btnPtmAddKey3 = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false};
            _btnPtmAddKey3.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnPtmAddKey3.Click += (s,e) => BeginCapture(CaptureTarget.PtmKey3, _lblPtmKey3);
            card.Controls.Add(_btnPtmAddKey3);
            // Initial visibility
            _btnPtmAddKey2.Visible = _ptmKeyCode2 <= 0 && _ptmKeyCode > 0;
            // PTM overlay toggle — on hotkey row
            var _chkPtmOverlay = MakeOverlayCheck(y, card, _settings.PtmShowOverlay, (v) => { _audio.PtmShowOverlay = v; });
            var _chkPtmSound = MakeSoundCheck(y, card, _settings.PtmSoundFeedback, (v) => { _audio.PtmSoundFeedback = v; });
            var _drpPtmSound = MakeSoundDropdown(y, card, _settings.SoundFeedbackType, (v) => { _audio.SoundFeedbackType = v; });
            LayoutPtmKeys();

            y = hk2Y + 34;
            AddLine(card, y); y += 10;

            // MODE 3: PUSH-TO-TOGGLE — with overlay icon
            _tglPtToggle = new ToggleSwitch { Location = Dpi.Pt(16, y) }; _tglPtToggle.PaintParentBg = PaintCardBg; card.Controls.Add(_tglPtToggle);
            _tglPtToggle.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(16), PixelY = Dpi.S(y), Source = _tglPtToggle, Card = card };
              _cardToggleMap[card].Add(ct); }
            AddText(card, "Push-to-Toggle", 64, y, 10f, TXT, FontStyle.Bold);
            AddText(card, "Tap to mute, tap again to unmute.", 64, y+20, 7.5f, TXT3);
            _tglPtToggle.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglPtToggle.Checked && (IsCapturingKey || CaptureCooldownActive())) { _loading=true; _tglPtToggle.Checked=false; _loading=false; return; }
                if (_tglPtToggle.Checked && _ptToggleKeyCode <= 0) { _loading=true; _tglPtToggle.Checked=false; _loading=false; BeginCapture(CaptureTarget.ToggleKey1, _lblPtToggleKey); return; }
                if (!_tglPtToggle.Checked) {
                    CancelAllCaptures();_ptToggleKeyCode=0;_ptToggleKeyCode2=0;_ptToggleKeyCode3=0;_lblPtToggleKey.Text="Add Key";_lblPtToggleKey.BackColor=INPUT_BG;_lblPtToggleKey.ForeColor=ACC;LayoutToggleKeys();_audio.DisablePtToggleMode();
                    // Toggle OFF: PTT and PTM can no longer coexist — keep PTT, kill PTM
                    if (_tglPtt.Checked && _tglPtm.Checked) {
                        _loading=true; _tglPtm.Checked=false; _loading=false;
                        _ptmKeyCode=0;_ptmKeyCode2=0;_ptmKeyCode3=0;_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;LayoutPtmKeys();_audio.DisablePtmMode();
                    }
                }
                else { _audio.PtToggleEnabled = true; _audio.PtToggleKey = _ptToggleKeyCode; _audio.PtToggleKey2 = _ptToggleKeyCode2; _audio.PtToggleKey3 = _ptToggleKeyCode3;
                    if (_tglVoiceActivity != null && _tglVoiceActivity.Checked) { _loading=true; _tglVoiceActivity.Checked=false; _loading=false; _audio.VoiceActivityEnabled = false; } }
                RefreshPttHotkeyLabel();
            } };
            int hk3Y = y + 46;
            AddText(card, "Hotkey:", ki, hk3Y+4, 8f, TXT3);
            _lblPtToggleKey = new Label{Text=_ptToggleKeyCode > 0 ? PushToTalk.GetKeyName(_ptToggleKeyCode) : "Add Key",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(80,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(ki+50,hk3Y)};
            _lblPtToggleKey.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPtToggleKey.Width-1,_lblPtToggleKey.Height-1); };
            _lblPtToggleKey.MouseEnter += (s,e) => { if(!IsCapturingKey) _lblPtToggleKey.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPtToggleKey.MouseLeave += (s,e) => { if(!IsCapturingKey) _lblPtToggleKey.BackColor = INPUT_BG; };
            _lblPtToggleKey.Click += (s,e) => BeginCapture(CaptureTarget.ToggleKey1, _lblPtToggleKey); card.Controls.Add(_lblPtToggleKey);
            // Toggle Add Key button for key2
            _btnToggleAddKey2 = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Location=Dpi.Pt(ki+152,hk3Y)};
            _btnToggleAddKey2.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnToggleAddKey2.Click += (s,e) => BeginCapture(CaptureTarget.ToggleKey2, _lblPtToggleKey2);
            card.Controls.Add(_btnToggleAddKey2);
            // Toggle key2 row
            int toggleK2Y = hk3Y + 30;
            _lblPtToggleKey2 = new Label{Text=_ptToggleKeyCode2>0?KeyName(_ptToggleKeyCode2):"",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(80,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(ki+50,toggleK2Y),Visible=_ptToggleKeyCode2>0};
            _lblPtToggleKey2.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPtToggleKey2.Width-1,_lblPtToggleKey2.Height-1); };
            _lblPtToggleKey2.Click += (s,e) => BeginCapture(CaptureTarget.ToggleKey2, _lblPtToggleKey2);
            card.Controls.Add(_lblPtToggleKey2);
            _btnToggleRemKey2 = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(24,24),BackColor=Color.Transparent,TabStop=false,Location=Dpi.Pt(ki+150,toggleK2Y+1),Visible=_ptToggleKeyCode2>0};
            _btnToggleRemKey2.FlatAppearance.BorderSize=0; _btnToggleRemKey2.Paint+=(s,e)=>{using(var p=new Pen(Color.FromArgb(120,120,120),2f)){var g2=e.Graphics;g2.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float cx=_btnToggleRemKey2.Width/2f,cy=_btnToggleRemKey2.Height/2f;g2.DrawLine(p,cx-4,cy-4,cx+4,cy+4);g2.DrawLine(p,cx+4,cy-4,cx-4,cy+4);}};
            _btnToggleRemKey2.Click += (s,e) => { _ptToggleKeyCode2=0;_audio.PtToggleKey2=0;CompactKeys(); };
            card.Controls.Add(_btnToggleRemKey2);
            // Toggle key3 row
            int toggleK3Y = toggleK2Y + 30;
            _lblPtToggleKey3 = new Label{Text=_ptToggleKeyCode3>0?KeyName(_ptToggleKeyCode3):"",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(80,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(ki+50,toggleK3Y),Visible=_ptToggleKeyCode3>0};
            _lblPtToggleKey3.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPtToggleKey3.Width-1,_lblPtToggleKey3.Height-1); };
            _lblPtToggleKey3.Click += (s,e) => BeginCapture(CaptureTarget.ToggleKey3, _lblPtToggleKey3);
            card.Controls.Add(_lblPtToggleKey3);
            _btnToggleRemKey3 = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(24,24),BackColor=Color.Transparent,TabStop=false,Location=Dpi.Pt(ki+150,toggleK3Y+1),Visible=_ptToggleKeyCode3>0};
            _btnToggleRemKey3.FlatAppearance.BorderSize=0; _btnToggleRemKey3.Paint+=(s,e)=>{using(var p=new Pen(Color.FromArgb(120,120,120),2f)){var g2=e.Graphics;g2.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;float cx=_btnToggleRemKey3.Width/2f,cy=_btnToggleRemKey3.Height/2f;g2.DrawLine(p,cx-4,cy-4,cx+4,cy+4);g2.DrawLine(p,cx+4,cy-4,cx-4,cy+4);}};
            _btnToggleRemKey3.Click += (s,e) => { _ptToggleKeyCode3=0;_audio.PtToggleKey3=0;CompactKeys(); };
            card.Controls.Add(_btnToggleRemKey3);
            // Toggle Add Key button for key3
            _btnToggleAddKey3 = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false};
            _btnToggleAddKey3.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnToggleAddKey3.Click += (s,e) => BeginCapture(CaptureTarget.ToggleKey3, _lblPtToggleKey3);
            card.Controls.Add(_btnToggleAddKey3);
            _btnToggleAddKey2.Visible = _ptToggleKeyCode2 <= 0 && _ptToggleKeyCode > 0;
            // Toggle overlay toggle — on hotkey row
            var _chkToggleOverlay = MakeOverlayCheck(y, card, _settings.PtToggleShowOverlay, (v) => { _audio.PtToggleShowOverlay = v; });
            var _chkToggleSound = MakeSoundCheck(y, card, _settings.PtToggleSoundFeedback, (v) => { _audio.PtToggleSoundFeedback = v; });
            var _drpToggleSound = MakeSoundDropdown(y, card, _settings.SoundFeedbackType, (v) => { _audio.SoundFeedbackType = v; });
            LayoutToggleKeys();

            // Handle resizing to keep dropdowns top-right of their segments
            card.Resize += (s2, e2) => {
                int rEdge = card.Width - Dpi.S(16);
                int yPtt = 56, yPtm = 56 + 46 + 34 + 10, yTog = 56 + 46 + 34 + 10 + 46 + 34 + 10;
                
                // Spaced: Dropdown (116), Speaker (154), Eye (188) -- scooted 15px to clear mascot
                if (_chkKey1Overlay != null) { _chkKey1Overlay.PixelX = rEdge - Dpi.S(188); _chkKey1Overlay.PixelY = Dpi.S(yPtt); }
                _chkPttSound.PixelX = rEdge - Dpi.S(154); _chkPttSound.PixelY = Dpi.S(yPtt);
                _drpPttSound.Location = new Point(rEdge - Dpi.S(116), Dpi.S(yPtt));

                if (_chkPtmOverlay != null) { _chkPtmOverlay.PixelX = rEdge - Dpi.S(188); _chkPtmOverlay.PixelY = Dpi.S(yPtm); }
                _chkPtmSound.PixelX = rEdge - Dpi.S(154); _chkPtmSound.PixelY = Dpi.S(yPtm);
                _drpPtmSound.Location = new Point(rEdge - Dpi.S(116), Dpi.S(yPtm));

                if (_chkToggleOverlay != null) { _chkToggleOverlay.PixelX = rEdge - Dpi.S(188); _chkToggleOverlay.PixelY = Dpi.S(yTog); }
                _chkToggleSound.PixelX = rEdge - Dpi.S(154); _chkToggleSound.PixelY = Dpi.S(yTog);
                _drpToggleSound.Location = new Point(rEdge - Dpi.S(116), Dpi.S(yTog));
                
                btnMicSec.Location = new Point(card.Width - Dpi.S(90) - Dpi.S(2), Dpi.S(18));
            };

            y += 90;
            AddText(card, "These options mute all microphones system-wide \u2014 headset, camera mic, USB devices.", 16, y - 1, 8.5f, Color.FromArgb(100, 180, 255));
            
            card.Dock = DockStyle.Fill;
            pane.Controls.Add(card);
        }
        void BuildVolLockPane(Panel pane) {
            var card = MakeCard(2, "Volume Protection", "Keep your mic and speaker levels exactly where you set them."); int y = 56;
            _volCard = card;
            if (!_cardSliderMap.ContainsKey(card)) _cardSliderMap[card] = new List<CardSlider>();
            if (!_cardIconMap.ContainsKey(card)) _cardIconMap[card] = new List<CardIcon>();

            // ── MIC SECTION ──────────────────────────────────────────────
            _tglMicEnf = Tgl("Lock Microphone Volume", "Prevents apps from silently changing your mic level.", y, card);
            _tglMicEnf.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglMicEnf.Checked) {
                    try { _micPreLockVol = (int)Audio.GetMicVolume(); } catch { _micPreLockVol = -1; }
                    if (_sliderRestoreMicTimer != null) { _sliderRestoreMicTimer.Stop(); _sliderRestoreMicTimer.Dispose(); _sliderRestoreMicTimer = null; }
                    _audio.MicLockVolume = _trkMicVol.Value;
                    _audio.MicLockEnabled = true;
                    try { Audio.SetMicVolume(_trkMicVol.Value); UpdateCurrent(); } catch { }
                } else {
                    _audio.MicLockEnabled = false;
                    AnimateSliderRestore(true);
                }
                if (_pttWarningText != null) {
                    _pttWarningText = BuildPttWarningText();
                }
                if (_micLockIcon != null) { _micLockIcon.Checked = _tglMicEnf.Checked; }
            } };
            y+=44;

            // PTT warning — stored as string, drawn directly in card paint handler
            _pttWarningText = BuildPttWarningText();
            _pttWarningY = Dpi.S(y);
            y += 16; // Always reserve space — prevents layout shift when warning toggles

            // Device name
            AddText(card, Audio.GetMicName()??"Mic: Unknown", 64, y, 8f, TXT4);
            y+=18;

            // Current mic volume slider + speaker mute icon
            int initMicVol = 50; try { initMicVol = (int)Audio.GetMicVolume(); } catch {}
            bool initMicMuted = false; try { initMicMuted = Audio.GetMicMute(); } catch {}
            AddText(card, "Volume:", 64, y+4, 8f, TXT3);
            _micCurVolSlider = new SlickSlider{Minimum=0,Maximum=100,Value=initMicVol,Location=Dpi.Pt(120,y-4),Size=Dpi.Size(180,30)}; _micCurVolSlider.PaintParentBg = PaintCardBg; card.Controls.Add(_micCurVolSlider);
            _micCurVolSlider.Visible = false;
            { var cs = new CardSlider { PixelX = Dpi.S(120), PixelY = Dpi.S(y-4), PixelW = Dpi.S(180), PixelH = Dpi.S(30), Source = _micCurVolSlider, Card = card };
              _cardSliderMap[card].Add(cs); }
            _lblMicCurVolPct = AddText(card, initMicVol+"%", 380, y+4, 8.5f, GREEN, FontStyle.Bold); _lblMicCurVolPct.RightAlign = true;
            _micCurVolSlider.ValueChanged += (s,e) => { _lblMicCurVolPct.Text=_micCurVolSlider.Value+"%"; card.Invalidate(); };
            _micCurVolSlider.DragCompleted += (s,e) => { if (!_loading) { try { Audio.SetMicVolume(_micCurVolSlider.Value); } catch {} UpdateCurrent(); } };
            // Mic mute icon (speaker icon — it's a mute button)
            _micMuteIcon = new CardIcon { W = 28, H = 24, Checked = !initMicMuted, IsEye = false, Card = card,
                OnChange = (on) => { try { Audio.SetMicMute(!on); } catch {} UpdateCurrent(); } };
            _micMuteIcon.SetPos(392, y);
            _cardIconMap[card].Add(_micMuteIcon);
            y+=28;

            // Lock at: slider (at bottom of mic section)
            AddText(card, "Lock at:", 64, y+4, 8f, TXT2);
            _trkMicVol = new SlickSlider{Minimum=0,Maximum=100,Value=100,Location=Dpi.Pt(120,y-4),Size=Dpi.Size(180,30)}; _trkMicVol.PaintParentBg = PaintCardBg; card.Controls.Add(_trkMicVol);
            _trkMicVol.Visible = false;
            { var cs = new CardSlider { PixelX = Dpi.S(120), PixelY = Dpi.S(y-4), PixelW = Dpi.S(180), PixelH = Dpi.S(30), Source = _trkMicVol, Card = card };
              _cardSliderMap[card].Add(cs); }
            _plMicVol = AddText(card, "100%", 380, y+4, 8.5f, ACC, FontStyle.Bold); _plMicVol.RightAlign = true;
            _trkMicVol.ValueChanged += (s,e) => { _plMicVol.Text=_trkMicVol.Value+"%"; card.Invalidate(); };
            _trkMicVol.DragCompleted += (s,e) => { if (!_loading) { _audio.MicLockVolume=_trkMicVol.Value; if (_tglMicEnf.Checked) try { Audio.SetMicVolume(_trkMicVol.Value); } catch { } } };
            // Lock icon next to Lock at % — reflects toggle state, clickable to toggle lock
            _micLockIcon = new CardIcon { W = 20, H = 24, Checked = _settings.MicEnforceEnabled, IsLock = true, Card = card,
                OnChange = (locked) => {
                    if (_loading) return;
                    if ((DateTime.UtcNow - _lastLockClick).TotalMilliseconds < 300) { _micLockIcon.Checked = !locked; return; }
                    _lastLockClick = DateTime.UtcNow;
                    _loading = true; _tglMicEnf.Checked = locked; _loading = false;
                    if (locked) {
                        StartGlisten(_tglMicEnf);
                        if (_volCard != null) _volCard.Invalidate();
                        try { _micPreLockVol = (int)Audio.GetMicVolume(); } catch { _micPreLockVol = -1; }
                        if (_sliderRestoreMicTimer != null) { _sliderRestoreMicTimer.Stop(); _sliderRestoreMicTimer.Dispose(); _sliderRestoreMicTimer = null; }
                        _audio.MicLockVolume = _trkMicVol.Value;
                        _audio.MicLockEnabled = true;
                        try { Audio.SetMicVolume(_trkMicVol.Value); UpdateCurrent(); } catch { }
                    } else {
                        _audio.MicLockEnabled = false;
                        AnimateSliderRestore(true);
                    }
                    _pttWarningText = BuildPttWarningText();
                } };
            _micLockIcon.SetPos(396, y);
            _cardIconMap[card].Add(_micLockIcon);

            // Max All button — positioned absolute top-right of mic section
            var btnMicMax = new Button{Text="\u266B  Max All",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(82,24),ForeColor=ACC,BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8),Font=new Font("Segoe UI",7.5f,FontStyle.Bold),TabStop=false};
            btnMicMax.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            btnMicMax.MouseEnter+=(s,e)=>{btnMicMax.BackColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);btnMicMax.ForeColor=Color.FromArgb(Math.Min(255,ACC.R+50),Math.Min(255,ACC.G+50),Math.Min(255,ACC.B+50));};
            btnMicMax.MouseLeave+=(s,e)=>{btnMicMax.BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);btnMicMax.ForeColor=ACC;};
            btnMicMax.Click+=(s,e)=>{ AnimateSlider(_trkMicVol, 100, ()=>{ _audio.MicLockVolume=100; try { Audio.SetMicVolume(100); } catch { } UpdateCurrent(); }); AnimateSlider(_micCurVolSlider, 100, ()=>{ try { Audio.SetMicVolume(100); } catch { } UpdateCurrent(); }); AnimateSlider(_trkSpkVol, 100, ()=>{ _audio.SpeakerLockVolume=100; try { Audio.SetSpeakerVolume(100); } catch { } UpdateCurrent(); }); AnimateSlider(_spkCurVolSlider, 100, ()=>{ try { Audio.SetSpeakerVolume(100); } catch { } UpdateCurrent(); }); };
            card.Controls.Add(btnMicMax);

            // Position Max All on resize
            card.Resize += (s2,e2) => {
                int rEdge = card.Width - Dpi.S(16);
                btnMicMax.Left = rEdge - Dpi.S(82);
                btnMicMax.Top = Dpi.S(56);
            };

            y+=34; AddLine(card, y); y+=10;

            // ── SPEAKER SECTION ──────────────────────────────────────────
            _tglSpkEnf = Tgl("Lock Speaker Volume", "Prevents apps from changing your system volume.", y, card);
            _tglSpkEnf.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglSpkEnf.Checked) {
                    try { _spkPreLockVol = (int)Audio.GetSpeakerVolume(); } catch { _spkPreLockVol = -1; }
                    if (_sliderRestoreSpkTimer != null) { _sliderRestoreSpkTimer.Stop(); _sliderRestoreSpkTimer.Dispose(); _sliderRestoreSpkTimer = null; }
                    _audio.SpeakerLockVolume = _trkSpkVol.Value;
                    _audio.SpeakerLockEnabled = true;
                    try { Audio.SetSpeakerVolume(_trkSpkVol.Value); UpdateCurrent(); } catch { }
                } else {
                    _audio.SpeakerLockEnabled = false;
                    AnimateSliderRestore(false);
                }
                if (_spkLockIcon != null) { _spkLockIcon.Checked = _tglSpkEnf.Checked; }
                _spkWarningText = BuildSpkWarningText();
            } };
            y+=44;

            // Speaker warning — AFK fade, app enforcement status
            _spkWarningText = BuildSpkWarningText();
            _spkWarningY = Dpi.S(y);
            y += 16; // Always reserve space for symmetry with mic section

            // Device name
            AddText(card, Audio.GetSpeakerName()??"Speaker: Unknown", 64, y, 8f, TXT4);
            y+=18;

            // Current speaker volume slider + speaker mute icon
            int initSpkVol2 = 100; try { initSpkVol2 = (int)Audio.GetSpeakerVolume(); } catch {}
            bool initSpkMuted = false; try { initSpkMuted = Audio.GetSpeakerMute(); } catch {}
            AddText(card, "Volume:", 64, y+4, 8f, TXT3);
            _spkCurVolSlider = new SlickSlider{Minimum=0,Maximum=100,Value=initSpkVol2,Location=Dpi.Pt(120,y-4),Size=Dpi.Size(180,30)}; _spkCurVolSlider.PaintParentBg = PaintCardBg; card.Controls.Add(_spkCurVolSlider);
            _spkCurVolSlider.Visible = false;
            { var cs = new CardSlider { PixelX = Dpi.S(120), PixelY = Dpi.S(y-4), PixelW = Dpi.S(180), PixelH = Dpi.S(30), Source = _spkCurVolSlider, Card = card };
              _cardSliderMap[card].Add(cs); }
            _lblSpkCurVolPct = AddText(card, initSpkVol2+"%", 380, y+4, 8.5f, GREEN, FontStyle.Bold); _lblSpkCurVolPct.RightAlign = true;
            _spkCurVolSlider.ValueChanged += (s,e) => { _lblSpkCurVolPct.Text=_spkCurVolSlider.Value+"%"; card.Invalidate(); };
            _spkCurVolSlider.DragCompleted += (s,e) => { if (!_loading) { try { Audio.SetSpeakerVolume(_spkCurVolSlider.Value); } catch {} UpdateCurrent(); } };
            // Speaker mute icon (speaker icon)
            _spkMuteIcon = new CardIcon { W = 28, H = 24, Checked = !initSpkMuted, IsEye = false, Card = card,
                OnChange = (on) => { try { Audio.SetSpeakerMute(!on); } catch {} UpdateCurrent(); } };
            _spkMuteIcon.SetPos(392, y);
            _cardIconMap[card].Add(_spkMuteIcon);
            y+=28;

            // Lock at: slider (at bottom of speaker section)
            AddText(card, "Lock at:", 64, y+4, 8f, TXT2);
            _trkSpkVol = new SlickSlider{Minimum=0,Maximum=100,Value=100,Location=Dpi.Pt(120,y-4),Size=Dpi.Size(180,30)}; _trkSpkVol.PaintParentBg = PaintCardBg; card.Controls.Add(_trkSpkVol);
            _trkSpkVol.Visible = false;
            { var cs = new CardSlider { PixelX = Dpi.S(120), PixelY = Dpi.S(y-4), PixelW = Dpi.S(180), PixelH = Dpi.S(30), Source = _trkSpkVol, Card = card };
              _cardSliderMap[card].Add(cs); }
            _plSpkVol = AddText(card, "100%", 380, y+4, 8.5f, ACC, FontStyle.Bold); _plSpkVol.RightAlign = true;
            _trkSpkVol.ValueChanged += (s,e) => { _plSpkVol.Text=_trkSpkVol.Value+"%"; card.Invalidate(); };
            _trkSpkVol.DragCompleted += (s,e) => { if (!_loading) { _audio.SpeakerLockVolume=_trkSpkVol.Value; if (_tglSpkEnf.Checked) try { Audio.SetSpeakerVolume(_trkSpkVol.Value); } catch { } } };
            // Lock icon next to Lock at % — reflects toggle state, clickable to toggle lock
            _spkLockIcon = new CardIcon { W = 20, H = 24, Checked = _settings.SpeakerEnforceEnabled, IsLock = true, Card = card,
                OnChange = (locked) => {
                    if (_loading) return;
                    if ((DateTime.UtcNow - _lastLockClick).TotalMilliseconds < 300) { _spkLockIcon.Checked = !locked; return; }
                    _lastLockClick = DateTime.UtcNow;
                    _loading = true; _tglSpkEnf.Checked = locked; _loading = false;
                    if (locked) {
                        StartGlisten(_tglSpkEnf);
                        if (_volCard != null) _volCard.Invalidate();
                        try { _spkPreLockVol = (int)Audio.GetSpeakerVolume(); } catch { _spkPreLockVol = -1; }
                        if (_sliderRestoreSpkTimer != null) { _sliderRestoreSpkTimer.Stop(); _sliderRestoreSpkTimer.Dispose(); _sliderRestoreSpkTimer = null; }
                        _audio.SpeakerLockVolume = _trkSpkVol.Value;
                        _audio.SpeakerLockEnabled = true;
                        try { Audio.SetSpeakerVolume(_trkSpkVol.Value); UpdateCurrent(); } catch { }
                    } else {
                        _audio.SpeakerLockEnabled = false;
                        AnimateSliderRestore(false);
                    }
                    if (_spkLockIcon != null) _spkLockIcon.Checked = locked;
                    _spkWarningText = BuildSpkWarningText();
                } };
            _spkLockIcon.SetPos(396, y);
            _cardIconMap[card].Add(_spkLockIcon);

            pane.Controls.Add(card);
        }

        void RefreshPttHotkeyLabel() {
            _pttWarningText = BuildPttWarningText();
            if (_volCard != null) _volCard.Invalidate();
        }
        /// <summary>Starts the toggle glisten animation at 30ms/frame — matches wizard speed exactly.</summary>
        void StartGlisten(ToggleSwitch target) {
            _tglGlistenTarget = target;
            _tglGlistenFrame = 0;
            if (_glistenTimer != null) { _glistenTimer.Stop(); _glistenTimer.Dispose(); }
            _glistenTimer = new Timer { Interval = 30 };
            _glistenTimer.Tick += (s, e) => {
                if (_tglGlistenTarget == null || _tglGlistenFrame >= 35) {
                    _tglGlistenTarget = null; _glistenTimer.Stop(); _glistenTimer.Dispose(); _glistenTimer = null; return;
                }
                _tglGlistenFrame++;
                if (_tglGlistenFrame >= 35) { _tglGlistenTarget = null; _glistenTimer.Stop(); _glistenTimer.Dispose(); _glistenTimer = null; }
                if (_pttCard != null) _pttCard.Invalidate();
                if (_volCard != null) _volCard.Invalidate();
            };
            _glistenTimer.Start();
            // Immediate first paint
            if (_pttCard != null) _pttCard.Invalidate();
            if (_volCard != null) _volCard.Invalidate();
        }
        /// <summary>Builds mic status showing ALL active protections: PTT, AFK mute, etc.</summary>
        string BuildPttWarningText() {
            if (_audio == null && _settings == null) return "";
            var parts = new System.Collections.Generic.List<string>();
            // PTT/PTM/Toggle
            if (_audio != null) {
                string mode = ""; string key = "";
                if (_audio.PttEnabled) {
                    mode = "Push-to-Talk";
                    if (_audio.PttKey > 0) key = PushToTalk.GetKeyName(_audio.PttKey);
                } else if (_audio.PtmEnabled) {
                    mode = "Push-to-Mute";
                    if (_audio.PtmKey > 0) key = PushToTalk.GetKeyName(_audio.PtmKey);
                } else if (_audio.PtToggleEnabled) {
                    mode = "Push-to-Toggle";
                    if (_audio.PtToggleKey > 0) key = PushToTalk.GetKeyName(_audio.PtToggleKey);
                }
                if (mode.Length > 0) {
                    if (key.Length > 0) parts.Add(mode + " (" + key + ")");
                    else parts.Add(mode);
                }
            }
            // AFK mic mute
            if (_settings != null && _settings.AfkMicMuteEnabled)
                parts.Add("AFK mute after " + _settings.AfkMicMuteSec + "s");
            if (parts.Count == 0) return "";
            return "\u26A0  " + string.Join("  \u2022  ", parts.ToArray()) + " active.";
        }
        /// <summary>Builds speaker status text: AFK fade, app enforcement, or nothing.</summary>
        string BuildSpkWarningText() {
            if (_settings == null) return "";
            var parts = new System.Collections.Generic.List<string>();
            if (_settings.AfkSpeakerMuteEnabled)
                parts.Add("AFK volume fade after " + _settings.AfkSpeakerMuteSec + "s");
            if (_settings.AppVolumeEnforceEnabled)
                parts.Add("Per-app volume enforcement active");
            if (parts.Count == 0) return "";
            return "\u26A0  " + string.Join("  \u2022  ", parts.ToArray()) + ".";
        }
        void RefreshSpkWarning() {
            _spkWarningText = BuildSpkWarningText();
            if (_volCard != null) _volCard.Invalidate();
        }

        private Panel _appListPanel;
        private List<AppRuleRow> _appRows = new List<AppRuleRow>();
        private static Dictionary<string, Image> _appIconCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        // Shared fonts for app rows — avoid per-row allocation
        static readonly Font _rowFont = new Font("Segoe UI", 9f);
        static readonly Font _rowVolFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        // _rowBtnFont removed — all X buttons are now owner-drawn via PaintRemoveIcon

        class AppRuleRow {
            public string Name;
            public Panel Row;
            public SlickSlider Slider;
            public Label VolLabel;
            public Label CurrentLabel;
            public bool Locked = false;
            public int InitialValue = 100;
            public int CurrentVolume = -1; // -1 = unknown, 0-100 = live from system
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder exeName, ref uint size);

        static Image GetAppIcon(string processName) {
            if (_appIconCache.ContainsKey(processName)) return _appIconCache[processName];
            Image icon = null;
            try {
                // Method 1: MainModule.FileName (works for standard processes)
                var procs = Process.GetProcessesByName(processName);
                if (procs.Length > 0) {
                    try {
                        string path = procs[0].MainModule.FileName;
                        var ico = Icon.ExtractAssociatedIcon(path);
                        if (ico != null) { icon = ico.ToBitmap(); ico.Dispose(); }
                    } catch {
                        // Method 2: QueryFullProcessImageName (works for elevated/UWP/access-denied)
                        try {
                            // PROCESS_QUERY_LIMITED_INFORMATION = 0x1000 (works even for elevated processes)
                            IntPtr hProc = OpenProcess(0x1000, false, (uint)procs[0].Id);
                            if (hProc != IntPtr.Zero) {
                                try {
                                    var sb = new StringBuilder(1024);
                                    uint sz = 1024;
                                    if (QueryFullProcessImageName(hProc, 0, sb, ref sz)) {
                                        string path2 = sb.ToString();
                                        if (System.IO.File.Exists(path2)) {
                                            var ico2 = Icon.ExtractAssociatedIcon(path2);
                                            if (ico2 != null) { icon = ico2.ToBitmap(); ico2.Dispose(); }
                                        }
                                    }
                                } finally { CloseHandle(hProc); }
                            }
                        } catch { }
                    }
                }
                foreach (var p in procs) p.Dispose();
            } catch { }
            _appIconCache[processName] = icon; // Cache null too to avoid retrying
            return icon;
        }

        void BuildAppsPane(Panel pane) {
            var card = MakeCard(4, "Apps", "Lock individual app volumes to specific levels."); int y = 56;
            _tglAppEnf = Tgl("Per-App Volume Enforcement", "Green = current  \u2022  Slider = target", y, card);
            _tglAppEnf.CheckedChanged += (s,e) => { if (!_loading) { _audio.AppVolumeEnabled = _tglAppEnf.Checked; _settings.AppVolumeRules = CollectAppRules(); _settings.Save(); _spkWarningText = BuildSpkWarningText(); } };

            if (!_cardSliderMap.ContainsKey(card)) _cardSliderMap[card] = new List<CardSlider>();
            if (!_cardIconMap.ContainsKey(card)) _cardIconMap[card] = new List<CardIcon>();

            // Max All button — top right of card (matches Volume Protection page)
            var btnMaxAll = new Button{Text="\u266B  Max All",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(82,24),ForeColor=ACC,BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8),Font=new Font("Segoe UI",7.5f,FontStyle.Bold),TabStop=false};
            btnMaxAll.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            btnMaxAll.MouseEnter+=(s,e)=>{btnMaxAll.BackColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);btnMaxAll.ForeColor=Color.FromArgb(Math.Min(255,ACC.R+50),Math.Min(255,ACC.G+50),Math.Min(255,ACC.B+50));};
            btnMaxAll.MouseLeave+=(s,e)=>{btnMaxAll.BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);btnMaxAll.ForeColor=ACC;};
            btnMaxAll.Click+=(s,e)=>{ AnimateMaxAllApps(); AnimateSlider(_sysVolSlider, 100, ()=>{ try { Audio.SetSpeakerVolume(100); } catch {} }); };
            card.Controls.Add(btnMaxAll);

            y+=44;

            // Scan + Clear buttons
            var btn = new Button{Text="\u25B6  Scan Running Apps",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(148,26),ForeColor=ACC,BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8),Location=Dpi.Pt(16,y),Font=new Font("Segoe UI",8.5f,FontStyle.Bold),TabStop=false};
            btn.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            btn.Click+=(s,e)=>ScanApps();
            btn.MouseEnter+=(s,e)=>btn.BackColor=Color.FromArgb(ACC.R/5,ACC.G/5,ACC.B/5);
            btn.MouseLeave+=(s,e)=>btn.BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);
            card.Controls.Add(btn);

            var btnClear = new Button{Text="Clear",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(50,26),ForeColor=TXT4,BackColor=Color.FromArgb(24,24,24),Location=Dpi.Pt(172,y),Font=new Font("Segoe UI",8f),TabStop=false};
            btnClear.FlatAppearance.BorderColor=Color.FromArgb(40,40,40);
            btnClear.Click+=(s,e)=>{_appRows.Clear();RebuildAppList();};
            btnClear.MouseEnter+=(s,e)=>btnClear.BackColor=Color.FromArgb(36,36,36);
            btnClear.MouseLeave+=(s,e)=>btnClear.BackColor=Color.FromArgb(24,24,24);
            card.Controls.Add(btnClear);

            // System volume slider — right side of scan buttons row
            int initSpkVol = 100; try { initSpkVol = (int)Audio.GetSpeakerVolume(); } catch {}
            AddText(card, "System Vol", 240, y+6, 7.5f, TXT3);
            _sysVolSlider = new SlickSlider{Minimum=0,Maximum=100,Value=initSpkVol,Size=Dpi.Size(130,24),Location=Dpi.Pt(300,y)};
            _sysVolSlider.PaintParentBg = PaintCardBg;
            _sysVolSlider.ValueChanged += (s,e) => {
                if (_loading) return;
                int val = _sysVolSlider.Value;
                _lblSysVolPct.Text = val + "%";
                card.Invalidate();
                foreach (var row in _appRows) {
                    if (row.VolLabel != null && row.Slider != null)
                        row.VolLabel.Text = row.Slider.Value + "% / " + val + "%";
                }
            };
            _sysVolSlider.DragCompleted += (s,e) => {
                if (_loading) return;
                try { Audio.SetSpeakerVolume(_sysVolSlider.Value); } catch {}
            };
            card.Controls.Add(_sysVolSlider);
            _sysVolSlider.Visible = false; // Hidden — CardSlider paints instead
            var _sysVolCS = new CardSlider { PixelX = Dpi.S(300), PixelY = Dpi.S(y), PixelW = Dpi.S(130), PixelH = Dpi.S(24), Source = _sysVolSlider, Card = card };
            _cardSliderMap[card].Add(_sysVolCS);
            _lblSysVolPct = AddText(card, initSpkVol+"%", 445, y+4, 8.5f, ACC, FontStyle.Bold);
            // Speaker mute icon — right of percentage
            bool initSysMuted = false; try { initSysMuted = Audio.GetSpeakerMute(); } catch {}
            _appsSysMuteIcon = new CardIcon { W = 28, H = 24, Checked = !initSysMuted, IsEye = false, Card = card,
                OnChange = (on) => { try { Audio.SetSpeakerMute(!on); } catch {} } };
            _appsSysMuteIcon.SetPos(476, y);
            _cardIconMap[card].Add(_appsSysMuteIcon);

            // Position Max All button top-right on resize
            card.Resize += (s2,e2) => { btnMaxAll.Left = card.Width - Dpi.S(82) - Dpi.S(16); btnMaxAll.Top = Dpi.S(56); };

            y+=32;

            int listY = y; // logical Y=144
            _appListPanel = new ScrollPanel{Location=Dpi.Pt(16,listY),Size=Dpi.Size(100, 200),BackColor=Color.FromArgb(14,14,14)};
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(_appListPanel, true, null);
            _appListPanel.Paint += (s2,e2) => {
                var gp = e2.Graphics;
                gp.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                if (_appRows.Count == 0) {
                    using (var f = new Font("Segoe UI", 8.5f))
                    using (var b = new SolidBrush(TXT4)) {
                        string msg = "No app rules yet \u2014 click Scan to detect running Audio Apps";
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            gp.DrawString(msg, f, b, new RectangleF(0, 0, _appListPanel.Width, _appListPanel.Height), sf);
                    }
                }
                using (var p = new Pen(Color.FromArgb(32,32,32)))
                    gp.DrawRectangle(p, 0, 0, _appListPanel.Width - 1, _appListPanel.Height - 1);
            };
            card.Controls.Add(_appListPanel);

            // Size list to fill card; fires on initial layout AND on resize
            int scaledListY = Dpi.S(listY);
            int margin = Dpi.S(16);
            int lastListW = 0;
            bool rebuildGuard = false;
            // Cache last live sessions to avoid COM calls on every resize
            System.Collections.Generic.List<AudioSession> _lastLiveSessions = null;
            EventHandler sizeList = (s, e) => {
                int newW = card.Width - margin * 2;
                int newH = card.Height - scaledListY - margin;
                if (newW > Dpi.S(200)) _appListPanel.Width = newW;
                if (newH > Dpi.S(60)) _appListPanel.Height = newH;
                // Rebuild rows if panel width changed (rows need to match)
                if (!rebuildGuard && _appListPanel.Width != lastListW && _appRows.Count > 0) {
                    lastListW = _appListPanel.Width;
                    rebuildGuard = true;
                    try { RebuildAppList(_lastLiveSessions); } finally { rebuildGuard = false; }
                }
            };
            card.Resize += sizeList;
            card.Layout += (s2, e2) => sizeList(s2, e2); // fires on initial layout

            pane.Controls.Add(card);
        }

        void RebuildAppList(System.Collections.Generic.List<AudioSession> cachedSessions = null) {
            _appListPanel.SuspendLayout();
            _appListPanel.Controls.Clear();
            var sp = _appListPanel as ScrollPanel;
            if (sp != null) sp.ResetScroll();
            int y = 0, rowH = Dpi.S(44);
            int iconSz = Dpi.S(20);
            int rowW = Math.Max(Dpi.S(300), _appListPanel.Width);

            // Batch-query current volumes (use cached if provided)
            var liveSessions = cachedSessions;
            if (liveSessions == null) try { liveSessions = Audio.GetAudioSessions(); } catch { }
            var liveVols = new System.Collections.Generic.Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (liveSessions != null)
                foreach (var ls in liveSessions)
                    if (!liveVols.ContainsKey(ls.ProcessName))
                        liveVols[ls.ProcessName] = ls.Volume;

            // Get master speaker volume to compute effective output level
            float masterVol = 100f;
            try { float mv = Audio.GetSpeakerVolume(); if (mv >= 0) masterVol = mv; } catch { }

            for (int i = 0; i < _appRows.Count; i++) {
                var ar = _appRows[i];
                float lv; 
                if (liveVols.TryGetValue(ar.Name, out lv))
                    ar.CurrentVolume = (int)((lv / 100f) * masterVol); // Effective output = session × master
                else
                    ar.CurrentVolume = -1;

                Color rowBg = ar.Locked ? Color.FromArgb(14, 18, 24) : Color.FromArgb(16, 16, 16);
                var row = new BufferedPanel{Location=new Point(0,y),Size=new Size(rowW,rowH),BackColor=rowBg};
                ar.Row = row;

                row.Paint += (s,e) => {
                };

                // Lock toggle
                string cName = ar.Name;
                var lockPanel = new Panel{Size=new Size(Dpi.S(30),Dpi.S(32)),Location=new Point(Dpi.S(4),Dpi.S(6)),BackColor=Color.Transparent};
                lockPanel.Paint += (s,e) => {
                    var g2 = e.Graphics; g2.SmoothingMode = SmoothingMode.AntiAlias;
                    var rw = _appRows.Find(x => x.Name == cName);
                    DrawLockIcon(g2, Dpi.S(3), Dpi.S(4), Dpi.S(24), rw != null && rw.Locked);
                };
                lockPanel.MouseEnter += (s,e) => { lockPanel.BackColor = Color.FromArgb(32, 32, 32); };
                lockPanel.MouseLeave += (s,e) => { lockPanel.BackColor = Color.Transparent; };
                lockPanel.MouseClick += (s,e) => {
                    var r = _appRows.Find(x => x.Name == cName);
                    if (r != null) { r.Locked = !r.Locked; RebuildAppList(); SyncAppRulesLive(); }
                };
                row.Controls.Add(lockPanel);

                // App icon
                Image appIcon = GetAppIcon(ar.Name);
                if (appIcon != null) {
                    var iconBox = new PictureBox{Size=new Size(iconSz,iconSz),Location=new Point(Dpi.S(38),Dpi.S(12)),SizeMode=PictureBoxSizeMode.Zoom,Image=appIcon,BackColor=Color.Transparent};
                    row.Controls.Add(iconBox);
                }

                // Layout: [lock 34] [icon 24] [name 92] [curVol 46] [slider...] [target 80] [X 28]
                int nameX = Dpi.S(62);
                int btnXx = rowW - Dpi.S(28);
                int targetX = btnXx - Dpi.S(80);
                int sliderEnd = targetX - Dpi.S(4);

                // App name — wider to handle "steamwebhelper" etc
                var lbl = new Label{Text=ar.Name,Font=_rowFont,ForeColor=ar.Locked?TXT:TXT4,AutoSize=false,Size=new Size(Dpi.S(92),rowH),TextAlign=ContentAlignment.MiddleLeft,Location=new Point(nameX,0)};
                row.Controls.Add(lbl);

                // Current volume — green text showing actual system level (wider to fit "100%")
                int curX = nameX + Dpi.S(92);
                string curText = ar.CurrentVolume >= 0 ? ar.CurrentVolume + "%" : "\u2014";
                Color curClr = ar.CurrentVolume >= 0 ? Color.FromArgb(80, 200, 120) : TXT4;
                var curLbl = new Label{Text=curText,Font=_rowVolFont,ForeColor=curClr,AutoSize=false,Size=new Size(Dpi.S(46),rowH),TextAlign=ContentAlignment.MiddleCenter,Location=new Point(curX,0)};
                ar.CurrentLabel = curLbl;
                row.Controls.Add(curLbl);

                // Slider
                int sliderX = curX + Dpi.S(48);
                int sliderW = Math.Max(Dpi.S(60), sliderEnd - sliderX);
                var slider = new SlickSlider{Minimum=0,Maximum=100,Value=ar.Slider!=null?ar.Slider.Value:ar.InitialValue,Location=new Point(sliderX,0),Size=new Size(sliderW,rowH),Enabled=ar.Locked,BackColor=rowBg};
                ar.Slider = slider;
                ar.InitialValue = slider.Value;
                row.Controls.Add(slider);

                // Target volume + master context (accent blue) — shows "app% / master%"
                string targetText = slider.Value + "% / " + (int)masterVol + "%";
                var volLbl = new Label{Text=targetText,Font=_rowVolFont,ForeColor=ar.Locked?ACC:TXT4,AutoSize=false,Size=new Size(Dpi.S(80),rowH),TextAlign=ContentAlignment.MiddleCenter,Location=new Point(targetX,0)};
                ar.VolLabel = volLbl;
                string sliderAppName = ar.Name;
                bool sliderLocked = ar.Locked;
                Label sliderCurLbl = curLbl;
                float cachedMaster = masterVol; // snapshot for drag feedback
                slider.ValueChanged += (s,e) => {
                    // Lightweight: update label text only (no COM calls during drag)
                    volLbl.Text = slider.Value + "% / " + (int)cachedMaster + "%";
                    if (sliderLocked) {
                        int eff = (int)((slider.Value / 100f) * cachedMaster);
                        sliderCurLbl.Text = eff + "%";
                        sliderCurLbl.ForeColor = Color.FromArgb(80, 200, 120);
                    }
                };
                slider.DragCompleted += (s,e) => {
                    // Heavy: push to system only on mouse release
                    if (sliderLocked) {
                        try { Audio.SetAppVolume(sliderAppName, slider.Value); } catch { }
                        SyncAppRulesLive();
                    }
                    // Refresh master volume cache
                    try { float mv = Audio.GetSpeakerVolume(); if (mv >= 0) cachedMaster = mv; } catch { }
                };
                row.Controls.Add(volLbl);

                // Remove button
                string capName = ar.Name;
                var btnX = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=new Size(Dpi.S(22),Dpi.S(22)),BackColor=Color.Transparent,TabStop=false,Location=new Point(btnXx,Dpi.S(11))};
                btnX.FlatAppearance.BorderSize=0;
                btnX.FlatAppearance.MouseOverBackColor=Color.Transparent;
                btnX.FlatAppearance.MouseDownBackColor=Color.Transparent;
                bool hoverX = false;
                btnX.MouseEnter+=(s,e)=>{hoverX=true;btnX.Invalidate();};
                btnX.MouseLeave+=(s,e)=>{hoverX=false;btnX.Invalidate();};
                btnX.Paint+=(s,e)=>{PaintRemoveIcon(e.Graphics,btnX.ClientRectangle,hoverX);};
                btnX.Click += (s,e) => { _appRows.RemoveAll(r=>r.Name==capName); RebuildAppList(); SyncAppRulesLive(); };
                row.Controls.Add(btnX);

                _appListPanel.Controls.Add(row);
                y += rowH;
            }
            _appListPanel.ResumeLayout();
            // Rows fill full width — no scrollbar to account for
            _appListPanel.Invalidate();
        }

        /// <summary>Draws a lock (locked) or unlock (open shackle) icon.</summary>
        static void DrawLockIcon(Graphics g, int x, int y, int sz, bool locked) {
            float s = sz / 16f;
            Color c = locked ? DarkTheme.Accent : Color.FromArgb(80, 80, 80);
            // Lock body (rounded rect)
            using (var b = new SolidBrush(c)) {
                g.FillRectangle(b, x + 3*s, y + 7*s, 10*s, 8*s);
            }
            // Keyhole
            using (var b = new SolidBrush(Color.FromArgb(20, 20, 20))) {
                g.FillEllipse(b, x + 6.5f*s, y + 9.5f*s, 3*s, 3*s);
                g.FillRectangle(b, x + 7.2f*s, y + 11.5f*s, 1.6f*s, 2*s);
            }
            // Shackle
            using (var p = new Pen(c, 2f*s)) {
                p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                if (locked) {
                    g.DrawArc(p, x + 4.5f*s, y + 1.5f*s, 7*s, 7*s, 180, 180);
                } else {
                    // Open shackle — shifted right and up
                    g.DrawArc(p, x + 6f*s, y + 0.5f*s, 7*s, 7*s, 180, 180);
                }
            }
        }

        Dictionary<string,int> CollectAppRules() {
            var d = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            foreach (var ar in _appRows) {
                if (!d.ContainsKey(ar.Name)) {
                    int vol = ar.Slider != null ? ar.Slider.Value : ar.InitialValue;
                    d[ar.Name] = ar.Locked ? vol : -(vol + 1);
                }
            }
            return d;
        }

        /// <summary>Push current app rules to live settings so enforcement picks them up immediately.</summary>
        void SyncAppRulesLive() {
            _settings.AppVolumeRules = CollectAppRules();
        }

        // =====================================================================
        //  PANE 5: VOICE ACTIVITY
        // =====================================================================

        private ToggleSwitch _tglVoiceActivity;
        private SlickSlider _trkVoiceThreshold;
        private PaintedLabel _lblVoiceThresholdPct, _lblVoicePeakPct;
        private Timer _voiceMeterTimer;
        private CardToggle _ctVoiceActivity;
        // Overlay + sound checkboxes
        // Voice Activity fields (overlay/sound now use CardIcon inline)
        private NumericUpDown _nudVoiceHoldover;
        // Meter drag state
        private bool _optMeterDragging;
        private int _optMeterX, _optMeterW;

        void BuildVoiceActivityPane(Panel pane) {
            var card = MakeCard(1, "Voice Activity", "Auto-unmute when you speak");

            int y = 56;

            // Enable toggle
            _tglVoiceActivity = new ToggleSwitch { Visible = false };
            card.Controls.Add(_tglVoiceActivity);
            _ctVoiceActivity = new CardToggle { PixelX = Dpi.S(16), PixelY = Dpi.S(y), Source = _tglVoiceActivity, Card = card };
            if (!_cardToggleMap.ContainsKey(card)) _cardToggleMap[card] = new List<CardToggle>();
            _cardToggleMap[card].Add(_ctVoiceActivity);
            AddText(card, "Enable Voice Activity", 64, y + 1, 9.5f, TXT, FontStyle.Bold);
            AddText(card, "Mic stays muted until your voice is detected.", 64, y + 18, 7.5f, TXT3, FontStyle.Regular, 340);

            y += 46;
            AddLine(card, y);
            y += 12;

            // ── LIVE LEVEL & THRESHOLD (one bar, draggable) ──
            AddText(card, "Level & Threshold", 16, y, 8.5f, TXT, FontStyle.Bold);
            _lblVoicePeakPct = AddText(card, "0%", 380, y, 7.5f, TXT3);
            _lblVoicePeakPct.RightAlign = true;
            y += 14;
            _lblVoiceThresholdPct = AddText(card, "Threshold: " + _settings.VoiceActivityThreshold + "%", 16, y, 7.5f, TXT3);
            y += 14;

            // Meter bar drawn in paint handler
            int meterY = y;
            int meterH = 24;
            _optMeterX = Dpi.S(16);
            _optMeterW = 0; // set in paint from card width
            y += meterH + 6;
            AddText(card, "Drag the white handle to set threshold", 16, y, 7f, TXT4, FontStyle.Italic);
            y += 18;

            // Hidden slider for threshold state
            _trkVoiceThreshold = new SlickSlider { Minimum = 1, Maximum = 100, Value = _settings.VoiceActivityThreshold, Visible = false };
            card.Controls.Add(_trkVoiceThreshold);
            _trkVoiceThreshold.ValueChanged += (s, e) => {
                _lblVoiceThresholdPct.Text = "Threshold: " + _trkVoiceThreshold.Value + "%";
                card.Invalidate();
                if (_pushToTalk != null) _pushToTalk.SetVoiceThreshold(_trkVoiceThreshold.Value / 100f);
            };

            AddLine(card, y);
            y += 12;

            // ── HOLDOVER (NUD, matches AFK seconds style) ──
            AddText(card, "Holdover Delay", 16, y, 9f, TXT, FontStyle.Bold);
            y += 20;
            AddText(card, "Keep mic open for", 64, y + 2, 9f, TXT, FontStyle.Regular);
            _nudVoiceHoldover = new PaddedNumericUpDown { Minimum = 200, Maximum = 5000, Increment = 100, Value = _settings.VoiceActivityHoldoverMs, Location = Dpi.Pt(200, y), Size = Dpi.Size(70, 24), BackColor = INPUT_BG, ForeColor = TXT, Font = new Font("Segoe UI", 9f), BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center };
            _nudVoiceHoldover.Leave += (s2, e2) => { try { int v; if (int.TryParse(_nudVoiceHoldover.Text, out v)) { v = Math.Max(200, Math.Min(5000, v)); _nudVoiceHoldover.Value = v; } } catch { _nudVoiceHoldover.Value = 2000; } };
            _nudVoiceHoldover.ValueChanged += (s2, e2) => { if (!_loading && _audio != null) { _audio.VoiceActivityHoldoverMs = (int)_nudVoiceHoldover.Value; if (_pushToTalk != null) _pushToTalk.SetVoiceHoldover((int)_nudVoiceHoldover.Value); } };
            card.Controls.Add(_nudVoiceHoldover);
            AddText(card, "ms after speech stops", 278, y + 2, 9f, TXT3, FontStyle.Regular);

            y += 34;
            AddLine(card, y);
            y += 12;

            // ── EYE ICON ONLY — inline on enable row, right-aligned to match Mic pane ──
            var _chkVAOverlay = MakeOverlayCheck(56, card, _settings.VoiceActivityShowOverlay, (v) => { if (!_loading) _audio.VoiceActivityShowOverlay = v; });
            // Right-align to match Mic pane eye position
            card.Resize += (s2, e2) => {
                int rEdge = card.Width - Dpi.S(16);
                if (_chkVAOverlay != null) { _chkVAOverlay.PixelX = rEdge - Dpi.S(188); _chkVAOverlay.PixelY = Dpi.S(56); }
            };

            // ── WARNINGS ──
            AddText(card, "\u26A0  Exclusive with Push-to-Talk modes. Only one can be active at a time.", 16, y, 7f, DarkTheme.Amber);
            y += 16;
            int _vaWarnY = y; // saved for split-color paint in VA card handler

            // Wire events (overlay/sound now driven by CardIcon, not ToggleSwitches)
            _tglVoiceActivity.CheckedChanged += (s, e) => {
                if (_loading) return;
                if (_tglVoiceActivity.Checked) {
                    CancelAllCaptures();
                    // Visually uncheck + clear hotkeys for PTT/PTM/Toggle
                    _loading = true;
                    if (_tglPtt != null && _tglPtt.Checked) _tglPtt.Checked = false;
                    if (_tglPtm != null && _tglPtm.Checked) _tglPtm.Checked = false;
                    if (_tglPtToggle != null && _tglPtToggle.Checked) _tglPtToggle.Checked = false;
                    _loading = false;
                    // Clear PTT keys
                    _pttKeyCode=0;_pttKeyCode2=0;_pttKeyCode3=0;
                    if(_lblPttKey!=null){_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;}
                    if(_lblPttKey2!=null)_lblPttKey2.Text="";
                    if(_lblPttKey3!=null)_lblPttKey3.Text="";
                    // Clear PTM keys
                    _ptmKeyCode=0;_ptmKeyCode2=0;_ptmKeyCode3=0;
                    if(_lblPtmKey!=null){_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;}
                    if(_lblPtmKey2!=null)_lblPtmKey2.Text="";
                    if(_lblPtmKey3!=null)_lblPtmKey3.Text="";
                    // Clear Toggle keys
                    _ptToggleKeyCode=0;_ptToggleKeyCode2=0;_ptToggleKeyCode3=0;
                    if(_lblPtToggleKey!=null){_lblPtToggleKey.Text="Add Key";_lblPtToggleKey.BackColor=INPUT_BG;_lblPtToggleKey.ForeColor=ACC;}
                    if(_lblPtToggleKey2!=null)_lblPtToggleKey2.Text="";
                    if(_lblPtToggleKey3!=null)_lblPtToggleKey3.Text="";
                    CompactKeys();
                    LayoutPttKeys(); LayoutPtmKeys(); LayoutToggleKeys();
                    // Backend disable all three modes (each saves + fires independently)
                    _audio.DisablePttMode();
                    _audio.DisablePtmMode();
                    _audio.DisablePtToggleMode();
                }
                _audio.VoiceActivityEnabled = _tglVoiceActivity.Checked;
            };

            // Custom paint for meter + CardToggles
            int savedMeterY = Dpi.S(meterY);
            int savedMeterH = Dpi.S(meterH);
            card.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int mx = Dpi.S(16), mw = card.Width - Dpi.S(32);
                _optMeterW = mw;

                float peak = _pushToTalk != null ? _pushToTalk.CurrentPeakLevel : 0f;
                float threshold = _trkVoiceThreshold.Value / 100f;

                // Track background
                using (var path = DarkTheme.RoundedRect(new Rectangle(mx, savedMeterY, mw, savedMeterH), Dpi.S(4)))
                using (var b = new SolidBrush(Color.FromArgb(22, 22, 22))) g.FillPath(b, path);
                using (var b = new SolidBrush(Color.FromArgb(12, 0, 0, 0)))
                    g.FillRectangle(b, mx + 1, savedMeterY + 1, mw - 2, Dpi.S(2));

                // Graduation marks
                for (int pct = 25; pct <= 75; pct += 25) {
                    int gx = mx + (int)(mw * pct / 100f);
                    int ga = pct == 50 ? 18 : 10;
                    using (var p = new Pen(Color.FromArgb(ga, 255, 255, 255), 1))
                        g.DrawLine(p, gx, savedMeterY + Dpi.S(3), gx, savedMeterY + savedMeterH - Dpi.S(3));
                }

                // Peak fill — gradient for depth
                int fillW = Math.Max(0, (int)(mw * Math.Min(1f, peak)));
                if (fillW > 4) {
                    Color barCol = peak >= threshold ? ACC : GREEN;
                    if (peak >= threshold) {
                        for (int gl = 0; gl < 2; gl++) {
                            using (var b = new SolidBrush(Color.FromArgb(8 - gl * 3, barCol.R, barCol.G, barCol.B)))
                                g.FillRectangle(b, mx - gl - 1, savedMeterY - gl - 1, mw + (gl + 1) * 2, savedMeterH + (gl + 1) * 2);
                        }
                    }
                    var fillRect = new Rectangle(mx, savedMeterY, fillW, savedMeterH);
                    using (var path = DarkTheme.RoundedRect(fillRect, Dpi.S(4))) {
                        var oldClip2 = g.Clip;
                        g.SetClip(path, CombineMode.Intersect);
                        Color topCol = Color.FromArgb(200, barCol.R, barCol.G, barCol.B);
                        Color botCol = Color.FromArgb(120, barCol.R, barCol.G, barCol.B);
                        using (var lgb = new LinearGradientBrush(new Point(0, savedMeterY), new Point(0, savedMeterY + savedMeterH), topCol, botCol))
                            g.FillRectangle(lgb, fillRect);
                        using (var b = new SolidBrush(Color.FromArgb(35, 255, 255, 255)))
                            g.FillRectangle(b, mx, savedMeterY + 1, fillW, Dpi.S(2));
                        g.Clip = oldClip2;
                    }
                }

                // Threshold handle — matches wizard style
                int thX = mx + (int)(mw * threshold);
                using (var b = new SolidBrush(Color.FromArgb(25, 255, 255, 255)))
                    g.FillRectangle(b, thX - Dpi.S(3), savedMeterY, Dpi.S(6), savedMeterH);
                using (var p = new Pen(Color.FromArgb(240, 255, 255, 255), Dpi.PenW(2))) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, thX, savedMeterY + Dpi.S(3), thX, savedMeterY + savedMeterH - Dpi.S(3));
                }
                float hCy = savedMeterY + savedMeterH / 2f;
                int thr2 = Dpi.S(6);
                using (var b = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                    g.FillEllipse(b, thX - thr2 + 1, hCy - thr2 + 1, thr2 * 2, thr2 * 2);
                if (_optMeterDragging) {
                    int glR = thr2 + Dpi.S(3);
                    using (var b = new SolidBrush(Color.FromArgb(50, ACC.R, ACC.G, ACC.B)))
                        g.FillEllipse(b, thX - glR, hCy - glR, glR * 2, glR * 2);
                }
                using (var b = new SolidBrush(Color.White))
                    g.FillEllipse(b, thX - thr2, hCy - thr2, thr2 * 2, thr2 * 2);
                int dotR2 = Dpi.S(2);
                using (var b = new SolidBrush(ACC))
                    g.FillEllipse(b, thX - dotR2, hCy - dotR2, dotR2 * 2, dotR2 * 2);

                // === VA enable row glow — breathing accent bloom ===
                if (_tglVoiceActivity != null && _tglVoiceActivity.Checked && _pushToTalk != null && _pushToTalk.IsVoiceActive) {
                    int glTop = Dpi.S(52);
                    int glBot = Dpi.S(98);
                    int glLeft = Dpi.S(10);
                    int glRight = card.Width - Dpi.S(10);
                    DarkTheme.PaintBreathingGlow(g, new Rectangle(glLeft, glTop, glRight - glLeft, glBot - glTop), ACC, Dpi.S(6));
                }

                // CardToggles
                List<CardToggle> ctgls;
                if (_cardToggleMap.TryGetValue(card, out ctgls)) {
                    foreach (var ct in ctgls) ct.Paint(g, ACC);
                }
                // CardIcons (eye overlay)
                List<CardIcon> icons;
                if (_cardIconMap.TryGetValue(card, out icons)) {
                    foreach (var ic in icons) ic.Paint(g, ACC);
                }
                // Split-color PAUSE warning
                PaintPauseWarning(g, Dpi.S(16), Dpi.S(_vaWarnY), 7f);
            };

            // Mouse handlers — meter drag + CardToggle clicks
            card.MouseDown += (s, e) => {
                if (e.Button != MouseButtons.Left) return;
                if (e.X >= _optMeterX && e.X <= _optMeterX + _optMeterW && e.Y >= savedMeterY - Dpi.S(8) && e.Y <= savedMeterY + savedMeterH + Dpi.S(8)) {
                    _optMeterDragging = true;
                    float pct = (float)(e.X - _optMeterX) / Math.Max(1, _optMeterW);
                    _trkVoiceThreshold.Value = Math.Max(1, Math.Min(100, (int)(Math.Max(0.01f, Math.Min(1f, pct)) * 100)));
                    card.Invalidate(); return;
                }
            };
            card.MouseMove += (s, e) => {
                // Meter drag — MakeCard handles CardToggle/CardIcon hover
                if (_optMeterDragging) {
                    float pct = (float)(e.X - _optMeterX) / Math.Max(1, _optMeterW);
                    _trkVoiceThreshold.Value = Math.Max(1, Math.Min(100, (int)(Math.Max(0.01f, Math.Min(1f, pct)) * 100)));
                    card.Invalidate();
                }
                if (_optMeterW > 0 && e.X >= _optMeterX && e.X <= _optMeterX + _optMeterW &&
                    e.Y >= savedMeterY - Dpi.S(8) && e.Y <= savedMeterY + savedMeterH + Dpi.S(8))
                    card.Cursor = Cursors.Hand;
            };
            card.MouseUp += (s, e) => {
                if (_optMeterDragging) { _optMeterDragging = false; if (!_loading) _audio.VoiceActivityThreshold = _trkVoiceThreshold.Value; card.Invalidate(); }
            };
            card.MouseClick += (s, e) => {
                // Only guard: if click was on meter, don't let MakeCard's handler process it as a toggle
                if (e.X >= _optMeterX && e.X <= _optMeterX + _optMeterW && e.Y >= savedMeterY - Dpi.S(8) && e.Y <= savedMeterY + savedMeterH + Dpi.S(8)) return;
                // CardToggle + CardIcon clicks handled by MakeCard's MouseClick handler
            };
            card.MouseLeave += (s, e) => {
                // Meter drag finalize — MakeCard handles CardToggle/CardIcon hover reset
                if (_optMeterDragging) { _optMeterDragging = false; if (!_loading) _audio.VoiceActivityThreshold = _trkVoiceThreshold.Value; }
            };

            // Live meter refresh timer — only ticks when visible
            _voiceMeterTimer = new Timer { Interval = 30 };
            _voiceMeterTimer.Tick += (s, e) => {
                try {
                    if (_panes != null && _panes.Length > 1 && _panes[1] != null && _panes[1].Visible && _pushToTalk != null) {
                        float peak = _pushToTalk.CurrentPeakLevel;
                        if (_lblVoicePeakPct != null) _lblVoicePeakPct.Text = ((int)(peak * 100)) + "%";
                        card.Invalidate();
                    }
                } catch { }
            };
            _voiceMeterTimer.Start();

            pane.Controls.Add(card);
        }

        void BuildGeneralPane(Panel pane) {
            var card = MakeCard(5, "General", "Startup behavior, notifications, and legal information.");
            // Card glass top is at y=46, separator below this section at y=96.
            int y = 56;
            _tglStartup = Tgl("Start with Windows", null, y, card);
            _tglStartup.CheckedChanged += (s,e) => { if (!_loading) { _audio.StartWithWindows = _tglStartup.Checked; } };
            // Run Setup Wizard button — same row as Start with Windows, right-aligned
            int bwY = y;
            var bw = new Button{Text="Run Setup Wizard",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(120,24),ForeColor=TXT2,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f),TabStop=false};
            bw.FlatAppearance.BorderColor=INPUT_BDR; bw.Click+=(s,e)=>{DialogResult=DialogResult.Retry;Close();};
            bw.MouseEnter+=(s,e)=>bw.BackColor=Color.FromArgb(36,36,36);
            bw.MouseLeave+=(s,e)=>bw.BackColor=Color.FromArgb(20,20,20);
            card.Controls.Add(bw);
            // Check for Updates button — same row, to the left of Run Setup Wizard, blue theme
            var btnUpdate = new Button{Text="Check for Updates",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(155,24),ForeColor=ACC,BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Cursor=Cursors.Hand};
            btnUpdate.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            btnUpdate.MouseEnter+=(s,e)=>{if(btnUpdate.Enabled)btnUpdate.BackColor=Color.FromArgb(ACC.R/5,ACC.G/5,ACC.B/5);};
            btnUpdate.MouseLeave+=(s,e)=>{if(btnUpdate.Enabled)btnUpdate.BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);};
            _updateCheckHandler = (s,e) => { if (!_updateShimmering) CheckForUpdates(btnUpdate); };
            btnUpdate.Click += _updateCheckHandler;
            card.Controls.Add(btnUpdate);
            // Position both buttons — wizard at right edge, update to its left
            EventHandler posWiz = (s2, e2) => {
                if (card.Width > 0) {
                    bw.Location = new Point(card.Width - bw.Width - Dpi.S(16), Dpi.S(bwY));
                    btnUpdate.Location = new Point(bw.Left - btnUpdate.Width - Dpi.S(8), Dpi.S(bwY));
                }
            };
            card.Layout += (s2, e2) => posWiz(null, EventArgs.Empty);
            card.Resize += posWiz;
            y += 37; AddLine(card, y); y += 14;
            AddText(card, "NOTIFICATIONS", 16, y, 7.5f, TXT3, FontStyle.Bold);
            y += 22;
            _tglOverlay = Tgl("Show Mic Status Overlay", "Floating pill shows mic open/closed \u2014 drag to reposition, right-click to dismiss", y, card);
            _tglOverlay.CheckedChanged += (s,e) => { if (!_loading) { _audio.MicOverlayEnabled = _tglOverlay.Checked; } };
            y += 42;
            _tglNotifyCorr = Tgl("Volume Correction Alerts", "Show a toast when Angry Audio resets your audio.", y, card);
            _tglNotifyCorr.CheckedChanged += (s,e) => { if (!_loading) { _audio.NotifyOnCorrection = _tglNotifyCorr.Checked; } };
            y += 42;
            _tglNotifyDev = Tgl("Device Change Alerts", "Notify when a new mic or speaker is detected.", y, card);
            _tglNotifyDev.CheckedChanged += (s,e) => { if (!_loading) { _audio.NotifyOnDeviceChange = _tglNotifyDev.Checked; } };
            y += 42; AddLine(card, y); y += 16;
            // LEGAL section — vertically centered in remaining card space
            AddText(card, "LEGAL", 16, y, 7.5f, TXT3, FontStyle.Bold);
            y += 20;
            AddText(card, AppVersion.Copyright, 16, y, 8f, TXT2);
            y += 18;
            AddText(card, "Unauthorized copying, modification, or distribution prohibited.", 16, y, 7f, TXT4, FontStyle.Regular, 440);
            pane.Controls.Add(card);
        }

        void BuildFooter() {
            _footer = new BufferedPanel{Dock=DockStyle.Bottom,Height=Dpi.S(50),BackColor=BG};
            Rectangle _optSaveRect = Rectangle.Empty, _optCancelRect = Rectangle.Empty;
            bool _optSaveHover = false, _optCancelHover = false;
            _footer.Paint += (s,e) => {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                PaintUnifiedStars(g, _footer);
                using(var p=new Pen(BDR)) g.DrawLine(p,0,0,_footer.Width,0);
                int cr = Dpi.S(6);
                // Save button
                _optSaveRect = new Rectangle(_footer.Width - Dpi.S(96), Dpi.S(10), Dpi.S(80), Dpi.S(30));
                float pulse = (float)((Math.Sin(_saveOrbitPhase * 0.8) + 1.0) / 2.0);
                int pr = (int)(20 + (180 - 20) * pulse), pg = (int)(50 + (240 - 50) * pulse), pb = (int)(80 + (255 - 80) * pulse);
                Color sbg = _optSaveHover ? Color.FromArgb(140, 220, 255) : Color.FromArgb(pr, pg, pb);
                using (var path = DarkTheme.RoundedRect(_optSaveRect, cr))
                using (var b = new SolidBrush(sbg)) g.FillPath(b, path);
                TextRenderer.DrawText(g, "Save", DarkTheme.BtnFontBold, _optSaveRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                // Cancel button
                _optCancelRect = new Rectangle(_optSaveRect.Left - Dpi.S(90), Dpi.S(10), Dpi.S(80), Dpi.S(30));
                Color cbg = _optCancelHover ? Color.FromArgb(45, 45, 45) : Color.FromArgb(28, 28, 28);
                using (var path = DarkTheme.RoundedRect(_optCancelRect, cr))
                using (var b = new SolidBrush(cbg)) g.FillPath(b, path);
                using (var path = DarkTheme.RoundedRect(_optCancelRect, cr))
                using (var p = new Pen(Color.FromArgb(50, 50, 50))) g.DrawPath(p, path);
                TextRenderer.DrawText(g, "Cancel", DarkTheme.BtnFont, _optCancelRect, Color.FromArgb(170, 170, 170), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            _footer.MouseMove += (s, e) => {
                bool sh = _optSaveRect.Contains(e.Location);
                bool ch = _optCancelRect.Contains(e.Location);
                if (sh != _optSaveHover || ch != _optCancelHover) {
                    _optSaveHover = sh; _optCancelHover = ch;
                    _footer.Cursor = (sh || ch) ? Cursors.Hand : Cursors.Default;
                    _footer.Invalidate();
                }
            };
            _footer.MouseLeave += (s, e) => { _optSaveHover = _optCancelHover = false; _footer.Cursor = Cursors.Default; _footer.Invalidate(); };
            _footer.MouseClick += (s, e) => {
                if (_optSaveRect.Contains(e.Location)) DoSave();
                else if (_optCancelRect.Contains(e.Location)) Close();
            };
            Controls.Add(_footer);

            _saveOrbitPhase = 0f;
            _saveOrbitTimer = new Timer { Interval = 30 };
            _saveOrbitTimer.Tick += (s, e) => {
                _saveOrbitPhase += 0.08f;
                if (_saveOrbitPhase > (float)(Math.PI * 2)) _saveOrbitPhase -= (float)(Math.PI * 2);
                _footer.Invalidate();
            };
            _saveOrbitTimer.Start();
        }


        void FlashModeToggles() {
            if (_hotkeyFlashTimer != null && _hotkeyFlashTimer.Enabled) return;
            // Glow the specific mode segment that the pressed hotkey belongs to
            if (_tglPtt != null && _tglPtt.Checked) FlashToggleOn(_tglPtt);
            else if (_tglPtm != null && _tglPtm.Checked) FlashToggleOn(_tglPtm);
            else if (_tglPtToggle != null && _tglPtToggle.Checked) FlashToggleOn(_tglPtToggle);
        }

        private DateTime _lastCaptureComplete = DateTime.MinValue;
        private bool CaptureCooldownActive() { return (DateTime.Now - _lastCaptureComplete).TotalMilliseconds < 300; }

        // =====================================================================
        //  UNIFIED CAPTURE — single entry point, single completion handler
        // =====================================================================

        /// <summary>Start a key capture for the specified target. ONE method for all 9 slots.</summary>
        void BeginCapture(CaptureTarget target, Label label) {
            if (_audio.IsCapturing || CaptureCooldownActive()) return;
            // For key2/key3 slots, require at least one mode toggle to be on
            if (target == CaptureTarget.PttKey2 || target == CaptureTarget.PttKey3 ||
                target == CaptureTarget.PtmKey2 || target == CaptureTarget.PtmKey3 ||
                target == CaptureTarget.ToggleKey2 || target == CaptureTarget.ToggleKey3) {
                if (!_tglPtt.Checked && !_tglPtm.Checked && !_tglPtToggle.Checked) { FlashToggleOn(_tglPtt); return; }
            }
            label.Text = "Press..."; label.BackColor = ACC; label.ForeColor = Color.White;
            _captureGlowLabel = label; _captureGlowFrame = 0;
            // Update layout so the label is visible at its new size
            if (target >= CaptureTarget.PttKey1 && target <= CaptureTarget.PttKey3) LayoutPttKeys();
            else if (target >= CaptureTarget.PtmKey1 && target <= CaptureTarget.PtmKey3) LayoutPtmKeys();
            else LayoutToggleKeys();
            Logger.Info("BeginCapture: " + target);
            _audio.StartCapture(target, vk => OnCaptureComplete(target, label, vk));
        }

        /// <summary>Single completion handler for ALL key captures.</summary>
        void OnCaptureComplete(CaptureTarget target, Label label, int vk) {
            _captureGlowLabel = null;
            _lastCaptureComplete = DateTime.Now;

            if (vk == 0) { // Escape or cancel
                HandleCaptureCancel(target, label);
                return;
            }

            // Check cross-mode duplicate
            int excl = AudioSettings.ExcludeModeFor(target);
            if (_audio.IsKeyInUse(vk, excl) || _audio.IsDuplicateInMode(vk, target)) {
                ResetLabel(target, label);
                ShakeReject(label, null);
                return;
            }

            // === SUCCESS — apply the key ===
            ResetLabel(target, label);
            SetLocalKeyCode(target, vk);
            label.Text = KeyName(vk);

            // Write to AudioSettings FIRST — fires SettingsChange.PttMode so TrayApp restarts engine.
            // CompactKeys must run AFTER this, not before — writing _settings directly before _audio
            // poisons AudioSettings' change detection (setter sees no diff → no event → engine never restarts).
            _audio.SetKeyForTarget(target, vk);

            // Auto-enable mode toggle + glisten if this was a key1 capture
            switch (target) {
                case CaptureTarget.PttKey1:
                    if (!_tglPtt.Checked) { _loading=true; _tglPtt.Checked=true; _loading=false; StartGlisten(_tglPtt); }
                    // Mutual exclusivity: PTT on kills PTM (unless Toggle is on)
                    if (!_tglPtToggle.Checked && _tglPtm.Checked) {
                        _loading=true; _tglPtm.Checked=false; _loading=false;
                        _ptmKeyCode=0;_ptmKeyCode2=0;_ptmKeyCode3=0;if(_lblPtmKey!=null){_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;}LayoutPtmKeys();_audio.DisablePtmMode();
                    }
                    _audio.SetPttKeyAndEnable(vk); _audio.PttKey2=_pttKeyCode2; _audio.PttKey3=_pttKeyCode3;
                    break;
                case CaptureTarget.PttKey2:
                    _key2ShowOverlay=true; _audio.PttKey2ShowOverlay=true; if(_chkKey2Overlay!=null)_chkKey2Overlay.Checked=true;
                    _audio.PttKey2=vk; _audio.Save();
                    break;
                case CaptureTarget.PttKey3:
                    _key3ShowOverlay=true; _audio.PttKey3ShowOverlay=true; if(_chkKey3Overlay!=null)_chkKey3Overlay.Checked=true;
                    _audio.PttKey3=vk; _audio.Save();
                    break;
                case CaptureTarget.PtmKey1:
                    if (!_tglPtm.Checked) { _loading=true; _tglPtm.Checked=true; _loading=false; StartGlisten(_tglPtm); }
                    // Mutual exclusivity: PTM on kills PTT (unless Toggle is on)
                    if (!_tglPtToggle.Checked && _tglPtt.Checked) {
                        _loading=true; _tglPtt.Checked=false; _loading=false;
                        _pttKeyCode=0;_pttKeyCode2=0;_pttKeyCode3=0;_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;CompactKeys();LayoutPttKeys();_audio.DisablePttMode();
                    }
                    _audio.SetPtmKeyAndEnable(vk);
                    break;
                case CaptureTarget.PtmKey2:
                    _audio.PtmKey2=vk; _audio.Save(); LayoutPtmKeys();
                    break;
                case CaptureTarget.PtmKey3:
                    _audio.PtmKey3=vk; _audio.Save(); LayoutPtmKeys();
                    break;
                case CaptureTarget.ToggleKey1:
                    if (!_tglPtToggle.Checked) { _loading=true; _tglPtToggle.Checked=true; _loading=false; StartGlisten(_tglPtToggle); }
                    _audio.SetPtToggleKeyAndEnable(vk);
                    break;
                case CaptureTarget.ToggleKey2:
                    _audio.PtToggleKey2=vk; _audio.Save(); LayoutToggleKeys();
                    break;
                case CaptureTarget.ToggleKey3:
                    _audio.PtToggleKey3=vk; _audio.Save(); LayoutToggleKeys();
                    break;
            }
            CompactKeys(); // Sync labels, layout, and _settings AFTER _audio writes
            RefreshPttHotkeyLabel();
        }

        /// <summary>Handle Escape/cancel for a specific target.</summary>
        void HandleCaptureCancel(CaptureTarget target, Label label) {
            ResetLabel(target, label);
            SetLocalKeyCode(target, 0);
            // Write to AudioSettings FIRST, then CompactKeys — same fix as OnCaptureComplete
            _audio.SetKeyForTarget(target, 0);
            _audio.Save();
            CompactKeys();

            // If all keys for the mode are gone, disable mode
            switch (target) {
                case CaptureTarget.PttKey1: case CaptureTarget.PttKey2: case CaptureTarget.PttKey3:
                    if (_pttKeyCode==0 && _pttKeyCode2==0 && _pttKeyCode3==0 && _tglPtt.Checked) { _loading=true; _tglPtt.Checked=false; _loading=false; _audio.DisablePttMode(); }
                    break;
                case CaptureTarget.PtmKey1: case CaptureTarget.PtmKey2: case CaptureTarget.PtmKey3:
                    if (_ptmKeyCode==0 && _ptmKeyCode2==0 && _ptmKeyCode3==0 && _tglPtm.Checked) { _loading=true; _tglPtm.Checked=false; _loading=false; _audio.DisablePtmMode(); }
                    break;
                case CaptureTarget.ToggleKey1: case CaptureTarget.ToggleKey2: case CaptureTarget.ToggleKey3:
                    if (_ptToggleKeyCode==0 && _ptToggleKeyCode2==0 && _ptToggleKeyCode3==0 && _tglPtToggle.Checked) { _loading=true; _tglPtToggle.Checked=false; _loading=false; _audio.DisablePtToggleMode(); }
                    break;
            }
            RefreshPttHotkeyLabel();
        }

        /// <summary>Reset a label back to its non-capturing appearance.</summary>
        void ResetLabel(CaptureTarget target, Label label) {
            label.BackColor = INPUT_BG; label.ForeColor = ACC;
        }

        /// <summary>Set the local key code field for a target.</summary>
        void SetLocalKeyCode(CaptureTarget target, int vk) {
            switch (target) {
                case CaptureTarget.PttKey1: _pttKeyCode = vk; break;
                case CaptureTarget.PttKey2: _pttKeyCode2 = vk; break;
                case CaptureTarget.PttKey3: _pttKeyCode3 = vk; break;
                case CaptureTarget.PtmKey1: _ptmKeyCode = vk; break;
                case CaptureTarget.PtmKey2: _ptmKeyCode2 = vk; break;
                case CaptureTarget.PtmKey3: _ptmKeyCode3 = vk; break;
                case CaptureTarget.ToggleKey1: _ptToggleKeyCode = vk; break;
                case CaptureTarget.ToggleKey2: _ptToggleKeyCode2 = vk; break;
                case CaptureTarget.ToggleKey3: _ptToggleKeyCode3 = vk; break;
            }
        }

        /// <summary>Cancel any active key capture, bounce toggles back to off if no key was set, reset all labels.</summary>
        void CancelAllCaptures() {
            if (_audio.IsCapturing) _audio.CancelCapture();
            _captureGlowLabel = null;
            _lastCaptureComplete = DateTime.Now;
            // Reset ALL toggles (if no key) and ALL labels
            _loading = true;
            if (_pttKeyCode <= 0) _tglPtt.Checked = false;
            if (_ptmKeyCode <= 0) _tglPtm.Checked = false;
            if (_ptToggleKeyCode <= 0) _tglPtToggle.Checked = false;
            _loading = false;
            _lblPttKey.Text = _pttKeyCode > 0 ? PushToTalk.GetKeyName(_pttKeyCode) : "Add Key";
            _lblPttKey.BackColor = INPUT_BG; _lblPttKey.ForeColor = ACC;
            if (_lblPtmKey != null) { _lblPtmKey.Text = _ptmKeyCode > 0 ? PushToTalk.GetKeyName(_ptmKeyCode) : "Add Key"; _lblPtmKey.BackColor = INPUT_BG; _lblPtmKey.ForeColor = ACC; }
            if (_lblPtToggleKey != null) { _lblPtToggleKey.Text = _ptToggleKeyCode > 0 ? PushToTalk.GetKeyName(_ptToggleKeyCode) : "Add Key"; _lblPtToggleKey.BackColor = INPUT_BG; _lblPtToggleKey.ForeColor = ACC; }
            CompactKeys(); LayoutPttKeys(); LayoutPtmKeys(); LayoutToggleKeys();
        }

        void UpdateKey3Visibility() { LayoutPttKeys(); }
        void UpdatePtmKey2Vis() { LayoutPtmKeys(); }
        void UpdateToggleKey2Vis() { LayoutToggleKeys(); }
        void PaintPauseWarning(Graphics g, int x, int y, float fontSize) {
            using (var f = new Font("Segoe UI", fontSize)) {
                string pre = "\u26A0  Right-click the tray icon to ";
                string pause = "PAUSE";
                string post = " at any time.";
                float preW = g.MeasureString(pre, f).Width - 4; // trim slack
                float pauseW = g.MeasureString(pause, f).Width - 4;
                using (var b = new SolidBrush(DarkTheme.ErrorRed)) g.DrawString(pre, f, b, x, y);
                using (var b = new SolidBrush(Color.White)) g.DrawString(pause, f, b, x + preW, y);
                using (var b = new SolidBrush(DarkTheme.ErrorRed)) g.DrawString(post, f, b, x + preW + pauseW, y);
            }
        }
        CardIcon MakeOverlayCheck(int y, Panel card, bool initialOn, Action<bool> onChange) {
            var icon = new CardIcon { W = 24, H = 24, Checked = initialOn, IsEye = true, OnChange = onChange, Card = card };
            icon.SetPos(405, y);
            if (!_cardIconMap.ContainsKey(card)) _cardIconMap[card] = new List<CardIcon>();
            _cardIconMap[card].Add(icon);
            return icon;
        }
        CardIcon MakeSoundCheck(int y, Panel card, bool initialOn, Action<bool> onChange) {
            var icon = new CardIcon { W = 28, H = 24, Checked = initialOn, IsEye = false, OnChange = onChange, Card = card };
            icon.SetPos(430, y);
            if (!_cardIconMap.ContainsKey(card)) _cardIconMap[card] = new List<CardIcon>();
            _cardIconMap[card].Add(icon);
            return icon;
        }
        /// <summary>Creates a compact dark sound dropdown at a specific position, right-aligned on the hotkey row.</summary>
        Panel MakeSoundDropdown(int y, Panel card, int settingValue, Action<int> onChange) {
            string[] sndNames = { "Soft Click", "Double Tap", "Chirp", "Radio", "Chime", "Pop", "Custom..." };
            int sel = Math.Max(0, Math.Min(sndNames.Length - 1, settingValue));
            var btn = new BufferedPanel { Size = Dpi.Size(86, 24), Location = Dpi.Pt(330, y), BackColor = INPUT_BG, Cursor = Cursors.Hand };
            bool hov = false;
            btn.Paint += (s2, e2) => {
                var g2 = e2.Graphics; g2.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g2.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                Color bg = hov ? Color.FromArgb(30, 30, 30) : INPUT_BG; Color bdr = hov ? ACC : Color.FromArgb(60, ACC.R, ACC.G, ACC.B);
                int cr = Dpi.S(3);
                using (var path = DarkTheme.RoundedRect(new Rectangle(0, 0, btn.Width - 1, btn.Height - 1), cr)) { using (var b = new SolidBrush(bg)) g2.FillPath(b, path); using (var p = new Pen(bdr, 1f)) g2.DrawPath(p, path); }
                string txt = sel == 6 && !string.IsNullOrEmpty(_settings.CustomSoundPath) ? System.IO.Path.GetFileNameWithoutExtension(_settings.CustomSoundPath) : sndNames[Math.Min(sel, sndNames.Length-1)];
                if (txt.Length > 10) txt = txt.Substring(0, 9) + "..";
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(TXT)) g2.DrawString(txt, f, b, Dpi.S(6), Dpi.S(4));
                float cx2 = btn.Width - Dpi.S(12), cy2 = btn.Height / 2f;
                using (var p = new Pen(ACC, Dpi.S(2))) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g2.DrawLine(p, cx2 - Dpi.S(3), cy2 - Dpi.S(2), cx2, cy2 + Dpi.S(2)); g2.DrawLine(p, cx2, cy2 + Dpi.S(2), cx2 + Dpi.S(3), cy2 - Dpi.S(2)); }
            };
            btn.MouseEnter += (s2, e2) => { hov = true; btn.Invalidate(); };
            btn.MouseLeave += (s2, e2) => { hov = false; btn.Invalidate(); };
            card.Controls.Add(btn);

            btn.MouseClick += (s2, e2) => {
                // Close any other open dropdown
                if (_activeDropdownPopup != null && !_activeDropdownPopup.IsDisposed) {
                    _activeDropdownPopup.Close(); _activeDropdownPopup = null;
                }
                // Create topmost borderless popup form
                var popForm = new Form {
                    FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
                    ShowInTaskbar = false, TopMost = true, BackColor = Color.FromArgb(18, 18, 18),
                    Size = new Size(Dpi.S(86), Dpi.S(sndNames.Length * 24 + 4))
                };
                var screenPt = btn.PointToScreen(new Point(0, btn.Height + Dpi.S(2)));
                popForm.Location = screenPt;
                int hovIdx = -1;
                var popPanel = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 18, 18) };
                popPanel.Paint += (s3, e3) => {
                    var g2 = e3.Graphics; g2.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g2.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    int cr2 = Dpi.S(3);
                    using (var path = DarkTheme.RoundedRect(new Rectangle(0, 0, popPanel.Width - 1, popPanel.Height - 1), cr2)) {
                        using (var b = new SolidBrush(Color.FromArgb(18, 18, 18))) g2.FillPath(b, path);
                        using (var p = new Pen(Color.FromArgb(60, ACC.R, ACC.G, ACC.B), 1f)) g2.DrawPath(p, path);
                    }
                    for (int i = 0; i < sndNames.Length; i++) {
                        int iy = Dpi.S(2) + i * Dpi.S(24);
                        if (i == hovIdx) using (var b = new SolidBrush(Color.FromArgb(35, ACC.R, ACC.G, ACC.B))) g2.FillRectangle(b, Dpi.S(2), iy, popPanel.Width - Dpi.S(4), Dpi.S(24));
                        if (i == 6) using (var p = new Pen(Color.FromArgb(40, 40, 40))) g2.DrawLine(p, Dpi.S(6), iy, popPanel.Width - Dpi.S(6), iy);
                        Color tc = i == sel ? ACC : (i == 6 ? Color.FromArgb(180, 180, 180) : TXT);
                        using (var f = new Font("Segoe UI", 7.5f, i == sel ? FontStyle.Bold : (i == 6 ? FontStyle.Italic : FontStyle.Regular)))
                        using (var b = new SolidBrush(tc)) g2.DrawString(sndNames[i], f, b, Dpi.S(6), iy + Dpi.S(4));
                    }
                };
                popPanel.MouseMove += (s3, e3) => { int nh = (e3.Y - Dpi.S(2)) / Dpi.S(24); if (nh < 0 || nh >= sndNames.Length) nh = -1; if (nh != hovIdx) { hovIdx = nh; popPanel.Invalidate(); } };
                popPanel.MouseLeave += (s3, e3) => { hovIdx = -1; popPanel.Invalidate(); };
                popPanel.MouseClick += (s3, e3) => {
                    int ci = (e3.Y - Dpi.S(2)) / Dpi.S(24);
                    if (ci >= 0 && ci < sndNames.Length) {
                        popForm.Close(); _activeDropdownPopup = null;
                        if (ci == 6) {
                            using (var ofd = new OpenFileDialog()) {
                                ofd.Title = "Choose Feedback Sound";
                                ofd.Filter = "Sound Files|*.wav|All Files|*.*";
                                ofd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                                if (ofd.ShowDialog() == DialogResult.OK) { _settings.CustomSoundPath = ofd.FileName; sel = 6; onChange(6); btn.Invalidate(); }
                            }
                        } else { sel = ci; onChange(ci); btn.Invalidate(); TrayApp.PreviewFeedbackSound(ci); }
                    }
                };
                popForm.Controls.Add(popPanel);
                popForm.Deactivate += (s3, e3) => { if (!popForm.IsDisposed) popForm.Close(); _activeDropdownPopup = null; };
                _activeDropdownPopup = popForm;
                popForm.Show();
            };
            return btn;
        }
        void UpdateKey2Visibility() { LayoutPttKeys(); }
        /// <summary>Lays out all PTT keys horizontally on the hotkey row: [Key1][x] [Key2][x] [Key3][x] [Add Key]</summary>
        void LayoutPttKeys() {
            int ki = 64; // base x for hotkey area
            int hk1Y = 56 + 46; // base y for hotkey row
            int kbW = 80, rmW = 18, gap = 4;
            int x = ki + 50; // start after "Hotkey:" label
            // Key 1 — always visible, positioned by BuildPttPane, just ensure size
            _lblPttKey.Size = Dpi.Size(kbW, 26);
            _lblPttKey.Location = Dpi.Pt(x, hk1Y);
            x += kbW + gap;
            // Key 2
            bool hasKey2 = _pttKeyCode2 > 0 || _audio.CurrentTarget == CaptureTarget.PttKey2, hasKey3 = _pttKeyCode3 > 0 || _audio.CurrentTarget == CaptureTarget.PttKey3;
            if (hasKey2) {
                _lblPttKey2.Location = Dpi.Pt(x, hk1Y); _lblPttKey2.Visible = true;
                x += kbW;
                _btnRemoveKey2.Location = Dpi.Pt(x + 1, hk1Y + 4); _btnRemoveKey2.Visible = true;
                x += rmW + gap;
            } else { _lblPttKey2.Visible = false; _btnRemoveKey2.Visible = false; }
            // Key 3
            if (hasKey3) {
                _lblPttKey3.Location = Dpi.Pt(x, hk1Y); _lblPttKey3.Visible = true;
                x += kbW;
                _btnRemoveKey3.Location = Dpi.Pt(x + 1, hk1Y + 4); _btnRemoveKey3.Visible = true;
                x += rmW + gap;
            } else { _lblPttKey3.Visible = false; _btnRemoveKey3.Visible = false; }
            // "Add Key" button — show if we have fewer than 3 keys and the previous one is already assigned
            int keyCount = (_pttKeyCode > 0 ? 1 : 0) + (_pttKeyCode2 > 0 ? 1 : 0) + (_pttKeyCode3 > 0 ? 1 : 0);
            bool showAdd = _pttKeyCode > 0 && keyCount < 3 && !_audio.IsCapturing;
            if (showAdd) {
                // Use _btnAddKey2 if no key2, else _btnAddKey3
                if (!hasKey2) { _btnAddKey2.Location = Dpi.Pt(x, hk1Y); _btnAddKey2.Visible = true; _btnAddKey3.Visible = false; }
                else { _btnAddKey3.Location = Dpi.Pt(x, hk1Y); _btnAddKey3.Visible = true; _btnAddKey2.Visible = false; }
            } else { _btnAddKey2.Visible = false; _btnAddKey3.Visible = false; }
        }

        void LayoutPtmKeys() {
            int ki = 64; // base x for hotkey area
            int hk2Y = 56 + 46 + 34 + 10 + 46; // base y for PTM hotkey row
            int kbW = 80, rmW = 18, gap = 4;
            int x = ki + 50; // start after "Hotkey:" label
            if (_lblPtmKey == null) return;
            // Key 1
            _lblPtmKey.Size = Dpi.Size(kbW, 26);
            _lblPtmKey.Location = Dpi.Pt(x, hk2Y);
            x += kbW + gap;
            // Key 2
            bool hasKey2 = _ptmKeyCode2 > 0 || _audio.CurrentTarget == CaptureTarget.PtmKey2;
            bool hasKey3 = _ptmKeyCode3 > 0 || _audio.CurrentTarget == CaptureTarget.PtmKey3;

            if (hasKey2) {
                _lblPtmKey2.Location = Dpi.Pt(x, hk2Y); _lblPtmKey2.Visible = true;
                x += kbW;
                if (_btnPtmRemKey2 != null) { _btnPtmRemKey2.Location = Dpi.Pt(x + 1, hk2Y + 4); _btnPtmRemKey2.Visible = true; _btnPtmRemKey2.Size = Dpi.Size(rmW, rmW); }
                x += rmW + gap;
            } else { _lblPtmKey2.Visible = false; if (_btnPtmRemKey2 != null) _btnPtmRemKey2.Visible = false; }
            // Key 3
            if (hasKey3) {
                if (_lblPtmKey3 != null) { _lblPtmKey3.Location = Dpi.Pt(x, hk2Y); _lblPtmKey3.Visible = true; }
                x += kbW;
                if (_btnPtmRemKey3 != null) { _btnPtmRemKey3.Location = Dpi.Pt(x + 1, hk2Y + 4); _btnPtmRemKey3.Visible = true; _btnPtmRemKey3.Size = Dpi.Size(rmW, rmW); }
                x += rmW + gap;
            } else { if (_lblPtmKey3 != null) _lblPtmKey3.Visible = false; if (_btnPtmRemKey3 != null) _btnPtmRemKey3.Visible = false; }
            // Add Key button
            int keyCount = ((_ptmKeyCode > 0) ? 1 : 0) + ((_ptmKeyCode2 > 0) ? 1 : 0) + ((_ptmKeyCode3 > 0) ? 1 : 0);
            bool showAdd = _ptmKeyCode > 0 && keyCount < 3 && !_audio.IsCapturing;
            if (showAdd) {
                // Show the appropriate add button
                if (_ptmKeyCode2 <= 0) { if (_btnPtmAddKey2 != null) { _btnPtmAddKey2.Location = Dpi.Pt(x, hk2Y); _btnPtmAddKey2.Visible = true; } if (_btnPtmAddKey3 != null) _btnPtmAddKey3.Visible = false; }
                else { if (_btnPtmAddKey2 != null) _btnPtmAddKey2.Visible = false; if (_btnPtmAddKey3 != null) { _btnPtmAddKey3.Location = Dpi.Pt(x, hk2Y); _btnPtmAddKey3.Visible = true; } }
            } else { if (_btnPtmAddKey2 != null) _btnPtmAddKey2.Visible = false; if (_btnPtmAddKey3 != null) _btnPtmAddKey3.Visible = false; }
        }

        void LayoutToggleKeys() {
            int ki = 64; // base x for hotkey area
            int hk3Y = 56 + 46 + 34 + 10 + 46 + 34 + 10 + 46; // base y for Toggle hotkey row
            int kbW = 80, rmW = 18, gap = 4;
            int x = ki + 50; // start after "Hotkey:" label
            if (_lblPtToggleKey == null) return;
            // Key 1
            _lblPtToggleKey.Size = Dpi.Size(kbW, 26);
            _lblPtToggleKey.Location = Dpi.Pt(x, hk3Y);
            x += kbW + gap;
            // Key 2
            bool hasKey2 = _ptToggleKeyCode2 > 0 || _audio.CurrentTarget == CaptureTarget.ToggleKey2;
            bool hasKey3 = _ptToggleKeyCode3 > 0 || _audio.CurrentTarget == CaptureTarget.ToggleKey3;

            if (hasKey2) {
                _lblPtToggleKey2.Location = Dpi.Pt(x, hk3Y); _lblPtToggleKey2.Visible = true;
                x += kbW;
                if (_btnToggleRemKey2 != null) { _btnToggleRemKey2.Location = Dpi.Pt(x + 1, hk3Y + 4); _btnToggleRemKey2.Visible = true; _btnToggleRemKey2.Size = Dpi.Size(rmW, rmW); }
                x += rmW + gap;
            } else { _lblPtToggleKey2.Visible = false; if (_btnToggleRemKey2 != null) _btnToggleRemKey2.Visible = false; }
            // Key 3
            if (hasKey3) {
                if (_lblPtToggleKey3 != null) { _lblPtToggleKey3.Location = Dpi.Pt(x, hk3Y); _lblPtToggleKey3.Visible = true; }
                x += kbW;
                if (_btnToggleRemKey3 != null) { _btnToggleRemKey3.Location = Dpi.Pt(x + 1, hk3Y + 4); _btnToggleRemKey3.Visible = true; _btnToggleRemKey3.Size = Dpi.Size(rmW, rmW); }
                x += rmW + gap;
            } else { if (_lblPtToggleKey3 != null) _lblPtToggleKey3.Visible = false; if (_btnToggleRemKey3 != null) _btnToggleRemKey3.Visible = false; }
            // Add Key button
            int keyCount = ((_ptToggleKeyCode > 0) ? 1 : 0) + ((_ptToggleKeyCode2 > 0) ? 1 : 0) + ((_ptToggleKeyCode3 > 0) ? 1 : 0);
            bool showAdd = _ptToggleKeyCode > 0 && keyCount < 3 && !_audio.IsCapturing;
            if (showAdd) {
                if (_ptToggleKeyCode2 <= 0) { if (_btnToggleAddKey2 != null) { _btnToggleAddKey2.Location = Dpi.Pt(x, hk3Y); _btnToggleAddKey2.Visible = true; } if (_btnToggleAddKey3 != null) _btnToggleAddKey3.Visible = false; }
                else { if (_btnToggleAddKey2 != null) _btnToggleAddKey2.Visible = false; if (_btnToggleAddKey3 != null) { _btnToggleAddKey3.Location = Dpi.Pt(x, hk3Y); _btnToggleAddKey3.Visible = true; } }
            } else { if (_btnToggleAddKey2 != null) _btnToggleAddKey2.Visible = false; if (_btnToggleAddKey3 != null) _btnToggleAddKey3.Visible = false; }
        }
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        static bool IsKeyDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
        /// <summary>Checks if a key is held. For toggle keys (CapsLock/ScrollLock/NumLock), uses PushToTalk's LL hook state because GetAsyncKeyState is unreliable for these keys.</summary>
        static bool IsKeyHeld(int vk) {
            if (vk == 0x14 || vk == 0x91 || vk == 0x90) // VK_CAPS_LOCK, VK_SCROLL_LOCK, VK_NUM_LOCK
                return PushToTalk.HookHeldKey == vk;
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }
        string KeyName(int c){return PushToTalk.GetKeyName(c);}

        /// <summary>Shake a label left-right with red flash to indicate rejected input. Premium rejection feel.</summary>
        void ShakeReject(Label lbl, Action onComplete = null)
        {
            var origLoc = lbl.Location;
            var origBg = lbl.BackColor;
            var origFg = lbl.ForeColor;
            lbl.BackColor = Color.FromArgb(60, 20, 20);
            lbl.ForeColor = Color.FromArgb(220, 55, 55);
            int tick = 0;
            int[] offsets = { 6, -6, 5, -5, 3, -3, 2, -1, 0 };
            var shakeTimer = new Timer { Interval = 25 };
            shakeTimer.Tick += (s, e) => {
                if (tick >= offsets.Length) {
                    shakeTimer.Stop(); shakeTimer.Dispose();
                    lbl.Location = origLoc;
                    var fadeTimer = new Timer { Interval = 400 };
                    fadeTimer.Tick += (s2, e2) => { fadeTimer.Stop(); fadeTimer.Dispose(); lbl.BackColor = origBg; lbl.ForeColor = origFg; if (onComplete != null) onComplete(); };
                    fadeTimer.Start();
                    return;
                }
                lbl.Location = new Point(origLoc.X + Dpi.S(offsets[tick]), origLoc.Y);
                tick++;
            };
            shakeTimer.Start();
        }

        /// <summary>Brief glow flash on a toggle after it's auto-enabled by hotkey capture.</summary>
        private DateTime _lastFlashTime = DateTime.MinValue;
        private Form _activeDropdownPopup; // tracks currently open dropdown — only one at a time
        private ToggleSwitch _flashTarget;
        private int _flashAlpha;
        private Timer _flashFadeTimer;

        void FlashToggleOn(ToggleSwitch tgl) {
            if (tgl == null || tgl.Parent == null) return;
            if ((DateTime.Now - _lastFlashTime).TotalMilliseconds < 800) return; // cooldown
            _lastFlashTime = DateTime.Now;
            _flashTarget = tgl;
            _flashAlpha = 40;
            if (_flashFadeTimer == null) {
                _flashFadeTimer = new Timer { Interval = 50 };
                _flashFadeTimer.Tick += (s2, e2) => {
                    _flashAlpha -= 4;
                    if (_flashAlpha <= 0) { _flashAlpha = 0; _flashTarget = null; _flashFadeTimer.Stop(); }
                    if (_pttCard != null) {
                        _pttCard.Invalidate();
                        // Invalidate all children in flash region so they repaint with flash tint
                        foreach (Control c in _pttCard.Controls) c.Invalidate();
                    }
                };
            }
            _flashFadeTimer.Start();
            if (_pttCard != null) {
                _pttCard.Invalidate();
                foreach (Control c in _pttCard.Controls) c.Invalidate();
            }
        }

        private Timer _enforceTimer;

        /// <summary>If a hotkey is set but no PTT/PTM/PTToggle is enabled, shake the hotkey field
        /// and flash the three toggle rows in sequence (1-2-3, 1-2-3...) until user picks one.</summary>
        void EnforceToggleSelection()
        {
            if (_tglPtt.Checked || _tglPtm.Checked || _tglPtToggle.Checked) return;
            if (_enforceTimer != null) {
                // Re-trigger: user clicked something without choosing a toggle
                if (_enforceClickHandler != null) _enforceClickHandler(null, null);
                return;
            }

            var card = _tglPtt.Parent;
            if (card == null) return;

            // Navigate to the PTT pane so user can see the toggles
            SwitchPane(0);

            // Highlight panels for each toggle row — use BufferedPanel to prevent flicker
            var highlights = new Panel[3];
            var toggles = new ToggleSwitch[] { _tglPtt, _tglPtm, _tglPtToggle };
            Color flashColor = Color.FromArgb(60, ACC.R, ACC.G, ACC.B);
            Color flashBorder = Color.FromArgb(180, ACC.R, ACC.G, ACC.B);

            for (int i = 0; i < 3; i++)
            {
                var tgl = toggles[i];
                highlights[i] = new BufferedPanel {
                    Location = new Point(Dpi.S(4), tgl.Top - Dpi.S(4)),
                    Size = new Size(card.Width - Dpi.S(8), Dpi.S(42)),
                    BackColor = Color.Transparent
                };
                highlights[i].Paint += (s, e) => {
                    var hl = (Panel)s;
                    if (hl.BackColor != Color.Transparent) {
                        using (var p = new Pen(flashBorder, 2f))
                            e.Graphics.DrawRectangle(p, 1, 1, hl.Width - 3, hl.Height - 3);
                    }
                };
                highlights[i].Visible = false;
                card.Controls.Add(highlights[i]);
                // Toggles are hidden — clicks handled by CardToggle through card MouseClick
            }

            // Animation: shake + 1-2-3, 1-2-3, then HIDE and stop.
            int step = 0;
            _enforceCard = card;
            _enforceHighlights = highlights;
            _enforceTimer = new Timer { Interval = 180 };
            _enforceTimer.Tick += (s, e) => {
                if (_tglPtt.Checked || _tglPtm.Checked || _tglPtToggle.Checked)
                {
                    CleanupEnforcement();
                    return;
                }

                if (step < 6)
                {
                    int idx = step % 3;
                    for (int i = 0; i < 3; i++)
                    {
                        highlights[i].BackColor = (i == idx) ? flashColor : Color.Transparent;
                        highlights[i].Visible = true;
                        highlights[i].Invalidate();
                    }
                    step++;
                }
                else if (step == 6)
                {
                    // Animation done — fully clean up (remove panels, detach handlers)
                    CleanupEnforcement();
                }
            };

            _enforceClickHandler = (s, e) => {
                if (_tglPtt.Checked || _tglPtm.Checked || _tglPtToggle.Checked) return;
                step = 0;
                ShakeReject(_lblPttKey);
                if (!_enforceTimer.Enabled) _enforceTimer.Start();
            };
            MouseClick += _enforceClickHandler;
            card.MouseClick += _enforceClickHandler;

            _enforceTimer.Start();
            ShakeReject(_lblPttKey);
        }
        private Control _enforceCard;
        private Panel[] _enforceHighlights;

        void CleanupEnforcement()
        {
            if (_enforceTimer != null) { _enforceTimer.Stop(); _enforceTimer.Dispose(); _enforceTimer = null; }
            if (_enforceHighlights != null && _enforceCard != null)
                foreach (var h in _enforceHighlights) { try { _enforceCard.Controls.Remove(h); h.Dispose(); } catch { } }
            _enforceHighlights = null;
            if (_lblPttKey != null) { _lblPttKey.BackColor = INPUT_BG; _lblPttKey.ForeColor = ACC; }
            if (_enforceClickHandler != null)
            {
                MouseClick -= _enforceClickHandler;
                if (_enforceCard != null) _enforceCard.MouseClick -= _enforceClickHandler;
                _enforceClickHandler = null;
            }
            _enforceCard = null;
        }
        private MouseEventHandler _enforceClickHandler;

        /// <summary>Paint a premium circle-X remove icon with anti-aliased lines.</summary>
        void PaintRemoveIcon(Graphics g, Rectangle r, bool hover)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            int rad = Dpi.S(9);
            Color circleColor = hover ? Color.FromArgb(50, 220, 60, 60) : Color.FromArgb(30, 140, 60, 60);
            Color xColor = hover ? Color.FromArgb(230, 70, 70) : Color.FromArgb(140, 60, 60);
            using (var b = new SolidBrush(circleColor))
                g.FillEllipse(b, cx - rad, cy - rad, rad * 2, rad * 2);
            using (var p = new Pen(hover ? Color.FromArgb(80, 220, 60, 60) : Color.FromArgb(40, 140, 60, 60), 1f))
                g.DrawEllipse(p, cx - rad, cy - rad, rad * 2, rad * 2);
            int arm = Dpi.S(4);
            using (var p = new Pen(xColor, Dpi.S(2)))
            {
                p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                g.DrawLine(p, cx - arm, cy - arm, cx + arm, cy + arm);
                g.DrawLine(p, cx + arm, cy - arm, cx - arm, cy + arm);
            }
        }

        void ScanApps(){
            try{
                var ss=Audio.GetAudioSessions();
                int totalSessions = Audio.LastScanTotalSessions;
                int skippedSessions = Audio.LastScanSkippedSessions;
                if(ss==null||ss.Count==0){
                    string diag = "No apps with active audio sessions detected.\n\n";
                    diag += "Total raw sessions: " + totalSessions + "\n";
                    diag += "Sessions skipped: " + skippedSessions + "\n";
                    if (!string.IsNullOrEmpty(Audio.LastScanDiagnostics))
                        diag += "\nSkip reasons:\n" + Audio.LastScanDiagnostics;
                    diag += "\nTip: Apps must be actively playing audio to appear.";
                    DarkMessage.Show(diag, "Scan Running Apps");
                    return;
                }
                var existing = CollectAppRules();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var a in ss){
                    string n = a.ProcessName;
                    if (seen.Contains(n)) continue;
                    seen.Add(n);
                    // existing[] returns encoded values: positive=locked vol, negative=unlocked -(vol+1)
                    int rawExisting;
                    int vol;
                    if (existing.TryGetValue(n, out rawExisting))
                        vol = rawExisting >= 0 ? rawExisting : -(rawExisting + 1);
                    else
                        vol = (int)a.Volume;
                    // Update existing row or add new
                    var existRow = _appRows.Find(r => string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase));
                    if (existRow != null) {
                        if (existRow.Slider != null) existRow.Slider.Value = vol;
                        else existRow.InitialValue = vol;
                    } else {
                        _appRows.Add(new AppRuleRow { Name = n, InitialValue = vol });
                    }
                }
                RebuildAppList(ss); // Reuse sessions we already fetched
                DarkMessage.Show("Found " + ss.Count + " Audio App(s)\nClick the lock icon next to an app to enforce its volume.", "Scan Complete");
            }catch(Exception err){
                Logger.Error("ScanApps failed.",err);
                DarkMessage.Show("Scan failed: "+err.Message+"\n\nCheck the log file for details.", "Scan Error");
            }
        }

        /// <summary>Refreshes the overlay toggle from current settings (called externally when overlay is dismissed).</summary>
        public void RefreshOverlayToggle() {
            try {
                if (InvokeRequired) { BeginInvoke((Action)RefreshOverlayToggle); return; }
                _loading = true;
                _tglOverlay.Checked = _settings.MicOverlayEnabled;
                
                _loading = false;
            } catch { _loading = false; }
        }

        /// <summary>Blinks the overlay toggle row 3 times to draw attention.</summary>
        /// <summary>Smoothly animate a slider to a target value, then call onDone.</summary>
        void AnimateSlider(SlickSlider slider, int target, Action onDone)
        {
            if (slider.Value == target) { if (onDone != null) onDone(); return; }
            var tmr = new Timer { Interval = 30 };
            tmr.Tick += (s, e) => {
                int diff = target - slider.Value;
                int step = Math.Max(1, Math.Abs(diff) / 5);
                if (Math.Abs(diff) <= step) {
                    slider.Value = target;
                    tmr.Stop(); tmr.Dispose();
                    if (onDone != null) onDone();
                } else {
                    slider.Value += diff > 0 ? step : -step;
                }
            };
            tmr.Start();
        }

        /// <summary>Max All button on Apps page — smoothly animates all app sliders to max.</summary>
        void AnimateMaxAllApps()
        {
            foreach (var row in _appRows)
            {
                if (row.Slider == null) continue;
                int max = row.Slider.Maximum;
                AnimateSlider(row.Slider, max, null);
            }
        }

        public void BlinkOverlayToggle() {
            try {
                if (InvokeRequired) { BeginInvoke((Action)BlinkOverlayToggle); return; }
                if (_tglOverlay == null || _tglOverlay.Parent == null) return;
                var card = _tglOverlay.Parent;
                int tglY = _tglOverlay.Top;
                // Create a highlight panel over the toggle row
                var highlight = new Panel {
                    Location = new Point(Dpi.S(4), tglY - Dpi.S(4)),
                    Size = new Size(card.Width - Dpi.S(8), Dpi.S(42)),
                    BackColor = Color.FromArgb(40, ACC.R, ACC.G, ACC.B)
                };
                highlight.Paint += (s, e) => {
                    using (var p = new Pen(Color.FromArgb(120, ACC.R, ACC.G, ACC.B), 2f))
                        e.Graphics.DrawRectangle(p, 1, 1, highlight.Width - 3, highlight.Height - 3);
                };
                card.Controls.Add(highlight);
                highlight.BringToFront();
                _tglOverlay.BringToFront();
                // Find and bring the label controls to front too
                foreach (Control c in card.Controls) {
                    if (c is Label && c.Top >= tglY - Dpi.S(2) && c.Top <= tglY + Dpi.S(30))
                        c.BringToFront();
                }
                // Blink 4 times then remove
                int blinks = 0;
                var blinkTimer = new Timer { Interval = 300 };
                blinkTimer.Tick += (s, e) => {
                    blinks++;
                    highlight.Visible = !highlight.Visible;
                    if (blinks >= 8) { // 4 on + 4 off
                        blinkTimer.Stop(); blinkTimer.Dispose();
                        try { card.Controls.Remove(highlight); highlight.Dispose(); } catch { }
                    }
                };
                blinkTimer.Start();
            } catch { }
        }

        void LoadSettings(){try{ _loading=true;
            _tglAfkMic.Checked=_settings.AfkMicMuteEnabled;_nudAfkMicSec.Value=Clamp(_settings.AfkMicMuteSec,5,3600);
            _tglAfkSpk.Checked=_settings.AfkSpeakerMuteEnabled;_nudAfkSpkSec.Value=Clamp(_settings.AfkSpeakerMuteSec,5,3600);
            _tglPtt.Checked=_settings.PushToTalkEnabled;_tglPtm.Checked=_settings.PushToMuteEnabled;_tglPtToggle.Checked=_settings.PushToToggleEnabled;_pttKeyCode=_settings.PushToTalkEnabled?_settings.PushToTalkKey:0;_lblPttKey.Text=_pttKeyCode>0?KeyName(_pttKeyCode):"Add Key";_pttKeyCode2=_settings.PushToTalkEnabled?_settings.PushToTalkKey2:0;_pttKeyCode3=_settings.PushToTalkEnabled?_settings.PushToTalkKey3:0;_key1ShowOverlay=_settings.PttKey1ShowOverlay;_key2ShowOverlay=_settings.PttKey2ShowOverlay;_key3ShowOverlay=_settings.PttKey3ShowOverlay;if(_chkKey1Overlay!=null)_chkKey1Overlay.Checked=_key1ShowOverlay;if(_chkKey2Overlay!=null)_chkKey2Overlay.Checked=_key2ShowOverlay;if(_chkKey3Overlay!=null)_chkKey3Overlay.Checked=_key3ShowOverlay;if(_lblPttKey2!=null){_lblPttKey2.Text=_pttKeyCode2>0?KeyName(_pttKeyCode2):"";UpdateKey2Visibility();}if(_lblPttKey3!=null){_lblPttKey3.Text=_pttKeyCode3>0?KeyName(_pttKeyCode3):"";UpdateKey3Visibility();}
            if(_lblPtmKey!=null){_ptmKeyCode=_settings.PushToMuteEnabled?_settings.PushToMuteKey:0;_ptmKeyCode2=_settings.PushToMuteEnabled?_settings.PushToMuteKey2:0;_ptmKeyCode3=_settings.PushToMuteEnabled?_settings.PushToMuteKey3:0;_lblPtmKey.Text=_ptmKeyCode>0?KeyName(_ptmKeyCode):"Add Key";if(_lblPtmKey2!=null)_lblPtmKey2.Text=_ptmKeyCode2>0?KeyName(_ptmKeyCode2):"";if(_lblPtmKey3!=null)_lblPtmKey3.Text=_ptmKeyCode3>0?KeyName(_ptmKeyCode3):"";}if(_lblPtToggleKey!=null){_ptToggleKeyCode=_settings.PushToToggleEnabled?_settings.PushToToggleKey:0;_ptToggleKeyCode2=_settings.PushToToggleEnabled?_settings.PushToToggleKey2:0;_ptToggleKeyCode3=_settings.PushToToggleEnabled?_settings.PushToToggleKey3:0;_lblPtToggleKey.Text=_ptToggleKeyCode>0?KeyName(_ptToggleKeyCode):"Add Key";if(_lblPtToggleKey2!=null)_lblPtToggleKey2.Text=_ptToggleKeyCode2>0?KeyName(_ptToggleKeyCode2):"";if(_lblPtToggleKey3!=null)_lblPtToggleKey3.Text=_ptToggleKeyCode3>0?KeyName(_ptToggleKeyCode3):"";}_tglOverlay.Checked=_settings.MicOverlayEnabled;
            _tglMicEnf.Checked=_settings.MicEnforceEnabled;_trkMicVol.Value=Clamp(_settings.MicVolumePercent,0,100);_plMicVol.Text=_trkMicVol.Value+"%";
            _tglSpkEnf.Checked=_settings.SpeakerEnforceEnabled;_trkSpkVol.Value=Clamp(_settings.SpeakerVolumePercent,0,100);_plSpkVol.Text=_trkSpkVol.Value+"%";
            // Refresh PTT hotkey label on Volume Lock page
            RefreshPttHotkeyLabel();
            // Snapshot current system volume so unlock can restore it even if app started with lock on
            if (_settings.MicEnforceEnabled) { try { _micPreLockVol = (int)Audio.GetMicVolume(); } catch { _micPreLockVol = -1; } }
            if (_settings.SpeakerEnforceEnabled) { try { _spkPreLockVol = (int)Audio.GetSpeakerVolume(); } catch { _spkPreLockVol = -1; } }
            _tglAppEnf.Checked=_settings.AppVolumeEnforceEnabled;
            if(_settings.AppVolumeRules!=null&&_settings.AppVolumeRules.Count>0){_appRows.Clear();foreach(var kv in _settings.AppVolumeRules){bool locked=kv.Value>=0;int vol=locked?kv.Value:-(kv.Value+1);_appRows.Add(new AppRuleRow{Name=kv.Key,Locked=locked,InitialValue=Math.Max(0,Math.Min(100,vol))});}RebuildAppList();}
            _tglStartup.Checked=_settings.StartWithWindows;_tglNotifyCorr.Checked=_settings.NotifyOnCorrection;_tglNotifyDev.Checked=_settings.NotifyOnDeviceChange;
            // Voice Activity
            if (_tglVoiceActivity != null) _tglVoiceActivity.Checked = _settings.VoiceActivityEnabled;
            if (_trkVoiceThreshold != null) { _trkVoiceThreshold.Value = Clamp(_settings.VoiceActivityThreshold, 1, 100); if (_lblVoiceThresholdPct != null) _lblVoiceThresholdPct.Text = "Threshold: " + _trkVoiceThreshold.Value + "%"; }
            if (_nudVoiceHoldover != null) _nudVoiceHoldover.Value = Clamp(_settings.VoiceActivityHoldoverMs, 200, 5000);
            CompactKeys();
            // Enforce PTT/PTM mutual exclusivity on load (Toggle is the gatekeeper)
            if (!_tglPtToggle.Checked && _tglPtt.Checked && _tglPtm.Checked) {
                _tglPtm.Checked = false;
                _ptmKeyCode=0;_ptmKeyCode2=0;_ptmKeyCode3=0;
                if(_lblPtmKey!=null){_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;}
                LayoutPtmKeys();
                _audio.DisablePtmMode();
            }
        }catch(Exception ex){Logger.Error("Options load failed.",ex);}finally{_loading=false;}}

        void DoSave(){
            if (IsCapturingKey) { CancelAllCaptures(); }
            try{
            // All audio settings are already saved live via _audio property setters.
            // Only need to persist app volume rules (not yet on _audio) and compact keys.
            _settings.AppVolumeEnforceEnabled=_tglAppEnf.Checked;_settings.AppVolumeRules=CollectAppRules();
            CompactKeys();
            _settings.Save();DialogResult=DialogResult.OK;Close();
        }catch(Exception ex){Logger.Error("Options save failed.",ex);DarkMessage.Show("Save failed: "+ex.Message,"Error");}}

        void CompactKeys(){
            // PTT: shift keys left to fill gaps
            if(_pttKeyCode<=0 && _pttKeyCode2>0){_pttKeyCode=_pttKeyCode2;_pttKeyCode2=0;}
            if(_pttKeyCode2<=0 && _pttKeyCode3>0){_pttKeyCode2=_pttKeyCode3;_pttKeyCode3=0;}
            if(_pttKeyCode<=0 && _pttKeyCode2>0){_pttKeyCode=_pttKeyCode2;_pttKeyCode2=0;}
            // PTM: shift keys left to fill gaps (now supports 3 keys)
            if(_ptmKeyCode<=0 && _ptmKeyCode2>0){_ptmKeyCode=_ptmKeyCode2;_ptmKeyCode2=0;}
            if(_ptmKeyCode2<=0 && _ptmKeyCode3>0){_ptmKeyCode2=_ptmKeyCode3;_ptmKeyCode3=0;}
            if(_ptmKeyCode<=0 && _ptmKeyCode2>0){_ptmKeyCode=_ptmKeyCode2;_ptmKeyCode2=0;}
            // Toggle: shift keys left to fill gaps (now supports 3 keys)
            if(_ptToggleKeyCode<=0 && _ptToggleKeyCode2>0){_ptToggleKeyCode=_ptToggleKeyCode2;_ptToggleKeyCode2=0;}
            if(_ptToggleKeyCode2<=0 && _ptToggleKeyCode3>0){_ptToggleKeyCode2=_ptToggleKeyCode3;_ptToggleKeyCode3=0;}
            if(_ptToggleKeyCode<=0 && _ptToggleKeyCode2>0){_ptToggleKeyCode=_ptToggleKeyCode2;_ptToggleKeyCode2=0;}
            
            if(_lblPttKey!=null) { _lblPttKey.Text=_pttKeyCode>0?KeyName(_pttKeyCode):"Add Key"; _lblPttKey.BackColor=INPUT_BG; _lblPttKey.ForeColor=ACC; }
            if(_lblPttKey2!=null) { _lblPttKey2.Text=_pttKeyCode2>0?KeyName(_pttKeyCode2):""; _lblPttKey2.BackColor=INPUT_BG; _lblPttKey2.ForeColor=ACC; }
            if(_lblPttKey3!=null) { _lblPttKey3.Text=_pttKeyCode3>0?KeyName(_pttKeyCode3):""; _lblPttKey3.BackColor=INPUT_BG; _lblPttKey3.ForeColor=ACC; }
            
            if(_lblPtmKey!=null) { _lblPtmKey.Text=_ptmKeyCode>0?KeyName(_ptmKeyCode):"Add Key"; _lblPtmKey.BackColor=INPUT_BG; _lblPtmKey.ForeColor=ACC; }
            if(_lblPtmKey2!=null) { _lblPtmKey2.Text=_ptmKeyCode2>0?KeyName(_ptmKeyCode2):""; _lblPtmKey2.BackColor=INPUT_BG; _lblPtmKey2.ForeColor=ACC; }
            if(_lblPtmKey3!=null) { _lblPtmKey3.Text=_ptmKeyCode3>0?KeyName(_ptmKeyCode3):""; _lblPtmKey3.BackColor=INPUT_BG; _lblPtmKey3.ForeColor=ACC; }
            
            if(_lblPtToggleKey!=null) { _lblPtToggleKey.Text=_ptToggleKeyCode>0?KeyName(_ptToggleKeyCode):"Add Key"; _lblPtToggleKey.BackColor=INPUT_BG; _lblPtToggleKey.ForeColor=ACC; }
            if(_lblPtToggleKey2!=null) { _lblPtToggleKey2.Text=_ptToggleKeyCode2>0?KeyName(_ptToggleKeyCode2):""; _lblPtToggleKey2.BackColor=INPUT_BG; _lblPtToggleKey2.ForeColor=ACC; }
            if(_lblPtToggleKey3!=null) { _lblPtToggleKey3.Text=_ptToggleKeyCode3>0?KeyName(_ptToggleKeyCode3):""; _lblPtToggleKey3.BackColor=INPUT_BG; _lblPtToggleKey3.ForeColor=ACC; }
            
            _settings.PushToTalkKey = _pttKeyCode;
            _settings.PushToTalkKey2 = _pttKeyCode2;
            _settings.PushToTalkKey3 = _pttKeyCode3;
            _settings.PushToMuteKey = _ptmKeyCode;
            _settings.PushToMuteKey2 = _ptmKeyCode2;
            _settings.PushToMuteKey3 = _ptmKeyCode3;
            _settings.PushToToggleKey = _ptToggleKeyCode;
            _settings.PushToToggleKey2 = _ptToggleKeyCode2;
            _settings.PushToToggleKey3 = _ptToggleKeyCode3;

            LayoutPttKeys();
            LayoutPtmKeys();
            LayoutToggleKeys();
        }

        Dictionary<string,int> ParseAppRules(){ return CollectAppRules(); }
        static int Clamp(int v,int min,int max){return v<min?min:v>max?max:v;}
        public void OnRunWizard(){DialogResult=DialogResult.Retry;Close();}
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            // During key capture, suppress Escape from closing the form — let the polling handler deal with it
            if (_audio != null && _audio.IsCapturing) {
                if (keyData == Keys.Escape) return true; // swallow it — polling will handle the cancel
                return true; // swallow all keys during capture
            }
            if (keyData == Keys.Space) return true;
            if (keyData == Keys.Escape) { if (_starShowMode) { ToggleStarShow(); return true; } return base.ProcessCmdKey(ref msg, keyData); }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        // WS_EX_COMPOSITED: forces all child controls to paint in a single composited pass.
        // Eliminates the parent-then-child paint gap that causes black line artifacts on sliders.
        private Timer _updateShimmerTimer;
        private Timer _saveOrbitTimer;
        private float _saveOrbitPhase;
        // Footer buttons are owner-drawn (no child controls)
        private float _updateShimmerX;
        private Button _updateBtn;
        private bool _updateShimmering;
        private DateTime _updateStartTime;
        private int _pendingUpdateResult; // 0=pending, 1=up-to-date, 2=update-available, -1=error
        private string _pendingLatestVer;

        private void CheckForUpdates(Button btn)
        {
            btn.Text = "Checking...";
            btn.ForeColor = Color.White;
            btn.FlatAppearance.BorderColor = ACC;
            _updateBtn = btn;
            _pendingUpdateResult = 0;
            _updateStartTime = DateTime.UtcNow;

            // Start shimmer
            _updateShimmerX = -0.3f;
            _updateShimmering = true;
            btn.Paint -= UpdateBtnShimmerPaint;
            btn.Paint += UpdateBtnShimmerPaint;
            if (_updateShimmerTimer == null)
            {
                _updateShimmerTimer = new Timer { Interval = 10 };
                _updateShimmerTimer.Tick += (s, e) =>
                {
                    if (!_updateShimmering) { _updateShimmerTimer.Stop(); if (_updateBtn != null) _updateBtn.Invalidate(); return; }
                    _updateShimmerX += 0.02f;
                    if (_updateShimmerX > 1.3f)
                    {
                        // Check if result is in AND minimum time elapsed
                        if (_pendingUpdateResult != 0 && (DateTime.UtcNow - _updateStartTime).TotalMilliseconds >= 1500)
                        {
                            _updateShimmering = false;
                            _updateShimmerTimer.Stop();
                            ApplyUpdateResult();
                            return;
                        }
                        _updateShimmerX = -0.3f;
                    }
                    if (_updateBtn != null) _updateBtn.Invalidate();
                };
            }
            _updateShimmerTimer.Start();

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // GitHub requires TLS 1.2
                    System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                    using (var client = new System.Net.WebClient())
                    {
                        client.Headers.Add("User-Agent", "AngryAudio/" + AppVersion.Version);
                        string raw = client.DownloadString("https://raw.githubusercontent.com/Gantera2k/Angry-Audio/main/version.txt");
                        string latest = raw.Trim().Trim(new char[]{'\uFEFF', '\u200B'}); // Strip BOM and zero-width spaces
                        int cmp = CompareVersions(latest, AppVersion.Version);
                        _pendingLatestVer = latest;
                        _pendingUpdateResult = cmp > 0 ? 2 : 1;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Update check failed.", ex);
                    _pendingUpdateResult = -1;
                }
            });
        }

        private void UpdateBtnShimmerPaint(object sender, PaintEventArgs e)
        {
            if (!_updateShimmering) return;
            var btn = (Button)sender;
            int w = btn.Width, h = btn.Height;
            int bandW = Math.Max(w / 3, 20);
            int cx = (int)(_updateShimmerX * (w + bandW)) - bandW / 2;
            var shimmerRect = new Rectangle(cx - bandW / 2, 0, bandW, h);
            try
            {
                using (var lgb = new LinearGradientBrush(
                    new Point(shimmerRect.Left, 0), new Point(shimmerRect.Right, 0),
                    Color.Transparent, Color.Transparent))
                {
                    var cb = new System.Drawing.Drawing2D.ColorBlend(3);
                    cb.Colors = new[] {
                        Color.FromArgb(0, 255, 255, 255),
                        Color.FromArgb(45, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255)
                    };
                    cb.Positions = new[] { 0f, 0.5f, 1f };
                    lgb.InterpolationColors = cb;
                    e.Graphics.FillRectangle(lgb, shimmerRect);
                }
            }
            catch { }
        }

        private void ApplyUpdateResult()
        {
            if (_updateBtn == null) return;
            _updateBtn.Paint -= UpdateBtnShimmerPaint;
            _updateBtn.Invalidate();

            if (_pendingUpdateResult == 2)
            {
                _updateBtn.Text = "\u2B06 Update v" + _pendingLatestVer;
                _updateBtn.ForeColor = DarkTheme.Green;
                _updateBtn.FlatAppearance.BorderColor = DarkTheme.Green;
                _updateBtn.BackColor = Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);

                _updateBtn.Click -= _updateCheckHandler;
                _updateBtn.Click -= _updateDownloadHandler;
                _updateDownloadHandler = (s2, e2) => {
                    UpdateDialog.ShowUpdate(_pendingLatestVer);
                };
                _updateBtn.Click += _updateDownloadHandler;
            }
            else if (_pendingUpdateResult == 1)
            {
                _updateBtn.Text = "\u2714 Up to Date";
                _updateBtn.ForeColor = DarkTheme.Green;
                _updateBtn.FlatAppearance.BorderColor = DarkTheme.Green;
                _updateBtn.BackColor = Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);

            }
            else
            {
                _updateBtn.Text = "\u26A0 Check Failed \u2014 Retry";
                _updateBtn.ForeColor = Color.FromArgb(255, 180, 80);
                _updateBtn.FlatAppearance.BorderColor = Color.FromArgb(255, 180, 80);

            }
        }

        private EventHandler _updateDownloadHandler;
        private EventHandler _updateCheckHandler;

        /// <summary>Compares version strings like "51.1" vs "50.9". Returns positive if a > b.</summary>
        private static int CompareVersions(string a, string b)
        {
            var pa = a.Split('.'); var pb = b.Split('.');
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                int va = i < pa.Length ? int.Parse(pa[i].Trim()) : 0;
                int vb = i < pb.Length ? int.Parse(pb[i].Trim()) : 0;
                if (va != vb) return va - vb;
            }
            return 0;
        }

        // Suspend star animation during resize to prevent flicker
        private bool _isResizing;
        protected override void OnResizeBegin(EventArgs e) {
            base.OnResizeBegin(e);
            _isResizing = true;
            if (_twinkleTimer != null) _twinkleTimer.Stop();
        }
        protected override void OnResizeEnd(EventArgs e) {
            base.OnResizeEnd(e);
            _isResizing = false;
            if (_twinkleTimer != null) _twinkleTimer.Start();
            Invalidate(true);
        }

        // WS_EX_COMPOSITED: forces all child controls to paint in a single composited pass.
        // Research: https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles
        protected override CreateParams CreateParams {
            get {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }
    }

}
