using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AngryAudio
{
    /// <summary>
    /// Push-to-talk / push-to-toggle / push-to-mute system.
    /// 
    /// PRIMARY DETECTION: Polls GetAsyncKeyState on a fast timer (~30ms).
    /// This is immune to Windows silently killing hooks during heavy game rendering.
    /// GetAsyncKeyState reads physical key state directly — no hooks, no focus issues.
    ///
    /// SECONDARY (optional): A lightweight LL keyboard hook ONLY to consume CapsLock/
    /// ScrollLock/NumLock keypresses (prevent system toggle). The hook does NO state
    /// tracking — if Windows kills it, nothing breaks. Polling carries 100% of the logic.
    /// </summary>
    public class PushToTalk : IDisposable
    {
        // Win32
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        // Hook — ONLY for consuming toggle keys (CapsLock etc.)
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // Polling timer
        private System.Threading.Timer _pollTimer;
        private System.Threading.Timer _hookHealthTimer;
        private const int POLL_INTERVAL_MS = 30;

        // Hook (optional, for key consumption only)
        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _hookProc;
        private bool _needsHook;

        // State
        private bool _enabled;
        private bool _keyHeld;
        private volatile int _hookKeyDown; // Set by LL hook when consuming toggle keys
        /// <summary>Exposed for UI highlight — the VK currently held via LL hook (toggle keys only).</summary>
        public static volatile int HookHeldKey;
        private bool _disposed;
        private System.Collections.Generic.HashSet<int> _hotkeys = new System.Collections.Generic.HashSet<int>();

        private bool _toggleMode;
        private bool _pushToMuteMode;
        private bool _toggleMicOpen;
        private System.Collections.Generic.Dictionary<int, int> _keyModes = new System.Collections.Generic.Dictionary<int, int>();
        private int _activeMode = -1; // 0=PTT, 1=PTM, 2=Toggle
        public int ActiveMode { get { return _activeMode; } }
        private bool _hasPttKeys, _hasPtmKeys, _hasToggleKeys;

        // Common virtual key codes
        public const int VK_CAPS_LOCK = 0x14;
        public const int VK_SCROLL_LOCK = 0x91;
        public const int VK_NUM_LOCK = 0x90;
        public const int VK_F13 = 0x7C;
        public const int VK_F14 = 0x7D;
        public const int VK_PAUSE = 0x13;
        public const int VK_INSERT = 0x2D;
        public const int VK_TILDE = 0xC0;

        // --- Public API (unchanged) ---

        public bool IsTalking { get {
            if (_keyHeld) {
                if (_activeMode == 0) return true;   // PTT held = talking
                if (_activeMode == 1) return false;  // PTM held = muted
                if (_activeMode == 2) return _toggleMicOpen; // Toggle = current state
            }
            // No key held — resting state
            if (_hasPttKeys || _hasPtmKeys || _hasToggleKeys) {
                bool hasPtmOnly = _hasPtmKeys && !_hasPttKeys && !_hasToggleKeys;
                if (hasPtmOnly) return true; // PTM resting = mic open = talking
                if (_hasToggleKeys && !_hasPttKeys) return _toggleMicOpen;
                return false; // PTT present = resting muted = not talking
            }
            return _toggleMode ? _toggleMicOpen : (_pushToMuteMode ? !_keyHeld : _keyHeld);
        } }
        public bool Enabled { get { return _enabled; } }
        public System.Collections.Generic.HashSet<int> Hotkeys { get { return _hotkeys; } }
        public int LastTriggeredKey { get; private set; }
        public bool IsToggleMode { get { return _toggleMode; } }
        public bool IsPushToMuteMode { get { return _pushToMuteMode; } }
        /// <summary>When true, hook passes all keys through without consuming them. Used during key capture.</summary>
        public bool SuspendHook { get; set; }

        public event Action OnTalkStart;
        public event Action OnTalkStop;

        public PushToTalk()
        {
            _hotkeys.Add(VK_CAPS_LOCK);
            _hookProc = HookCallback;
        }

        /// <summary>
        /// Enable PTT/PTM/Toggle. Starts polling timer.
        /// Installs a lightweight hook ONLY if CapsLock/ScrollLock/NumLock is a hotkey.
        /// </summary>
        public void Enable(int hotkey, bool consumeKey, bool toggleMode = false, bool pushToMuteMode = false, int hotkey2 = 0, int hotkey3 = 0)
        {
            if (_enabled) Disable();

            _hotkeys.Clear();
            _hotkeys.Add(hotkey);
            if (hotkey2 > 0) _hotkeys.Add(hotkey2);
            if (hotkey3 > 0) _hotkeys.Add(hotkey3);

            _toggleMode = toggleMode;
            _pushToMuteMode = pushToMuteMode;
            _keyHeld = false;
            _toggleMicOpen = false;

            // Only install hook if we need to consume toggle keys
            _needsHook = _hotkeys.Contains(VK_CAPS_LOCK) || _hotkeys.Contains(VK_SCROLL_LOCK) || _hotkeys.Contains(VK_NUM_LOCK);
            if (_needsHook)
            {
                try
                {
                    using (var process = Process.GetCurrentProcess())
                    using (var module = process.MainModule)
                    {
                        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(module.ModuleName), 0);
                    }
                    if (_hookId == IntPtr.Zero)
                        Logger.Error("Failed to install key-consumption hook. Error: " + Marshal.GetLastWin32Error());
                }
                catch (Exception ex) { Logger.Error("Hook install failed.", ex); }
            }

            _enabled = true;

            if (_pushToMuteMode)
            {
                Audio.SetMicMute(false);
                Logger.Info("Push-to-mute enabled (polling). Hotkeys: " + string.Join(", ", _hotkeys));
            }
            else
            {
                Audio.SetMicMute(true);
                string mode = _toggleMode ? "toggle" : "hold";
                Logger.Info("Push-to-" + mode + " enabled (polling). Hotkeys: " + string.Join(", ", _hotkeys));
            }

            ForceCapsLockOff();

            // Start polling — this is the primary detection mechanism
            _pollTimer = new System.Threading.Timer(PollKeyState, null, POLL_INTERVAL_MS, POLL_INTERVAL_MS);

            // Hook health check timer — reinstalls hook if Windows killed it
            if (_needsHook)
            {
                _hookHealthTimer = new System.Threading.Timer(_ => {
                    if (!_enabled || !_needsHook) return;
                    if (_hookId == IntPtr.Zero) {
                        Logger.Warn("Hook health: hook handle is zero — reinstalling.");
                        ReinstallHook();
                    }
                }, null, 5000, 5000);
            }
        }

        public void EnableMultiMode(int pttKey, int ptmKey, int toggleKey, bool consumeKey, int pttKey2 = 0, int pttKey3 = 0, int ptmKey2 = 0, int ptmKey3 = 0, int toggleKey2 = 0, int toggleKey3 = 0)
        {
            if (_enabled) Disable();
            _hotkeys.Clear();
            _keyModes.Clear();
            if (pttKey > 0) { _hotkeys.Add(pttKey); _keyModes[pttKey] = 0; }
            if (pttKey2 > 0) { _hotkeys.Add(pttKey2); _keyModes[pttKey2] = 0; }
            if (pttKey3 > 0) { _hotkeys.Add(pttKey3); _keyModes[pttKey3] = 0; }
            if (ptmKey > 0) { _hotkeys.Add(ptmKey); _keyModes[ptmKey] = 1; }
            if (ptmKey2 > 0) { _hotkeys.Add(ptmKey2); _keyModes[ptmKey2] = 1; }
            if (ptmKey3 > 0) { _hotkeys.Add(ptmKey3); _keyModes[ptmKey3] = 1; }
            if (toggleKey > 0) { _hotkeys.Add(toggleKey); _keyModes[toggleKey] = 2; }
            if (toggleKey2 > 0) { _hotkeys.Add(toggleKey2); _keyModes[toggleKey2] = 2; }
            if (toggleKey3 > 0) { _hotkeys.Add(toggleKey3); _keyModes[toggleKey3] = 2; }
            _hasPttKeys = pttKey > 0 || pttKey2 > 0 || pttKey3 > 0;
            _hasPtmKeys = ptmKey > 0 || ptmKey2 > 0 || ptmKey3 > 0;
            _hasToggleKeys = toggleKey > 0 || toggleKey2 > 0 || toggleKey3 > 0;
            _toggleMode = _hasToggleKeys && !_hasPttKeys && !_hasPtmKeys;
            _pushToMuteMode = _hasPtmKeys && !_hasPttKeys;
            _keyHeld = false;
            _toggleMicOpen = false;
            _activeMode = -1;
            _needsHook = _hotkeys.Contains(VK_CAPS_LOCK) || _hotkeys.Contains(VK_SCROLL_LOCK) || _hotkeys.Contains(VK_NUM_LOCK);
            if (_needsHook)
            {
                try
                {
                    using (var process = Process.GetCurrentProcess())
                    using (var module = process.MainModule)
                        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(module.ModuleName), 0);
                    if (_hookId == IntPtr.Zero)
                        Logger.Error("LL Hook install FAILED. Error: " + Marshal.GetLastWin32Error());
                    else
                        Logger.Info("LL Hook installed. Handle: " + _hookId + " Thread: " + System.Threading.Thread.CurrentThread.ManagedThreadId);
                }
                catch (Exception ex) { Logger.Error("Hook install failed.", ex); }
            }
            _enabled = true;
            // PTM-only starts unmuted (mic open); all other combos start muted
            bool ptmOnly = _hasPtmKeys && !_hasPttKeys && !_hasToggleKeys;
            Audio.SetMicMute(!ptmOnly);
            Logger.Info("Multi-mode enabled. Keys: " + string.Join(", ", _hotkeys) + " InitialMute=" + (!ptmOnly));
            ForceCapsLockOff();
            _pollTimer = new System.Threading.Timer(PollKeyState, null, POLL_INTERVAL_MS, POLL_INTERVAL_MS);
            if (_needsHook)
                _hookHealthTimer = new System.Threading.Timer(_ => {
                    if (!_enabled || !_needsHook) return;
                    if (_hookId == IntPtr.Zero) { Logger.Info("Hook health: hook lost, reinstalling..."); ReinstallHook(); }
                }, null, 5000, 5000);
        }

        public void Disable()
        {
            if (!_enabled) return;

            if (_pollTimer != null)
            {
                try { _pollTimer.Dispose(); } catch { }
                _pollTimer = null;
            }

            if (_hookHealthTimer != null)
            {
                try { _hookHealthTimer.Dispose(); } catch { }
                _hookHealthTimer = null;
            }

            if (_hookId != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(_hookId); } catch { }
                _hookId = IntPtr.Zero;
            }

            _enabled = false;
            _keyHeld = false;
            _toggleMicOpen = false;

            Audio.SetMicMute(false);
            Logger.Info("Push-to-" + (_toggleMode ? "toggle" : (_pushToMuteMode ? "mute" : "talk")) + " disabled.");
        }

        /// <summary>
        /// Core polling callback — reads physical key state via GetAsyncKeyState.
        /// Runs every ~30ms. This is the ONLY place key state transitions happen.
        /// No hooks involved. Cannot be killed by Windows. Works in all focus states.
        /// </summary>
        private void PollKeyState(object state)
        {
            if (!_enabled) return;

            try
            {
                bool anyDown = false;
                int triggeredKey = 0;
                foreach (int vk in _hotkeys)
                {
                    // Skip CapsLock if ForceCapsLockOff recently injected synthetic events
                    if (vk == VK_CAPS_LOCK && Environment.TickCount < _capsLockSuppressUntilTick) continue;

                    bool isToggleKey = (vk == VK_CAPS_LOCK || vk == VK_SCROLL_LOCK || vk == VK_NUM_LOCK);
                    bool keyDown;

                    if (isToggleKey)
                    {
                        // For toggle keys: prefer the hook's direct signal (most reliable).
                        // GetAsyncKeyState is unreliable for CapsLock on many keyboards —
                        // it may only flash briefly or not report held state at all.
                        keyDown = (_hookKeyDown == vk);
                        // Fallback: if hook isn't installed or was killed, use GetAsyncKeyState
                        if (!keyDown && _hookId == IntPtr.Zero)
                            keyDown = (GetAsyncKeyState(vk) & 0x8000) != 0;
                    }
                    else
                    {
                        keyDown = (GetAsyncKeyState(vk) & 0x8000) != 0;
                    }
                    
                    if (keyDown)
                    {
                        anyDown = true;
                        triggeredKey = vk;
                        break;
                    }
                }

                // Determine mode for this key
                int mode = _keyModes.ContainsKey(triggeredKey) ? _keyModes[triggeredKey] : (_toggleMode ? 2 : (_pushToMuteMode ? 1 : 0));

                if (anyDown && !_keyHeld)
                {
                    _keyHeld = true;
                    _activeMode = mode;
                    LastTriggeredKey = triggeredKey;
                    if (mode == 2) // Toggle
                    {
                        _toggleMicOpen = !_toggleMicOpen;
                        Audio.SetMicMute(!_toggleMicOpen);
                        if (_toggleMicOpen) { if (OnTalkStart != null) { try { OnTalkStart(); } catch { } } }
                        else { if (OnTalkStop != null) { try { OnTalkStop(); } catch { } } }
                    }
                    else if (mode == 1) // PTM
                    {
                        Audio.SetMicMute(true);
                        if (OnTalkStop != null) { try { OnTalkStop(); } catch { } }
                    }
                    else // PTT (mode 0)
                    {
                        Audio.SetMicMute(false);
                        if (OnTalkStart != null) { try { OnTalkStart(); } catch { } }
                    }
                }
                else if (!anyDown && _keyHeld)
                {
                    _keyHeld = false;
                    if (_activeMode == 1) // PTM key-up
                    {
                        Audio.SetMicMute(false);
                        if (OnTalkStart != null) { try { OnTalkStart(); } catch { } }
                    }
                    else if (_activeMode == 0) // PTT key-up
                    {
                        Audio.SetMicMute(true);
                        if (OnTalkStop != null) { try { OnTalkStop(); } catch { } }
                        Logger.Debug("PTT: Key up — mic muted.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("PTT poll error.", ex);
            }
        }

        /// <summary>
        /// Called by external polling loop to enforce mic state against other apps.
        /// </summary>
        public void EnforceMute()
        {
            if (!_enabled) return;

            // Determine what mic state SHOULD be right now based on active mode and key state
            bool shouldBeMuted;
            if (_keyHeld)
            {
                // Key is physically held — respect the active mode
                if (_activeMode == 0) shouldBeMuted = false;      // PTT: key held = mic open
                else if (_activeMode == 1) shouldBeMuted = true;   // PTM: key held = mic muted
                else shouldBeMuted = !_toggleMicOpen;              // Toggle: depends on toggle state
            }
            else
            {
                // No key held — what's the resting state?
                if (_hasPttKeys || _hasPtmKeys || _hasToggleKeys)
                {
                    bool hasPtmOnly = _hasPtmKeys && !_hasPttKeys && !_hasToggleKeys;
                    if (hasPtmOnly) shouldBeMuted = false;         // PTM-only resting = open
                    else if (_hasToggleKeys && !_hasPttKeys) shouldBeMuted = !_toggleMicOpen; // Toggle resting = toggle state
                    else shouldBeMuted = true;                     // PTT present = resting muted
                }
                else
                {
                    // Legacy single-mode
                    if (_toggleMode) shouldBeMuted = !_toggleMicOpen;
                    else if (_pushToMuteMode) shouldBeMuted = false;
                    else shouldBeMuted = true;
                }
            }

            try
            {
                if (Audio.GetMicMute() != shouldBeMuted)
                {
                    Audio.SetMicMute(shouldBeMuted);
                    Logger.Debug("EnforceMute: corrected to " + (shouldBeMuted ? "muted" : "open") + " (mode=" + _activeMode + ")");
                }
            }
            catch { }
        }

        /// <summary>
        /// Lightweight hook — ONLY consumes CapsLock/ScrollLock/NumLock to prevent system toggle.
        /// Does NO state tracking. If Windows kills it, nothing breaks.
        /// </summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _enabled && !SuspendHook)
            {
                int msg = (int)wParam;
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    int flags = Marshal.ReadInt32(lParam, 8);
                    if ((flags & 0x10) == 0) // Not injected
                    {
                        if (_hotkeys.Contains(vkCode) &&
                            (vkCode == VK_CAPS_LOCK || vkCode == VK_SCROLL_LOCK || vkCode == VK_NUM_LOCK))
                        {
                            Logger.Info("HOOK: Consuming VK 0x" + vkCode.ToString("X2") + " — setting _hookKeyDown");
                            _hookKeyDown = vkCode;
                            HookHeldKey = vkCode;
                            return (IntPtr)1;
                        }
                    }
                }
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    int flags = Marshal.ReadInt32(lParam, 8);
                    // Skip injected keyups — ForceCapsLockOff injects these to clear toggle state.
                    // If we consume them, CapsLock state never clears → infinite ForceCapsLockOff loop.
                    if ((flags & 0x10) != 0) return CallNextHookEx(_hookId, nCode, wParam, lParam);
                    if (_hotkeys.Contains(vkCode) &&
                        (vkCode == VK_CAPS_LOCK || vkCode == VK_SCROLL_LOCK || vkCode == VK_NUM_LOCK))
                    {
                        Logger.Info("HOOK: Key UP VK 0x" + vkCode.ToString("X2") + " — clearing _hookKeyDown");
                        _hookKeyDown = 0;
                        HookHeldKey = 0;
                        return (IntPtr)1; // Consume KEYUP too to prevent toggle
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void ReinstallHook()
        {
            try
            {
                if (_hookId != IntPtr.Zero)
                {
                    try { UnhookWindowsHookEx(_hookId); } catch { }
                    _hookId = IntPtr.Zero;
                }
                using (var process = Process.GetCurrentProcess())
                using (var module = process.MainModule)
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(module.ModuleName), 0);
                }
                if (_hookId != IntPtr.Zero)
                    Logger.Info("Hook reinstalled successfully.");
                else
                    Logger.Error("Hook reinstall failed. Error: " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex) { Logger.Error("ReinstallHook failed.", ex); }
        }

        /// <summary>Timestamp of last ForceCapsLockOff injection. Poll ignores CapsLock for 100ms after.</summary>
        private volatile int _capsLockSuppressUntilTick;

        public void ForceCapsLockOff()
        {
            if (!_enabled || !_hotkeys.Contains(VK_CAPS_LOCK)) return;
            if (_keyHeld) return; // Don't fight the user's physical keypress
            if ((GetKeyState(VK_CAPS_LOCK) & 0x0001) != 0)
            {
                _capsLockSuppressUntilTick = Environment.TickCount + 100; // suppress for 100ms
                keybd_event(0x14, 0x45, 0x0001, UIntPtr.Zero);
                keybd_event(0x14, 0x45, 0x0001 | 0x0002, UIntPtr.Zero);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disable();
            DisableVoiceActivity();
            StopPeakMonitor();
        }

        public static string GetKeyName(int vk)
        {
            switch (vk)
            {
                case 0: return "Add Key";
                case 0x01: return "MB1";
                case 0x02: return "MB2";
                case 0x04: return "MB3";
                case 0x05: return "MB4";
                case 0x06: return "MB5";
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x10: return "Shift";
                case 0x11: return "Ctrl";
                case 0x12: return "Alt";
                case 0x13: return "Pause";
                case 0x14: return "Caps Lock";
                case 0x1B: return "Esc";
                case 0x20: return "Space";
                case 0x21: return "Page Up";
                case 0x22: return "Page Down";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "Left Arrow";
                case 0x26: return "Up Arrow";
                case 0x27: return "Right Arrow";
                case 0x28: return "Down Arrow";
                case 0x2C: return "Print Screen";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x5B: return "Left Win";
                case 0x5C: return "Right Win";
                case 0x5D: return "Menu";
                case 0x91: return "Scroll Lock";
                case 0x90: return "Num Lock";
                case 0xA0: return "Left Shift";
                case 0xA1: return "Right Shift";
                case 0xA2: return "Left Ctrl";
                case 0xA3: return "Right Ctrl";
                case 0xA4: return "Left Alt";
                case 0xA5: return "Right Alt";
                case 0xC0: return "Tilde";
                case 0xBA: return "Semicolon";
                case 0xBB: return "Equals";
                case 0xBC: return "Comma";
                case 0xBD: return "Minus";
                case 0xBE: return "Period";
                case 0xBF: return "Slash";
                case 0xDB: return "Left Bracket";
                case 0xDC: return "Backslash";
                case 0xDD: return "Right Bracket";
                case 0xDE: return "Quote";
                default:
                    if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
                    if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
                    if (vk >= 0x60 && vk <= 0x69) return "Num " + (vk - 0x60);
                    if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
                    return "Key 0x" + vk.ToString("X2");
            }
        }

        public static Tuple<string, int>[] GetHotkeyOptions()
        {
            return new[]
            {
                Tuple.Create("~ (Tilde)", VK_TILDE),
                Tuple.Create("Caps Lock", VK_CAPS_LOCK),
                Tuple.Create("Scroll Lock", VK_SCROLL_LOCK),
                Tuple.Create("Insert", VK_INSERT),
                Tuple.Create("Pause", VK_PAUSE),
                Tuple.Create("F13", VK_F13),
                Tuple.Create("F14", VK_F14),
            };
        }

        // =====================================================================
        //  VOICE ACTIVITY DETECTION
        // =====================================================================

        private System.Threading.Timer _voiceTimer;
        private IAudioMeterInformation _voiceMeter; // fallback
        private IAudioClient _voiceAudioClient;
        private IAudioCaptureClient _voiceCaptureClient;
        private int _voiceChannels, _voiceBitsPerSample, _voiceBlockAlign;
        private bool _useRawCapture;
        private volatile bool _voiceEnabled;
        private volatile float _voiceThreshold = 0.30f; // 0.0–1.0
        private volatile int _voiceHoldoverMs = 500;
        private volatile bool _voiceSpeaking;
        private int _voiceSilentMs; // ms since last peak above threshold
        private const int VOICE_POLL_MS = 20;

        /// <summary>Current peak level (0.0–1.0). Read by UI for live meter. Written by voice timer.</summary>
        public volatile float CurrentPeakLevel;

        /// <summary>True when voice activity has detected speech and mic is open.</summary>
        public bool IsVoiceActive { get { return _voiceSpeaking; } }

        /// <summary>True when voice monitoring is running.</summary>
        public bool VoiceMonitoringActive { get { return _voiceEnabled; } }

        /// <summary>
        /// Start voice activity monitoring. Mutually exclusive with PTT/PTM/Toggle.
        /// </summary>
        public void EnableVoiceActivity(float threshold, int holdoverMs)
        {
            DisableVoiceActivity();

            _voiceThreshold = Math.Max(0.01f, Math.Min(1f, threshold));
            _voiceHoldoverMs = Math.Max(200, Math.Min(5000, holdoverMs));
            _voiceSpeaking = false;
            _voiceSilentMs = 0;
            CurrentPeakLevel = 0f;
            _useRawCapture = false;

            // Try raw WASAPI capture first — bypasses mute/volume, reads directly from hardware
            IAudioClient ac; IAudioCaptureClient cc; int ch, bps, ba;
            if (Audio.OpenRawCapture(out ac, out cc, out ch, out bps, out ba))
            {
                _voiceAudioClient = ac;
                _voiceCaptureClient = cc;
                _voiceChannels = ch;
                _voiceBitsPerSample = bps;
                _voiceBlockAlign = ba;
                _useRawCapture = true;
                Logger.Info("Voice Activity: using raw WASAPI capture (bypasses mute/volume).");
            }
            else
            {
                // Fallback to IAudioMeterInformation
                Logger.Info("Voice Activity: raw capture unavailable, falling back to peak meter.");
                try
                {
                    _voiceMeter = Audio.GetMicMeterInfo();
                    if (_voiceMeter == null)
                    {
                        Logger.Error("Voice Activity: Failed to acquire peak meter. Cannot start.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Voice Activity: COM activation failed.", ex);
                    return;
                }
            }

            // Start muted
            Audio.SetMicMute(true);
            _voiceEnabled = true;

            _voiceTimer = new System.Threading.Timer(VoicePollCallback, null, VOICE_POLL_MS, VOICE_POLL_MS);
            Logger.Info("Voice Activity enabled. Threshold=" + (_voiceThreshold * 100f).ToString("F0") + "% Holdover=" + _voiceHoldoverMs + "ms RawCapture=" + _useRawCapture);
        }

        /// <summary>Stop voice activity monitoring and release COM resources.</summary>
        public void DisableVoiceActivity()
        {
            if (!_voiceEnabled && _voiceTimer == null && _voiceMeter == null && _voiceAudioClient == null) return;

            _voiceEnabled = false;

            if (_voiceTimer != null)
            {
                try { _voiceTimer.Dispose(); } catch { }
                _voiceTimer = null;
            }

            if (_voiceMeter != null)
            {
                try { if (System.Runtime.InteropServices.Marshal.IsComObject(_voiceMeter)) System.Runtime.InteropServices.Marshal.ReleaseComObject(_voiceMeter); } catch { }
                _voiceMeter = null;
            }

            Audio.CloseRawCapture(ref _voiceAudioClient, ref _voiceCaptureClient);
            _useRawCapture = false;

            if (_voiceSpeaking)
            {
                _voiceSpeaking = false;
                if (OnTalkStop != null) { try { OnTalkStop(); } catch { } }
            }

            Audio.SetMicMute(false);
            CurrentPeakLevel = 0f;
            Logger.Info("Voice Activity disabled.");
        }

        /// <summary>Update threshold without restarting the monitor.</summary>
        public void SetVoiceThreshold(float threshold)
        {
            _voiceThreshold = Math.Max(0.01f, Math.Min(1f, threshold));
        }

        /// <summary>Update holdover without restarting the monitor.</summary>
        public void SetVoiceHoldover(int ms)
        {
            _voiceHoldoverMs = Math.Max(200, Math.Min(5000, ms));
        }

        /// <summary>
        /// Voice activity polling callback — runs every ~20ms on a ThreadPool thread.
        /// State machine: SILENT → SPEAKING → SILENT with holdover.
        /// </summary>
        private void VoicePollCallback(object state)
        {
            if (!_voiceEnabled) return;

            try
            {
                float peak;

                // Read peak from raw capture (bypasses mute) or fallback meter
                if (_useRawCapture && _voiceCaptureClient != null)
                {
                    peak = Audio.ReadRawPeak(_voiceCaptureClient, _voiceChannels, _voiceBitsPerSample, _voiceBlockAlign);
                }
                else if (_voiceMeter != null)
                {
                    int hr = _voiceMeter.GetPeakValue(out peak);
                    if (hr != 0) { CurrentPeakLevel = 0f; return; }
                }
                else { CurrentPeakLevel = 0f; return; }

                CurrentPeakLevel = peak;
                bool aboveThreshold = peak >= _voiceThreshold;

                if (_voiceSpeaking)
                {
                    if (aboveThreshold)
                    {
                        // Still speaking — reset holdover
                        _voiceSilentMs = 0;
                    }
                    else
                    {
                        // Below threshold — accumulate silence
                        _voiceSilentMs += VOICE_POLL_MS;
                        if (_voiceSilentMs >= _voiceHoldoverMs)
                        {
                            // Holdover expired — transition to SILENT
                            _voiceSpeaking = false;
                            _voiceSilentMs = 0;
                            Audio.SetMicMute(true);
                            if (OnTalkStop != null) { try { OnTalkStop(); } catch { } }
                        }
                    }
                }
                else
                {
                    if (aboveThreshold)
                    {
                        // Transition to SPEAKING
                        _voiceSpeaking = true;
                        _voiceSilentMs = 0;
                        Audio.SetMicMute(false);
                        if (OnTalkStart != null) { try { OnTalkStart(); } catch { } }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Voice Activity poll error.", ex);
            }
        }

        /// <summary>
        /// Start monitoring peak level only (no mute/unmute) — for the Options UI live meter.
        /// Call StopPeakMonitor() when the pane is hidden.
        /// </summary>
        public void StartPeakMonitor()
        {
            if (_voiceEnabled) return; // Already running full voice activity
            if (_peakMonitorTimer != null) return; // Already monitoring

            // Try raw capture first
            IAudioClient ac; IAudioCaptureClient cc; int ch, bps, ba;
            if (Audio.OpenRawCapture(out ac, out cc, out ch, out bps, out ba))
            {
                _peakMonitorAudioClient = ac;
                _peakMonitorCaptureClient = cc;
                _peakMonitorChannels = ch;
                _peakMonitorBitsPerSample = bps;
                _peakMonitorBlockAlign = ba;
                _peakMonitorRaw = true;
            }
            else
            {
                // Fallback to meter
                try {
                    _peakMonitorMeter = Audio.GetMicMeterInfo();
                    if (_peakMonitorMeter == null) return;
                } catch { return; }
                _peakMonitorRaw = false;
            }

            _peakMonitorTimer = new System.Threading.Timer(_ => {
                if (_peakMonitorRaw && _peakMonitorCaptureClient != null) {
                    CurrentPeakLevel = Audio.ReadRawPeak(_peakMonitorCaptureClient, _peakMonitorChannels, _peakMonitorBitsPerSample, _peakMonitorBlockAlign);
                } else if (_peakMonitorMeter != null) {
                    try { float peak; int hr = _peakMonitorMeter.GetPeakValue(out peak); CurrentPeakLevel = hr == 0 ? peak : 0f; } catch { CurrentPeakLevel = 0f; }
                }
            }, null, 30, 30);
        }

        /// <summary>Stop the peak-only monitor (UI meter).</summary>
        public void StopPeakMonitor()
        {
            if (_peakMonitorTimer != null)
            {
                try { _peakMonitorTimer.Dispose(); } catch { }
                _peakMonitorTimer = null;
            }
            if (_peakMonitorMeter != null)
            {
                try { if (System.Runtime.InteropServices.Marshal.IsComObject(_peakMonitorMeter)) System.Runtime.InteropServices.Marshal.ReleaseComObject(_peakMonitorMeter); } catch { }
                _peakMonitorMeter = null;
            }
            Audio.CloseRawCapture(ref _peakMonitorAudioClient, ref _peakMonitorCaptureClient);
            _peakMonitorRaw = false;
            CurrentPeakLevel = 0f;
        }

        private System.Threading.Timer _peakMonitorTimer;
        private IAudioMeterInformation _peakMonitorMeter;
        private IAudioClient _peakMonitorAudioClient;
        private IAudioCaptureClient _peakMonitorCaptureClient;
        private int _peakMonitorChannels, _peakMonitorBitsPerSample, _peakMonitorBlockAlign;
        private bool _peakMonitorRaw;
    }
}
