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
        private bool _skipSplash;
        private EventWaitHandle _killEvent;

        // Enforcement timing
        private DateTime _lastMicEnforce = DateTime.MinValue;
        private DateTime _lastSpeakerEnforce = DateTime.MinValue;

        // AFK state per device
        private AfkState _micAfkState = AfkState.Active;
        private AfkState _speakerAfkState = AfkState.Active;

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
        private WelcomeForm _openWelcomeForm;
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
        private MicStatusOverlay _micStatus;

        public TrayApp(bool openSettings, bool startPaused)
        {
            _openSettingsOnStart = openSettings;
            _isPaused = startPaused;

            // Load settings
            _settings = Settings.Load();
            _audio = new AudioSettings(_settings);
            _audio.Changed += OnAudioSettingsChanged;
            _audio.CaptureStateChanged += (capturing) => { if (_pushToTalk != null) _pushToTalk.SuspendHook = capturing; };

            // Build icons and tray icon FIRST — user needs to see we're alive
            BuildIcons();
            BuildTrayIcon();

            // Initialize push-to-talk and mic overlay BEFORE welcome dialog
            // so toggle callbacks work during first-run wizard
            _pushToTalk = new PushToTalk();
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
                        _micStatus.UseExtendedDelay();
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
                _micStatus.UseExtendedDelay(); // first show after app launch = 2s
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
                // FORCE mic lock on for first run — non-negotiable.
                // Covers: fresh install (no file), leftover settings file from previous test,
                // and any path where the wizard closes before page 2.
                _settings.MicEnforceEnabled = true;
                _settings.Save();
                ShowWelcomeDialog();
            }

            // Show privacy splash on every startup (unless wizard is open or was closed by opening Options)
            if (!_skipSplash && _openWelcomeForm == null)
            {
                // Red warning fires AFTER splash closes so they don't overlap
                if (_settings.FirstRunComplete)
                    ShowSplash(() => CheckMicUnprotected());
                else
                    ShowSplash();
            }
            else if (_settings.FirstRunComplete)
            {
                // No splash (wizard opened options) — check immediately
                CheckMicUnprotected();
            }

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
                        foreach (var s in listeners) names.Add(s.ProcessName);
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
                                    ShowCorrectionToast((_lastOpenMicApp ?? "App") + " Released Your Mic \u2014 AFK Protection Re-Enabled");
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
                        string names = string.Join(", ", _micListenerNames.ToArray());
                        if (names.Length > 45) names = names.Substring(0, 42) + "...";
                        _micListenerMenuItem.Text = "\ud83c\udfa4 Mic: " + names;
                        _micListenerMenuItem.ForeColor = Color.FromArgb(255, 180, 100);
                    } else {
                        _micListenerMenuItem.Text = "\ud83c\udfa4 Mic: No apps listening";
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

            _pauseMenuItem = new ToolStripMenuItem("\u23F8  Pause Angry Audio");
            _pauseMenuItem.Click += (s, e) => TogglePause();
            UpdatePauseMenuItem();
            _contextMenu.Items.Add(_pauseMenuItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var viewLogItem = new ToolStripMenuItem("\ud83d\udcdd  View Log");
            viewLogItem.Click += (s, e) => ViewLog();
            _contextMenu.Items.Add(viewLogItem);

            var resetWizardItem = new ToolStripMenuItem("\u2728  Run Setup Wizard");
            resetWizardItem.Click += (s, e) => RunSetupWizard();
            _contextMenu.Items.Add(resetWizardItem);

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

            // Force context menu handle creation NOW — thread pool timer callbacks
            // use _contextMenu.BeginInvoke() to marshal UI work to the main thread.
            // Without this, IsHandleCreated is false until the user right-clicks the tray,
            // and any toast/balloon triggered before that runs showAction() directly on
            // the thread pool thread (creating a Form without a message loop = crash).
            if (!_contextMenu.IsHandleCreated)
            {
                var forceHandle = _contextMenu.Handle;
            }

            UpdateTrayState();
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
            return _settings.PushToTalkEnabled || _settings.PushToMuteEnabled || _settings.PushToToggleEnabled;
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
            _pushToTalk.EnableMultiMode(pttKey, ptmKey, toggleKey, _settings.PushToTalkConsumeKey, _settings.PushToTalkKey2, _settings.PushToTalkKey3, _settings.PushToMuteKey2, _settings.PushToMuteKey3, _settings.PushToToggleKey2, _settings.PushToToggleKey3);
            // Explicitly sync system mic state to match chosen mode
            try {
                if (_settings.PushToTalkEnabled || _settings.PushToToggleEnabled) {
                    Audio.SetMicMute(true);
                } else if (_settings.PushToMuteEnabled) {
                    Audio.SetMicMute(false);
                }
            } catch { }
            // Show correct overlay state
            if (_micStatus != null && !_micStatus.IsDisposed)
            {
                _micStatus.UseExtendedDelay();
                bool hasPtt = pttKey > 0 || _settings.PushToTalkKey2 > 0 || _settings.PushToTalkKey3 > 0;
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
                if (triggeredKey == _settings.PushToTalkKey3 && _settings.PushToTalkKey3 > 0) return _settings.PttKey3ShowOverlay;
                return _settings.PttKey1ShowOverlay;
            }
            if (mode == 1) return _settings.PtmShowOverlay;
            if (mode == 2) return _settings.PtToggleShowOverlay;
            return true;
        }

        private void OnPttTalkStart()
        {
            // Sound feedback — check the specific mode's setting
            bool shouldPlaySound = false;
            if (_pushToTalk != null) {
                if (_pushToTalk.VoiceMonitoringActive) shouldPlaySound = _settings.VoiceActivitySoundFeedback;
                else {
                    int mode = _pushToTalk.ActiveMode;
                    if (mode == 0) shouldPlaySound = _settings.PttSoundFeedback;
                    else if (mode == 1) shouldPlaySound = _settings.PtmSoundFeedback;
                    else if (mode == 2) shouldPlaySound = _settings.PtToggleSoundFeedback;
                }
            }
            if (shouldPlaySound) PlayFeedbackSound(true);

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
                    if (ShouldShowOverlayForKey())
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
            if (_pushToTalk != null) {
                if (_pushToTalk.VoiceMonitoringActive) shouldPlaySound = _settings.VoiceActivitySoundFeedback;
                else {
                    int mode = _pushToTalk.ActiveMode;
                    if (mode == 0) shouldPlaySound = _settings.PttSoundFeedback;
                    else if (mode == 1) shouldPlaySound = _settings.PtmSoundFeedback;
                    else if (mode == 2) shouldPlaySound = _settings.PtToggleSoundFeedback;
                }
            }
            if (shouldPlaySound) PlayFeedbackSound(false);

            UpdateTrayState();
            try
            {
                if (_micStatus != null && !_micStatus.IsDisposed)
                {
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
        /// <summary>Play audio feedback sound based on user's selected type.</summary>
        private void PlayFeedbackSound(bool micOpen)
        {
            int type = _settings.SoundFeedbackType;
            string customPath = _settings.CustomSoundPath;
            System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                try {
                    if (type == 6 && !string.IsNullOrEmpty(customPath) && System.IO.File.Exists(customPath)) {
                        // Custom sound file — use SoundPlayer for WAV
                        try { using (var player = new System.Media.SoundPlayer(customPath)) player.Play(); } catch { Console.Beep(800, 15); }
                        return;
                    }
                    switch (type) {
                        case 0: Console.Beep(micOpen ? 800 : 600, 15); break;
                        case 1: Console.Beep(micOpen ? 700 : 500, 10); System.Threading.Thread.Sleep(30); Console.Beep(micOpen ? 900 : 600, 10); break;
                        case 2: Console.Beep(micOpen ? 600 : 900, 8); Console.Beep(micOpen ? 900 : 600, 12); break;
                        case 3: Console.Beep(micOpen ? 1000 : 700, 20); break;
                        case 4: Console.Beep(micOpen ? 523 : 659, 25); System.Threading.Thread.Sleep(20); Console.Beep(micOpen ? 659 : 523, 25); break;
                        case 5: Console.Beep(micOpen ? 500 : 400, 8); break;
                        default: Console.Beep(micOpen ? 800 : 600, 15); break;
                    }
                } catch { }
            });
        }

        /// <summary>Play a preview of a feedback sound type (for Options page).</summary>
        public static void PreviewFeedbackSound(int type, string customPath = null)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                try {
                    if (type == 6 && !string.IsNullOrEmpty(customPath) && System.IO.File.Exists(customPath)) {
                        try { using (var player = new System.Media.SoundPlayer(customPath)) player.Play(); } catch { Console.Beep(800, 15); }
                        return;
                    }
                    switch (type) {
                        case 0: Console.Beep(800, 15); break;
                        case 1: Console.Beep(700, 10); System.Threading.Thread.Sleep(30); Console.Beep(900, 10); break;
                        case 2: Console.Beep(600, 8); Console.Beep(900, 12); break;
                        case 3: Console.Beep(1000, 20); break;
                        case 4: Console.Beep(523, 25); System.Threading.Thread.Sleep(20); Console.Beep(659, 25); break;
                        case 5: Console.Beep(500, 8); break;
                        default: Console.Beep(800, 15); break;
                    }
                } catch { }
            });
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
                var splash = new SplashForm(_settings);
                _activeSplash = splash;
                if (afterClose != null)
                    splash.FormClosed += (s, e) => afterClose();
                splash.FormClosed += (s, e) => { if (_activeSplash == splash) _activeSplash = null; };
                splash.Show();
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
            try { if (_activeSplash != null && !_activeSplash.IsDisposed) { _activeSplash.Close(); } } catch { }
            _activeSplash = null;
        }

        // --- Menu Actions ---

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

                // Close any open WelcomeForm first
                try { if (_openWelcomeForm != null && !_openWelcomeForm.IsDisposed) { _skipSplash = true; _openWelcomeForm.Close(); } } catch { }

                if (_openOptionsForm != null && !_openOptionsForm.IsDisposed)
                {
                    if (paneIndex >= 0) _openOptionsForm.NavigateToPane(paneIndex);
                    if (blinkOverlayToggle) _openOptionsForm.BlinkOverlayToggle();
                    if (_openOptionsForm.WindowState == FormWindowState.Minimized)
                        _openOptionsForm.WindowState = FormWindowState.Normal;
                    _openOptionsForm.BringToFront();
                    _openOptionsForm.Activate();
                    return;
                }

                _openOptionsForm = new OptionsForm(_settings, _audio, _pushToTalk);
                if (paneIndex >= 0) _openOptionsForm.NavigateToPane(paneIndex);
                if (blinkOverlayToggle)
                    _openOptionsForm.Shown += (s, e) => { _openOptionsForm.BlinkOverlayToggle(); };
                _openOptionsForm.TopMost = true; // Start topmost to appear above other apps
                _openOptionsForm.FormClosed += (s, e) => {
                    try
                    {
                        var result = _openOptionsForm.DialogResult;
                        if (result == DialogResult.OK)
                        {
                            // All settings were applied live via AudioSettings.Changed events.
                            // Just ensure final save and apply startup setting.
                            _settings.Save();
                            _settings.ApplyStartupSetting();
                            Logger.Info("Settings saved by user.");
                        }
                        else if (result == DialogResult.Retry)
                        {
                            _openOptionsForm.Dispose();
                            _openOptionsForm = null;
                            RunSetupWizard();
                            return;
                        }
                        _openOptionsForm.Dispose();
                        _openOptionsForm = null;

                    }
                    catch (Exception ex) { Logger.Error("Options close handler failed.", ex); }
                };
                // Activated event — drop TopMost once user has seen it, so it behaves normally
                _openOptionsForm.Activated += (s, e) => {
                    if (_openOptionsForm != null && _openOptionsForm.TopMost)
                        _openOptionsForm.TopMost = false;
                };
                _openOptionsForm.Show(); // NON-MODAL — overlay remains interactive
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to show options.", ex);
            }
        }

        // --- AudioSettings Changed Handler ---
        // This is the unified actuation point. When ANY AudioSettings property changes,
        // this fires and TrayApp actuates the hardware/overlay/engine accordingly.
        private bool _audioSettingsProcessing;
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

                    // Check if engine needs restart (key or enabled changed) vs just overlay/sound refresh
                    bool engineNeedsRestart = false;
                    if (_pushToTalk != null && _pushToTalk.Enabled)
                    {
                        // Engine is running — check if mode or keys actually differ
                        bool modeMatch = true;
                        if (_audio.PtToggleEnabled && !_pushToTalk.IsToggleMode) modeMatch = false;
                        else if (!_audio.PtToggleEnabled && _pushToTalk.IsToggleMode) modeMatch = false;
                        bool ptmOnlyWanted = _audio.PtmEnabled && !_audio.PttEnabled && !_audio.PtToggleEnabled;
                        if (ptmOnlyWanted != _pushToTalk.IsPushToMuteMode) modeMatch = false;
                        // If modes match and engine is running, this is likely a cosmetic change (overlay/sound)
                        engineNeedsRestart = !modeMatch;
                    }
                    else if (shouldBeActive)
                    {
                        // Engine not running but should be — definitely need to start
                        engineNeedsRestart = true;
                    }

                    if (shouldBeActive && engineNeedsRestart)
                    {
                        DisablePtt();
                        EnablePtt();
                        // EnablePtt already handles: mic mute state, overlay show/hide, engine start.
                        // Just show the toast here.

                        // Build dynamic key list for toast
                        string modeName = null;
                        string keyList = null;
                        string modeDesc = null;
                        if (_audio.PttEnabled && _audio.PttKey > 0)
                        {
                            modeName = "Push-to-Talk";
                            keyList = PushToTalk.GetKeyName(_audio.PttKey);
                            if (_audio.PttKey2 > 0) keyList += ", " + PushToTalk.GetKeyName(_audio.PttKey2);
                            if (_audio.PttKey3 > 0) keyList += ", " + PushToTalk.GetKeyName(_audio.PttKey3);
                            modeDesc = "Hold " + keyList + " to unmute your mic";
                        }
                        else if (_audio.PtmEnabled && _audio.PtmKey > 0)
                        {
                            modeName = "Push-to-Mute";
                            keyList = PushToTalk.GetKeyName(_audio.PtmKey);
                            if (_audio.PtmKey2 > 0) keyList += ", " + PushToTalk.GetKeyName(_audio.PtmKey2);
                            if (_audio.PtmKey3 > 0) keyList += ", " + PushToTalk.GetKeyName(_audio.PtmKey3);
                            modeDesc = "Hold " + keyList + " to mute your mic";
                        }
                        else if (_audio.PtToggleEnabled && _audio.PtToggleKey > 0)
                        {
                            modeName = "Push-to-Toggle";
                            keyList = PushToTalk.GetKeyName(_audio.PtToggleKey);
                            if (_audio.PtToggleKey2 > 0) keyList += ", " + PushToTalk.GetKeyName(_audio.PtToggleKey2);
                            if (_audio.PtToggleKey3 > 0) keyList += ", " + PushToTalk.GetKeyName(_audio.PtToggleKey3);
                            modeDesc = "Tap " + keyList + " to toggle your mic on/off";
                        }

                        // Show toast (skip during wizard — the animation is the feedback)
                        if (modeName != null && modeDesc != null && (_openWelcomeForm == null || _openWelcomeForm.IsDisposed))
                            ShowCorrectionToast(modeName + " Enabled \u2014 " + modeDesc, true);
                    }
                    else if (shouldBeActive && !engineNeedsRestart)
                    {
                        // Key change with same mode — still need engine restart for new keys
                        DisablePtt();
                        EnablePtt();
                    }
                    else if (!shouldBeActive)
                    {
                        // Dismiss correction toast synchronously to avoid racing with mic warning
                        if (_activeToast != null && !_activeToast.IsDisposed)
                            try { _activeToast.Dismiss(); } catch { }
                        DisablePtt();
                        try { Audio.SetMicMute(false); } catch { }
                        if (_micStatus != null && !_micStatus.IsDisposed)
                        {
                            _micStatus.UseExtendedDelay();
                            _micStatus.HideOverlay();
                        }
                        CheckMicUnprotected();
                    }

                    if (shouldBeActive)
                        DismissMicWarning();
                    UpdateTrayState();
                }

                if ((what & SettingsChange.MicLock) != 0)
                {
                    if (_audio.MicLockEnabled)
                    {
                        try { _micPreLockVol = (int)Audio.GetMicVolume(); } catch { _micPreLockVol = -1; }
                        if (_restoreMicTimer != null) { _restoreMicTimer.Dispose(); _restoreMicTimer = null; }
                        EnforceMic(false);
                        DismissMicWarning();
                        if (_openWelcomeForm == null || _openWelcomeForm.IsDisposed)
                            ShowCorrectionToast("Mic Volume Locked at " + _audio.MicLockVolume + "%" + " \u2014 Volume enforcement is active", true, true, false);
                        Logger.Info("Mic lock ON (snapshot: " + _micPreLockVol + "%)");
                    }
                    else
                    {
                        DismissActiveToast();
                        SmoothRestore(true);
                        CheckMicUnprotected();
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
                        if (_openWelcomeForm == null || _openWelcomeForm.IsDisposed)
                            ShowCorrectionToast("Speaker Volume Locked at " + _audio.SpeakerLockVolume + "%" + " \u2014 Volume enforcement is active", true, false, true);
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
                        DismissMicWarning();
                        _settings.MicOverlayEnabled = true;
                        _settings.Save();
                        if (_micStatus != null && !_micStatus.IsDisposed)
                        {
                            _micStatus.OverlayEnabled = true;
                            _micStatus.UseExtendedDelay();
                            _micStatus.ShowMicOpenIdle();
                        }
                        if (_openWelcomeForm == null || _openWelcomeForm.IsDisposed)
                            ShowCorrectionToast("Mute Microphone When Idle \u2014 Mic will auto-mute after " + _audio.AfkMicSec + "s of inactivity", true);
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
                        CheckMicUnprotected();
                    }
                    UpdateTrayState();
                }

                if ((what & SettingsChange.AfkSpeaker) != 0)
                {
                    if (_audio.AfkSpeakerEnabled && (_openWelcomeForm == null || _openWelcomeForm.IsDisposed))
                        ShowCorrectionToast("Mute Speakers When Idle \u2014 Audio will fade out after " + _audio.AfkSpeakerSec + "s of inactivity", true);
                    else if (!_audio.AfkSpeakerEnabled)
                        DismissActiveToast();
                    UpdateTrayState();
                }

                if ((what & SettingsChange.PttCosmetic) != 0)
                {
                    // Overlay or sound toggle changed — refresh overlay visibility without restarting engine
                    if (_micStatus != null && !_micStatus.IsDisposed && _pushToTalk != null && _pushToTalk.Enabled)
                    {
                        bool showOverlay = false;
                        if (_audio.PttEnabled) showOverlay = _audio.PttShowOverlay;
                        else if (_audio.PtmEnabled) showOverlay = _audio.PtmShowOverlay;
                        else if (_audio.PtToggleEnabled) showOverlay = _audio.PtToggleShowOverlay;

                        if (!showOverlay)
                            _micStatus.HideOverlay();
                        else
                        {
                            _micStatus.OverlayEnabled = true;
                            bool micMuted = false;
                            try { micMuted = Audio.GetMicMute(); } catch { }
                            if (micMuted) _micStatus.ShowMicClosed();
                            else _micStatus.ShowMicOpenIdle();
                        }
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
                            _micStatus.UseExtendedDelay();
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
                            _micStatus.UseExtendedDelay();
                            _micStatus.ShowMicClosed();
                        }

                        DismissMicWarning();
                        DismissActiveToast();
                        if (_openWelcomeForm == null || _openWelcomeForm.IsDisposed)
                            ShowCorrectionToast("Voice Activity Enabled \u2014 Mic auto-unmutes when you speak", true);
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
                        try { Audio.SetMicMute(false); } catch { }
                        if (_micStatus != null && !_micStatus.IsDisposed)
                        {
                            _micStatus.UseExtendedDelay();
                            _micStatus.HideOverlay();
                        }
                        CheckMicUnprotected();
                    }
                    UpdateTrayState();
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
                        Audio.SetMicMute(true);
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

        private void ExitApplication()
        {
            Logger.Info("User requested exit.");
            Dispose();
            Application.Exit();
        }

        private void RunSetupWizard()
        {
            _settings.FirstRunComplete = false;
            _settings.Save();
            ShowWelcomeDialog();
        }

        // --- Welcome Dialog ---

        private void ShowWelcomeDialog()
        {
            // Prevent duplicate wizard instances
            if (_openWelcomeForm != null && !_openWelcomeForm.IsDisposed) {
                if (_openWelcomeForm.WindowState == FormWindowState.Minimized)
                    _openWelcomeForm.WindowState = FormWindowState.Normal;
                try { _openWelcomeForm.BringToFront(); _openWelcomeForm.Activate(); } catch { }
                return;
            }

            CloseSplash(); // never overlap splash with welcome wizard

            // Close any open OptionsForm first
            try { if (_openOptionsForm != null && !_openOptionsForm.IsDisposed) { _openOptionsForm.Close(); _openOptionsForm.Dispose(); _openOptionsForm = null; } } catch { _openOptionsForm = null; }

            _openWelcomeForm = new WelcomeForm(_audio, _pushToTalk);
            _openWelcomeForm.FormClosed += (s, e) => {
                // All settings were applied live via AudioSettings.Changed events.
                // Just mark first run complete and do final save.
                _settings.FirstRunComplete = true;
                _settings.Save();
                _settings.ApplyStartupSetting();
                Logger.Info("First run complete. MicEnf=" + _settings.MicEnforceEnabled
                    + " SpkEnf=" + _settings.SpeakerEnforceEnabled
                    + " AfkMic=" + _settings.AfkMicMuteEnabled
                    + " PTT=" + _settings.PushToTalkEnabled);
                Logger.Info("First-run wizard complete.");

                // Show splash ONLY if Options isn't already open
                if (_openOptionsForm == null || _openOptionsForm.IsDisposed)
                    ShowSplash(() => CheckMicUnprotected());
                else
                    CheckMicUnprotected();

                _openWelcomeForm = null;
            };
            _openWelcomeForm.Show();
        }

        // --- Correction Toast ---

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

            if (_contextMenu != null && _contextMenu.IsHandleCreated)
            {
                try { _contextMenu.BeginInvoke(showAction); }
                catch (Exception ex) { Logger.Error("BeginInvoke failed for toast.", ex); }
            }
            else
            {
                Logger.Warn("Cannot show toast — no UI handle available.");
            }
        }

        private void DismissActiveToast()
        {
            if (_activeToast == null || _activeToast.IsDisposed) return;
            Action dismissAction = () => {
                try { if (_activeToast != null && !_activeToast.IsDisposed) _activeToast.Dismiss(); } catch { }
            };
            if (_contextMenu != null && _contextMenu.IsHandleCreated)
                try { _contextMenu.BeginInvoke(dismissAction); } catch { }
            // If no handle, toast will auto-dismiss on its own timer
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
                    bool wizardUp = _openWelcomeForm != null && !_openWelcomeForm.IsDisposed;

                    if (unprotected && !warningShowing && !splashUp && !wizardUp && !_micWarningDismissed) {
                        ShowMicWarning();
                    } else if (!unprotected && warningShowing) {
                        HideMicWarning();
                    } else if (unprotected && !warningShowing) {
                        // Log why we're NOT showing
                        if (splashUp) Logger.Info("MicWarningPoller: waiting for splash");
                        else if (wizardUp) Logger.Info("MicWarningPoller: waiting for wizard");
                        else if (_micWarningDismissed) Logger.Info("MicWarningPoller: already dismissed by user");
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

        /// <summary>Legacy call — now a no-op. The poll timer handles everything.</summary>
        private void CheckMicUnprotected() { }

        /// <summary>Legacy call — now a no-op. The poll timer handles everything.</summary>
        private void DismissMicWarning() { }

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

                if (_contextMenu != null && _contextMenu.IsHandleCreated)
                    try { _contextMenu.BeginInvoke(showAction); } catch { Logger.Warn("BeginInvoke failed for InfoToast — skipping (no UI handle)."); }
                else
                    Logger.Warn("Cannot show InfoToast — no UI handle available.");
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

            if (_pollTimer != null) _pollTimer.Dispose();
            if (_pttSafetyTimer != null) _pttSafetyTimer.Dispose();
            if (_killEvent != null) _killEvent.Dispose();
            if (_pushToTalk != null) _pushToTalk.Dispose();
            if (_restoreMicTimer != null) _restoreMicTimer.Dispose();
            if (_restoreSpkTimer != null) _restoreSpkTimer.Dispose();

            if (_openWelcomeForm != null && !_openWelcomeForm.IsDisposed)
                try { _openWelcomeForm.Close(); _openWelcomeForm.Dispose(); } catch { }
            _openWelcomeForm = null;

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
