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
    public class OptionsForm : Form, IMessageFilter
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        public bool ForceNoActivation { get; set; }
        protected override bool ShowWithoutActivation { get { return ForceNoActivation ? true : base.ShowWithoutActivation; } }

        private Settings _settings;
        private Panel _contentPanel, _sidebar, _footer;
        private Panel[] _panes, _navPanels, _navAccents;

        private Label[] _navLabels;
        private int _activePane = 0;
        private bool _starShowMode = false;
        private int _savedMascotX, _savedMascotY;

        private ToggleSwitch _tglPtt, _tglPtm, _tglPtToggle, _tglMicEnf, _tglSpkEnf, _tglAppEnf, _tglStartup, _tglNotifyCorr, _tglNotifyDev, _tglOverlay, _tglRestrictSound;
        private string _pttWarningText = "";
        private int _pttWarningY = 0;
        private string _spkWarningText = "";
        private int _spkWarningY = 0;
        private Label _lblPttKey, _lblPttKey2, _lblPtmKey, _lblPtmKey2, _lblPtToggleKey, _lblPtToggleKey2;
        private Label _lblKey2Label, _lblKey2Hint;
        private Button _btnRemoveKey, _btnRemoveKey2, _btnAddKey2;
        private Button _btnPtmRemKey, _btnPtmAddKey2, _btnPtmRemKey2, _btnToggleRemKey, _btnToggleAddKey2, _btnToggleRemKey2;
        private CardIcon _chkKey1Overlay, _chkKey2Overlay;
        private CardIcon _chkPttSound, _chkPtmOverlay, _chkPtmSound, _chkToggleOverlay, _chkToggleSound;
        private CardIcon _duckPtt, _duckPtm, _duckToggle; // rubber duck icons for PTT page
        private CardIcon _keyPtt, _keyPtm, _keyToggle; // key suppress icons for PTT page
        private float _ptmDuckSnapshot = -1f, _toggleDuckSnapshot = -1f; // pre-duck speaker volume snapshots
        private CardSlider _csPttVol, _csPtmVol, _csToggleVol;
        private Panel _drpPttSound, _drpPtmSound, _drpToggleSound;
        private Dictionary<Panel, List<CardIcon>> _cardIconMap = new Dictionary<Panel, List<CardIcon>>();
        private Dictionary<Panel, List<CardToggle>> _cardToggleMap = new Dictionary<Panel, List<CardToggle>>();
        private Dictionary<Panel, List<CardSlider>> _cardSliderMap = new Dictionary<Panel, List<CardSlider>>();
        private bool _key1ShowOverlay = true, _key2ShowOverlay = true;
        private Timer _pollTimer;
        private int _basePttY, _basePtmY, _baseTogY, _baseDictHldY, _baseDictTogY;
        private int _pttKeyCode = 0, _pttKeyCode2 = 0, _ptmKeyCode = 0, _ptmKeyCode2 = 0, _ptToggleKeyCode = 0, _ptToggleKeyCode2 = 0; private bool _loading;
        private int _mmKeyCode = 0, _mmKeyCode2 = 0;
        private bool _mmToggleConflictActive; // true when bouncing between MM/Toggle "Press..." states
        private Label _lblMmPtt, _lblMmPtm, _lblMmToggle; // MM key1 display in each section
        private Label _lblMm2Ptt, _lblMm2Ptm, _lblMm2Toggle; // MM key2 display in each section
        private Button _btnMmRemPtt, _btnMmRemPtm, _btnMmRemToggle, _btnMm2RemPtt, _btnMm2RemPtm, _btnMm2RemToggle; // remove buttons for key1, key2
        private Button _btnMm2AddPtt, _btnMm2AddPtm, _btnMm2AddToggle; // "Add MM" for key2
        private Timer _animTimer;
        private int _animTick; // used for MM hotkey rainbow phase math
        private float _animBreathPhase = 0f;
        private int _animBreathAlpha = 90;
        private float _animSpinPhase = 0f; // 0..perimeter, advances each tick for spinning line
        private ToolTip _mmTip;
        private string _lastTipText = "";
        private CardIcon _lastHoveredCardIcon = null;
        public bool IsCapturingKey { get { return _audio != null && _audio.IsCapturing; } }
        private SlickSlider _trkMicVol, _trkSpkVol;
        private SlickSlider _micCurVolSlider, _spkCurVolSlider;
        private PaintedLabel _lblMicCurVolPct, _lblSpkCurVolPct;
        private CardIcon _micMuteIcon, _spkMuteIcon;
        private CardIcon _micLockIcon, _spkLockIcon, _duckLockIcon, _pttDuckLockIcon;
        private DateTime _lastLockClick = DateTime.MinValue;
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
        static readonly string[] NAV = { "🎙️ Input", "🔊 Output", "🖥️ Display", "📝 Dictation", "💤 AFK Protection", "🛠️ General" };
        const int SB_W = 155;

        private AudioSettings _audio; // Unified state controller
        private PushToTalk _pushToTalk; // For voice activity peak monitor

        public OptionsForm(Settings settings, AudioSettings audio, PushToTalk ptt = null) {
            _settings = settings;
            _audio = audio;
            _pushToTalk = ptt;
            Text = AppVersion.FullName + " \u2014 Options";
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BG; ForeColor = TXT;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f);
            ClientSize = Dpi.Size(840, 880);
            MinimumSize = Size; // Lock minimum to current layout size (includes title bar + borders)
            _defaultSize = Size;
            DoubleBuffered = true;
            try { Icon = Mascot.CreateIcon(); } catch { }
            DarkTheme.DarkTitleBar(Handle);
            // Always open dead-center of the screen
            StartPosition = FormStartPosition.CenterScreen;

            // Initialize Dictation keys from settings
            _dictKeyCode = _settings.DictationKey;
            _dictKeyCode2 = _settings.DictationKey2;
            _dictToggleKeyCode = _settings.DictationToggleKey;
            _dictToggleKeyCode2 = _settings.DictationToggleKey2;

            _sidebar = new Panel { Dock = DockStyle.Left, Width = Dpi.S(SB_W), BackColor = SB_BG };
            var sep = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = BDR };
            _contentPanel = new BufferedPanel { Dock = DockStyle.Fill, BackColor = BG, Padding = Dpi.Pad(20, 16, 20, 0) };
            _contentPanel.Paint += (s, e) => {
                // Always render stars to full form height (not just content panel height)
                // so hiding the footer in star-show mode doesn't cause a jarring re-stretch.
                int starH = ClientSize.Height - _contentPanel.Top;
                if (starH < _contentPanel.Height) starH = _contentPanel.Height;
                if (_stars != null) _stars.Paint(e.Graphics, _contentPanel.Width, starH);
            };

            BuildSidebar(); BuildPanes(); BuildFooter();
            Controls.Add(_contentPanel); Controls.Add(sep); Controls.Add(_sidebar);
            if (_footer != null) _footer.BringToFront(); // Visual order
            _footer.SendToBack(); // DOCK order: evaluate first, prevent overlap
            _sidebar.SendToBack(); // Sidebar evaluates second
            // Global click handler — any click outside a NUD steals focus, killing the cursor
            Application.AddMessageFilter(new NudDefocusFilter(this));
        
            LoadSettings(); SwitchPane(0);
            _pollTimer = new Timer { Interval = 1000 };
            _pollTimer.Tick += (s, e) => UpdateCurrent();
            _pollTimer.Start();
            // Twinkle timer — slowly animates card stars (now 16ms = 60fps for maximum smoothness)
            _twinkleTimer = new Timer { Interval = 16 };
            _twinkleTimer.Tick += (s, e) => {
                if (_isResizing) return;
                bool isLarge = (Width * Height) > 1200000; // ~1100x1100 — freezes at maximize, not normal size
                if (!isLarge) {
                    InvalidateCardsDeep(); // Full invalidation only on twinkle change
                }
                // Hotkey test detection — flash only the specific mode when user presses its assigned hotkey
                if (!IsCapturingKey) {
                    bool pttDown = false, ptmDown = false, toggleDown = false;
                    bool pttEnabled = _tglPtt != null && _tglPtt.Checked;
                    bool ptmEnabled = _tglPtm != null && _tglPtm.Checked;
                    bool toggleEnabled = _tglPtToggle != null && _tglPtToggle.Checked;
                    
                    if (pttEnabled && _pttKeyCode > 0) pttDown = IsKeyHeld(_pttKeyCode);
                    if (pttEnabled && !pttDown && _pttKeyCode2 > 0) pttDown = IsKeyHeld(_pttKeyCode2);
                    
                    if (ptmEnabled && _ptmKeyCode > 0) ptmDown = IsKeyHeld(_ptmKeyCode);
                    if (ptmEnabled && !ptmDown && _ptmKeyCode2 > 0) ptmDown = IsKeyHeld(_ptmKeyCode2);
                    
                    if (toggleEnabled && _ptToggleKeyCode > 0) toggleDown = IsKeyHeld(_ptToggleKeyCode);
                    if (toggleEnabled && !toggleDown && _ptToggleKeyCode2 > 0) toggleDown = IsKeyHeld(_ptToggleKeyCode2);
                    
                    bool mmDown = toggleEnabled && ((_mmKeyCode > 0 && IsKeyHeld(_mmKeyCode)) || (_mmKeyCode2 > 0 && IsKeyHeld(_mmKeyCode2)));
                    
                    bool dictPushEnabled = _tglDictPushToDict != null && _tglDictPushToDict.Checked;
                    bool dictPushDown = dictPushEnabled && ((_dictKeyCode > 0 && IsKeyHeld(_dictKeyCode)) || (_dictKeyCode2 > 0 && IsKeyHeld(_dictKeyCode2)));
                    
                    bool dictTogEnabled = _tglDictToggle != null && _tglDictToggle.Checked;
                    bool dictTogDown  = dictTogEnabled && ((_dictToggleKeyCode > 0 && IsKeyHeld(_dictToggleKeyCode)) || (_dictToggleKeyCode2 > 0 && IsKeyHeld(_dictToggleKeyCode2)));
                    
                    bool anyDown = pttDown || ptmDown || toggleDown || mmDown || dictPushDown || dictTogDown;
                    // MM key exclusion: if MM is held and shares the same VK as toggle/ptt/ptm,
                    // suppress the other handler to prevent phantom PTT/PTM/Toggle animations.
                    if (mmDown) {
                        if ((_mmKeyCode > 0 && (_mmKeyCode == _pttKeyCode || _mmKeyCode == _pttKeyCode2)) ||
                            (_mmKeyCode2 > 0 && (_mmKeyCode2 == _pttKeyCode || _mmKeyCode2 == _pttKeyCode2)))
                            pttDown = false;
                        if ((_mmKeyCode > 0 && (_mmKeyCode == _ptmKeyCode || _mmKeyCode == _ptmKeyCode2)) ||
                            (_mmKeyCode2 > 0 && (_mmKeyCode2 == _ptmKeyCode || _mmKeyCode2 == _ptmKeyCode2)))
                            ptmDown = false;
                        if ((_mmKeyCode > 0 && (_mmKeyCode == _ptToggleKeyCode || _mmKeyCode == _ptToggleKeyCode2)) ||
                            (_mmKeyCode2 > 0 && (_mmKeyCode2 == _ptToggleKeyCode || _mmKeyCode2 == _ptToggleKeyCode2)))
                            toggleDown = false;
                    }
                    // Continuous held-key highlight — stays lit while key is held
                    bool[] downs = { pttDown, ptmDown, toggleDown };
                    bool changed = false;
                    for (int i = 0; i < 3; i++) {
                        if (downs[i] != _optModeActive[i]) { _optModeActive[i] = downs[i]; changed = true; }
                    }
                    // MM key held: flash the MM labels purple
                    if (mmDown != _mmKeyHeld) {
                        _mmKeyHeld = mmDown;
                        Color mmBg = mmDown ? Color.FromArgb(40, 160, 90, 210) : INPUT_BG;
                        Color mmFg = mmDown ? Color.FromArgb(220, 180, 255) : Color.FromArgb(160, 90, 210);
                        foreach (var ml in new Label[] { _lblMmPtt, _lblMm2Ptt, _lblMmPtm, _lblMm2Ptm, _lblMmToggle, _lblMm2Toggle })
                            if (ml != null && ml.Visible) { ml.BackColor = mmBg; ml.ForeColor = mmFg; }
                        changed = true;
                    }
                    // MM key: continuously override effective segment while held (runs AFTER regular _optModeActive loop)
                    if (mmDown && _pushToTalk != null && _tglPtToggle != null && _tglPtToggle.Checked) {
                        bool micOpen = _pushToTalk.ToggleMicOpen;
                        int mmEffective = micOpen ? 1 : 0; // 1=PTM when open, 0=PTT when closed
                        if (!_optModeActive[mmEffective]) { _optModeActive[mmEffective] = true; changed = true; }
                        // Ensure the OTHER segment is off
                        int other = micOpen ? 0 : 1;
                        if (_optModeActive[other]) { _optModeActive[other] = false; changed = true; }
                    } else if (!mmDown && _mmKeyHeld == false) {
                        // MM just released — ensure both cleared (they should already be false from the regular loop)
                    }
                    if (changed && _pttCard != null) _pttCard.Invalidate();
                    // Dictation key highlight — lights up hotkey labels while held
                    if (dictPushDown != _dictPushKeyHeld) {
                        _dictPushKeyHeld = dictPushDown;
                        Color dp_bg = dictPushDown ? Color.FromArgb(35, 90, 160, 255) : INPUT_BG;
                        Color dp_fg = dictPushDown ? Color.FromArgb(140, 200, 255) : ACC;
                        if (_lblDictKey  != null && _lblDictKey.Visible)  { _lblDictKey.BackColor  = dp_bg; _lblDictKey.ForeColor  = dp_fg; }
                        if (_lblDictKey2 != null && _lblDictKey2.Visible) { _lblDictKey2.BackColor = dp_bg; _lblDictKey2.ForeColor = dp_fg; }
                        if (_dictCard != null) _dictCard.Invalidate();
                    }
                    if (dictTogDown != _dictTogKeyHeld) {
                        _dictTogKeyHeld = dictTogDown;
                        Color dt_bg = dictTogDown ? Color.FromArgb(35, 90, 160, 255) : INPUT_BG;
                        Color dt_fg = dictTogDown ? Color.FromArgb(140, 200, 255) : ACC;
                        if (_lblDictToggleKey  != null && _lblDictToggleKey.Visible)  { _lblDictToggleKey.BackColor  = dt_bg; _lblDictToggleKey.ForeColor  = dt_fg; }
                        if (_lblDictToggleKey2 != null && _lblDictToggleKey2.Visible) { _lblDictToggleKey2.BackColor = dt_bg; _lblDictToggleKey2.ForeColor = dt_fg; }
                        if (_dictCard != null) _dictCard.Invalidate();
                    }
                    // Glisten on rising edge
                    if (anyDown && !_hotkeyWasDown) {
                        bool flashedPtt = false;
                        if (pttDown && _tglPtt.Checked) { StartGlisten(_tglPtt); flashedPtt=true; }
                        else if (ptmDown && _tglPtm.Checked) { StartGlisten(_tglPtm); flashedPtt=true; }
                        else if (toggleDown && _tglPtToggle.Checked) { StartGlisten(_tglPtToggle); flashedPtt=true; }
                        else if (mmDown) { 
                            if (_pushToTalk != null && _pushToTalk.ToggleMicOpen)
                                StartGlisten(_tglPtm);  // Mic open → MM acts as PTM
                            else
                                StartGlisten(_tglPtt);   // Mic closed → MM acts as PTT
                            flashedPtt=true; 
                        }
                        if (!flashedPtt && !_tglPtt.Checked && !_tglPtm.Checked && !_tglPtToggle.Checked) {
                            if (pttDown) { StartGlisten(_tglPtt); }
                            else if (ptmDown) { StartGlisten(_tglPtm); }
                            else if (toggleDown) { StartGlisten(_tglPtToggle); }
                            else if (mmDown) { StartGlisten(_tglPtt); }
                        }
                        bool flashedDict = false;
                        if (dictPushDown && _tglDictPushToDict.Checked) { StartGlisten(_tglDictPushToDict); flashedDict=true; }
                        else if (dictTogDown && _tglDictToggle.Checked) { StartGlisten(_tglDictToggle); flashedDict=true; }
                        if (!flashedDict && !_tglDictPushToDict.Checked && !_tglDictToggle.Checked) {
                            if (dictPushDown) { StartGlisten(_tglDictPushToDict); }
                            else if (dictTogDown) { StartGlisten(_tglDictToggle); }
                        }
                    }
                    _hotkeyWasDown = anyDown;
                }
            };
            _twinkleTimer.Start();
            _animTimer = new Timer { Interval = 16 }; // 60fps for smooth breathing border and spin
            _animTimer.Tick += (s, e) => {
                _animTick++;
                _animBreathPhase += 0.015f;
                if (_animBreathPhase > (float)(Math.PI * 2)) _animBreathPhase -= (float)(Math.PI * 2);
                _animBreathAlpha = 50 + (int)(50 * ((Math.Sin(_animBreathPhase) + 1.0) / 2.0));
                _animSpinPhase += 1.3f;
                if (_animSpinPhase > 100000f) _animSpinPhase -= 100000f;
                
                // Hotkey Capture state "Press..." animation (now silky smooth at 60fps)
                if (_captureGlowLabel != null) { 
                    _captureGlowFrame++; 
                    _captureGlowLabel.Invalidate(); 
                }

                // Perf: only invalidate ASSIGNED hotkey labels on the active pane
                // Only the MM Toggle label gets rainbow animation — all others are static
                Label[] activeLabels;
                if (_activePane == 0 && _lblMmToggle != null && _mmKeyCode > 0)
                    activeLabels = new Label[] { _lblMmToggle };
                else
                    activeLabels = new Label[0];
                foreach (var lbl in activeLabels) {
                    if (lbl == null || !lbl.Visible || lbl.Parent == null || !lbl.Parent.Visible) continue;
                    lbl.Invalidate();
                }
            };
            _animTimer.Start();
            // Shooting star animation — occasional streaks across card backgrounds
            _stars = new StarBackground(() => { InvalidateCards(); });
            FormClosing += (s, e) => {
                // If user clicked X or pressed Alt+F4, just hide — don't kill the form or the app
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    _settings.Save();
                    if (WindowState == FormWindowState.Normal) { _settings.LastWindowX = Location.X; _settings.LastWindowY = Location.Y; }
                    Hide();
                    return;
                }
                // Full teardown only on app exit
                Application.RemoveMessageFilter(this); CancelAllCaptures(); if (WindowState == FormWindowState.Normal) { _settings.LastWindowX = Location.X; _settings.LastWindowY = Location.Y; } _settings.Save(); CleanupEnforcement(); if (_pollTimer != null) { _pollTimer.Stop(); _pollTimer.Dispose(); } if (_twinkleTimer != null) { _twinkleTimer.Stop(); _twinkleTimer.Dispose(); } if (_flashFadeTimer != null) { _flashFadeTimer.Stop(); _flashFadeTimer.Dispose(); } if (_stars != null) _stars.Dispose(); if (_sliderRestoreMicTimer != null) { _sliderRestoreMicTimer.Stop(); _sliderRestoreMicTimer.Dispose(); } if (_sliderRestoreSpkTimer != null) { _sliderRestoreSpkTimer.Stop(); _sliderRestoreSpkTimer.Dispose(); } if (_updateShimmerTimer != null) { _updateShimmerTimer.Stop(); _updateShimmerTimer.Dispose(); } if (_saveOrbitTimer != null) { _saveOrbitTimer.Stop(); _saveOrbitTimer.Dispose(); } if (_glistenTimer != null) { _glistenTimer.Stop(); _glistenTimer.Dispose(); } if (_voiceMeterTimer != null) { _voiceMeterTimer.Stop(); _voiceMeterTimer.Dispose(); } if (_animTimer != null) { _animTimer.Stop(); _animTimer.Dispose(); } if (_eqAnimTimer != null) { _eqAnimTimer.Stop(); _eqAnimTimer.Dispose(); } if (_mmTip != null) _mmTip.Dispose(); if (_pushToTalk != null) _pushToTalk.StopPeakMonitor();
            };
        }

        public bool PreFilterMessage(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_RBUTTONDOWN = 0x0204;
            
            // If we are capturing a hotkey and the user clicks the mouse anywhere on the form,
            // treat it as an abort/cancel just like pressing Escape.
            if ((m.Msg == WM_LBUTTONDOWN || m.Msg == WM_RBUTTONDOWN) && IsCapturingKey)
            {
                CancelAllCaptures();
                return true; // Swallow the click so it doesn't trigger anything else
            }
            return false;
        }

        private Size _defaultSize;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
        private bool _hotkeyWasDown;
        private bool _mmKeyHeld;
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

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
        }



        void InvalidateCards() {
            try {
                _contentPanel.Invalidate(false); // background stars + shooting stars
                // Also invalidate active card so shooting stars render through glass at 30fps
                // (matches WelcomeForm where p.Invalidate() cascades to card naturally)
                for (int i = 0; i < _panes.Length; i++) {
                    if (_panes[i] != null && _panes[i].Visible) {
                        _panes[i].Invalidate(false);
                        foreach (Control c in _panes[i].Controls) {
                            if (c is BufferedPanel) c.Invalidate(false);
                        }
                        break;
                    }
                }

            } catch { }
        }

        // Full invalidation — used by twinkle timer when stars change
        // Only invalidates the active card panel — CardToggle/CardSlider/CardIcon/PaintedLabel
        // are all drawn in the card's Paint handler. Real child controls (buttons, labels, NUDs)
        // have opaque backgrounds and don't need star bg updates every tick.
        void InvalidateCardsDeep() {
            try {
                _contentPanel.Invalidate(false);
                for (int i = 0; i < _panes.Length; i++) {
                    if (_panes[i] != null && _panes[i].Visible) {
                        _panes[i].Invalidate(false);
                        foreach (Control c in _panes[i].Controls) {
                            if (c is BufferedPanel) c.Invalidate(false); // card panels only
                        }
                        break;
                    }
                }

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
                bool isSub = false; // No more sub-items
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
            _sidebar.Controls.Add(navBox);

            var foot = new BufferedPanel { Dock = DockStyle.Bottom, Height = Dpi.S(136), BackColor = SB_BG };
            foot.Paint += (s, e) => {
                var g = e.Graphics; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int lineY = Dpi.S(86);
                int msz = Dpi.S(72);
                Mascot.DrawMascot(g, foot.Width - msz - Dpi.S(24), lineY - msz + Dpi.S(2), msz);
                using (var p = new Pen(Color.FromArgb(20, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B))) g.DrawLine(p, Dpi.S(14), lineY, foot.Width - Dpi.S(14), lineY);
                using (var f = new Font("Segoe UI", 7f)) using (var b = new SolidBrush(TXT4))
                    g.DrawString("by Andrew Ganter", f, b, Dpi.S(14), lineY + Dpi.S(12));
                using (var f = new Font("Segoe UI", 7f, FontStyle.Italic)) using (var b = new SolidBrush(Color.FromArgb(50, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B)))
                    g.DrawString("Your privacy, your rules", f, b, Dpi.S(14), lineY + Dpi.S(28));
            };
            foot.MouseDown += (s, e) => {
                int dividerY = Dpi.S(110);
                if (e.Button == MouseButtons.Right) { ToggleStarShow(); return; }
                if (e.Y < Dpi.S(86)) return; // clicking cat ignores
                if (e.Y < dividerY) {
                    if (_stars.Shooting != null) for (int i = 0; i < 5; i++) _stars.Shooting.ForceLaunchMeteor();
                } else {
                    if (_stars.Celestial != null) for (int i = 0; i < 5; i++) _stars.Celestial.ForceLaunch();
                }
            };
            _sidebar.Controls.Add(foot);

            // === 3. HEADER (Dock.Top) — added LAST so it docks FIRST ===
            var hdr = new BufferedPanel { Dock = DockStyle.Top, Height = Dpi.S(58), BackColor = SB_BG };
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
            for (int i = 0; i < 6; i++) {
                bool a = i == idx;
                Color lblCol = a ? ACC : TXT3;
                _navLabels[i].ForeColor = lblCol;
                _navLabels[i].Font = new Font("Segoe UI", 9f, a ? FontStyle.Bold : FontStyle.Regular);
                _navAccents[i].BackColor = a ? ACC : SB_BG;
                _navPanels[i].BackColor = a ? HOVER : SB_BG;
                _panes[i].Visible = a;
            }
            // Always run peak monitor when Options are open
            if (_pushToTalk != null) {
                if (idx == 0 || idx == 2) _pushToTalk.StartPeakMonitor();
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
                // Hide all panes and footer
                for (int i = 0; i < 6; i++) _panes[i].Visible = false;
                if (_footer != null) _footer.Visible = false;
                // Launch a burst of meteors for the show
                if (_stars.Shooting != null) for (int i = 0; i < 5; i++) _stars.Shooting.ForceLaunchMeteor();
                if (_stars.Celestial != null) _stars.Celestial.ForceLaunch();
            } else {
                // Restore active pane and footer
                _panes[_activePane].Visible = true;
                if (_footer != null) _footer.Visible = true;
            }
            _contentPanel.Invalidate();
        }

        /// <summary>Randomizes the star background theme to a new random variation.</summary>
        public void RandomizeBackdrop() { if (_stars != null) { _stars.SetRandomTheme(); InvalidateCardsDeep(); } }

        public void NavigateToPane(int idx) { if (idx >= 0 && idx < 6) SwitchPane(idx); }
        public void SetDisplayFilterType(int filterType) {
            _settings.DisplayFilterType = filterType;
            RefreshDisplayUI();
        }
        public void SetDisplayEnabled(bool enabled)  { _settings.DisplayEnabled  = enabled;  RefreshDisplayUI(); }

        /// <summary>
        /// Re-reads all Display settings from _settings and syncs every UI control on the Display pane.
        /// Safe to call from any thread via BeginInvoke, or directly on the UI thread.
        /// </summary>
        public void RefreshDisplayUI()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)RefreshDisplayUI); return; }
            _loading = true;
            try {
                if (_tglDisplay        != null) _tglDisplay.Checked        = _settings.DisplayEnabled;
                if (_tglBlueLightFilter != null) _tglBlueLightFilter.Checked = (_settings.DisplayTempK <= 3500);
                for (int j = 0; j < 6; j++)
                    if (_tglColorFilter[j] != null) _tglColorFilter[j].Checked = (_settings.DisplayFilterType == j);
                if (_trkIntensity  != null) { _trkIntensity.Value  = _settings.DisplayColorIntensity; }
                if (_trkColorBoost != null) { _trkColorBoost.Value = _settings.DisplayColorBoost; }
                if (_lblIntensity  != null) _lblIntensity.Text  = _settings.DisplayColorIntensity + "%";
                if (_lblColorBoost != null) _lblColorBoost.Text = _settings.DisplayColorBoost + "%";
                if (_trkTempK  != null) _trkTempK.Value  = Math.Max(12, Math.Min(65, _settings.DisplayTempK / 100));
                if (_trkBright != null) _trkBright.Value = Math.Max(20, Math.Min(100, _settings.DisplayBrightness));
                if (_lblTempK  != null) _lblTempK.Text   = _settings.DisplayTempK + "K";
                if (_lblBright != null) _lblBright.Text  = _settings.DisplayBrightness + "%";
                // Refresh preset button highlight
                if (_displayCard != null) {
                    // Preset buttons are owner-drawn (Text=""), invalidate them all to repaint name+hotkey
                    int presetBtnH = Dpi.S(42);
                    foreach (Control c in _displayCard.Controls) {
                        var b = c as Button;
                        if (b != null && b.Text == "" && b.Height == presetBtnH) {
                            b.BackColor = Color.FromArgb(ACC.R/8, ACC.G/8, ACC.B/8); // will be corrected in Paint
                            b.Invalidate();
                        }
                    }
                    _displayCard.Invalidate();
                }
            } finally { _loading = false; }
        }

        void BuildPanes() {
            _panes = new Panel[6];
            for (int i = 0; i < 6; i++) {
                var p = new BufferedPanel { Dock = DockStyle.Fill, Visible = false, BackColor = BG };
                p.Paint += (s, e) => { int sh = ClientSize.Height - _contentPanel.Top; if (sh < _contentPanel.Height) sh = _contentPanel.Height; if (_stars != null) _stars.Paint(e.Graphics, _contentPanel.Width, sh, p.Left, p.Top); };
                _panes[i] = p;
                _contentPanel.Controls.Add(p);
            }
            // Display pane has a tall card — enable scroll so Grayscale/Invert/Presets are reachable
            _panes[2].AutoScroll = true;
            
            // Pane 0: PTT + Voice Activity + Mic Lock
            // Controls added last with DockStyle.Top become the TOPMOST visually.
            // But we created them Top-to-Bottom. We must flip the order via SendToBack.
            BuildPttPane(_panes[0]);
            BuildMicLockPane(_panes[0]);
            
            // Force Z-Order: The last control sent to back claims the absolute top edge in DockStyle.Top
            if (_micLockCard != null) _micLockCard.SendToBack();
            if (_pttCard != null)    _pttCard.SendToBack();

            // Pane 1: Speaker Lock + Apps + EQ
            BuildSpkLockPane(_panes[1]);
            BuildAppsPane(_panes[1]);
            BuildEqPane(_panes[1]);

            // Force Z-Order for Pane 1
            if (_volCard != null) _volCard.SendToBack();
            if (_eqCard != null) _eqCard.SendToBack();

            // Pane 2: Display (color temperature + brightness)
            BuildDisplayPane(_panes[2]);
            if (_displayCard != null) _displayCard.SendToBack();

            // Pane 3: Dictation
            BuildDictPane(_panes[3]);
            if (_dictCard != null)   _dictCard.SendToBack();

            // Pane 4: AFK Protection
            BuildAfkPane(_panes[4]);

            // Pane 5: General & Legal merged (Legal moved to footer)
            BuildGeneralPane(_panes[5]);
            if (_generalCard != null) _generalCard.SendToBack();

            // Apply sound restriction if saved
            ApplySoundRestriction();
            // Restore last active pane
            int lastPane = _settings.LastActivePane;
            if (lastPane >= 0 && lastPane < 6) SwitchPane(lastPane); else SwitchPane(0);
        }



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

        BufferedPanel MakeCard(int paneIdx, string title, string sub = null, string badge = null) {
            var c = new BufferedPanel { Dock = DockStyle.Top, BackColor = Color.Transparent };
            c.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int cardTop = Dpi.S(sub != null ? 46 : 38);
                int cardBottom = c.Height - 1;
                if (_cardBottomLimit.ContainsKey(c) && _cardBottomLimit[c] > 0)
                    cardBottom = Math.Min(cardBottom, _cardBottomLimit[c]);
                int rad = Dpi.S(6);

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

                // 7) All painted labels — drawn directly on glass
                if (_cardLabels.ContainsKey(c)) {
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
                }

                // PTT/Speaker warning text — drawn directly in card paint handler
                if (c == _micLockCard) {
                    if (_pttWarningY > 0) {
                        using (var warnFont = new Font("Segoe UI", 7.5f))
                        using (var warnBrush = new SolidBrush(Color.FromArgb(220, 180, 80))) {
                            g.DrawString(_pttWarningText != null && _pttWarningText.Length > 0 ? _pttWarningText : "\u26A0  No active mic protections.", warnFont, warnBrush, Dpi.S(64), _pttWarningY);
                        }
                    }
                }
                if (c == _volCard) {
                    if (_spkWarningY > 0) {
                        using (var warnFont = new Font("Segoe UI", 7.5f))
                        using (var warnBrush = new SolidBrush(Color.FromArgb(220, 180, 80))) {
                            g.DrawString(_spkWarningText != null && _spkWarningText.Length > 0 ? _spkWarningText : "\u26A0  No active speaker protections.", warnFont, warnBrush, Dpi.S(64), _spkWarningY);
                        }
                    }
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
                if (c == _dictCard) {
                    if (_dictPushKeyHeld && _tglDictPushToDict != null) {
                        int sTop = _tglDictPushToDict.Top - Dpi.S(4);
                        int sBot = _tglDictPushToDict.Top + Dpi.S(38);
                        int sLeft = Dpi.S(10), sRight = c.Width - Dpi.S(10);
                        DarkTheme.PaintBreathingGlow(g, new Rectangle(sLeft, sTop, sRight - sLeft, sBot - sTop), ACC, Dpi.S(6));
                    }
                    if (_dictTogKeyHeld && _tglDictToggle != null) {
                        int sTop = _tglDictToggle.Top - Dpi.S(4);
                        int sBot = _tglDictToggle.Top + Dpi.S(38);
                        int sLeft = Dpi.S(10), sRight = c.Width - Dpi.S(10);
                        DarkTheme.PaintBreathingGlow(g, new Rectangle(sLeft, sTop, sRight - sLeft, sBot - sTop), ACC, Dpi.S(6));
                    }
                }


                // Card icons — eye/speaker painted directly, zero compositing
                List<CardIcon> icons;
                if (_cardIconMap.TryGetValue(c, out icons)) {
                    // Defensive enforce: dictation card icons must reflect their toggle state on EVERY paint
                    if (c == _dictCard) {
                        bool pthLive = _tglDictPushToDict != null && _tglDictPushToDict.Checked;
                        bool pttLive = _tglDictToggle != null && _tglDictToggle.Checked;
                        if (_dictPthOvr != null) _dictPthOvr.Dimmed = !pthLive;
                        if (_duckDictPth != null) _duckDictPth.Dimmed = !pthLive;
                        if (_dictPthSnd != null) _dictPthSnd.Dimmed = !pthLive;
                        if (_dictPttOvr != null) _dictPttOvr.Dimmed = !pttLive;
                        if (_duckDictPtt != null) _duckDictPtt.Dimmed = !pttLive;
                        if (_dictPttSnd != null) _dictPttSnd.Dimmed = !pttLive;
                    }
                    // Defensive enforce: PTT card icons — gate sound dropdown/slider on sound checked state
                    if (c == _pttCard) {
                        bool hasMmKey = _mmKeyCode > 0 || _mmKeyCode2 > 0;
                        bool pttActive = (_tglPtt != null && _tglPtt.Checked) || hasMmKey;
                        bool ptmActive = (_tglPtm != null && _tglPtm.Checked) || hasMmKey;
                        bool togActive = _tglPtToggle != null && _tglPtToggle.Checked;
                        // Dim icons when toggle is off
                        if (_chkKey1Overlay != null) _chkKey1Overlay.Dimmed = !pttActive;
                        if (_duckPtt != null) _duckPtt.Dimmed = !pttActive;
                        if (_keyPtt != null) _keyPtt.Dimmed = !pttActive;
                        if (_chkPttSound != null) _chkPttSound.Dimmed = !pttActive;
                        if (_chkPtmOverlay != null) _chkPtmOverlay.Dimmed = !ptmActive;
                        if (_duckPtm != null) _duckPtm.Dimmed = !ptmActive;
                        if (_keyPtm != null) _keyPtm.Dimmed = !ptmActive;
                        if (_chkPtmSound != null) _chkPtmSound.Dimmed = !ptmActive;
                        if (_chkToggleOverlay != null) _chkToggleOverlay.Dimmed = !togActive;
                        if (_duckToggle != null) _duckToggle.Dimmed = !togActive;
                        if (_keyToggle != null) _keyToggle.Dimmed = !togActive;
                        if (_chkToggleSound != null) _chkToggleSound.Dimmed = !togActive;
                        // Gate dropdown/slider on BOTH toggle-active AND sound-checked
                        bool pttSndOn = pttActive && _chkPttSound != null && _chkPttSound.Checked;
                        bool ptmSndOn = ptmActive && _chkPtmSound != null && _chkPtmSound.Checked;
                        bool togSndOn = togActive && _chkToggleSound != null && _chkToggleSound.Checked;
                        if (_drpPttSound != null) _drpPttSound.Enabled = pttSndOn;
                        if (_csPttVol != null) _csPttVol.Source.Enabled = pttSndOn;
                        if (_drpPtmSound != null) _drpPtmSound.Enabled = ptmSndOn;
                        if (_csPtmVol != null) _csPtmVol.Source.Enabled = ptmSndOn;
                        if (_drpToggleSound != null) _drpToggleSound.Enabled = togSndOn;
                        if (_csToggleVol != null) _csToggleVol.Source.Enabled = togSndOn;
                    }
                    foreach (var icon in icons) if (icon.Visible) icon.Paint(g, ACC);
                }
                // Card toggles — painted directly, real ToggleSwitch hidden
                List<CardToggle> ctgls;
                if (_cardToggleMap.TryGetValue(c, out ctgls)) {
                    foreach (var ct in ctgls) if (ct.Visible) ct.Paint(g, ACC);
                }
                // Card sliders — painted directly, real SlickSlider hidden
                List<CardSlider> cslds;
                if (_cardSliderMap.TryGetValue(c, out cslds)) {
                    // Defensive enforce: dictation card dropdowns/sliders must reflect toggle state
                    if (c == _dictCard) {
                        bool pthLive = _tglDictPushToDict != null && _tglDictPushToDict.Checked;
                        bool pttLive = _tglDictToggle != null && _tglDictToggle.Checked;
                        bool pthSndOn = _dictPthSnd != null && _dictPthSnd.Checked;
                        bool pttSndOn = _dictPttSnd != null && _dictPttSnd.Checked;
                        if (_dictPthDrop != null) { _dictPthDrop.Enabled = pthLive && pthSndOn; _dictPthDrop.Invalidate(); }
                        if (_dictPthVol != null) _dictPthVol.Source.Enabled = pthLive && pthSndOn;
                        if (_dictPttDrop != null) { _dictPttDrop.Enabled = pttLive && pttSndOn; _dictPttDrop.Invalidate(); }
                        if (_dictPttVol != null) _dictPttVol.Source.Enabled = pttLive && pttSndOn;
                        // Ducking section enforcement handled by RefreshDuckingSection
                        bool anyDictDuck = (_duckDictPth != null && _duckDictPth.Checked && _tglDictPushToDict != null && _tglDictPushToDict.Checked) || (_duckDictPtt != null && _duckDictPtt.Checked && _tglDictToggle != null && _tglDictToggle.Checked);
                        bool anyPttDuck = AnyPttDuckActive();
                        bool anyDuck = anyDictDuck || anyPttDuck;
                        // Paint Volume: show slider value (responsive during drag)
                        if (_trkSysVol != null && _lblSysVol != null) {
                            int svNow = _trkSysVol.Value;
                            float greenX0 = Dpi.S(90);
                            using (var fv = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                            using (var bv = new SolidBrush(Color.FromArgb(80, 200, 120)))
                                g.DrawString(svNow + "%", fv, bv, greenX0, _lblSysVol.Y);
                        }
                                                // Paint effective volume for dictation slider
                        if (_trkDuckVol != null && _csDuckVol != null && _lblDuckTo != null) {
                            float sysVol = -1;
                            try { sysVol = Audio.GetSpeakerVolume(); } catch { }
                            if (sysVol >= 0) {
                                // Fixed x positions so both rows align
                                float greenX = Dpi.S(90);
                                float ratioX = _csDuckVol.PixelX + _csDuckVol.PixelW + Dpi.S(8);
                                // Dictation row
                                int effective = (int)(sysVol * _trkDuckVol.Value / 100f);
                                using (var f2 = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                                using (var b2 = new SolidBrush(anyDictDuck ? Color.FromArgb(80, 200, 120) : Color.FromArgb(40, 40, 40)))
                                    g.DrawString(effective + "%", f2, b2, greenX, _lblDuckTo.Y);
                                string ratioTxt = _trkDuckVol.Value + "% / " + (int)sysVol + "%";
                                using (var f3 = new Font("Segoe UI", 7.5f))
                                using (var b3 = new SolidBrush(anyDictDuck ? Color.FromArgb(90, 90, 90) : Color.FromArgb(40, 40, 40)))
                                    g.DrawString(ratioTxt, f3, b3, ratioX, _lblDuckTo.Y + Dpi.S(1));
                                // Microphone row
                                if (_trkPttDuckVol != null && _csPttDuckVol != null && _lblPttDuckTo != null) {
                                    int ptdEff = (int)(sysVol * _trkPttDuckVol.Value / 100f);
                                    using (var f2 = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                                    using (var b2 = new SolidBrush(anyPttDuck ? Color.FromArgb(80, 200, 120) : Color.FromArgb(40, 40, 40)))
                                        g.DrawString(ptdEff + "%", f2, b2, greenX, _lblPttDuckTo.Y);
                                    string ptRatioTxt = _trkPttDuckVol.Value + "% / " + (int)sysVol + "%";
                                    using (var f3 = new Font("Segoe UI", 7.5f))
                                    using (var b3 = new SolidBrush(anyPttDuck ? Color.FromArgb(90, 90, 90) : Color.FromArgb(40, 40, 40)))
                                        g.DrawString(ptRatioTxt, f3, b3, ratioX, _lblPttDuckTo.Y + Dpi.S(1));
                                }
                            }
                        }
                    }
                    foreach (var cs in cslds) cs.Paint(g, ACC);
                }

                // Separator line drawing removed.
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
                        if (!ct.Dimmed && ct.HitTest(e2.Location)) { ct.Click(); return; }
                    }
                }
                List<CardIcon> icons2;
                if (_cardIconMap.TryGetValue(c, out icons2)) {
                    foreach (var icon in icons2) {
                        if (icon.Visible && icon.HitTest(e2.Location)) { icon.Toggle(); break; }
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
                CardIcon newHover = null;
                List<CardIcon> icons2;
                if (_cardIconMap.TryGetValue(c, out icons2)) {
                    foreach (var icon in icons2) {
                        bool wasHover = icon.Hover;
                        icon.Hover = icon.Visible && icon.HitTest(e2.Location);
                        if (icon.Hover) newHover = icon;
                        if (wasHover != icon.Hover) c.Invalidate();
                    }
                }
                if (newHover != _lastHoveredCardIcon) {
                    _lastHoveredCardIcon = newHover;
                    if (_mmTip != null) {
                        if (_lastHoveredCardIcon != null) {
                            string tip = "";
                            if (_lastHoveredCardIcon.IsDuck) tip = "Audio Ducking";
                            else if (_lastHoveredCardIcon.IsKey) tip = "Suppress Hotkey";
                            else if (_lastHoveredCardIcon.IsEye) tip = "Show Overlay";
                            else if (_lastHoveredCardIcon.IsLock) tip = "Lock Volume";
                            else tip = "Play Sound";
                            _lastTipText = tip;
                            _mmTip.Show(tip, c, _lastHoveredCardIcon.PixelX - Dpi.S(20), _lastHoveredCardIcon.PixelY + _lastHoveredCardIcon.H + Dpi.S(28), 4000);
                        } else {
                            _mmTip.Hide(c);
                        }
                    }
                }
                List<CardToggle> ctgls2;
                if (_cardToggleMap.TryGetValue(c, out ctgls2)) {
                    foreach (var ct in ctgls2) {
                        bool wasH = ct.Hover;
                        ct.Hover = ct.HitTest(e2.Location);
                        if (wasH != ct.Hover) c.Invalidate();
                    }
                }
                if (_cardSliderMap.TryGetValue(c, out cslds2)) {
                    foreach (var cs in cslds2) {
                        bool wasH = cs.Hover;
                        cs.Hover = cs.ThumbHitTest(e2.Location) || cs.Dragging;
                        if (wasH != cs.Hover) c.Invalidate();
                    }
                }
                // No cursor change — hover glow is the only feedback
            };
            c.MouseLeave += (s, e2) => {
                if (_lastHoveredCardIcon != null && _cardIconMap.ContainsKey(c) && _cardIconMap[c].Contains(_lastHoveredCardIcon)) {
                    _lastHoveredCardIcon = null;
                    if (_mmTip != null) _mmTip.Hide(c);
                }
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

        // Tgl now only creates ToggleSwitch — text is painted
        ToggleSwitch Tgl(string label, string sub, int y, Panel card, string hotkey = null) {
            var t = new ToggleSwitch { Location = Dpi.Pt(16, y) }; card.Controls.Add(t);
            t.Visible = false;
            var ct = new CardToggle { PixelX = Dpi.S(16), PixelY = Dpi.S(y), Source = t, Card = card };
            if (!_cardToggleMap.ContainsKey(card)) _cardToggleMap[card] = new List<CardToggle>();
            _cardToggleMap[card].Add(ct);
            var titleLbl = AddText(card, label, 64, y, 9.5f, TXT);
            // Hotkey badge: always at fixed x=240 so all badges line up vertically
            if (!string.IsNullOrEmpty(hotkey))
                AddText(card, hotkey, 240, y + 2, 7.5f, ACC);
            if (sub != null) AddText(card, sub, 64, y + 19, 7.5f, TXT3);
            return t;
        }
        NumericUpDown Nud(int min, int max, int val, int x, int y, int w) { var n = new PaddedNumericUpDown { Minimum=min,Maximum=max,Value=val, Location=Dpi.Pt(x,y), Size=Dpi.Size(w,24), BackColor=INPUT_BG, ForeColor=TXT, Font=new Font("Segoe UI",9f), BorderStyle=BorderStyle.FixedSingle, TextAlign=HorizontalAlignment.Center };
            n.Leave += (s,e) => { try { int v; if (int.TryParse(n.Text, out v)) { v = Math.Max(min, Math.Min(max, v)); n.Value = v; } } catch { n.Value = val; } };
            return n; }

        // Dynamic painted labels that update at runtime
        private PaintedLabel _plMicVol, _plSpkVol;
        private Panel _volCard; // reference for invalidation on value change
        private Panel _micLockCard; // reference for invalidation
        private Panel _pttCard; // reference for animation invalidation
        private Panel _generalCard;
        private Panel _displayCard;
        // === DICTATION ===
        private int _dictKeyCode = 0, _dictKeyCode2 = 0;           // push-to-hold keys
        private int _dictToggleKeyCode = 0, _dictToggleKeyCode2 = 0; // push-to-toggle keys
        private Label _lblDictKey, _lblDictKey2;
        private Label _lblDictToggleKey, _lblDictToggleKey2;
        private bool _dictPushKeyHeld, _dictTogKeyHeld;
        private Panel _dictCard;

        Color GetKeyTheme(Label lbl) {
            if (lbl.Parent == _dictCard) return Color.FromArgb(50, 205, 50); // Dictation green
            if (lbl == _lblMmPtt || lbl == _lblMm2Ptt || lbl == _lblMmPtm || lbl == _lblMm2Ptm || lbl == _lblMmToggle || lbl == _lblMm2Toggle) return Color.FromArgb(160, 90, 210);
            return ACC; // Default PTT/PTM/Toggle Blue
        }

        void AttachKeyStyling(Label lbl) {
            lbl.EnabledChanged += (s,e) => { lbl.ForeColor = lbl.Enabled ? GetKeyTheme(lbl) : Color.FromArgb(80,80,80); lbl.Invalidate(); };
            lbl.Paint += PaintHotkeyLabel;
            lbl.MouseEnter += (s,e) => {
                if (!lbl.Enabled || IsCapturingKey) return;
                var th = GetKeyTheme(lbl);
                string t = lbl.Tag as string;
                bool isAdd = (t == "Add Key" || t == "Add MM Key");
                lbl.BackColor = isAdd ? Color.FromArgb(th.R/5,th.G/5,th.B/5) : Color.FromArgb(28,28,28);
            };
            lbl.MouseLeave += (s,e) => {
                if (!lbl.Enabled || IsCapturingKey) return;
                string t = lbl.Tag as string;
                bool isAdd = (t == "Add Key" || t == "Add MM Key");
                lbl.BackColor = isAdd ? Color.FromArgb(20,20,20) : INPUT_BG;
            };
        }

        void UpdateKeyStyle(Label lbl, bool isAdd, string text) {
            if (lbl == null) return;
            lbl.Text = "";
            lbl.Tag = text;
            var th = GetKeyTheme(lbl);
            lbl.BackColor = isAdd ? Color.FromArgb(20,20,20) : INPUT_BG;
            lbl.ForeColor = lbl.Enabled ? th : Color.FromArgb(80,80,80);
            if (isAdd && !IsCapturingKey && lbl.Enabled && lbl.ClientRectangle.Contains(lbl.PointToClient(Cursor.Position))) {
                lbl.BackColor = Color.FromArgb(th.R/5,th.G/5,th.B/5);
            }
            lbl.Invalidate();
        }



        void BuildPttPane(Panel pane) {
            var card = MakeCard(0, "Input", "Control your mic when your mic is live. Enable one or more modes below.", "Alt+I");
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

            AddText(card, "These options mute all microphones system-wide \u2014 headset, camera mic, USB devices.", 16, 56, 8.5f, Color.FromArgb(100, 180, 255));

            // MODE 1: PUSH-TO-TALK
            int y = 84;
            _tglPtt = new ToggleSwitch { Location = Dpi.Pt(16, y) }; card.Controls.Add(_tglPtt);
            _tglPtt.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(16), PixelY = Dpi.S(y), Source = _tglPtt, Card = card };
              if (!_cardToggleMap.ContainsKey(card)) _cardToggleMap[card] = new List<CardToggle>(); _cardToggleMap[card].Add(ct); }
            AddText(card, "Push-to-Talk", 64, y, 10f, TXT, FontStyle.Bold);
            AddText(card, "Silent until you hold the key to open.", 64, y+20, 7.5f, TXT3);
            _tglPtt.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglPtt.Checked && (IsCapturingKey || CaptureCooldownActive())) { _loading=true; _tglPtt.Checked=false; _loading=false; return; }
                if (_tglPtt.Checked && _pttKeyCode <= 0) { _loading=true; _tglPtt.Checked=false; _loading=false; BeginCapture(CaptureTarget.PttKey1, _lblPttKey); return; }
                if (!_tglPtt.Checked) { CancelAllCaptures();_pttKeyCode=0;_pttKeyCode2=0;_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;CompactKeys();LayoutPttKeys();_audio.DisablePttMode(); try { Audio.SetMicMute(false); } catch { } }
                else {
                    // PTT ON: if Toggle is active, migrate PTT key to MM (cannot coexist)
                    if (_tglPtToggle.Checked) {
                        if (_pttKeyCode > 0 && _mmKeyCode <= 0) { _mmKeyCode = _pttKeyCode; _audio.MmKey = _pttKeyCode; }
                        _pttKeyCode=0;_pttKeyCode2=0;_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;CompactKeys();LayoutPttKeys();
                        _loading=true; _tglPtt.Checked=false; _loading=false;
                        UpdateMmKeyLabels(); _audio.Save();
                        RefreshPttHotkeyLabel(); RefreshVolumeSliders(); if (_pttCard != null) _pttCard.Invalidate();
                        return;
                    }
                    // PTT ON: if Toggle is off, force PTM off (mutually exclusive without Toggle)
                    if (!_tglPtToggle.Checked && _tglPtm.Checked) {
                        // Restore PTM duck snapshot before disabling
                        if (_ptmDuckSnapshot >= 0f) { try { Audio.SetSpeakerVolume(_ptmDuckSnapshot); } catch { } _ptmDuckSnapshot = -1f; }
                        if (_duckPtm != null && _duckPtm.Checked) { _duckPtm.Checked = false; _audio.PtmDuckEnabled = false; }
                        _loading=true; _tglPtm.Checked=false; _loading=false;
                        _ptmKeyCode=0;_ptmKeyCode2=0;_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;LayoutPtmKeys();_audio.DisablePtmMode();
                    }
                    _audio.PttEnabled = true; _audio.PttKey = _pttKeyCode; _audio.PttKey2 = _pttKeyCode2;
                    // Disable VA — mutually exclusive (must set backend too, not just visual)
                    if (_tglVoiceActivity != null && _tglVoiceActivity.Checked) { _loading=true; _tglVoiceActivity.Checked=false; _loading=false; _audio.VoiceActivityEnabled = false; }
                    if (_pttKeyCode > 0) Toast.Show("Push-to-Talk Enabled", "Silent until you hold (" + PushToTalk.GetKeyName(_pttKeyCode) + ") to open.", ToastAccent.Blue, 3500);
                }
                RefreshPttHotkeyLabel();
                RefreshVolumeSliders();
                RefreshDuckingSection();
                if (_pttCard != null) _pttCard.Invalidate();
            } };
            int hk1Y = y + 46;
            _basePttY = hk1Y;
            AddText(card, "Hotkey:", ki, hk1Y+4, 8f, TXT3);
            _lblPttKey = new Label{Text=_pttKeyCode > 0 ? PushToTalk.GetKeyName(_pttKeyCode) : "Add Key",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(80,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(ki+50,hk1Y)};
            AttachKeyStyling(_lblPttKey);
            _lblPttKey.Click += (s,e) => BeginCapture(CaptureTarget.PttKey1, _lblPttKey); card.Controls.Add(_lblPttKey);
            _chkKey1Overlay = MakeOverlayCheck(y, card, _key1ShowOverlay, (v) => { _key1ShowOverlay = v; _audio.PttKey1ShowOverlay = v; });
            _duckPtt = MakeDuckCheck(y, card, _settings.PttDuckEnabled, (v) => { _audio.PttDuckEnabled = v; RefreshDuckingSection(); ShowDuckToast(_pttKeyCode > 0 ? PushToTalk.GetKeyName(_pttKeyCode) : "PTT", v); });
            _keyPtt = MakeKeyCheck(y, card, _settings.PttSuppressEnabled, (v) => { _audio.PttSuppressEnabled = v; ShowKeyToast(_pttKeyCode > 0 ? PushToTalk.GetKeyName(_pttKeyCode) : "PTT", v); });
            _chkPttSound = MakeSoundCheck(y, card, _settings.PttSoundFeedback, (v) => {
                _audio.PttSoundFeedback = v;
                if (_drpPttSound != null) _drpPttSound.Enabled = v;
                if (_csPttVol != null) _csPttVol.Source.Enabled = v;
                if (_pttCard != null) _pttCard.Invalidate();
            });
            _drpPttSound = MakeSoundDropdown(y, card, _settings.PttSoundType, (v) => { _audio.PttSoundType = v; }, () => _settings.PttSoundVolume);
            _csPttVol = MakeVolumeSlider(y, card, _settings.PttSoundVolume, (v) => { _audio.PttSoundVolume = v; });
            _csPttVol.Source.Enabled = _settings.PushToTalkEnabled && _settings.PttSoundFeedback;
            // Initial dim state
            if (!_settings.PushToTalkEnabled) { _chkKey1Overlay.Dimmed = true; if (_duckPtt != null) _duckPtt.Dimmed = true; if (_keyPtt != null) _keyPtt.Dimmed = true; _chkPttSound.Dimmed = true; _drpPttSound.Enabled = false; }
            else if (!_settings.PttSoundFeedback) { _drpPttSound.Enabled = false; }
            // --- HORIZONTAL KEY ROW: Key1 [x] Key2 [x] Key3 [x] [Add Key] ---
            // All keys on a single row. "Add Key" appears after the last assigned key.
            int kbW = 80; // key box width (base)
            int rmW = 18; // remove button width

            // Key1 remove button for PTT
            _btnRemoveKey = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false,Visible=false};
            _btnRemoveKey.FlatAppearance.BorderSize=0; _btnRemoveKey.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnRemoveKey.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverRm1 = false;
            _btnRemoveKey.MouseEnter += (s,e) => { _hoverRm1=true; _btnRemoveKey.Invalidate(); };
            _btnRemoveKey.MouseLeave += (s,e) => { _hoverRm1=false; _btnRemoveKey.Invalidate(); };
            _btnRemoveKey.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnRemoveKey.ClientRectangle, _hoverRm1); };
            _btnRemoveKey.Click += (s,e) => { if (_pttKeyCode2 > 0) { _pttKeyCode = _pttKeyCode2; _pttKeyCode2 = 0; } else { _pttKeyCode = 0; _loading=true; _tglPtt.Checked=false; _loading=false; } _audio.PttKey = _pttKeyCode; _audio.PttKey2 = _pttKeyCode2; if(_pttKeyCode<=0){_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;_audio.DisablePttMode();} CompactKeys(); };
            card.Controls.Add(_btnRemoveKey);

            _btnAddKey2 = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false};
            _btnAddKey2.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnAddKey2.MouseEnter += (s,e) => { _btnAddKey2.BackColor=Color.FromArgb(ACC.R/5,ACC.G/5,ACC.B/5); };
            _btnAddKey2.MouseLeave += (s,e) => { _btnAddKey2.BackColor=Color.FromArgb(20,20,20); };
            _btnAddKey2.Click += (s,e) => BeginCapture(CaptureTarget.PttKey2, _lblPttKey2); card.Controls.Add(_btnAddKey2);
            // Key 2 box (inline, right of key 1)
            _lblKey2Label = new Label{Text="",Font=new Font("Segoe UI",8f),ForeColor=TXT3,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(0,0),Visible=false}; card.Controls.Add(_lblKey2Label); // hidden, kept for compat
            _lblPttKey2 = new Label{Text=PushToTalk.GetKeyName(_pttKeyCode2),Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(kbW,26),TextAlign=ContentAlignment.MiddleCenter};
            AttachKeyStyling(_lblPttKey2);
            _lblPttKey2.Click += (s,e) => BeginCapture(CaptureTarget.PttKey2, _lblPttKey2); card.Controls.Add(_lblPttKey2);
            _chkKey2Overlay = null;
            _lblKey2Hint = new Label{Text="",Font=new Font("Segoe UI",7f),ForeColor=TXT4,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(0,0),Visible=false}; card.Controls.Add(_lblKey2Hint);
            _btnRemoveKey2 = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false,Visible=false};
            _btnRemoveKey2.FlatAppearance.BorderSize=0; _btnRemoveKey2.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnRemoveKey2.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverRm2 = false;
            _btnRemoveKey2.MouseEnter += (s,e) => { _hoverRm2=true; _btnRemoveKey2.Invalidate(); };
            _btnRemoveKey2.MouseLeave += (s,e) => { _hoverRm2=false; _btnRemoveKey2.Invalidate(); };
            _btnRemoveKey2.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnRemoveKey2.ClientRectangle, _hoverRm2); };
            _btnRemoveKey2.Click += (s,e) => { _pttKeyCode2 = 0; LayoutPttKeys(); _audio.PttKey = _pttKeyCode; _audio.PttKey2 = _pttKeyCode2; };
            card.Controls.Add(_btnRemoveKey2);
            // MM buttons — purple, right-aligned on hotkey row (same size as "Add Key")
            if (_mmTip == null) { 
                _mmTip = new ToolTip { InitialDelay = 0, ReshowDelay = 0, AutoPopDelay = 8000, UseAnimation = false, UseFading = false, OwnerDraw = true }; 
                _mmTip.Draw += (st, ev) => {
                    ev.DrawBackground(); ev.DrawBorder();
                    using (var bg = new SolidBrush(Color.FromArgb(30,30,30))) ev.Graphics.FillRectangle(bg, ev.Bounds);
                    using (var p = new Pen(Color.FromArgb(70,70,70))) ev.Graphics.DrawRectangle(p, 0, 0, ev.Bounds.Width - 1, ev.Bounds.Height - 1);
                    string txt = _mmTip.GetToolTip(ev.AssociatedControl); if (string.IsNullOrEmpty(txt)) txt = _lastTipText;
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var fg = new SolidBrush(Color.FromArgb(220,220,220))) ev.Graphics.DrawString(txt, new Font("Segoe UI", 8.5f), fg, ev.Bounds, sf);
                };
                _mmTip.Popup += (st, ev) => {
                    string txt = _mmTip.GetToolTip(ev.AssociatedControl); if (string.IsNullOrEmpty(txt)) txt = _lastTipText;
                    var sz = TextRenderer.MeasureText(txt, new Font("Segoe UI", 8.5f));
                    ev.ToolTipSize = new Size(sz.Width + Dpi.S(12), sz.Height + Dpi.S(10));
                };
            }
            Color MM_CLR = Color.FromArgb(160, 90, 210);
            int mmW = 80; // same as kbW
            _lblMmPtt = new Label{Text=_mmKeyCode>0?KeyName(_mmKeyCode):"Add MM Key",Font=new Font("Segoe UI",8f,FontStyle.Bold),ForeColor=MM_CLR,BackColor=_mmKeyCode>0?INPUT_BG:Color.FromArgb(20,20,20),Size=Dpi.Size(mmW,26),TextAlign=ContentAlignment.MiddleCenter};
            AttachKeyStyling(_lblMmPtt);
            _lblMmPtt.Click += (s,e) => BeginCapture(CaptureTarget.MmKey, _lblMmPtt); card.Controls.Add(_lblMmPtt);
            _btnMmRemPtt = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false,Visible=false,Location=new Point(-100,-100)};
            _btnMmRemPtt.FlatAppearance.BorderSize=0; _btnMmRemPtt.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnMmRemPtt.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverMmRmPtt = false;
            _btnMmRemPtt.MouseEnter += (s,e) => { _hoverMmRmPtt=true; _btnMmRemPtt.Invalidate(); };
            _btnMmRemPtt.MouseLeave += (s,e) => { _hoverMmRmPtt=false; _btnMmRemPtt.Invalidate(); };
            _btnMmRemPtt.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnMmRemPtt.ClientRectangle, _hoverMmRmPtt); };
            _btnMmRemPtt.Click += (s,e) => { if (_mmKeyCode2 > 0) { _mmKeyCode = _mmKeyCode2; _mmKeyCode2 = 0; } else { _mmKeyCode = 0; } _audio.MmKey = _mmKeyCode; _audio.MmKey2 = _mmKeyCode2; UpdateMmKeyLabels(); }; card.Controls.Add(_btnMmRemPtt);
            
            _lblMm2Ptt = new Label{Text=_mmKeyCode2>0?KeyName(_mmKeyCode2):"Add Key",Font=new Font("Segoe UI",8f,FontStyle.Bold),ForeColor=MM_CLR,BackColor=_mmKeyCode2>0?INPUT_BG:Color.FromArgb(20,20,20),Size=Dpi.Size(mmW,26),TextAlign=ContentAlignment.MiddleCenter,Visible=false};
            AttachKeyStyling(_lblMm2Ptt);
            _lblMm2Ptt.Click += (s,e) => BeginCapture(CaptureTarget.MmKey2, _lblMm2Ptt); card.Controls.Add(_lblMm2Ptt);
            bool _hoverMm2RmPtt = false;
            _btnMm2RemPtt = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false,Visible=false};
            _btnMm2RemPtt.FlatAppearance.BorderSize=0; _btnMm2RemPtt.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnMm2RemPtt.FlatAppearance.MouseDownBackColor=Color.Transparent;
            _btnMm2RemPtt.MouseEnter += (s,e) => { _hoverMm2RmPtt=true; _btnMm2RemPtt.Invalidate(); };
            _btnMm2RemPtt.MouseLeave += (s,e) => { _hoverMm2RmPtt=false; _btnMm2RemPtt.Invalidate(); };
            _btnMm2RemPtt.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnMm2RemPtt.ClientRectangle, _hoverMm2RmPtt); };
            _btnMm2RemPtt.Click += (s,e) => { _mmKeyCode2 = 0; _audio.MmKey2 = _mmKeyCode2; UpdateMmKeyLabels(); }; card.Controls.Add(_btnMm2RemPtt);
            
            _btnMm2AddPtt = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=MM_CLR,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Visible=false};
            _btnMm2AddPtt.FlatAppearance.BorderColor=Color.FromArgb(MM_CLR.R/3,MM_CLR.G/3,MM_CLR.B/3);
            _btnMm2AddPtt.MouseEnter += (s,e) => { _btnMm2AddPtt.BackColor=Color.FromArgb(MM_CLR.R/5,MM_CLR.G/5,MM_CLR.B/5); };
            _btnMm2AddPtt.MouseLeave += (s,e) => { _btnMm2AddPtt.BackColor=Color.FromArgb(20,20,20); };
            _btnMm2AddPtt.Click += (s,e) => BeginCapture(CaptureTarget.MmKey2, _lblMm2Ptt); card.Controls.Add(_btnMm2AddPtt);
            LayoutPttKeys();

            y = hk1Y + 34;
            AddLine(card, y); y += 10;

            // MODE 2: PUSH-TO-MUTE — with overlay icon
            _tglPtm = new ToggleSwitch { Location = Dpi.Pt(16, y) }; card.Controls.Add(_tglPtm);
            _tglPtm.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(16), PixelY = Dpi.S(y), Source = _tglPtm, Card = card };
              _cardToggleMap[card].Add(ct); }
            AddText(card, "Push-to-Mute", 64, y, 10f, TXT, FontStyle.Bold);
            AddText(card, "Open until you hold the key to silence.", 64, y+20, 7.5f, TXT3);
            _tglPtm.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglPtm.Checked && (IsCapturingKey || CaptureCooldownActive())) { _loading=true; _tglPtm.Checked=false; _loading=false; return; }
                if (_tglPtm.Checked && _ptmKeyCode <= 0) { _loading=true; _tglPtm.Checked=false; _loading=false; BeginCapture(CaptureTarget.PtmKey1, _lblPtmKey); return; }
                if (!_tglPtm.Checked) {
                    // PTM OFF: restore snapshot if duck was active
                    if (_ptmDuckSnapshot >= 0f) { try { Audio.SetSpeakerVolume(_ptmDuckSnapshot); } catch { } _ptmDuckSnapshot = -1f; }
                    if (_duckPtm != null && _duckPtm.Checked) { _duckPtm.Checked = false; _audio.PtmDuckEnabled = false; }
                    CancelAllCaptures();_ptmKeyCode=0;_ptmKeyCode2=0;_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;LayoutPtmKeys();_audio.DisablePtmMode(); try { Audio.SetMicMute(false); } catch { }
                }
                else {
                    // PTM ON: force duck OFF to prevent immediate volume change
                    if (_duckPtm != null && _duckPtm.Checked) { _duckPtm.Checked = false; _audio.PtmDuckEnabled = false; }
                    _ptmDuckSnapshot = -1f;
                    // PTM ON: if Toggle is off, force PTT off (mutually exclusive without Toggle)
                    if (!_tglPtToggle.Checked && _tglPtt.Checked) {
                        if (_duckPtt != null && _duckPtt.Checked) { _duckPtt.Checked = false; _audio.PttDuckEnabled = false; }
                        _loading=true; _tglPtt.Checked=false; _loading=false;
                        _pttKeyCode=0;_pttKeyCode2=0;_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;CompactKeys();LayoutPttKeys();_audio.DisablePttMode();
                    }
                    _audio.PtmEnabled = true; _audio.PtmKey = _ptmKeyCode; _audio.PtmKey2 = _ptmKeyCode2;
                    if (_tglVoiceActivity != null && _tglVoiceActivity.Checked) { _loading=true; _tglVoiceActivity.Checked=false; _loading=false; _audio.VoiceActivityEnabled = false; }
                    if (_ptmKeyCode > 0) Toast.Show("Push-to-Mute Enabled", "Open until you hold (" + PushToTalk.GetKeyName(_ptmKeyCode) + ") to silence.", ToastAccent.Blue, 3500);
                }
                RefreshPttHotkeyLabel();
                RefreshVolumeSliders();
                RefreshDuckingSection();
            } };
            int hk2Y = y + 46;
            _basePtmY = hk2Y;
            AddText(card, "Hotkey:", ki, hk2Y+4, 8f, TXT3);
            _lblPtmKey = new Label{Text=_ptmKeyCode > 0 ? PushToTalk.GetKeyName(_ptmKeyCode) : "Add Key",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(kbW,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(ki+50,hk2Y)};
            AttachKeyStyling(_lblPtmKey);
            _lblPtmKey.Click += (s,e) => BeginCapture(CaptureTarget.PtmKey1, _lblPtmKey); card.Controls.Add(_lblPtmKey);
            // Key1 remove button for PTM
            _btnPtmRemKey = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false,Visible=false};
            _btnPtmRemKey.FlatAppearance.BorderSize=0; _btnPtmRemKey.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnPtmRemKey.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverPtmRm1 = false;
            _btnPtmRemKey.MouseEnter += (s,e) => { _hoverPtmRm1=true; _btnPtmRemKey.Invalidate(); };
            _btnPtmRemKey.MouseLeave += (s,e) => { _hoverPtmRm1=false; _btnPtmRemKey.Invalidate(); };
            _btnPtmRemKey.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnPtmRemKey.ClientRectangle, _hoverPtmRm1); };
            _btnPtmRemKey.Click += (s,e) => { if (_ptmKeyCode2 > 0) { _ptmKeyCode = _ptmKeyCode2; _ptmKeyCode2 = 0; } else { _ptmKeyCode = 0; _loading=true; _tglPtm.Checked=false; _loading=false; } _audio.PtmKey = _ptmKeyCode; _audio.PtmKey2 = _ptmKeyCode2; if(_ptmKeyCode<=0){_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;_audio.DisablePtmMode();} CompactKeys(); };
            card.Controls.Add(_btnPtmRemKey);
            // PTM Add Key button for key2
            _btnPtmAddKey2 = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Location=Dpi.Pt(ki+152,hk2Y)};
            _btnPtmAddKey2.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnPtmAddKey2.Click += (s,e) => BeginCapture(CaptureTarget.PtmKey2, _lblPtmKey2);
            card.Controls.Add(_btnPtmAddKey2);
            // PTM key2 row
            int ptmK2Y = hk2Y + 30;
            _lblPtmKey2 = new Label{Text=_ptmKeyCode2>0?KeyName(_ptmKeyCode2):"",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(kbW,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(ki+50,ptmK2Y),Visible=_ptmKeyCode2>0};
            AttachKeyStyling(_lblPtmKey2);
            _lblPtmKey2.Click += (s,e) => BeginCapture(CaptureTarget.PtmKey2, _lblPtmKey2);
            card.Controls.Add(_lblPtmKey2);
            _btnPtmRemKey2 = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false,Location=Dpi.Pt(ki+150,ptmK2Y+1),Visible=_ptmKeyCode2>0};
            _btnPtmRemKey2.FlatAppearance.BorderSize=0; _btnPtmRemKey2.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnPtmRemKey2.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverPtmRm2 = false;
            _btnPtmRemKey2.MouseEnter += (s,e) => { _hoverPtmRm2=true; _btnPtmRemKey2.Invalidate(); };
            _btnPtmRemKey2.MouseLeave += (s,e) => { _hoverPtmRm2=false; _btnPtmRemKey2.Invalidate(); };
            _btnPtmRemKey2.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnPtmRemKey2.ClientRectangle, _hoverPtmRm2); };
            _btnPtmRemKey2.Click += (s,e) => { _ptmKeyCode2=0;_audio.PtmKey2=0;CompactKeys(); };
            card.Controls.Add(_btnPtmRemKey2);
            // PTM MM buttons — purple, same sizing as Add Key
            Color MM_CLR2 = Color.FromArgb(160, 90, 210); int mmW2 = 80;
            _lblMmPtm = new Label{Text=_mmKeyCode>0?KeyName(_mmKeyCode):"Add MM Key",Font=new Font("Segoe UI",8f,FontStyle.Bold),ForeColor=MM_CLR2,BackColor=_mmKeyCode>0?INPUT_BG:Color.FromArgb(20,20,20),Size=Dpi.Size(mmW2,26),TextAlign=ContentAlignment.MiddleCenter};
            AttachKeyStyling(_lblMmPtm);
            _lblMmPtm.Click += (s,e) => BeginCapture(CaptureTarget.MmKey, _lblMmPtm); card.Controls.Add(_lblMmPtm);
            _btnMmRemPtm = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false,Visible=false};
            _btnMmRemPtm.FlatAppearance.BorderSize=0; _btnMmRemPtm.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnMmRemPtm.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverMmRmPtm = false;
            _btnMmRemPtm.MouseEnter += (s,e) => { _hoverMmRmPtm=true; _btnMmRemPtm.Invalidate(); };
            _btnMmRemPtm.MouseLeave += (s,e) => { _hoverMmRmPtm=false; _btnMmRemPtm.Invalidate(); };
            _btnMmRemPtm.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnMmRemPtm.ClientRectangle, _hoverMmRmPtm); };
            _btnMmRemPtm.Click += (s,e) => { if (_mmKeyCode2 > 0) { _mmKeyCode = _mmKeyCode2; _mmKeyCode2 = 0; } else { _mmKeyCode = 0; } _audio.MmKey = _mmKeyCode; _audio.MmKey2 = _mmKeyCode2; UpdateMmKeyLabels(); }; card.Controls.Add(_btnMmRemPtm);
            
            _lblMm2Ptm = new Label{Text=_mmKeyCode2>0?KeyName(_mmKeyCode2):"Add Key",Font=new Font("Segoe UI",8f,FontStyle.Bold),ForeColor=MM_CLR2,BackColor=_mmKeyCode2>0?INPUT_BG:Color.FromArgb(20,20,20),Size=Dpi.Size(mmW2,26),TextAlign=ContentAlignment.MiddleCenter,Visible=false};
            AttachKeyStyling(_lblMm2Ptm);
            _lblMm2Ptm.Click += (s,e) => BeginCapture(CaptureTarget.MmKey2, _lblMm2Ptm); card.Controls.Add(_lblMm2Ptm);
            bool _hoverMm2RmPtm = false;
            _btnMm2RemPtm = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false,Visible=false};
            _btnMm2RemPtm.FlatAppearance.BorderSize=0; _btnMm2RemPtm.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnMm2RemPtm.FlatAppearance.MouseDownBackColor=Color.Transparent;
            _btnMm2RemPtm.MouseEnter += (s,e) => { _hoverMm2RmPtm=true; _btnMm2RemPtm.Invalidate(); };
            _btnMm2RemPtm.MouseLeave += (s,e) => { _hoverMm2RmPtm=false; _btnMm2RemPtm.Invalidate(); };
            _btnMm2RemPtm.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnMm2RemPtm.ClientRectangle, _hoverMm2RmPtm); };
            _btnMm2RemPtm.Click += (s,e) => { _mmKeyCode2 = 0; _audio.MmKey2 = _mmKeyCode2; UpdateMmKeyLabels(); }; card.Controls.Add(_btnMm2RemPtm);
            
            _btnMm2AddPtm = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=MM_CLR2,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Visible=false};
            _btnMm2AddPtm.FlatAppearance.BorderColor=Color.FromArgb(MM_CLR2.R/3,MM_CLR2.G/3,MM_CLR2.B/3);
            _btnMm2AddPtm.MouseEnter += (s,e) => { _btnMm2AddPtm.BackColor=Color.FromArgb(MM_CLR2.R/5,MM_CLR2.G/5,MM_CLR2.B/5); };
            _btnMm2AddPtm.MouseLeave += (s,e) => { _btnMm2AddPtm.BackColor=Color.FromArgb(20,20,20); };
            _btnMm2AddPtm.Click += (s,e) => BeginCapture(CaptureTarget.MmKey2, _lblMm2Ptm); card.Controls.Add(_btnMm2AddPtm);
            // Initial visibility
            _btnPtmAddKey2.Visible = _ptmKeyCode2 <= 0 && _ptmKeyCode > 0;
            // PTM overlay toggle — on hotkey row
            _chkPtmOverlay = MakeOverlayCheck(y, card, _settings.PtmShowOverlay, (v) => { _audio.PtmShowOverlay = v; });
            _duckPtm = MakeDuckCheck(y, card, false, (v) => {
                if (v) { try { _ptmDuckSnapshot = Audio.GetSpeakerVolume(); } catch { _ptmDuckSnapshot = -1f; } }
                else { if (_ptmDuckSnapshot >= 0f) { try { Audio.SetSpeakerVolume(_ptmDuckSnapshot); } catch { } _ptmDuckSnapshot = -1f; } }
                _audio.PtmDuckEnabled = v; RefreshDuckingSection(); ShowDuckToast(_ptmKeyCode > 0 ? PushToTalk.GetKeyName(_ptmKeyCode) : "PTM", v);
            });
            _keyPtm = MakeKeyCheck(y, card, _settings.PtmSuppressEnabled, (v) => { _audio.PtmSuppressEnabled = v; ShowKeyToast(_ptmKeyCode > 0 ? PushToTalk.GetKeyName(_ptmKeyCode) : "PTM", v); });
            _chkPtmSound = MakeSoundCheck(y, card, _settings.PtmSoundFeedback, (v) => {
                _audio.PtmSoundFeedback = v;
                if (_drpPtmSound != null) _drpPtmSound.Enabled = v;
                if (_csPtmVol != null) _csPtmVol.Source.Enabled = v;
                if (_pttCard != null) _pttCard.Invalidate();
            });
            _drpPtmSound = MakeSoundDropdown(y, card, _settings.PtmSoundType, (v) => { _audio.PtmSoundType = v; }, () => _settings.PtmSoundVolume);
            _csPtmVol = MakeVolumeSlider(y, card, _settings.PtmSoundVolume, (v) => { _audio.PtmSoundVolume = v; });
            _csPtmVol.Source.Enabled = _settings.PushToMuteEnabled && _settings.PtmSoundFeedback;
            if (!_settings.PushToMuteEnabled) { _chkPtmOverlay.Dimmed = true; if (_duckPtm != null) _duckPtm.Dimmed = true; if (_keyPtm != null) _keyPtm.Dimmed = true; _chkPtmSound.Dimmed = true; _drpPtmSound.Enabled = false; }
            else if (!_settings.PtmSoundFeedback) { _drpPtmSound.Enabled = false; }
            LayoutPtmKeys();

            y = hk2Y + 34;
            AddLine(card, y); y += 10;

            // MODE 3: PUSH-TO-TOGGLE — with overlay icon
            _tglPtToggle = new ToggleSwitch { Location = Dpi.Pt(16, y) }; card.Controls.Add(_tglPtToggle);
            _tglPtToggle.Visible = false;
            { var ct = new CardToggle { PixelX = Dpi.S(16), PixelY = Dpi.S(y), Source = _tglPtToggle, Card = card };
              _cardToggleMap[card].Add(ct); }
            AddText(card, "Push-to-Toggle", 64, y, 10f, TXT, FontStyle.Bold);
            AddText(card, "Tap to mute, tap again to unmute.", 64, y+20, 7.5f, TXT3);
            _tglPtToggle.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglPtToggle.Checked && (IsCapturingKey || CaptureCooldownActive())) { _loading=true; _tglPtToggle.Checked=false; _loading=false; return; }
                if (_tglPtToggle.Checked && _ptToggleKeyCode <= 0) { _loading=true; _tglPtToggle.Checked=false; _loading=false; BeginCapture(CaptureTarget.ToggleKey1, _lblPtToggleKey); return; }
                if (!_tglPtToggle.Checked) {
                    // Toggle OFF: restore snapshot if duck was active
                    if (_toggleDuckSnapshot >= 0f) { try { Audio.SetSpeakerVolume(_toggleDuckSnapshot); } catch { } _toggleDuckSnapshot = -1f; }
                    if (_duckToggle != null && _duckToggle.Checked) { _duckToggle.Checked = false; _audio.PtToggleDuckEnabled = false; }
                    CancelAllCaptures();_ptToggleKeyCode=0;_ptToggleKeyCode2=0;_lblPtToggleKey.Text="Add Key";_lblPtToggleKey.BackColor=INPUT_BG;_lblPtToggleKey.ForeColor=ACC;LayoutToggleKeys();_audio.DisablePtToggleMode();
                    // SAFETY: force mic unmute — DisablePtToggleMode already does this, but belt-and-suspenders
                    try { Audio.SetMicMute(false); } catch { }
                    // Toggle OFF: also clear any MM key — it depends on Toggle being active
                    _mmKeyCode=0; _mmKeyCode2=0; _audio.MmKey=0; _audio.MmKey2=0; _audio.Save();
                    // Toggle OFF: PTT and PTM can no longer coexist — keep PTT, kill PTM
                    if (_tglPtt.Checked && _tglPtm.Checked) {
                        _loading=true; _tglPtm.Checked=false; _loading=false;
                        _ptmKeyCode=0;_ptmKeyCode2=0;_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;LayoutPtmKeys();_audio.DisablePtmMode();
                    }
                }
                else {
                    // Toggle ON: force duck OFF to prevent immediate volume change
                    if (_duckToggle != null && _duckToggle.Checked) { _duckToggle.Checked = false; _audio.PtToggleDuckEnabled = false; }
                    _toggleDuckSnapshot = -1f;
                    _audio.PtToggleEnabled = true; _audio.PtToggleKey = _ptToggleKeyCode; _audio.PtToggleKey2 = _ptToggleKeyCode2;
                    // Toggle ON: disable PTT and PTM, migrate their first key to MM key
                    int mmCandidate = 0;
                    if (_tglPtt.Checked) {
                        if (_pttKeyCode > 0) mmCandidate = _pttKeyCode;
                        if (_duckPtt != null && _duckPtt.Checked) { _duckPtt.Checked = false; _audio.PttDuckEnabled = false; }
                        _loading=true; _tglPtt.Checked=false; _loading=false;
                        _pttKeyCode=0;_pttKeyCode2=0;_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;CompactKeys();LayoutPttKeys();_audio.DisablePttMode();
                        if (_chkKey1Overlay != null) _chkKey1Overlay.Dimmed = true;
                        if (_chkPttSound != null) _chkPttSound.Dimmed = true;
                        if (_drpPttSound != null) _drpPttSound.Enabled = false;
                        if (_csPttVol != null) _csPttVol.Source.Enabled = false;
                    }
                    if (_tglPtm.Checked) {
                        if (mmCandidate <= 0 && _ptmKeyCode > 0) mmCandidate = _ptmKeyCode;
                        if (_ptmDuckSnapshot >= 0f) { try { Audio.SetSpeakerVolume(_ptmDuckSnapshot); } catch { } _ptmDuckSnapshot = -1f; }
                        if (_duckPtm != null && _duckPtm.Checked) { _duckPtm.Checked = false; _audio.PtmDuckEnabled = false; }
                        _loading=true; _tglPtm.Checked=false; _loading=false;
                        _ptmKeyCode=0;_ptmKeyCode2=0;if(_lblPtmKey!=null){_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;}LayoutPtmKeys();_audio.DisablePtmMode();
                        if (_chkPtmOverlay != null) _chkPtmOverlay.Dimmed = true;
                        if (_chkPtmSound != null) _chkPtmSound.Dimmed = true;
                        if (_drpPtmSound != null) _drpPtmSound.Enabled = false;
                        if (_csPtmVol != null) _csPtmVol.Source.Enabled = false;
                    }
                    // Migrate the captured key to MM key
                    if (mmCandidate > 0 && _mmKeyCode <= 0) {
                        _mmKeyCode = mmCandidate; _audio.MmKey = mmCandidate; _audio.Save();
                    }
                    // Disable VA — mutually exclusive
                    if (_tglVoiceActivity != null && _tglVoiceActivity.Checked) { _loading=true; _tglVoiceActivity.Checked=false; _loading=false; _audio.VoiceActivityEnabled = false; }
                    if (_ptToggleKeyCode > 0) Toast.Show("Push-to-Toggle Enabled", "Tap (" + PushToTalk.GetKeyName(_ptToggleKeyCode) + ") to mute/unmute.", ToastAccent.Blue, 3500);
                }
                RefreshPttHotkeyLabel();
                UpdateMmKeyLabels();
                // After auto-migration, hide the MM remove button — it's a silent indicator, not user-editable
                if (_btnMmRemToggle != null) _btnMmRemToggle.Visible = false;
                RefreshVolumeSliders();
                RefreshDuckingSection();
            } };
            int hk3Y = y + 46;
            _baseTogY = hk3Y;
            AddText(card, "Hotkey:", ki, hk3Y+4, 8f, TXT3);
            _lblPtToggleKey = new Label{Text=_ptToggleKeyCode > 0 ? PushToTalk.GetKeyName(_ptToggleKeyCode) : "Add Key",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(kbW,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(ki+50,hk3Y)};
            AttachKeyStyling(_lblPtToggleKey);
            _lblPtToggleKey.Click += (s,e) => BeginCapture(CaptureTarget.ToggleKey1, _lblPtToggleKey); card.Controls.Add(_lblPtToggleKey);
            // Key1 remove button for Toggle
            _btnToggleRemKey = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false,Visible=false};
            _btnToggleRemKey.FlatAppearance.BorderSize=0; _btnToggleRemKey.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnToggleRemKey.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverTogRm1 = false;
            _btnToggleRemKey.MouseEnter += (s,e) => { _hoverTogRm1=true; _btnToggleRemKey.Invalidate(); };
            _btnToggleRemKey.MouseLeave += (s,e) => { _hoverTogRm1=false; _btnToggleRemKey.Invalidate(); };
            _btnToggleRemKey.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnToggleRemKey.ClientRectangle, _hoverTogRm1); };
            _btnToggleRemKey.Click += (s,e) => { if (_ptToggleKeyCode2 > 0) { _ptToggleKeyCode = _ptToggleKeyCode2; _ptToggleKeyCode2 = 0; } else { _ptToggleKeyCode = 0; _loading=true; _tglPtToggle.Checked=false; _loading=false; } _audio.PtToggleKey = _ptToggleKeyCode; _audio.PtToggleKey2 = _ptToggleKeyCode2; if(_ptToggleKeyCode<=0){_lblPtToggleKey.Text="Add Key";_lblPtToggleKey.BackColor=INPUT_BG;_lblPtToggleKey.ForeColor=ACC;_audio.DisablePtToggleMode(); _mmKeyCode=0;_mmKeyCode2=0;_audio.MmKey=0;_audio.MmKey2=0;_audio.Save();UpdateMmKeyLabels();} CompactKeys(); };
            card.Controls.Add(_btnToggleRemKey);
            // Toggle Add Key button for key2
            _btnToggleAddKey2 = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Location=Dpi.Pt(ki+152,hk3Y)};
            _btnToggleAddKey2.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnToggleAddKey2.Click += (s,e) => BeginCapture(CaptureTarget.ToggleKey2, _lblPtToggleKey2);
            card.Controls.Add(_btnToggleAddKey2);
            // Toggle key2 row
            int toggleK2Y = hk3Y + 30;
            _lblPtToggleKey2 = new Label{Text=_ptToggleKeyCode2>0?KeyName(_ptToggleKeyCode2):"",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(kbW,26),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(ki+50,toggleK2Y),Visible=_ptToggleKeyCode2>0};
            AttachKeyStyling(_lblPtToggleKey2);
            _lblPtToggleKey2.Click += (s,e) => BeginCapture(CaptureTarget.ToggleKey2, _lblPtToggleKey2);
            card.Controls.Add(_lblPtToggleKey2);
            _btnToggleRemKey2 = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false,Location=Dpi.Pt(ki+150,toggleK2Y+1),Visible=_ptToggleKeyCode2>0};
            _btnToggleRemKey2.FlatAppearance.BorderSize=0; _btnToggleRemKey2.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnToggleRemKey2.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverTogRm2 = false;
            _btnToggleRemKey2.MouseEnter += (s,e) => { _hoverTogRm2=true; _btnToggleRemKey2.Invalidate(); };
            _btnToggleRemKey2.MouseLeave += (s,e) => { _hoverTogRm2=false; _btnToggleRemKey2.Invalidate(); };
            _btnToggleRemKey2.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnToggleRemKey2.ClientRectangle, _hoverTogRm2); };
            _btnToggleRemKey2.Click += (s,e) => { _ptToggleKeyCode2=0;_audio.PtToggleKey2=0;CompactKeys(); };
            card.Controls.Add(_btnToggleRemKey2);
            // Toggle MM buttons — purple, same sizing
            Color MM_CLR3 = Color.FromArgb(160, 90, 210); int mmW3 = 80;
            _lblMmToggle = new Label{Text=_mmKeyCode>0?KeyName(_mmKeyCode):"Add MM Key",Font=new Font("Segoe UI",8f,FontStyle.Bold),ForeColor=MM_CLR3,BackColor=_mmKeyCode>0?INPUT_BG:Color.FromArgb(20,20,20),Size=Dpi.Size(mmW3,26),TextAlign=ContentAlignment.MiddleCenter};
            AttachKeyStyling(_lblMmToggle);
            _lblMmToggle.Click += (s,e) => { if (_tglPtToggle != null && _tglPtToggle.Checked) BeginCapture(CaptureTarget.MmKey, _lblMmToggle); }; card.Controls.Add(_lblMmToggle);
            _btnMmRemToggle = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false,Visible=false};
            _btnMmRemToggle.FlatAppearance.BorderSize=0; _btnMmRemToggle.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnMmRemToggle.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverMmRmToggle = false;
            _btnMmRemToggle.MouseEnter += (s,e) => { _hoverMmRmToggle=true; _btnMmRemToggle.Invalidate(); };
            _btnMmRemToggle.MouseLeave += (s,e) => { _hoverMmRmToggle=false; _btnMmRemToggle.Invalidate(); };
            _btnMmRemToggle.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnMmRemToggle.ClientRectangle, _hoverMmRmToggle); };
            _btnMmRemToggle.Click += (s,e) => { if (_tglPtToggle != null && !_tglPtToggle.Checked) return; if (_mmKeyCode2>0) { _mmKeyCode = _mmKeyCode2; _mmKeyCode2 = 0; } else { _mmKeyCode = 0; } _audio.MmKey = _mmKeyCode; _audio.MmKey2 = _mmKeyCode2; UpdateMmKeyLabels(); }; card.Controls.Add(_btnMmRemToggle);
            
            _lblMm2Toggle = new Label{Text=_mmKeyCode2>0?KeyName(_mmKeyCode2):"Add Key",Font=new Font("Segoe UI",8f,FontStyle.Bold),ForeColor=MM_CLR3,BackColor=_mmKeyCode2>0?INPUT_BG:Color.FromArgb(20,20,20),Size=Dpi.Size(mmW3,26),TextAlign=ContentAlignment.MiddleCenter,Visible=false};
            AttachKeyStyling(_lblMm2Toggle);
            _lblMm2Toggle.Click += (s,e) => { if (_tglPtToggle != null && _tglPtToggle.Checked) BeginCapture(CaptureTarget.MmKey2, _lblMm2Toggle); }; card.Controls.Add(_lblMm2Toggle);
            bool _hoverMm2RmToggle = false;
            _btnMm2RemToggle = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(rmW,rmW),BackColor=Color.Transparent,TabStop=false,Visible=false};
            _btnMm2RemToggle.FlatAppearance.BorderSize=0; _btnMm2RemToggle.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnMm2RemToggle.FlatAppearance.MouseDownBackColor=Color.Transparent;
            _btnMm2RemToggle.MouseEnter += (s,e) => { _hoverMm2RmToggle=true; _btnMm2RemToggle.Invalidate(); };
            _btnMm2RemToggle.MouseLeave += (s,e) => { _hoverMm2RmToggle=false; _btnMm2RemToggle.Invalidate(); };
            _btnMm2RemToggle.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnMm2RemToggle.ClientRectangle, _hoverMm2RmToggle); };
            _btnMm2RemToggle.Click += (s,e) => { if (_tglPtToggle != null && !_tglPtToggle.Checked) return; _mmKeyCode2 = 0; _audio.MmKey2 = _mmKeyCode2; UpdateMmKeyLabels(); }; card.Controls.Add(_btnMm2RemToggle);
            
            _btnMm2AddToggle = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=MM_CLR3,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Visible=false};
            _btnMm2AddToggle.FlatAppearance.BorderColor=Color.FromArgb(MM_CLR3.R/3,MM_CLR3.G/3,MM_CLR3.B/3);
            _btnMm2AddToggle.MouseEnter += (s,e) => { _btnMm2AddToggle.BackColor=Color.FromArgb(MM_CLR3.R/5,MM_CLR3.G/5,MM_CLR3.B/5); };
            _btnMm2AddToggle.MouseLeave += (s,e) => { _btnMm2AddToggle.BackColor=Color.FromArgb(20,20,20); };
            _btnMm2AddToggle.Click += (s,e) => { if (_tglPtToggle != null && _tglPtToggle.Checked) BeginCapture(CaptureTarget.MmKey2, _lblMm2Toggle); }; card.Controls.Add(_btnMm2AddToggle);
            _btnToggleAddKey2.Visible = _ptToggleKeyCode2 <= 0 && _ptToggleKeyCode > 0;
            // Toggle overlay toggle — on hotkey row
            _chkToggleOverlay = MakeOverlayCheck(y, card, _settings.PtToggleShowOverlay, (v) => { _audio.PtToggleShowOverlay = v; });
            _duckToggle = MakeDuckCheck(y, card, false, (v) => {
                if (v) { try { _toggleDuckSnapshot = Audio.GetSpeakerVolume(); } catch { _toggleDuckSnapshot = -1f; } }
                else { if (_toggleDuckSnapshot >= 0f) { try { Audio.SetSpeakerVolume(_toggleDuckSnapshot); } catch { } _toggleDuckSnapshot = -1f; } }
                _audio.PtToggleDuckEnabled = v; RefreshDuckingSection(); ShowDuckToast(_ptToggleKeyCode > 0 ? PushToTalk.GetKeyName(_ptToggleKeyCode) : "Toggle", v);
            });
            _keyToggle = MakeKeyCheck(y, card, _settings.PtToggleSuppressEnabled, (v) => { _audio.PtToggleSuppressEnabled = v; ShowKeyToast(_ptToggleKeyCode > 0 ? PushToTalk.GetKeyName(_ptToggleKeyCode) : "Toggle", v); });
            _chkToggleSound = MakeSoundCheck(y, card, _settings.PtToggleSoundFeedback, (v) => {
                _audio.PtToggleSoundFeedback = v;
                if (_drpToggleSound != null) _drpToggleSound.Enabled = v;
                if (_csToggleVol != null) _csToggleVol.Source.Enabled = v;
                if (_pttCard != null) _pttCard.Invalidate();
            });
            _drpToggleSound = MakeSoundDropdown(y, card, _settings.PtToggleSoundType, (v) => { _audio.PtToggleSoundType = v; }, () => _settings.PtToggleSoundVolume);
            _csToggleVol = MakeVolumeSlider(y, card, _settings.PtToggleSoundVolume, (v) => { _audio.PtToggleSoundVolume = v; });
            _csToggleVol.Source.Enabled = _settings.PushToToggleEnabled && _settings.PtToggleSoundFeedback;
            if (!_settings.PushToToggleEnabled) { _chkToggleOverlay.Dimmed = true; if (_duckToggle != null) _duckToggle.Dimmed = true; if (_keyToggle != null) _keyToggle.Dimmed = true; _chkToggleSound.Dimmed = true; _drpToggleSound.Enabled = false; }
            else if (!_settings.PtToggleSoundFeedback) { _drpToggleSound.Enabled = false; }
            y = hk3Y + 34;
            AddLine(card, y); y += 10;

            // ═════════════════════════════════════════════════════════════════
            // MODE 4: VOICE ACTIVITY
            // ═════════════════════════════════════════════════════════════════
            int yVAct = y;
            _tglVoiceActivity = new ToggleSwitch { Location = Dpi.Pt(16, y), Visible = false };
            card.Controls.Add(_tglVoiceActivity);
            _ctVoiceActivity = new CardToggle { PixelX = Dpi.S(16), PixelY = Dpi.S(y), Source = _tglVoiceActivity, Card = card };
            _cardToggleMap[card].Add(_ctVoiceActivity);
            AddText(card, "Enable Voice Activity", 64, y + 1, 9.5f, TXT, FontStyle.Bold);
            AddText(card, "Auto-unmute when you speak", 64, y + 20, 7.5f, TXT3);
            AddText(card, "Mic stays muted until your voice is detected.", 64, y + 36, 7.5f, TXT4);
            
            y += 72;
            
            // ── LIVE LEVEL & THRESHOLD (one bar, draggable) ──
            AddText(card, "Level & Threshold", 16, y, 8.5f, TXT, FontStyle.Bold);
            _lblVoicePeakPct = AddText(card, "0%", 380, y, 7.5f, TXT3);
            _lblVoicePeakPct.RightAlign = true;
            y += 14;
            _lblVoiceThresholdPct = AddText(card, "Threshold: " + _settings.VoiceActivityThreshold + "%", 16, y, 7.5f, TXT3);
            y += 14;

            int meterY = y;
            int meterH = 24;
            _optMeterX = Dpi.S(16);
            _optMeterW = 0; 
            y += meterH + 6;
            AddText(card, "Drag the white handle to set threshold", 16, y, 7f, TXT4, FontStyle.Italic);
            y += 18;

            _trkVoiceThreshold = new SlickSlider { Minimum = 1, Maximum = 100, Value = _settings.VoiceActivityThreshold, Visible = false };
            card.Controls.Add(_trkVoiceThreshold);
            _trkVoiceThreshold.ValueChanged += (s, e) => {
                _lblVoiceThresholdPct.Text = "Threshold: " + _trkVoiceThreshold.Value + "%";
                card.Invalidate();
                if (_pushToTalk != null) _pushToTalk.SetVoiceThreshold(_trkVoiceThreshold.Value / 100f);
            };

            AddLine(card, y);
            y += 12;

            // ── HOLDOVER ──
            AddText(card, "Holdover Delay", 16, y, 9f, TXT, FontStyle.Bold);
            y += 20;
            AddText(card, "Keep mic open for", 64, y + 2, 9f, TXT, FontStyle.Regular);
            _nudVoiceHoldover = new PaddedNumericUpDown { Minimum = 200, Maximum = 5000, Increment = 100, Value = _settings.VoiceActivityHoldoverMs, Location = Dpi.Pt(200, y), Size = Dpi.Size(70, 24), BackColor = INPUT_BG, ForeColor = TXT, Font = new Font("Segoe UI", 9f), BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center };
            _nudVoiceHoldover.Leave += (s2, e2) => { try { int v; if (int.TryParse(_nudVoiceHoldover.Text, out v)) { v = Math.Max(200, Math.Min(5000, v)); _nudVoiceHoldover.Value = v; } } catch { _nudVoiceHoldover.Value = 2000; } };
            _nudVoiceHoldover.ValueChanged += (s2, e2) => { if (!_loading && _audio != null) { _audio.VoiceActivityHoldoverMs = (int)_nudVoiceHoldover.Value; if (_pushToTalk != null) _pushToTalk.SetVoiceHoldover((int)_nudVoiceHoldover.Value); } };
            _nudVoiceHoldover.TabStop = false;
            card.Controls.Add(_nudVoiceHoldover);
            AddText(card, "ms after speech stops", 278, y + 2, 9f, TXT3, FontStyle.Regular);

            y += 34;

            var _chkVAOverlay = MakeOverlayCheck(yVAct, card, _settings.VoiceActivityShowOverlay, (v) => { if (!_loading) _audio.VoiceActivityShowOverlay = v; });
            _chkVAOverlay.Dimmed = !_settings.VoiceActivityEnabled;

            // ── WARNINGS ──
            AddText(card, "\u26A0  Exclusive with Push-to-Talk modes. Only one can be active at a time.", 16, y, 7f, DarkTheme.Amber);
            y += 16;
            int _vaWarnY = y; 

            _tglVoiceActivity.CheckedChanged += (s, e) => {
                if (_loading) return;
                if (_tglVoiceActivity.Checked) {
                    CancelAllCaptures();
                    _loading = true;
                    if (_tglPtt != null && _tglPtt.Checked) _tglPtt.Checked = false;
                    if (_tglPtm != null && _tglPtm.Checked) _tglPtm.Checked = false;
                    if (_tglPtToggle != null && _tglPtToggle.Checked) _tglPtToggle.Checked = false;
                    _loading = false;
                    _pttKeyCode=0;_pttKeyCode2=0;
                    if(_lblPttKey!=null){_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;}
                    if(_lblPttKey2!=null)_lblPttKey2.Text="";
                    _ptmKeyCode=0;_ptmKeyCode2=0;
                    if(_lblPtmKey!=null){_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;}
                    if(_lblPtmKey2!=null)_lblPtmKey2.Text="";
                    _ptToggleKeyCode=0;_ptToggleKeyCode2=0;
                    if(_lblPtToggleKey!=null){_lblPtToggleKey.Text="Add Key";_lblPtToggleKey.BackColor=INPUT_BG;_lblPtToggleKey.ForeColor=ACC;}
                    if(_lblPtToggleKey2!=null)_lblPtToggleKey2.Text="";
                    CompactKeys();
                    LayoutPttKeys(); LayoutPtmKeys(); LayoutToggleKeys();
                    _audio.DisablePttMode();
                    _audio.DisablePtmMode();
                    _audio.DisablePtToggleMode();
                }
                _audio.VoiceActivityEnabled = _tglVoiceActivity.Checked;
                if (_tglVoiceActivity.Checked) {
                    Toast.Show("Voice Activity Enabled", "Mic automatically opens when you speak.", ToastAccent.Blue, 3500);
                }
                if (_chkVAOverlay != null) _chkVAOverlay.Dimmed = !_tglVoiceActivity.Checked;
                RefreshPttHotkeyLabel();
            };

            int savedMeterY = Dpi.S(meterY);
            int savedMeterH = Dpi.S(meterH);
            card.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int mx = Dpi.S(16), mw = card.Width - Dpi.S(32);
                _optMeterW = mw;

                float peak = _pushToTalk != null ? _pushToTalk.CurrentPeakLevel : 0f;
                float threshold = _trkVoiceThreshold.Value / 100f;

                var meterRect = new Rectangle(mx, savedMeterY, mw, savedMeterH);
                if (_optMeterHover || _optMeterDragging) {
                    using (var path = DarkTheme.RoundedRect(meterRect, Dpi.S(4)))
                    using (var p = new Pen(Color.FromArgb(_optMeterDragging ? 70 : 35, ACC.R, ACC.G, ACC.B))) g.DrawPath(p, path);
                }
                using (var path = DarkTheme.RoundedRect(meterRect, Dpi.S(4)))
                using (var b = new SolidBrush(Color.FromArgb(22, 22, 22))) g.FillPath(b, path);
                using (var b = new SolidBrush(Color.FromArgb(12, 0, 0, 0))) g.FillRectangle(b, mx + 1, savedMeterY + 1, mw - 2, Dpi.S(2));

                for (int pct = 25; pct <= 75; pct += 25) {
                    int gx = mx + (int)(mw * pct / 100f);
                    int ga = pct == 50 ? 18 : 10;
                    using (var p = new Pen(Color.FromArgb(ga, 255, 255, 255), 1)) g.DrawLine(p, gx, savedMeterY + Dpi.S(3), gx, savedMeterY + savedMeterH - Dpi.S(3));
                }

                int fillW = Math.Max(0, (int)(mw * Math.Min(1f, peak)));
                if (fillW > 4) {
                    Color barCol = (peak >= threshold) ? ACC : GREEN;
                    if (_vaGlowOpacity > 0.01f) {
                        int glowAlpha = (int)(20 * _vaGlowOpacity);
                        for (int gl = 0; gl < 3; gl++) {
                            int a = Math.Max(1, glowAlpha - gl * 5);
                            var glowRect = new Rectangle(mx - gl - 1, savedMeterY - gl - 1, mw + (gl + 1) * 2, savedMeterH + (gl + 1) * 2);
                            using (var glowPath = DarkTheme.RoundedRect(glowRect, Dpi.S(4) + gl + 1))
                            using (var b = new SolidBrush(Color.FromArgb(a, ACC.R, ACC.G, ACC.B)))
                                g.FillPath(b, glowPath);
                        }
                    }
                    var fillRect = new Rectangle(mx, savedMeterY, fillW, savedMeterH);
                    using (var path = DarkTheme.RoundedRect(fillRect, Dpi.S(4))) {
                        var oldClip2 = g.Clip; g.SetClip(path, CombineMode.Intersect);
                        Color topCol = Color.FromArgb(200, barCol.R, barCol.G, barCol.B);
                        Color botCol = Color.FromArgb(120, barCol.R, barCol.G, barCol.B);
                        using (var lgb = new LinearGradientBrush(new Point(0, savedMeterY), new Point(0, savedMeterY + savedMeterH), topCol, botCol)) g.FillRectangle(lgb, fillRect);
                        using (var b = new SolidBrush(Color.FromArgb(35, 255, 255, 255))) g.FillRectangle(b, mx, savedMeterY + 1, fillW, Dpi.S(2));
                        g.Clip = oldClip2;
                    }
                }

                int thX = mx + (int)(mw * threshold);
                using (var b = new SolidBrush(Color.FromArgb(25, 255, 255, 255))) g.FillRectangle(b, thX - Dpi.S(3), savedMeterY, Dpi.S(6), savedMeterH);
                using (var p = new Pen(Color.FromArgb(240, 255, 255, 255), Dpi.PenW(2))) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, thX, savedMeterY + Dpi.S(3), thX, savedMeterY + savedMeterH - Dpi.S(3)); }
                float hCy = savedMeterY + savedMeterH / 2f; int thr2 = Dpi.S(6);
                using (var b = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillEllipse(b, thX - thr2 + 1, hCy - thr2 + 1, thr2 * 2, thr2 * 2);
                if (_optMeterHover || _optMeterDragging) {
                    int glR = thr2 + Dpi.S(3); int glA = _optMeterDragging ? 50 : 30;
                    using (var b = new SolidBrush(Color.FromArgb(glA, ACC.R, ACC.G, ACC.B))) g.FillEllipse(b, thX - glR, hCy - glR, glR * 2, glR * 2);
                }
                using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, thX - thr2, hCy - thr2, thr2 * 2, thr2 * 2);
                int dotR2 = Dpi.S(2); using (var b = new SolidBrush(ACC)) g.FillEllipse(b, thX - dotR2, hCy - dotR2, dotR2 * 2, dotR2 * 2);

                if (_tglVoiceActivity != null && _tglVoiceActivity.Checked && _pushToTalk != null && _pushToTalk.IsVoiceActive) {
                    int glTop = Dpi.S(yVAct - 4);
                    int glBot = Dpi.S(yVAct + 52);
                    int glLeft = Dpi.S(10);
                    int glRight = card.Width - Dpi.S(10);
                    DarkTheme.PaintBreathingGlow(g, new Rectangle(glLeft, glTop, glRight - glLeft, glBot - glTop), ACC, Dpi.S(6));
                }

                PaintPauseWarning(g, Dpi.S(16), Dpi.S(_vaWarnY), 7f);
            };

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
                if (_optMeterDragging) {
                    float pct = (float)(e.X - _optMeterX) / Math.Max(1, _optMeterW);
                    _trkVoiceThreshold.Value = Math.Max(1, Math.Min(100, (int)(Math.Max(0.01f, Math.Min(1f, pct)) * 100)));
                    card.Invalidate();
                }
                bool inMeter = _optMeterW > 0 && e.X >= _optMeterX && e.X <= _optMeterX + _optMeterW &&
                    e.Y >= savedMeterY - Dpi.S(8) && e.Y <= savedMeterY + savedMeterH + Dpi.S(8);
                if (inMeter != _optMeterHover) { _optMeterHover = inMeter; card.Invalidate(); }
            };
            card.MouseUp += (s, e) => {
                if (_optMeterDragging) { _optMeterDragging = false; if (!_loading) _audio.VoiceActivityThreshold = _trkVoiceThreshold.Value; card.Invalidate(); }
            };
            card.MouseClick += (s, e) => {
                if (e.X >= _optMeterX && e.X <= _optMeterX + _optMeterW && e.Y >= savedMeterY - Dpi.S(8) && e.Y <= savedMeterY + savedMeterH + Dpi.S(8)) return;
            };
            card.MouseLeave += (s, e) => {
                if (_optMeterDragging) { _optMeterDragging = false; if (!_loading) _audio.VoiceActivityThreshold = _trkVoiceThreshold.Value; }
                if (_optMeterHover) { _optMeterHover = false; card.Invalidate(); }
            };

            _voiceMeterTimer = new Timer { Interval = 16 }; // 60fps
            _voiceMeterTimer.Tick += (s, e) => {
                try {
                    if (_panes != null && _panes.Length > 0 && _panes[0] != null && _panes[0].Visible && _pushToTalk != null) {
                        float peak = _pushToTalk.CurrentPeakLevel;
                        float threshold = _trkVoiceThreshold != null ? _trkVoiceThreshold.Value / 100f : 0.5f;
                        if (_lblVoicePeakPct != null) _lblVoicePeakPct.Text = ((int)(peak * 100)) + "%";
                        // Glow persistence: snap to 1.0 when above threshold, hold for holdover, then fade
                        if (peak >= threshold) {
                            _vaGlowOpacity = 1f;
                            _vaGlowPeakTick = Environment.TickCount;
                        } else if (_vaGlowOpacity > 0f) {
                            int holdMs = _nudVoiceHoldover != null ? (int)_nudVoiceHoldover.Value : 300;
                            int elapsed = Environment.TickCount - _vaGlowPeakTick;
                            if (elapsed < holdMs) {
                                _vaGlowOpacity = 1f; // hold at full intensity
                            } else {
                                float fadeMs = 500f;
                                _vaGlowOpacity = Math.Max(0f, 1f - (elapsed - holdMs) / fadeMs);
                            }
                        }
                        card.Invalidate();
                    }
                } catch { }
            };
            _voiceMeterTimer.Start();

            LayoutToggleKeys();

            // Handle resizing to keep dropdowns top-right of their segments
            card.Resize += (s2, e2) => {
                int rEdge = card.Width - Dpi.S(16);
                // Section base y positions (must match BuildPttPane layout: y+46 hotkey, 34+10 gap)
                int yPtt = 84, yPtm = 174, yTog = 264;

                // Row 1 (left→right): Key(-256), Duck(-222), Eye(-188), Sound(-154), Dropdown(-116)
                if (_keyPtt   != null) { _keyPtt.PixelX   = rEdge - Dpi.S(256); _keyPtt.PixelY   = Dpi.S(yPtt); }
                if (_duckPtt  != null) { _duckPtt.PixelX  = rEdge - Dpi.S(222); _duckPtt.PixelY  = Dpi.S(yPtt); }
                if (_chkKey1Overlay != null) { _chkKey1Overlay.PixelX = rEdge - Dpi.S(188); _chkKey1Overlay.PixelY = Dpi.S(yPtt); }
                _chkPttSound.PixelX = rEdge - Dpi.S(154); _chkPttSound.PixelY = Dpi.S(yPtt);
                _drpPttSound.Location = new Point(rEdge - Dpi.S(116), Dpi.S(yPtt));

                if (_keyPtm   != null) { _keyPtm.PixelX   = rEdge - Dpi.S(256); _keyPtm.PixelY   = Dpi.S(yPtm); }
                if (_duckPtm  != null) { _duckPtm.PixelX  = rEdge - Dpi.S(222); _duckPtm.PixelY  = Dpi.S(yPtm); }
                if (_chkPtmOverlay != null) { _chkPtmOverlay.PixelX = rEdge - Dpi.S(188); _chkPtmOverlay.PixelY = Dpi.S(yPtm); }
                _chkPtmSound.PixelX = rEdge - Dpi.S(154); _chkPtmSound.PixelY = Dpi.S(yPtm);
                _drpPtmSound.Location = new Point(rEdge - Dpi.S(116), Dpi.S(yPtm));

                if (_keyToggle  != null) { _keyToggle.PixelX  = rEdge - Dpi.S(256); _keyToggle.PixelY  = Dpi.S(yTog); }
                if (_duckToggle != null) { _duckToggle.PixelX = rEdge - Dpi.S(222); _duckToggle.PixelY = Dpi.S(yTog); }
                if (_chkToggleOverlay != null) { _chkToggleOverlay.PixelX = rEdge - Dpi.S(188); _chkToggleOverlay.PixelY = Dpi.S(yTog); }
                _chkToggleSound.PixelX = rEdge - Dpi.S(154); _chkToggleSound.PixelY = Dpi.S(yTog);
                _drpToggleSound.Location = new Point(rEdge - Dpi.S(116), Dpi.S(yTog));

                if (_chkVAOverlay != null) { _chkVAOverlay.PixelX = rEdge - Dpi.S(188); _chkVAOverlay.PixelY = Dpi.S(yVAct); }

                // Row 2 (+26): Slider only
                if (_csPttVol    != null) { _csPttVol.PixelX    = rEdge - Dpi.S(116); _csPttVol.PixelY    = Dpi.S(yPtt + 26); }
                if (_csPtmVol    != null) { _csPtmVol.PixelX    = rEdge - Dpi.S(116); _csPtmVol.PixelY    = Dpi.S(yPtm + 26); }
                if (_csToggleVol != null) { _csToggleVol.PixelX = rEdge - Dpi.S(116); _csToggleVol.PixelY = Dpi.S(yTog + 26); }

                btnMicSec.Location = new Point(card.Width - Dpi.S(90) - Dpi.S(2), Dpi.S(18));
            };
            
            y += 20;
            card.Dock = DockStyle.Top;
            card.Height = Dpi.S(y);
            pane.Controls.Add(card);
            
            LayoutAllMm();
        }
        void BuildMicLockPane(Panel pane) {
            var card = MakeCard(0, "Mic Lock", "Prevents apps from silently changing your mic level."); int y = 56;
            _micLockCard = card;
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

            _pttWarningText = BuildPttWarningText();
            _pttWarningY = Dpi.S(y);
            y += 24; 

            AddText(card, Audio.GetMicName()??"Mic: Unknown", 64, y, 8f, TXT4);
            y+=18;

            int initMicVol = 50; try { initMicVol = (int)Audio.GetMicVolume(); } catch {}
            bool initMicMuted = false; try { initMicMuted = Audio.GetMicMute(); } catch {}
            AddText(card, "Volume:", 64, y+4, 8f, TXT3);
            _micCurVolSlider = new SlickSlider{Minimum=0,Maximum=100,Value=initMicVol,Location=Dpi.Pt(120,y-4),Size=Dpi.Size(180,30)}; card.Controls.Add(_micCurVolSlider);
            _micCurVolSlider.Visible = false;
            { var cs = new CardSlider { PixelX = Dpi.S(120), PixelY = Dpi.S(y-4), PixelW = Dpi.S(180), PixelH = Dpi.S(30), Source = _micCurVolSlider, Card = card };
              _cardSliderMap[card].Add(cs); }
            _lblMicCurVolPct = AddText(card, initMicVol+"%", 380, y+4, 8.5f, GREEN, FontStyle.Bold); _lblMicCurVolPct.RightAlign = true;
            _micCurVolSlider.ValueChanged += (s,e) => { _lblMicCurVolPct.Text=_micCurVolSlider.Value+"%"; card.Invalidate(); };
            _micCurVolSlider.DragCompleted += (s,e) => { if (!_loading) { try { Audio.SetMicVolume(_micCurVolSlider.Value); } catch {} UpdateCurrent(); } };
            _micMuteIcon = new CardIcon { W = 28, H = 24, Checked = !initMicMuted, IsEye = false, Card = card,
                OnChange = (on) => { try { Audio.SetMicMute(!on); } catch {} UpdateCurrent(); } };
            _micMuteIcon.SetPos(392, y);
            _cardIconMap[card].Add(_micMuteIcon);
            y+=28;

            AddText(card, "Lock at:", 64, y+4, 8f, TXT2);
            _trkMicVol = new SlickSlider{Minimum=0,Maximum=100,Value=100,Location=Dpi.Pt(120,y-4),Size=Dpi.Size(180,30)}; card.Controls.Add(_trkMicVol);
            _trkMicVol.Visible = false;
            { var cs = new CardSlider { PixelX = Dpi.S(120), PixelY = Dpi.S(y-4), PixelW = Dpi.S(180), PixelH = Dpi.S(30), Source = _trkMicVol, Card = card };
              _cardSliderMap[card].Add(cs); }
            _plMicVol = AddText(card, "100%", 380, y+4, 8.5f, ACC, FontStyle.Bold); _plMicVol.RightAlign = true;
            _trkMicVol.ValueChanged += (s,e) => { _plMicVol.Text=_trkMicVol.Value+"%"; card.Invalidate(); };
            _trkMicVol.DragCompleted += (s,e) => { if (!_loading) { _audio.MicLockVolume=_trkMicVol.Value; if (_tglMicEnf.Checked) try { Audio.SetMicVolume(_trkMicVol.Value); } catch { } } };
            _micLockIcon = new CardIcon { W = 20, H = 24, Checked = _settings.MicEnforceEnabled, IsLock = true, Card = card,
                OnChange = (locked) => {
                    if (_loading) return;
                    if ((DateTime.UtcNow - _lastLockClick).TotalMilliseconds < 300) { _micLockIcon.Checked = !locked; return; }
                    _lastLockClick = DateTime.UtcNow;
                    _loading = true; _tglMicEnf.Checked = locked; _loading = false;
                    if (locked) {
                        StartGlisten(_tglMicEnf);
                        if (_micLockCard != null) _micLockCard.Invalidate();
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

            card.Dock = DockStyle.Top;
            card.Height = Dpi.S(232);
            pane.Controls.Add(card);
        }

        void BuildSpkLockPane(Panel pane) {
            var card = MakeCard(1, "Volume Lock", "Keep your speaker levels exactly where you set them."); int y = 56;
            _volCard = card;
            if (!_cardSliderMap.ContainsKey(card)) _cardSliderMap[card] = new List<CardSlider>();
            if (!_cardIconMap.ContainsKey(card)) _cardIconMap[card] = new List<CardIcon>();

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

            _spkWarningText = BuildSpkWarningText();
            _spkWarningY = Dpi.S(y);
            y += 24;

            AddText(card, Audio.GetSpeakerName()??"Speaker: Unknown", 64, y, 8f, TXT4);
            y+=18;

            int initSpkVol2 = 100; try { initSpkVol2 = (int)Audio.GetSpeakerVolume(); } catch {}
            bool initSpkMuted = false; try { initSpkMuted = Audio.GetSpeakerMute(); } catch {}
            AddText(card, "Volume:", 64, y+4, 8f, TXT3);
            _spkCurVolSlider = new SlickSlider{Minimum=0,Maximum=100,Value=initSpkVol2,Location=Dpi.Pt(120,y-4),Size=Dpi.Size(180,30)}; card.Controls.Add(_spkCurVolSlider);
            _spkCurVolSlider.Visible = false;
            { var cs = new CardSlider { PixelX = Dpi.S(120), PixelY = Dpi.S(y-4), PixelW = Dpi.S(180), PixelH = Dpi.S(30), Source = _spkCurVolSlider, Card = card };
              _cardSliderMap[card].Add(cs); }
            _lblSpkCurVolPct = AddText(card, initSpkVol2+"%", 380, y+4, 8.5f, GREEN, FontStyle.Bold); _lblSpkCurVolPct.RightAlign = true;
            _spkCurVolSlider.ValueChanged += (s,e) => { _lblSpkCurVolPct.Text=_spkCurVolSlider.Value+"%"; card.Invalidate(); };
            _spkCurVolSlider.DragCompleted += (s,e) => { if (!_loading) { try { Audio.SetSpeakerVolume(_spkCurVolSlider.Value); } catch {} UpdateCurrent(); } };
            _spkMuteIcon = new CardIcon { W = 28, H = 24, Checked = !initSpkMuted, IsEye = false, Card = card,
                OnChange = (on) => { try { Audio.SetSpeakerMute(!on); } catch {} UpdateCurrent(); } };
            _spkMuteIcon.SetPos(392, y);
            _cardIconMap[card].Add(_spkMuteIcon);
            y+=28;

            AddText(card, "Lock at:", 64, y+4, 8f, TXT2);
            _trkSpkVol = new SlickSlider{Minimum=0,Maximum=100,Value=100,Location=Dpi.Pt(120,y-4),Size=Dpi.Size(180,30)}; card.Controls.Add(_trkSpkVol);
            _trkSpkVol.Visible = false;
            { var cs = new CardSlider { PixelX = Dpi.S(120), PixelY = Dpi.S(y-4), PixelW = Dpi.S(180), PixelH = Dpi.S(30), Source = _trkSpkVol, Card = card };
              _cardSliderMap[card].Add(cs); }
            _plSpkVol = AddText(card, "100%", 380, y+4, 8.5f, ACC, FontStyle.Bold); _plSpkVol.RightAlign = true;
            _trkSpkVol.ValueChanged += (s,e) => { _plSpkVol.Text=_trkSpkVol.Value+"%"; card.Invalidate(); };
            _trkSpkVol.DragCompleted += (s,e) => { if (!_loading) { _audio.SpeakerLockVolume=_trkSpkVol.Value; if (_tglSpkEnf.Checked) try { Audio.SetSpeakerVolume(_trkSpkVol.Value); } catch { } } };
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


            card.Dock = DockStyle.Top;
            card.Height = Dpi.S(220);
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
            _glistenTimer = new Timer { Interval = 16 }; // 60fps
            _glistenTimer.Tick += (s, e) => {
                if (_tglGlistenTarget == null || _tglGlistenFrame >= 65) {
                    _tglGlistenTarget = null; _glistenTimer.Stop(); _glistenTimer.Dispose(); _glistenTimer = null; return;
                }
                _tglGlistenFrame++;
                if (_tglGlistenFrame >= 65) { _tglGlistenTarget = null; _glistenTimer.Stop(); _glistenTimer.Dispose(); _glistenTimer = null; }
                // Only invalidate the card that contains the animated toggle (and only if visible)
                var parentCard = target.Parent;
                if (parentCard != null && parentCard.Visible && parentCard.Parent != null && parentCard.Parent.Visible)
                    parentCard.Invalidate();
            };
            _glistenTimer.Start();
            // Immediate first paint
            var firstCard = target.Parent;
            if (firstCard != null && firstCard.Visible && firstCard.Parent != null && firstCard.Parent.Visible)
                firstCard.Invalidate();
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
            public int TargetVolume = 100; // Raw app volume 0-100 (what user set). Slider shows effective = TargetVolume * master / 100
            public int CurrentVolume = -1; // -1 = unknown, 0-100 = live from system
        }
        float _lastMasterVol = 100f;
        Timer _appVolTimer;

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
            btnMaxAll.Click+=(s,e)=>{ AnimateMaxAllApps(); try { Audio.SetSpeakerVolume(100); } catch {} };
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

// System volume slider removed (redundant with Volume Lock page)

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
            card.Layout += (s2, e2) => sizeList(s2, e2); // fires on initial layout

            card.Dock = DockStyle.Fill;
            pane.Controls.Add(card);

            // Initialize master volume and start poll timer
            try { float mv = Audio.GetSpeakerVolume(); if (mv >= 0) _lastMasterVol = mv; } catch { }
            _appVolTimer = new Timer { Interval = 200 };
            _appVolTimer.Tick += (s2, e2) => {
                float newMaster;
                try { newMaster = Audio.GetSpeakerVolume(); } catch { return; }
                if (newMaster < 0) return;
                if (Math.Abs(newMaster - _lastMasterVol) < 0.5f) return; // No change
                _lastMasterVol = newMaster;
                // Update all app slider positions to reflect new effective volume
                foreach (var ar in _appRows) {
                    if (ar.Slider == null) continue;
                    int eff = (int)(ar.TargetVolume * newMaster / 100f);
                    if (ar.Slider.Value != eff) ar.Slider.Value = eff;
                    if (ar.VolLabel != null) ar.VolLabel.Text = ar.TargetVolume + "% / " + (int)newMaster + "%";
                    if (ar.CurrentLabel != null && ar.CurrentVolume >= 0) {
                        ar.CurrentVolume = eff;
                        ar.CurrentLabel.Text = eff + "%";
                    }
                }
            };
            _appVolTimer.Start();
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

                // Slider — displays EFFECTIVE volume (target × master / 100)
                // The full bar always represents 0-100% absolute output
                int sliderX = curX + Dpi.S(48);
                int sliderW = Math.Max(Dpi.S(60), sliderEnd - sliderX);
                int rawTarget = ar.TargetVolume > 0 ? ar.TargetVolume : (ar.Slider != null ? ar.Slider.Value : ar.InitialValue);
                ar.TargetVolume = rawTarget;
                int effectiveVal = (int)(rawTarget * masterVol / 100f);
                var slider = new SlickSlider{Minimum=0,Maximum=100,Value=effectiveVal,Location=new Point(sliderX,0),Size=new Size(sliderW,rowH),Enabled=ar.Locked,BackColor=rowBg};
                ar.Slider = slider;
                ar.InitialValue = rawTarget;
                row.Controls.Add(slider);

                // Target volume + master context (accent blue) — shows "target% / master%"
                string targetText = rawTarget + "% / " + (int)masterVol + "%";
                var volLbl = new Label{Text=targetText,Font=_rowVolFont,ForeColor=ar.Locked?ACC:TXT4,AutoSize=false,Size=new Size(Dpi.S(80),rowH),TextAlign=ContentAlignment.MiddleCenter,Location=new Point(targetX,0)};
                ar.VolLabel = volLbl;
                string sliderAppName = ar.Name;
                bool sliderLocked = ar.Locked;
                Label sliderCurLbl = curLbl;
                AppRuleRow capturedRow = ar;
                slider.ValueChanged += (s,e) => {
                    // Reverse-map: user sees effective, we compute raw target
                    float curMaster = _lastMasterVol;
                    if (curMaster < 1) curMaster = 1; // avoid /0
                    int newTarget = Math.Min(100, (int)(slider.Value * 100f / curMaster));
                    capturedRow.TargetVolume = newTarget;
                    volLbl.Text = newTarget + "% / " + (int)curMaster + "%";
                    if (sliderLocked) {
                        sliderCurLbl.Text = slider.Value + "%";
                        sliderCurLbl.ForeColor = Color.FromArgb(80, 200, 120);
                    }
                };
                slider.DragCompleted += (s,e) => {
                    // Heavy: push to system only on mouse release
                    if (sliderLocked) {
                        try { Audio.SetAppVolume(sliderAppName, capturedRow.TargetVolume); } catch { }
                        SyncAppRulesLive();
                    }
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
                    int vol = ar.TargetVolume; // Raw app volume, not the effective display value
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
        private bool _optMeterDragging, _optMeterHover;
        private int _optMeterX, _optMeterW;
        private float _vaGlowOpacity = 0f; // 0..1 animated glow intensity
        private int _vaGlowPeakTick = 0; // TickCount when peak last exceeded threshold

        // BuildVoiceActivityPane merged into BuildPttPane


        private int _eqVizY = 0;
        private Panel _eqCard;
        private float[] _eqBands = new float[10];
        private float[] _eqTargets = new float[10];
        private Timer _eqAnimTimer;
        private bool _customEqMode = false;
        private int _eqDragBandIdx = -1;
        private int _eqDragStartY;
        private float _eqDragStartVal;
        static readonly string[] EQ_LABELS = { "31", "62", "125", "250", "500", "1K", "2K", "4K", "8K", "16K" };
        static readonly float[] EQ_FLAT =    { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
        static readonly float[] EQ_BASS =    { 0.9f, 0.85f,0.7f, 0.55f,0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
        private DarkComboBox _cmbEqPreset;

        void BuildEqPane(Panel pane) {
            var card = MakeCard(5, "Output", "Audio Enhancements Customised by You.", "Alt+O");
            _eqCard = card;
            int y = 56;

            // ── AUDIO ENHANCEMENTS ──
            AddText(card, "EQUALIZER", 16, y, 7.5f, TXT3, FontStyle.Bold);
            y += 24;

            AddLine(card, y); y += 14;

            // ── PRESET ──
            AddText(card, "PRESET", 16, y + 6, 7.5f, TXT3, FontStyle.Bold);
            _cmbEqPreset = new DarkComboBox { Size = Dpi.Size(210, 26), Location = Dpi.Pt(70, y) };
            _cmbEqPreset.Items.AddRange(new object[] {
                "Flat", "Bass Heavy", "Vocal Clarity", "Surround Boost",
                "Night Cinema", "Dynamic Compression", "Gaming", "Podcast", "Custom"
            });
            _cmbEqPreset.SelectedIndex = 0;
            _cmbEqPreset.SelectedIndexChanged += (s,e) => {
                if (!_loading) {
                    int si = _cmbEqPreset.SelectedIndex;
                    if (si == 5) {
                        // "Dynamic Compression" preset — enable Night Mode, flatten EQ bars
                        _customEqMode = false;
                        _audio.NightModeEnabled = true;
                        Array.Copy(EQ_FLAT, _eqTargets, 10);
                        if (_eqAnimTimer != null) _eqAnimTimer.Start();
                        LiveWriteApoConfig();
                    } else if (si < 9) {
                        // Disable loudness eq for all other presets
                        if (_audio.NightModeEnabled) { _audio.NightModeEnabled = false; }
                        _customEqMode = (si == 8); ApplyEqPreset(si < 5 ? si : si - 1); // skip index 5 in EQ arrays
                    } else { _customEqMode = true; if (_eqCard != null) _eqCard.Invalidate(); }
                    LiveWriteApoConfig();
                }
            };
            card.Controls.Add(_cmbEqPreset);
            y += 42;

            AddLine(card, y); y += 18;

            // ── FREQUENCY RESPONSE ──
            AddText(card, "FREQUENCY RESPONSE", 16, y, 7.5f, TXT3, FontStyle.Bold);
            y += 20;

            card.Paint += PaintEqVisualizer;
            _eqVizY = y;

            card.MouseDown += (s, e) => {
                if (e.Button != MouseButtons.Left) return;
                int oy = Dpi.S(_eqVizY), oh = Dpi.S(110);
                int ox = Dpi.S(16), ow = card.Width - Dpi.S(32);
                if (e.Y < oy || e.Y >= oy + oh) return;
                int bx = ox + Dpi.S(34);
                int barW = (ow - Dpi.S(60)) / _eqBands.Length;
                for (int i = 0; i < _eqBands.Length; i++) {
                    if (e.X >= bx && e.X < bx + barW) {
                        _eqDragBandIdx  = i;
                        _eqDragStartY   = e.Y - oy;
                        _eqDragStartVal = _eqBands[i];
                        break;
                    }
                    bx += barW;
                }
            };
            card.MouseMove += (s, e) => {
                int oy = Dpi.S(_eqVizY), oh = Dpi.S(110);
                int maxBarH = oh - Dpi.S(18) - Dpi.S(8);
                card.Cursor = (e.Y >= oy && e.Y < oy + oh) ? DarkTheme.Hand : Cursors.Default;
                if (_eqDragBandIdx < 0 || e.Button != MouseButtons.Left || maxBarH <= 0) return;
                float delta  = -(float)((e.Y - oy) - _eqDragStartY) / maxBarH;
                float newVal = Math.Max(0f, Math.Min(1f, _eqDragStartVal + delta));
                _eqBands[_eqDragBandIdx]   = newVal;
                _eqTargets[_eqDragBandIdx] = newVal;
                AutoDetectPreset();
                card.Invalidate();
            };
            card.MouseUp    += (s, e) => { if (_eqDragBandIdx >= 0) { _eqDragBandIdx = -1; LiveWriteApoConfig(); } else { _eqDragBandIdx = -1; } };
            card.MouseLeave += (s, e) => { _eqDragBandIdx = -1; card.Cursor = Cursors.Default; };

            Array.Copy(EQ_FLAT, _eqBands, 10);
            Array.Copy(EQ_FLAT, _eqTargets, 10);
            _eqAnimTimer = new Timer { Interval = 16 };
            _eqAnimTimer.Tick += (s,e) => {
                bool changed = false;
                for (int i = 0; i < 10; i++) {
                    float diff = _eqTargets[i] - _eqBands[i];
                    if (Math.Abs(diff) > 0.005f) { _eqBands[i] += diff * 0.08f; changed = true; }
                    else if (_eqBands[i] != _eqTargets[i]) { _eqBands[i] = _eqTargets[i]; changed = true; }
                }
                if (changed) { if (_eqCard != null) _eqCard.Invalidate(); }
                else _eqAnimTimer.Stop();
            };

            card.Dock = DockStyle.Top;
            card.Height = Dpi.S(300); // taller EQ to match Apps
            pane.Controls.Add(card);
        }

        void UpdateEqTargets() {
            float[] src = EQ_FLAT;
            Array.Copy(src, _eqTargets, 10);
            _customEqMode = false;
            if (_eqAnimTimer != null) _eqAnimTimer.Start();
            AutoDetectPreset();
        }

        // Write current _eqBands + compression state to APO config immediately (APO hot-reloads, no restart)
        void LiveWriteApoConfig() {
            if (!Audio.IsEqualizerAPOInstalled()) return;
            bool compress = _audio != null && _audio.NightModeEnabled;
            int  release  = _settings.NightModeReleaseTime;
            // Use _eqTargets — the intended final state. Snap _eqBands too so they stay in sync.
            Array.Copy(_eqTargets, _eqBands, 10);
            float[] snap  = (float[])_eqTargets.Clone();
            // Persist band values
            var parts = new string[10];
            for (int i = 0; i < 10; i++)
                parts[i] = snap[i].ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            _audio.EqBands = string.Join("|", parts);
            System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                string err = Audio.WriteEqualizerAPOConfig(snap, compress, release);
                if (err != null) Logger.Warn("LiveWriteApoConfig failed: " + err);
            });
        }

        void ApplyEqPreset(int idx) {
            float[][] presets = {
                EQ_FLAT,
                new float[] { 0.95f,0.9f, 0.75f,0.55f,0.45f,0.45f,0.5f, 0.5f, 0.5f, 0.5f },
                new float[] { 0.4f, 0.45f,0.5f, 0.65f,0.8f, 0.85f,0.7f, 0.55f,0.5f, 0.5f },
                new float[] { 0.55f,0.5f, 0.45f,0.4f, 0.5f, 0.6f, 0.75f,0.8f, 0.7f, 0.6f },
                new float[] { 0.7f, 0.65f,0.5f, 0.6f, 0.75f,0.8f, 0.65f,0.5f, 0.45f,0.4f },
                new float[] { 0.6f, 0.55f,0.5f, 0.5f, 0.55f,0.65f,0.8f, 0.85f,0.75f,0.6f },
                new float[] { 0.35f,0.4f, 0.5f, 0.7f, 0.85f,0.9f, 0.8f, 0.6f, 0.5f, 0.45f },
            };
            if (idx >= 0 && idx < presets.Length) {
                Array.Copy(presets[idx], _eqTargets, 10);
                _customEqMode = false;
                if (_eqAnimTimer != null) _eqAnimTimer.Start();
            }
        }

        // Check if current bands match any named preset; update combo without triggering change event
        void AutoDetectPreset() {
            if (_cmbEqPreset == null) return;
            float[][] namedPresets = {
                EQ_FLAT,
                new float[] { 0.95f,0.9f, 0.75f,0.55f,0.45f,0.45f,0.5f, 0.5f, 0.5f, 0.5f },
                new float[] { 0.4f, 0.45f,0.5f, 0.65f,0.8f, 0.85f,0.7f, 0.55f,0.5f, 0.5f },
                new float[] { 0.55f,0.5f, 0.45f,0.4f, 0.5f, 0.6f, 0.75f,0.8f, 0.7f, 0.6f },
                new float[] { 0.7f, 0.65f,0.5f, 0.6f, 0.75f,0.8f, 0.65f,0.5f, 0.45f,0.4f },
                new float[] { 0.6f, 0.55f,0.5f, 0.5f, 0.55f,0.65f,0.8f, 0.85f,0.75f,0.6f },
                new float[] { 0.35f,0.4f, 0.5f, 0.7f, 0.85f,0.9f, 0.8f, 0.6f, 0.5f, 0.45f },
            };
            int matchIdx = 8; // default to Custom
            for (int p = 0; p < namedPresets.Length; p++) {
                bool match = true;
                for (int b = 0; b < 10; b++) {
                    if (Math.Abs(_eqBands[b] - namedPresets[p][b]) > 0.02f) { match = false; break; }
                }
                if (match) { matchIdx = p < 5 ? p : p + 1; break; }
            }
            _loading = true;
            _cmbEqPreset.SelectedIndex = matchIdx;
            _customEqMode = (matchIdx == 8);
            _loading = false;
            if (_eqCard != null) _eqCard.Invalidate();
        }

        void PaintEqVisualizer(object sender, PaintEventArgs e) {
            if (_eqCard == null) return;
            var g = e.Graphics;

            // NO TranslateTransform — GDI+ clips stay in world space, so transform
            // causes the clip to be in the wrong position. Instead, all draw calls
            // use explicit ox/oy offsets. h is hardcoded since the form is size-locked.
            int ox = Dpi.S(16);   // left edge of viz in card coords
            int oy = Dpi.S(_eqVizY); // top edge of viz in card coords
            int w  = _eqCard.Width - Dpi.S(32);
            int h  = Dpi.S(110);  // hardcoded — form locked at Dpi.Size(730,410)
            if (w < Dpi.S(60)) return;

            // Clip to viz rect so nothing draws into card header or footer
            var oldClip = g.Clip;
            g.SetClip(new Rectangle(ox, oy, w, h));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int bands   = _eqBands.Length;
            int barW    = (w - Dpi.S(60)) / bands;
            int gap     = Dpi.S(3);
            int baseY   = oy + h - Dpi.S(18);   // y of the 0dB baseline
            int maxBarH = (h - Dpi.S(18) - Dpi.S(8)); // drawable bar height

            // Background grid lines
            using (var p = new Pen(Color.FromArgb(20, 255, 255, 255))) {
                for (int i = 0; i <= 4; i++) {
                    int gy = oy + Dpi.S(8) + (maxBarH * i / 4);
                    g.DrawLine(p, ox + Dpi.S(30), gy, ox + w - Dpi.S(8), gy);
                }
            }
            // dB labels
            using (var f = new Font("Segoe UI", 6f)) using (var b = new SolidBrush(TXT4)) {
                g.DrawString("+12", f, b, ox, oy + Dpi.S(4));
                g.DrawString("0dB", f, b, ox, oy + Dpi.S(8) + maxBarH / 2 - Dpi.S(5));
                g.DrawString("-12", f, b, ox, baseY - Dpi.S(10));
            }

            // Bars with gradient fill
            int bx = ox + Dpi.S(34);
            for (int i = 0; i < bands; i++) {
                float val   = _eqBands[i];
                int barH    = (int)(maxBarH * val);
                int by      = baseY - barH;
                var barRect = new Rectangle(bx, by, barW - gap, barH);
                if (barRect.Height > 0 && barRect.Width > 0) {
                    float hue  = 200f + i * 16f;
                    Color top  = HsvToRgb(hue, 0.6f, 1f, 180);
                    Color bot  = HsvToRgb(hue, 0.8f, 0.4f, 100);
                    using (var grad = new LinearGradientBrush(
                        new Rectangle(bx, by, barW - gap, Math.Max(1, barH)), top, bot, 90f))
                        g.FillRectangle(grad, barRect);
                    // Bright top cap
                    using (var cap = new SolidBrush(HsvToRgb(hue, 0.4f, 1f, 220)))
                        g.FillRectangle(cap, bx, by, barW - gap, Dpi.S(2));
                }
                // Frequency label (below baseline, still within clip)
                using (var f = new Font("Segoe UI", 5.5f)) using (var br = new SolidBrush(TXT4)) {
                    var sf = new StringFormat { Alignment = StringAlignment.Center };
                    g.DrawString(EQ_LABELS[i], f, br, bx + (barW - gap) / 2f, baseY + Dpi.S(2), sf);
                }
                // Custom mode: drag handle
                if (_customEqMode) {
                    int handleY = (barH > 0) ? by : baseY - Dpi.S(2);
                    int handleW = barW - gap;
                    int handleH = Dpi.S(5);
                    bool isDragging = (_eqDragBandIdx == i);
                    Color handleCol = isDragging
                        ? Color.FromArgb(255, ACC.R, ACC.G, ACC.B)
                        : Color.FromArgb(180, 200, 230, 255);
                    using (var hb = new SolidBrush(handleCol))
                        g.FillRectangle(hb, bx, handleY - handleH / 2, handleW, handleH);
                    int dotR = Dpi.S(3);
                    using (var db = new SolidBrush(handleCol))
                        g.FillEllipse(db, bx + handleW/2 - dotR, handleY - dotR, dotR*2, dotR*2);
                }
                bx += barW;
            }

            // Custom mode hint text
            if (_customEqMode) {
                using (var f = new Font("Segoe UI", 6.5f, FontStyle.Italic))
                using (var b = new SolidBrush(Color.FromArgb(100, ACC.R, ACC.G, ACC.B))) {
                    var sf = new StringFormat { Alignment = StringAlignment.Far };
                    g.DrawString("Drag bars to adjust", f, b, ox + w - Dpi.S(6), oy + Dpi.S(82), sf);
                }
            }
            g.Clip = oldClip;
        }

        // ═════════════════════════════════════════════════════════════════
        // AFK PROTECTION PANE
        // ═════════════════════════════════════════════════════════════════
        private Panel _afkCard;
        private ToggleSwitch _tglAfkMic, _tglAfkSpk;
        private PaddedNumericUpDown _nudAfkMicSec, _nudAfkSpkSec;

        void BuildAfkPane(Panel pane) {
            var card = MakeCard(7, "AFK Protection", "Automatically mute or fade audio when you step away.", "Alt+]");
            _afkCard = card;
            if (!_cardSliderMap.ContainsKey(card)) _cardSliderMap[card] = new List<CardSlider>();

            int y = 56;
            Color GOLD = Color.FromArgb(218, 175, 62);
            Color GOLD_DIM = Color.FromArgb(100, 90, 50);

            // ── SECTION 1: Mic Mute ─────────────────────────────────────
            _tglAfkMic = Tgl("Mute Microphone", null, y, card);
            AddText(card, "Automatically mute your mic after a period of inactivity.", 64, y + 20, 7.5f, TXT3);

            // Timeout row
            int nudY = y + 48;
            AddText(card, "Timeout:", 64, nudY + 4, 8f, TXT3);
            _nudAfkMicSec = new PaddedNumericUpDown {
                Minimum = 10, Maximum = 3600, Value = Math.Max(10, Math.Min(3600, _settings.AfkMicMuteSec)),
                Size = Dpi.Size(56, 24), Location = Dpi.Pt(130, nudY),
                BackColor = Color.FromArgb(24, 24, 24), ForeColor = TXT,
                Font = new Font("Segoe UI", 9f), BorderStyle = BorderStyle.None,
                TextAlign = HorizontalAlignment.Center
            };
            card.Controls.Add(_nudAfkMicSec);
            AddText(card, "seconds of inactivity", 192, nudY + 4, 8f, TXT4);

            // Description
            int descY = nudY + 32;
            AddText(card, "When no keyboard or mouse input is detected for this duration,", 64, descY, 7f, TXT4);
            AddText(card, "your microphone will be muted to prevent accidental audio.", 64, descY + 14, 7f, TXT4);



            y = descY + 36;
            AddLine(card, y); y += 12;

            // ── SECTION 2: Speaker Fade ─────────────────────────────────
            _afkSpkY = y;
            _tglAfkSpk = Tgl("Fade Speaker Volume", null, y, card);
            AddText(card, "Gradually reduce speaker volume when you're away.", 64, y + 20, 7.5f, TXT3);

            // Timeout row
            int nudY2 = y + 48;
            AddText(card, "Timeout:", 64, nudY2 + 4, 8f, TXT3);
            _nudAfkSpkSec = new PaddedNumericUpDown {
                Minimum = 10, Maximum = 3600, Value = Math.Max(10, Math.Min(3600, _settings.AfkSpeakerMuteSec)),
                Size = Dpi.Size(56, 24), Location = Dpi.Pt(130, nudY2),
                BackColor = Color.FromArgb(24, 24, 24), ForeColor = TXT,
                Font = new Font("Segoe UI", 9f), BorderStyle = BorderStyle.None,
                TextAlign = HorizontalAlignment.Center
            };
            card.Controls.Add(_nudAfkSpkSec);
            AddText(card, "seconds of inactivity", 192, nudY2 + 4, 8f, TXT4);

            // Description
            int descY2 = nudY2 + 32;
            AddText(card, "Speaker volume will fade to zero over a few seconds,", 64, descY2, 7f, TXT4);
            AddText(card, "then restore instantly when you return.", 64, descY2 + 14, 7f, TXT4);

            y = descY2 + 40;

            // ── Wire toggles and NUDs ───────────────────────────────────
            _loading = true;
            _tglAfkMic.Checked = _settings.AfkMicMuteEnabled;
            _tglAfkSpk.Checked = _settings.AfkSpeakerMuteEnabled;
            _loading = false;

            _tglAfkMic.CheckedChanged += (s, e) => {
                if (_loading) return;
                _audio.AfkMicEnabled = _tglAfkMic.Checked;
                _pttWarningText = BuildPttWarningText(); if (_pttCard != null) _pttCard.Invalidate();
                card.Invalidate();
            };
            _tglAfkSpk.CheckedChanged += (s, e) => {
                if (_loading) return;
                _audio.AfkSpeakerEnabled = _tglAfkSpk.Checked;
                RefreshSpkWarning();
                card.Invalidate();
            };
            _nudAfkMicSec.ValueChanged += (s, e) => {
                _audio.AfkMicSec = (int)_nudAfkMicSec.Value;
                _pttWarningText = BuildPttWarningText(); if (_pttCard != null) _pttCard.Invalidate();
            };
            _nudAfkSpkSec.ValueChanged += (s, e) => {
                _audio.AfkSpeakerSec = (int)_nudAfkSpkSec.Value;
                RefreshSpkWarning();
            };

            // Dim NUDs when toggle is off
            _nudAfkMicSec.Enabled = _tglAfkMic.Checked;
            _nudAfkSpkSec.Enabled = _tglAfkSpk.Checked;
            _tglAfkMic.CheckedChanged += (s, e) => { if (!_loading) _nudAfkMicSec.Enabled = _tglAfkMic.Checked; };
            _tglAfkSpk.CheckedChanged += (s, e) => { if (!_loading) _nudAfkSpkSec.Enabled = _tglAfkSpk.Checked; };

            card.Dock = DockStyle.Top;
            card.Height = Dpi.S(y);
            pane.Controls.Add(card);
        }
        private int _afkSpkY; // stored for paint handler

        void BuildGeneralPane(Panel pane) {
            var card = MakeCard(7, "General", "Startup behavior, notifications, and legal information.");
            _generalCard = card;
            // Card glass top is at y=46, separator below this section at y=96.
            int y = 56;
            _tglStartup = Tgl("Start with Windows", null, y, card);
            _tglStartup.CheckedChanged += (s,e) => { if (!_loading) { _audio.StartWithWindows = _tglStartup.Checked; } };
            // Run Setup Wizard button — same row as Start with Windows, right-aligned
            int bwY = y;
            var bw = new Button{Text="Run Splash!",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(120,24),ForeColor=TXT2,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f),TabStop=false};
            bw.FlatAppearance.BorderColor=INPUT_BDR; bw.Click+=(s,e)=>{
                var tray = TrayApp.Instance;
                if (tray != null) tray.RunSplashFromOptions();
            };
            bw.MouseEnter+=(s,e)=>bw.BackColor=Color.FromArgb(36,36,36);
            bw.MouseLeave+=(s,e)=>bw.BackColor=Color.FromArgb(20,20,20);
            card.Controls.Add(bw);
            // Check for Updates button — same row, to the left of Run Setup Wizard, blue theme
            var btnUpdate = new Button{Text="Check for Updates",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(155,24),ForeColor=ACC,BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false};
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
            _tglRestrictSound = Tgl("Silent Mode: Mute All Sound Effects", "Disables all sound feedback, PTT clicks, dictation sounds, and Windows voice typing sounds", y, card);
            _tglRestrictSound.CheckedChanged += (s,e) => { if (!_loading) { _audio.RestrictSoundOutput = _tglRestrictSound.Checked; ApplySoundRestriction(); } };
            y += 42;
            _tglOverlay = Tgl("Show Mic Status Overlay", "Floating pill shows mic open/closed \u2014 drag to reposition, right-click to dismiss", y, card);
            _tglOverlay.CheckedChanged += (s,e) => { if (!_loading) { _audio.MicOverlayEnabled = _tglOverlay.Checked; } };
            y += 42;
            _tglNotifyCorr = Tgl("Volume Correction Alerts", "Show a toast when Angry Audio resets your audio.", y, card);
            _tglNotifyCorr.CheckedChanged += (s,e) => { if (!_loading) { _audio.NotifyOnCorrection = _tglNotifyCorr.Checked; } };
            y += 42;
            _tglNotifyDev = Tgl("Device Change Alerts", "Notify when a new mic or speaker is detected.", y, card);
            _tglNotifyDev.CheckedChanged += (s,e) => { if (!_loading) { _audio.NotifyOnDeviceChange = _tglNotifyDev.Checked; } };
            y += 42; AddLine(card, y); y += 16;

            card.Dock = DockStyle.Top;
            card.Height = Dpi.S(y);
            pane.Controls.Add(card);

            // Legal & Open Source — separate card docked to BOTTOM of pane
            var legalCard = MakeCard(0, "Legal & Open Source", "Licenses and acknowledgments for third-party software.");
            int ly = 56;
            AddText(legalCard, "This application uses the following open-source software:", 16, ly, 8f, TXT); ly += 28;

            AddText(legalCard, "CSCore", 16, ly, 8.5f, ACC, FontStyle.Bold); ly += 18;
            AddText(legalCard, "MS-PL License - Copyright (c) Florian", 16, ly, 7.5f, TXT3); ly += 26;

            AddText(legalCard, "Equalizer APO", 16, ly, 8.5f, ACC, FontStyle.Bold); ly += 18;
            AddText(legalCard, "GNU GPL v2 - Copyright (c) 2012 jonas-the-lemur", 16, ly, 7.5f, TXT3); ly += 26;

            AddText(legalCard, "NAudio", 16, ly, 8.5f, ACC, FontStyle.Bold); ly += 18;
            AddText(legalCard, "MIT License - Copyright (c) 2020 Mark Heath", 16, ly, 7.5f, TXT3); ly += 26;

            AddText(legalCard, "System.Text.Json", 16, ly, 8.5f, ACC, FontStyle.Bold); ly += 18;
            AddText(legalCard, "MIT License - Copyright (c) Microsoft Corporation", 16, ly, 7.5f, TXT3); ly += 26;

            AddText(legalCard, "Whisper.net / OpenAI Whisper", 16, ly, 8.5f, ACC, FontStyle.Bold); ly += 18;
            AddText(legalCard, "MIT License - Copyright (c) 2022 OpenAI & 2024 OpenAI", 16, ly, 7.5f, TXT3); ly += 42;

            AddText(legalCard, "© 2026 Andrew Ganter. All Rights Reserved.", 16, ly, 8f, TXT); ly += 18;
            AddText(legalCard, "Unauthorized copying, modification, or distribution prohibited.", 16, ly, 7.5f, TXT3); ly += 24;

            legalCard.Dock = DockStyle.Bottom;
            legalCard.Height = Dpi.S(ly + 10);
            pane.Controls.Add(legalCard);
        }

        // =====================================================================
        //  PANE 5: DISPLAY (Color Temperature + Brightness)
        // =====================================================================

        private ToggleSwitch _tglDisplay;
        private ToggleSwitch _tglBlueLightFilter, _tglLockForefront;
        private ToggleSwitch[] _tglColorFilter = new ToggleSwitch[6]; // index = Windows FilterType
        private SlickSlider _trkTempK, _trkBright, _trkIntensity, _trkColorBoost;
        private CardSlider _csTempK, _csBright, _csIntensity, _csColorBoost;
        private PaintedLabel _lblTempK, _lblBright, _lblIntensity, _lblColorBoost;
        private int _displayMonitorIdx = -1; // -1 = all monitors
        private List<DisplayManager.MonitorInfo> _displayMonitors;
        private Panel _displayMonitorTabBar;

        void BuildDisplayPane(Panel pane) {
            var card = MakeCard(8, "Display", "Adjust color temperature, brightness, and color filters.", "Alt+P");
            _displayCard = card;
            if (!_cardSliderMap.ContainsKey(card)) _cardSliderMap[card] = new List<CardSlider>();

            // ── Nav buttons in card header (right-aligned) ──────────────────
            // ◀ Alt+Q  and  ▶ Alt+E  cycle through all presets + filters
            Action<bool> navCycle = (fwd) => {
                var tray = TrayApp.Instance;
                if (tray != null) { tray.CycleDisplay(fwd); }
                else             { CycleDisplayPreset(); } // fallback
            };
            int hdrH = Dpi.S(50);
            int navW = Dpi.S(62); int navH = Dpi.S(26); int navY = (hdrH - navH) / 2;
            // ▶ Forward (Alt+E)
            var btnNext = new Button {
                Text = "Alt+E \u25BA", FlatStyle = FlatStyle.Flat,
                Size = new Size(navW, navH),
                Location = new Point(card.Width - Dpi.S(8) - navW, navY),
                ForeColor = ACC, BackColor = Color.FromArgb(ACC.R/12, ACC.G/12, ACC.B/12),
                Font = new Font("Segoe UI", 7.5f), TabStop = false, Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnNext.FlatAppearance.BorderColor = Color.FromArgb(ACC.R/4, ACC.G/4, ACC.B/4);
            btnNext.FlatAppearance.BorderSize = 1;
            if (_mmTip != null) _mmTip.SetToolTip(btnNext, "Next preset/filter  (Alt+E)");
            btnNext.Click += (s, e) => navCycle(true);
            btnNext.MouseEnter += (s,e) => btnNext.BackColor = Color.FromArgb(ACC.R/7, ACC.G/7, ACC.B/7);
            btnNext.MouseLeave += (s,e) => btnNext.BackColor = Color.FromArgb(ACC.R/12, ACC.G/12, ACC.B/12);
            card.Controls.Add(btnNext);
            // ◀ Backward (Alt+Q)
            var btnPrev = new Button {
                Text = "\u25C4 Alt+Q", FlatStyle = FlatStyle.Flat,
                Size = new Size(navW, navH),
                Location = new Point(card.Width - Dpi.S(8) - navW * 2 - Dpi.S(4), navY),
                ForeColor = ACC, BackColor = Color.FromArgb(ACC.R/12, ACC.G/12, ACC.B/12),
                Font = new Font("Segoe UI", 7.5f), TabStop = false, Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnPrev.FlatAppearance.BorderColor = Color.FromArgb(ACC.R/4, ACC.G/4, ACC.B/4);
            btnPrev.FlatAppearance.BorderSize = 1;
            if (_mmTip != null) _mmTip.SetToolTip(btnPrev, "Previous preset/filter  (Alt+Q)");
            btnPrev.Click += (s, e) => navCycle(false);
            btnPrev.MouseEnter += (s,e) => btnPrev.BackColor = Color.FromArgb(ACC.R/7, ACC.G/7, ACC.B/7);
            btnPrev.MouseLeave += (s,e) => btnPrev.BackColor = Color.FromArgb(ACC.R/12, ACC.G/12, ACC.B/12);
            card.Controls.Add(btnPrev);
            // ── End nav buttons ─────────────────────────────────────────────
            int y = 56;

            // â”€â”€ Master toggle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            _tglDisplay = Tgl("Display Adjustments", "Apply color temperature + brightness to your monitors", y, card);
            _tglDisplay.CheckedChanged += (s,e) => {
                if (_loading) return;
                _settings.DisplayEnabled = _tglDisplay.Checked;
                if (_tglDisplay.Checked) {
                    ApplyDisplaySettings();
                } else {
                    _loading = true;
                    _trkTempK.Value  = 65;
                    _trkBright.Value = 100;
                    if (_tglBlueLightFilter != null) _tglBlueLightFilter.Checked = false;
                    for (int j = 0; j < 6; j++) if (_tglColorFilter[j] != null) _tglColorFilter[j].Checked = false;
                    _loading = false;
                    _lblTempK.Text  = "6500K";
                    _lblBright.Text = "100%";
                    _settings.DisplayTempK     = 6500;
                    _settings.DisplayBrightness = 100;
                    _settings.DisplayFilterType = -1;
                    _settings.DisplayPreset     = "Normal";
                    card.Invalidate();
                    DisplayManager.ResetToNormal();
                    RefreshDisplayUI();
                }
                UpdateDisplayGating();
                _settings.Save();
            };
            y += 44;

            // â”€â”€ Monitor tabs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            _displayMonitors = DisplayManager.GetMonitors();
            _displayMonitorTabBar = new Panel { Location = Dpi.Pt(16, y), Size = Dpi.Size(420, 28), BackColor = Color.Transparent };
            card.Controls.Add(_displayMonitorTabBar);
            BuildMonitorTabs();
            card.Resize += (s2, e2) => { _displayMonitorTabBar.Width = card.Width - Dpi.S(32); };
            y += 30;

            AddLine(card, y); y += 10;

            // â”€â”€ 4 SLIDERS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

            // Color Temperature
            AddText(card, "COLOR TEMPERATURE", 16, y, 7.5f, TXT3, FontStyle.Bold);
            _lblTempK = AddText(card, _settings.DisplayTempK + "K", 310, y, 8f, ACC, FontStyle.Bold);
            y += 22;
            AddText(card, "Warm", 16, y+4, 7f, TXT4); AddText(card, "Cool", 280, y+4, 7f, TXT4);
            _trkTempK = new SlickSlider { Minimum = 12, Maximum = 65, Value = _settings.DisplayTempK / 100, Location = Dpi.Pt(50, y-2), Size = Dpi.Size(220, 30) };
            card.Controls.Add(_trkTempK); _trkTempK.Visible = false;
            _csTempK = new CardSlider { PixelX = Dpi.S(50), PixelY = Dpi.S(y-2), PixelW = Dpi.S(220), PixelH = Dpi.S(30), Source = _trkTempK, Card = card };
            _cardSliderMap[card].Add(_csTempK);
            _trkTempK.ValueChanged += (s,e) => { int k = _trkTempK.Value * 100; _lblTempK.Text = k + "K"; card.Invalidate(); if (_loading) return; _settings.DisplayTempK = k; };
            _trkTempK.DragCompleted += (s,e) => { if (!_tglDisplay.Checked) { _loading = true; _tglDisplay.Checked = true; _loading = false; _settings.DisplayEnabled = true; } ApplyDisplaySettings(); _settings.Save(); };
            y += 36;

            // Brightness
            AddText(card, "BRIGHTNESS", 16, y, 7.5f, TXT3, FontStyle.Bold);
            _lblBright = AddText(card, _settings.DisplayBrightness + "%", 310, y, 8f, ACC, FontStyle.Bold);
            y += 22;
            AddText(card, "Dim", 16, y+4, 7f, TXT4); AddText(card, "Full", 280, y+4, 7f, TXT4);
            _trkBright = new SlickSlider { Minimum = 20, Maximum = 100, Value = _settings.DisplayBrightness, Location = Dpi.Pt(50, y-2), Size = Dpi.Size(220, 30) };
            card.Controls.Add(_trkBright); _trkBright.Visible = false;
            _csBright = new CardSlider { PixelX = Dpi.S(50), PixelY = Dpi.S(y-2), PixelW = Dpi.S(220), PixelH = Dpi.S(30), Source = _trkBright, Card = card };
            _cardSliderMap[card].Add(_csBright);
            _trkBright.ValueChanged += (s,e) => { _lblBright.Text = _trkBright.Value + "%"; card.Invalidate(); if (_loading) return; _settings.DisplayBrightness = _trkBright.Value; };
            _trkBright.DragCompleted += (s,e) => { if (!_tglDisplay.Checked) { _loading = true; _tglDisplay.Checked = true; _loading = false; _settings.DisplayEnabled = true; } ApplyDisplaySettings(); _settings.Save(); };
            y += 36;

            // Filter Intensity
            AddText(card, "FILTER INTENSITY", 16, y, 7.5f, TXT3, FontStyle.Bold);
            _lblIntensity = AddText(card, _settings.DisplayColorIntensity + "%", 310, y, 8f, ACC, FontStyle.Bold);
            y += 22;
            AddText(card, "Low", 16, y+4, 7f, TXT4); AddText(card, "High", 280, y+4, 7f, TXT4);
            _trkIntensity = new SlickSlider { Minimum = 0, Maximum = 100, Value = _settings.DisplayColorIntensity, Location = Dpi.Pt(50, y-2), Size = Dpi.Size(220, 30) };
            card.Controls.Add(_trkIntensity); _trkIntensity.Visible = false;
            _csIntensity = new CardSlider { PixelX = Dpi.S(50), PixelY = Dpi.S(y-2), PixelW = Dpi.S(220), PixelH = Dpi.S(30), Source = _trkIntensity, Card = card };
            _cardSliderMap[card].Add(_csIntensity);
            _trkIntensity.ValueChanged += (s,e) => { _lblIntensity.Text = _trkIntensity.Value + "%"; card.Invalidate(); if (_loading) return; _settings.DisplayColorIntensity = _trkIntensity.Value; };
            _trkIntensity.DragCompleted += (s,e) => { _settings.Save(); if (_tglDisplay.Checked) ApplyDisplaySettings(); };
            y += 36;

            // Color Boost
            AddText(card, "COLOR BOOST", 16, y, 7.5f, TXT3, FontStyle.Bold);
            _lblColorBoost = AddText(card, _settings.DisplayColorBoost + "%", 310, y, 8f, ACC, FontStyle.Bold);
            y += 22;
            AddText(card, "Low", 16, y+4, 7f, TXT4); AddText(card, "High", 280, y+4, 7f, TXT4);
            _trkColorBoost = new SlickSlider { Minimum = 0, Maximum = 100, Value = _settings.DisplayColorBoost, Location = Dpi.Pt(50, y-2), Size = Dpi.Size(220, 30) };
            card.Controls.Add(_trkColorBoost); _trkColorBoost.Visible = false;
            _csColorBoost = new CardSlider { PixelX = Dpi.S(50), PixelY = Dpi.S(y-2), PixelW = Dpi.S(220), PixelH = Dpi.S(30), Source = _trkColorBoost, Card = card };
            _cardSliderMap[card].Add(_csColorBoost);
            _trkColorBoost.ValueChanged += (s,e) => { _lblColorBoost.Text = _trkColorBoost.Value + "%"; card.Invalidate(); if (_loading) return; _settings.DisplayColorBoost = _trkColorBoost.Value; };
            _trkColorBoost.DragCompleted += (s,e) => { _settings.Save(); if (_tglDisplay.Checked) ApplyDisplaySettings(); };
            y += 36;

            AddLine(card, y); y += 14;

            // â”€â”€ 8 TOGGLES â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

            // Blue Light Filter (toggle 1)
            _tglBlueLightFilter = Tgl("Blue Light Filter", "Warm 3800K overlay reduces eye strain at night", y, card);
            _tglBlueLightFilter.CheckedChanged += (s,e) => {
                if (_loading) return;
                _loading = true;
                string selectedDev = SelectedDeviceName();
                
                if (_tglBlueLightFilter.Checked) {
                    // Turn off all color filters (mutual exclusivity)
                    for (int j = 0; j < 6; j++) if (_tglColorFilter[j] != null) _tglColorFilter[j].Checked = false;
                    _settings.DisplayFilterType = -1;
                    
                    if (string.IsNullOrEmpty(selectedDev)) {
                        if (_trkTempK != null) _trkTempK.Value = 38;
                        _settings.DisplayTempK = 3800;
                    } else {
                        SetMonitorOverride(selectedDev, 3800, _trkBright != null ? _trkBright.Value : 100);
                        if (_trkTempK != null) _trkTempK.Value = 38;
                    }
                    
                    _settings.DisplayEnabled = true;
                    if (!_tglDisplay.Checked) _tglDisplay.Checked = true;
                } else {
                    if (string.IsNullOrEmpty(selectedDev)) {
                        if (_trkTempK != null) _trkTempK.Value = 65;
                        _settings.DisplayTempK = 6500;
                    } else {
                        SetMonitorOverride(selectedDev, 6500, _trkBright != null ? _trkBright.Value : 100);
                        if (_trkTempK != null) _trkTempK.Value = 65;
                    }
                }
                _settings.DisplayPreset = ""; // deselect any active preset
                foreach (Control c in card.Controls) if (c is Button) c.Invalidate();
                _loading = false;
                DisplayManager.ApplyColorFilter(_settings.DisplayFilterType,
                    _settings.DisplayColorIntensity, _settings.DisplayColorBoost);
                _settings.Save();
                if (_tglDisplay.Checked) ApplyDisplaySettings();
            };
            y += 40;

            // 6 Color Filter toggles (toggles 2-7)
            string[] cfHotkeys = { "", "", "", "", "", "" };
            for (int fi = 0; fi < 6; fi++) {
                int fIdx = fi;
                _tglColorFilter[fi] = Tgl(DisplayManager.FilterNames[fi], DisplayManager.FilterDescriptions[fi], y, card, cfHotkeys[fi]);
                _tglColorFilter[fi].CheckedChanged += (s, e) => {
                    if (_loading) return;
                    _loading = true;
                    if (_tglColorFilter[fIdx].Checked) {
                        // Turn off all other color filters and Blue Light (mutual exclusivity)
                        for (int j = 0; j < 6; j++) if (j != fIdx && _tglColorFilter[j] != null) _tglColorFilter[j].Checked = false;
                        if (_tglBlueLightFilter != null) _tglBlueLightFilter.Checked = false;
                        _settings.DisplayFilterType = fIdx;
                        _settings.DisplayEnabled    = true;
                        if (!_tglDisplay.Checked) _tglDisplay.Checked = true;
                    } else {
                        _settings.DisplayFilterType = -1;
                    }
                    _settings.DisplayPreset = ""; // deselect preset
                    foreach (Control c in card.Controls) if (c is Button) c.Invalidate();
                    _loading = false;
                    DisplayManager.ApplyColorFilter(_settings.DisplayFilterType,
                        _settings.DisplayColorIntensity, _settings.DisplayColorBoost);
                    _settings.Save();
                };
                y += 40;
            }

            // Lock to Forefront (toggle 8)
            _tglLockForefront = Tgl("Lock to Forefront", "Keep this window on top while adjusting", y, card);
            _tglLockForefront.CheckedChanged += (s,e) => { if (_loading) return; TopMost = _tglLockForefront.Checked; };
            y += 40;

            AddLine(card, y); y += 14;

            // â”€â”€ PRESETS â€” all 6 on one row â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            AddText(card, "PRESETS", 16, y, 7.5f, TXT3, FontStyle.Bold); y += 24;

            // 6 presets, 1 row. Each button width = (card - margin*2 - gaps*5) / 6
            // At design-time card is ~450 logical px wide. Use fixed 62px each with 6px gap.
            // 6 presets, 1 row. No individual hotkeys â€” Alt+P cycles through all.
            // Buttons are narrower without hotkey subtext (use 56px each, 5px gap).
            int bW  = Dpi.S(54); int bH  = Dpi.S(38);
            int bGX = Dpi.S(5);
            int bStartX = Dpi.S(16); int bStartY = Dpi.S(y);
            for (int p = 0; p < DisplayManager.PresetNames.Length; p++) {
                string preset = DisplayManager.PresetNames[p];
                string desc   = DisplayManager.PresetDescs[p];
                var btn = new Button {
                    Text = "", FlatStyle = FlatStyle.Flat,
                    Size = new Size(bW, bH),
                    Location = new Point(bStartX + p * (bW + bGX), bStartY),
                    BackColor = Color.FromArgb(ACC.R/8, ACC.G/8, ACC.B/8),
                    TabStop = false
                };
                btn.FlatAppearance.BorderColor = Color.FromArgb(ACC.R/3, ACC.G/3, ACC.B/3);
                btn.FlatAppearance.BorderSize  = 1;
                string cP = preset;
                btn.Paint += (s2, pe) => {
                    bool act = _settings.DisplayPreset == cP;
                    Color tgt = act ? Color.FromArgb(ACC.R/3, ACC.G/3, ACC.B/3) : Color.FromArgb(ACC.R/8, ACC.G/8, ACC.B/8);
                    if (btn.BackColor != tgt) btn.BackColor = tgt;
                    pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    pe.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    using (var f = new Font("Segoe UI", 7.5f, act ? FontStyle.Bold : FontStyle.Regular)) {
                        var sz = pe.Graphics.MeasureString(cP, f);
                        using (var b = new SolidBrush(act ? Color.White : ACC))
                            pe.Graphics.DrawString(cP, f, b, (btn.Width - sz.Width) / 2f, (btn.Height - sz.Height) / 2f);
                    }
                };
                btn.Click += (s2,e2) => {
                    _settings.DisplayPreset = cP;
                    int tK; int br; int ft; int intensity; int colorBoost;
                    DisplayManager.GetPresetValues(cP, out tK, out br, out ft, out intensity, out colorBoost);
                    
                    // If a specific monitor is selected, ONLY apply Preset TempK/Brightness to that monitor
                    string selectedDev = SelectedDeviceName();
                    
                    _loading = true;
                    if (string.IsNullOrEmpty(selectedDev)) {
                        _settings.DisplayTempK = tK; _settings.DisplayBrightness = br;
                        _trkTempK.Value = tK / 100; _trkBright.Value = br;
                        _lblTempK.Text = tK + "K"; _lblBright.Text = br + "%";
                    } else {
                        SetMonitorOverride(selectedDev, tK, br);
                        _trkTempK.Value = tK / 100; _trkBright.Value = br;
                        _lblTempK.Text = tK + "K"; _lblBright.Text = br + "%";
                    }
                    
                    // Filters are global
                    _settings.DisplayFilterType = ft;
                    for (int j = 0; j < 6; j++) if (_tglColorFilter[j] != null) _tglColorFilter[j].Checked = (ft == j);
                    if (_tglBlueLightFilter != null) _tglBlueLightFilter.Checked = false;
                    
                    if (!_tglDisplay.Checked) _tglDisplay.Checked = true;
                    _loading = false;
                    
                    foreach (Control ctrl in card.Controls) if (ctrl is Button) ctrl.Invalidate();
                    _settings.Save();
                    ApplyDisplaySettings();
                };
                btn.MouseEnter += (s2,e2) => { if (_settings.DisplayPreset != cP) btn.BackColor = Color.FromArgb(ACC.R/5, ACC.G/5, ACC.B/5); };
                btn.MouseLeave += (s2,e2) => { if (_settings.DisplayPreset != cP) btn.BackColor = Color.FromArgb(ACC.R/8, ACC.G/8, ACC.B/8); };
                card.Controls.Add(btn);
                if (_mmTip != null) _mmTip.SetToolTip(btn, desc);
            }
            // Cycle (Alt+P) button -- advances through presets
            var btnCycle = new Button {
                Text = "\u25B6", FlatStyle = FlatStyle.Flat,
                Size = new Size(Dpi.S(36), bH),
                Location = new Point(bStartX + 6 * (bW + bGX), bStartY),
                ForeColor = ACC, BackColor = Color.FromArgb(ACC.R/10, ACC.G/10, ACC.B/10),
                Font = new Font("Segoe UI", 9f), TabStop = false
            };
            btnCycle.FlatAppearance.BorderColor = Color.FromArgb(ACC.R/3, ACC.G/3, ACC.B/3);
            btnCycle.FlatAppearance.BorderSize  = 1;
            if (_mmTip != null) _mmTip.SetToolTip(btnCycle, "Cycle preset (Alt+P)");
            btnCycle.Click += (s,e) => CycleDisplayPreset();
            btnCycle.MouseEnter += (s,e) => btnCycle.BackColor = Color.FromArgb(ACC.R/6, ACC.G/6, ACC.B/6);
            btnCycle.MouseLeave += (s,e) => btnCycle.BackColor = Color.FromArgb(ACC.R/10, ACC.G/10, ACC.B/10);
            card.Controls.Add(btnCycle);
            // Reset button -- to the right of cycle button
            var btnReset = new Button {
                Text = "\u21BA", FlatStyle = FlatStyle.Flat,
                Size = new Size(Dpi.S(30), bH),
                Location = new Point(bStartX + 6 * (bW + bGX) + Dpi.S(40), bStartY),
                ForeColor = Color.FromArgb(160,160,160), BackColor = Color.FromArgb(22, 22, 22),
                Font = new Font("Segoe UI", 11f), TabStop = false
            };
            btnReset.FlatAppearance.BorderColor = Color.FromArgb(44, 44, 44);
            btnReset.FlatAppearance.BorderSize  = 1;
            if (_mmTip != null) _mmTip.SetToolTip(btnReset, "Reset to Day (default)");
            btnReset.Click += (s,e) => {
                _loading = true;
                string selectedDev = SelectedDeviceName();
                
                if (string.IsNullOrEmpty(selectedDev)) {
                    _trkTempK.Value  = 65; _trkBright.Value = 100;
                    for (int j = 0; j < 6; j++) if (_tglColorFilter[j] != null) _tglColorFilter[j].Checked = false;
                    if (_tglBlueLightFilter != null) _tglBlueLightFilter.Checked = false;
                    _settings.DisplayTempK = 6500; _settings.DisplayBrightness = 100;
                    _settings.DisplayFilterType = -1; _settings.DisplayMonitorSettings = "";
                    _settings.DisplayPreset = "Day";
                    _lblTempK.Text = "6500K"; _lblBright.Text = "100%";
                    DisplayManager.ResetToNormal();
                } else {
                    SetMonitorOverride(selectedDev, 6500, 100);
                    _trkTempK.Value = 65; _trkBright.Value = 100;
                    _lblTempK.Text = "6500K"; _lblBright.Text = "100%";
                    // Do not reset global filters or preset text
                }
                
                _loading = false;
                card.Invalidate(); _settings.Save();
                ApplyDisplaySettings(); RefreshDisplayUI();
            };
            btnReset.MouseEnter += (s,e) => btnReset.BackColor = Color.FromArgb(36, 36, 36);
            btnReset.MouseLeave += (s,e) => btnReset.BackColor = Color.FromArgb(22, 22, 22);
            card.Controls.Add(btnReset);
            y += 38 + 8;

            card.Dock   = DockStyle.Top;
            card.Height = Dpi.S(y + 10);
            pane.Controls.Add(card);

            if (_settings.DisplayEnabled) {
                BeginInvoke((MethodInvoker)(() => ApplyDisplaySettings()));
            }
            UpdateDisplayGating(); // set initial enabled/disabled state
        }
        void BuildMonitorTabs() {
            if (_displayMonitorTabBar == null) return;
            if (_displayMonitors == null) return;   // null-guard
            _displayMonitorTabBar.Controls.Clear();
            var monitors = _displayMonitors;
            int tabX = 0;
            var allLabels = new List<string> { "All Monitors" };
            foreach (var m in monitors) allLabels.Add(m.FriendlyName ?? m.DeviceName ?? "Monitor");
            for (int i = 0; i < allLabels.Count; i++) {
                int capturedIdx = i - 1; // -1 = all, 0..n-1 = specific monitor
                string capturedDevName = (i == 0 || i - 1 >= monitors.Count) ? null : monitors[i - 1].DeviceName;
                bool isActive = _displayMonitorIdx == capturedIdx;
                string label = allLabels[i];
                if (label.Length > 18) label = label.Substring(0, 17) + "\u2026";
                var tab = new Button {
                    Text = label,
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(Math.Max(Dpi.S(80), TextRenderer.MeasureText(label, new Font("Segoe UI", 8f)).Width + Dpi.S(14)), Dpi.S(26)),
                    Location = new Point(tabX, 0),
                    ForeColor = isActive ? Color.White : TXT3,
                    BackColor = isActive ? Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3) : Color.FromArgb(24,24,24),
                    Font = new Font("Segoe UI", 8f, isActive ? FontStyle.Bold : FontStyle.Regular),
                    TabStop = false
                };
                tab.FlatAppearance.BorderColor = Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
                tab.Click += (s, e) => {
                    _displayMonitorIdx = capturedIdx;
                    // Load this monitor's saved temp/brightness into the sliders
                    int tK = GetMonitorTempK(capturedDevName);
                    int br = GetMonitorBrightness(capturedDevName);
                    _loading = true;
                    if (_trkTempK != null) _trkTempK.Value = Math.Max(12, Math.Min(65, tK / 100));
                    if (_trkBright != null) _trkBright.Value = Math.Max(20, Math.Min(100, br));
                    if (_lblTempK != null) _lblTempK.Text = tK + "K";
                    if (_lblBright != null) _lblBright.Text = br + "%";
                    if (_tglBlueLightFilter != null) _tglBlueLightFilter.Checked = (tK == 3800 && _settings.DisplayFilterType < 0 && _settings.DisplayEnabled);
                    _loading = false;
                    // Defer tab rebuild — Controls.Clear() must not run inside a click handler of those controls
                    if (_displayMonitorTabBar != null && !_displayMonitorTabBar.IsDisposed)
                        BeginInvoke((MethodInvoker)BuildMonitorTabs);
                };
                _displayMonitorTabBar.Controls.Add(tab);
                tabX += tab.Width + Dpi.S(4);
            }
        }

        /// <summary>Get saved tempK for a specific device name (null=all).</summary>
        int GetMonitorTempK(string devName) {
            if (string.IsNullOrEmpty(devName)) return _settings.DisplayTempK;
            var ov = ParseMonitorOverride(devName);
            return ov.HasValue ? ov.Value.Key : 6500; // neutral default per monitor
        }
        int GetMonitorBrightness(string devName) {
            if (string.IsNullOrEmpty(devName)) return _settings.DisplayBrightness;
            var ov = ParseMonitorOverride(devName);
            return ov.HasValue ? ov.Value.Value : 100; // neutral default per monitor
        }
        /// <summary>Parse "devName:tempK:brightness" entry from DisplayMonitorSettings.</summary>
        System.Collections.Generic.KeyValuePair<int,int>? ParseMonitorOverride(string devName) {
            string raw = _settings.DisplayMonitorSettings ?? "";
            foreach (string part in raw.Split('|')) {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int c1 = p.IndexOf(':');            // first colon separates devName from tempK
                if (c1 <= 0) continue;
                int c2 = p.IndexOf(':', c1 + 1);   // second colon separates tempK from brightness
                if (c2 <= c1) continue;
                string name = p.Substring(0, c1);
                if (!string.Equals(name, devName, StringComparison.OrdinalIgnoreCase)) continue;
                int tk, br;
                int.TryParse(p.Substring(c1 + 1, c2 - c1 - 1), out tk);
                int.TryParse(p.Substring(c2 + 1), out br);
                return new System.Collections.Generic.KeyValuePair<int,int>(tk > 0 ? tk : 6500, br > 0 ? br : 100);
            }
            return null;
        }
        void SetMonitorOverride(string devName, int tempK, int brightness) {
            var parts = new List<string>();
            string raw = _settings.DisplayMonitorSettings ?? "";
            bool found = false;
            foreach (string part in raw.Split('|')) {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int c1 = p.IndexOf(':');
                bool match = c1 > 0 && string.Equals(p.Substring(0, c1), devName, StringComparison.OrdinalIgnoreCase);
                if (match) { parts.Add(devName + ":" + tempK + ":" + brightness); found = true; }
                else parts.Add(p);
            }
            if (!found) parts.Add(devName + ":" + tempK + ":" + brightness);
            _settings.DisplayMonitorSettings = string.Join("|", parts.ToArray());
        }
        string SelectedDeviceName() {
            if (_displayMonitorIdx < 0 || _displayMonitors == null || _displayMonitorIdx >= _displayMonitors.Count)
                return null;
            return _displayMonitors[_displayMonitorIdx].DeviceName;
        }

        void ApplyDisplaySettings() {
            string devName   = SelectedDeviceName();
            int tempK        = _settings.DisplayTempK;
            int brightness   = _settings.DisplayBrightness;
            int filterType   = _settings.DisplayFilterType;
            int intensity    = _settings.DisplayColorIntensity;
            int colorBoost   = _settings.DisplayColorBoost;
            if (!string.IsNullOrEmpty(devName)) {
                SetMonitorOverride(devName, tempK, brightness);
                _settings.Save();
            }
            DisplayManager.ApplyAll(tempK, brightness, filterType, intensity, colorBoost, devName);

            // Re-apply saved overrides for other monitors so they aren't lost when global base is reset
            if (string.IsNullOrEmpty(devName)) {
                string raw = _settings.DisplayMonitorSettings ?? "";
                foreach (string part in raw.Split('|')) {
                    string p = part.Trim();
                    if (p.Length == 0) continue;
                    int c1 = p.IndexOf(':');
                    int c2 = p.IndexOf(':', c1 + 1);
                    if (c1 > 0 && c2 > c1) {
                        string mName = p.Substring(0, c1);
                        int tk, br;
                        int.TryParse(p.Substring(c1 + 1, c2 - c1 - 1), out tk);
                        int.TryParse(p.Substring(c2 + 1), out br);
                        if (tk > 0 && br > 0) {
                            ScreenOverlay.Apply(tk, br, mName);
                        }
                    }
                }
            }
        }

        void UpdateDisplayGating() {
            bool on = _tglDisplay != null && _tglDisplay.Checked;
            if (_tglBlueLightFilter != null) _tglBlueLightFilter.Enabled = on;
            for (int j = 0; j < 6; j++) if (_tglColorFilter[j] != null) _tglColorFilter[j].Enabled = on;
            if (_tglLockForefront != null) _tglLockForefront.Enabled = on;
            if (_trkIntensity  != null) _trkIntensity.Enabled  = on;
            if (_trkColorBoost != null) _trkColorBoost.Enabled = on;
        }

        public void CycleDisplayPreset() {
            var names = DisplayManager.PresetNames;
            string cur = _settings.DisplayPreset ?? "";
            int idx = System.Array.IndexOf(names, cur);
            string next = names[(idx + 1) % names.Length];
            // Simulate clicking that preset
            _settings.DisplayPreset = next;
            int tK; int br; int ft; int intensity; int colorBoost;
            DisplayManager.GetPresetValues(next, out tK, out br, out ft, out intensity, out colorBoost);
            
            string selectedDev = SelectedDeviceName();
            _loading = true;
            
            if (string.IsNullOrEmpty(selectedDev)) {
                _settings.DisplayTempK = tK; _settings.DisplayBrightness = br;
                if (_trkTempK  != null) _trkTempK.Value  = tK / 100;
                if (_trkBright != null) _trkBright.Value = br;
                if (_lblTempK  != null) _lblTempK.Text  = tK + "K";
                if (_lblBright != null) _lblBright.Text = br + "%";
            } else {
                SetMonitorOverride(selectedDev, tK, br);
                if (_trkTempK  != null) _trkTempK.Value  = tK / 100;
                if (_trkBright != null) _trkBright.Value = br;
                if (_lblTempK  != null) _lblTempK.Text  = tK + "K";
                if (_lblBright != null) _lblBright.Text = br + "%";
            }
            
            _settings.DisplayFilterType = ft;
            for (int j = 0; j < 6; j++) if (_tglColorFilter[j] != null) _tglColorFilter[j].Checked = (ft == j);
            if (_tglBlueLightFilter != null) _tglBlueLightFilter.Checked = false;
            if (_tglDisplay != null && !_tglDisplay.Checked) _tglDisplay.Checked = true;
            _loading = false;

            if (_displayCard != null) { foreach (Control ctrl in _displayCard.Controls) if (ctrl is Button) ctrl.Invalidate(); }
            _settings.Save();
            ApplyDisplaySettings();
        }

        // Dictate pane toggle fields
        private ToggleSwitch _tglDictPushToDict, _tglDictToggle;
        private DarkComboBox _cmbDictEngine, _cmbDictModel;
        private Button _btnAddDictPth2, _btnRmDictPth1, _btnRmDictPth2;
        private Button _btnAddDictPtt2, _btnRmDictPtt1, _btnRmDictPtt2;
        // Dictation page icon/dropdown/slider references (need class-level access for OnCaptureComplete)
        private CardIcon _dictPthOvr, _dictPthSnd, _dictPttOvr, _dictPttSnd;
        private CardIcon _duckDictPth, _duckDictPtt; // rubber duck icons for Dictation page
        private CardIcon _keyDictPth, _keyDictPtt; // key icons for Dictation suppression
        private Panel _dictPthDrop, _dictPttDrop;
        private CardSlider _dictPthVol, _dictPttVol;
        // Audio ducking fields
        private ToggleSwitch _tglDuck;
        private SlickSlider _trkDuckVol, _trkPttDuckVol, _trkSysVol;
        private PaintedLabel _lblDuckPct;
        private CardSlider _csDuckVol, _csPttDuckVol, _csSysVol;
        private PaintedLabel _lblDuckTitle, _lblDuckSub, _lblDuckTglTitle, _lblDuckTglSub, _lblDuckTo, _lblPttDuckTo, _lblSysVol;

        void BuildDictPane(Panel pane) {
            var card = MakeCard(6, "Dictation", "Type anywhere with your voice \u2014 no cloud, no subscription.", "Alt+[");
            _dictCard = card;
            if (!_cardSliderMap.ContainsKey(card)) _cardSliderMap[card] = new List<CardSlider>();

            // ── ENGINE  y=56 ──────────────────────────────────────────────────
            int y = 56;
            AddText(card, "ENGINE", 16, y + 6, 7.5f, TXT3, FontStyle.Bold);
            _cmbDictEngine = new DarkComboBox { Size = Dpi.Size(190, 26), Location = Dpi.Pt(70, y) };
            _cmbDictEngine.Items.Add("Windows (Built-in)");
            _cmbDictEngine.Items.Add("Whisper AI (CPU)");
            if (DictationManager.HasNvidiaCuda())
                _cmbDictEngine.Items.Add("NVIDIA AI (GPU)");
            _cmbDictEngine.SelectedIndex = Math.Max(0, Math.Min(_cmbDictEngine.Items.Count - 1, _settings.DictationEngine));
            card.Controls.Add(_cmbDictEngine);

            // ── MODEL SIZE (visible only for Whisper engines) ────────────────
            y += 34;
            var plModelLbl = AddText(card, "MODEL", 16, y + 6, 7.5f, TXT3, FontStyle.Bold);
            _cmbDictModel = new DarkComboBox { Size = Dpi.Size(190, 26), Location = Dpi.Pt(70, y) };
            for (int mi = 0; mi < DictationManager.ModelCount; mi++)
                _cmbDictModel.Items.Add(DictationManager.WhisperModelLabels[mi]);
            _cmbDictModel.SelectedIndex = Math.Max(0, Math.Min(DictationManager.ModelCount - 1, _settings.DictationWhisperModel));
            card.Controls.Add(_cmbDictModel);

            // Description label below dropdown — updates on selection
            var plModelDesc = AddText(card, DictationManager.WhisperModelDescs[_cmbDictModel.SelectedIndex], 70, y + 28, 7f, TXT3);

            bool whisperInitM = DictationManager.IsWhisperEngine(_settings.DictationEngine);
            plModelLbl.Visible = whisperInitM;
            _cmbDictModel.Visible = whisperInitM;
            plModelDesc.Visible = whisperInitM;

            // Silent background download — user picks a model and it just works
            _cmbDictModel.SelectedIndexChanged += (s, e) => {
                if (_loading) return;
                int idx = _cmbDictModel.SelectedIndex;
                _audio.DictationWhisperModel = idx;
                plModelDesc.Text = DictationManager.WhisperModelDescs[idx];
                // Silently download in background if not already present
                if (!DictationManager.IsModelDownloaded(idx))
                    System.Threading.ThreadPool.QueueUserWorkItem(_ => DictationManager.DownloadWhisperModelWithProgress(idx, null));
            };

            // Also silently pre-download engine binaries when engine is selected
            int initEngine = _settings.DictationEngine;
            if (initEngine == 2 && !DictationManager.IsCudaExeReady())
                System.Threading.ThreadPool.QueueUserWorkItem(_ => DictationManager.DownloadCudaExeInBackground());
            // Pre-download selected model if needed
            if (DictationManager.IsWhisperEngine(initEngine) && !DictationManager.IsModelDownloaded(_settings.DictationWhisperModel))
                System.Threading.ThreadPool.QueueUserWorkItem(_ => DictationManager.DownloadWhisperModelWithProgress(_settings.DictationWhisperModel, null));

            y += 48;
            AddLine(card, y); y += 10;
            // y ≈ 134

            // Push-to-Hold icons (right side) — eye, sound, dropdown, volume
            // Pre-declare Push-to-Toggle vars so resize handler closure can capture them
            int yPtt2 = 0;
            int yPthIcons = 0;

            // Handle resizing to keep dropdowns top-right of the segment, like PttPane does
            card.Resize += (s2, e2) => {
                int rEdge = card.Width - Dpi.S(16);
                // Push-to-Hold Row 1: Key(-256), Duck(-222), Eye(-188), Sound(-154), Dropdown(-116)
                if (_keyDictPth  != null) { _keyDictPth.PixelX  = rEdge - Dpi.S(256); _keyDictPth.PixelY  = Dpi.S(yPthIcons); }
                if (_duckDictPth != null) { _duckDictPth.PixelX = rEdge - Dpi.S(222); _duckDictPth.PixelY = Dpi.S(yPthIcons); }
                if (_dictPthOvr  != null) { _dictPthOvr.PixelX  = rEdge - Dpi.S(188); _dictPthOvr.PixelY  = Dpi.S(yPthIcons); }
                if (_dictPthSnd  != null) { _dictPthSnd.PixelX  = rEdge - Dpi.S(154); _dictPthSnd.PixelY  = Dpi.S(yPthIcons); }
                if (_dictPthDrop != null) _dictPthDrop.Location = new Point(rEdge - Dpi.S(116), Dpi.S(yPthIcons));
                // Push-to-Hold Row 2 (+26): Slider only
                if (_dictPthVol  != null) { _dictPthVol.PixelX  = rEdge - Dpi.S(116); _dictPthVol.PixelY  = Dpi.S(yPthIcons + 26); }

                // Push-to-Toggle Row 1: Key(-256), Duck(-222), Eye(-188), Sound(-154), Dropdown(-116)
                if (_keyDictPtt  != null) { _keyDictPtt.PixelX  = rEdge - Dpi.S(256); _keyDictPtt.PixelY  = Dpi.S(yPtt2); }
                if (_duckDictPtt != null) { _duckDictPtt.PixelX = rEdge - Dpi.S(222); _duckDictPtt.PixelY = Dpi.S(yPtt2); }
                if (_dictPttOvr  != null) { _dictPttOvr.PixelX  = rEdge - Dpi.S(188); _dictPttOvr.PixelY  = Dpi.S(yPtt2); }
                if (_dictPttSnd  != null) { _dictPttSnd.PixelX  = rEdge - Dpi.S(154); _dictPttSnd.PixelY  = Dpi.S(yPtt2); }
                if (_dictPttDrop != null) _dictPttDrop.Location = new Point(rEdge - Dpi.S(116), Dpi.S(yPtt2));
                // Push-to-Toggle Row 2 (+26): Slider only
                if (_dictPttVol  != null) { _dictPttVol.PixelX  = rEdge - Dpi.S(116); _dictPttVol.PixelY  = Dpi.S(yPtt2 + 26); }
            };

            bool whisperInit = _settings.DictationEngine > 0;
            int ki = 64;  // hotkey label x-indent
            Color DIM   = Color.FromArgb(80, 80, 80);
            Color RED   = Color.FromArgb(220, 60, 60);

            // ═════════════════════════════════════════════════════════════════
            // SECTION 1: Push-to-Hold
            //   Always shown. Grayed + red note when Windows engine selected.
            // ═════════════════════════════════════════════════════════════════
            _tglDictPushToDict = new ToggleSwitch { Location = Dpi.Pt(16, y) };
            // PaintParentBg removed
            card.Controls.Add(_tglDictPushToDict);
            _tglDictPushToDict.Visible = false;
            var ctPth = new CardToggle { PixelX = Dpi.S(16), PixelY = Dpi.S(y), Source = _tglDictPushToDict, Card = card };
            if (!_cardToggleMap.ContainsKey(card)) _cardToggleMap[card] = new List<CardToggle>();
            _cardToggleMap[card].Add(ctPth);


            _tglDictPushToDict.CheckedChanged += (s,e) => {
                if (_loading) return;
                if (_tglDictPushToDict.Checked && (IsCapturingKey || CaptureCooldownActive())) { _loading=true; _tglDictPushToDict.Checked=false; _loading=false; return; }
                // When turning ON with no key: start capture (mutual exclusion deferred to OnCaptureComplete)
                if (_tglDictPushToDict.Checked && _dictKeyCode <= 0) {
                    _loading=true; _tglDictPushToDict.Checked=false; _loading=false; BeginCapture(CaptureTarget.DictationKey1, _lblDictKey); return;
                }

                if (!_tglDictPushToDict.Checked) {
                    CancelAllCaptures();
                    _audio.DictationKey = 0; _audio.DictationKey2 = 0;
                    _dictKeyCode = 0; _dictKeyCode2 = 0;
                    if (_lblDictKey != null) { _lblDictKey.Text = "Add Key"; _lblDictKey.BackColor = INPUT_BG; _lblDictKey.ForeColor = ACC; }
                    CompactKeys();
                } else {
                    // Mutual exclusion: turn off Push-to-Toggle when Push-to-Hold is enabled
                    if (_tglDictToggle != null && _tglDictToggle.Checked) {
                        _loading=true; _tglDictToggle.Checked=false; _loading=false;
                        _audio.DictationToggleKey = 0; _audio.DictationToggleKey2 = 0;
                        _dictToggleKeyCode = 0; _dictToggleKeyCode2 = 0;
                        if (_lblDictToggleKey != null) { _lblDictToggleKey.Text = "Add Key"; _lblDictToggleKey.BackColor = INPUT_BG; _lblDictToggleKey.ForeColor = ACC; }
                        _audio.DictationToggleEnabled = false;
                        CompactKeys();
                    }
                }
                _audio.DictationEnabled = _tglDictPushToDict.Checked;
                // Dim PTH icons when toggle is off
                _dictPthOvr.Dimmed = !_tglDictPushToDict.Checked; _dictPthSnd.Dimmed = !_tglDictPushToDict.Checked;
                if (_keyDictPth != null) _keyDictPth.Dimmed = !_tglDictPushToDict.Checked;
                if (_tglDictPushToDict.Checked) {
                    _dictPthOvr.Checked = true;
                    _dictPthSnd.Checked = false; // Speaker OFF by default — user clicks to enable
                    if (_duckDictPth != null) { _duckDictPth.Checked = true; _audio.DictPthDuckEnabled = true; }
                }
                bool pthSndOn = _dictPthSnd != null && _dictPthSnd.Checked;
                if (_dictPthDrop != null) _dictPthDrop.Enabled = _tglDictPushToDict.Checked && pthSndOn;
                if (_dictPthVol != null) _dictPthVol.Source.Enabled = _tglDictPushToDict.Checked && pthSndOn;
                // Also dim killed PTT section if mutual exclusion fired
                if (_tglDictToggle != null && !_tglDictToggle.Checked) {
                    if (_dictPttOvr != null) _dictPttOvr.Dimmed = true; if (_dictPttSnd != null) _dictPttSnd.Dimmed = true;
                    if (_dictPttDrop != null) _dictPttDrop.Enabled = false;
                    if (_dictPttVol != null) _dictPttVol.Source.Enabled = false;
                }
                RefreshDuckingSection();
                EnsureMicAccess();
                card.Invalidate();
            };

            // Push-to-Hold hotkey row (supports 2 keys)
            int hkPthY = y + 46;
            _baseDictHldY = hkPthY;
            int kbW2 = 80; int rmW2 = 18; int kGap = 4;
            var plPthHk = AddText(card, "Hotkey:", ki, hkPthY + 4, 8f, TXT3);
            _lblDictKey = new Label {
                Text = _dictKeyCode > 0 ? KeyName(_dictKeyCode) : "Add Key",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = ACC, BackColor = INPUT_BG,
                Size = Dpi.Size(kbW2, 26), TextAlign = ContentAlignment.MiddleCenter,
                Location = Dpi.Pt(ki + 50, hkPthY)
            };
            AttachKeyStyling(_lblDictKey);
            _lblDictKey.Click += (s2,e2) => {
                // Mutual exclusion deferred to OnCaptureComplete — don't kill toggle during Press...
                BeginCapture(CaptureTarget.DictationKey1, _lblDictKey);
            };
            card.Controls.Add(_lblDictKey);

            bool hoverRmPth = false;
            var btnRmPthKey = new Button { Text="", FlatStyle=FlatStyle.Flat, Size=Dpi.Size(rmW2,rmW2), BackColor=Color.Transparent,
                Location=Dpi.Pt(ki+50+kbW2+kGap, hkPthY+2), TabStop=false, Visible=_dictKeyCode>0 };
            btnRmPthKey.FlatAppearance.BorderSize=0; btnRmPthKey.FlatAppearance.MouseOverBackColor=Color.Transparent; btnRmPthKey.FlatAppearance.MouseDownBackColor=Color.Transparent;
            btnRmPthKey.MouseEnter+=(s2,e2)=>{ hoverRmPth=true; btnRmPthKey.Invalidate(); }; btnRmPthKey.MouseLeave+=(s2,e2)=>{ hoverRmPth=false; btnRmPthKey.Invalidate(); };
            btnRmPthKey.Paint+=(s2,e2)=>PaintRemoveIcon(e2.Graphics,btnRmPthKey.ClientRectangle,hoverRmPth);
            btnRmPthKey.Click+=(s2,e2)=>{ if (_dictKeyCode2 > 0) { _dictKeyCode = _dictKeyCode2; _dictKeyCode2 = 0; } else { _dictKeyCode = 0; _loading=true; _tglDictPushToDict.Checked=false; _loading=false; _audio.DictationEnabled=false; _dictPthOvr.Dimmed=true; _dictPthSnd.Dimmed=true; if(_dictPthDrop!=null)_dictPthDrop.Enabled=false; if(_dictPthVol!=null)_dictPthVol.Source.Enabled=false; } _audio.DictationKey = _dictKeyCode; _audio.DictationKey2 = _dictKeyCode2; CompactKeys(); if (_dictCard != null) _dictCard.Invalidate(); };
            card.Controls.Add(btnRmPthKey);

            // Key2 for push-to-hold
            _lblDictKey2 = new Label {
                Text = _dictKeyCode2 > 0 ? KeyName(_dictKeyCode2) : "",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = ACC, BackColor = INPUT_BG,
                Size = Dpi.Size(kbW2, 26), TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            AttachKeyStyling(_lblDictKey2);
            _lblDictKey2.Click += (s2,e2) => BeginCapture(CaptureTarget.DictationKey2, _lblDictKey2);
            card.Controls.Add(_lblDictKey2);

            bool hoverRmPth2 = false;
            var btnRmPthKey2 = new Button { Text="", FlatStyle=FlatStyle.Flat, Size=Dpi.Size(rmW2,rmW2), BackColor=Color.Transparent, TabStop=false, Visible=false };
            btnRmPthKey2.FlatAppearance.BorderSize=0; btnRmPthKey2.FlatAppearance.MouseOverBackColor=Color.Transparent; btnRmPthKey2.FlatAppearance.MouseDownBackColor=Color.Transparent;
            btnRmPthKey2.MouseEnter+=(s2,e2)=>{ hoverRmPth2=true; btnRmPthKey2.Invalidate(); }; btnRmPthKey2.MouseLeave+=(s2,e2)=>{ hoverRmPth2=false; btnRmPthKey2.Invalidate(); };
            btnRmPthKey2.Paint+=(s2,e2)=>PaintRemoveIcon(e2.Graphics,btnRmPthKey2.ClientRectangle,hoverRmPth2);
            btnRmPthKey2.Click+=(s2,e2)=>{ _dictKeyCode2 = 0; _audio.DictationKey2 = _dictKeyCode2; CompactKeys(); if (_dictCard != null) _dictCard.Invalidate(); };
            card.Controls.Add(btnRmPthKey2);

            // Add-key buttons
            _btnAddDictPth2 = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=Color.FromArgb(50, 205, 50),BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Visible=false};
            _btnAddDictPth2.FlatAppearance.BorderColor=Color.FromArgb(16, 68, 16);
            _btnAddDictPth2.MouseEnter += (s,e) => { _btnAddDictPth2.BackColor=Color.FromArgb(10, 41, 10); };
            _btnAddDictPth2.MouseLeave += (s,e) => { _btnAddDictPth2.BackColor=Color.FromArgb(20,20,20); };
            _btnAddDictPth2.Click += (s,e) => BeginCapture(CaptureTarget.DictationKey2, _lblDictKey2);
            card.Controls.Add(_btnAddDictPth2);
            
            _btnRmDictPth1 = btnRmPthKey; _btnRmDictPth2 = btnRmPthKey2; // Store reference so LayoutDictKeys can control it

            var plPthTitle = AddText(card, "Push-to-Hold",                                          64, y,    10f,  TXT,  FontStyle.Bold);
            var plPthSub   = AddText(card, "Hold the key to dictate, release to stop.",              64, y+20, 7.5f, TXT3);
            yPthIcons = y; // FIX: Capture dynamic y for icons BEFORE y advances

            _dictPthOvr  = MakeOverlayCheck(yPthIcons, card, _settings.DictShowOverlay,   (v) => { _audio.DictShowOverlay   = v; });
            _duckDictPth = MakeDuckCheck(yPthIcons, card, _settings.DictPthDuckEnabled, (v) => { _audio.DictPthDuckEnabled = v; RefreshDuckingSection(); ShowDuckToast(_dictKeyCode > 0 ? PushToTalk.GetKeyName(_dictKeyCode) : "Dictation", v); });
            _keyDictPth  = MakeKeyCheck(yPthIcons, card, _settings.DictPthSuppressEnabled, (v) => { _audio.DictPthSuppressEnabled = v; ShowKeyToast(_dictKeyCode > 0 ? PushToTalk.GetKeyName(_dictKeyCode) : "Dictation", v); });
            _dictPthSnd  = MakeSoundCheck  (yPthIcons, card, _settings.DictSoundFeedback, (v) => {
                _audio.DictSoundFeedback = v;
                if (_dictPthDrop != null) _dictPthDrop.Enabled = v;
                if (_dictPthVol != null) _dictPthVol.Source.Enabled = v;
                if (_dictCard != null) _dictCard.Invalidate();
            });
            _dictPthSnd.Checked = false; // Speaker OFF by default — user clicks to enable
            _dictPthDrop = MakeSoundDropdown(yPthIcons, card, _settings.SoundFeedbackType, (v) => { _audio.SoundFeedbackType = v; }, () => _settings.DictSoundVolume);
            _dictPthVol  = MakeVolumeSlider(yPthIcons, card, _settings.DictSoundVolume, (v) => { _audio.DictSoundVolume = v; });
            
            // Local aliases for closures
            var pthOvr = _dictPthOvr; var pthSnd = _dictPthSnd; var pthDrop = _dictPthDrop; var pthVolSlider = _dictPthVol;

            // Red warning label for Windows engine (shown in place of hotkey row)
            var lblPthWinWarn = new Label {
                Text = "Windows built-in does not support push-to-hold.",
                Font = new Font("Segoe UI", 8f), ForeColor = RED, BackColor = Color.Transparent,
                AutoSize = false, Size = Dpi.Size(320, 18),
                Location = Dpi.Pt(ki, hkPthY + 5), Visible = !whisperInit
            };
            card.Controls.Add(lblPthWinWarn);

            // Helper: set PTH row enabled/dimmed state based on engine
            Action<bool> setPthEnabled = (enabled) => {
                Color tc = enabled ? TXT  : DIM;
                Color sc = enabled ? TXT3 : DIM;
                plPthTitle.Color = tc; plPthSub.Color = sc;
                plPthHk.Color    = sc;
                _lblDictKey.Enabled  = enabled;
                btnRmPthKey.Enabled  = enabled;
                pthOvr.Dimmed  = !enabled; pthSnd.Dimmed = !enabled;
                if (_keyDictPth != null) _keyDictPth.Dimmed = !enabled;
                if (pthDrop != null) pthDrop.Enabled = enabled;
                ctPth.Dimmed = !enabled;
                // Hotkey row vs warning label
                plPthHk.Visible          = enabled;
                _lblDictKey.Visible      = enabled;
                if (!enabled && _lblDictKey2 != null) _lblDictKey2.Visible = false;
                if (!enabled && _btnAddDictPth2 != null) _btnAddDictPth2.Visible = false;
                btnRmPthKey.Visible      = enabled && _dictKeyCode > 0;
                lblPthWinWarn.Visible    = !enabled;
                // Properly disable toggle if engine unsupported
                if (_tglDictPushToDict != null) {
                    if (!enabled) { _loading = true; _tglDictPushToDict.Checked = false; _loading = false; }
                    _tglDictPushToDict.Enabled = enabled;
                }
                card.Invalidate();
            };
            setPthEnabled(whisperInit);

            y = hkPthY + 34;
            AddLine(card, y); y += 10;
            // Push-to-Toggle icons (right side) — eye, sound, dropdown, volume
            yPtt2 = y;
            _dictPttOvr  = MakeOverlayCheck(y, card, _settings.DictShowOverlay, (v) => { _audio.DictShowOverlay = v; });
            _duckDictPtt = MakeDuckCheck(y, card, _settings.DictPttDuckEnabled, (v) => { _audio.DictPttDuckEnabled = v; RefreshDuckingSection(); ShowDuckToast(_dictToggleKeyCode > 0 ? PushToTalk.GetKeyName(_dictToggleKeyCode) : "Dict Toggle", v); });
            _keyDictPtt  = MakeKeyCheck(y, card, _settings.DictPttSuppressEnabled, (v) => { _audio.DictPttSuppressEnabled = v; ShowKeyToast(_dictToggleKeyCode > 0 ? PushToTalk.GetKeyName(_dictToggleKeyCode) : "Dict Toggle", v); });
            _dictPttSnd  = MakeSoundCheck  (y, card, _settings.DictSoundFeedback, (v) => {
                _audio.DictSoundFeedback = v;
                if (_dictPttDrop != null) _dictPttDrop.Enabled = v;
                if (_dictPttVol != null) _dictPttVol.Source.Enabled = v;
                if (_dictCard != null) _dictCard.Invalidate();
            });
            _dictPttSnd.Checked = false; // Speaker OFF by default — user clicks to enable
            _dictPttDrop = MakeSoundDropdown(y, card, _settings.SoundFeedbackType, (v) => { _audio.SoundFeedbackType = v; }, () => _settings.DictSoundVolume);
            _dictPttVol  = MakeVolumeSlider(y, card, _settings.DictSoundVolume, (v) => { _audio.DictSoundVolume = v; });
            var pttOvr = _dictPttOvr; var pttSnd = _dictPttSnd; var pttDrop = _dictPttDrop; var pttVolSlider = _dictPttVol;
            // Initial dim — both sections start dimmed unless toggle is on; sound always off by default
            pttOvr.Dimmed = !_settings.DictationToggleEnabled; pttSnd.Dimmed = !_settings.DictationToggleEnabled;
            if (pttDrop != null) pttDrop.Enabled = false;
            if (pttVolSlider != null) pttVolSlider.Source.Enabled = false;
            pthOvr.Dimmed = !_settings.DictationEnabled; pthSnd.Dimmed = !_settings.DictationEnabled;
            if (pthDrop != null) pthDrop.Enabled = false;
            if (pthVolSlider != null) pthVolSlider.Source.Enabled = false;

            // ═════════════════════════════════════════════════════════════════
            // SECTION 2: Push-to-Toggle (both engines)
            // ═════════════════════════════════════════════════════════════════
            _tglDictToggle = new ToggleSwitch { Location = Dpi.Pt(16, y) };
            // PaintParentBg removed
            card.Controls.Add(_tglDictToggle);
            _tglDictToggle.Visible = false;
            var ctPtt = new CardToggle { PixelX = Dpi.S(16), PixelY = Dpi.S(y), Source = _tglDictToggle, Card = card };
            _cardToggleMap[card].Add(ctPtt);

            AddText(card, "Push-to-Toggle",                                          64, y,    10f,  TXT,  FontStyle.Bold);
            AddText(card, "Tap to start dictating, tap again to stop.",              64, y+20, 7.5f, TXT3);
            
            _tglDictToggle.CheckedChanged += (s,e) => {
                if (_loading) return;
                if (_tglDictToggle.Checked && (IsCapturingKey || CaptureCooldownActive())) { _loading=true; _tglDictToggle.Checked=false; _loading=false; return; }
                // When turning ON with no key: start capture (mutual exclusion deferred to OnCaptureComplete)
                if (_tglDictToggle.Checked && _dictToggleKeyCode <= 0) {
                    _loading=true; _tglDictToggle.Checked=false; _loading=false; BeginCapture(CaptureTarget.DictationToggleKey, _lblDictToggleKey); return;
                }

                if (!_tglDictToggle.Checked) {
                    CancelAllCaptures();
                    _audio.DictationToggleKey = 0; _audio.DictationToggleKey2 = 0;
                    _dictToggleKeyCode = 0; _dictToggleKeyCode2 = 0;
                    if (_lblDictToggleKey != null) { _lblDictToggleKey.Text = "Add Key"; _lblDictToggleKey.BackColor = INPUT_BG; _lblDictToggleKey.ForeColor = ACC; }
                    CompactKeys();
                } else {
                    // Mutual exclusion: turn off Push-to-Hold when Push-to-Toggle is enabled
                    if (_tglDictPushToDict != null && _tglDictPushToDict.Checked) {
                        _loading=true; _tglDictPushToDict.Checked=false; _loading=false;
                        _audio.DictationKey = 0; _audio.DictationKey2 = 0;
                        _dictKeyCode = 0; _dictKeyCode2 = 0;
                        if (_lblDictKey != null) { _lblDictKey.Text = "Add Key"; _lblDictKey.BackColor = INPUT_BG; _lblDictKey.ForeColor = ACC; }
                        _audio.DictationEnabled = false;
                        CompactKeys();
                    }
                }
                _audio.DictationToggleEnabled = _tglDictToggle.Checked;
                // Dim Push-to-Toggle icons
                _dictPttOvr.Dimmed = !_tglDictToggle.Checked; _dictPttSnd.Dimmed = !_tglDictToggle.Checked;
                if (_keyDictPtt != null) _keyDictPtt.Dimmed = !_tglDictToggle.Checked;
                if (_tglDictToggle.Checked) {
                    _dictPttOvr.Checked = true;
                    _dictPttSnd.Checked = false; // Speaker OFF by default — user clicks to enable
                    if (_duckDictPtt != null) { _duckDictPtt.Checked = true; _audio.DictPttDuckEnabled = true; }
                }
                bool pttSndOn = _dictPttSnd != null && _dictPttSnd.Checked;
                if (_dictPttDrop != null) _dictPttDrop.Enabled = _tglDictToggle.Checked && pttSndOn;
                if (_dictPttVol != null) _dictPttVol.Source.Enabled = _tglDictToggle.Checked && pttSndOn;
                
                // Also dim killed PTH section if mutual exclusion fired
                if (_tglDictPushToDict != null && !_tglDictPushToDict.Checked) {
                    if (_dictPthOvr != null) _dictPthOvr.Dimmed = true; if (_dictPthSnd != null) _dictPthSnd.Dimmed = true;
                    if (_dictPthDrop != null) _dictPthDrop.Enabled = false;
                    if (_dictPthVol != null) _dictPthVol.Source.Enabled = false;
                }
                RefreshDuckingSection();
                EnsureMicAccess();
                card.Invalidate();
            };


            // Push-to-Toggle hotkey row (supports 2 keys)
            int hkPttY = y + 46;
            _baseDictTogY = hkPttY;
            int tkbW = 80; int trmW = 18;
            AddText(card, "Hotkey:", ki, hkPttY + 4, 8f, TXT3);
            _lblDictToggleKey = new Label {
                Text = _dictToggleKeyCode > 0 ? KeyName(_dictToggleKeyCode) : "Add Key",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = ACC, BackColor = INPUT_BG,
                Size = Dpi.Size(tkbW, 26), TextAlign = ContentAlignment.MiddleCenter,
                Location = Dpi.Pt(ki + 50, hkPttY)
            };
            AttachKeyStyling(_lblDictToggleKey);
            _lblDictToggleKey.Click += (s2,e2) => {
                // Mutual exclusion deferred to OnCaptureComplete — don't kill push-to-hold during Press...
                BeginCapture(CaptureTarget.DictationToggleKey, _lblDictToggleKey);
            };
            card.Controls.Add(_lblDictToggleKey);

            bool hoverRmPtt = false;
            var btnRmPttKey = new Button { Text="", FlatStyle=FlatStyle.Flat, Size=Dpi.Size(trmW,trmW), BackColor=Color.Transparent, TabStop=false, Visible=false };
            btnRmPttKey.FlatAppearance.BorderSize=0; btnRmPttKey.FlatAppearance.MouseOverBackColor=Color.Transparent; btnRmPttKey.FlatAppearance.MouseDownBackColor=Color.Transparent;
            btnRmPttKey.MouseEnter+=(s2,e2)=>{ hoverRmPtt=true; btnRmPttKey.Invalidate(); }; btnRmPttKey.MouseLeave+=(s2,e2)=>{ hoverRmPtt=false; btnRmPttKey.Invalidate(); };
            btnRmPttKey.Paint+=(s2,e2)=>PaintRemoveIcon(e2.Graphics,btnRmPttKey.ClientRectangle,hoverRmPtt);
            btnRmPttKey.Click+=(s2,e2)=>{ if (_dictToggleKeyCode2>0) { _dictToggleKeyCode=_dictToggleKeyCode2; _dictToggleKeyCode2=0; } else { _dictToggleKeyCode=0; _loading=true; _tglDictToggle.Checked=false; _loading=false; _audio.DictationToggleEnabled=false; _dictPttOvr.Dimmed=true; _dictPttSnd.Dimmed=true; if(_dictPttDrop!=null)_dictPttDrop.Enabled=false; if(_dictPttVol!=null)_dictPttVol.Source.Enabled=false; } _audio.DictationToggleKey=_dictToggleKeyCode; _audio.DictationToggleKey2 = _dictToggleKeyCode2; CompactKeys(); if (_dictCard != null) _dictCard.Invalidate(); };
            card.Controls.Add(btnRmPttKey);

            // Key2 for push-to-toggle
            _lblDictToggleKey2 = new Label {
                Text = _dictToggleKeyCode2 > 0 ? KeyName(_dictToggleKeyCode2) : "",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = ACC, BackColor = INPUT_BG,
                Size = Dpi.Size(tkbW, 26), TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            AttachKeyStyling(_lblDictToggleKey2);
            _lblDictToggleKey2.Click += (s2,e2) => BeginCapture(CaptureTarget.DictationToggleKey2, _lblDictToggleKey2);
            card.Controls.Add(_lblDictToggleKey2);

            bool hoverRmPtt2 = false;
            var btnRmPttKey2 = new Button { Text="", FlatStyle=FlatStyle.Flat, Size=Dpi.Size(trmW,trmW), BackColor=Color.Transparent, TabStop=false, Visible=false };
            btnRmPttKey2.FlatAppearance.BorderSize=0; btnRmPttKey2.FlatAppearance.MouseOverBackColor=Color.Transparent; btnRmPttKey2.FlatAppearance.MouseDownBackColor=Color.Transparent;
            btnRmPttKey2.MouseEnter+=(s2,e2)=>{ hoverRmPtt2=true; btnRmPttKey2.Invalidate(); }; btnRmPttKey2.MouseLeave+=(s2,e2)=>{ hoverRmPtt2=false; btnRmPttKey2.Invalidate(); };
            btnRmPttKey2.Paint+=(s2,e2)=>PaintRemoveIcon(e2.Graphics,btnRmPttKey2.ClientRectangle,hoverRmPtt2);
            btnRmPttKey2.Click+=(s2,e2)=>{ _dictToggleKeyCode2=0; _audio.DictationToggleKey2=_dictToggleKeyCode2; CompactKeys(); if (_dictCard != null) _dictCard.Invalidate(); };
            card.Controls.Add(btnRmPttKey2);
            
            Color gC2 = Color.FromArgb(50, 205, 50);
            _btnAddDictPtt2 = new Button{Text="Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(56,26),ForeColor=gC2,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Visible=false};
            _btnAddDictPtt2.FlatAppearance.BorderColor=Color.FromArgb(gC2.R/3,gC2.G/3,gC2.B/3);
            _btnAddDictPtt2.MouseEnter += (s,e) => { _btnAddDictPtt2.BackColor=Color.FromArgb(gC2.R/5,gC2.G/5,gC2.B/5); };
            _btnAddDictPtt2.MouseLeave += (s,e) => { _btnAddDictPtt2.BackColor=Color.FromArgb(20,20,20); };
            _btnAddDictPtt2.Click += (s,e) => BeginCapture(CaptureTarget.DictationToggleKey2, _lblDictToggleKey2);
            card.Controls.Add(_btnAddDictPtt2);
            
            _btnRmDictPtt1 = btnRmPttKey; _btnRmDictPtt2 = btnRmPttKey2; // Store reference so LayoutDictKeys can control it


            // Handled dynamically via LayoutDictKeys()

            // ── Engine combo handler ──────────────────────────────────────────
            _cmbDictEngine.SelectedIndexChanged += (s,e) => {
                if (_loading) return;
                bool isNonWindows = _cmbDictEngine.SelectedIndex > 0;
                bool isWhisper = DictationManager.IsWhisperEngine(_cmbDictEngine.SelectedIndex);
                // Reset both dictation toggles FIRST (suppress events during cleanup)
                _audio.BeginUpdate();
                if (_tglDictPushToDict != null && _tglDictPushToDict.Checked) {
                    _loading=true; _tglDictPushToDict.Checked=false; _loading=false;
                    _audio.DictationKey = 0; _audio.DictationKey2 = 0;
                    _dictKeyCode = 0; _dictKeyCode2 = 0;
                    if (_lblDictKey != null) { _lblDictKey.Text = "Add Key"; _lblDictKey.BackColor = INPUT_BG; _lblDictKey.ForeColor = ACC; }
                    _audio.DictationEnabled = false;
                }
                if (_tglDictToggle != null && _tglDictToggle.Checked) {
                    _loading=true; _tglDictToggle.Checked=false; _loading=false;
                    _audio.DictationToggleKey = 0; _audio.DictationToggleKey2 = 0;
                    _dictToggleKeyCode = 0; _dictToggleKeyCode2 = 0;
                    if (_lblDictToggleKey != null) { _lblDictToggleKey.Text = "Add Key"; _lblDictToggleKey.BackColor = INPUT_BG; _lblDictToggleKey.ForeColor = ACC; }
                    _audio.DictationToggleEnabled = false;
                }
                _audio.EndUpdate();
                // Set engine LAST — fires SettingsChange.Dictation with toggles already OFF
                // This ensures TrayApp sees anyDictOn=false → calls _dictationManager.Stop()
                _audio.DictationEngine = _cmbDictEngine.SelectedIndex;
                CompactKeys();
                setPthEnabled(isNonWindows);
                // Show/hide model row (only for Whisper engines)
                plModelLbl.Visible = isWhisper;
                _cmbDictModel.Visible = isWhisper;
                plModelDesc.Visible = isWhisper;
                // Silently pre-download engine binaries when engine changes
                int selEng = _cmbDictEngine.SelectedIndex;
                if (selEng == 2 && !DictationManager.IsCudaExeReady())
                    System.Threading.ThreadPool.QueueUserWorkItem(__ => DictationManager.DownloadCudaExeInBackground());
                if (isWhisper && !DictationManager.IsModelDownloaded(_cmbDictModel.SelectedIndex))
                    System.Threading.ThreadPool.QueueUserWorkItem(__ => DictationManager.DownloadWhisperModelWithProgress(_cmbDictModel.SelectedIndex, null));
                if (_dictCard != null) _dictCard.Invalidate();
            };

            // ═════════════════════════════════════════════════════════════════
            // SECTION 3: Audio Ducking
            // ═════════════════════════════════════════════════════════════════
            y = hkPttY + 34;
            AddLine(card, y); y += 10;

            _lblDuckTitle = AddText(card, "Audio Ducking", 16, y, 10f, TXT, FontStyle.Bold);
            y += 20;
            _lblDuckSub = AddText(card, "Lower system volume while speaking or dictating.", 16, y, 7.5f, TXT3);
            y += 28;

            // Toggle: enable/disable ducking
            _tglDuck = Tgl("Lower System Volume", "Reduces speaker volume when dictation is active.", y, card);
            // Store the toggle's labels for dimming (Tgl adds 2 labels: title at index -2, sub at index -1)
            { var lbls = _cardLabels[card]; _lblDuckTglTitle = lbls[lbls.Count - 2]; _lblDuckTglSub = lbls[lbls.Count - 1]; }
            _tglDuck.Checked = _settings.DictDuckingEnabled;
            _tglDuck.CheckedChanged += (s2, e2) => { if (!_loading) { _audio.DictDuckingEnabled = _tglDuck.Checked; if (_tglDuck.Checked) { if (_trkDuckVol != null) { _trkDuckVol.Value = 20; _audio.DictDuckingVolume = 20; } if (_trkPttDuckVol != null) { _trkPttDuckVol.Value = 20; _audio.PttDuckingVolume = 20; } } if (_duckLockIcon != null) _duckLockIcon.Checked = _tglDuck.Checked; } };
            y += 44;

            // "Volume:" — read-only system volume slider
            _lblSysVol = AddText(card, "Volume:", 16, y + 4, 8f, TXT2);
            _trkSysVol = new SlickSlider { Minimum = 0, Maximum = 100, Value = 0, Location = Dpi.Pt(120, y - 4), Size = Dpi.Size(180, 30) };
            card.Controls.Add(_trkSysVol);
            _trkSysVol.Visible = false;
            if (!_cardSliderMap.ContainsKey(card)) _cardSliderMap[card] = new List<CardSlider>();
            _csSysVol = new CardSlider { PixelX = Dpi.S(120), PixelY = Dpi.S(y - 4), PixelW = Dpi.S(180), PixelH = Dpi.S(30), Source = _trkSysVol, Card = card };
            _cardSliderMap[card].Add(_csSysVol);
            _trkSysVol.ValueChanged += (s2, e2) => { if (!_loading) card.Invalidate(); };
            _trkSysVol.DragCompleted += (s2, e2) => { if (!_loading) { try { Audio.SetSpeakerVolume(_trkSysVol.Value); } catch { } } };
            // Sync with current system volume
            try { float sv = Audio.GetSpeakerVolume(); if (sv >= 0) _trkSysVol.Value = (int)sv; } catch { }
            y += 36;
            // "Dictation:" label + green effective % painted by card.Paint
            _lblDuckTo = AddText(card, "Dictation:", 16, y + 4, 8f, TXT2);
            _trkDuckVol = new SlickSlider { Minimum = 0, Maximum = 100, Value = _settings.DictDuckingVolume, Location = Dpi.Pt(120, y - 4), Size = Dpi.Size(180, 30) };
            card.Controls.Add(_trkDuckVol);
            _trkDuckVol.Visible = false; // Hidden — CardSlider paints instead
            if (!_cardSliderMap.ContainsKey(card)) _cardSliderMap[card] = new List<CardSlider>();
            _csDuckVol = new CardSlider { PixelX = Dpi.S(120), PixelY = Dpi.S(y - 4), PixelW = Dpi.S(180), PixelH = Dpi.S(30), Source = _trkDuckVol, Card = card };
            _cardSliderMap[card].Add(_csDuckVol);
            _lblDuckPct = AddText(card, _settings.DictDuckingVolume + "%", 310, y + 4, 8.5f, ACC, FontStyle.Bold);
            _lblDuckPct.RightAlign = true;
            _lblDuckPct.Visible = false; // Hidden — ratio painted directly in card.Paint
            _trkDuckVol.ValueChanged += (s2, e2) => { card.Invalidate(); };
            _trkDuckVol.DragCompleted += (s2, e2) => { if (!_loading) _audio.DictDuckingVolume = _trkDuckVol.Value; };

            // Lock icon
            if (!_cardIconMap.ContainsKey(card)) _cardIconMap[card] = new List<CardIcon>();
            _duckLockIcon = new CardIcon { W = 20, H = 24, Checked = _settings.DictDuckingEnabled, IsLock = true, Card = card,
                OnChange = (locked) => {
                    if (_loading) return;
                    _loading = true; _tglDuck.Checked = locked; _loading = false;
                    _audio.DictDuckingEnabled = locked;
                    if (_duckLockIcon != null) _duckLockIcon.Checked = locked;
                } };
            _duckLockIcon.SetPos(396, y);
            _cardIconMap[card].Add(_duckLockIcon);

            y += 36;

            // PTT/PTM/Toggle ducking slider
            _lblPttDuckTo = AddText(card, "Microphone:", 16, y + 4, 8f, TXT2);
            _trkPttDuckVol = new SlickSlider { Minimum = 0, Maximum = 100, Value = _settings.PttDuckingVolume, Location = Dpi.Pt(120, y - 4), Size = Dpi.Size(180, 30) };
            card.Controls.Add(_trkPttDuckVol);
            _trkPttDuckVol.Visible = false;
            _csPttDuckVol = new CardSlider { PixelX = Dpi.S(120), PixelY = Dpi.S(y - 4), PixelW = Dpi.S(180), PixelH = Dpi.S(30), Source = _trkPttDuckVol, Card = card };
            _cardSliderMap[card].Add(_csPttDuckVol);
            _trkPttDuckVol.ValueChanged += (s2, e2) => { card.Invalidate(); };
            _trkPttDuckVol.DragCompleted += (s2, e2) => { if (!_loading) _audio.PttDuckingVolume = _trkPttDuckVol.Value; };
            // PTT ducking lock icon
            _pttDuckLockIcon = new CardIcon { W = 20, H = 24, Checked = AnyPttDuckActive(), IsLock = true, Card = card,
                OnChange = (locked) => {
                    if (_loading) return;
                    // Toggle all PTT duck flags at once
                    _audio.BeginUpdate();
                    _audio.PttDuckEnabled = locked;
                    _audio.PtmDuckEnabled = locked;
                    _audio.PtToggleDuckEnabled = locked;
                    _audio.EndUpdate();
                    if (_duckPtt != null) _duckPtt.Checked = locked;
                    if (_duckPtm != null) _duckPtm.Checked = locked;
                    if (_duckToggle != null) _duckToggle.Checked = locked;
                    RefreshDuckingSection();
                } };
            _pttDuckLockIcon.SetPos(396, y);
            _cardIconMap[card].Add(_pttDuckLockIcon);

            // Sync sliders with saved settings (no longer force 20% — respects user preference)
            // Initial grey-out: ducking section dims when no duck icons are active
            RefreshDuckingSection();

            y += 34;

            // ═════════════════════════════════════════════════════════════════
            // SECTION 4: Testing Zone
            // ═════════════════════════════════════════════════════════════════
            AddLine(card, y); y += 10;
            
            AddText(card, "Testing Zone", 16, y, 10f, TXT, FontStyle.Bold);
            y += 24;
            AddText(card, "Test your dictation here. Click the box, hold your assigned hotkey, and speak.", 16, y, 7.5f, TXT3);
            y += 20;

            int meterY = y;
            int meterH = 12;
            int meterX = 16;
            y += meterH + 12;

            int rtbX = 16;
            int rtbY = y;
            int rtbH = 120;

            var rtbBorderPanel = new Panel {
                Location = new Point(Dpi.S(rtbX), Dpi.S(rtbY)),
                Size = new Size(card.Width - Dpi.S(32), Dpi.S(rtbH)),
                BackColor = INPUT_BDR,
                Padding = new Padding(1)
            };
            var rtbTest = new TextBox {
                Dock = DockStyle.Fill,
                BackColor = INPUT_BG, ForeColor = TXT,
                Font = new Font("Segoe UI", 9.5f),
                BorderStyle = BorderStyle.None,
                Multiline = true,
                ScrollBars = ScrollBars.None,
                Text = ""
            };

            // Paint "Click here to test..." centered when empty
            rtbBorderPanel.Paint += (s, e) => {
                if (string.IsNullOrEmpty(rtbTest.Text) && !rtbTest.Focused) {
                    var g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    string hint = "Click here to test...";
                    using (var f = new Font("Segoe UI", 12f, FontStyle.Italic))
                    using (var b = new SolidBrush(TXT3)) {
                        var sz = g.MeasureString(hint, f);
                        float hx = (rtbBorderPanel.Width - sz.Width) / 2;
                        float hy = (rtbBorderPanel.Height - sz.Height) / 2;
                        g.DrawString(hint, f, b, hx, hy);
                    }
                }
            };
            rtbTest.TextChanged += (s, e) => { rtbBorderPanel.Invalidate(); };
            rtbTest.GotFocus += (s, e) => { rtbBorderPanel.Invalidate(); };
            rtbTest.LostFocus += (s, e) => { rtbBorderPanel.Invalidate(); };

            DictationManager.OnTextInjected += (text) => {
                if (rtbTest.IsDisposed) return;
                rtbTest.BeginInvoke((Action)(() => {
                    rtbTest.AppendText(text + " ");
                }));
            };

            rtbBorderPanel.Controls.Add(rtbTest);
            card.Controls.Add(rtbBorderPanel);
            y += rtbH + 16;

            int savedMeterY = Dpi.S(meterY);
            int savedMeterH = Dpi.S(meterH);
            int savedRtbX = Dpi.S(rtbX);
            int savedRtbY = Dpi.S(rtbY);

            EventHandler sizeRtb = (s2, e2) => {
                rtbBorderPanel.Width = card.Width - Dpi.S(32);
                rtbBorderPanel.Height = card.Height - savedRtbY - Dpi.S(16);
            };
            card.Layout += new LayoutEventHandler(sizeRtb);
            card.Resize += sizeRtb;

            card.Paint += (s, e) => {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                int mx = Dpi.S(meterX), mw = card.Width - Dpi.S(32);
                float peak = _pushToTalk != null ? _pushToTalk.CurrentPeakLevel : 0f;
                if (peak == 0f && DictationManager.Current != null) peak = DictationManager.Current.CurrentPeakLevel;

                var meterRect = new Rectangle(mx, savedMeterY, mw, savedMeterH);
                
                // Track background
                using (var path = DarkTheme.RoundedRect(meterRect, Dpi.S(4)))
                using (var b = new SolidBrush(Color.FromArgb(22, 22, 22))) g.FillPath(b, path);
                
                // Peak fill
                int fillW = Math.Max(0, (int)(mw * Math.Min(1f, peak)));
                if (fillW > 4) {
                    var fillRect = new Rectangle(mx, savedMeterY, fillW, savedMeterH);
                    using (var path = DarkTheme.RoundedRect(fillRect, Dpi.S(4))) {
                        var oldClip = g.Clip; g.SetClip(path, CombineMode.Intersect);
                        using (var b = new SolidBrush(Color.FromArgb(200, GREEN.R, GREEN.G, GREEN.B))) g.FillRectangle(b, fillRect);
                        g.Clip = oldClip;
                    }
                }
            };

            var _dictMeterTimer = new Timer { Interval = 16 }; // 60fps
            _dictMeterTimer.Tick += (s, e) => {
                try {
                    if (card.Visible && (_pushToTalk != null || DictationManager.Current != null)) {
                        card.Invalidate(new Rectangle(Dpi.S(10), savedMeterY - Dpi.S(4), card.Width - Dpi.S(20), savedMeterH + Dpi.S(8)));
                    }
                } catch { }
            };
            card.VisibleChanged += (s, e) => { if (card.Visible) _dictMeterTimer.Start(); else _dictMeterTimer.Stop(); };
            _dictMeterTimer.Start();

            // ── Load initial toggle states ────────────────────────────────────
            // Load key2 values
            _dictKeyCode        = _settings.DictationKey;
            _dictKeyCode2       = _settings.DictationKey2;
            _dictToggleKeyCode  = _settings.DictationToggleKey;
            _dictToggleKeyCode2 = _settings.DictationToggleKey2;

            _loading = true;
            _tglDictPushToDict.Checked = _settings.DictationEnabled;
            _tglDictToggle.Checked     = _settings.DictationToggleEnabled;
            _loading = false;

            // Explicitly dim controls based on loaded toggle states (handlers didn't fire because _loading was true)
            bool pthOn = _tglDictPushToDict.Checked;
            if (_dictPthOvr  != null) _dictPthOvr.Dimmed  = !pthOn;
            if (_dictPthSnd  != null) _dictPthSnd.Dimmed  = !pthOn;
            if (_dictPthDrop != null) { _dictPthDrop.Enabled = pthOn; _dictPthDrop.Invalidate(); }
            if (_dictPthVol  != null) _dictPthVol.Source.Enabled = pthOn;

            bool pttOn = _tglDictToggle.Checked;
            if (_dictPttOvr  != null) _dictPttOvr.Dimmed  = !pttOn;
            if (_dictPttSnd  != null) _dictPttSnd.Dimmed  = !pttOn;
            if (_dictPttDrop != null) { _dictPttDrop.Enabled = pttOn; _dictPttDrop.Invalidate(); }
            if (_dictPttVol  != null) _dictPttVol.Source.Enabled = pttOn;

            card.Invalidate();

            card.Dock = DockStyle.Fill;
            pane.Controls.Add(card);
            
            LayoutDictKeys();
        }




        /// <summary>Grey-out or enable the Audio Ducking section based on dictation toggle state.
        /// Auto-enables ducking when any dictation mode is turned on.</summary>
        void RefreshDuckingSection() {
            // Check WHICH main toggles are actively turned ON
            bool pthLive = _tglDictPushToDict != null && _tglDictPushToDict.Checked;
            bool pttLive = _tglDictToggle != null && _tglDictToggle.Checked;
            bool pttMainLive = _tglPtt != null && _tglPtt.Checked;
            bool ptmMainLive = _tglPtm != null && _tglPtm.Checked;
            bool ptToggleLive = _tglPtToggle != null && _tglPtToggle.Checked;

            // Check if their corresponding duck icons are checked
            bool dictPthDuck = pthLive && _duckDictPth != null && _duckDictPth.Checked;
            bool dictPttDuck = pttLive && _duckDictPtt != null && _duckDictPtt.Checked;
            bool anyDictDuck = dictPthDuck || dictPttDuck;

            bool pttDuck = pttMainLive && _duckPtt != null && _duckPtt.Checked;
            bool ptmDuck = ptmMainLive && _duckPtm != null && _duckPtm.Checked;
            bool tglDuck = ptToggleLive && _duckToggle != null && _duckToggle.Checked;
            bool anyPttDuck = pttDuck || ptmDuck || tglDuck;

            bool anyDuck = anyDictDuck || anyPttDuck;

            // Auto-enable dict ducking toggle when ANY valid duck icon turns on
            if (anyDuck && _tglDuck != null && !_tglDuck.Checked) {
                _loading = true; _tglDuck.Checked = true; _loading = false;
                _audio.BeginUpdate();
                _audio.DictDuckingEnabled = true;
                _audio.EndUpdate();
                if (_duckLockIcon != null) _duckLockIcon.Checked = true;
            }

            // Auto-disable ducking toggle when NO valid duck icons are active
            if (!anyDuck && _tglDuck != null && _tglDuck.Checked) {
                _loading = true; _tglDuck.Checked = false; _loading = false;
                _audio.DictDuckingEnabled = false;
                if (_duckLockIcon != null) _duckLockIcon.Checked = false;
            }

            // Grey out / enable ducking controls — section lit when ANY duck icon is active
            if (_tglDuck != null) _tglDuck.Enabled = anyDuck;
            if (_trkDuckVol != null) _trkDuckVol.Enabled = anyDuck;
            if (_csDuckVol != null) _csDuckVol.Source.Enabled = anyDictDuck && (_tglDuck != null && _tglDuck.Checked);
            if (_duckLockIcon != null) _duckLockIcon.Dimmed = !anyDictDuck;

            // PTT slider: enabled when any PTT duck icon is active
            if (_trkPttDuckVol != null) _trkPttDuckVol.Enabled = anyDuck;
            if (_csPttDuckVol != null) _csPttDuckVol.Source.Enabled = anyPttDuck;
            if (_pttDuckLockIcon != null) { _pttDuckLockIcon.Dimmed = !anyPttDuck; _pttDuckLockIcon.Checked = anyPttDuck; }

            // Dim ALL labels in the section
            Color dimTxt = Color.FromArgb(60, 60, 60);
            if (_lblDuckTitle != null) _lblDuckTitle.Color = anyDuck ? TXT : dimTxt;
            if (_lblDuckSub != null) _lblDuckSub.Color = anyDuck ? TXT3 : dimTxt;
            if (_lblDuckTglTitle != null) _lblDuckTglTitle.Color = anyDuck ? TXT : dimTxt;
            if (_lblDuckTglSub != null) _lblDuckTglSub.Color = anyDuck ? TXT3 : dimTxt;
            if (_lblDuckTo != null) _lblDuckTo.Color = (anyDictDuck || anyPttDuck) ? TXT2 : dimTxt;
            if (_lblPttDuckTo != null) _lblPttDuckTo.Color = anyPttDuck ? TXT2 : dimTxt;
            if (_lblDuckPct != null) _lblDuckPct.Color = anyDuck ? ACC : dimTxt;
            // Dim the CardToggle associated with _tglDuck
            List<CardToggle> ctgls;
            if (_dictCard != null && _cardToggleMap.TryGetValue(_dictCard, out ctgls)) {
                foreach (var ct in ctgls) { if (ct.Source == _tglDuck) ct.Dimmed = !anyDuck; }
            }
            if (_dictCard != null) _dictCard.Invalidate();
        }

        bool AnyPttDuckActive() {
            bool pttLive = _tglPtt != null && _tglPtt.Checked;
            bool pttDuck = pttLive && _duckPtt != null && _duckPtt.Checked;

            bool ptmLive = _tglPtm != null && _tglPtm.Checked;
            bool ptmDuck = ptmLive && _duckPtm != null && _duckPtm.Checked;

            bool tglLive = _tglPtToggle != null && _tglPtToggle.Checked;
            bool tglDuck = tglLive && _duckToggle != null && _duckToggle.Checked;

            return pttDuck || ptmDuck || tglDuck;
        }

        /// <summary>Ensure Windows microphone privacy access is enabled.
        /// Automatically turns it on when any dictation or mic toggle is activated.</summary>
        void EnsureMicAccess() {
            try {
                // User-level mic consent: HKCU\...\ConsentStore\microphone
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone", true)) {
                    if (key != null) {
                        string val = key.GetValue("Value") as string;
                        if (val != null && val != "Allow") {
                            key.SetValue("Value", "Allow");
                            Logger.Info("EnsureMicAccess: Enabled user microphone privacy access.");
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Error("EnsureMicAccess: Failed to set mic privacy.", ex);
            }
        }

        void BuildFooter() {
            _footer = new BufferedPanel{Dock=DockStyle.Bottom,Height=Dpi.S(50),BackColor=Color.Transparent};
            Rectangle _optSaveRect = Rectangle.Empty, _optCancelRect = Rectangle.Empty;
            bool _optSaveHover = false, _optCancelHover = false;
            _footer.Paint += (s,e) => {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
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
                    _footer.Invalidate();
                }
            };
            _footer.MouseLeave += (s, e) => { _optSaveHover = _optCancelHover = false; _footer.Invalidate(); };
            _footer.MouseClick += (s, e) => {
                if (_optSaveRect.Contains(e.Location)) DoSave();
                else if (_optCancelRect.Contains(e.Location)) Close();
            };
            Controls.Add(_footer);

            _saveOrbitPhase = 0f;
            _saveOrbitTimer = new Timer { Interval = 16 }; // 60fps
            _saveOrbitTimer.Tick += (s, e) => {
                _saveOrbitPhase += 0.04f; // Halved from 0.08f to compensate for 16ms vs 30ms
                if (_saveOrbitPhase > (float)(Math.PI * 2)) _saveOrbitPhase -= (float)(Math.PI * 2);
                _footer.Invalidate();
            };
            _saveOrbitTimer.Start();
        }
        void FlashModeToggles() {
            // Glow the specific mode segment that the pressed hotkey belongs to
            if (_tglPtt != null && _tglPtt.Checked) FlashToggleOn(_tglPtt);
            else if (_tglPtm != null && _tglPtm.Checked) FlashToggleOn(_tglPtm);
            else if (_tglPtToggle != null && _tglPtToggle.Checked) FlashToggleOn(_tglPtToggle);
        }

        private DateTime _lastCaptureComplete = DateTime.MinValue;
        private bool CaptureCooldownActive() { return (DateTime.Now - _lastCaptureComplete).TotalMilliseconds < 50; }

        // =====================================================================
        //  UNIFIED CAPTURE — single entry point, single completion handler
        void BeginCapture(CaptureTarget target, Label label) {
            if (_audio.IsCapturing || CaptureCooldownActive()) return;
            // For key2/key3 slots, require at least one mode toggle to be on
            if (target == CaptureTarget.PttKey2 ||
                target == CaptureTarget.PtmKey2 ||
                target == CaptureTarget.ToggleKey2) {
                if (!_tglPtt.Checked && !_tglPtm.Checked && !_tglPtToggle.Checked) { FlashToggleOn(_tglPtt); return; }
            }
            Color captureColor = GetKeyTheme(label);
            label.Text = "Press..."; label.BackColor = captureColor; label.ForeColor = Color.White;
            _captureGlowLabel = label; _captureGlowFrame = 0;
            // Update layout so the label is visible at its new size
            if (target >= CaptureTarget.PttKey1 && target <= CaptureTarget.PttKey2) LayoutPttKeys();
            else if (target >= CaptureTarget.PtmKey1 && target <= CaptureTarget.PtmKey2) LayoutPtmKeys();
            else if (target >= CaptureTarget.ToggleKey1 && target <= CaptureTarget.ToggleKey2) LayoutToggleKeys();
            else if (target == CaptureTarget.MmKey || target == CaptureTarget.MmKey2) { LayoutAllMm(); }
            else if (target == CaptureTarget.DictationKey1 || target == CaptureTarget.DictationKey2 || target == CaptureTarget.DictationToggleKey || target == CaptureTarget.DictationToggleKey2) { LayoutDictKeys(); }
            Logger.Info("BeginCapture: " + target);
            _audio.StartCapture(target, vk => OnCaptureComplete(target, label, vk));
        }

        /// <summary>Clear a key from all slots except the target being captured. Allows key reuse across modes.</summary>
        void ClearKeyFromAllSlots(int vk, CaptureTarget newTarget) {
            if (vk <= 0) return;
            // Clear from each slot if the key matches and it's not the target slot
            if (newTarget != CaptureTarget.PttKey1 && _pttKeyCode == vk) { _pttKeyCode = 0; _audio.PttKey = 0; ResetSlotLabel(_lblPttKey); }
            if (newTarget != CaptureTarget.PttKey2 && _pttKeyCode2 == vk) { _pttKeyCode2 = 0; _audio.PttKey2 = 0; ResetSlotLabel(_lblPttKey2); }
            if (newTarget != CaptureTarget.PtmKey1 && _ptmKeyCode == vk) { _ptmKeyCode = 0; _audio.PtmKey = 0; ResetSlotLabel(_lblPtmKey); }
            if (newTarget != CaptureTarget.PtmKey2 && _ptmKeyCode2 == vk) { _ptmKeyCode2 = 0; _audio.PtmKey2 = 0; ResetSlotLabel(_lblPtmKey2); }
            if (newTarget != CaptureTarget.ToggleKey1 && _ptToggleKeyCode == vk) { _ptToggleKeyCode = 0; _audio.PtToggleKey = 0; ResetSlotLabel(_lblPtToggleKey); }
            if (newTarget != CaptureTarget.ToggleKey2 && _ptToggleKeyCode2 == vk) { _ptToggleKeyCode2 = 0; _audio.PtToggleKey2 = 0; ResetSlotLabel(_lblPtToggleKey2); }
            if (newTarget != CaptureTarget.MmKey && _mmKeyCode == vk) { _mmKeyCode = 0; _audio.MmKey = 0; }
            if (newTarget != CaptureTarget.MmKey2 && _mmKeyCode2 == vk) { _mmKeyCode2 = 0; _audio.MmKey2 = 0; }
            if (newTarget != CaptureTarget.DictationKey1 && _dictKeyCode == vk) { _dictKeyCode = 0; _audio.DictationKey = 0; ResetSlotLabel(_lblDictKey); }
            if (newTarget != CaptureTarget.DictationKey2 && _dictKeyCode2 == vk) { _dictKeyCode2 = 0; _audio.DictationKey2 = 0; ResetSlotLabel(_lblDictKey2); }
            if (newTarget != CaptureTarget.DictationToggleKey && _dictToggleKeyCode == vk) { _dictToggleKeyCode = 0; _audio.DictationToggleKey = 0; ResetSlotLabel(_lblDictToggleKey); }
            if (newTarget != CaptureTarget.DictationToggleKey2 && _dictToggleKeyCode2 == vk) { _dictToggleKeyCode2 = 0; _audio.DictationToggleKey2 = 0; ResetSlotLabel(_lblDictToggleKey2); }

            // ── Auto-disable modes that lost ALL keys ──
            if (_tglPtt != null && _tglPtt.Checked && _pttKeyCode <= 0 && _pttKeyCode2 <= 0) {
                _loading = true; _tglPtt.Checked = false; _loading = false;
                _audio.DisablePttMode();
                if (_chkKey1Overlay != null) _chkKey1Overlay.Dimmed = true;
                if (_chkPttSound != null) _chkPttSound.Dimmed = true;
                if (_drpPttSound != null) _drpPttSound.Enabled = false;
                if (_csPttVol != null) _csPttVol.Source.Enabled = false;
                LayoutPttKeys();
            }
            if (_tglPtm != null && _tglPtm.Checked && _ptmKeyCode <= 0 && _ptmKeyCode2 <= 0) {
                _loading = true; _tglPtm.Checked = false; _loading = false;
                _audio.DisablePtmMode();
                if (_chkPtmOverlay != null) _chkPtmOverlay.Dimmed = true;
                if (_chkPtmSound != null) _chkPtmSound.Dimmed = true;
                if (_drpPtmSound != null) _drpPtmSound.Enabled = false;
                if (_csPtmVol != null) _csPtmVol.Source.Enabled = false;
                LayoutPtmKeys();
            }
            if (_tglPtToggle != null && _tglPtToggle.Checked && _ptToggleKeyCode <= 0 && _ptToggleKeyCode2 <= 0) {
                _loading = true; _tglPtToggle.Checked = false; _loading = false;
                _audio.DisablePtToggleMode();
                try { Audio.SetMicMute(false); } catch { }
                // Clear MM key — depends on Toggle being active
                _mmKeyCode=0; _mmKeyCode2=0; _audio.MmKey=0; _audio.MmKey2=0;
                // Restore duck snapshots
                if (_toggleDuckSnapshot >= 0f) { try { Audio.SetSpeakerVolume(_toggleDuckSnapshot); } catch { } _toggleDuckSnapshot = -1f; }
                if (_duckToggle != null && _duckToggle.Checked) { _duckToggle.Checked = false; _audio.PtToggleDuckEnabled = false; }
                if (_chkToggleOverlay != null) _chkToggleOverlay.Dimmed = true;
                if (_chkToggleSound != null) _chkToggleSound.Dimmed = true;
                if (_drpToggleSound != null) _drpToggleSound.Enabled = false;
                if (_csToggleVol != null) _csToggleVol.Source.Enabled = false;
                LayoutToggleKeys();
                UpdateMmKeyLabels();
            }
            if (_tglDictPushToDict != null && _tglDictPushToDict.Checked && _dictKeyCode <= 0 && _dictKeyCode2 <= 0) {
                _loading = true; _tglDictPushToDict.Checked = false; _loading = false;
                _audio.DictationEnabled = false;
                if (_dictPthOvr != null) _dictPthOvr.Dimmed = true;
                if (_dictPthSnd != null) _dictPthSnd.Dimmed = true;
                if (_dictPthDrop != null) _dictPthDrop.Enabled = false;
                if (_dictPthVol != null) _dictPthVol.Source.Enabled = false;
            }
            if (_tglDictToggle != null && _tglDictToggle.Checked && _dictToggleKeyCode <= 0 && _dictToggleKeyCode2 <= 0) {
                _loading = true; _tglDictToggle.Checked = false; _loading = false;
                _audio.DictationToggleEnabled = false;
                if (_dictPttOvr != null) _dictPttOvr.Dimmed = true;
                if (_dictPttSnd != null) _dictPttSnd.Dimmed = true;
                if (_dictPttDrop != null) _dictPttDrop.Enabled = false;
                if (_dictPttVol != null) _dictPttVol.Source.Enabled = false;
            }

            CompactKeys();
            _audio.Save();
            if (_pttCard != null) _pttCard.Invalidate();
            if (_dictCard != null) _dictCard.Invalidate();
        }
        void ResetSlotLabel(Label lbl) {
            if (lbl == null) return;
            lbl.Text = "Add Key"; lbl.BackColor = INPUT_BG; lbl.ForeColor = ACC; lbl.Tag = null;
        }

        /// <summary>Single completion handler for ALL key captures.</summary>
        void OnCaptureComplete(CaptureTarget target, Label label, int vk) {
            _captureGlowLabel = null;
            _lastCaptureComplete = DateTime.Now;

            if (vk == 0) { // Escape or cancel
                HandleCaptureCancel(target, label);
                return;
            }

            // ── MM↔Toggle conflict check (MUST run before ClearKeyFromAllSlots) ──
            // MM key and Toggle key CANNOT be the same. If they match, the OTHER
            // slot enters "Press..." — bouncing back and forth until two different keys are chosen.
            // Cancelling at any point during the ping-pong disables toggle mode + clears both.
            if ((target == CaptureTarget.MmKey || target == CaptureTarget.MmKey2) &&
                (vk == _ptToggleKeyCode || vk == _ptToggleKeyCode2)) {
                // MM captured same key as Toggle → clear Toggle, enter "Press..." on Toggle
                _mmToggleConflictActive = true;
                // Store the rejected MM key locally so reverse check can detect it
                _mmKeyCode = vk;
                _ptToggleKeyCode=0; _ptToggleKeyCode2=0; _audio.PtToggleKey=0; _audio.PtToggleKey2=0; _audio.Save();
                // Reset the MM label from "Press..." back to its default state
                _lblMmToggle.Text = KeyName(vk); _lblMmToggle.BackColor = INPUT_BG;
                _lblMmToggle.ForeColor = Color.FromArgb(160, 90, 210);
                LayoutToggleKeys(); UpdateMmKeyLabels(); CompactKeys();
                if (_pttCard != null) _pttCard.Invalidate();
                // Reset cooldown so the immediate BeginCapture isn't blocked
                _lastCaptureComplete = DateTime.MinValue;
                BeginCapture(CaptureTarget.ToggleKey1, _lblPtToggleKey);
                return;
            }
            if ((target == CaptureTarget.ToggleKey1 || target == CaptureTarget.ToggleKey2) &&
                _mmKeyCode > 0 && (vk == _mmKeyCode || vk == _mmKeyCode2)) {
                // Toggle captured same key as MM → clear MM, enter "Press..." on MM
                _mmToggleConflictActive = true;
                _mmKeyCode=0; _mmKeyCode2=0; _audio.MmKey=0; _audio.MmKey2=0;
                // Save the toggle key first so it's not lost
                _ptToggleKeyCode = vk; _audio.PtToggleKey = vk; _audio.Save();
                // Reset the Toggle label from "Press..." back to showing the key
                _lblPtToggleKey.Text = KeyName(vk); _lblPtToggleKey.BackColor = INPUT_BG;
                _lblPtToggleKey.ForeColor = ACC;
                UpdateMmKeyLabels(); LayoutToggleKeys(); CompactKeys();
                if (_pttCard != null) _pttCard.Invalidate();
                // Reset cooldown so the immediate BeginCapture isn't blocked
                _lastCaptureComplete = DateTime.MinValue;
                BeginCapture(CaptureTarget.MmKey, _lblMmToggle);
                return;
            }
            // If we get here with a successful capture during a conflict, the conflict is resolved
            if (_mmToggleConflictActive) _mmToggleConflictActive = false;

            // Auto-clear: if key is used elsewhere, remove it from old slot first
            ClearKeyFromAllSlots(vk, target);

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
                    // Toggle active: redirect captured key to MM immediately — never enable PTT
                    if (_tglPtToggle != null && _tglPtToggle.Checked) {
                        _mmKeyCode = vk; _audio.MmKey = vk; // Always set — replace existing MM key
                        _pttKeyCode=0;_pttKeyCode2=0;_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;
                        CompactKeys();LayoutPttKeys();UpdateMmKeyLabels();_audio.Save();
                        try { Audio.SetMicMute(false); } catch { } // failsafe: ensure mic isn't stuck muted
                        if (_pttCard != null) _pttCard.Invalidate();
                        RefreshDuckingSection();
                        break;
                    }
                    if (!_tglPtt.Checked) { _loading=true; _tglPtt.Checked=true; _loading=false; StartGlisten(_tglPtt); }
                    // Undim icons since handler was bypassed
                    if (_chkKey1Overlay != null) { _chkKey1Overlay.Dimmed = false; _chkKey1Overlay.Checked = true; _audio.PttKey1ShowOverlay = true; }
                    if (_duckPtt != null) { _duckPtt.Dimmed = false; _duckPtt.Checked = true; _audio.PttDuckEnabled = true; }
                    if (_chkPttSound != null) { _chkPttSound.Dimmed = false; }
                    { bool sndOn = _chkPttSound != null && _chkPttSound.Checked; if (_drpPttSound != null) _drpPttSound.Enabled = sndOn; if (_csPttVol != null) _csPttVol.Source.Enabled = sndOn; }
                    // Mutual exclusivity: PTT on kills PTM (unless Toggle is on)
                    if (!_tglPtToggle.Checked && _tglPtm.Checked) {
                        if (_ptmDuckSnapshot >= 0f) { try { Audio.SetSpeakerVolume(_ptmDuckSnapshot); } catch { } _ptmDuckSnapshot = -1f; }
                        if (_duckPtm != null && _duckPtm.Checked) { _duckPtm.Checked = false; _audio.PtmDuckEnabled = false; }
                        _loading=true; _tglPtm.Checked=false; _loading=false;
                        _ptmKeyCode=0;_ptmKeyCode2=0;if(_lblPtmKey!=null){_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;}LayoutPtmKeys();_audio.DisablePtmMode();
                        if (_chkPtmOverlay != null) _chkPtmOverlay.Dimmed = true;
                        if (_chkPtmSound != null) _chkPtmSound.Dimmed = true;
                        if (_drpPtmSound != null) _drpPtmSound.Enabled = false;
                        if (_csPtmVol != null) _csPtmVol.Source.Enabled = false;
                    }
                    _audio.SetPttKeyAndEnable(vk); _audio.PttKey2=_pttKeyCode2;
                    if (_pttCard != null) _pttCard.Invalidate();
                    break;
                case CaptureTarget.PttKey2:
                    _key2ShowOverlay=true; _audio.PttKey2ShowOverlay=true; if(_chkKey2Overlay!=null)_chkKey2Overlay.Checked=true;
                    _audio.PttKey2=vk; _audio.Save();
                    break;
                case CaptureTarget.PtmKey1:
                    // Toggle active: redirect captured key to MM immediately — never enable PTM
                    if (_tglPtToggle != null && _tglPtToggle.Checked) {
                        _mmKeyCode = vk; _audio.MmKey = vk; // Always set — replace existing MM key
                        _ptmKeyCode=0;_ptmKeyCode2=0;if(_lblPtmKey!=null){_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;}
                        LayoutPtmKeys();UpdateMmKeyLabels();_audio.Save();
                        try { Audio.SetMicMute(false); } catch { } // failsafe: ensure mic isn't stuck muted
                        if (_pttCard != null) _pttCard.Invalidate();
                        RefreshDuckingSection();
                        break;
                    }
                    if (!_tglPtm.Checked) { _loading=true; _tglPtm.Checked=true; _loading=false; StartGlisten(_tglPtm); }
                    // Undim icons since handler was bypassed
                    if (_chkPtmOverlay != null) { _chkPtmOverlay.Dimmed = false; _chkPtmOverlay.Checked = true; _audio.PtmShowOverlay = true; }
                    if (_duckPtm != null) { _duckPtm.Dimmed = false; } // Duck stays OFF — user must opt in
                    if (_chkPtmSound != null) { _chkPtmSound.Dimmed = false; }
                    { bool sndOn = _chkPtmSound != null && _chkPtmSound.Checked; if (_drpPtmSound != null) _drpPtmSound.Enabled = sndOn; if (_csPtmVol != null) _csPtmVol.Source.Enabled = sndOn; }
                    // Mutual exclusivity: PTM on kills PTT (unless Toggle is on)
                    if (!_tglPtToggle.Checked && _tglPtt.Checked) {
                        if (_duckPtt != null && _duckPtt.Checked) { _duckPtt.Checked = false; _audio.PttDuckEnabled = false; }
                        _loading=true; _tglPtt.Checked=false; _loading=false;
                        _pttKeyCode=0;_pttKeyCode2=0;_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;CompactKeys();LayoutPttKeys();_audio.DisablePttMode();
                        if (_chkKey1Overlay != null) _chkKey1Overlay.Dimmed = true;
                        if (_chkPttSound != null) _chkPttSound.Dimmed = true;
                        if (_drpPttSound != null) _drpPttSound.Enabled = false;
                        if (_csPttVol != null) _csPttVol.Source.Enabled = false;
                    }
                    _audio.SetPtmKeyAndEnable(vk);
                    if (_pttCard != null) _pttCard.Invalidate();
                    break;
                case CaptureTarget.PtmKey2:
                    _audio.PtmKey2=vk; _audio.Save(); LayoutPtmKeys();
                    break;
                case CaptureTarget.ToggleKey1:
                    if (!_tglPtToggle.Checked) { _loading=true; _tglPtToggle.Checked=true; _loading=false; StartGlisten(_tglPtToggle); }
                    // Undim icons since handler was bypassed
                    if (_chkToggleOverlay != null) { _chkToggleOverlay.Dimmed = false; _chkToggleOverlay.Checked = true; _audio.PtToggleShowOverlay = true; }
                    if (_duckToggle != null) { _duckToggle.Dimmed = false; } // Duck stays OFF — user must opt in
                    if (_chkToggleSound != null) { _chkToggleSound.Dimmed = false; } // Sound stays at current checked state
                    { bool sndOn = _chkToggleSound != null && _chkToggleSound.Checked; if (_drpToggleSound != null) _drpToggleSound.Enabled = sndOn; if (_csToggleVol != null) _csToggleVol.Source.Enabled = sndOn; }
                    // Toggle ON: disable PTT and PTM, migrate first key to MM key
                    { int mmCand = 0;
                      if (_tglPtt.Checked) {
                          if (_pttKeyCode > 0) mmCand = _pttKeyCode;
                          if (_duckPtt != null && _duckPtt.Checked) { _duckPtt.Checked = false; _audio.PttDuckEnabled = false; }
                          _loading=true; _tglPtt.Checked=false; _loading=false;
                          _pttKeyCode=0;_pttKeyCode2=0;_lblPttKey.Text="Add Key";_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;CompactKeys();LayoutPttKeys();_audio.DisablePttMode();
                          if (_chkKey1Overlay != null) _chkKey1Overlay.Dimmed = true;
                          if (_chkPttSound != null) _chkPttSound.Dimmed = true;
                          if (_drpPttSound != null) _drpPttSound.Enabled = false;
                          if (_csPttVol != null) _csPttVol.Source.Enabled = false;
                      }
                      if (_tglPtm.Checked) {
                          if (mmCand <= 0 && _ptmKeyCode > 0) mmCand = _ptmKeyCode;
                          if (_ptmDuckSnapshot >= 0f) { try { Audio.SetSpeakerVolume(_ptmDuckSnapshot); } catch { } _ptmDuckSnapshot = -1f; }
                          if (_duckPtm != null && _duckPtm.Checked) { _duckPtm.Checked = false; _audio.PtmDuckEnabled = false; }
                          _loading=true; _tglPtm.Checked=false; _loading=false;
                          _ptmKeyCode=0;_ptmKeyCode2=0;if(_lblPtmKey!=null){_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;}LayoutPtmKeys();_audio.DisablePtmMode();
                          if (_chkPtmOverlay != null) _chkPtmOverlay.Dimmed = true;
                          if (_chkPtmSound != null) _chkPtmSound.Dimmed = true;
                          if (_drpPtmSound != null) _drpPtmSound.Enabled = false;
                          if (_csPtmVol != null) _csPtmVol.Source.Enabled = false;
                      }
                      if (mmCand > 0 && _mmKeyCode <= 0) { _mmKeyCode = mmCand; _audio.MmKey = mmCand; _audio.Save(); }
                    }
                    _audio.SetPtToggleKeyAndEnable(vk);
                    UpdateMmKeyLabels();
                    if (_pttCard != null) _pttCard.Invalidate();
                    break;
                case CaptureTarget.ToggleKey2:
                    _audio.PtToggleKey2=vk; _audio.Save(); LayoutToggleKeys();
                    break;
                case CaptureTarget.MmKey:
                    _audio.MmKey=vk; _audio.Save(); UpdateMmKeyLabels();
                    if (_pttCard != null) _pttCard.Invalidate();
                    break;
                case CaptureTarget.MmKey2:
                    _audio.MmKey2=vk; _audio.Save(); UpdateMmKeyLabels();
                    if (_pttCard != null) _pttCard.Invalidate();
                    break;
                case CaptureTarget.DictationKey1:
                    // Auto-disable toggle mode when push-to-hold key is captured
                    if (_tglDictToggle != null && _tglDictToggle.Checked) {
                        _loading=true; _tglDictToggle.Checked=false; _loading=false;
                        _audio.DictationToggleKey = 0; _audio.DictationToggleKey2 = 0;
                        _dictToggleKeyCode = 0; _dictToggleKeyCode2 = 0;
                        if (_lblDictToggleKey != null) { _lblDictToggleKey.Text = "Add Key"; _lblDictToggleKey.BackColor = INPUT_BG; _lblDictToggleKey.ForeColor = ACC; }
                        _audio.DictationToggleEnabled = false;
                        // Dim killed PTT section
                        if (_dictPttOvr != null) _dictPttOvr.Dimmed = true;
                        if (_dictPttSnd != null) _dictPttSnd.Dimmed = true;
                        if (_dictPttDrop != null) _dictPttDrop.Enabled = false;
                        if (_dictPttVol != null) _dictPttVol.Source.Enabled = false;
                    }
                    if (!_tglDictPushToDict.Checked) { _loading=true; _tglDictPushToDict.Checked=true; _loading=false; _audio.DictationEnabled=true; _audio.Save(); StartGlisten(_tglDictPushToDict); }
                    // Undim PTH icons since handler was bypassed
                    if (_dictPthOvr != null) { _dictPthOvr.Dimmed = false; _dictPthOvr.Checked = true; _audio.DictShowOverlay = true; }
                    if (_dictPthSnd != null) { _dictPthSnd.Dimmed = false; _dictPthSnd.Checked = false; } // Speaker OFF by default
                    if (_duckDictPth != null) { _duckDictPth.Dimmed = false; _duckDictPth.Checked = true; _audio.DictPthDuckEnabled = true; }
                    if (_dictPthDrop != null) _dictPthDrop.Enabled = false; // Dropdown OFF until speaker clicked
                    if (_dictPthVol != null) _dictPthVol.Source.Enabled = false; // Slider OFF until speaker clicked
                    _dictKeyCode = vk; _audio.DictationKey = vk;
                    if (_lblDictKey != null) _lblDictKey.Text = KeyName(vk);
                    RefreshDuckingSection();
                    EnsureMicAccess();
                    if (_dictCard != null) _dictCard.Invalidate();
                    break;
                case CaptureTarget.DictationKey2:
                    _dictKeyCode2 = vk; _audio.DictationKey2 = vk;
                    if (_lblDictKey2 != null) { _lblDictKey2.Text = KeyName(vk); _lblDictKey2.Visible = true; }
                    if (_dictCard != null) _dictCard.Invalidate();
                    break;
                case CaptureTarget.DictationToggleKey:
                    // Auto-disable push-to-hold when toggle key is captured
                    if (_tglDictPushToDict != null && _tglDictPushToDict.Checked) {
                        _loading=true; _tglDictPushToDict.Checked=false; _loading=false;
                        _audio.DictationKey = 0; _audio.DictationKey2 = 0;
                        _dictKeyCode = 0; _dictKeyCode2 = 0;
                        if (_lblDictKey != null) { _lblDictKey.Text = "Add Key"; _lblDictKey.BackColor = INPUT_BG; _lblDictKey.ForeColor = ACC; }
                        _audio.DictationEnabled = false;
                        // Dim killed PTH section
                        if (_dictPthOvr != null) _dictPthOvr.Dimmed = true;
                        if (_dictPthSnd != null) _dictPthSnd.Dimmed = true;
                        if (_dictPthDrop != null) _dictPthDrop.Enabled = false;
                        if (_dictPthVol != null) _dictPthVol.Source.Enabled = false;
                    }
                    if (!_tglDictToggle.Checked) { _loading=true; _tglDictToggle.Checked=true; _loading=false; _audio.DictationToggleEnabled=true; _audio.Save(); StartGlisten(_tglDictToggle); }
                    // Undim PTT icons since handler was bypassed
                    if (_dictPttOvr != null) { _dictPttOvr.Dimmed = false; _dictPttOvr.Checked = true; _audio.DictShowOverlay = true; }
                    if (_dictPttSnd != null) { _dictPttSnd.Dimmed = false; _dictPttSnd.Checked = false; } // Speaker OFF by default
                    if (_duckDictPtt != null) { _duckDictPtt.Dimmed = false; _duckDictPtt.Checked = true; _audio.DictPttDuckEnabled = true; }
                    if (_dictPttDrop != null) _dictPttDrop.Enabled = false; // Dropdown OFF until speaker clicked
                    if (_dictPttVol != null) _dictPttVol.Source.Enabled = false; // Slider OFF until speaker clicked
                    _dictToggleKeyCode = vk; _audio.DictationToggleKey = vk;
                    if (_lblDictToggleKey != null) _lblDictToggleKey.Text = KeyName(vk);
                    RefreshDuckingSection();
                    EnsureMicAccess();
                    if (_dictCard != null) _dictCard.Invalidate();
                    break;
                case CaptureTarget.DictationToggleKey2:
                    _dictToggleKeyCode2 = vk; _audio.DictationToggleKey2 = vk;
                    if (_lblDictToggleKey2 != null) { _lblDictToggleKey2.Text = KeyName(vk); _lblDictToggleKey2.Visible = true; }
                    if (_dictCard != null) _dictCard.Invalidate();
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
                case CaptureTarget.PttKey1: case CaptureTarget.PttKey2:
                    if (_pttKeyCode==0 && _pttKeyCode2==0 && _tglPtt.Checked) { _loading=true; _tglPtt.Checked=false; _loading=false; _audio.DisablePttMode(); }
                    break;
                case CaptureTarget.PtmKey1: case CaptureTarget.PtmKey2:
                    if (_ptmKeyCode==0 && _ptmKeyCode2==0 && _tglPtm.Checked) { _loading=true; _tglPtm.Checked=false; _loading=false; _audio.DisablePtmMode(); }
                    break;
                case CaptureTarget.ToggleKey1: case CaptureTarget.ToggleKey2:
                    if (_ptToggleKeyCode==0 && _ptToggleKeyCode2==0 && _tglPtToggle.Checked) {
                        _loading=true; _tglPtToggle.Checked=false; _loading=false;
                        _audio.DisablePtToggleMode();
                        // Also clear MM keys — they depend on Toggle being active
                        _mmKeyCode=0; _mmKeyCode2=0; _audio.MmKey=0; _audio.MmKey2=0; _audio.Save();
                        UpdateMmKeyLabels();
                        _mmToggleConflictActive = false;
                    }
                    break;
                case CaptureTarget.MmKey:
                case CaptureTarget.MmKey2:
                    if (_mmToggleConflictActive) {
                        // Cancelled during ping-pong → disable toggle entirely + clear both
                        _mmToggleConflictActive = false;
                        _ptToggleKeyCode=0; _ptToggleKeyCode2=0; _audio.PtToggleKey=0; _audio.PtToggleKey2=0;
                        _mmKeyCode=0; _mmKeyCode2=0; _audio.MmKey=0; _audio.MmKey2=0; _audio.Save();
                        _lblPtToggleKey.Text="Add Key"; _lblPtToggleKey.BackColor=INPUT_BG; _lblPtToggleKey.ForeColor=ACC;
                        _loading=true; _tglPtToggle.Checked=false; _loading=false;
                        _audio.DisablePtToggleMode();
                        LayoutToggleKeys(); CompactKeys();
                    }
                    UpdateMmKeyLabels();
                    break;
                case CaptureTarget.DictationKey1:
                    _dictKeyCode = 0; _audio.DictationKey = 0;
                    if (_lblDictKey != null) _lblDictKey.Text = "Add Key";
                    if (_dictCard != null) _dictCard.Invalidate();
                    break;
                case CaptureTarget.DictationKey2:
                    _dictKeyCode2 = 0; _audio.DictationKey2 = 0;
                    if (_lblDictKey2 != null) { _lblDictKey2.Visible = false; }
                    if (_dictCard != null) _dictCard.Invalidate();
                    break;
                case CaptureTarget.DictationToggleKey:
                    _dictToggleKeyCode = 0; _audio.DictationToggleKey = 0;
                    if (_lblDictToggleKey != null) _lblDictToggleKey.Text = "Add Key";
                    if (_dictCard != null) _dictCard.Invalidate();
                    break;
                case CaptureTarget.DictationToggleKey2:
                    _dictToggleKeyCode2 = 0; _audio.DictationToggleKey2 = 0;
                    if (_lblDictToggleKey2 != null) { _lblDictToggleKey2.Visible = false; }
                    if (_dictCard != null) _dictCard.Invalidate();
                    break;
            }
            RefreshPttHotkeyLabel();
        }

        /// <summary>Reset a label back to its non-capturing appearance.</summary>
        void ResetLabel(CaptureTarget target, Label label) {
            label.BackColor = INPUT_BG;
            label.ForeColor = (target == CaptureTarget.MmKey || target == CaptureTarget.MmKey2) ? Color.FromArgb(160, 90, 210) : ACC;
        }

        /// <summary>Set the local key code field for a target.</summary>
        void SetLocalKeyCode(CaptureTarget target, int vk) {
            switch (target) {
                case CaptureTarget.PttKey1: _pttKeyCode = vk; break;
                case CaptureTarget.PttKey2: _pttKeyCode2 = vk; break;
                case CaptureTarget.PtmKey1: _ptmKeyCode = vk; break;
                case CaptureTarget.PtmKey2: _ptmKeyCode2 = vk; break;
                case CaptureTarget.ToggleKey1: _ptToggleKeyCode = vk; break;
                case CaptureTarget.ToggleKey2: _ptToggleKeyCode2 = vk; break;
                case CaptureTarget.MmKey: _mmKeyCode = vk; break;
                case CaptureTarget.MmKey2: _mmKeyCode2 = vk; break;
                case CaptureTarget.DictationKey1: _dictKeyCode = vk; break;
                case CaptureTarget.DictationKey2: _dictKeyCode2 = vk; break;
                case CaptureTarget.DictationToggleKey: _dictToggleKeyCode = vk; break;
                case CaptureTarget.DictationToggleKey2: _dictToggleKeyCode2 = vk; break;
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
            // Reset MM labels to their current state
            UpdateMmKeyLabels();
            CompactKeys(); LayoutPttKeys(); LayoutPtmKeys(); LayoutToggleKeys(); LayoutDictKeys();
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
        CardIcon MakeDuckCheck(int y, Panel card, bool initialOn, Action<bool> onChange) {
            var icon = new CardIcon { W = 24, H = 24, Checked = initialOn, IsDuck = true, OnChange = onChange, Card = card };
            icon.SetPos(385, y);
            if (!_cardIconMap.ContainsKey(card)) _cardIconMap[card] = new List<CardIcon>();
            _cardIconMap[card].Add(icon);
            return icon;
        }
        CardIcon MakeKeyCheck(int y, Panel card, bool initialOn, Action<bool> onChange) {
            var icon = new CardIcon { W = 24, H = 24, Checked = initialOn, IsKey = true, OnChange = onChange, Card = card };
            icon.SetPos(360, y);
            if (!_cardIconMap.ContainsKey(card)) _cardIconMap[card] = new List<CardIcon>();
            _cardIconMap[card].Add(icon);
            return icon;
        }
        /// <summary>Show a toast popup when a duck icon is toggled.</summary>
        void ShowDuckToast(string modeName, bool enabled) {
            string title = "Audio Ducking " + (enabled ? "Enabled" : "Disabled");
            string body = modeName + (enabled ? " \u2014 Volume will lower to (" + (_trkDuckVol != null ? _trkDuckVol.Value : 20) + "%) while active" : " \u2014 Volume will no longer be reduced");
            string _duckMsg = title + " \u2014 " + body;
            PopupThread.Invoke(() => { var t2 = new CorrectionToast(_duckMsg, false, false); t2.ShowNoFocus(); });
        }
        /// <summary>Show a toast popup when a key suppress icon is toggled.</summary>
        void ShowKeyToast(string modeName, bool enabled) {
            string title = "Hotkey Suppression " + (enabled ? "Enabled" : "Disabled");
            string body = modeName + (enabled ? " \u2014 Keystrokes will be blocked from other apps" : " \u2014 Keystrokes will flow to all apps");
            string _keyMsg = title + " \u2014 " + body;
            PopupThread.Invoke(() => { var t2 = new CorrectionToast(_keyMsg, false, false); t2.ShowNoFocus(); });
        }
        /// <summary>Creates a compact dark sound dropdown at a specific position, right-aligned on the hotkey row.</summary>
        Panel MakeSoundDropdown(int y, Panel card, int settingValue, Action<int> onChange, Func<int> getVolume) {
            string[] sndNames = { "Soft Click", "Double Tap", "Chirp", "Radio", "Chime", "Pop", "Custom..." };
            int sel = Math.Max(0, Math.Min(sndNames.Length - 1, settingValue));
            var btn = new BufferedPanel { Size = Dpi.Size(86, 24), Location = Dpi.Pt(330, y), BackColor = INPUT_BG };
            bool hov = false;
            btn.Paint += (s2, e2) => {
                var g2 = e2.Graphics; g2.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g2.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                bool dim = !btn.Enabled;
                Color bg = dim ? Color.FromArgb(18, 18, 18) : (hov ? Color.FromArgb(30, 30, 30) : INPUT_BG);
                Color bdr = dim ? Color.FromArgb(35, 35, 35) : (hov ? ACC : Color.FromArgb(60, ACC.R, ACC.G, ACC.B));
                Color txtC = dim ? Color.FromArgb(80, 80, 80) : TXT;
                Color chevC = dim ? Color.FromArgb(60, 60, 60) : ACC;
                int cr = Dpi.S(3);
                using (var path = DarkTheme.RoundedRect(new Rectangle(0, 0, btn.Width - 1, btn.Height - 1), cr)) { using (var b = new SolidBrush(bg)) g2.FillPath(b, path); using (var p = new Pen(bdr, 1f)) g2.DrawPath(p, path); }
                string txt = sel == 6 && !string.IsNullOrEmpty(_settings.CustomSoundPath) ? System.IO.Path.GetFileNameWithoutExtension(_settings.CustomSoundPath) : sndNames[Math.Min(sel, sndNames.Length-1)];
                if (txt.Length > 10) txt = txt.Substring(0, 9) + "..";
                using (var f = new Font("Segoe UI", 7.5f)) using (var b = new SolidBrush(txtC)) g2.DrawString(txt, f, b, Dpi.S(6), Dpi.S(4));
                float cx2 = btn.Width - Dpi.S(12), cy2 = btn.Height / 2f;
                using (var p = new Pen(chevC, Dpi.S(2))) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g2.DrawLine(p, cx2 - Dpi.S(3), cy2 - Dpi.S(2), cx2, cy2 + Dpi.S(2)); g2.DrawLine(p, cx2, cy2 + Dpi.S(2), cx2 + Dpi.S(3), cy2 - Dpi.S(2)); }
            };
            btn.MouseEnter += (s2, e2) => { hov = true; btn.Invalidate(); };
            btn.MouseLeave += (s2, e2) => { hov = false; btn.Invalidate(); };
            card.Controls.Add(btn);

            btn.MouseClick += (s2, e2) => {
                if (!btn.Enabled) return; // Don't open dropdown when disabled
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
                        } else { sel = ci; onChange(ci); btn.Invalidate(); TrayApp.PreviewFeedbackSound(ci, getVolume != null ? getVolume() : 80); }
                    }
                };
                popForm.Controls.Add(popPanel);
                popForm.Deactivate += (s3, e3) => { if (!popForm.IsDisposed) popForm.Close(); _activeDropdownPopup = null; };
                _activeDropdownPopup = popForm;
                popForm.Show();
            };
            return btn;
        }
        /// <summary>Creates a compact volume slider (CardSlider) positioned below the sound dropdown.</summary>
        CardSlider MakeVolumeSlider(int y, Panel card, int initialValue, Action<int> onChange) {
            var hiddenSlider = new SlickSlider {
                Minimum = 0, Maximum = 100, Value = Math.Max(0, Math.Min(100, initialValue)),
                Size = Dpi.Size(86, 22),
                Location = Dpi.Pt(0, 0),
                Visible = false
            };
            hiddenSlider.ValueChanged += (s, e) => { if (onChange != null) onChange(hiddenSlider.Value); if (card != null) card.Invalidate(); };
            card.Controls.Add(hiddenSlider);
            var cs = new CardSlider {
                PixelX = Dpi.S(330), PixelY = Dpi.S(y + 26),
                PixelW = Dpi.S(86), PixelH = Dpi.S(20),
                Source = hiddenSlider, Card = card
            };
            if (!_cardSliderMap.ContainsKey(card)) _cardSliderMap[card] = new List<CardSlider>();
            _cardSliderMap[card].Add(cs);
            return cs;
        }
        void UpdateKey2Visibility() { LayoutPttKeys(); }

        /// <summary>Paint handler for all hotkey labels: breathing theme border + spinning rainbow trail.</summary>
        void PaintHotkeyLabel(object sender, PaintEventArgs e) {
            var lbl = (Label)sender;
            if (!string.IsNullOrEmpty(lbl.Text)) { lbl.Tag = lbl.Text; lbl.Text = ""; }
            var g = e.Graphics;
            int w = lbl.Width - 1, h = lbl.Height - 1;
            if (w < 5 || h < 5) return;

            Color themeColor = GetKeyTheme(lbl);
            string lblTag = lbl.Tag as string;
            bool assigned = (lblTag != "Add Key" && lblTag != "Add MM Key" && lblTag != "Press..." && !string.IsNullOrEmpty(lblTag));
            
            if (!assigned) {
                // Not assigned — match FlatStyle.Flat button look exactly
                bool isCapture = (lblTag == "Press..." && _audio.IsCapturing);
                if (isCapture) {
                    // Capture mode: fill with theme color, white text
                    using (var b = new SolidBrush(themeColor)) g.FillRectangle(b, 0, 0, lbl.Width, lbl.Height);
                    using (var p = new Pen(Color.FromArgb(_animBreathAlpha, 255, 255, 255))) g.DrawRectangle(p, 0, 0, w, h);
                    TextRenderer.DrawText(g, lblTag, lbl.Font, new Rectangle(0,0,lbl.Width,lbl.Height), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                } else {
                    // Fill background explicitly (matches Button BackColor)
                    using (var bg = new SolidBrush(lbl.BackColor)) g.FillRectangle(bg, 0, 0, lbl.Width, lbl.Height);
                    // 1px border using same R/3,G/3,B/3 formula as FlatAppearance.BorderColor
                    using (var p = new Pen(Color.FromArgb(themeColor.R/3, themeColor.G/3, themeColor.B/3), 1f)) g.DrawRectangle(p, 0, 0, w, h);
                    string t = !string.IsNullOrEmpty(lblTag) ? lblTag : "";
                    if (!string.IsNullOrEmpty(t)) {
                        using (var sf = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center })
                        using (var br = new SolidBrush(themeColor))
                            g.DrawString(t, lbl.Font, br, new RectangleF(0, 0, lbl.Width, lbl.Height), sf);
                    }
                }
                return;
            }

            // === ASSIGNED KEY ===
            bool isMmKey = (lbl == _lblMmPtt || lbl == _lblMm2Ptt || 
                            lbl == _lblMmPtm || lbl == _lblMm2Ptm || 
                            lbl == _lblMmToggle || lbl == _lblMm2Toggle);
            if (isMmKey) {
                // MM key: lightweight purple breathing pulse
                int glowAlpha = Math.Max(30, _animBreathAlpha);
                Color mmColor = Color.FromArgb(140, 80, 255); // Purple identifier
                
                using (var bgBrush = new SolidBrush(lbl.BackColor)) g.FillRectangle(bgBrush, 0, 0, w, h);
                using (var fillBr = new SolidBrush(Color.FromArgb(glowAlpha / 4, mmColor))) g.FillRectangle(fillBr, 0, 0, w, h);
                using (var p = new Pen(Color.FromArgb(glowAlpha, mmColor), 1.5f)) g.DrawRectangle(p, 0, 0, w, h);
            } else {
                // Clean static themed border, or dark grey if disabled
                bool manuallyDisabled = (lbl == _lblMmToggle && _tglPtToggle != null && !_tglPtToggle.Checked);
                Color borderColor = !manuallyDisabled ? Color.FromArgb(themeColor.R/2, themeColor.G/2, themeColor.B/2) : Color.FromArgb(40, 40, 40);
                using (var p = new Pen(borderColor, 1f)) g.DrawRectangle(p, 0, 0, w, h);
            }
            // Draw text ON TOP
            string txt = !string.IsNullOrEmpty(lbl.Tag as string) ? (string)lbl.Tag : "";
            if (!string.IsNullOrEmpty(txt)) {
                bool isTextDimmed = (lbl == _lblMmToggle && _tglPtToggle != null && !_tglPtToggle.Checked);
                using (var sf = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center })
                using (var br = new SolidBrush(!isTextDimmed ? Color.White : Color.FromArgb(100, 100, 100)))
                    g.DrawString(txt, lbl.Font, br, new RectangleF(0, 0, lbl.Width, lbl.Height), sf);
            }
        }

        static PointF PerimPoint(float dist, int w, int h) {
            float perim = 2f * w + 2f * h;
            while (dist < 0) dist += perim;
            while (dist >= perim) dist -= perim;
            if (dist < w) return new PointF(dist, 0);
            dist -= w;
            if (dist < h) return new PointF(w, dist);
            dist -= h;
            if (dist < w) return new PointF(w - dist, h);
            dist -= w;
            return new PointF(0, h - dist);
        }

        static Color HsvToRgb(float h, float s, float v, int a) {
            h = h % 360f; if (h < 0) h += 360f;
            float c = v * s, x = c * (1f - Math.Abs((h / 60f) % 2f - 1f)), m = v - c;
            float r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb(Math.Min(255, a), (int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        int LayoutHotkeyRow(int startY, int startX, Label lbl1, Button rem1, Label lbl2, Button rem2, Button add2, 
                            int key1, int key2, 
                            CaptureTarget t1, CaptureTarget t2, bool isActive = true) {
            int kbW = 80, gap = 4;
            int x = startX;
            if (lbl1 == null) return x;
            lbl1.Size = Dpi.Size(kbW, 26); lbl1.Location = Dpi.Pt(x, startY); lbl1.Visible = isActive;
            if (!isActive) {
                if (rem1 != null) rem1.Visible = false; if (lbl2 != null) lbl2.Visible = false;
                if (rem2 != null) rem2.Visible = false; if (add2 != null) add2.Visible = false;
                return x;
            }
            x += kbW + gap;
            bool has1 = key1 > 0 || _audio.CurrentTarget == t1;
            bool has2 = key2 > 0 || _audio.CurrentTarget == t2;
            // Show Key1 remove button when a key is assigned, advance x by actual button width
            if (rem1 != null) {
                int rw1 = (int)(rem1.Width / Dpi.Scale);
                if (rw1 < 1) rw1 = 18;
                if (has1) { rem1.Location = Dpi.Pt(x, startY + 4); rem1.Visible = true; x += rw1 + gap; }
                else { rem1.Visible = false; }
            }

            if (lbl2 != null) {
                if (has2) { lbl2.Size = Dpi.Size(kbW, 26); lbl2.Location = Dpi.Pt(x, startY); lbl2.Visible = true; x += kbW + gap; 
                    if (rem2 != null) { int rw2 = (int)(rem2.Width / Dpi.Scale); if (rw2 < 1) rw2 = 18; rem2.Location = Dpi.Pt(x, startY+4); rem2.Visible = true; x += rw2+gap; }
                } else { lbl2.Visible = false; if (rem2 != null) rem2.Visible = false; }
            }

            bool showAdd2 = has1 && !has2 && !_audio.IsCapturing;
            if (showAdd2 && add2 != null) { add2.Location = Dpi.Pt(x, startY); add2.Visible = true; x += add2.Width + gap; } 
            else if (add2 != null) { add2.Visible = false; }
            
            return x;
        }

        int LayoutPttKeys() {
            return LayoutHotkeyRow(_basePttY, 114, _lblPttKey, _btnRemoveKey, _lblPttKey2, _btnRemoveKey2, _btnAddKey2,
                                   _pttKeyCode, _pttKeyCode2, CaptureTarget.PttKey1, CaptureTarget.PttKey2);
        }

        int LayoutPtmKeys() {
            return LayoutHotkeyRow(_basePtmY, 114, _lblPtmKey, _btnPtmRemKey, _lblPtmKey2, _btnPtmRemKey2, _btnPtmAddKey2,
                                   _ptmKeyCode, _ptmKeyCode2, CaptureTarget.PtmKey1, CaptureTarget.PtmKey2);
        }

        int LayoutToggleKeys() {
            return LayoutHotkeyRow(_baseTogY, 114, _lblPtToggleKey, _btnToggleRemKey, _lblPtToggleKey2, _btnToggleRemKey2, _btnToggleAddKey2,
                                   _ptToggleKeyCode, _ptToggleKeyCode2, CaptureTarget.ToggleKey1, CaptureTarget.ToggleKey2);
        }

        void LayoutAllMm() {
            // Only Toggle section has MM key — PTT/PTM cleared for future volume slider
            if (_lblMmPtt != null) _lblMmPtt.Visible = false;
            if (_lblMm2Ptt != null) _lblMm2Ptt.Visible = false;
            if (_btnMmRemPtt != null) _btnMmRemPtt.Visible = false;
            if (_btnMm2RemPtt != null) _btnMm2RemPtt.Visible = false;
            if (_btnMm2AddPtt != null) _btnMm2AddPtt.Visible = false;
            if (_lblMmPtm != null) _lblMmPtm.Visible = false;
            if (_lblMm2Ptm != null) _lblMm2Ptm.Visible = false;
            if (_btnMmRemPtm != null) _btnMmRemPtm.Visible = false;
            if (_btnMm2RemPtm != null) _btnMm2RemPtm.Visible = false;
            if (_btnMm2AddPtm != null) _btnMm2AddPtm.Visible = false;
            // Single MM key only — hide key2 controls
            if (_lblMm2Toggle != null) _lblMm2Toggle.Visible = false;
            if (_btnMm2RemToggle != null) _btnMm2RemToggle.Visible = false;
            if (_btnMm2AddToggle != null) _btnMm2AddToggle.Visible = false;
            // Layout the one real MM key in Toggle section
            LayoutHotkeyRow(_baseTogY, 350, _lblMmToggle, _btnMmRemToggle, null, null, null,
                            _mmKeyCode, 0, CaptureTarget.MmKey, CaptureTarget.MmKey2);
        }

        void LayoutDictKeys() {
            bool pthEnabled = (_cmbDictEngine == null) || _cmbDictEngine.SelectedIndex > 0;
            LayoutHotkeyRow(_baseDictHldY, 114, _lblDictKey, _btnRmDictPth1, _lblDictKey2, _btnRmDictPth2, _btnAddDictPth2,
                            _dictKeyCode, _dictKeyCode2, CaptureTarget.DictationKey1, CaptureTarget.DictationKey2, pthEnabled);
            LayoutHotkeyRow(_baseDictTogY, 114, _lblDictToggleKey, _btnRmDictPtt1, _lblDictToggleKey2, _btnRmDictPtt2, _btnAddDictPtt2,
                            _dictToggleKeyCode, _dictToggleKeyCode2, CaptureTarget.DictationToggleKey, CaptureTarget.DictationToggleKey2, true);
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
            var shakeTimer = new Timer { Interval = 16 };
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

        /// <summary>Flash a control 4 times with a green highlight to draw attention. Works on any Control.</summary>
        void FlashControl(Control ctrl) {
            if (ctrl == null || ctrl.IsDisposed) return;
            var origBg = ctrl.BackColor;
            Color flashColor = Color.FromArgb(40, 80, 200, 120);
            int tick = 0;
            var timer = new Timer { Interval = 150 };
            timer.Tick += (s, e) => {
                tick++;
                if (tick > 8) { timer.Stop(); timer.Dispose(); ctrl.BackColor = origBg; ctrl.Invalidate(); if (ctrl.Parent != null) ctrl.Parent.Invalidate(); return; }
                ctrl.BackColor = (tick % 2 == 1) ? flashColor : origBg;
                ctrl.Invalidate();
                if (ctrl.Parent != null) ctrl.Parent.Invalidate();
            };
            timer.Start();
        }

        /// <summary>Flash a CardSlider by temporarily tinting its track. Uses card invalidation for painted sliders.</summary>
        private CardSlider _flashingSlider;
        private int _flashSliderTick;
        void FlashSlider(CardSlider cs) {
            if (cs == null || cs.Card == null) return;
            _flashingSlider = cs;
            _flashSliderTick = 0;
            var timer = new Timer { Interval = 150 };
            timer.Tick += (s, e) => {
                _flashSliderTick++;
                if (_flashSliderTick > 8) { timer.Stop(); timer.Dispose(); _flashingSlider = null; cs.Card.Invalidate(); return; }
                cs.Card.Invalidate();
            };
            timer.Start();
        }


        /// <summary>Wire right-click navigation on all duck and eye icons.</summary>
        void WireIconRightClicks() {
            // Eye icons: right-click → General page (pane 4), flash overlay toggle
            Action eyeRightClick = () => { NavigateToPane(4); BeginInvoke(new Action(() => { if (_tglOverlay != null) FlashControl(_tglOverlay); })); };
            if (_chkKey1Overlay != null) _chkKey1Overlay.OnRightClick = eyeRightClick;
            if (_chkPtmOverlay != null) _chkPtmOverlay.OnRightClick = eyeRightClick;
            if (_chkToggleOverlay != null) _chkToggleOverlay.OnRightClick = eyeRightClick;
            if (_dictPthOvr != null) _dictPthOvr.OnRightClick = eyeRightClick;
            if (_dictPttOvr != null) _dictPttOvr.OnRightClick = eyeRightClick;

            // Duck icons on PTT/PTM/Toggle (pane 0): right-click → Dictation page (pane 2), flash Microphone slider
            Action micDuckRightClick = () => { NavigateToPane(2); BeginInvoke(new Action(() => { if (_csPttDuckVol != null) FlashSlider(_csPttDuckVol); })); };
            if (_duckPtt != null) _duckPtt.OnRightClick = micDuckRightClick;
            if (_duckPtm != null) _duckPtm.OnRightClick = micDuckRightClick;
            if (_duckToggle != null) _duckToggle.OnRightClick = micDuckRightClick;

            // Duck icons on Dictation page (pane 2): right-click → flash Dictation slider (already on right page)
            Action dictDuckRightClick = () => { if (_csDuckVol != null) FlashSlider(_csDuckVol); };
            if (_duckDictPth != null) _duckDictPth.OnRightClick = dictDuckRightClick;
            if (_duckDictPtt != null) _duckDictPtt.OnRightClick = dictDuckRightClick;
        }
        /// <summary>Grey out or restore all sound-related controls based on RestrictSoundOutput setting.</summary>
        void ApplySoundRestriction() {
            bool restrict = _settings.RestrictSoundOutput;
            // Speaker icons
            if (_chkPttSound != null) _chkPttSound.Dimmed = restrict;
            if (_chkPtmSound != null) _chkPtmSound.Dimmed = restrict;
            if (_chkToggleSound != null) _chkToggleSound.Dimmed = restrict;
            // Sound dropdowns
            if (_drpPttSound != null) _drpPttSound.Enabled = !restrict;
            if (_drpPtmSound != null) _drpPtmSound.Enabled = !restrict;
            if (_drpToggleSound != null) _drpToggleSound.Enabled = !restrict;
            // Volume sliders
            if (_csPttVol != null && _csPttVol.Source != null) _csPttVol.Source.Enabled = !restrict;
            if (_csPtmVol != null && _csPtmVol.Source != null) _csPtmVol.Source.Enabled = !restrict;
            if (_csToggleVol != null && _csToggleVol.Source != null) _csToggleVol.Source.Enabled = !restrict;
            // Invalidate cards
            if (_pttCard != null) _pttCard.Invalidate();
        }
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
            _tglPtt.Checked=_settings.PushToTalkEnabled;_tglPtm.Checked=_settings.PushToMuteEnabled;_tglPtToggle.Checked=_settings.PushToToggleEnabled;_pttKeyCode=_settings.PushToTalkEnabled?_settings.PushToTalkKey:0;_lblPttKey.Text=_pttKeyCode>0?KeyName(_pttKeyCode):"Add Key";_pttKeyCode2=_settings.PushToTalkEnabled?_settings.PushToTalkKey2:0;_key1ShowOverlay=_settings.PttKey1ShowOverlay;_key2ShowOverlay=_settings.PttKey2ShowOverlay;if(_chkKey1Overlay!=null)_chkKey1Overlay.Checked=_key1ShowOverlay;if(_chkKey2Overlay!=null)_chkKey2Overlay.Checked=_key2ShowOverlay;if(_lblPttKey2!=null){_lblPttKey2.Text=_pttKeyCode2>0?KeyName(_pttKeyCode2):"";UpdateKey2Visibility();}
            if(_lblPtmKey!=null){_ptmKeyCode=_settings.PushToMuteEnabled?_settings.PushToMuteKey:0;_ptmKeyCode2=_settings.PushToMuteEnabled?_settings.PushToMuteKey2:0;_lblPtmKey.Text=_ptmKeyCode>0?KeyName(_ptmKeyCode):"Add Key";if(_lblPtmKey2!=null)_lblPtmKey2.Text=_ptmKeyCode2>0?KeyName(_ptmKeyCode2):"";}if(_lblPtToggleKey!=null){_ptToggleKeyCode=_settings.PushToToggleEnabled?_settings.PushToToggleKey:0;_ptToggleKeyCode2=_settings.PushToToggleEnabled?_settings.PushToToggleKey2:0;_lblPtToggleKey.Text=_ptToggleKeyCode>0?KeyName(_ptToggleKeyCode):"Add Key";if(_lblPtToggleKey2!=null)_lblPtToggleKey2.Text=_ptToggleKeyCode2>0?KeyName(_ptToggleKeyCode2):"";}_tglOverlay.Checked=_settings.MicOverlayEnabled;
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
            if (_tglRestrictSound != null) _tglRestrictSound.Checked = _settings.RestrictSoundOutput;
            // Voice Activity
            if (_tglVoiceActivity != null) _tglVoiceActivity.Checked = _settings.VoiceActivityEnabled;
            // Night Mode
            // _tglNightMode removed — NightModeEnabled is now controlled via preset dropdown
            // EQ enhancements
            // Load persisted EQ bands
            try {
                var parts = (_settings.EqBands ?? "").Split(new char[]{'|'});
                if (parts.Length == 10) {
                    for (int i = 0; i < 10; i++) {
                        float v;
                        if (float.TryParse(parts[i].Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out v))
                            _eqBands[i] = Math.Max(0f, Math.Min(1f, v));
                        else _eqBands[i] = 0.5f;
                    }
                } else { Array.Copy(EQ_FLAT, _eqBands, 10); }
                Array.Copy(_eqBands, _eqTargets, 10);
                if (_cmbEqPreset != null) AutoDetectPreset();
            } catch { Array.Copy(EQ_FLAT, _eqBands, 10); Array.Copy(EQ_FLAT, _eqTargets, 10); }
            // MM key
            _mmKeyCode = _settings.MultiModeKey;
            _mmKeyCode2 = _settings.MultiModeKey2;
            UpdateMmKeyLabels();
            // Dictation
            _dictKeyCode = _settings.DictationKey; // key always loaded; toggle controls whether it fires
            _dictToggleKeyCode = _settings.DictationToggleKey;
            if (_lblDictKey != null) _lblDictKey.Text = _dictKeyCode > 0 ? KeyName(_dictKeyCode) : "Add Key";
            if (_lblDictToggleKey != null) _lblDictToggleKey.Text = _dictToggleKeyCode > 0 ? KeyName(_dictToggleKeyCode) : "Add Key";
            if (_cmbDictEngine != null) { _loading = true; _cmbDictEngine.SelectedIndex = Math.Max(0, Math.Min(1, _settings.DictationEngine)); _loading = false; }
            if (_tglDictPushToDict != null) { _loading=true; _tglDictPushToDict.Checked = _settings.DictationEnabled;       _loading=false; }
            if (_tglDictToggle != null)     { _loading=true; _tglDictToggle.Checked     = _settings.DictationToggleEnabled;  _loading=false; }
            if (_trkVoiceThreshold != null) { _trkVoiceThreshold.Value = Clamp(_settings.VoiceActivityThreshold, 1, 100); if (_lblVoiceThresholdPct != null) _lblVoiceThresholdPct.Text = "Threshold: " + _trkVoiceThreshold.Value + "%"; }
            if (_nudVoiceHoldover != null) _nudVoiceHoldover.Value = Clamp(_settings.VoiceActivityHoldoverMs, 200, 5000);
            // Display
            if (_tglDisplay != null) _tglDisplay.Checked = _settings.DisplayEnabled;
            for (int j = 0; j < 6; j++) if (_tglColorFilter[j] != null) _tglColorFilter[j].Checked = (_settings.DisplayFilterType == j);
            if (_tglBlueLightFilter != null) _tglBlueLightFilter.Checked = (_settings.DisplayTempK <= 3500);
            if (_trkTempK != null) _trkTempK.Value = Clamp(_settings.DisplayTempK / 100, 12, 65);
            if (_trkBright != null) _trkBright.Value = Clamp(_settings.DisplayBrightness, 20, 100);
            CompactKeys();
            // Enforce PTT/PTM mutual exclusivity on load (Toggle is the gatekeeper)
            if (!_tglPtToggle.Checked && _tglPtt.Checked && _tglPtm.Checked) {
                _tglPtm.Checked = false;
                _ptmKeyCode=0;_ptmKeyCode2=0;
                if(_lblPtmKey!=null){_lblPtmKey.Text="Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;}
                LayoutPtmKeys();
                _audio.DisablePtmMode();
            }
        }catch(Exception ex){Logger.Error("Options load failed.",ex);}finally{_loading=false;}
            // Post-load: force dict sound icons OFF and auto-enable ducking if dictation is active
            if (_dictPthSnd != null) _dictPthSnd.Checked = false;
            if (_dictPttSnd != null) _dictPttSnd.Checked = false;
            RefreshDuckingSection();
        }

        void DoSave(){
            if (IsCapturingKey) { CancelAllCaptures(); }
            try{
            // All audio settings are already saved live via _audio property setters.
            // Only need to persist app volume rules (not yet on _audio) and compact keys.
            _settings.AppVolumeEnforceEnabled=_tglAppEnf.Checked;_settings.AppVolumeRules=CollectAppRules();
            CompactKeys();

            // Persist EQ band values
            var bandParts = new string[10];
            for (int i = 0; i < 10; i++)
                bandParts[i] = _eqBands[i].ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            _audio.EqBands = string.Join("|", bandParts);

            _settings.Save();

            // Capture values for background wiring BEFORE closing the form
            bool applyCompression = _audio != null && _audio.NightModeEnabled;
            float[] eqSnapshot    = (float[])_eqTargets.Clone();
            bool apoInstalled     = Audio.IsEqualizerAPOInstalled();

            // Close immediately so UI doesn't freeze — wiring runs on background thread
            DialogResult=DialogResult.OK; Close();

            // Audio wiring in background (audiosrv restart can take several seconds)
            System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                // Loudness Equalization via FxProperties + audiosrv restart
                string leErr = Audio.ApplyLoudnessEqualization(applyCompression);
                if (leErr != null) Logger.Warn("Loudness EQ wiring failed: " + leErr);

                // EQ bands via Equalizer APO config (live reload, no restart)
                if (apoInstalled) {
                    string apoErr = Audio.WriteEqualizerAPOConfig(eqSnapshot);
                    if (apoErr != null) Logger.Warn("Equalizer APO write failed: " + apoErr);
                }
            });

        }catch(Exception ex){Logger.Error("Options save failed.",ex);DarkMessage.Show("Save failed: "+ex.Message,"Error");}}

        /// <summary>Sync all three MM key labels/buttons to current _mmKeyCode state.</summary>
        void UpdateMmKeyLabels() {
            string txt1 = _mmKeyCode > 0 ? KeyName(_mmKeyCode) : "Add MM Key";
            bool enabled = _tglPtToggle != null && _tglPtToggle.Checked;
            Color MM = enabled ? Color.FromArgb(160, 90, 210) : Color.FromArgb(100, 100, 100);
            Color bg_has = enabled ? INPUT_BG : Color.FromArgb(18, 18, 18);
            Color bg_none = Color.FromArgb(20, 20, 20);

            // Only Toggle section has the real MM key
            if (_lblMmToggle != null) {
                _lblMmToggle.Tag = txt1;
                _lblMmToggle.Text = "";
                _lblMmToggle.BackColor = _mmKeyCode <= 0 ? bg_none : bg_has;
                _lblMmToggle.ForeColor = MM;
                _lblMmToggle.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
                // Keep Enabled=true so Click events always fire;
                // the Click handler already guards on _tglPtToggle.Checked.
                _lblMmToggle.Enabled = true;
            }
            // Clear key2 — single MM key only
            _mmKeyCode2 = 0; _audio.MmKey2 = 0;
            // Hide all PTT/PTM shadows and key2 controls
            if (_lblMmPtt != null) _lblMmPtt.Visible = false;
            if (_lblMm2Ptt != null) _lblMm2Ptt.Visible = false;
            if (_lblMmPtm != null) _lblMmPtm.Visible = false;
            if (_lblMm2Ptm != null) _lblMm2Ptm.Visible = false;
            if (_lblMm2Toggle != null) _lblMm2Toggle.Visible = false;
            // Hide all key2 buttons
            if (_btnMm2AddPtt != null) _btnMm2AddPtt.Visible = false;
            if (_btnMm2AddPtm != null) _btnMm2AddPtm.Visible = false;
            if (_btnMm2AddToggle != null) _btnMm2AddToggle.Visible = false;
            
            // Set tooltip on the one MM label
            string tip = "Multi-Mode Key \u2014 Hold to talk when toggle is OFF,\nhold to mute when toggle is ON";
            try {
                if (_mmTip != null && _lblMmToggle != null) _mmTip.SetToolTip(_lblMmToggle, tip);
            } catch { }

            RefreshVolumeSliders();
            LayoutAllMm();
        }

        void CompactKeys(){
            // PTT: shift keys left to fill gaps
            if(_pttKeyCode<=0 && _pttKeyCode2>0){_pttKeyCode=_pttKeyCode2;_pttKeyCode2=0;}
            // PTM: shift keys left to fill gaps
            if(_ptmKeyCode<=0 && _ptmKeyCode2>0){_ptmKeyCode=_ptmKeyCode2;_ptmKeyCode2=0;}
            // Toggle: shift keys left to fill gaps
            if(_ptToggleKeyCode<=0 && _ptToggleKeyCode2>0){_ptToggleKeyCode=_ptToggleKeyCode2;_ptToggleKeyCode2=0;}
            
            UpdateKeyStyle(_lblPttKey, _pttKeyCode<=0, _pttKeyCode>0?KeyName(_pttKeyCode):"Add Key");
            UpdateKeyStyle(_lblPttKey2, false, _pttKeyCode2>0?KeyName(_pttKeyCode2):""); if (_lblPttKey2!=null) _lblPttKey2.Visible = _pttKeyCode2>0;
            
            UpdateKeyStyle(_lblPtmKey, _ptmKeyCode<=0, _ptmKeyCode>0?KeyName(_ptmKeyCode):"Add Key");
            UpdateKeyStyle(_lblPtmKey2, false, _ptmKeyCode2>0?KeyName(_ptmKeyCode2):""); if (_lblPtmKey2!=null) _lblPtmKey2.Visible = _ptmKeyCode2>0;
            
            UpdateKeyStyle(_lblPtToggleKey, _ptToggleKeyCode<=0, _ptToggleKeyCode>0?KeyName(_ptToggleKeyCode):"Add Key");
            UpdateKeyStyle(_lblPtToggleKey2, false, _ptToggleKeyCode2>0?KeyName(_ptToggleKeyCode2):""); if (_lblPtToggleKey2!=null) _lblPtToggleKey2.Visible = _ptToggleKeyCode2>0;
            
            _settings.PushToTalkKey = _pttKeyCode;
            _settings.PushToTalkKey2 = _pttKeyCode2;
            _settings.PushToMuteKey = _ptmKeyCode;
            _settings.PushToMuteKey2 = _ptmKeyCode2;
            _settings.PushToToggleKey = _ptToggleKeyCode;
            _settings.PushToToggleKey2 = _ptToggleKeyCode2;
            _settings.MultiModeKey = _mmKeyCode;
            _settings.MultiModeKey2 = _mmKeyCode2;

            // Dictation: compact key2 slots
            if (_dictKeyCode <= 0 && _dictKeyCode2 > 0) { _dictKeyCode = _dictKeyCode2; _dictKeyCode2 = 0; }
            if (_dictToggleKeyCode <= 0 && _dictToggleKeyCode2 > 0) { _dictToggleKeyCode = _dictToggleKeyCode2; _dictToggleKeyCode2 = 0; }
            UpdateKeyStyle(_lblDictKey, _dictKeyCode<=0, _dictKeyCode>0 ? KeyName(_dictKeyCode) : "Add Key");
            UpdateKeyStyle(_lblDictKey2, false, _dictKeyCode2>0 ? KeyName(_dictKeyCode2) : ""); if (_lblDictKey2!=null) _lblDictKey2.Visible = _dictKeyCode2>0;
            UpdateKeyStyle(_lblDictToggleKey, _dictToggleKeyCode<=0, _dictToggleKeyCode>0 ? KeyName(_dictToggleKeyCode) : "Add Key");
            UpdateKeyStyle(_lblDictToggleKey2, false, _dictToggleKeyCode2>0 ? KeyName(_dictToggleKeyCode2) : ""); if (_lblDictToggleKey2!=null) _lblDictToggleKey2.Visible = _dictToggleKeyCode2>0;
            _settings.DictationKey = _dictKeyCode; _settings.DictationKey2 = _dictKeyCode2;
            _settings.DictationToggleKey = _dictToggleKeyCode; _settings.DictationToggleKey2 = _dictToggleKeyCode2;

            LayoutPttKeys();
            LayoutPtmKeys();
            LayoutToggleKeys();
            LayoutDictKeys();
        }

        Dictionary<string,int> ParseAppRules(){ return CollectAppRules(); }
        static int Clamp(int v,int min,int max){return v<min?min:v>max?max:v;}
        public void OnRunWizard(){var tray=TrayApp.Instance;if(tray!=null)tray.RunSplashFromOptions();}
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            // During key capture, suppress Escape from closing the form — let the polling handler deal with it
            if (_audio != null && _audio.IsCapturing) {
                if (keyData == Keys.Escape) return true; // swallow it — polling will handle the cancel
                return true; // swallow all keys during capture
            }
            if (keyData == Keys.Space) return true;
            // Secret: Ctrl+U on General tab → test update flow
            if (keyData == (Keys.Control | Keys.U) && _activePane == 5) {
                UpdateDialog.ShowUpdate("999");
                return true;
            }
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
                _updateBtn.Text = "\u2B06 Downloading v" + _pendingLatestVer + "\u2026";
                _updateBtn.ForeColor = DarkTheme.Green;
                _updateBtn.FlatAppearance.BorderColor = DarkTheme.Green;
                _updateBtn.BackColor = Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);

                // Auto-download immediately — seamless, no extra click
                _updateBtn.Click -= _updateCheckHandler;
                UpdateDialog.ShowUpdate(_pendingLatestVer);
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

        private EventHandler _updateCheckHandler;

        /// <summary>Compares version strings like "51.1" vs "50.9". Returns positive if a > b.</summary>
        private static int CompareVersions(string a, string b)
        {
            var pa = a.Split(new char[]{'.'}); var pb = b.Split(new char[]{'.'});
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                int va = i < pa.Length ? int.Parse(pa[i].Trim()) : 0;
                int vb = i < pb.Length ? int.Parse(pb[i].Trim()) : 0;
                if (va != vb) return va - vb;
            }
            return 0;
        }

        // Refresh volume slider enable/dim states based on current toggle selections AND MM key assignment
        private void RefreshVolumeSliders()
        {
            if (_loading) return;
            bool hasMm = _mmKeyCode > 0 || _mmKeyCode2 > 0;
            
            // PTT controls (active if PTT is checked OR if MM key is assigned)
            bool pttActive = (_tglPtt != null && _tglPtt.Checked) || hasMm;
            bool pttSoundOn = _chkPttSound != null && _chkPttSound.Checked;
            if (_chkKey1Overlay != null) _chkKey1Overlay.Dimmed = !pttActive;
            if (_chkPttSound != null) _chkPttSound.Dimmed = !pttActive;
            if (_drpPttSound != null) _drpPttSound.Enabled = pttActive && pttSoundOn;
            if (_csPttVol != null) _csPttVol.Source.Enabled = pttActive && pttSoundOn;

            // PTM controls (active if PTM is checked OR if MM key is assigned)
            bool ptmActive = (_tglPtm != null && _tglPtm.Checked) || hasMm;
            bool ptmSoundOn = _chkPtmSound != null && _chkPtmSound.Checked;
            if (_chkPtmOverlay != null) _chkPtmOverlay.Dimmed = !ptmActive;
            if (_chkPtmSound != null) _chkPtmSound.Dimmed = !ptmActive;
            if (_drpPtmSound != null) _drpPtmSound.Enabled = ptmActive && ptmSoundOn;
            if (_csPtmVol != null) _csPtmVol.Source.Enabled = ptmActive && ptmSoundOn;

            // Toggle controls (active only if Toggle is checked)
            bool toggleActive = (_tglPtToggle != null && _tglPtToggle.Checked);
            bool toggleSoundOn = _chkToggleSound != null && _chkToggleSound.Checked;
            if (_chkToggleOverlay != null) _chkToggleOverlay.Dimmed = !toggleActive;
            if (_chkToggleSound != null) _chkToggleSound.Dimmed = !toggleActive;
            if (_drpToggleSound != null) _drpToggleSound.Enabled = toggleActive && toggleSoundOn;
            if (_csToggleVol != null) _csToggleVol.Source.Enabled = toggleActive && toggleSoundOn;
            
            if (_pttCard != null) _pttCard.Invalidate();
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

        // Removed WS_EX_COMPOSITED — it forced ALL children to repaint on every parent
        // invalidate, making shooting star animation cascade to every card at 30fps.
        // BufferedPanel's OptimizedDoubleBuffer handles individual control double-buffering.
        // Matches WelcomeForm (which never had WS_EX_COMPOSITED and works perfectly).
        protected override CreateParams CreateParams {
            get {
                CreateParams cp = base.CreateParams;
                return cp;
            }
        }
    }
}


