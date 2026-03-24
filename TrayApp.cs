using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AngryAudio
{
    // AFK state per device
    internal enum AfkState
    {
        Active,
        FadingOut,
        AfkMuted,
        FadingIn
    }

    public class TrayApp : IDisposable
    {
        // --- Win32 for AFK detection ---
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        // --- Win32 for global hotkeys ---
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private const uint MOD_ALT     = 0x0001;
        private const uint MOD_CTRL    = 0x0002;
        private const uint MOD_WIN     = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;
        private const int HOTKEY_DISPLAY    = 9001;  // Alt+P  -- open Display tab
        private const int HOTKEY_CYCLE_NEXT = 9012;  // Alt+E  -- cycle forward
        private const int HOTKEY_CYCLE_PREV = 9013;  // Alt+Q  -- cycle backward
        private const int HOTKEY_INPUT      = 9014;  // Alt+I  -- open Input tab
        private const int HOTKEY_OUTPUT     = 9015;  // Alt+O  -- open Output tab
        private const int HOTKEY_DICT       = 9016;  // Alt+[  -- open Dictation tab
        private const int HOTKEY_AFK        = 9017;  // Alt+]  -- open AFK tab
        private HotkeyWindow _hotkeyWnd;

        // Singleton access for OptionsForm nav buttons
        internal static TrayApp Instance { get; private set; }

        // --- State ---
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _contextMenu;
        private ToolStripMenuItem _pauseMenuItem;
        private ToolStripMenuItem _micListenerMenuItem;
        private System.Threading.Timer _pollTimer;
        private System.Threading.Timer _pttSafetyTimer;
        private bool _pollFast;
        private Settings _settings;
        private AudioSettings _audio;
        private bool _isPaused;
        private bool _disposed;
        private bool _openSettingsOnStart;
        private EventWaitHandle _killEvent;

        // Enforcement timing
        private DateTime _lastMicEnforce = DateTime.MinValue;
        private DateTime _lastSpeakerEnforce = DateTime.MinValue;

        // AFK state per device
        private AfkState _micAfkState = AfkState.Active;
        private AfkState _speakerAfkState = AfkState.Active;
        private int _lastDictToastState = 0;

        // Fade-in state
        private float _micFadeCurrentPercent;
        private float _speakerFadeCurrentPercent;
        private float _micFadeTargetPercent;
        private float _speakerFadeTargetPercent;
        private float _preFadeMicVolume = 100f;
        private float _preFadeSpeakerVolume = 50f;
        private FadeOverlay _fadeOverlay;
        private volatile bool _fadeOverlayCreating;
        private OptionsForm _openOptionsForm;
                private volatile bool _micMutePausedForOpenMic;
        private DateTime _lastOpenMicCheck = DateTime.MinValue;
        private DateTime _lastAppEnforceCheck = DateTime.MinValue;
        private DateTime _lastMicListenerCheck = DateTime.MinValue;
        private string _lastMicListenerKey = "";
        private System.Collections.Generic.List<string> _micListenerNames = new System.Collections.Generic.List<string>();
        private DateTime _lastDeviceChangeCheck = DateTime.MinValue;
        private DateTime _lastTrayUpdate = DateTime.MinValue;
        private string _lastOpenMicApp;

        // Volume lock snapshot/restore — remembers pre-lock volume for smooth restore on unlock
        private int _micPreLockVol = -1;   // -1 = no snapshot
        private int _spkPreLockVol = -1;
        private System.Threading.Timer _restoreMicTimer;
        private System.Threading.Timer _restoreSpkTimer;
        private const int FadeSteps = 10;              // 10 steps (10% per step) — clean
        private const int FadeInStepMs = 300;          // 3 seconds total fade-in (300ms * 10)
        private const int FadeOutStepMs = 300;         // 3 seconds total fade-out (300ms * 10)
        private float _fadeOutStepSize;               // Calculated per-fade from pre-fade volume
        private float _fadeInStepSize;                // Calculated per-fade from target volume
        private DateTime _lastMicFadeStep = DateTime.MinValue;
        private DateTime _lastSpeakerFadeStep = DateTime.MinValue;

        // Device change tracking
        private string _lastMicDeviceId;
        private string _lastSpeakerDeviceId;


        // Notification throttling
        private static readonly TimeSpan NotifyThrottle = TimeSpan.FromSeconds(10);

        // Icon overlays
        private Icon _baseIcon;
        private Icon _pausedIcon;
        private Icon _afkIcon;
        private Icon _errorIcon;
        private Icon _pttIcon;
        private Icon _micHotIcon;

        // Push-to-talk
        private PushToTalk _pushToTalk;
        private DictationManager _dictationManager;
        private readonly object _duckLock = new object();
        private bool _isDucked;
        private bool _duckFading; // true while a fade is in progress (prevents snapshot corruption)
        private float _preDuckVolume = 50f;
        private System.Threading.Timer _duckFadeTimer;
        private MicStatusOverlay _micStatus;
        private bool _vaWasRunning; // tracks OFF→ON transition for Voice Activity toast

        public TrayApp(bool openSettings, bool startPaused)
        {
            Instance = this;
            _openSettingsOnStart = openSettings;
            _isPaused = startPaused;

            // Load settings
            _settings = Settings.Load();
            DisplayManager.InitMag(); // Initialize Magnification API on UI thread at startup
            _audio = new AudioSettings(_settings);
            _audio.InitCaptureContext(); // Capture UI SynchronizationContext for threadpool-based capture timer
            _audio.Changed += OnAudioSettingsChanged;
            _audio.CaptureStateChanged += (capturing) => {
                if (_pushToTalk != null) _pushToTalk.SuspendHook = capturing;
                // When capture ends, trigger deferred engine restart ONLY if one was actually deferred.
                // _deferredPttRestart is set by the SuspendHook guard in OnAudioSettingsChanged.
                if (!capturing && _deferredPttRestart) {
                    _deferredPttRestart = false;
                    OnAudioSettingsChanged(SettingsChange.PttMode);
                }
            };

            // Suppress Equalizer APO's update popup immediately (background thread)
            System.Threading.ThreadPool.QueueUserWorkItem(_ => SuppressAPOPopup());

            // Build icons and tray icon FIRST — user needs to see we're alive
            BuildIcons();
            BuildTrayIcon();

            // Initialize push-to-talk and mic overlay BEFORE welcome dialog
            // so toggle callbacks work during first-run wizard
            _pushToTalk = new PushToTalk();
            _dictationManager = new DictationManager(_audio);
            _dictationManager.PlayStartSound = () => { if (_settings.DictSoundFeedback) PlayFeedbackSound(true, _settings.DictSoundVolume, _settings.DictSoundType); };
            _dictationManager.PlayStopSound  = () => { if (_settings.DictSoundFeedback) PlayFeedbackSound(false, _settings.DictSoundVolume, _settings.DictSoundType); };
            _dictationManager.DuckAudio = () => {
                if (!_settings.DictDuckingEnabled) return;
                lock (_duckLock) {
                    if (_isDucked && !_duckFading) return; // already fully ducked
                    float cur = Audio.GetSpeakerVolume();
                    // Only snapshot if this is a FRESH duck (not re-ducking during a restore fade)
                    if (!_isDucked && !_duckFading) { if (cur >= 0) _preDuckVolume = cur; }
                    float target = cur * _settings.DictDuckingVolume / 100f;
                    _isDucked = true;
                    _duckFading = true;
                    FadeDuckVolume(cur, target, () => { lock (_duckLock) _duckFading = false; });
                }
            };
            _dictationManager.RestoreAudio = () => {
                lock (_duckLock) {
                    if (!_isDucked && !_duckFading) return; // nothing to restore
                    _isDucked = false;
                    _duckFading = true;
                    float cur = Audio.GetSpeakerVolume();
                    FadeDuckVolume(cur, _preDuckVolume, () => { lock (_duckLock) _duckFading = false; });
                }
            };
            _dictationManager.StatusChanged  += OnDictationStatus;
            _dictationManager.TextReady      += OnDictationTextReady;
            // Direct preview actions for instant popup (called from PollTick on UI thread)
            _dictationManager.ShowPreview = () => {
                try {
                    // Hide mic status pill while dictation preview is active
                    if (_micStatus != null && !_micStatus.IsDisposed) _micStatus.HideOverlay();
                    GetDictPreview().ShowListening();
                } catch { }
            };
            _dictationManager.HidePreview = () => {
                try {
                    if (_dictPreview != null && !_dictPreview.IsDisposed) _dictPreview.HideNow();
                } catch { }
            };
            // Pre-create preview handle so first show is instant
            if (_settings.DictShowOverlay) { var p = GetDictPreview(); var h = p.Handle; }
            if ((_settings.DictationEnabled && _settings.DictationKey > 0)
                || (_settings.DictationToggleEnabled && _settings.DictationToggleKey > 0))
                _dictationManager.Start();
            _micStatus = new MicStatusOverlay();
            var _forceHandle = _micStatus.Handle; // Force handle creation for cross-thread InvokeRequired
            _micStatus.OnGoAway += () => {
                _settings.MicOverlayEnabled = false;
                _settings.Save();
                _micStatus.OverlayEnabled = false;
                _micStatus.HideOverlay();
                // Refresh the toggle in any open OptionsForm so it stays in sync
                try { if (_openOptionsForm != null && !_openOptionsForm.IsDisposed) _openOptionsForm.RefreshOverlayToggle(); } catch { }
                ShowBalloonWithAction("Bye for now!", "You can bring me back anytime.",
                    "Come Back!", () => {
                        _settings.MicOverlayEnabled = true;
                        _settings.Save();
                        _micStatus.OverlayEnabled = true;
                        bool pttActive = _pushToTalk != null && _pushToTalk.Enabled;
                        if (pttActive && !_pushToTalk.IsTalking)
                            _micStatus.ShowMicClosed();
                        else if (pttActive && _pushToTalk.IsTalking)
                            _micStatus.ShowMicOpen();
                        else
                            _micStatus.ShowMicClosed();
                        try { if (_openOptionsForm != null && !_openOptionsForm.IsDisposed) _openOptionsForm.RefreshOverlayToggle(); } catch { }
                    },
                    "Options", () => ShowOptions(5, true));
                Logger.Info("User dismissed mic overlay via Go Away.");
            };
            _micStatus.OnOpenOptions += () => { ShowOptions(5, true); };
            _micStatus.OverlayEnabled = _settings.MicOverlayEnabled;
            _micStatus.PushToMuteMode = _settings.PushToMuteEnabled;
            _micStatus.ToggleMode = _settings.PushToToggleEnabled;

            // Start the polling loop BEFORE welcome dialog so AFK/enforcement
            // works immediately when toggled on during first-run wizard
            _pollTimer = new System.Threading.Timer(PollTick, null, 250, 250);

            // ALWAYS show mic overlay on startup — BEFORE welcome dialog
            // so users see mic status from the very first moment
            _settings.MicOverlayEnabled = true;
            _settings.Save();
            _micStatus.OverlayEnabled = true;
            try
            {
                bool pttActive = _pushToTalk != null && _pushToTalk.Enabled;
                if (pttActive)
                    _micStatus.ShowMicClosed();
                else
                    _micStatus.ShowMicOpenIdle();
                Logger.Info("Startup overlay shown immediately (ptt=" + pttActive + ").");
            }
            catch (Exception ex) { Logger.Error("Startup overlay failed.", ex); }

            // Check for first run
            if (!_settings.FirstRunComplete)
            {
                _settings.MicEnforceEnabled = true;
                _settings.FirstRunComplete = true; // Starter Wizard removed, silently complete
                _settings.Save();
            }

            // Show privacy splash on every startup
            ShowSplash(() => { });

            // Pre-create Options form behind splash so first open is instant
            var preCreateTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            preCreateTimer.Tick += (s2, e2) => { preCreateTimer.Stop(); preCreateTimer.Dispose(); PreCreateOptionsForm(); };
            preCreateTimer.Start();

            // Auto-check for updates 5 seconds after startup (seamless — auto-downloads if update found)
            var autoUpdateTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            autoUpdateTimer.Tick += (s2, e2) => {
                autoUpdateTimer.Stop(); autoUpdateTimer.Dispose();
                UpdateDialog.CheckAsync(false, (hasUpdate, latestVer) => {
                    if (hasUpdate && latestVer != null)
                        UpdateDialog.ShowUpdate(latestVer); // auto-download, no button needed
                });
            };
            autoUpdateTimer.Start();

            // Enable PTT/Toggle if configured (either from saved settings or just-completed wizard)
            if (_settings.PushToTalkEnabled || _settings.PushToToggleEnabled || _settings.PushToMuteEnabled)
            {
                EnablePtt();
            }
            // Enable Voice Activity if configured
            else if (_settings.VoiceActivityEnabled)
            {
                if (_pushToTalk != null)
                {
                    _pushToTalk.OnTalkStart -= OnPttTalkStart;
                    _pushToTalk.OnTalkStop -= OnPttTalkStop;
                    _pushToTalk.OnTalkStart += OnPttTalkStart;
                    _pushToTalk.OnTalkStop += OnPttTalkStop;
                    float threshold = _settings.VoiceActivityThreshold / 100f;
                    _pushToTalk.EnableVoiceActivity(threshold, _settings.VoiceActivityHoldoverMs);
                }
                if (_settings.VoiceActivityShowOverlay && _micStatus != null && !_micStatus.IsDisposed)
                {
                    _micStatus.OverlayEnabled = true;
                    _micStatus.PushToMuteMode = false;
                    _micStatus.ToggleMode = false;
                    _micStatus.ShowMicClosed();
                }
            }

            // Mic warning poller — checks every 500ms if mic is unprotected, shows/hides warning
            StartMicWarningPoller();

            // Set up the kill event for --kill support
            _killEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\Angry_Audio_Kill_Event");
            var killThread = new System.Threading.Thread(WaitForKillSignal)
            {
                IsBackground = true,
                Name = "Angry_Audio_Kill_Watcher"
            };
            killThread.Start();

            // Enforce immediately on startup (Rule #3)
            if (!_isPaused)
            {
                // Snapshot current volumes before enforcement locks them
                if (_settings.MicEnforceEnabled) { try { _micPreLockVol = (int)Audio.GetMicVolume(); } catch { _micPreLockVol = -1; } }
                if (_settings.SpeakerEnforceEnabled) { try { _spkPreLockVol = (int)Audio.GetSpeakerVolume(); } catch { _spkPreLockVol = -1; } }
                EnforceMic(true);
                EnforceSpeaker(true);
            }

            Logger.Info("TrayApp initialized." + (_isPaused ? " (paused)" : ""));

            // Open settings if requested via --settings
            if (_openSettingsOnStart)
            {
                ShowOptions();
            }
        }

        // --- Polling Loop ---

        private void SetPollSpeed(bool fast)
        {
            if (fast == _pollFast) return;
            _pollFast = fast;
            int ms = fast ? 100 : 250;
            try { _pollTimer.Change(ms, ms); } catch { }
        }

        private void PollTick(object state)
        {
            if (_isPaused || _disposed) return;

            // Force CapsLock off if it's a PTT hotkey
            try { if (_pushToTalk != null && _pushToTalk.Enabled && !IsCapturingKey()) _pushToTalk.ForceCapsLockOff(); } catch { }

            // Watchdog: if tray icon is gone, we're a zombie — exit immediately
            try
            {
                if (_trayIcon == null || !_trayIcon.Visible)
                {
                    Logger.Error("Tray icon lost — exiting to prevent zombie process.");
                    try { Dispose(); } catch { }
                    Environment.Exit(1);
                    return;
                }
            }
            catch { }

            try
            {
                // Track device changes (throttled — every 3s, not every tick)
                if ((DateTime.UtcNow - _lastDeviceChangeCheck).TotalSeconds >= 3)
                {
                    _lastDeviceChangeCheck = DateTime.UtcNow;
                    CheckDeviceChanges();
                }

                // Get idle time
                long idleMs = GetIdleTimeMs();

                // Process mic (skip AFK state machine if PTT is enabled — PTT owns mute state)
                bool pttOwns = _pushToTalk != null && _pushToTalk.Enabled;
                if (!pttOwns)
                {
                    ProcessDevice(
                        isMic: true,
                        idleMs: idleMs,
                        enforceEnabled: _settings.MicEnforceEnabled,
                        enforcePercent: _settings.MicVolumePercent,
                        afkEnabled: _settings.AfkMicMuteEnabled,
                        afkSec: _settings.AfkMicMuteSec);
                }
                else if (_settings.MicEnforceEnabled)
                {
                    // PTT owns mute/unmute, but volume enforcement must still run.
                    // Apps like LilySpeech change mic VOLUME while PTT controls MUTE.
                    EnforceMic(false);
                }

                // Process speaker
                ProcessDevice(
                    isMic: false,
                    idleMs: idleMs,
                    enforceEnabled: _settings.SpeakerEnforceEnabled,
                    enforcePercent: _settings.SpeakerVolumePercent,
                    afkEnabled: _settings.AfkSpeakerMuteEnabled,
                    afkSec: _settings.AfkSpeakerMuteSec);

                // Update tray tooltip and icon (throttled — every 2s in slow mode)
                if (_pollFast || (DateTime.UtcNow - _lastTrayUpdate).TotalSeconds >= 2)
                {
                    _lastTrayUpdate = DateTime.UtcNow;
                    UpdateTrayState();
                }

                // Per-app volume enforcement (throttled — every 2s to avoid excessive COM calls)
                if (_settings.AppVolumeEnforceEnabled && _settings.AppVolumeRules.Count > 0)
                {
                    if ((DateTime.UtcNow - _lastAppEnforceCheck).TotalSeconds >= 2)
                    {
                        _lastAppEnforceCheck = DateTime.UtcNow;
                        EnforceAppVolumes();
                    }
                }

                // Mic listener monitoring — who's using the mic? (throttled — every 10s)
                if ((DateTime.UtcNow - _lastMicListenerCheck).TotalSeconds >= 10)
                {
                    _lastMicListenerCheck = DateTime.UtcNow;
                    try
                    {
                        var listeners = Audio.GetMicCaptureSessions();
                        var names = new System.Collections.Generic.List<string>();
                        string self = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                        foreach (var s in listeners) {
                            string pn = s.ProcessName;
                            // Skip our own process and common system audio services
                            if (string.Equals(pn, self, StringComparison.OrdinalIgnoreCase)) continue;
                            if (string.Equals(pn, "Angry Audio", StringComparison.OrdinalIgnoreCase)) continue;
                            if (string.Equals(pn, "AngryAudio", StringComparison.OrdinalIgnoreCase)) continue;
                            if (string.Equals(pn, "audiodg", StringComparison.OrdinalIgnoreCase)) continue;
                            names.Add(pn);
                        }
                        names.Sort();
                        string key = string.Join(",", names.ToArray());
                        if (key != _lastMicListenerKey)
                        {
                            _lastMicListenerKey = key;
                            _micListenerNames = names;
                            if (names.Count > 0)
                                Logger.Info("Mic listeners: " + key);
                            else
                                Logger.Info("Mic listeners: none");
                        }
                    }
                    catch (Exception ex) { Logger.Error("Mic listener check failed", ex); }
                }

                // Push-to-talk auto-sync removed — OnAudioSettingsChanged handles all mode changes now.
                // Only keep the safety check for EnforceMute.

                // Push-to-talk enforcement — keeps mic muted when key not held
                if (_pushToTalk != null && _pushToTalk.Enabled)
                {
                    _pushToTalk.EnforceMute();
                }
                else
                {
                    // ── Mic-mute failsafe ──────────────────────────────────────
                    // If no PTT/PTM/Toggle mode is active and dictation isn't
                    // recording, the mic should NEVER be muted. Force unmute to
                    // recover from any bug that left it stuck.
                    bool dictActive = DictationManager.Current != null && DictationManager.Current.IsActive;
                    if (!dictActive)
                    {
                        try {
                            if (Audio.GetMicMute())
                            {
                                Audio.SetMicMute(false);
                                Logger.Warn("Mic-mute failsafe triggered: mic was stuck muted with no active mode.");
                            }
                        } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error in poll tick.", ex);
            }
        }

        private void ProcessDevice(bool isMic, long idleMs,
            bool enforceEnabled, int enforcePercent,
            bool afkEnabled, int afkSec)
        {
            AfkState afkState = isMic ? _micAfkState : _speakerAfkState;
            DateTime lastEnforce = isMic ? _lastMicEnforce : _lastSpeakerEnforce;
            float fadeCurrentPercent = isMic ? _micFadeCurrentPercent : _speakerFadeCurrentPercent;
            float fadeTargetPercent = isMic ? _micFadeTargetPercent : _speakerFadeTargetPercent;
            DateTime lastFadeStep = isMic ? _lastMicFadeStep : _lastSpeakerFadeStep;

            long afkThresholdMs = afkSec * 1000L;
            bool userIsIdle = afkEnabled && afkSec > 0 && idleMs >= afkThresholdMs;

            try
            {
            // State machine
            switch (afkState)
            {
                case AfkState.Active:
                    if (userIsIdle)
                    {
                        if (isMic)
                        {
                            // Check if any app is actively using the mic before muting.
                            // If so, pause AFK mute to avoid cutting off calls/recordings.
                            if ((DateTime.UtcNow - _lastOpenMicCheck).TotalSeconds >= 5)
                            {
                                _lastOpenMicCheck = DateTime.UtcNow;
                                var micApps = Audio.GetActiveMicCaptureSessions();

                                if (micApps.Count > 0)
                                {
                                    if (!_micMutePausedForOpenMic)
                                    {
                                        _micMutePausedForOpenMic = true;
                                        _lastOpenMicApp = micApps[0];
                                        Logger.Info("Open mic detected: " + string.Join(", ", micApps.ToArray()) + " — AFK mic mute paused.");
                                        ShowBalloon("Mic In Use", _lastOpenMicApp + " is using your mic — AFK mute paused.");
                                    }
                                    break; // Stay in Active state
                                }
                                else if (_micMutePausedForOpenMic)
                                {
                                    // App released the mic — re-enable muting
                                    _micMutePausedForOpenMic = false;
                                    Logger.Info("Open mic released by " + (_lastOpenMicApp ?? "app") + " — AFK mic mute re-enabled.");
                                    ShowCorrectionToast("(" + (_lastOpenMicApp ?? "App") + ") Released Your Mic \u2014 AFK Protection Re-Enabled");
                                }
                            }
                            else if (_micMutePausedForOpenMic)
                            {
                                break; // Still paused, skip muting
                            }

                            // No open mic — safe to mute
                            _micMutePausedForOpenMic = false;
                            // Mic mutes instantly — snapshot current volume first
                            _preFadeMicVolume = Audio.GetMicVolume();
                            if (_preFadeMicVolume < 10f) _preFadeMicVolume = 100f;
                            // Dictation God Mode: never AFK-mute during active dictation
                            if (DictationManager.Current != null && DictationManager.Current.IsActive)
                            {
                                Logger.Info("AFK mute skipped — dictation is active (God Mode).");
                                break;
                            }
                            Audio.SetMicMute(true);
                            afkState = AfkState.AfkMuted;
                            SetPollSpeed(false);
                            Logger.Info("Mic AFK muted instantly after " + afkSec + "s idle. Pre-fade: " + _preFadeMicVolume + "%.");
                            if (_micStatus != null && !_micStatus.IsDisposed)
                                _micStatus.ShowMicClosed();
                        }
                        else
                        {
                            // Speaker fades out gradually — snapshot current volume
                            float currentVol = Audio.GetSpeakerVolume();
                            _preFadeSpeakerVolume = currentVol;
                            if (_preFadeSpeakerVolume < 10f) _preFadeSpeakerVolume = 50f;
                            fadeCurrentPercent = currentVol;
                            fadeTargetPercent = 0f;
                            _fadeOutStepSize = _preFadeSpeakerVolume / FadeSteps;
                            lastFadeStep = DateTime.UtcNow;
                            afkState = AfkState.FadingOut;
                            SetPollSpeed(true);
                            Logger.Info("Speaker fade-out started from " + currentVol + "% (step: " + _fadeOutStepSize + "%) after " + afkSec + "s idle.");
                            ShowFadeOverlay(_preFadeSpeakerVolume, true, currentVol);
                            UpdateFadeOverlay(currentVol, _preFadeSpeakerVolume);
                        }
                    }
                    else
                    {
                        // Enforce volume lock every tick — fast with cached COM endpoints
                        if (enforceEnabled)
                        {
                            if (isMic) EnforceMic(false);
                            else EnforceSpeaker(false);
                        }
                    }
                    break;

                case AfkState.FadingOut:
                    if (!userIsIdle)
                    {
                        // User came back during fade-out — fade in from current level
                        HideFadeOverlay();
                        fadeTargetPercent = enforceEnabled ? enforcePercent : _preFadeSpeakerVolume;
                        // fadeCurrentPercent is already at whatever level the fade-out reached
                        float remaining = fadeTargetPercent - fadeCurrentPercent;
                        _fadeInStepSize = remaining > 0 ? remaining / FadeSteps : 1f;
                        Audio.SetSpeakerMute(false);
                        afkState = AfkState.FadingIn;
                        SetPollSpeed(true);
                        lastFadeStep = DateTime.UtcNow;
                        Logger.Info("Speaker fade-out cancelled at " + (int)fadeCurrentPercent + "% — fading in to " + fadeTargetPercent + "%.");
                        ShowFadeOverlay(fadeTargetPercent);
                    }
                    else if ((DateTime.UtcNow - lastFadeStep).TotalMilliseconds >= FadeOutStepMs)
                    {
                        fadeCurrentPercent = Math.Max(fadeCurrentPercent - _fadeOutStepSize, 0f);
                        Audio.SetSpeakerVolume(fadeCurrentPercent);
                        lastFadeStep = DateTime.UtcNow;

                        // Update overlay with remaining volume
                        UpdateFadeOverlay(fadeCurrentPercent, _preFadeSpeakerVolume);

                        if (fadeCurrentPercent <= 0f)
                        {
                            Audio.SetSpeakerMute(true);
                            afkState = AfkState.AfkMuted;
                            SetPollSpeed(false);
                            Logger.Info("Speaker fade-out complete — AFK muted.");
                            HideFadeOverlay();
                        }
                    }
                    break;

                case AfkState.AfkMuted:
                    if (!userIsIdle)
                    {
                        if (isMic)
                        {
                            // Mic restores INSTANTLY to pre-fade volume
                            float restoreMic = enforceEnabled ? enforcePercent : _preFadeMicVolume;
                            Audio.SetMicVolume(restoreMic);
                            Audio.SetMicMute(false);
                            afkState = AfkState.Active;
                            SetPollSpeed(false);
                            lastEnforce = DateTime.UtcNow;
                            Logger.Info("Mic instantly restored to " + restoreMic + "%.");
                            if (_micStatus != null && !_micStatus.IsDisposed)
                                _micStatus.ShowMicOpenIdle(); // AFK return = idle state, not PTT hold
                        }
                        else
                        {
                            // Speaker fades in gradually to pre-fade volume
                            fadeTargetPercent = enforceEnabled ? enforcePercent : _preFadeSpeakerVolume;
                            fadeCurrentPercent = 0f;
                            _fadeInStepSize = fadeTargetPercent / FadeSteps;
                            Audio.SetSpeakerVolume(0f);
                            Audio.SetSpeakerMute(false);

                            afkState = AfkState.FadingIn;
                            SetPollSpeed(true);
                            lastFadeStep = DateTime.UtcNow;
                            Logger.Info("Speaker fade-in started. Target: " + fadeTargetPercent + "%.");
                            ShowFadeOverlay(fadeTargetPercent);
                        }
                    }
                    // While AFK, do NOT enforce (Rule #1: AFK mute always wins)
                    break;

                case AfkState.FadingIn:
                    if ((DateTime.UtcNow - lastFadeStep).TotalMilliseconds >= FadeInStepMs)
                    {
                        fadeCurrentPercent = Math.Min(fadeCurrentPercent + _fadeInStepSize, fadeTargetPercent);

                        if (isMic) Audio.SetMicVolume(fadeCurrentPercent);
                        else Audio.SetSpeakerVolume(fadeCurrentPercent);

                        lastFadeStep = DateTime.UtcNow;

                        // Update overlay
                        UpdateFadeOverlay(fadeCurrentPercent, fadeTargetPercent);

                        if (fadeCurrentPercent >= fadeTargetPercent)
                        {
                            afkState = AfkState.Active;
                            SetPollSpeed(false);
                            lastEnforce = DateTime.UtcNow;
                            Logger.Info((isMic ? "Mic" : "Speaker") + " fade-in complete at " + fadeTargetPercent + "%.");
                            HideFadeOverlay();
                        }
                    }
                    // During fade, do NOT enforce (let the fade complete)
                    break;
            }
            }
            catch (Exception ex) { Logger.Error("ProcessDevice(" + (isMic ? "mic" : "spk") + ") failed.", ex); }

            // Write back modified state to fields
            if (isMic) { _micAfkState = afkState; _lastMicEnforce = lastEnforce; _micFadeCurrentPercent = fadeCurrentPercent; _micFadeTargetPercent = fadeTargetPercent; _lastMicFadeStep = lastFadeStep; }
            else { _speakerAfkState = afkState; _lastSpeakerEnforce = lastEnforce; _speakerFadeCurrentPercent = fadeCurrentPercent; _speakerFadeTargetPercent = fadeTargetPercent; _lastSpeakerFadeStep = lastFadeStep; }
        }

        // --- Enforcement ---

        private void EnforceMic(bool isStartup)
        {
            if (!_settings.MicEnforceEnabled) return;

            try
            {
                // Don't fight PTT/PTM/Toggle zero-volume protection
                bool pttMuting = _pushToTalk != null && _pushToTalk.Enabled && !_pushToTalk.IsTalking;

                float currentVol = Audio.GetMicVolume();
                if (currentVol < 0) return; // Device not available

                float targetVol = _settings.MicVolumePercent;

                // Always enforce volume level (even during PTT mute — so volume is correct when mic opens)
                if (Math.Abs(currentVol - targetVol) > 0.5f)
                {
                    Audio.SetMicVolume(targetVol);
                    Logger.Info("Mic enforced: " + (int)currentVol + "% → " + (int)targetVol + "%");
                }

                // Clear mute flag — prevents external apps from keeping mic muted
                // Only skip when PTT or AFK are intentionally muting
                if (!pttMuting && Audio.IsMicMuteFlagSet())
                {
                    if (_micAfkState != AfkState.AfkMuted && _micAfkState != AfkState.FadingOut)
                    {
                        Audio.SetMicMute(false);
                    }
                }
            }
            catch (Exception ex) { Logger.Error("EnforceMic failed.", ex); }
        }

        private void EnforceSpeaker(bool isStartup)
        {
            if (!_settings.SpeakerEnforceEnabled) return;

            try
            {
                float currentVol = Audio.GetSpeakerVolume();
                if (currentVol < 0) return;

                float targetVol = _settings.SpeakerVolumePercent;
                if (Math.Abs(currentVol - targetVol) > 0.5f)
                {
                    Audio.SetSpeakerVolume(targetVol);
                    Logger.Info("Speaker enforced: " + (int)currentVol + "% → " + (int)targetVol + "%");
                }

                // Clear mute flag when enforcing (unless AFK is intentionally muting)
                if (Audio.GetSpeakerMute())
                {
                    if (_speakerAfkState != AfkState.AfkMuted && _speakerAfkState != AfkState.FadingOut)
                    {
                        Audio.SetSpeakerMute(false);
                        Logger.Info("Speaker mute flag was set by another app. Cleared.");
                    }
                }
            }
            catch (Exception ex) { Logger.Error("EnforceSpeaker failed.", ex); }
        }

        // --- Volume Lock Snapshot Restore ---

        /// <summary>
        /// Smoothly restores volume to the pre-lock snapshot value over ~400ms.
        /// Called when a volume lock toggle is turned OFF.
        /// </summary>
        private void SmoothRestore(bool isMic)
        {
            int target = isMic ? _micPreLockVol : _spkPreLockVol;
            if (target < 0) return; // No snapshot — nothing to restore

            // Kill any existing restore timer for this channel
            if (isMic) { if (_restoreMicTimer != null) { _restoreMicTimer.Dispose(); _restoreMicTimer = null; } }
            else { if (_restoreSpkTimer != null) { _restoreSpkTimer.Dispose(); _restoreSpkTimer = null; } }

            float current;
            try { current = isMic ? Audio.GetMicVolume() : Audio.GetSpeakerVolume(); }
            catch { return; }

            if (Math.Abs(current - target) < 1f)
            {
                // Already there
                if (isMic) _micPreLockVol = -1; else _spkPreLockVol = -1;
                return;
            }

            const int steps = 16;
            const int intervalMs = 25; // 16 steps × 25ms = 400ms total
            float stepSize = (target - current) / steps;
            int step = 0;
            float vol = current;

            var timer = new System.Threading.Timer(_ =>
            {
                step++;
                if (step >= steps)
                {
                    // Final step — set exact target
                    try
                    {
                        if (isMic) Audio.SetMicVolume(target);
                        else Audio.SetSpeakerVolume(target);
                    }
                    catch { }

                    if (isMic) { _micPreLockVol = -1; if (_restoreMicTimer != null) { _restoreMicTimer.Dispose(); _restoreMicTimer = null; } }
                    else { _spkPreLockVol = -1; if (_restoreSpkTimer != null) { _restoreSpkTimer.Dispose(); _restoreSpkTimer = null; } }
                    return;
                }

                vol += stepSize;
                try
                {
                    if (isMic) Audio.SetMicVolume(vol);
                    else Audio.SetSpeakerVolume(vol);
                }
                catch { }
            }, null, intervalMs, intervalMs);

            if (isMic) _restoreMicTimer = timer;
            else _restoreSpkTimer = timer;
        }

        // --- Per-App Volume Enforcement ---

        private DateTime _lastAppCorrectionNotify = DateTime.MinValue;

        private void EnforceAppVolumes()
        {
            if (!_settings.AppVolumeEnforceEnabled || _settings.AppVolumeRules.Count == 0) return;

            try
            {
                var sessions = Audio.GetAudioSessions();
                foreach (var session in sessions)
                {
                    int targetVol;
                    if (_settings.AppVolumeRules.TryGetValue(session.ProcessName, out targetVol))
                    {
                        // Negative values = unlocked (not enforced), skip
                        if (targetVol < 0) continue;
                        if (Math.Abs(session.Volume - targetVol) > 0.5f)
                        {
                            Audio.SetAppVolume(session.ProcessName, targetVol);

                            if (_settings.NotifyOnCorrection && CanNotify(ref _lastAppCorrectionNotify))
                            {
                                ShowBalloon("App Volume Corrected", session.ProcessName + " reset to " + targetVol + "%.");
                            }

                            Logger.Info("App enforced: " + session.ProcessName + " " + (int)session.Volume + "% → " + targetVol + "%");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to enforce app volumes.", ex);
            }
        }

        // --- Device Change Detection ---

        private void CheckDeviceChanges()
        {
            if (!_settings.NotifyOnDeviceChange) return;

            try
            {
                string micId = Audio.GetMicDeviceId();
                string speakerId = Audio.GetSpeakerDeviceId();

            if (_lastMicDeviceId != null && micId != null && micId != _lastMicDeviceId)
            {
                Logger.Info("Default mic device changed: " + micId);
                ShowBalloon("New Default Microphone Detected", "Audio routing updated automatically.");
            }

            if (_lastSpeakerDeviceId != null && speakerId != null && speakerId != _lastSpeakerDeviceId)
            {
                Logger.Info("Default speaker device changed: " + speakerId);
                ShowBalloon("New Default Speaker Detected", "Audio routing updated automatically.");
            }

            _lastMicDeviceId = micId;
            _lastSpeakerDeviceId = speakerId;
            }
            catch (Exception ex) { Logger.Error("CheckDeviceChanges failed.", ex); }
        }

        // --- AFK Detection ---

        private long GetIdleTimeMs()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref info))
                return 0;

            return (long)((uint)Environment.TickCount - info.dwTime);
        }

        // --- Fade Overlay ---

        private void ShowInfoToast(string title, string body)
        {
            try
            {
                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        var toast = new InfoToast(title, body);
                        toast.ShowNoFocus();
                        Application.Run(toast);
                    }
                    catch { }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = "Angry_Audio_Info_Toast";
                thread.Start();
            }
            catch { }
        }

        /// <summary>
        /// Show a fade overlay indicating volume correction in progress.
        /// </summary>
        private void ShowFadeOverlay(float targetPercent, bool fadeOut = false, float initialCurrent = 0f)
        {
            try
            {
                // If overlay already exists or is being created, skip
                if ((_fadeOverlay != null && !_fadeOverlay.IsDisposed) || _fadeOverlayCreating)
                    return;

                _fadeOverlayCreating = true;

                // Dispose any dead overlay
                if (_fadeOverlay != null)
                {
                    try { _fadeOverlay.Dispose(); } catch { }
                    _fadeOverlay = null;
                }

                float startCurrent = fadeOut ? initialCurrent : 0f;

                // Create overlay on a dedicated STA thread with its own message pump
                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        _fadeOverlay = new FadeOverlay();
                        _fadeOverlay.SetFadeOut(fadeOut);
                        _fadeOverlayCreating = false;
                        _fadeOverlay.UpdateProgress(startCurrent, targetPercent);
                        _fadeOverlay.ShowOverlay();
                        Logger.Info("FadeOverlay created on dedicated thread.");
                        Application.Run();
                    }
                    catch (Exception ex)
                    {
                        _fadeOverlayCreating = false;
                        Logger.Error("FadeOverlay thread failed.", ex);
                    }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = "FadeOverlayThread";
                thread.Start();
            }
            catch (Exception ex)
            {
                _fadeOverlayCreating = false;
                Logger.Error("Failed to show fade overlay.", ex);
            }
        }

        private void UpdateFadeOverlay(float current, float target)
        {
            try
            {
                if (_fadeOverlay != null && !_fadeOverlay.IsDisposed)
                {
                    Action a = () =>
                    {
                        try
                        {
                            if (_fadeOverlay != null && !_fadeOverlay.IsDisposed)
                                _fadeOverlay.UpdateProgress(current, target);
                        }
                        catch { }
                    };
                    if (_fadeOverlay.InvokeRequired)
                        try { _fadeOverlay.BeginInvoke(a); } catch { }
                    else a();
                }
            }
            catch { }
        }

        private void HideFadeOverlay()
        {
            try
            {
                var overlay = _fadeOverlay;
                if (overlay != null && !overlay.IsDisposed)
                {
                    Action a = () =>
                    {
                        try
                        {
                            overlay.FadeOutAndClose();
                            // ExitThread after a delay to let fade complete
                            // Timer must stay rooted to fire — assigning prevents GC before 500ms elapses
                            #pragma warning disable 0219
                            var dummy = new System.Threading.Timer((state) => { try { Application.ExitThread(); } catch { } }, null, 500, System.Threading.Timeout.Infinite);
                            #pragma warning restore 0219
                        }
                        catch { }
                        Logger.Info("FadeOverlay disposed.");
                    };
                    if (overlay.InvokeRequired)
                        try { overlay.BeginInvoke(a); } catch { }
                    else a();
                }
                _fadeOverlay = null;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to hide fade overlay.", ex);
            }
        }

        // --- Tray Icon & Menu ---

        private void BuildTrayIcon()
        {
            _contextMenu = new ContextMenuStrip();
            _contextMenu.BackColor = DarkTheme.BgDark;
            _contextMenu.ForeColor = DarkTheme.TextLight;
            _contextMenu.ShowImageMargin = false;
            _contextMenu.ShowCheckMargin = false;
            _contextMenu.Renderer = new DarkMenuRenderer();

            // Hide tooltip when context menu is open so they don't overlap
            _contextMenu.Opening += (s, e) => {
                if (_trayIcon != null) _trayIcon.Text = "";
                // Refresh mic listener display
                if (_micListenerMenuItem != null) {
                    if (_micListenerNames.Count > 0) {
                        // Truncate individual app names to 12 chars for compact menu
                        var shortNames = new System.Collections.Generic.List<string>();
                        foreach (string n in _micListenerNames) {
                            shortNames.Add(n.Length > 12 ? n.Substring(0, 11) + "\u2026" : n);
                        }
                        string names = string.Join(", ", shortNames.ToArray());
                        if (names.Length > 28) names = names.Substring(0, 25) + "\u2026";
                        _micListenerMenuItem.Text = "\ud83c\udfa4 Mic: " + names;
                        _micListenerMenuItem.ForeColor = Color.FromArgb(255, 180, 100);
                    } else {
                        _micListenerMenuItem.Text = "\ud83c\udfa4 Mic: No apps";
                        _micListenerMenuItem.ForeColor = DarkTheme.Txt4;
                    }
                }
            };
            _contextMenu.Closed += (s, e) => UpdateTrayState();

            // Mic listener status — shows who's using your mic
            _micListenerMenuItem = new ToolStripMenuItem("") { Enabled = false };
            _micListenerMenuItem.ForeColor = DarkTheme.Txt3;
            _contextMenu.Items.Add(_micListenerMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());

            var optionsItem = new ToolStripMenuItem("\u2699  Options...");
            optionsItem.Click += (s, e) => ShowOptions();
            _contextMenu.Items.Add(optionsItem);

        // Display submenu has been removed as per user feedback
            _pauseMenuItem = new ToolStripMenuItem("\u23F8  Pause Angry Audio");
            _pauseMenuItem.Click += (s, e) => TogglePause();
            UpdatePauseMenuItem();
            _contextMenu.Items.Add(_pauseMenuItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var viewLogItem = new ToolStripMenuItem("\ud83d\udcdd  View Log");
            viewLogItem.Click += (s, e) => ViewLog();
            _contextMenu.Items.Add(viewLogItem);

            // Check for Updates removed from tray menu — accessible through Options instead



            _contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("\u274C  Exit");
            exitItem.Click += (s, e) => ExitApplication();
            _contextMenu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                Icon = _isPaused ? _pausedIcon : _baseIcon,
                Text = "Angry Audio",
                Visible = true,
                ContextMenuStrip = _contextMenu
            };

            _trayIcon.DoubleClick += (s, e) => ShowOptions();

            UpdateTrayState();

            // Register global hotkeys — log failures so we know when another app holds the key
            _hotkeyWnd = new HotkeyWindow(this);
            RegisterAndLog(HOTKEY_DISPLAY,    MOD_ALT | MOD_NOREPEAT, 0x50, "Alt+P (Display)");
            RegisterAndLog(HOTKEY_CYCLE_NEXT, MOD_ALT | MOD_NOREPEAT, 0x45, "Alt+E (Cycle Next)");
            RegisterAndLog(HOTKEY_CYCLE_PREV, MOD_ALT | MOD_NOREPEAT, 0x51, "Alt+Q (Cycle Prev)");
            RegisterAndLog(HOTKEY_INPUT,      MOD_ALT | MOD_NOREPEAT, 0x49, "Alt+I (Input)");
            RegisterAndLog(HOTKEY_OUTPUT,     MOD_ALT | MOD_NOREPEAT, 0x4F, "Alt+O (Output)");
            RegisterAndLog(HOTKEY_DICT,       MOD_ALT | MOD_NOREPEAT, 0xDB, "Alt+[ (Dictation)");
            RegisterAndLog(HOTKEY_AFK,        MOD_ALT | MOD_NOREPEAT, 0xDD, "Alt+] (AFK)");
        }

        private void RegisterAndLog(int id, uint mods, uint vk, string name)
        {
            if (!RegisterHotKey(_hotkeyWnd.Handle, id, mods, vk))
            {
                int err = Marshal.GetLastWin32Error();
                Logger.Warn("Failed to register hotkey " + name + " (error " + err + "). Another app may hold this key.");
            }
            else
            {
                Logger.Info("Registered hotkey " + name);
            }
        }

        private void UpdateTrayState()
        {
            if (_trayIcon == null || _disposed) return;

            try
            {
                // Determine icon
                Icon targetIcon;
                if (_isPaused)
                    targetIcon = _pausedIcon ?? _baseIcon;
                else if (_pushToTalk != null && _pushToTalk.Enabled && _pushToTalk.IsTalking)
                    targetIcon = _micHotIcon ?? _baseIcon; // green dot = mic is hot
                else if (_pushToTalk != null && _pushToTalk.Enabled && !_pushToTalk.IsTalking)
                    targetIcon = _pttIcon ?? _baseIcon; // red dot = mic is muted
                else if (_micAfkState == AfkState.AfkMuted || _speakerAfkState == AfkState.AfkMuted)
                    targetIcon = _afkIcon ?? _baseIcon;
                else
                    targetIcon = _baseIcon;

                if (_trayIcon.Icon != targetIcon)
                    _trayIcon.Icon = targetIcon;

                // Build tooltip (max 63 chars for NotifyIcon.Text)
                string tooltip = BuildTooltip();
                if (tooltip.Length > 63)
                    tooltip = tooltip.Substring(0, 63);

                _trayIcon.Text = tooltip;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to update tray state.", ex);
            }
        }

        private string BuildTooltip()
        {
            if (_isPaused) return "Angry Audio — Paused";

            if (_pushToTalk != null && _pushToTalk.Enabled)
            {
                string keyName = PushToTalk.GetKeyName(_settings.PushToTalkKey);
                if (_pushToTalk.IsToggleMode)
                {
                    if (_pushToTalk.IsTalking)
                        return "Angry Audio — Toggle: Mic Open (press " + keyName + " to mute)";
                    else
                        return "Angry Audio — Toggle: Mic Muted (press " + keyName + " to unmute)";
                }
                else
                {
                    if (_pushToTalk.IsTalking)
                        return "Angry Audio — PTT: Mic Open";
                    else
                        return "Angry Audio — PTT: Mic locked (hold " + keyName + " to talk)";
                }
            }

            bool micFading = _micAfkState == AfkState.FadingIn;
            bool speakerFading = _speakerAfkState == AfkState.FadingIn;
            if (micFading || speakerFading) return "Angry Audio — Restoring Audio\u2026";

            bool speakerFadingOut = _speakerAfkState == AfkState.FadingOut;
            if (speakerFadingOut) return "Angry Audio — Going AFK\u2026";

            bool micAfk = _micAfkState == AfkState.AfkMuted;
            bool speakerAfk = _speakerAfkState == AfkState.AfkMuted;

            string micPart = "";
            if (micAfk)
                micPart = "Mic: Muted (AFK)";
            else if (_settings.MicEnforceEnabled)
                micPart = "Mic: Locked at " + _settings.MicVolumePercent + "%";
            else if (_settings.AfkMicMuteEnabled)
                micPart = "Mic: AFK Guard Active";

            string speakerPart = "";
            if (speakerAfk)
                speakerPart = "Spk: Muted (AFK)";
            else if (_settings.SpeakerEnforceEnabled)
                speakerPart = "Spk: Locked at " + _settings.SpeakerVolumePercent + "%";
            else if (_settings.AfkSpeakerMuteEnabled)
                speakerPart = "Spk: AFK Guard Active";

            if (micPart == "" && speakerPart == "")
                return "Angry Audio — Idle";

            if (micPart != "" && speakerPart != "")
                return "Angry Audio — " + micPart + " | " + speakerPart;

            return "Angry Audio — " + (micPart != "" ? micPart : speakerPart);
        }

        // --- Icon Generation ---

        private void BuildIcons()
        {
            _baseIcon = GenerateTrayIcon(null);
            _pausedIcon = GenerateTrayIcon("‖");
            _afkIcon = GenerateTrayIcon("Z");
            _errorIcon = GenerateTrayIcon("!");
            _pttIcon = GenerateTrayIcon(null, Color.FromArgb(220, 60, 60)); // red dot = mic muted
            _micHotIcon = GenerateTrayIcon(null, Color.FromArgb(60, 200, 90)); // green dot = mic hot
        }

        private Icon GenerateTrayIcon(string badge, Color? dotColor = null)
        {
            using (var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;

                // Draw mascot head on fully transparent canvas
                Mascot.DrawMascotHead(g, 0, 0, 32);

                // Badge overlay (text in circle)
                if (badge != null)
                {
                    using (var badgeBg = new SolidBrush(badge == "!" ? DarkTheme.ErrorRed : DarkTheme.Accent))
                    using (var badgeFont = new Font("Segoe UI", 8, FontStyle.Bold))
                    using (var badgeTextBrush = new SolidBrush(Color.White))
                    {
                        g.FillEllipse(badgeBg, 19, 19, 13, 13);
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString(badge, badgeFont, badgeTextBrush, new RectangleF(19, 19, 13, 13), sf);
                    }
                }
                // Dot indicator (small colored circle, no text)
                else if (dotColor.HasValue)
                {
                    using (var bg = new SolidBrush(Color.FromArgb(40, 40, 40)))
                        g.FillEllipse(bg, 21, 21, 11, 11);
                    using (var fill = new SolidBrush(dotColor.Value))
                        g.FillEllipse(fill, 22, 22, 9, 9);
                }

                // Build proper ICO with embedded PNG for true alpha transparency
                // (GetHicon() loses alpha on many Windows versions)
                using (var pngStream = new System.IO.MemoryStream())
                {
                    bmp.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
                    byte[] pngBytes = pngStream.ToArray();

                    using (var icoStream = new System.IO.MemoryStream())
                    {
                        // ICONDIR header: reserved(2) + type=1(2) + count=1(2)
                        icoStream.Write(new byte[] { 0, 0, 1, 0, 1, 0 }, 0, 6);
                        // ICONDIRENTRY: width, height, colors, reserved
                        icoStream.WriteByte(32); // width
                        icoStream.WriteByte(32); // height
                        icoStream.WriteByte(0);  // color palette
                        icoStream.WriteByte(0);  // reserved
                        // planes (2 bytes) + bpp (2 bytes)
                        icoStream.Write(new byte[] { 1, 0, 32, 0 }, 0, 4);
                        // image data size (4 bytes)
                        icoStream.Write(BitConverter.GetBytes(pngBytes.Length), 0, 4);
                        // offset to image data = 22 (6 header + 16 entry)
                        icoStream.Write(BitConverter.GetBytes(22), 0, 4);
                        // PNG image data
                        icoStream.Write(pngBytes, 0, pngBytes.Length);

                        icoStream.Seek(0, System.IO.SeekOrigin.Begin);
                        return new Icon(icoStream);
                    }
                }
            }
        }

        // --- Push-to-Talk Helpers (eliminates duplicate sub/unsub/enable patterns) ---

        private bool AnyModeEnabled()
        {
            bool dictAny = (_settings.DictationEnabled && (_settings.DictationKey != 0 || _settings.DictationKey2 != 0)) ||
                           (_settings.DictationToggleEnabled && (_settings.DictationToggleKey != 0 || _settings.DictationToggleKey2 != 0));
            return _settings.PushToTalkEnabled || _settings.PushToMuteEnabled || _settings.PushToToggleEnabled || dictAny;
        }

        /// <summary>Clear stale hotkeys from disabled toggles. Prevents stuck-mute on startup.</summary>
        private void SanitizeHotkeys()
        {
            bool changed = false;
            if (!_settings.PushToTalkEnabled) {
                if (_settings.PushToTalkKey != 0) { _settings.PushToTalkKey = 0; changed = true; }
                if (_settings.PushToTalkKey2 != 0) { _settings.PushToTalkKey2 = 0; changed = true; }
            }
            if (!_settings.PushToMuteEnabled) {
                if (_settings.PushToMuteKey != 0) { _settings.PushToMuteKey = 0; changed = true; }
                if (_settings.PushToMuteKey2 != 0) { _settings.PushToMuteKey2 = 0; changed = true; }
            }
            // If Toggle IS ON and PTT keys exist, migrate them to MM (they should never coexist)
            if (_settings.PushToToggleEnabled) {
                if (_settings.PushToTalkKey != 0 && _settings.MultiModeKey == 0) {
                    _settings.MultiModeKey = _settings.PushToTalkKey; changed = true;
                }
                if (_settings.PushToTalkKey != 0) { _settings.PushToTalkKey = 0; changed = true; }
                if (_settings.PushToTalkKey2 != 0) { _settings.PushToTalkKey2 = 0; changed = true; }
                _settings.PushToTalkEnabled = false; changed = true;
            }
            if (!_settings.PushToToggleEnabled) {
                if (_settings.PushToToggleKey != 0) { _settings.PushToToggleKey = 0; changed = true; }
                if (_settings.PushToToggleKey2 != 0) { _settings.PushToToggleKey2 = 0; changed = true; }
                if (_settings.MultiModeKey != 0) { _settings.MultiModeKey = 0; changed = true; }
                if (_settings.MultiModeKey2 != 0) { _settings.MultiModeKey2 = 0; changed = true; }
            }
            // Disable modes that are ON but have no key (should never happen)
            if (_settings.PushToTalkEnabled && _settings.PushToTalkKey == 0) { _settings.PushToTalkEnabled = false; changed = true; }
            if (_settings.PushToMuteEnabled && _settings.PushToMuteKey == 0) { _settings.PushToMuteEnabled = false; changed = true; }
            if (_settings.PushToToggleEnabled && _settings.PushToToggleKey == 0) { _settings.PushToToggleEnabled = false; changed = true; }
            if (changed) {
                _settings.Save();
                Logger.Info("SanitizeHotkeys: cleared stale keys from disabled toggles.");
            }
        }
        private void EnablePtt()
        {
            if (_pushToTalk == null) return;
            if (!AnyModeEnabled()) return;
            _pushToTalk.OnTalkStart -= OnPttTalkStart;
            _pushToTalk.OnTalkStop -= OnPttTalkStop;
            _pushToTalk.OnTalkStart += OnPttTalkStart;
            _pushToTalk.OnTalkStop += OnPttTalkStop;
            // Per-mode keys — each mode uses its own key, no fallback
            int pttKey = _settings.PushToTalkEnabled ? _settings.PushToTalkKey : 0;
            int ptmKey = _settings.PushToMuteEnabled ? _settings.PushToMuteKey : 0;
            int toggleKey = _settings.PushToToggleEnabled ? _settings.PushToToggleKey : 0;
            int mmKey = _settings.MultiModeKey;
            int mmKey2 = _settings.MultiModeKey2;
            
            bool dictNeedsHook = false;
            // Collect all dictation keys for LL hook suppression
            var dictKeyList = new System.Collections.Generic.List<int>();
            if (_settings.DictationEnabled && _settings.DictationKey > 0) dictKeyList.Add(_settings.DictationKey);
            if (_settings.DictationEnabled && _settings.DictationKey2 > 0) dictKeyList.Add(_settings.DictationKey2);
            if (_settings.DictationToggleEnabled && _settings.DictationToggleKey > 0) dictKeyList.Add(_settings.DictationToggleKey);
            if (_settings.DictationToggleEnabled && _settings.DictationToggleKey2 > 0) dictKeyList.Add(_settings.DictationToggleKey2);
            if (dictKeyList.Count > 0) dictNeedsHook = true;
            
            // Collect keys that should be swallowed by the hook
            var swallowedKeysList = new System.Collections.Generic.List<int>();
            if (_settings.PttSuppressEnabled) {
                if (pttKey > 0) swallowedKeysList.Add(pttKey);
                if (_settings.PushToTalkKey2 > 0) swallowedKeysList.Add(_settings.PushToTalkKey2);
            }
            if (_settings.PtmSuppressEnabled) {
                if (ptmKey > 0) swallowedKeysList.Add(ptmKey);
                if (_settings.PushToMuteKey2 > 0) swallowedKeysList.Add(_settings.PushToMuteKey2);
            }
            if (_settings.PushToToggleEnabled && _settings.PtToggleSuppressEnabled) {
                if (toggleKey > 0) swallowedKeysList.Add(toggleKey);
                if (_settings.PushToToggleKey2 > 0) swallowedKeysList.Add(_settings.PushToToggleKey2);
            }
            if (_settings.DictationEnabled && _settings.DictPthSuppressEnabled) {
                if (_settings.DictationKey > 0) swallowedKeysList.Add(_settings.DictationKey);
                if (_settings.DictationKey2 > 0) swallowedKeysList.Add(_settings.DictationKey2);
            }
            if (_settings.DictationToggleEnabled && _settings.DictPttSuppressEnabled) {
                if (_settings.DictationToggleKey > 0) swallowedKeysList.Add(_settings.DictationToggleKey);
                if (_settings.DictationToggleKey2 > 0) swallowedKeysList.Add(_settings.DictationToggleKey2);
            }
            
            _pushToTalk.EnableMultiMode(pttKey, ptmKey, toggleKey, false, _settings.PushToTalkKey2, _settings.PushToMuteKey2, _settings.PushToToggleKey2, mmKey, mmKey2, _settings.PushToToggleEnabled, dictNeedsHook, dictKeyList.Count > 0 ? dictKeyList.ToArray() : null, swallowedKeysList.Count > 0 ? swallowedKeysList.ToArray() : null);
            // Explicitly sync system mic state to match chosen mode
            // Toggle always starts OFF (muted). PTM-only starts open. Everything else starts muted.
            // CRITICAL: only consider a mode "active" if it has an actual key assigned.
            // A mode with Enabled=true but key=0 is effectively inactive and must NOT keep mic muted.
            try {
                bool pttActual = _audio.PttEnabled && _settings.PushToTalkKey > 0;
                bool ptmActual = _audio.PtmEnabled && _settings.PushToMuteKey > 0;
                bool toggleActual = _audio.PtToggleEnabled && _settings.PushToToggleKey > 0;
                bool ptmOnlyActive = ptmActual && !pttActual && !toggleActual;
                if (!pttActual && !ptmActual && !toggleActual) {
                    // No operational mode — ensure mic is open
                    Audio.SetMicMute(false);
                } else {
                    // Dictation God Mode: don't mute mic if dictation is actively recording
                    if (DictationManager.Current != null && DictationManager.Current.IsActive)
                        Audio.SetMicMute(false); // keep mic open for dictation
                    else
                        Audio.SetMicMute(!ptmOnlyActive);
                }
            } catch { }
            // Show correct overlay state
            if (_micStatus != null && !_micStatus.IsDisposed)
            {
                bool hasPtt = pttKey > 0 || _settings.PushToTalkKey2 > 0;
                bool hasPtm = ptmKey > 0 || _settings.PushToMuteKey2 > 0;
                bool hasToggle = toggleKey > 0 || _settings.PushToToggleKey2 > 0;

                bool idleEnabled = false;
                if (hasPtt && _settings.PttKey1ShowOverlay) idleEnabled = true;
                if (hasPtm && _settings.PtmShowOverlay) idleEnabled = true;
                if (hasToggle && _settings.PtToggleShowOverlay) idleEnabled = true;

                if (!idleEnabled)
                {
                    _micStatus.HideOverlay();
                }
                else
                {
                    if (hasPtm && !hasPtt && !hasToggle)
                        _micStatus.ShowMicOpenIdle();
                    else
                        _micStatus.ShowMicClosed();
                }
            }
        }

        /// <summary>Smoothly fade speaker volume over ~100ms (10 × 10ms).
        /// Cancels any in-progress fade. Calls onComplete when done.</summary>
        private void FadeDuckVolume(float from, float to, Action onComplete = null)
        {
            // Cancel any existing fade
            if (_duckFadeTimer != null) { try { _duckFadeTimer.Dispose(); } catch { } _duckFadeTimer = null; }
            const int steps = 10;
            const int intervalMs = 10; // 10 steps * 10ms = 100ms total fade duration
            int step = 0;
            float startVol = from;
            float endVol = to;
            _duckFadeTimer = new System.Threading.Timer(_ => {
                step++;
                if (step >= steps) {
                    try { Audio.SetSpeakerVolume(endVol); } catch { }
                    var t = _duckFadeTimer; _duckFadeTimer = null; if (t != null) try { t.Dispose(); } catch { }
                    try { if (_openOptionsForm != null && !_openOptionsForm.IsDisposed) _openOptionsForm.BeginInvoke(new Action(() => _openOptionsForm.Invalidate())); } catch { }
                    if (onComplete != null) try { onComplete(); } catch { }
                    return;
                }
                float progress = (float)step / steps;
                float vol = startVol + (endVol - startVol) * progress;
                try { Audio.SetSpeakerVolume(vol); } catch { }
                try { if (_openOptionsForm != null && !_openOptionsForm.IsDisposed) _openOptionsForm.BeginInvoke(new Action(() => _openOptionsForm.Invalidate(true))); } catch { }
            }, null, 0, intervalMs);
        }

        private void DisablePtt()
        {
            if (_pushToTalk == null) return;
            _pushToTalk.OnTalkStart -= OnPttTalkStart;
            _pushToTalk.OnTalkStop -= OnPttTalkStop;
            _pushToTalk.Disable();
            // Safety: always unmute when engine stops
            try { Audio.SetMicMute(false); } catch { }
            Logger.Info("PTT engine disabled + mic unmuted.");
        }

        // --- Push-to-Talk Events ---

        private bool IsCapturingKey()
        {
            try { return _openOptionsForm != null && !_openOptionsForm.IsDisposed && _openOptionsForm.IsCapturingKey; } catch { return false; }
        }

        private bool ShouldShowOverlayForKey()
        {
            if (_pushToTalk == null) return false;
            // Voice Activity mode — use VA overlay setting
            if (_pushToTalk.VoiceMonitoringActive) return _settings.VoiceActivityShowOverlay;
            int mode = _pushToTalk.ActiveMode;
            if (mode == 0)
            {
                // PTT has per-key overlay toggles — check which key triggered
                int triggeredKey = _pushToTalk.LastTriggeredKey;
                if (triggeredKey == _settings.PushToTalkKey2 && _settings.PushToTalkKey2 > 0) return _settings.PttKey2ShowOverlay;
                return _settings.PttKey1ShowOverlay;
            }
            if (mode == 1) return _settings.PtmShowOverlay;
            if (mode == 2) return _settings.PtToggleShowOverlay;
            if (mode == 3) {
                // MM key: when acting as PTT (toggle closed), use PTT overlay setting
                // When acting as PTM (toggle open), use PTM overlay setting
                if (_pushToTalk.ToggleMicOpen) return _settings.PtmShowOverlay;
                return _settings.PttKey1ShowOverlay;
            }
            return true;
        }

        private void OnPttTalkStart()
        {
            // Sound feedback — check the specific mode's setting
            bool shouldPlaySound = false;
            int soundVol = 80;
            int soundType = 0;
            if (_pushToTalk != null) {
                if (_pushToTalk.VoiceMonitoringActive) { shouldPlaySound = _settings.VoiceActivitySoundFeedback; soundVol = _settings.PttSoundVolume; soundType = _settings.PttSoundType; }
                else {
                    int mode = _pushToTalk.ActiveMode;
                    if (mode == 0) { shouldPlaySound = _settings.PttSoundFeedback; soundVol = _settings.PttSoundVolume; soundType = _settings.PttSoundType; }
                    else if (mode == 1) { shouldPlaySound = _settings.PtmSoundFeedback; soundVol = _settings.PtmSoundVolume; soundType = _settings.PtmSoundType; }
                    else if (mode == 2) { shouldPlaySound = _settings.PtToggleSoundFeedback; soundVol = _settings.PtToggleSoundVolume; soundType = _settings.PtToggleSoundType; }
                    else if (mode == 3) {
                        bool mmOpen = _pushToTalk.ToggleMicOpen;
                        shouldPlaySound = mmOpen ? _settings.PtmSoundFeedback : _settings.PttSoundFeedback;
                        soundVol = mmOpen ? _settings.PtmSoundVolume : _settings.PttSoundVolume;
                        soundType = mmOpen ? _settings.PtmSoundType : _settings.PttSoundType;
                    }
                }
            }
            if (shouldPlaySound) PlayFeedbackSound(true, soundVol, soundType);

            // PTT Audio Ducking: lower system volume when mic is active
            if (_pushToTalk != null && !_pushToTalk.VoiceMonitoringActive) {
                int mode = _pushToTalk.ActiveMode;
                bool shouldDuck = (mode == 0 && _settings.PttDuckEnabled)
                               || (mode == 1 && _settings.PtmDuckEnabled)
                               || (mode == 2 && _settings.PtToggleDuckEnabled)
                               || (mode == 3 && (_settings.PttDuckEnabled || _settings.PtmDuckEnabled)); // MM checks PTT or PTM duck
                if (shouldDuck) {
                    lock (_duckLock) {
                        if (_isDucked && !_duckFading) { } // already fully ducked — skip
                        else {
                            float cur = Audio.GetSpeakerVolume();
                            if (!_isDucked && !_duckFading) { if (cur >= 0) _preDuckVolume = cur; }
                            float target = cur * _settings.PttDuckingVolume / 100f;
                            _isDucked = true;
                            _duckFading = true;
                            FadeDuckVolume(cur, target, () => { lock (_duckLock) _duckFading = false; });
                        }
                    }
                }
            }

            // Toggle state toast: when toggle flips to OPEN and MM key is set, show what MM does now
            if (_pushToTalk != null && _pushToTalk.ActiveMode == 2 && _settings.MultiModeKey > 0)
            {
                string mmName = PushToTalk.GetKeyName(_settings.MultiModeKey);
                if (_settings.MultiModeKey2 > 0) mmName += " / " + PushToTalk.GetKeyName(_settings.MultiModeKey2);
                string msg = "Mic Open \u2014 Hold " + mmName + " to mute";
                if (_micStatus != null && !_micStatus.IsDisposed)
                    try { _micStatus.BeginInvoke(new Action(() => ShowToggleStatusToast(msg, 1))); } catch { }
            }

            // If mic is AFK muted, break out of AFK immediately
            if (_micAfkState == AfkState.AfkMuted || _micAfkState == AfkState.FadingIn || _micAfkState == AfkState.FadingOut)
            {
                try
                {
                    float restoreMic = _settings.MicEnforceEnabled ? (float)_settings.MicVolumePercent : _preFadeMicVolume;
                    Audio.SetMicVolume(restoreMic);
                    Audio.SetMicMute(false);
                    Logger.Info("PTT broke AFK mic mute — restored to " + restoreMic + "%.");
                }
                catch (Exception ex) { Logger.Error("PTT AFK break failed.", ex); }
                _micAfkState = AfkState.Active;
                _lastMicEnforce = DateTime.UtcNow;
            }

            UpdateTrayState();
            try
            {
                if (_micStatus != null && !_micStatus.IsDisposed)
                {
                    _micStatus.MultiModeHeld = (_pushToTalk != null && _pushToTalk.ActiveMode == 3);
                    bool shouldShow = ShouldShowOverlayForKey();
                    Logger.Debug("OnPttTalkStart [DIAG]: shouldShow=" + shouldShow + " activeMode=" + (_pushToTalk != null ? _pushToTalk.ActiveMode.ToString() : "null"));
                    if (shouldShow)
                        _micStatus.ShowMicOpen();
                    else
                        _micStatus.HideOverlay();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to show mic open overlay.", ex);
            }
            SchedulePttSafetyCheck();
        }

        private void OnPttTalkStop()
        {
            // Sound feedback — check the specific mode's setting
            bool shouldPlaySound = false;
            int soundVol = 80;
            int soundType = 0;
            if (_pushToTalk != null) {
                if (_pushToTalk.VoiceMonitoringActive) { shouldPlaySound = _settings.VoiceActivitySoundFeedback; soundVol = _settings.PttSoundVolume; soundType = _settings.PttSoundType; }
                else {
                    int mode = _pushToTalk.ActiveMode;
                    if (mode == 0) { shouldPlaySound = _settings.PttSoundFeedback; soundVol = _settings.PttSoundVolume; soundType = _settings.PttSoundType; }
                    else if (mode == 1) { shouldPlaySound = _settings.PtmSoundFeedback; soundVol = _settings.PtmSoundVolume; soundType = _settings.PtmSoundType; }
                    else if (mode == 2) { shouldPlaySound = _settings.PtToggleSoundFeedback; soundVol = _settings.PtToggleSoundVolume; soundType = _settings.PtToggleSoundType; }
                    else if (mode == 3) {
                        bool mmOpen = _pushToTalk.ToggleMicOpen;
                        shouldPlaySound = mmOpen ? _settings.PtmSoundFeedback : _settings.PttSoundFeedback;
                        soundVol = mmOpen ? _settings.PtmSoundVolume : _settings.PttSoundVolume;
                        soundType = mmOpen ? _settings.PtmSoundType : _settings.PttSoundType;
                    }
                }
            }
            if (shouldPlaySound) PlayFeedbackSound(false, soundVol, soundType);

            // PTT Audio Ducking: restore system volume when mic goes inactive
            lock (_duckLock) {
                if (_isDucked || _duckFading) {
                    _isDucked = false;
                    _duckFading = true;
                    float cur = Audio.GetSpeakerVolume();
                    FadeDuckVolume(cur, _preDuckVolume, () => { lock (_duckLock) _duckFading = false; });
                }
            }

            // Toggle state toast: when toggle flips to CLOSED and MM key is set, show what MM does now
            if (_pushToTalk != null && _pushToTalk.ActiveMode == 2 && _settings.MultiModeKey > 0)
            {
                string mmName = PushToTalk.GetKeyName(_settings.MultiModeKey);
                if (_settings.MultiModeKey2 > 0) mmName += " / " + PushToTalk.GetKeyName(_settings.MultiModeKey2);
                string msg = "Mic Closed \u2014 Hold " + mmName + " to talk";
                if (_micStatus != null && !_micStatus.IsDisposed)
                    try { _micStatus.BeginInvoke(new Action(() => ShowToggleStatusToast(msg, 2))); } catch { }
            }

            UpdateTrayState();
            try
            {
                if (_micStatus != null && !_micStatus.IsDisposed)
                {
                    // MM key held detection: mode 3 + toggle ON = MM-as-PTM key-down (held)
                    // mode 3 + toggle OFF + OnTalkStop = MM-as-PTT key-up (release)
                    _micStatus.MultiModeHeld = false; // MM key released
                    if (ShouldShowOverlayForKey())
                        _micStatus.ShowMicClosed();
                    else
                        _micStatus.HideOverlay();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to show mic closed overlay.", ex);
            }
            SchedulePttSafetyCheck();
        }

        /// <summary>
        /// Schedules a delayed check to reconcile overlay state with actual PTT state.
        /// Fixes stuck overlay when rapid key spamming causes event ordering issues.
        /// </summary>
        /// <summary>Play audio feedback sound based on user's selected type with volume control.</summary>
        private void PlayFeedbackSound(bool micOpen, int volumePercent, int soundType)
        {
            if (_settings.RestrictSoundOutput) return; // Silent Mode: mute all sound effects
            int type = soundType;
            string customPath = _settings.CustomSoundPath;
            float vol = Math.Max(0f, Math.Min(1f, volumePercent / 100f));
            System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                try {
                    if (type == 6 && !string.IsNullOrEmpty(customPath) && System.IO.File.Exists(customPath)) {
                        try { PlayWavFileAtVolume(customPath, vol); } catch { PlayTone(800, 15, vol); }
                        return;
                    }
                    switch (type) {
                        case 0: PlayTone(micOpen ? 800 : 600, 15, vol); break;
                        case 1: PlayTone(micOpen ? 700 : 500, 10, vol); System.Threading.Thread.Sleep(30); PlayTone(micOpen ? 900 : 600, 10, vol); break;
                        case 2: PlayTone(micOpen ? 600 : 900, 8, vol); PlayTone(micOpen ? 900 : 600, 12, vol); break;
                        case 3: PlayTone(micOpen ? 1000 : 700, 20, vol); break;
                        case 4: PlayTone(micOpen ? 523 : 659, 25, vol); System.Threading.Thread.Sleep(20); PlayTone(micOpen ? 659 : 523, 25, vol); break;
                        case 5: PlayTone(micOpen ? 500 : 400, 8, vol); break;
                        default: PlayTone(micOpen ? 800 : 600, 15, vol); break;
                    }
                } catch { }
            });
        }

        /// <summary>Play a preview of a feedback sound type (for Options page).</summary>
        public static void PreviewFeedbackSound(int type, int volumePercent, string customPath = null)
        {
            float vol = Math.Max(0f, Math.Min(1f, volumePercent / 100f));
            System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                try {
                    if (type == 6 && !string.IsNullOrEmpty(customPath) && System.IO.File.Exists(customPath)) {
                        try { PlayWavFileAtVolume(customPath, vol); } catch { PlayTone(800, 15, vol); }
                        return;
                    }
                    switch (type) {
                        case 0: PlayTone(800, 15, vol); break;
                        case 1: PlayTone(700, 10, vol); System.Threading.Thread.Sleep(30); PlayTone(900, 10, vol); break;
                        case 2: PlayTone(600, 8, vol); PlayTone(900, 12, vol); break;
                        case 3: PlayTone(1000, 20, vol); break;
                        case 4: PlayTone(523, 25, vol); System.Threading.Thread.Sleep(20); PlayTone(659, 25, vol); break;
                        case 5: PlayTone(500, 8, vol); break;
                        default: PlayTone(800, 15, vol); break;
                    }
                } catch { }
            });
        }

        /// <summary>Generate and play a sine-wave tone as an in-memory WAV at the given volume (0.0–1.0).</summary>
        private static void PlayTone(int freqHz, int durationMs, float volume)
        {
            const int sampleRate = 44100;
            const int bitsPerSample = 16;
            const int channels = 1;
            int numSamples = sampleRate * durationMs / 1000;
            if (numSamples < 1) numSamples = 1;
            int dataSize = numSamples * channels * (bitsPerSample / 8);

            // Build WAV in memory
            using (var ms = new System.IO.MemoryStream(44 + dataSize))
            using (var bw = new System.IO.BinaryWriter(ms))
            {
                // RIFF header
                bw.Write(new char[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + dataSize);
                bw.Write(new char[] { 'W', 'A', 'V', 'E' });
                // fmt chunk
                bw.Write(new char[] { 'f', 'm', 't', ' ' });
                bw.Write(16); // chunk size
                bw.Write((short)1); // PCM
                bw.Write((short)channels);
                bw.Write(sampleRate);
                bw.Write(sampleRate * channels * bitsPerSample / 8); // byte rate
                bw.Write((short)(channels * bitsPerSample / 8)); // block align
                bw.Write((short)bitsPerSample);
                // data chunk
                bw.Write(new char[] { 'd', 'a', 't', 'a' });
                bw.Write(dataSize);

                double amplitude = 32760.0 * volume;
                double twoPiF = 2.0 * Math.PI * freqHz;
                // Fade in/out over 10% of duration to avoid clicks
                int fadeLen = Math.Max(1, numSamples / 10);
                for (int i = 0; i < numSamples; i++)
                {
                    double t = (double)i / sampleRate;
                    double sample = Math.Sin(twoPiF * t) * amplitude;
                    // Apply fade envelope
                    if (i < fadeLen) sample *= (double)i / fadeLen;
                    else if (i > numSamples - fadeLen) sample *= (double)(numSamples - i) / fadeLen;
                    bw.Write((short)Math.Max(-32768, Math.Min(32767, (int)sample)));
                }

                ms.Position = 0;
                using (var player = new System.Media.SoundPlayer(ms))
                    player.PlaySync();
            }
        }

        /// <summary>Play a WAV file with volume scaling.</summary>
        private static void PlayWavFileAtVolume(string path, float volume)
        {
            // For custom WAVs: read, scale samples, play from memory
            byte[] raw = System.IO.File.ReadAllBytes(path);
            if (raw.Length < 44 || volume >= 0.99f) {
                // Near-full volume or too short to process — play as-is
                using (var ms = new System.IO.MemoryStream(raw))
                using (var player = new System.Media.SoundPlayer(ms))
                    player.PlaySync();
                return;
            }
            // Find data chunk offset
            int dataOff = 12;
            while (dataOff < raw.Length - 8) {
                string chunkId = System.Text.Encoding.ASCII.GetString(raw, dataOff, 4);
                int chunkSz = BitConverter.ToInt32(raw, dataOff + 4);
                if (chunkId == "data") { dataOff += 8; break; }
                dataOff += 8 + chunkSz;
            }
            // Scale 16-bit samples
            int bps = BitConverter.ToInt16(raw, 34);
            if (bps == 16) {
                for (int i = dataOff; i + 1 < raw.Length; i += 2) {
                    short s = BitConverter.ToInt16(raw, i);
                    s = (short)(s * volume);
                    raw[i] = (byte)(s & 0xFF);
                    raw[i + 1] = (byte)((s >> 8) & 0xFF);
                }
            }
            using (var ms = new System.IO.MemoryStream(raw))
            using (var player = new System.Media.SoundPlayer(ms))
                player.PlaySync();
        }

        private void SchedulePttSafetyCheck()
        {
            if (_pttSafetyTimer != null) { try { _pttSafetyTimer.Dispose(); } catch { } }
            _pttSafetyTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (_pushToTalk == null || !_pushToTalk.Enabled) return;
                    if (_micStatus == null || _micStatus.IsDisposed) return;
                    if (!ShouldShowOverlayForKey()) return; // eyeball is off — don't force overlay
                    bool talking = _pushToTalk.IsTalking;
                    bool overlayShowsMicOpen = _micStatus.IsMicOpen;
                    if (!talking && overlayShowsMicOpen)
                    {
                        Logger.Info("PTT safety: overlay stuck open — forcing closed.");
                        _micStatus.ShowMicClosed();
                    }
                    else if (talking && !overlayShowsMicOpen && _micStatus.OverlayEnabled)
                    {
                        Logger.Info("PTT safety: overlay stuck closed — forcing open.");
                        _micStatus.ShowMicOpen();
                    }
                }
                catch { }
            }, null, 150, System.Threading.Timeout.Infinite);
        }

        // --- Splash ---

        private SplashForm _activeSplash;

        private void ShowSplash(Action afterClose = null)
        {
            try
            {
                CloseSplash(); // close any existing splash first
                PopupThread.Invoke(() =>
                {
                    try
                    {
                        var splash = new SplashForm(_settings);
                        _activeSplash = splash;
                        if (afterClose != null)
                            splash.FormClosed += (s, e) => afterClose();
                        splash.FormClosed += (s, e) => { if (_activeSplash == splash) _activeSplash = null; };
                        splash.Show();
                    }
                    catch { if (afterClose != null) try { afterClose(); } catch { } }
                });
                Logger.Info("Privacy splash shown.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to show splash.", ex);
                if (afterClose != null) try { afterClose(); } catch { }
            }
        }

        private void CloseSplash()
        {
            var s = _activeSplash;
            _activeSplash = null;
            if (s != null && !s.IsDisposed)
                PopupThread.Invoke(() => { try { s.Close(); } catch { } });
        }

        /// <summary>Called by OptionsForm "Run Splash!" button — hides options, shows splash, then restores options.</summary>
        internal void RunSplashFromOptions()
        {
            if (_openOptionsForm != null && !_openOptionsForm.IsDisposed)
                _openOptionsForm.Hide();
            ShowSplash(() =>
            {
                // Re-show options after splash closes
                if (_openOptionsForm != null && !_openOptionsForm.IsDisposed)
                {
                    try
                    {
                        if (_openOptionsForm.InvokeRequired)
                            _openOptionsForm.BeginInvoke((MethodInvoker)(() => { _openOptionsForm.Show(); _openOptionsForm.BringToFront(); }));
                        else
                            { _openOptionsForm.Show(); _openOptionsForm.BringToFront(); }
                    }
                    catch { }
                }
            });
        }

        // --- Menu Actions ---

        private bool _optionsPreCreated;

        /// <summary>Pre-creates the OptionsForm hidden so first open is instant.</summary>
        private void PreCreateOptionsForm()
        {
            if (_optionsPreCreated) return;
            _optionsPreCreated = true;
            try
            {
                if (_openOptionsForm != null && !_openOptionsForm.IsDisposed) return;
                _openOptionsForm = new OptionsForm(_settings, _audio, _pushToTalk);
                _openOptionsForm.TopMost = false;
                _openOptionsForm.FormClosing += OptionsForm_Closing;

                // Force handle creation, uxtheme.dll styling, and heavy GDI rendering 
                // completely in the background without stealing focus or flashing
                _openOptionsForm.ForceNoActivation = true;
                _openOptionsForm.Opacity = 0;
                _openOptionsForm.Show();
                Application.DoEvents(); // Force WinForms to synchronously process WM_PAINT and handle creation
                _openOptionsForm.Hide();
                _openOptionsForm.Opacity = 1;
                _openOptionsForm.ForceNoActivation = false;

                Logger.Info("OptionsForm pre-created and rendered (hidden).");
            }
            catch (Exception ex) { Logger.Error("Failed to pre-create OptionsForm.", ex); }
        }

        /// <summary>Ensures _openOptionsForm exists, creating it if needed.</summary>
        private void EnsureOptionsForm()
        {
            if (_openOptionsForm == null || _openOptionsForm.IsDisposed)
            {
                _openOptionsForm = new OptionsForm(_settings, _audio, _pushToTalk);
                _openOptionsForm.FormClosing += OptionsForm_Closing;
            }
        }

        /// <summary>Hide instead of destroy so reopening is instant.</summary>
        private void OptionsForm_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Intercept close: hide instead of destroy
            e.Cancel = true;
            _openOptionsForm.Hide();

            var result = _openOptionsForm.DialogResult;
            _openOptionsForm.DialogResult = DialogResult.None; // always reset to prevent stale state
            if (result == DialogResult.OK)
            {
                _settings.Save();
                _settings.ApplyStartupSetting();
                Logger.Info("Settings saved by user.");
            }
            else if (result == DialogResult.Retry)
            {
                ShowSplash();
            }
        }

        private void ShowOptions()
        {
            ShowOptions(-1, false);
        }
        private void ShowOptions(int paneIndex)
        {
            ShowOptions(paneIndex, false);
        }
        private void ShowOptions(int paneIndex, bool blinkOverlayToggle)
        {
            try
            {
                CloseSplash(); // never overlap splash with options

                EnsureOptionsForm();

                // Randomize star backdrop each time options is shown
                _openOptionsForm.RandomizeBackdrop();

                if (paneIndex >= 0) _openOptionsForm.NavigateToPane(paneIndex);
                if (blinkOverlayToggle && !_openOptionsForm.Visible)
                    _openOptionsForm.Shown += BlinkOnceHandler;

                if (_openOptionsForm.WindowState == FormWindowState.Minimized)
                    _openOptionsForm.WindowState = FormWindowState.Normal;

                _openOptionsForm.TopMost = true;
                _openOptionsForm.Show();
                _openOptionsForm.BringToFront();
                _openOptionsForm.Activate();

                // Drop TopMost after activation so it behaves normally
                _openOptionsForm.Activated += DropTopMostOnce;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to show options.", ex);
            }
        }

        private void BlinkOnceHandler(object sender, EventArgs e)
        {
            if (_openOptionsForm != null) _openOptionsForm.Shown -= BlinkOnceHandler;
            if (_openOptionsForm != null) _openOptionsForm.BlinkOverlayToggle();
        }

        private void DropTopMostOnce(object sender, EventArgs e)
        {
            if (_openOptionsForm != null && _openOptionsForm.TopMost)
                _openOptionsForm.TopMost = false;
            if (_openOptionsForm != null) _openOptionsForm.Activated -= DropTopMostOnce;
        }

        // --- AudioSettings Changed Handler ---
        // This is the unified actuation point. When ANY AudioSettings property changes,
        // this fires and TrayApp actuates the hardware/overlay/engine accordingly.
        private bool _audioSettingsProcessing;
        private bool _deferredPttRestart; // set by SuspendHook guard, consumed by CaptureStateChanged(false)
        private void OnAudioSettingsChanged(SettingsChange what)
        {
            if (_audioSettingsProcessing) return;
            _audioSettingsProcessing = true;
            try
            {
                if ((what & SettingsChange.PttMode) != 0)
                {
                    // Sync overlay mode flags
                    if (_micStatus != null && !_micStatus.IsDisposed)
                    {
                        _micStatus.PushToMuteMode = _audio.PtmEnabled;
                        _micStatus.ToggleMode = _audio.PtToggleEnabled;
                    }

                    bool shouldBeActive = _audio.PttEnabled || _audio.PtmEnabled || _audio.PtToggleEnabled;

                    // Disable Voice Activity if PTT/PTM/Toggle is being enabled — mutually exclusive
                    if (shouldBeActive && _audio.VoiceActivityEnabled)
                    {
                        if (_pushToTalk != null)
                        {
                            _pushToTalk.OnTalkStart -= OnPttTalkStart;
                            _pushToTalk.OnTalkStop -= OnPttTalkStop;
                            _pushToTalk.DisableVoiceActivity();
                        }
                        _audio.BeginUpdate();
                        _audio.VoiceActivityEnabled = false;
                        _audio.EndUpdate();
                    }

                    // CRITICAL: If we're mid-capture-callback, do NOT restart the engine!
                    // ClearKeyFromAllSlots fires PttMode mid-callback — restarting would install
                    // a new hook with SuspendHook=false, re-suppressing the very key being captured.
                    // We check SuspendHook (not IsCapturingKey) because _capturing is already false
                    // by the time the callback runs, but SuspendHook stays true until AFTER the callback.
                    if (_pushToTalk != null && _pushToTalk.SuspendHook)
                    {
                        _deferredPttRestart = true;
                        Logger.Info("PttMode change during capture callback — deferring engine restart.");
                        UpdateTrayState();
                    }
                    else
                    {
                    bool engineNeedsRestart = shouldBeActive;

                    if (shouldBeActive && engineNeedsRestart)
                    {
                        // Offload heavy engine restart to background thread to prevent UI lag.
                        // DisablePtt/EnablePtt involve P/Invoke SetWindowsHookEx which blocks the message pump.
                        System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                            DisablePtt();
                            EnablePtt();

                            // Build dynamic key list for toast (read-only access to _audio/_settings is safe)
                            string modeName = null;
                            string keyList = null;
                            string modeDesc = null;
                            if (_audio.PttEnabled && _audio.PttKey > 0)
                            {
                                modeName = "Push-to-Talk";
                                keyList = PushToTalk.GetKeyName(_audio.PttKey);
                                if (_audio.PttKey2 > 0) keyList += ", " + PushToTalk.GetKeyName(_audio.PttKey2);
                                modeDesc = "Hold (" + keyList + ") to unmute your mic";
                            }
                            else if (_audio.PtmEnabled && _audio.PtmKey > 0)
                            {
                                modeName = "Push-to-Mute";
                                keyList = PushToTalk.GetKeyName(_audio.PtmKey);
                                if (_audio.PtmKey2 > 0) keyList += ", " + PushToTalk.GetKeyName(_audio.PtmKey2);
                                modeDesc = "Hold (" + keyList + ") to mute your mic";
                            }
                            else if (_audio.PtToggleEnabled && _audio.PtToggleKey > 0)
                            {
                                modeName = "Push-to-Toggle";
                                keyList = PushToTalk.GetKeyName(_audio.PtToggleKey);
                                if (_audio.PtToggleKey2 > 0) keyList += ", " + PushToTalk.GetKeyName(_audio.PtToggleKey2);
                                modeDesc = "Tap (" + keyList + ") to toggle your mic on/off";
                            }

                            // Marshal toast back to UI thread
                            if (modeName != null && modeDesc != null)
                                InvokeOnUiThread(() => ShowCorrectionToast(modeName + " Enabled \u2014 " + modeDesc, true));

                            InvokeOnUiThread(() => UpdateTrayState());
                        });
                    }
                    else if (!shouldBeActive)
                    {
                        // Dismiss correction toast synchronously to avoid racing with mic warning
                        if (_activeToast != null && !_activeToast.IsDisposed)
                            try { _activeToast.Dismiss(); } catch { }
                        System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                            DisablePtt();
                            try { Audio.SetMicMute(false); } catch { }
                            // Re-enable for dictation key suppression only (no input modes, just the hook)
                            bool dictHasKeys = (_settings.DictationEnabled && (_settings.DictationKey > 0 || _settings.DictationKey2 > 0)) ||
                                               (_settings.DictationToggleEnabled && (_settings.DictationToggleKey > 0 || _settings.DictationToggleKey2 > 0));
                            if (dictHasKeys) EnablePtt(); // Installs hook for dict key suppression
                            InvokeOnUiThread(() => {
                                if (_micStatus != null && !_micStatus.IsDisposed)
                                    _micStatus.HideOverlay();
                                UpdateTrayState();
                            });
                        });
                    }

                    } // end else (not capturing)
                }

                if ((what & SettingsChange.MicLock) != 0)
                {
                    if (_audio.MicLockEnabled)
                    {
                        try { _micPreLockVol = (int)Audio.GetMicVolume(); } catch { _micPreLockVol = -1; }
                        if (_restoreMicTimer != null) { _restoreMicTimer.Dispose(); _restoreMicTimer = null; }
                        EnforceMic(false);
                        ShowCorrectionToast("Mic Volume Locked at (" + _audio.MicLockVolume + "%)" + " \u2014 Volume enforcement is active", true, true, false);
                        Logger.Info("Mic lock ON (snapshot: " + _micPreLockVol + "%)");
                    }
                    else
                    {
                        DismissActiveToast();
                        SmoothRestore(true);
                        Logger.Info("Mic lock OFF (restoring to " + _micPreLockVol + "%)");
                    }
                    UpdateTrayState();
                }

                if ((what & SettingsChange.SpeakerLock) != 0)
                {
                    if (_audio.SpeakerLockEnabled)
                    {
                        try { _spkPreLockVol = (int)Audio.GetSpeakerVolume(); } catch { _spkPreLockVol = -1; }
                        if (_restoreSpkTimer != null) { _restoreSpkTimer.Dispose(); _restoreSpkTimer = null; }
                        EnforceSpeaker(false);
                        ShowCorrectionToast("Speaker Volume Locked at (" + _audio.SpeakerLockVolume + "%)" + " \u2014 Volume enforcement is active", true, false, true);
                        Logger.Info("Speaker lock ON (snapshot: " + _spkPreLockVol + "%)");
                    }
                    else
                    {
                        DismissActiveToast();
                        SmoothRestore(false);
                        Logger.Info("Speaker lock OFF (restoring to " + _spkPreLockVol + "%)");
                    }
                    UpdateTrayState();
                }

                if ((what & SettingsChange.AfkMic) != 0)
                {
                    if (_audio.AfkMicEnabled)
                    {
                        _settings.MicOverlayEnabled = true;
                        _settings.Save();
                        if (_micStatus != null && !_micStatus.IsDisposed)
                        {
                            _micStatus.OverlayEnabled = true;
                            _micStatus.ShowMicOpenIdle();
                        }
                        ShowCorrectionToast("Mute Microphone When Idle \u2014 Mic will auto-mute after (" + _audio.AfkMicSec + "s) of inactivity", true);
                    }
                    else
                    {
                        DismissActiveToast();
                        // Unmute mic if it was AFK-muted
                        if (_micAfkState == AfkState.AfkMuted || _micAfkState == AfkState.FadingOut)
                        {
                            try { Audio.SetMicMute(false); } catch { }
                            _micAfkState = AfkState.Active;
                            Logger.Info("AFK mic mute disabled — mic restored.");
                        }
                        if (!_audio.PttEnabled && !_audio.PtmEnabled && !_audio.PtToggleEnabled)
                        {
                            if (_micStatus != null && !_micStatus.IsDisposed)
                                _micStatus.HideOverlay();
                        }
                    }
                    UpdateTrayState();
                }

                if ((what & SettingsChange.AfkSpeaker) != 0)
                {
                    if (_audio.AfkSpeakerEnabled)
                        ShowCorrectionToast("Mute Speakers When Idle \u2014 Audio will fade out after (" + _audio.AfkSpeakerSec + "s) of inactivity", true);
                    else if (!_audio.AfkSpeakerEnabled)
                        DismissActiveToast();
                    UpdateTrayState();
                }

                if ((what & SettingsChange.PttCosmetic) != 0)
                {
                    // Eyeball/overlay/sound/duck toggle changed — flash mic popup to show current state
                    if (_micStatus != null && !_micStatus.IsDisposed)
                    {
                        _micStatus.OverlayEnabled = true;
                        bool micMuted = false;
                        try { micMuted = Audio.GetMicMute(); } catch { }
                        if (micMuted) _micStatus.ShowMicClosed();
                        else _micStatus.ShowMicOpenIdle();
                    }
                }

                if ((what & SettingsChange.Overlay) != 0)
                {
                    if (_audio.MicOverlayEnabled)
                    {
                        if (_micStatus != null && !_micStatus.IsDisposed)
                        {
                            _micStatus.OverlayEnabled = true;
                            bool micMuted = false;
                            try { micMuted = Audio.GetMicMute(); } catch { }
                            if (micMuted) _micStatus.ShowMicClosed();
                            else _micStatus.ShowMicOpenIdle();
                        }
                    }
                    else
                    {
                        if (_micStatus != null && !_micStatus.IsDisposed)
                        {
                            _micStatus.OverlayEnabled = false;
                            _micStatus.HideOverlay();
                        }
                    }
                }

                // Startup and Notifications don't need actuation beyond what the property setter already does

                if ((what & SettingsChange.AppVolume) != 0)
                {
                    if (_settings.AppVolumeEnforceEnabled)
                        EnforceAppVolumes();
                }

                if ((what & SettingsChange.Dictation) != 0)
                {
                    if (_dictationManager != null)
                        _dictationManager.Start();

                    bool dictActive = _audio.DictationEnabled || _audio.DictationToggleEnabled;
                    if (dictActive)
                    {
                        string modeName = null;
                        string keyList = null;
                        string modeDesc = null;

                        if (_audio.DictationEnabled && _audio.DictationKey > 0)
                        {
                            modeName = "Push-to-Dictate";
                            keyList = PushToTalk.GetKeyName(_audio.DictationKey);
                            if (_audio.DictationKey2 > 0) keyList += ", " + PushToTalk.GetKeyName(_audio.DictationKey2);
                            modeDesc = "Hold (" + keyList + ") to start voice typing";
                        }
                        else if (_audio.DictationToggleEnabled && _audio.DictationToggleKey > 0)
                        {
                            modeName = "Dictation Toggle";
                            keyList = PushToTalk.GetKeyName(_audio.DictationToggleKey);
                            if (_audio.DictationToggleKey2 > 0) keyList += ", " + PushToTalk.GetKeyName(_audio.DictationToggleKey2);
                            modeDesc = "Tap (" + keyList + ") to toggle voice typing";
                        }

                        if (modeName != null && modeDesc != null)
                        {
                            int currentStateHash = (modeName + keyList + modeDesc).GetHashCode();
                            if (_lastDictToastState != currentStateHash)
                            {
                                _lastDictToastState = currentStateHash;
                                ShowCorrectionToast(modeName + " Enabled \u2014 " + modeDesc, true);
                            }
                        }
                    }
                    else
                    {
                        _lastDictToastState = 0;
                        if (_activeToast != null && !_activeToast.IsDisposed)
                            try { _activeToast.Dismiss(); } catch { }
                    }

                    // CRITICAL FIX: Restart the Hook Engine to apply the new Dictation suppression keys
                    DisablePtt();
                    EnablePtt();
                }

                if ((what & SettingsChange.VoiceActivity) != 0)
                {
                    if (_audio.VoiceActivityEnabled)
                    {
                        // Disable PTT/PTM/Toggle — mutually exclusive
                        if (_pushToTalk != null && _pushToTalk.Enabled)
                            DisablePtt();

                        // Wire up overlay/sound events (same handlers PTT uses)
                        if (_pushToTalk != null)
                        {
                            _pushToTalk.OnTalkStart -= OnPttTalkStart;
                            _pushToTalk.OnTalkStop -= OnPttTalkStop;
                            _pushToTalk.OnTalkStart += OnPttTalkStart;
                            _pushToTalk.OnTalkStop += OnPttTalkStop;
                        }

                        // Start voice monitoring
                        if (_pushToTalk != null)
                        {
                            float threshold = _audio.VoiceActivityThreshold / 100f;
                            _pushToTalk.EnableVoiceActivity(threshold, _audio.VoiceActivityHoldoverMs);
                        }

                        // Overlay
                        if (_audio.VoiceActivityShowOverlay && _micStatus != null && !_micStatus.IsDisposed)
                        {
                            _micStatus.OverlayEnabled = true;
                            _micStatus.PushToMuteMode = false;
                            _micStatus.ToggleMode = false;
                            _micStatus.ShowMicClosed();
                        }

                        // Only show toast on a real OFF→ON transition (not every settings re-eval)
                        if (!_vaWasRunning)
                        {
                            DismissActiveToast();
                            ShowCorrectionToast("Voice Activity Enabled \u2014 Mic auto-unmutes when you speak", true);
                        }
                        _vaWasRunning = true;
                    }
                    else
                    {
                        // Stop voice monitoring
                        if (_pushToTalk != null)
                        {
                            _pushToTalk.OnTalkStart -= OnPttTalkStart;
                            _pushToTalk.OnTalkStop -= OnPttTalkStop;
                            _pushToTalk.DisableVoiceActivity();
                        }

                        DismissActiveToast();
                        _vaWasRunning = false;
                        try { Audio.SetMicMute(false); } catch { }
                        if (_micStatus != null && !_micStatus.IsDisposed)
                        {
                            _micStatus.HideOverlay();
                        }
                    }
                    UpdateTrayState();
                }

                if ((what & SettingsChange.NightMode) != 0)
                {
                    // Settings saved. User must click "Apply & Restart Audio" to push to registry.
                    Logger.Info("Night Mode setting changed (saved, not applied yet).");
                }

                if ((what & SettingsChange.Equalizer) != 0)
                {
                    Logger.Info("EQ setting changed (saved, not applied yet).");
                }

                if ((what & SettingsChange.ApplyEnhancements) != 0)
                {
                    // Loudness Equalization via FxProperties + audiosrv restart
                    string leErr = Audio.ApplyLoudnessEqualization(_audio.NightModeEnabled);
                    if (leErr != null) Logger.Warn("ApplyEnhancements: Loudness EQ failed: " + leErr);
                    else Logger.Info("ApplyEnhancements: Loudness EQ " + (_audio.NightModeEnabled ? "ON" : "OFF"));

                    // EQ bands via Equalizer APO config (if installed)
                    if (Audio.IsEqualizerAPOInstalled())
                    {
                        var bandStrs = (_audio.EqBands ?? "").Split(new char[]{'|'});
                        var bands = new float[10];
                        bool valid = bandStrs.Length == 10;
                        for (int i = 0; i < 10 && valid; i++)
                        {
                            float v;
                            valid = float.TryParse(bandStrs[i].Trim(),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out v);
                            if (valid) bands[i] = Math.Max(0f, Math.Min(1f, v));
                        }
                        if (valid)
                        {
                            string apoErr = Audio.WriteEqualizerAPOConfig(bands);
                            if (apoErr != null) Logger.Warn("ApplyEnhancements: APO write failed: " + apoErr);
                            else Logger.Info("ApplyEnhancements: Equalizer APO config written.");
                        }
                    }
                }

                if ((what & SettingsChange.Dictation) != 0)
                {
                    // When BOTH dictation toggles are OFF, stop dictation manager → dismisses Windows speech popup
                    bool anyDictOn = _settings.DictationEnabled || _settings.DictationToggleEnabled;
                    if (!anyDictOn && _dictationManager != null)
                    {
                        _dictationManager.Stop();
                        Logger.Info("Dictation toggles all OFF — stopped DictationManager (dismissed speech popup).");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("OnAudioSettingsChanged failed for " + what, ex);
            }
            finally { _audioSettingsProcessing = false; }
        }


        private void UpdatePauseMenuItem()
        {
            if (_pauseMenuItem == null) return;
            if (_isPaused)
            {
                _pauseMenuItem.Text = "\u23F8  Paused";
                _pauseMenuItem.ForeColor = Color.FromArgb(220, 80, 80);
            }
            else
            {
                _pauseMenuItem.Text = "\u23F8  Pause";
                _pauseMenuItem.ForeColor = DarkTheme.TextLight;
            }
        }

        private void TogglePause()
        {
            _isPaused = !_isPaused;
            UpdatePauseMenuItem();

            if (_isPaused)
            {
                // Release everything — mic must be fully free when paused
                if (_pushToTalk != null && _pushToTalk.Enabled)
                    DisablePtt();
                Audio.SetMicMute(false);
                ShowCorrectionToast("\u23F8  Angry Audio Paused \u2014 All Protection Disabled");
            }
            else
            {
                // Resuming — re-enable PTT if it was configured, enforce volumes
                if (_settings.PushToTalkEnabled || _settings.PushToMuteEnabled || _settings.PushToToggleEnabled)
                {
                    EnablePtt();
                    if (_settings.PushToTalkEnabled || _settings.PushToToggleEnabled)
                    {
                        // Dictation God Mode: don't mute if dictation is actively recording
                        if (DictationManager.Current == null || !DictationManager.Current.IsActive)
                            Audio.SetMicMute(true);
                    }
                }
                _lastMicEnforce = DateTime.MinValue;
                _lastSpeakerEnforce = DateTime.MinValue;
                if (_settings.MicEnforceEnabled && _micPreLockVol < 0) { try { _micPreLockVol = (int)Audio.GetMicVolume(); } catch { } }
                if (_settings.SpeakerEnforceEnabled && _spkPreLockVol < 0) { try { _spkPreLockVol = (int)Audio.GetSpeakerVolume(); } catch { } }
                EnforceMic(true);
                EnforceSpeaker(true);
                ShowCorrectionToast("\u25B6  Angry Audio Resumed \u2014 Protection Active");
            }

            UpdateTrayState();
            Logger.Info(_isPaused ? "Angry Audio paused." : "Angry Audio resumed.");
        }

        private void ViewLog()
        {
            ShowLogViewer();
        }

        private void ShowLogViewer()
        {
            try
            {
                string logPath = Logger.GetLogFilePath();
                if (logPath != null && System.IO.File.Exists(logPath))
                {
                    System.Diagnostics.Process.Start("notepad.exe", "\"" + logPath + "\"");
                }
                else
                {
                    ShowBalloon("Log File Not Found", "No log file exists yet for this session.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open log file.", ex);
            }
        }

        internal void CycleDisplay(bool forward)
        {
            if (!_settings.DisplayEnabled) _settings.DisplayEnabled = true;

            // Determine which monitor the mouse cursor is currently on
            string selectedDev = null;
            try { selectedDev = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).DeviceName; } catch { }

            // Use DisplayManager's unified logic to find where we currently are in the cycle
            bool blueLightOn = (_settings.DisplayTempK == 3800 && _settings.DisplayPreset == "");
            int cur = DisplayManager.GetCurrentCycleIdx(_settings.DisplayPreset, blueLightOn, _settings.DisplayFilterType);

            // Infinite loop using modulo
            int n = DisplayManager.CycleItemNames.Length;
            int next = forward ? (cur + 1) % n : ((cur - 1 + n) % n);

            Logger.Info("CycleDisplay: fwd=" + forward + " preset='" + (_settings.DisplayPreset ?? "") +
                "' ft=" + _settings.DisplayFilterType + " tempK=" + _settings.DisplayTempK +
                " cur=" + cur + "(" + DisplayManager.CycleItemNames[cur] + ") → next=" + next + "(" + DisplayManager.CycleItemNames[next] + ")");

            // Apply the next item
            string appliedName = string.IsNullOrEmpty(selectedDev) 
                ? DisplayManager.ApplyCycleItem(next) 
                : DisplayManager.ApplyCycleItem(next, selectedDev);

            string sub = DisplayManager.CycleItemSubs[next];

            // Persist the new state cleanly using type-based lookup
            int tK = 6500; int br = 100; int ft = -1; string preset = "";
            int itemType = DisplayManager.CycleItemType[next];
            if (itemType == -1) {
                // Preset
                preset = DisplayManager.CycleItemNames[next];
                int ii; int cb;
                DisplayManager.GetPresetValues(preset, out tK, out br, out ft, out ii, out cb);
            } else if (itemType == -2) {
                // Blue Light
                tK = 3800; br = 85; ft = -1; preset = "";
            } else {
                // Color filter
                tK = 6500; br = 100; ft = itemType; preset = "";
            }

            // Always update global settings — these drive GetCurrentCycleIdx
            _settings.DisplayTempK = tK; 
            _settings.DisplayBrightness = br;
            _settings.DisplayFilterType = ft; 
            _settings.DisplayPreset = preset;

            // Also update per-monitor override if we know which monitor
            if (!string.IsNullOrEmpty(selectedDev)) {
                var parts = new System.Collections.Generic.List<string>();
                string raw = _settings.DisplayMonitorSettings ?? "";
                bool found = false;
                foreach (string part in raw.Split('|')) {
                    string p = part.Trim();
                    if (p.Length == 0) continue;
                    int c1 = p.IndexOf(':');
                    bool match = c1 > 0 && string.Equals(p.Substring(0, c1), selectedDev, StringComparison.OrdinalIgnoreCase);
                    if (match) { parts.Add(selectedDev + ":" + tK + ":" + br); found = true; }
                    else parts.Add(p);
                }
                if (!found) parts.Add(selectedDev + ":" + tK + ":" + br);
                _settings.DisplayMonitorSettings = string.Join("|", parts.ToArray());
            }

            _settings.Save();
            
            if (_openOptionsForm != null && !_openOptionsForm.IsDisposed)
                try { _openOptionsForm.RefreshDisplayUI(); } catch {}

            try { Toast.Show(DisplayManager.CycleItemNames[next], sub, ToastAccent.Blue, 1500); } catch {}
        }

        private void ExitApplication()
        {
            Logger.Info("User requested exit.");
            // Unregister global hotkeys
            if (_hotkeyWnd != null) {
                UnregisterHotKey(_hotkeyWnd.Handle, HOTKEY_DISPLAY);
                UnregisterHotKey(_hotkeyWnd.Handle, HOTKEY_CYCLE_NEXT);
                UnregisterHotKey(_hotkeyWnd.Handle, HOTKEY_CYCLE_PREV);
                UnregisterHotKey(_hotkeyWnd.Handle, HOTKEY_INPUT);
                UnregisterHotKey(_hotkeyWnd.Handle, HOTKEY_OUTPUT);
                UnregisterHotKey(_hotkeyWnd.Handle, HOTKEY_DICT);
                UnregisterHotKey(_hotkeyWnd.Handle, HOTKEY_AFK);
                _hotkeyWnd.DestroyHandle();
                _hotkeyWnd = null;
            }
            // Restore original display gamma before exiting
            DisplayManager.ResetToNormal();
            Dispose();
            Application.Exit();
        }

        /// <summary>Hidden NativeWindow that receives WM_HOTKEY messages for Alt+R / Alt+I / Alt+O.</summary>
        private class HotkeyWindow : NativeWindow
        {
            private const int WM_HOTKEY = 0x0312;
            private readonly TrayApp _owner;
            public HotkeyWindow(TrayApp owner) {
                _owner = owner;
                CreateHandle(new CreateParams());
            }
            protected override void WndProc(ref Message m) {
                if (m.Msg == WM_HOTKEY) {
                    int id = m.WParam.ToInt32();
                    if (id == HOTKEY_DISPLAY) {
                        // Alt+D: jump to Display tab
                        _owner.ShowOptions(2);
                        if (_owner._openOptionsForm != null)
                            _owner._openOptionsForm.TopMost = true;

                    } else if (id == HOTKEY_CYCLE_NEXT || id == HOTKEY_CYCLE_PREV) {
                        bool fwd = (id == HOTKEY_CYCLE_NEXT);
                        _owner.CycleDisplay(fwd);

                    } else if (id == HOTKEY_INPUT) {
                        // Alt+I: jump to Input tab
                        _owner.ShowOptions(0);
                        if (_owner._openOptionsForm != null)
                            _owner._openOptionsForm.TopMost = true;

                    } else if (id == HOTKEY_OUTPUT) {
                        // Alt+O: jump to Output tab
                        _owner.ShowOptions(1);
                        if (_owner._openOptionsForm != null)
                            _owner._openOptionsForm.TopMost = true;

                    } else if (id == HOTKEY_DICT) {
                        // Alt+[: jump to Dictation tab (pane index 3)
                        _owner.ShowOptions(3);
                        if (_owner._openOptionsForm != null)
                            _owner._openOptionsForm.TopMost = true;

                    } else if (id == HOTKEY_AFK) {
                        // Alt+]: jump to AFK Protection tab (pane index 4)
                        _owner.ShowOptions(4);
                        if (_owner._openOptionsForm != null)
                            _owner._openOptionsForm.TopMost = true;
                    }
                }
                base.WndProc(ref m);
            }
        }



        // --- Welcome Dialog ---



        // --- Correction Toast ---

        // ── Dictation status + preview ───────────────────────────────────────

        // ── Dictation preview overlay ────────────────────────────────────────
        private DictationPreview _dictPreview;

        DictationPreview GetDictPreview()
        {
            if (_dictPreview == null || _dictPreview.IsDisposed)
                _dictPreview = new DictationPreview();
            return _dictPreview;
        }

        private void OnDictationStatus(string status)
        {
            if (!_settings.DictShowOverlay) return;
            try
            {
                Action act = () =>
                {
                    try
                    {
                        if (status == "Listening...")
                        {
                            // Hide mic status pill while dictation is active
                            if (_micStatus != null && !_micStatus.IsDisposed) _micStatus.HideOverlay();
                            GetDictPreview().ShowListening();
                        }
                        else if (status == "Processing...")
                        {
                            GetDictPreview().ShowProcessing();
                        }
                        else if (status == "Ready")
                        {
                            GetDictPreview().HideNow();
                            // Restore mic status pill to correct state
                            if (_micStatus != null && !_micStatus.IsDisposed)
                            {
                                if (_micStatus.IsMicOpen) _micStatus.ShowMicOpenIdle();
                                else _micStatus.ShowMicClosed();
                            }
                        }
                    }
                    catch (Exception ex) { Logger.Error("DictationPreview: show failed", ex); }
                };
                InvokeOnUiThread(act);
            }
            catch (Exception ex) { Logger.Error("DictationPreview: OnDictationStatus failed", ex); }
        }

        private void OnDictationTextReady(string text)
        {
            try
            {
                Action act = () =>
                {
                    try
                    {
                        if (_settings.DictShowOverlay)
                            GetDictPreview().ShowResult(text);
                    }
                    catch (Exception ex) { Logger.Error("DictationPreview: ShowResult failed", ex); }
                };
                InvokeOnUiThread(act);
            }
            catch (Exception ex) { Logger.Error("DictationPreview: OnDictationTextReady failed", ex); }
        }

        void InvokeOnUiThread(Action act)
        {
            if (_micStatus != null && !_micStatus.IsDisposed && _micStatus.InvokeRequired)
                _micStatus.BeginInvoke(act);
            else
                act();
        }


        private CorrectionToast _activeToast;
        private DateTime _lastToastShown = DateTime.MinValue;
        private const int ToastCooldownSec = 3; // Responsive but no spam

        private void ShowCorrectionToast(string message)
        {
            ShowCorrectionToast(message, false, false, false);
        }

        private void ShowCorrectionToast(string message, bool force)
        {
            ShowCorrectionToast(message, force, false, false);
        }

        private void ShowCorrectionToast(string message, bool force, bool showMicBtn, bool showSpkBtn)
        {
            RecordNotif(message);

            // Throttle (skip if forced — e.g. user just toggled a lock)
            if (!force && (DateTime.Now - _lastToastShown).TotalSeconds < ToastCooldownSec) return;
            _lastToastShown = DateTime.Now;

            // Must create forms on the UI thread
            Action showAction = () =>
            {
                try
                {
                    // Kill ALL existing toasts before showing new one
                    ToastStack.DismissAllExcept(null);

                    _activeToast = new CorrectionToast(message, showMicBtn, showSpkBtn);
                    _activeToast.FormClosed += (s, e) =>
                    {
                        var toast = (CorrectionToast)s;
                        if (toast.MicToggled)
                        {
                            _settings.MicEnforceEnabled = false;
                            _settings.Save();
                            Logger.Info("User disabled mic enforcement via toast.");
                            UpdateTrayState();
                        }
                        if (toast.SpkToggled)
                        {
                            _settings.SpeakerEnforceEnabled = false;
                            _settings.Save();
                            Logger.Info("User disabled speaker enforcement via toast.");
                            UpdateTrayState();
                        }
                        _activeToast = null;
                    };
                    _activeToast.ShowNoFocus();
                }
                catch (Exception ex) { Logger.Error("Toast show failed.", ex); }
            };

            try { PopupThread.Invoke(showAction); }
            catch (Exception ex) { Logger.Error("BeginInvoke failed for toast.", ex); }
        }

        private void DismissActiveToast()
        {
            if (_activeToast == null || _activeToast.IsDisposed) return;
            Action dismissAction = () => {
                try { if (_activeToast != null && !_activeToast.IsDisposed) _activeToast.Dismiss(); } catch { }
            };
            try { PopupThread.Invoke(dismissAction); } catch { }
        }

        /// <summary>Show a toggle status toast with green mic-open or red mic-closed icon and matching colors.</summary>
        private void ShowToggleStatusToast(string message, int micStatusMode)
        {
            RecordNotif(message);
            _lastToastShown = DateTime.Now;
            Action showAction = () =>
            {
                try
                {
                    ToastStack.DismissAllExcept(null);
                    _activeToast = new CorrectionToast(message, false, false) { MicStatusMode = micStatusMode };
                    _activeToast.FormClosed += (s, e) => { _activeToast = null; };
                    _activeToast.ShowNoFocus();
                }
                catch (Exception ex) { Logger.Error("Toggle status toast failed.", ex); }
            };
            try { PopupThread.Invoke(showAction); } catch { }
        }

        // --- Mic Unprotected Warning ---

        private MicWarningToast _micWarningToast;
        private System.Windows.Forms.Timer _micWarningPollTimer;
        private bool _micWarningDismissed; // true after user dismisses — reset when any mic protection turns on
        private bool _micWasProtected = true; // tracks previous state to detect transitions

        /// <summary>Mic is unprotected when no mic muting/control features are active.
        /// Volume lock doesn't count — it protects volume level, not mic open/close state.
        /// Speaker toggles don't count either.</summary>
        private bool IsMicFullyUnprotected()
        {
            return !_settings.AfkMicMuteEnabled &&
                   !_settings.PushToTalkEnabled &&
                   !_settings.PushToMuteEnabled &&
                   !_settings.PushToToggleEnabled &&
                   !_settings.VoiceActivityEnabled;
        }

        /// <summary>Start the mic warning poll timer. Called once at startup.
        /// Every 500ms, checks if mic is unprotected and shows/hides warning accordingly.
        /// Waits for splash screen and wizard to close before first show.
        /// Only shows once per unprotected session — user dismiss is respected.</summary>
        private void StartMicWarningPoller()
        {
            if (_micWarningPollTimer != null) return;
            string _lastPollerLogReason = null;
            _micWarningPollTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _micWarningPollTimer.Tick += (s, e) => {
                try {
                    bool unprotected = IsMicFullyUnprotected();
                    bool warningShowing = _micWarningToast != null && !_micWarningToast.IsDisposed;

                    // When protection turns ON, reset the dismiss flag so warning can fire next time
                    if (!unprotected && _micWasProtected == false) {
                        _micWarningDismissed = false;
                    }
                    _micWasProtected = !unprotected;

                    // Don't show while splash or wizard is up
                    bool splashUp = _activeSplash != null && !_activeSplash.IsDisposed;
                    bool wizardUp = false;

                    if (unprotected && !warningShowing && !splashUp && !wizardUp && !_micWarningDismissed) {
                        _lastPollerLogReason = null;
                        ShowMicWarning();
                    } else if (!unprotected && warningShowing) {
                        _lastPollerLogReason = null;
                        HideMicWarning();
                    } else if (unprotected && !warningShowing) {
                        // Log why we're NOT showing — only once per reason, not every tick
                        string reason = splashUp ? "waiting for splash" : wizardUp ? "waiting for wizard" : _micWarningDismissed ? "already dismissed by user" : null;
                        if (reason != null && reason != _lastPollerLogReason) {
                            Logger.Info("MicWarningPoller: " + reason);
                            _lastPollerLogReason = reason;
                        }
                    }
                } catch { }
            };
            _micWarningPollTimer.Start();
        }

        private void ShowMicWarning()
        {
            try {
                if (_micWarningToast != null && !_micWarningToast.IsDisposed)
                    try { _micWarningToast.Close(); } catch { }

                // Kill other popups so red warning stands alone
                ToastStack.DismissAllExcept(typeof(MicWarningToast));

                _micWarningToast = new MicWarningToast();
                _micWarningToast.FormClosed += (s2, e2) => {
                    bool wantsSettings = ((MicWarningToast)s2).OpenSettings;
                    _micWarningToast = null;
                    _micWarningDismissed = true; // User dismissed — don't re-show until protection cycles
                    if (wantsSettings) {
                        if (_contextMenu != null && _contextMenu.IsHandleCreated)
                            try { _contextMenu.BeginInvoke((Action)(() => ShowOptions(0))); } catch { ShowOptions(0); }
                        else ShowOptions(0);
                    }
                };
                _micWarningToast.ShowNoFocus();
                Logger.Info("Mic warning shown — all mic protections are off.");
            } catch (Exception ex) { Logger.Error("ShowMicWarning failed.", ex); }
        }

        private void HideMicWarning()
        {
            if (_micWarningToast == null || _micWarningToast.IsDisposed) return;
            try { _micWarningToast.Close(); } catch { }
            _micWarningToast = null;
            Logger.Info("Mic warning dismissed — mic protection enabled.");
        }


        // --- Notifications ---

        private static readonly System.Collections.Generic.List<string> _notifHistory = new System.Collections.Generic.List<string>();
        private static readonly object _notifLock = new object();
        private const int MaxNotifHistory = 50;

        internal static System.Collections.Generic.List<string> GetNotifHistory()
        {
            lock (_notifLock) { return new System.Collections.Generic.List<string>(_notifHistory); }
        }

        private void RecordNotif(string message)
        {
            lock (_notifLock)
            {
                _notifHistory.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
                if (_notifHistory.Count > MaxNotifHistory) _notifHistory.RemoveAt(0);
            }
        }

        // --- Info Toasts ---

        private InfoToast _activeInfoToast;

        private void ShowBalloon(string message)
        {
            ShowBalloon("Angry Audio", message);
        }

        private void ShowBalloon(string title, string message)
        {
            ShowBalloonWithAction(title, message, null, null);
        }

        private void ShowBalloonWithAction(string title, string message, string btn1Text, Action onBtn1, string btn2Text = null, Action onBtn2 = null)
        {
            RecordNotif(message);
            try
            {
                Action showAction = () =>
                {
                    try
                    {
                        // Kill ALL existing toasts — not just the tracked reference
                        ToastStack.DismissAllExcept(null);

                        _activeInfoToast = new InfoToast(title, message, btn1Text, btn2Text);
                        if (onBtn1 != null)
                            _activeInfoToast.Btn1Clicked += (s2, e2) => { try { onBtn1(); } catch { } };
                        if (onBtn2 != null)
                            _activeInfoToast.Btn2Clicked += (s2, e2) => { try { onBtn2(); } catch { } };
                        _activeInfoToast.FormClosed += (s, e) => { _activeInfoToast = null; };
                        _activeInfoToast.ShowNoFocus();
                    }
                    catch (Exception ex) { Logger.Error("InfoToast show failed.", ex); }
                };
                try { PopupThread.Invoke(showAction); } catch { Logger.Warn("BeginInvoke failed for InfoToast — skipping."); }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to show notification.", ex);
            }
        }

        private bool CanNotify(ref DateTime lastNotify)
        {
            if ((DateTime.UtcNow - lastNotify) >= NotifyThrottle)
            {
                lastNotify = DateTime.UtcNow;
                return true;
            }
            return false;
        }

        // --- Kill Signal ---

        private void WaitForKillSignal()
        {
            try
            {
                _killEvent.WaitOne();
                Logger.Info("Kill signal received.");

                // Use multiple fallback strategies to ensure we actually exit
                try
                {
                    if (_contextMenu != null && _contextMenu.IsHandleCreated)
                    {
                        _contextMenu.BeginInvoke((Action)ExitApplication);
                    }
                    else
                    {
                        // Fallback — force exit directly
                        Dispose();
                        Environment.Exit(0);
                    }
                }
                catch
                {
                    // Nuclear fallback
                    try { Dispose(); } catch { }
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error in kill signal watcher.", ex);
            }
        }

        // --- Dispose ---

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // CRITICAL: always reset display effects and unmute mic before exiting — covers crash paths,
            // Environment.Exit(), and normal exit. Must be first, before any timer stops.
            try { Audio.SetMicMute(false); } catch { }
            try { DisplayManager.ResetToNormal(); } catch { }
            try { DisplayManager.Cleanup(); } catch { }

            if (_pollTimer != null) _pollTimer.Dispose();
            if (_pttSafetyTimer != null) _pttSafetyTimer.Dispose();
            if (_killEvent != null) _killEvent.Dispose();
            if (_pushToTalk != null) _pushToTalk.Dispose();
            if (_dictationManager != null) _dictationManager.Dispose();
            if (_dictPreview != null && !_dictPreview.IsDisposed) _dictPreview.Dispose();
            if (_restoreMicTimer != null) _restoreMicTimer.Dispose();
            if (_restoreSpkTimer != null) _restoreSpkTimer.Dispose();

            
            if (_openOptionsForm != null && !_openOptionsForm.IsDisposed)
                try { _openOptionsForm.Close(); _openOptionsForm.Dispose(); } catch { }
            _openOptionsForm = null;

            if (_micStatus != null && !_micStatus.IsDisposed)
                _micStatus.Dispose();

            if (_fadeOverlay != null && !_fadeOverlay.IsDisposed)
                _fadeOverlay.Dispose();

            if (_activeToast != null && !_activeToast.IsDisposed)
                try { _activeToast.Close(); _activeToast.Dispose(); } catch { }
            if (_activeInfoToast != null && !_activeInfoToast.IsDisposed)
                try { _activeInfoToast.Close(); _activeInfoToast.Dispose(); } catch { }
            if (_micWarningToast != null && !_micWarningToast.IsDisposed)
                try { _micWarningToast.Close(); _micWarningToast.Dispose(); } catch { }
            if (_micWarningPollTimer != null)
                try { _micWarningPollTimer.Stop(); _micWarningPollTimer.Dispose(); } catch { }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            if (_contextMenu != null) _contextMenu.Dispose();
            if (_baseIcon != null) _baseIcon.Dispose();
            if (_pausedIcon != null) _pausedIcon.Dispose();
            if (_afkIcon != null) _afkIcon.Dispose();
            if (_errorIcon != null) _errorIcon.Dispose();
            if (_pttIcon != null) _pttIcon.Dispose();
            if (_micHotIcon != null) _micHotIcon.Dispose();
        }

        // ── Equalizer APO Popup Suppression ─────────────────────────────
        // Disables APO's own scheduled update checker and kills any stray
        // Configurator process so the user never sees an APO popup.
        // Also silently re-registers APO on all render devices to keep
        // everything up to date without any user interaction.
        static void SuppressAPOPopup()
        {
            try
            {
                // 1. Disable APO's scheduled update checker task
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = "/change /tn \"EqualizerAPOUpdateChecker\" /disable",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var proc = System.Diagnostics.Process.Start(psi))
                    {
                        proc.WaitForExit(5000);
                        Logger.Info("APO: Disabled EqualizerAPOUpdateChecker task (exit=" + proc.ExitCode + ")");
                    }
                }
                catch (Exception ex) { Logger.Debug("APO: schtasks disable failed: " + ex.Message); }

                // 2. Kill any running Configurator.exe (APO's UI tool)
                try
                {
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName("Configurator"))
                        try { p.Kill(); Logger.Info("APO: Killed running Configurator.exe"); } catch { }
                }
                catch { }

                // 3. Silently re-register APO on all active render devices
                try
                {
                    RegisterAPOOnAllDevices();
                    Logger.Info("APO: Silently re-registered on all render devices.");
                }
                catch (Exception ex) { Logger.Debug("APO: Device re-registration failed: " + ex.Message); }
            }
            catch (Exception ex)
            {
                Logger.Debug("SuppressAPOPopup failed: " + ex.Message);
            }
        }

        static void RegisterAPOOnAllDevices()
        {
            const string APO_CLSID = "{E4B8D70A-CB30-4AE6-8596-E2DCFC84FCBC}";
            const string RENDER_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
            string[] LFX_GFX_VALS = new string[] {
                "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},1",
                "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},2",
                "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5",
                "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6",
            };

            string apoDir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
                "EqualizerAPO");
            if (!System.IO.Directory.Exists(apoDir)) return;

            using (var renderKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RENDER_KEY, false))
            {
                if (renderKey == null) return;
                foreach (string deviceGuid in renderKey.GetSubKeyNames())
                {
                    try
                    {
                        using (var devKey = renderKey.OpenSubKey(deviceGuid, false))
                        {
                            if (devKey == null) continue;
                            object stateObj = devKey.GetValue("DeviceState");
                            if (stateObj == null) continue;
                            int state = 0;
                            try { state = Convert.ToInt32(stateObj); } catch { continue; }
                            if (state != 1) continue;
                        }

                        string fxPath = RENDER_KEY + @"\" + deviceGuid + @"\FxProperties";
                        using (var fxKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(fxPath, true))
                        {
                            if (fxKey == null) continue;
                            foreach (string valName in LFX_GFX_VALS)
                                fxKey.SetValue(valName, APO_CLSID, Microsoft.Win32.RegistryValueKind.String);
                        }
                    }
                    catch { }
                }
            }
        }
    }

    // --- Dark Context Menu Renderer ---

    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var rect = new Rectangle(Point.Empty, e.Item.Size);
            Color color = e.Item.Selected ? DarkTheme.BtnHover : DarkTheme.BgDark;
            using (var brush = new SolidBrush(color))
                e.Graphics.FillRectangle(brush, rect);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(DarkTheme.BgDark))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(DarkTheme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(DarkTheme.Separator))
                e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Respect per-item ForeColor if it was explicitly set (not default)
            if (e.Item.ForeColor != Control.DefaultForeColor && e.Item.ForeColor != Color.Empty)
                e.TextColor = e.Item.Selected ? BrightenColor(e.Item.ForeColor, 40) : e.Item.ForeColor;
            else
                e.TextColor = e.Item.Selected ? Color.FromArgb(255, 255, 255) : DarkTheme.TextLight;
            base.OnRenderItemText(e);
        }

        private static Color BrightenColor(Color c, int amount)
        {
            return Color.FromArgb(c.A,
                Math.Min(255, c.R + amount),
                Math.Min(255, c.G + amount),
                Math.Min(255, c.B + amount));
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(DarkTheme.BgDark))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Draw a simple checkmark in accent color
            var rect = e.ImageRectangle;
            using (var brush = new SolidBrush(DarkTheme.BgDark))
                e.Graphics.FillRectangle(brush, rect);
            using (var font = new Font("Segoe UI", 9f))
            using (var brush = new SolidBrush(DarkTheme.Accent))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString("\u2713", font, brush, rect, sf);
            }
        }
    }
}
