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
        private const int POLL_INTERVAL_MS = 30;

        // Hook (optional, for key consumption only)
        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _hookProc;
        private bool _needsHook;

        // State
        private bool _enabled;
        private bool _keyHeld;
        private volatile int _hookKeyDown; // Set by LL hook when consuming toggle keys
        private bool _disposed;
        private System.Collections.Generic.HashSet<int> _hotkeys = new System.Collections.Generic.HashSet<int>();

        private bool _toggleMode;
        private bool _pushToMuteMode;
        private bool _toggleMicOpen;

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

        public bool IsTalking { get { return _toggleMode ? _toggleMicOpen : (_pushToMuteMode ? !_keyHeld : _keyHeld); } }
        public bool Enabled { get { return _enabled; } }
        public System.Collections.Generic.HashSet<int> Hotkeys { get { return _hotkeys; } }
        public int LastTriggeredKey { get; private set; }
        public bool IsToggleMode { get { return _toggleMode; } }
        public bool IsPushToMuteMode { get { return _pushToMuteMode; } }

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
        }

        public void Disable()
        {
            if (!_enabled) return;

            if (_pollTimer != null)
            {
                try { _pollTimer.Dispose(); } catch { }
                _pollTimer = null;
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
                    // For hooked toggle keys, check both GetAsyncKeyState AND the hook's direct signal
                    bool isToggleKey = (vk == VK_CAPS_LOCK || vk == VK_SCROLL_LOCK || vk == VK_NUM_LOCK);
                    bool keyDown = (GetAsyncKeyState(vk) & 0x8000) != 0;
                    if (!keyDown && isToggleKey && _hookKeyDown == vk)
                        keyDown = true; // Hook saw it even if GetAsyncKeyState didn't
                    
                    if (keyDown)
                    {
                        anyDown = true;
                        triggeredKey = vk;
                        break;
                    }
                }

                if (_toggleMode)
                {
                    if (anyDown && !_keyHeld)
                    {
                        _keyHeld = true;
                        LastTriggeredKey = triggeredKey;
                        _toggleMicOpen = !_toggleMicOpen;
                        Audio.SetMicMute(!_toggleMicOpen);
                        if (_toggleMicOpen)
                        {
                            try { OnTalkStart?.Invoke(); } catch { }
                            Logger.Debug("Toggle: Mic opened.");
                        }
                        else
                        {
                            try { OnTalkStop?.Invoke(); } catch { }
                            Logger.Debug("Toggle: Mic closed.");
                        }
                    }
                    else if (!anyDown && _keyHeld)
                    {
                        _keyHeld = false;
                    }
                }
                else if (_pushToMuteMode)
                {
                    if (anyDown && !_keyHeld)
                    {
                        _keyHeld = true;
                        LastTriggeredKey = triggeredKey;
                        Audio.SetMicMute(true);
                        try { OnTalkStop?.Invoke(); } catch { }
                        Logger.Debug("PTM: Key down — mic muted.");
                    }
                    else if (!anyDown && _keyHeld)
                    {
                        _keyHeld = false;
                        Audio.SetMicMute(false);
                        try { OnTalkStart?.Invoke(); } catch { }
                        Logger.Debug("PTM: Key up — mic open.");
                    }
                }
                else
                {
                    if (anyDown && !_keyHeld)
                    {
                        _keyHeld = true;
                        LastTriggeredKey = triggeredKey;
                        Audio.SetMicMute(false);
                        try { OnTalkStart?.Invoke(); } catch { }
                        Logger.Debug("PTT: Key down — mic open.");
                    }
                    else if (!anyDown && _keyHeld)
                    {
                        _keyHeld = false;
                        Audio.SetMicMute(true);
                        try { OnTalkStop?.Invoke(); } catch { }
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

            if (_toggleMode)
            {
                bool shouldBeMuted = !_toggleMicOpen;
                if (Audio.GetMicMute() != shouldBeMuted)
                {
                    Audio.SetMicMute(shouldBeMuted);
                    Logger.Debug("Toggle: Re-enforced mic " + (shouldBeMuted ? "muted" : "open") + ".");
                }
            }
            else if (_pushToMuteMode)
            {
                if (!_keyHeld && Audio.GetMicMute())
                {
                    Audio.SetMicMute(false);
                    Logger.Debug("PTM: Re-opened mic (something tried to mute it).");
                }
            }
            else
            {
                if (!_keyHeld && !Audio.GetMicMute())
                {
                    Audio.SetMicMute(true);
                    Logger.Debug("PTT: Re-muted mic (something tried to unmute it).");
                }
            }
        }

        /// <summary>
        /// Lightweight hook — ONLY consumes CapsLock/ScrollLock/NumLock to prevent system toggle.
        /// Does NO state tracking. If Windows kills it, nothing breaks.
        /// </summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _enabled)
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
                            return (IntPtr)1;
                        }
                    }
                }
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    if (_hotkeys.Contains(vkCode) &&
                        (vkCode == VK_CAPS_LOCK || vkCode == VK_SCROLL_LOCK || vkCode == VK_NUM_LOCK))
                    {
                        Logger.Info("HOOK: Key UP VK 0x" + vkCode.ToString("X2") + " — clearing _hookKeyDown");
                        _hookKeyDown = 0;
                        return (IntPtr)1; // Consume KEYUP too to prevent toggle
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void ForceCapsLockOff()
        {
            if (!_enabled || !_hotkeys.Contains(VK_CAPS_LOCK)) return;
            if ((GetKeyState(VK_CAPS_LOCK) & 0x0001) != 0)
            {
                keybd_event(0x14, 0x45, 0x0001, UIntPtr.Zero);
                keybd_event(0x14, 0x45, 0x0001 | 0x0002, UIntPtr.Zero);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disable();
        }

        public static string GetKeyName(int vk)
        {
            switch (vk)
            {
                case 0: return "Add Key";
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
    }
}
