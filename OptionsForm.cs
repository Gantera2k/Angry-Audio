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

        private ToggleSwitch _tglAfkMic, _tglAfkSpk, _tglPtt, _tglPtm, _tglPtToggle, _tglMicEnf, _tglSpkEnf, _tglAppEnf, _tglStartup, _tglNotifyCorr, _tglNotifyDev, _tglOverlay, _tglSoundFeedback;
        private NumericUpDown _nudAfkMicSec, _nudAfkSpkSec;
        private Label _lblPttKey, _lblPttKey2, _lblPttKey3, _lblPtmKey, _lblPtToggleKey;
        private Label _lblKey2Label, _lblKey2Hint, _lblKey3Label, _lblKey3Hint;
        private Button _btnRemoveKey2, _btnAddKey2, _btnRemoveKey3, _btnAddKey3;
        private CheckBox _chkKey1Overlay, _chkKey2Overlay, _chkKey3Overlay;
        private bool _key1ShowOverlay = true, _key2ShowOverlay = true, _key3ShowOverlay = true;
        private Timer _pollTimer;
        private int _pttKeyCode = 0, _pttKeyCode2 = 0, _pttKeyCode3 = 0, _ptmKeyCode = 0, _ptToggleKeyCode = 0; private bool _capturingKey, _capturingKey2, _capturingKey3, _capturingPtmKey, _capturingToggleKey, _loading;
        public bool IsCapturingKey { get { return _capturingKey || _capturingKey2 || _capturingKey3 || _capturingPtmKey || _capturingToggleKey; } }
        private SlickSlider _trkMicVol, _trkSpkVol, _sysVolSlider;
        private Label _lblSysVolPct;
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
        static readonly string[] NAV = { "Push-to-Talk", "AFK Protection", "Volume Lock", "Apps", "General" };
        const int SB_W = 155;

        private Action<string> _onToggle; // Callback for instant actuation

        public OptionsForm(Settings settings, Action<string> onToggle = null) {
            _settings = settings;
            _onToggle = onToggle;
            Text = AppVersion.FullName + " \u2014 Options";
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true;
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
            _contentPanel.Paint += (s, e) => { PaintUnifiedStars(e.Graphics, _contentPanel); };

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
                // Hotkey test detection — flash when user presses their assigned hotkey
                if (!_capturingKey && !_capturingKey2 && !_capturingKey3 && _pttKeyCode > 0) {
                    bool hotDown = (GetAsyncKeyState(_pttKeyCode) & 0x8000) != 0;
                    if (!hotDown && _pttKeyCode2 > 0) hotDown = (GetAsyncKeyState(_pttKeyCode2) & 0x8000) != 0;
                    if (!hotDown && _pttKeyCode3 > 0) hotDown = (GetAsyncKeyState(_pttKeyCode3) & 0x8000) != 0;
                    if (hotDown && !_hotkeyWasDown) {
                        FlashModeToggles();
                        // If no toggle is on, remind user they need one
                        if (!_tglPtt.Checked && !_tglPtm.Checked && !_tglPtToggle.Checked)
                            EnforceToggleSelection();
                    }
                    _hotkeyWasDown = hotDown;
                }
            };
            _twinkleTimer.Start();
            // Shooting star animation — occasional streaks across card backgrounds
            _stars = new StarBackground(() => { InvalidateCards(); });
            FormClosing += (s, e) => { if (WindowState == FormWindowState.Normal) { _settings.LastWindowX = Location.X; _settings.LastWindowY = Location.Y; _settings.Save(); } StopCapturePolling(); _captureTimer?.Dispose(); CleanupEnforcement(); _pollTimer?.Stop(); _pollTimer?.Dispose(); _twinkleTimer?.Stop(); _twinkleTimer?.Dispose(); _hotkeyFlashTimer?.Stop(); _hotkeyFlashTimer?.Dispose(); _stars?.Dispose(); _sliderRestoreMicTimer?.Stop(); _sliderRestoreMicTimer?.Dispose(); _sliderRestoreSpkTimer?.Stop(); _sliderRestoreSpkTimer?.Dispose(); _updateShimmerTimer?.Stop(); _updateShimmerTimer?.Dispose(); _saveOrbitTimer?.Stop(); _saveOrbitTimer?.Dispose(); };
        }

        private Size _defaultSize;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
        private Timer _hotkeyFlashTimer;
        private int _hotkeyFlashStep;
        private bool _hotkeyWasDown;

        // === Key capture via GetAsyncKeyState polling ===
        // WndProc/ProcessCmdKey/KeyDown all fail for CapsLock because WM_KEYDOWN
        // goes to the FOCUSED CHILD CONTROL, not the Form. GetAsyncKeyState reads
        // physical hardware state directly — same proven mechanism PushToTalk uses.
        private Timer _captureTimer;
        private bool[] _prevKeyState = new bool[256]; // track previous state to detect transitions

        void StartCapturePolling() {
            // Tell TrayApp to suspend PTT hook so we can capture the key
            if (_onToggle != null) _onToggle("capture_start");
            // Snapshot current key state so we only detect NEW presses
            for (int i = 0; i < 256; i++)
                _prevKeyState[i] = (GetAsyncKeyState(i) & 0x8000) != 0;
            if (_captureTimer == null) {
                _captureTimer = new Timer { Interval = 30 };
                _captureTimer.Tick += CaptureTimerTick;
            }
            Logger.Info("CapturePolling STARTED — timer active, interval=30ms");
            _captureTimer.Start();
        }

        void StopCapturePolling() {
            if (_captureTimer != null) _captureTimer.Stop();
            if (_onToggle != null) _onToggle("capture_stop");
        }

        private int _capturePollCount;
        void CaptureTimerTick(object s, EventArgs e) {
            _capturePollCount++;
            if (!_capturingKey && !_capturingKey2 && !_capturingKey3 && !_capturingPtmKey && !_capturingToggleKey) { StopCapturePolling(); Logger.Info("CapturePolling stopped — no active capture"); return; }
            // Scan for newly pressed keys (transition from up→down)
            for (int vk = 1; vk < 256; vk++) {
                // Skip mouse buttons (1=LMB, 2=RMB, 3=cancel, 4=MMB, 5=X1, 6=X2)
                if (vk >= 1 && vk <= 3) continue;
                short raw = GetAsyncKeyState(vk);
                bool down = (raw & 0x8000) != 0;
                bool wasDown = _prevKeyState[vk];
                _prevKeyState[vk] = down;
                if (down && !wasDown) {
                    Logger.Info("CAPTURED vk=0x" + vk.ToString("X2") + " (" + ((Keys)vk).ToString() + ") after " + _capturePollCount + " polls");
                    _capturePollCount = 0;
                    var ke = new KeyEventArgs((Keys)vk);
                    StopCapturePolling();
                    if (_capturingKey) OnKeyCapture(this, ke);
                    else if (_capturingKey2) OnKeyCapture2(this, ke);
                    else if (_capturingKey3) OnKeyCapture3(this, ke);
                    else if (_capturingPtmKey) OnPtmKeyCapture(this, ke);
                    else if (_capturingToggleKey) OnToggleKeyCapture(this, ke);
                    return;
                }
            }
        }

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
        void InvalidateCardsDeep() {
            try {
                _contentPanel.Invalidate(false);
                for (int i = 0; i < 5; i++) {
                    if (_panes[i] != null && _panes[i].Visible) {
                        foreach (Control c in _panes[i].Controls) c.Invalidate(false);
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
                if (mic >= 0 && _plMicCur != null) _plMicCur.Text = "Current: " + (int)mic + "%";
                if (spk >= 0 && _plSpkCur != null) _plSpkCur.Text = "Current: " + (int)spk + "%";
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
                    if (!IsDisposed) {
                        slider.Value = target;
                        if (isMic) { _settings.MicVolumePercent = target; _micPreLockVol = -1; }
                        else { _settings.SpeakerVolumePercent = target; _spkPreLockVol = -1; }
                        _settings.Save();
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
            _navPanels = new Panel[5]; _navLabels = new Label[5]; _navAccents = new Panel[5];
            var navBox = new Panel { Dock = DockStyle.Fill, BackColor = SB_BG, Padding = Dpi.Pad(0, 6, 0, 0) };
            for (int i = 4; i >= 0; i--) {
                int idx = i;
                var nav = new Panel { Dock = DockStyle.Top, Height = Dpi.S(34), BackColor = SB_BG };
                var ac = new Panel { Location = new Point(0, 0), Size = new Size(Dpi.S(3), Dpi.S(34)), BackColor = SB_BG };
                nav.Controls.Add(ac);
                var lbl = new Label { Text = NAV[idx], Font = new Font("Segoe UI", 9f), ForeColor = TXT3, AutoSize = false, Location = Dpi.Pt(20, 7), Size = Dpi.Size(SB_W - 24, 20) };
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
                int dividerY = Dpi.S(24); // split between the two text lines
                if (e.Y < dividerY) {
                    // Clicked "by Andrew Ganter" — spawn shooting star
                    if (_stars.Shooting != null) _stars.Shooting.ForceLaunchMeteor();
                } else {
                    // Clicked "Your privacy, your rules" — spawn rare event
                    if (_stars.Celestial != null) _stars.Celestial.ForceLaunch();
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
            _activePane = idx;
            _settings.LastActivePane = idx; _settings.Save();
            for (int i = 0; i < 5; i++) {
                bool a = i == idx;
                _navLabels[i].ForeColor = a ? ACC : TXT3;
                _navLabels[i].Font = new Font("Segoe UI", 9f, a ? FontStyle.Bold : FontStyle.Regular);
                _navAccents[i].BackColor = a ? ACC : SB_BG;
                _navPanels[i].BackColor = a ? HOVER : SB_BG;
                _panes[i].Visible = a;
            }
        }

        public void NavigateToPane(int idx) { if (idx >= 0 && idx < 5) SwitchPane(idx); }

        void BuildPanes() {
            _panes = new Panel[5];
            for (int i = 0; i < 5; i++) { _panes[i] = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false, BackColor = BG }; _contentPanel.Controls.Add(_panes[i]); }
            BuildPttPane(_panes[0]); BuildAfkPane(_panes[1]); BuildVolLockPane(_panes[2]); BuildAppsPane(_panes[3]); BuildGeneralPane(_panes[4]);
            // Restore last active pane
            int lastPane = _settings.LastActivePane;
            if (lastPane >= 0 && lastPane < 5) SwitchPane(lastPane); else SwitchPane(0);
        }

        // Title info for card painting
        private string[] _paneTitles = new string[5];
        private string[] _paneSubs = new string[5];
        private string[] _paneBadges = new string[5];

        // === PAINTED LABEL SYSTEM ===
        // All text is drawn in the card Paint handler — zero child controls, zero flicker.
        class PaintedLabel {
            public string Text;
            public int X, Y;
            public Font Font;
            public Color Color;
            public int MaxWidth; // 0 = auto
            public bool RightAlign; // true = X is right edge, text aligns right
        }
        Dictionary<Panel, List<PaintedLabel>> _cardLabels = new Dictionary<Panel, List<PaintedLabel>>();
        Dictionary<Panel, List<int>> _cardLines = new Dictionary<Panel, List<int>>(); // separator Y positions

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
                int rad = Dpi.S(6);

                // 1) Unified starfield — same stars as entire form, seamless
                PaintUnifiedStars(g, c);

                // 2) Frosted glass card — tint + dimmed unified stars
                var cardRect = new Rectangle(0, cardTop, c.Width - 1, c.Height - cardTop - 1);
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
                            g.DrawString(sub, f, b, Dpi.S(16), Dpi.S(28));
                    }
                }

                // 7) All painted labels — drawn directly on glass (no backing rects)
                if (_cardLabels.ContainsKey(c)) {
                    foreach (var lbl in _cardLabels[c]) {
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
        }

        // Tgl now only creates ToggleSwitch — text is painted
        ToggleSwitch Tgl(string label, string sub, int y, Panel card) {
            var t = new ToggleSwitch { Location = Dpi.Pt(16, y) }; t.PaintParentBg = PaintCardBg; card.Controls.Add(t);
            AddText(card, label, 64, y, 9.5f, TXT);
            if (sub != null) AddText(card, sub, 64, y + 19, 7.5f, TXT3);
            return t;
        }
        NumericUpDown Nud(int min, int max, int val, int x, int y, int w) { var n = new PaddedNumericUpDown { Minimum=min,Maximum=max,Value=val, Location=Dpi.Pt(x,y), Size=Dpi.Size(w,24), BackColor=INPUT_BG, ForeColor=TXT, Font=new Font("Segoe UI",9f), BorderStyle=BorderStyle.FixedSingle, TextAlign=HorizontalAlignment.Center };
            n.Leave += (s,e) => { try { int v; if (int.TryParse(n.Text, out v)) { v = Math.Max(min, Math.Min(max, v)); n.Value = v; } } catch { n.Value = val; } };
            return n; }

        // Dynamic painted labels that update at runtime
        private PaintedLabel _plMicCur, _plSpkCur, _plMicVol, _plSpkVol;
        private Panel _volCard; // reference for invalidation on value change

        void BuildAfkPane(Panel pane) {
            var card = MakeCard(1, "AFK Protection", "When you return, audio fades back in over 2s so you're never startled.");
            int y = 64;
            _tglAfkMic = Tgl("Mute Microphone When Idle", "Automatically mutes your mic when you step away.", y, card);
            _tglAfkMic.CheckedChanged += (s,e) => { if (!_loading) {
                _settings.AfkMicMuteEnabled = _tglAfkMic.Checked;
                if (_onToggle != null) _onToggle(_tglAfkMic.Checked ? "afk_mic_on" : "afk_mic_off");
                if (_tglAfkMic.Checked) { _loading = true; if (_tglPtt.Checked) _tglPtt.Checked = false; if (_tglPtm.Checked) _tglPtm.Checked = false; if (_tglPtToggle.Checked) _tglPtToggle.Checked = false; _loading = false; }
            } };
            y += 52;
            AddText(card, "After", 64, y+3, 9f, TXT2);
            _nudAfkMicSec = Nud(5,3600,10,104,y,60); card.Controls.Add(_nudAfkMicSec);
            _nudAfkMicSec.ValueChanged += (s,e) => { if (!_loading) { _settings.AfkMicMuteSec = (int)_nudAfkMicSec.Value; _settings.Save(); } };
            AddText(card, "seconds of inactivity", 170, y+3, 9f, TXT2);
            y+=34;
            AddText(card, "\u26A0  Disable this during open mic or voice calls", 64, y+4, 7.5f, DarkTheme.Amber);
            AddText(card, "Mutes all microphones system-wide \u2014 headset, camera mic, USB devices.", 64, y+22, 7f, Color.FromArgb(90, ACC.R, ACC.G, ACC.B));
            y+=48; AddLine(card, y); y+=24;
            _tglAfkSpk = Tgl("Mute Speakers When Idle", "Fades out system audio when you're away.", y, card);
            _tglAfkSpk.CheckedChanged += (s,e) => { if (!_loading) { _settings.AfkSpeakerMuteEnabled = _tglAfkSpk.Checked; if (_onToggle != null) _onToggle(_tglAfkSpk.Checked ? "afk_spk_on" : "afk_spk_off"); } };
            y += 52;
            AddText(card, "After", 64, y+3, 9f, TXT2);
            _nudAfkSpkSec = Nud(5,3600,10,104,y,60); card.Controls.Add(_nudAfkSpkSec);
            _nudAfkSpkSec.ValueChanged += (s,e) => { if (!_loading) { _settings.AfkSpeakerMuteSec = (int)_nudAfkSpkSec.Value; _settings.Save(); } };
            AddText(card, "seconds of inactivity", 170, y+3, 9f, TXT2);
            pane.Controls.Add(card);
        }

        void BuildPttPane(Panel pane) {
            var card = MakeCard(0, "Push-to-Talk", "Set this same key as push-to-talk in Discord, Zoom, Teams, etc.");
            int y = 64;

            // --- PTT SECTION ---
            _tglPtt = Tgl("Enable Push-to-Talk", "Mic stays muted until you hold the hotkey.", y, card);
            _tglPtt.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglPtt.Checked && _pttKeyCode <= 0) { _loading = true; _tglPtt.Checked = false; _loading = false; return; }
                _settings.PushToTalkEnabled = _tglPtt.Checked; _settings.PushToTalkKey = _pttKeyCode; _settings.PushToTalkKey2 = _pttKeyCode2; _settings.PushToTalkKey3 = _pttKeyCode3;
                if (_onToggle != null) _onToggle(_tglPtt.Checked ? "ptt_on" : "ptt_off");
            } };
            y += 40;
            AddText(card, "Hotkey:", 64, y+3, 8f, TXT3);
            _lblPttKey = new Label{Text=_pttKeyCode > 0 ? PushToTalk.GetKeyName(_pttKeyCode) : "Add Key",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(100,28),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(120,y)};
            _lblPttKey.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPttKey.Width-1,_lblPttKey.Height-1); };
            _lblPttKey.MouseEnter += (s,e) => { if(!_capturingKey) _lblPttKey.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPttKey.MouseLeave += (s,e) => { if(!_capturingKey) _lblPttKey.BackColor = INPUT_BG; };
            _lblPttKey.Click += (s,e) => StartKeyCapture(); card.Controls.Add(_lblPttKey);
            _chkKey1Overlay = MakeOverlayCheck(y, card, _key1ShowOverlay, (v) => { _key1ShowOverlay = v; _settings.PttKey1ShowOverlay = v; if(!_loading && _onToggle!=null)_onToggle("eye1"); });
            if (_chkKey1Overlay != null) _chkKey1Overlay.Location = Dpi.Pt(226, y+1);
            AddText(card, "Click to change", 258, y+6, 7f, TXT4);
            y += 32;
            // Key 2 row
            _lblKey2Label = new Label{Text="Key 2:",Font=new Font("Segoe UI",8f),ForeColor=TXT3,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(64,y+3)}; card.Controls.Add(_lblKey2Label);
            _lblPttKey2 = new Label{Text=PushToTalk.GetKeyName(_pttKeyCode2),Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(100,28),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(120,y)};
            _lblPttKey2.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPttKey2.Width-1,_lblPttKey2.Height-1); };
            _lblPttKey2.MouseEnter += (s,e) => { if(!_capturingKey2) _lblPttKey2.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPttKey2.MouseLeave += (s,e) => { if(!_capturingKey2) _lblPttKey2.BackColor = INPUT_BG; };
            _lblPttKey2.Click += (s,e) => StartKeyCapture2(); card.Controls.Add(_lblPttKey2);
            _chkKey2Overlay = MakeOverlayCheck(y, card, _key2ShowOverlay, (v) => { _key2ShowOverlay = v; _settings.PttKey2ShowOverlay = v; if(!_loading && _onToggle!=null)_onToggle("eye2"); });
            if (_chkKey2Overlay != null) _chkKey2Overlay.Location = Dpi.Pt(226, y+1);
            _lblKey2Hint = new Label{Text="",Font=new Font("Segoe UI",7f),ForeColor=TXT4,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(258,y+6)}; card.Controls.Add(_lblKey2Hint);
            _btnRemoveKey2 = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(24,24),BackColor=Color.Transparent,TabStop=false,Location=Dpi.Pt(258,y+2)};
            _btnRemoveKey2.FlatAppearance.BorderSize=0; _btnRemoveKey2.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnRemoveKey2.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverRm2 = false;
            _btnRemoveKey2.MouseEnter += (s,e) => { _hoverRm2=true; _btnRemoveKey2.Invalidate(); };
            _btnRemoveKey2.MouseLeave += (s,e) => { _hoverRm2=false; _btnRemoveKey2.Invalidate(); };
            _btnRemoveKey2.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnRemoveKey2.ClientRectangle, _hoverRm2); };
            _btnRemoveKey2.Click += (s,e) => { if (_pttKeyCode3 > 0) { _pttKeyCode2 = _pttKeyCode3; _settings.PushToTalkKey2 = _pttKeyCode3; _lblPttKey2.Text = KeyName(_pttKeyCode2); _key2ShowOverlay = _key3ShowOverlay; _settings.PttKey2ShowOverlay = _key3ShowOverlay; if(_chkKey2Overlay!=null) _chkKey2Overlay.Checked = _key2ShowOverlay; _pttKeyCode3 = 0; _settings.PushToTalkKey3 = 0; _key3ShowOverlay = true; _settings.PttKey3ShowOverlay = true; } else { _pttKeyCode2 = 0; _settings.PushToTalkKey2 = 0; } UpdateKey2Visibility(); };
            card.Controls.Add(_btnRemoveKey2);
            _btnAddKey2 = new Button{Text="+ Add Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(100,28),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Location=Dpi.Pt(120,y)};
            _btnAddKey2.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnAddKey2.MouseEnter += (s,e) => { _btnAddKey2.BackColor=Color.FromArgb(ACC.R/5,ACC.G/5,ACC.B/5); };
            _btnAddKey2.MouseLeave += (s,e) => { _btnAddKey2.BackColor=Color.FromArgb(20,20,20); };
            _btnAddKey2.Click += (s,e) => StartKeyCapture2(); card.Controls.Add(_btnAddKey2);
            UpdateKey2Visibility();
            _lblKey3Label = new Label{Text="Key 3:",Font=new Font("Segoe UI",8f),ForeColor=TXT3,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(290,y+3)}; card.Controls.Add(_lblKey3Label);
            _lblPttKey3 = new Label{Text=PushToTalk.GetKeyName(_pttKeyCode3),Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(100,28),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(340,y)};
            _lblPttKey3.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPttKey3.Width-1,_lblPttKey3.Height-1); };
            _lblPttKey3.MouseEnter += (s,e) => { if(!_capturingKey3) _lblPttKey3.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPttKey3.MouseLeave += (s,e) => { if(!_capturingKey3) _lblPttKey3.BackColor = INPUT_BG; };
            _lblPttKey3.Click += (s,e) => StartKeyCapture3(); card.Controls.Add(_lblPttKey3);
            _chkKey3Overlay = MakeOverlayCheck(y, card, _key3ShowOverlay, (v) => { _key3ShowOverlay = v; _settings.PttKey3ShowOverlay = v; if(!_loading && _onToggle!=null)_onToggle("eye3"); });
            if (_chkKey3Overlay != null) _chkKey3Overlay.Location = Dpi.Pt(446, y+1);
            _btnRemoveKey3 = new Button{Text="",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(24,24),BackColor=Color.Transparent,TabStop=false,Location=Dpi.Pt(446,y+2)};
            _btnRemoveKey3.FlatAppearance.BorderSize=0; _btnRemoveKey3.FlatAppearance.MouseOverBackColor=Color.Transparent; _btnRemoveKey3.FlatAppearance.MouseDownBackColor=Color.Transparent;
            bool _hoverRm3 = false;
            _btnRemoveKey3.MouseEnter += (s,e) => { _hoverRm3=true; _btnRemoveKey3.Invalidate(); };
            _btnRemoveKey3.MouseLeave += (s,e) => { _hoverRm3=false; _btnRemoveKey3.Invalidate(); };
            _btnRemoveKey3.Paint += (s,e) => { PaintRemoveIcon(e.Graphics, _btnRemoveKey3.ClientRectangle, _hoverRm3); };
            _btnRemoveKey3.Click += (s,e) => { _pttKeyCode3 = 0; _settings.PushToTalkKey3 = 0; UpdateKey3Visibility(); };
            card.Controls.Add(_btnRemoveKey3);
            _btnAddKey3 = new Button{Text="+ Key",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(70,28),ForeColor=ACC,BackColor=Color.FromArgb(20,20,20),Font=new Font("Segoe UI",8f,FontStyle.Bold),TabStop=false,Location=Dpi.Pt(290,y)};
            _btnAddKey3.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            _btnAddKey3.MouseEnter += (s,e) => { _btnAddKey3.BackColor=Color.FromArgb(ACC.R/5,ACC.G/5,ACC.B/5); };
            _btnAddKey3.MouseLeave += (s,e) => { _btnAddKey3.BackColor=Color.FromArgb(20,20,20); };
            _btnAddKey3.Click += (s,e) => StartKeyCapture3(); card.Controls.Add(_btnAddKey3);
            _lblKey3Hint = new Label{Text="",Font=new Font("Segoe UI",7f),ForeColor=TXT4,AutoSize=true,BackColor=Color.Transparent,Location=Dpi.Pt(258,y+6)}; card.Controls.Add(_lblKey3Hint);
            UpdateKey3Visibility();

            y += 38;
            AddLine(card, y); y += 16;

            // --- PTM SECTION ---
            _tglPtm = Tgl("Enable Push-to-Mute", "Mic stays open \u2014 hold the hotkey to mute.", y, card);
            _tglPtm.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglPtm.Checked && _ptmKeyCode <= 0) { _loading = true; _tglPtm.Checked = false; _loading = false; return; }
                _settings.PushToMuteEnabled = _tglPtm.Checked; _settings.PushToMuteKey = _ptmKeyCode;
                if (_onToggle != null) _onToggle(_tglPtm.Checked ? "ptm_on" : "ptm_off");
            } };
            y += 40;
            AddText(card, "Hotkey:", 64, y+3, 8f, TXT3);
            _lblPtmKey = new Label{Text=_ptmKeyCode > 0 ? PushToTalk.GetKeyName(_ptmKeyCode) : "Add Key",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(100,28),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(120,y)};
            _lblPtmKey.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPtmKey.Width-1,_lblPtmKey.Height-1); };
            _lblPtmKey.MouseEnter += (s,e) => { if(!_capturingPtmKey) _lblPtmKey.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPtmKey.MouseLeave += (s,e) => { if(!_capturingPtmKey) _lblPtmKey.BackColor = INPUT_BG; };
            _lblPtmKey.Click += (s,e) => StartPtmKeyCapture(); card.Controls.Add(_lblPtmKey);
            AddText(card, "Click to change", 226, y+6, 7f, TXT4);

            y += 38;
            AddLine(card, y); y += 16;

            // --- TOGGLE SECTION ---
            _tglPtToggle = Tgl("Enable Push-to-Toggle", "Tap once to unmute, tap again to mute.", y, card);
            _tglPtToggle.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglPtToggle.Checked && _ptToggleKeyCode <= 0) { _loading = true; _tglPtToggle.Checked = false; _loading = false; return; }
                _settings.PushToToggleEnabled = _tglPtToggle.Checked; _settings.PushToToggleKey = _ptToggleKeyCode;
                if (_onToggle != null) _onToggle(_tglPtToggle.Checked ? "ptt_toggle_on" : "ptt_toggle_off");
            } };
            y += 40;
            AddText(card, "Hotkey:", 64, y+3, 8f, TXT3);
            _lblPtToggleKey = new Label{Text=_ptToggleKeyCode > 0 ? PushToTalk.GetKeyName(_ptToggleKeyCode) : "Add Key",Font=new Font("Segoe UI",9f,FontStyle.Bold),ForeColor=ACC,BackColor=INPUT_BG,Size=Dpi.Size(100,28),TextAlign=ContentAlignment.MiddleCenter,Location=Dpi.Pt(120,y)};
            _lblPtToggleKey.Paint += (s,e) => { using(var p=new Pen(Color.FromArgb(60,ACC.R,ACC.G,ACC.B))) e.Graphics.DrawRectangle(p,0,0,_lblPtToggleKey.Width-1,_lblPtToggleKey.Height-1); };
            _lblPtToggleKey.MouseEnter += (s,e) => { if(!_capturingToggleKey) _lblPtToggleKey.BackColor = Color.FromArgb(28, 28, 28); };
            _lblPtToggleKey.MouseLeave += (s,e) => { if(!_capturingToggleKey) _lblPtToggleKey.BackColor = INPUT_BG; };
            _lblPtToggleKey.Click += (s,e) => StartToggleKeyCapture(); card.Controls.Add(_lblPtToggleKey);
            AddText(card, "Click to change", 226, y+6, 7f, TXT4);

            y += 40;
            AddText(card, "Mutes all microphones system-wide \u2014 headset, camera mic, USB devices.", 20, y, 7f, Color.FromArgb(90, ACC.R, ACC.G, ACC.B));
            pane.Controls.Add(card);
        }

        void BuildVolLockPane(Panel pane) {
            var card = MakeCard(2, "Volume Lock", "Keep your mic and speaker levels exactly where you set them."); int y = 64;
            _volCard = card;
            _tglMicEnf = Tgl("Lock Microphone Volume", "Prevents apps from silently changing your mic level.", y, card);
            _tglMicEnf.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglMicEnf.Checked) {
                    // Snapshot for slider restore
                    try { _micPreLockVol = (int)Audio.GetMicVolume(); } catch { _micPreLockVol = -1; }
                    if (_sliderRestoreMicTimer != null) { _sliderRestoreMicTimer.Stop(); _sliderRestoreMicTimer.Dispose(); _sliderRestoreMicTimer = null; }
                    // Notify TrayApp BEFORE enforcing so it can snapshot the real pre-lock volume
                    _settings.MicEnforceEnabled = true;
                    if (_onToggle != null) _onToggle("mic_lock_on");
                    try { Audio.SetMicVolume(_trkMicVol.Value); UpdateCurrent(); } catch { }
                } else {
                    _settings.MicEnforceEnabled = false;
                    if (_onToggle != null) _onToggle("mic_lock_off");
                    AnimateSliderRestore(true);
                }
            } };
            y+=48;
            AddText(card, Audio.GetMicName()??"Mic: Unknown", 64, y, 7.5f, TXT4);
            _plMicCur = AddText(card, "Current: --%", 380, y, 7.5f, GREEN); _plMicCur.RightAlign = true;
            y+=22;
            AddText(card, "Lock at:", 64, y+2, 9f, TXT2);
            _trkMicVol = new SlickSlider{Minimum=0,Maximum=100,Value=100,Location=Dpi.Pt(120,y-8),Size=Dpi.Size(180,30)}; _trkMicVol.PaintParentBg = PaintCardBg; card.Controls.Add(_trkMicVol);
            _plMicVol = AddText(card, "100%", 380, y+2, 9.5f, ACC, FontStyle.Bold); _plMicVol.RightAlign = true;
            _trkMicVol.ValueChanged += (s,e) => { _plMicVol.Text=_trkMicVol.Value+"%"; card.Invalidate(); };
            _trkMicVol.DragCompleted += (s,e) => { if (!_loading) { _settings.MicVolumePercent=_trkMicVol.Value; _settings.Save(); if (_tglMicEnf.Checked) try { Audio.SetMicVolume(_trkMicVol.Value); } catch { } } };
            // Max button for mic
            var btnMicMax = new Button{Text="\u266B  Max All",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(82,24),ForeColor=ACC,BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8),Font=new Font("Segoe UI",7.5f,FontStyle.Bold),TabStop=false};
            btnMicMax.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            btnMicMax.MouseEnter+=(s,e)=>{btnMicMax.BackColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);btnMicMax.ForeColor=Color.FromArgb(Math.Min(255,ACC.R+50),Math.Min(255,ACC.G+50),Math.Min(255,ACC.B+50));};
            btnMicMax.MouseLeave+=(s,e)=>{btnMicMax.BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);btnMicMax.ForeColor=ACC;};
            btnMicMax.Click+=(s,e)=>{ AnimateSlider(_trkMicVol, 100, ()=>{ _settings.MicVolumePercent=100; _settings.Save(); if (_tglMicEnf.Checked) try { Audio.SetMicVolume(100); } catch { } UpdateCurrent(); }); AnimateSlider(_trkSpkVol, 100, ()=>{ _settings.SpeakerVolumePercent=100; _settings.Save(); if (_tglSpkEnf.Checked) try { Audio.SetSpeakerVolume(100); } catch { } UpdateCurrent(); }); };
            card.Controls.Add(btnMicMax);
            card.Resize += (s2,e2) => { btnMicMax.Left = card.Width - Dpi.S(82) - Dpi.S(16); btnMicMax.Top = Dpi.S(68); };
            y+=44; AddLine(card, y); y+=24;
            _tglSpkEnf = Tgl("Lock Speaker Volume", "Prevents apps from changing your system volume.", y, card);
            _tglSpkEnf.CheckedChanged += (s,e) => { if (!_loading) {
                if (_tglSpkEnf.Checked) {
                    try { _spkPreLockVol = (int)Audio.GetSpeakerVolume(); } catch { _spkPreLockVol = -1; }
                    if (_sliderRestoreSpkTimer != null) { _sliderRestoreSpkTimer.Stop(); _sliderRestoreSpkTimer.Dispose(); _sliderRestoreSpkTimer = null; }
                    _settings.SpeakerEnforceEnabled = true;
                    if (_onToggle != null) _onToggle("spk_lock_on");
                    try { Audio.SetSpeakerVolume(_trkSpkVol.Value); UpdateCurrent(); } catch { }
                } else {
                    _settings.SpeakerEnforceEnabled = false;
                    if (_onToggle != null) _onToggle("spk_lock_off");
                    AnimateSliderRestore(false);
                }
            } };
            y+=48;
            AddText(card, Audio.GetSpeakerName()??"Speaker: Unknown", 64, y, 7.5f, TXT4);
            _plSpkCur = AddText(card, "Current: --%", 380, y, 7.5f, GREEN); _plSpkCur.RightAlign = true;
            y+=22;
            AddText(card, "Lock at:", 64, y+2, 9f, TXT2);
            _trkSpkVol = new SlickSlider{Minimum=0,Maximum=100,Value=100,Location=Dpi.Pt(120,y-8),Size=Dpi.Size(180,30)}; _trkSpkVol.PaintParentBg = PaintCardBg; card.Controls.Add(_trkSpkVol);
            _plSpkVol = AddText(card, "100%", 380, y+2, 9.5f, ACC, FontStyle.Bold); _plSpkVol.RightAlign = true;
            _trkSpkVol.ValueChanged += (s,e) => { _plSpkVol.Text=_trkSpkVol.Value+"%"; card.Invalidate(); };
            _trkSpkVol.DragCompleted += (s,e) => { if (!_loading) { _settings.SpeakerVolumePercent=_trkSpkVol.Value; _settings.Save(); if (_tglSpkEnf.Checked) try { Audio.SetSpeakerVolume(_trkSpkVol.Value); } catch { } } };

            pane.Controls.Add(card);
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
            var card = MakeCard(3, "Apps", "Lock individual app volumes to specific levels."); int y = 64;
            _tglAppEnf = Tgl("Per-App Volume Enforcement", "Green = current  \u2022  Slider = target", y, card);
            _tglAppEnf.CheckedChanged += (s,e) => { if (!_loading) { _settings.AppVolumeEnforceEnabled = _tglAppEnf.Checked; _settings.AppVolumeRules = CollectAppRules(); if (_onToggle != null) _onToggle(_tglAppEnf.Checked ? "app_lock_on" : "app_lock_off"); } };

            // System volume slider — controls master speaker volume
            var lblSysVol = new Label{Text="System Vol",ForeColor=TXT3,BackColor=Color.Transparent,Font=new Font("Segoe UI",7.5f),Size=Dpi.Size(58,16),TextAlign=ContentAlignment.MiddleRight,Location=Dpi.Pt(0,y+4)};
            card.Controls.Add(lblSysVol);
            int initSpkVol = 100; try { initSpkVol = (int)Audio.GetSpeakerVolume(); } catch {}
            _sysVolSlider = new SlickSlider{Minimum=0,Maximum=100,Value=initSpkVol,Size=Dpi.Size(150,24),Location=Dpi.Pt(0,y+2)};
            _sysVolSlider.PaintParentBg = PaintCardBg;
            _sysVolSlider.ValueChanged += (s,e) => {
                if (_loading) return;
                int val = _sysVolSlider.Value;
                try { Audio.SetSpeakerVolume(val); } catch {}
                _lblSysVolPct.Text = val + "%";
                // Update all app row labels to reflect new system vol
                foreach (var row in _appRows) {
                    if (row.VolLabel != null && row.Slider != null)
                        row.VolLabel.Text = row.Slider.Value + "% / " + val + "%";
                }
            };
            card.Controls.Add(_sysVolSlider);
            _lblSysVolPct = new Label{Text=initSpkVol+"%",ForeColor=ACC,BackColor=Color.Transparent,Font=new Font("Segoe UI",8f,FontStyle.Bold),Size=Dpi.Size(38,16),TextAlign=ContentAlignment.MiddleLeft,Location=Dpi.Pt(0,y+6)};
            card.Controls.Add(_lblSysVolPct);
            // Position system vol slider to the right
            card.Resize += (s2,e2) => {
                int rEdge = card.Width - Dpi.S(16);
                _lblSysVolPct.Left = rEdge - Dpi.S(38);
                _sysVolSlider.Left = _lblSysVolPct.Left - Dpi.S(152);
                _sysVolSlider.Width = Dpi.S(150);
                lblSysVol.Left = _sysVolSlider.Left - Dpi.S(60);
            };
            y+=48;

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

            // Max All button — accent blue, matches Save style
            var btnMaxAll = new Button{Text="\u266B  Max All",FlatStyle=FlatStyle.Flat,Size=Dpi.Size(90,26),ForeColor=ACC,BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8),Location=Dpi.Pt(0,y),Font=new Font("Segoe UI",8.5f,FontStyle.Bold),TabStop=false};
            btnMaxAll.FlatAppearance.BorderColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);
            btnMaxAll.MouseEnter+=(s,e)=>{btnMaxAll.BackColor=Color.FromArgb(ACC.R/3,ACC.G/3,ACC.B/3);btnMaxAll.ForeColor=Color.FromArgb(Math.Min(255,ACC.R+50),Math.Min(255,ACC.G+50),Math.Min(255,ACC.B+50));};
            btnMaxAll.MouseLeave+=(s,e)=>{btnMaxAll.BackColor=Color.FromArgb(ACC.R/8,ACC.G/8,ACC.B/8);btnMaxAll.ForeColor=ACC;};
            btnMaxAll.Click+=(s,e)=>AnimateMaxAllApps();
            card.Controls.Add(btnMaxAll);
            // Position Max All button to the right — uses Resize event
            card.Resize += (s2,e2) => { btnMaxAll.Left = card.Width - Dpi.S(90) - Dpi.S(16); };
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
            if (_appListPanel is ScrollPanel sp) sp.ResetScroll();
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
                    using (var p = new Pen(Color.FromArgb(30,30,30)))
                        e.Graphics.DrawLine(p, Dpi.S(6), 0, row.Width - Dpi.S(6), 0);
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

        void BuildGeneralPane(Panel pane) {
            var card = MakeCard(4, "General", "Startup behavior, notifications, and legal information.");
            // Card glass top is at y=46, separator below this section at y=96.
            int y = 64;
            _tglStartup = Tgl("Start with Windows", null, y, card);
            _tglStartup.CheckedChanged += (s,e) => { if (!_loading) { _settings.StartWithWindows = _tglStartup.Checked; _settings.ApplyStartupSetting(); if (_onToggle != null) _onToggle(_tglStartup.Checked ? "startup_on" : "startup_off"); } };
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
            _tglOverlay.CheckedChanged += (s,e) => { if (!_loading) { _settings.MicOverlayEnabled = _tglOverlay.Checked; if (_onToggle != null) _onToggle(_tglOverlay.Checked ? "overlay_on" : "overlay_off"); } };
            y += 42;
            _tglSoundFeedback = Tgl("PTT Sound Feedback", "Play a click when your mic opens or closes \u2014 so you know it worked without looking.", y, card);
            _tglSoundFeedback.CheckedChanged += (s,e) => { if (!_loading) { _settings.PttSoundFeedback = _tglSoundFeedback.Checked; } };
            y += 42;
            _tglNotifyCorr = Tgl("Volume Correction Alerts", "Show a toast when Angry Audio resets your audio.", y, card);
            _tglNotifyCorr.CheckedChanged += (s,e) => { if (!_loading) { _settings.NotifyOnCorrection = _tglNotifyCorr.Checked; if (_onToggle != null) _onToggle(_tglNotifyCorr.Checked ? "notify_corr_on" : "notify_corr_off"); } };
            y += 42;
            _tglNotifyDev = Tgl("Device Change Alerts", "Notify when a new mic or speaker is detected.", y, card);
            _tglNotifyDev.CheckedChanged += (s,e) => { if (!_loading) { _settings.NotifyOnDeviceChange = _tglNotifyDev.Checked; if (_onToggle != null) _onToggle(_tglNotifyDev.Checked ? "notify_dev_on" : "notify_dev_off"); } };
            y += 42; AddLine(card, y); y += 16;
            // LEGAL section — vertically centered in remaining card space
            AddText(card, "LEGAL", 16, y, 7.5f, TXT3, FontStyle.Bold);
            y += 20;
            AddText(card, AppVersion.Copyright, 16, y, 8f, TXT2);
            y += 18;
            AddText(card, "Unauthorized copying, modification, or distribution prohibited.", 16, y, 7f, TXT4, FontStyle.Regular, 440);
            y += 30;
            card.Dock = DockStyle.None;
            card.Size = new Size(10, Dpi.S(y + 40));
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            pane.Controls.Add(card);
            pane.Layout += (s, e) => { if (card != null) card.Width = pane.ClientSize.Width - 1; };
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
                var saved = g.Save();
                g.TranslateTransform(_optSaveRect.X, _optSaveRect.Y);
                DarkTheme.PaintOrbitingStar(g, _optSaveRect.Width, _optSaveRect.Height, _saveOrbitPhase, cr);
                g.Restore(saved);
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
            if (_lblPttKey == null) return;
            // Brief green flash on the hotkey label to confirm "I heard that"
            var origBg = _lblPttKey.BackColor;
            var origFg = _lblPttKey.ForeColor;
            _hotkeyFlashStep = 0;
            if (_hotkeyFlashTimer == null) {
                _hotkeyFlashTimer = new Timer { Interval = 80 };
                _hotkeyFlashTimer.Tick += (s, e) => {
                    _hotkeyFlashStep++;
                    if (_hotkeyFlashStep == 1) {
                        _lblPttKey.BackColor = Color.FromArgb(20, 180, 80);
                        _lblPttKey.ForeColor = Color.White;
                    } else if (_hotkeyFlashStep == 2) {
                        // Also flash key2/key3 if they match
                        if (_pttKeyCode2 > 0 && _lblPttKey2.Visible) { _lblPttKey2.BackColor = Color.FromArgb(20, 180, 80); _lblPttKey2.ForeColor = Color.White; }
                        if (_pttKeyCode3 > 0 && _lblPttKey3.Visible) { _lblPttKey3.BackColor = Color.FromArgb(20, 180, 80); _lblPttKey3.ForeColor = Color.White; }
                    } else {
                        _lblPttKey.BackColor = origBg; _lblPttKey.ForeColor = origFg;
                        if (_lblPttKey2 != null) { _lblPttKey2.BackColor = INPUT_BG; _lblPttKey2.ForeColor = ACC; }
                        if (_lblPttKey3 != null) { _lblPttKey3.BackColor = INPUT_BG; _lblPttKey3.ForeColor = ACC; }
                        _hotkeyFlashTimer.Stop();
                    }
                };
            }
            _hotkeyFlashTimer.Start();
        }

        void StartKeyCapture(){if(_capturingKey2||_capturingKey3)return;_capturingKey=true;_lblPttKey.Text="Press...";_lblPttKey.BackColor=ACC;_lblPttKey.ForeColor=Color.White;Logger.Info("StartKeyCapture() called");StartCapturePolling();}
        void StartKeyCapture2(){
            if(!_tglPtt.Checked && !_tglPtm.Checked && !_tglPtToggle.Checked){EnforceToggleSelection();return;}
            if(_capturingKey || _capturingKey3){return;}
            _capturingKey2=true;_lblPttKey2.Text="Press...";_lblPttKey2.BackColor=ACC;_lblPttKey2.ForeColor=Color.White;_btnAddKey2.Visible=false;_lblPttKey2.Visible=true;_lblKey2Label.Visible=true;_lblKey2Hint.Visible=true;StartCapturePolling();}
        void OnKeyCapture2(object s,KeyEventArgs e){if(!_capturingKey2)return;e.Handled=true;e.SuppressKeyPress=true;if(e.KeyCode==Keys.Escape){if(_pttKeyCode2==0){UpdateKey2Visibility();}else{_lblPttKey2.Text=KeyName(_pttKeyCode2);}_lblPttKey2.BackColor=INPUT_BG;_lblPttKey2.ForeColor=ACC;_capturingKey2=false;return;}
            int vk=(int)e.KeyCode;
            if (vk == 0x10) vk = IsKeyDown(0xA1) ? 0xA1 : 0xA0;
            if (vk == 0x11) vk = IsKeyDown(0xA3) ? 0xA3 : 0xA2;
            if (vk == 0x12) vk = IsKeyDown(0xA5) ? 0xA5 : 0xA4;
            if(vk>=0xA0&&vk<=0xA5){try{if((GetAsyncKeyState(0xA1)&0x8000)!=0)vk=0xA1;else if((GetAsyncKeyState(0xA0)&0x8000)!=0)vk=0xA0;else if((GetAsyncKeyState(0xA3)&0x8000)!=0)vk=0xA3;else if((GetAsyncKeyState(0xA2)&0x8000)!=0)vk=0xA2;else if((GetAsyncKeyState(0xA5)&0x8000)!=0)vk=0xA5;else if((GetAsyncKeyState(0xA4)&0x8000)!=0)vk=0xA4;}catch{}}
            _pttKeyCode2=vk;
            // Duplicate check
            if(vk==_pttKeyCode || (_pttKeyCode3>0 && vk==_pttKeyCode3) || (_ptmKeyCode>0 && vk==_ptmKeyCode) || (_ptToggleKeyCode>0 && vk==_ptToggleKeyCode)){_capturingKey2=false;_pttKeyCode2=0;_settings.PushToTalkKey2=0;ShakeReject(_lblPttKey2, ()=>{UpdateKey2Visibility();});return;}
            _lblPttKey2.Text=KeyName(_pttKeyCode2);_lblPttKey2.BackColor=INPUT_BG;_lblPttKey2.ForeColor=ACC;_capturingKey2=false;_settings.PushToTalkKey2=_pttKeyCode2;_key2ShowOverlay=true;_settings.PttKey2ShowOverlay=true;if(_chkKey2Overlay!=null)_chkKey2Overlay.Checked=true;UpdateKey2Visibility();if(_onToggle!=null)_onToggle("ptt_key2:"+_pttKeyCode2);}
        void StartKeyCapture3(){
            if(!_tglPtt.Checked && !_tglPtm.Checked && !_tglPtToggle.Checked){EnforceToggleSelection();return;}
            if(_capturingKey || _capturingKey2){return;}
            _capturingKey3=true;_lblPttKey3.Text="Press...";_lblPttKey3.BackColor=ACC;_lblPttKey3.ForeColor=Color.White;_btnAddKey3.Visible=false;_lblPttKey3.Visible=true;_lblKey3Label.Visible=true;_lblKey3Hint.Visible=true;StartCapturePolling();}
        void OnKeyCapture3(object s,KeyEventArgs e){if(!_capturingKey3)return;e.Handled=true;e.SuppressKeyPress=true;if(e.KeyCode==Keys.Escape){if(_pttKeyCode3==0){UpdateKey3Visibility();}else{_lblPttKey3.Text=KeyName(_pttKeyCode3);}_lblPttKey3.BackColor=INPUT_BG;_lblPttKey3.ForeColor=ACC;_capturingKey3=false;return;}
            int vk=(int)e.KeyCode;
            if (vk == 0x10) vk = IsKeyDown(0xA1) ? 0xA1 : 0xA0;
            if (vk == 0x11) vk = IsKeyDown(0xA3) ? 0xA3 : 0xA2;
            if (vk == 0x12) vk = IsKeyDown(0xA5) ? 0xA5 : 0xA4;
            if(vk>=0xA0&&vk<=0xA5){try{if((GetAsyncKeyState(0xA1)&0x8000)!=0)vk=0xA1;else if((GetAsyncKeyState(0xA0)&0x8000)!=0)vk=0xA0;else if((GetAsyncKeyState(0xA3)&0x8000)!=0)vk=0xA3;else if((GetAsyncKeyState(0xA2)&0x8000)!=0)vk=0xA2;else if((GetAsyncKeyState(0xA5)&0x8000)!=0)vk=0xA5;else if((GetAsyncKeyState(0xA4)&0x8000)!=0)vk=0xA4;}catch{}}
            _pttKeyCode3=vk;
            // Duplicate check
            if(vk==_pttKeyCode || (_pttKeyCode2>0 && vk==_pttKeyCode2) || (_ptmKeyCode>0 && vk==_ptmKeyCode) || (_ptToggleKeyCode>0 && vk==_ptToggleKeyCode)){_capturingKey3=false;_pttKeyCode3=0;_settings.PushToTalkKey3=0;ShakeReject(_lblPttKey3, ()=>{UpdateKey3Visibility();});return;}
            _lblPttKey3.Text=KeyName(_pttKeyCode3);_lblPttKey3.BackColor=INPUT_BG;_lblPttKey3.ForeColor=ACC;_capturingKey3=false;_settings.PushToTalkKey3=_pttKeyCode3;_key3ShowOverlay=true;_settings.PttKey3ShowOverlay=true;if(_chkKey3Overlay!=null)_chkKey3Overlay.Checked=true;UpdateKey3Visibility();if(_onToggle!=null)_onToggle("ptt_key3:"+_pttKeyCode3);}
        void UpdateKey3Visibility(){bool hasKey3=_pttKeyCode3>0;_lblPttKey3.Visible=hasKey3;_lblKey3Label.Visible=hasKey3;_lblKey3Hint.Visible=hasKey3;_btnRemoveKey3.Visible=hasKey3;_btnAddKey3.Visible=!hasKey3 && _pttKeyCode2 > 0;if(_chkKey3Overlay!=null)_chkKey3Overlay.Visible=hasKey3;}
        void StartPtmKeyCapture(){if(_capturingKey||_capturingKey2||_capturingKey3||_capturingToggleKey)return;_capturingPtmKey=true;_lblPtmKey.Text="Press...";_lblPtmKey.BackColor=ACC;_lblPtmKey.ForeColor=Color.White;StartCapturePolling();}
        void OnPtmKeyCapture(object s,KeyEventArgs e){if(!_capturingPtmKey)return;e.Handled=true;e.SuppressKeyPress=true;if(e.KeyCode==Keys.Escape){_lblPtmKey.Text=_ptmKeyCode>0?KeyName(_ptmKeyCode):"Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;_capturingPtmKey=false;return;}
            int vk=(int)e.KeyCode;if(vk==0x10)vk=IsKeyDown(0xA1)?0xA1:0xA0;if(vk==0x11)vk=IsKeyDown(0xA3)?0xA3:0xA2;if(vk==0x12)vk=IsKeyDown(0xA5)?0xA5:0xA4;
            if(vk==_pttKeyCode||vk==_pttKeyCode2||vk==_pttKeyCode3||vk==_ptToggleKeyCode){_capturingPtmKey=false;_lblPtmKey.Text=_ptmKeyCode>0?KeyName(_ptmKeyCode):"Add Key";_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;ShakeReject(_lblPtmKey,null);return;}
            _ptmKeyCode=vk;_lblPtmKey.Text=KeyName(_ptmKeyCode);_lblPtmKey.BackColor=INPUT_BG;_lblPtmKey.ForeColor=ACC;_capturingPtmKey=false;_settings.PushToMuteKey=_ptmKeyCode;
            if(!_tglPtm.Checked){_loading=true;_tglPtm.Checked=true;_loading=false;_settings.PushToMuteEnabled=true;if(_onToggle!=null)_onToggle("ptm_on");}
            if(_onToggle!=null)_onToggle("ptm_key:"+_ptmKeyCode);}
        void StartToggleKeyCapture(){if(_capturingKey||_capturingKey2||_capturingKey3||_capturingPtmKey)return;_capturingToggleKey=true;_lblPtToggleKey.Text="Press...";_lblPtToggleKey.BackColor=ACC;_lblPtToggleKey.ForeColor=Color.White;StartCapturePolling();}
        void OnToggleKeyCapture(object s,KeyEventArgs e){if(!_capturingToggleKey)return;e.Handled=true;e.SuppressKeyPress=true;if(e.KeyCode==Keys.Escape){_lblPtToggleKey.Text=_ptToggleKeyCode>0?KeyName(_ptToggleKeyCode):"Add Key";_lblPtToggleKey.BackColor=INPUT_BG;_lblPtToggleKey.ForeColor=ACC;_capturingToggleKey=false;return;}
            int vk=(int)e.KeyCode;if(vk==0x10)vk=IsKeyDown(0xA1)?0xA1:0xA0;if(vk==0x11)vk=IsKeyDown(0xA3)?0xA3:0xA2;if(vk==0x12)vk=IsKeyDown(0xA5)?0xA5:0xA4;
            if(vk==_pttKeyCode||vk==_pttKeyCode2||vk==_pttKeyCode3||vk==_ptmKeyCode){_capturingToggleKey=false;_lblPtToggleKey.Text=_ptToggleKeyCode>0?KeyName(_ptToggleKeyCode):"Add Key";_lblPtToggleKey.BackColor=INPUT_BG;_lblPtToggleKey.ForeColor=ACC;ShakeReject(_lblPtToggleKey,null);return;}
            _ptToggleKeyCode=vk;_lblPtToggleKey.Text=KeyName(_ptToggleKeyCode);_lblPtToggleKey.BackColor=INPUT_BG;_lblPtToggleKey.ForeColor=ACC;_capturingToggleKey=false;_settings.PushToToggleKey=_ptToggleKeyCode;
            if(!_tglPtToggle.Checked){_loading=true;_tglPtToggle.Checked=true;_loading=false;_settings.PushToToggleEnabled=true;if(_onToggle!=null)_onToggle("ptt_toggle_on");}
            if(_onToggle!=null)_onToggle("toggle_key:"+_ptToggleKeyCode);}
        CheckBox MakeOverlayCheck(int y, Panel card, bool initialOn, Action<bool> onChange) {
            // Custom eye toggle — owner-drawn, no text
            var eye = new CheckBox{Checked=initialOn,Appearance=Appearance.Button,FlatStyle=FlatStyle.Flat,Size=Dpi.Size(38,26),Location=Dpi.Pt(405,y),BackColor=Color.Transparent,TabStop=false};
            eye.FlatAppearance.BorderSize=0;
            eye.FlatAppearance.CheckedBackColor=Color.Transparent;
            eye.FlatAppearance.MouseDownBackColor=Color.Transparent;
            eye.FlatAppearance.MouseOverBackColor=Color.Transparent;
            bool hover = false;
            eye.MouseEnter += (s,e) => { hover=true; eye.Invalidate(); };
            eye.MouseLeave += (s,e) => { hover=false; eye.Invalidate(); };
            eye.Paint += (s,e) => {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int w = eye.Width, h = eye.Height;
                float cx = w/2f, cy = h/2f;
                bool on = eye.Checked;
                Color col = on ? (hover ? Color.FromArgb(140,220,255) : ACC) : (hover ? Color.FromArgb(90,90,90) : Color.FromArgb(55,55,55));
                float sc = Dpi.S(1);
                // Eye shape using bezier curves for smooth almond
                using (var path = new System.Drawing.Drawing2D.GraphicsPath()) {
                    // Top lid
                    path.AddBezier(cx-9*sc, cy, cx-4*sc, cy-6*sc, cx+4*sc, cy-6*sc, cx+9*sc, cy);
                    // Bottom lid
                    path.AddBezier(cx+9*sc, cy, cx+4*sc, cy+6*sc, cx-4*sc, cy+6*sc, cx-9*sc, cy);
                    path.CloseFigure();
                    using (var p = new Pen(col, 1.4f*sc)) g.DrawPath(p, path);
                }
                if (on) {
                    // Simple filled pupil
                    float pr = 2.5f*sc;
                    using (var b = new SolidBrush(col)) g.FillEllipse(b, cx-pr, cy-pr, pr*2, pr*2);
                } else {
                    // Diagonal slash
                    using (var p = new Pen(col, 1.6f*sc)) {
                        p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        g.DrawLine(p, cx-7*sc, cy+5*sc, cx+7*sc, cy-5*sc);
                    }
                }
            };
            var tt = new ToolTip();
            tt.SetToolTip(eye, initialOn ? "Popup visible — click to hide" : "Popup hidden — click to show");
            eye.CheckedChanged += (s,e) => {
                tt.SetToolTip(eye, eye.Checked ? "Popup visible — click to hide" : "Popup hidden — click to show");
                onChange(eye.Checked);
            };
            card.Controls.Add(eye);
            return eye;
        }
        void UpdateKey2Visibility(){bool hasKey2=_pttKeyCode2>0;bool hasKey1=_pttKeyCode>0;_lblPttKey2.Visible=hasKey2;_lblKey2Label.Visible=hasKey2;_lblKey2Hint.Visible=hasKey2;_btnRemoveKey2.Visible=hasKey2;_btnAddKey2.Visible=!hasKey2 && hasKey1;if(_chkKey2Overlay!=null)_chkKey2Overlay.Visible=hasKey2;if(_btnAddKey3!=null)UpdateKey3Visibility();}
        void OnKeyCapture(object s,KeyEventArgs e){if(!_capturingKey)return;e.Handled=true;e.SuppressKeyPress=true;if(e.KeyCode==Keys.Escape){_lblPttKey.Text=KeyName(_pttKeyCode);_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;_capturingKey=false;return;}
            // WinForms gives generic modifier VK codes (0x10=Shift, 0x11=Ctrl, 0x12=Alt)
            // but the low-level keyboard hook sees specific left/right codes (0xA0/0xA1, 0xA2/0xA3, 0xA4/0xA5).
            // Translate generic → left-specific so the hook can match.
            int vk = (int)e.KeyCode;
            if (vk == 0x10) vk = IsKeyDown(0xA1) ? 0xA1 : 0xA0; // ShiftKey → LShift or RShift
            if (vk == 0x11) vk = IsKeyDown(0xA3) ? 0xA3 : 0xA2; // ControlKey → LCtrl or RCtrl
            if (vk == 0x12) vk = IsKeyDown(0xA5) ? 0xA5 : 0xA4; // Menu → LAlt or RAlt
            _pttKeyCode=vk;
            // Duplicate check
            if((_pttKeyCode2>0 && vk==_pttKeyCode2)||(_pttKeyCode3>0 && vk==_pttKeyCode3)||(_ptmKeyCode>0 && vk==_ptmKeyCode)||(_ptToggleKeyCode>0 && vk==_ptToggleKeyCode)){_capturingKey=false;_pttKeyCode=_settings.PushToTalkKey;_lblPttKey.Text=KeyName(_pttKeyCode);ShakeReject(_lblPttKey);return;}
            _lblPttKey.Text=KeyName(_pttKeyCode);_lblPttKey.BackColor=INPUT_BG;_lblPttKey.ForeColor=ACC;_capturingKey=false;_settings.PushToTalkKey=_pttKeyCode;UpdateKey2Visibility();if(_onToggle!=null)_onToggle("ptt_key:"+_pttKeyCode);}
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        static bool IsKeyDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
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
                highlights[i].BringToFront();
                tgl.BringToFront();
                foreach (Control c in card.Controls)
                    if (c is Label && c.Top >= tgl.Top - Dpi.S(2) && c.Top <= tgl.Top + Dpi.S(30))
                        c.BringToFront();
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
                    // HIDE highlights — don't leave blue rectangles behind
                    for (int i = 0; i < 3; i++) { highlights[i].Visible = false; }
                    _enforceTimer.Stop();
                    step++;
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
                if (_tglSoundFeedback != null) _tglSoundFeedback.Checked = _settings.PttSoundFeedback;
                _loading = false;
            } catch { _loading = false; }
        }

        /// <summary>Blinks the overlay toggle row 3 times to draw attention.</summary>
        /// <summary>Smoothly animate a slider to a target value, then call onDone.</summary>
        void AnimateSlider(SlickSlider slider, int target, Action onDone)
        {
            if (slider.Value == target) { onDone?.Invoke(); return; }
            var tmr = new Timer { Interval = 16 };
            tmr.Tick += (s, e) => {
                int diff = target - slider.Value;
                int step = Math.Max(1, Math.Abs(diff) / 5);
                if (Math.Abs(diff) <= step) {
                    slider.Value = target;
                    tmr.Stop(); tmr.Dispose();
                    onDone?.Invoke();
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
            _tglPtt.Checked=_settings.PushToTalkEnabled;_tglPtm.Checked=_settings.PushToMuteEnabled;_tglPtToggle.Checked=_settings.PushToToggleEnabled;_pttKeyCode=_settings.PushToTalkKey;_lblPttKey.Text=KeyName(_pttKeyCode);_pttKeyCode2=_settings.PushToTalkKey2;_pttKeyCode3=_settings.PushToTalkKey3;_key1ShowOverlay=_settings.PttKey1ShowOverlay;_key2ShowOverlay=_settings.PttKey2ShowOverlay;_key3ShowOverlay=_settings.PttKey3ShowOverlay;if(_chkKey1Overlay!=null)_chkKey1Overlay.Checked=_key1ShowOverlay;if(_chkKey2Overlay!=null)_chkKey2Overlay.Checked=_key2ShowOverlay;if(_chkKey3Overlay!=null)_chkKey3Overlay.Checked=_key3ShowOverlay;if(_lblPttKey2!=null){_lblPttKey2.Text=_pttKeyCode2>0?KeyName(_pttKeyCode2):"";UpdateKey2Visibility();}if(_lblPttKey3!=null){_lblPttKey3.Text=_pttKeyCode3>0?KeyName(_pttKeyCode3):"";UpdateKey3Visibility();}
            if(_lblPtmKey!=null){_ptmKeyCode=_settings.PushToMuteKey;_lblPtmKey.Text=_ptmKeyCode>0?KeyName(_ptmKeyCode):"Add Key";}if(_lblPtToggleKey!=null){_ptToggleKeyCode=_settings.PushToToggleKey;_lblPtToggleKey.Text=_ptToggleKeyCode>0?KeyName(_ptToggleKeyCode):"Add Key";}_tglOverlay.Checked=_settings.MicOverlayEnabled;
            _tglMicEnf.Checked=_settings.MicEnforceEnabled;_trkMicVol.Value=Clamp(_settings.MicVolumePercent,0,100);_plMicVol.Text=_trkMicVol.Value+"%";
            _tglSpkEnf.Checked=_settings.SpeakerEnforceEnabled;_trkSpkVol.Value=Clamp(_settings.SpeakerVolumePercent,0,100);_plSpkVol.Text=_trkSpkVol.Value+"%";
            _tglAppEnf.Checked=_settings.AppVolumeEnforceEnabled;
            if(_settings.AppVolumeRules!=null&&_settings.AppVolumeRules.Count>0){_appRows.Clear();foreach(var kv in _settings.AppVolumeRules){bool locked=kv.Value>=0;int vol=locked?kv.Value:-(kv.Value+1);_appRows.Add(new AppRuleRow{Name=kv.Key,Locked=locked,InitialValue=Math.Max(0,Math.Min(100,vol))});}RebuildAppList();}
            _tglStartup.Checked=_settings.StartWithWindows;_tglNotifyCorr.Checked=_settings.NotifyOnCorrection;_tglNotifyDev.Checked=_settings.NotifyOnDeviceChange;
        }catch(Exception ex){Logger.Error("Options load failed.",ex);}finally{_loading=false;}}

        void DoSave(){try{
            _settings.AfkMicMuteEnabled=_tglAfkMic.Checked;_settings.AfkMicMuteSec=(int)_nudAfkMicSec.Value;
            _settings.AfkSpeakerMuteEnabled=_tglAfkSpk.Checked;_settings.AfkSpeakerMuteSec=(int)_nudAfkSpkSec.Value;
            _settings.PushToTalkEnabled=_tglPtt.Checked;_settings.PushToMuteEnabled=_tglPtm.Checked;_settings.PushToToggleEnabled=_tglPtToggle.Checked;_settings.PushToTalkKey=_pttKeyCode;_settings.PushToTalkKey2=_pttKeyCode2;_settings.PushToTalkKey3=_pttKeyCode3;_settings.PushToTalkConsumeKey=false;_settings.PushToMuteKey=_ptmKeyCode;_settings.PushToToggleKey=_ptToggleKeyCode;
            _settings.MicOverlayEnabled=_tglOverlay.Checked;
            if (_tglSoundFeedback != null) _settings.PttSoundFeedback=_tglSoundFeedback.Checked;
            _settings.MicEnforceEnabled=_tglMicEnf.Checked;_settings.MicVolumePercent=_trkMicVol.Value;
            _settings.SpeakerEnforceEnabled=_tglSpkEnf.Checked;_settings.SpeakerVolumePercent=_trkSpkVol.Value;
            _settings.AppVolumeEnforceEnabled=_tglAppEnf.Checked;_settings.AppVolumeRules=CollectAppRules();
            _settings.StartWithWindows=_tglStartup.Checked;_settings.NotifyOnCorrection=_tglNotifyCorr.Checked;_settings.NotifyOnDeviceChange=_tglNotifyDev.Checked;
            _settings.Save();DialogResult=DialogResult.OK;Close();
        }catch(Exception ex){Logger.Error("Options save failed.",ex);DarkMessage.Show("Save failed: "+ex.Message,"Error");}}

        Dictionary<string,int> ParseAppRules(){ return CollectAppRules(); }
        static int Clamp(int v,int min,int max){return v<min?min:v>max?max:v;}
        public void OnRunWizard(){DialogResult=DialogResult.Retry;Close();}
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            // Space suppression (prevent scroll) — key capture now handled by polling
            if (_capturingKey || _capturingKey2 || _capturingKey3 || _capturingPtmKey || _capturingToggleKey) return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == Keys.Space) return true;
            if (keyData == Keys.Escape) { Close(); return true; }
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
                    if (!_updateShimmering) { _updateShimmerTimer.Stop(); _updateBtn?.Invalidate(); return; }
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
                    _updateBtn?.Invalidate();
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
                        string latest = raw.Trim().Trim('\uFEFF', '\u200B'); // Strip BOM and zero-width spaces
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
            _twinkleTimer?.Stop();
        }
        protected override void OnResizeEnd(EventArgs e) {
            base.OnResizeEnd(e);
            _isResizing = false;
            _twinkleTimer?.Start();
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
