// DisplayManager.cs
// Screen color engine.
//
// Color Filters: Uses SetMagnificationDesktopColorEffect (user32.dll) with a 25-field
//                struct — the approach proven to work by the NegativeScreen open-source app.
//                Also calls MagSetFullscreenColorEffect (magnification.dll) as backup.
//
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AngryAudio
{
    public static class DisplayManager
    {
        // ── Filter type constants ─────────────────────────────────────────────
        public const int CF_GRAYSCALE        = 0;
        public const int CF_INVERTED         = 1;
        public const int CF_GRAYSCALE_INV    = 2;
        public const int CF_DEUTERANOPIA     = 3;
        public const int CF_PROTANOPIA       = 4;
        public const int CF_TRITANOPIA       = 5;
        public const int CF_NONE             = -1;

        public static readonly string[] FilterNames = {
            "Grayscale", "Inverted", "Grayscale Inverted",
            "Red-green (deuteranopia)", "Red-green (protanopia)", "Blue-yellow (tritanopia)"
        };
        public static readonly string[] FilterDescriptions = {
            "Remove all color — calm, distraction-free black & white",
            "Reverse all screen colors — high-contrast dark mode",
            "Grayscale with inverted luminance — white text on black",
            "Enhance contrast for green-weak color blindness",
            "Enhance contrast for red-weak color blindness",
            "Enhance contrast for blue-yellow color blindness"
        };

        // ── ColorEffect struct: 25 explicit float fields ───────────────────────
        // Must be 25 individual fields for correct P/Invoke layout.
        // (float[] with ByValArray marshals as a managed heap pointer, not inline data.)
        [StructLayout(LayoutKind.Sequential)]
        struct ColorEffect
        {
            public float f00, f01, f02, f03, f04;
            public float f10, f11, f12, f13, f14;
            public float f20, f21, f22, f23, f24;
            public float f30, f31, f32, f33, f34;
            public float f40, f41, f42, f43, f44;
        }

        // ── P/Invokes ─────────────────────────────────────────────────────────

        // Primary: undocumented user32 function used by NegativeScreen — most reliable
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetMagnificationDesktopColorEffect(ref ColorEffect pEffect);

        // Backup: documented magnification.dll
        [DllImport("magnification.dll", SetLastError = true)]
        static extern bool MagInitialize();

        [DllImport("magnification.dll", SetLastError = true)]
        static extern bool MagUninitialize();

        [DllImport("magnification.dll", SetLastError = true)]
        static extern bool MagSetFullscreenColorEffect(ref ColorEffect pEffect);

        static bool _magInitialized;

        /// <summary>Call once from the UI thread at app startup to initialize the Magnification API eagerly.</summary>
        public static void InitMag()
        {
            if (_magInitialized) return;
            _magInitialized = MagInitialize();
            if (!_magInitialized)
                Logger.Error("DisplayManager.InitMag: MagInitialize failed (Win32=" + Marshal.GetLastWin32Error() + ")", null);
            else
                Logger.Info("DisplayManager.InitMag: MagInitialize OK");
        }

        // ── Win32 — WM_SETTINGCHANGE ──────────────────────────────────────────
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam,
            string lParam, uint flags, uint timeout, out IntPtr result);

        const uint WM_SETTINGCHANGE        = 0x001A;
        const uint SMTO_ABORTIFHUNG        = 0x0002;
        const uint SMTO_NOTIMEOUTIFNOTHUNG = 0x0008;
        static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;
        const string CF_KEY = @"HKEY_CURRENT_USER\Software\Microsoft\ColorFiltering";

        // ── Monitor enumeration ───────────────────────────────────────────────
        [DllImport("user32.dll")]
        static extern bool EnumDisplayDevices(string dev, uint n, ref DISPLAY_DEVICE dd, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]  public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        const uint DISPLAY_DEVICE_ACTIVE = 0x00000001;

        public class MonitorInfo { public string DeviceName; public string FriendlyName; }
        static List<MonitorInfo> _monitors;

        public static List<MonitorInfo> GetMonitors()
        {
            var list = new List<MonitorInfo>();
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
            for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
            {
                if ((dd.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0)
                {
                    string name = dd.DeviceName;
                    var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                    string friendly = name;
                    if (EnumDisplayDevices(name, 0, ref mon, 0) && !string.IsNullOrEmpty(mon.DeviceString))
                        friendly = mon.DeviceString;
                    var ex2 = _monitors != null ? _monitors.Find(m => m.DeviceName == name) : null;
                    list.Add(ex2 ?? new MonitorInfo { DeviceName = name, FriendlyName = friendly });
                }
                dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
            }
            _monitors = list;
            return list;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public static void ApplyAll(int tempK, int brightness,
                                    int filterType = CF_NONE,
                                    int intensity  = 100,
                                    int colorBoost = 75,
                                    string deviceName = null)
        {
            ScreenOverlay.Apply(tempK, brightness, deviceName);
            ApplyColorFilter(filterType, intensity, colorBoost);
        }

        /// <summary>
        /// Apply a color filter to the entire screen.
        /// Uses SetMagnificationDesktopColorEffect (user32.dll) — the method used by NegativeScreen.
        /// Falls back to MagSetFullscreenColorEffect (magnification.dll) if that fails.
        /// </summary>
        public static void ApplyColorFilter(int filterType, int intensity = 100, int colorBoost = 75)
        {
            try
            {
                bool active = (filterType >= 0 && filterType <= 5);
                float t = Math.Max(0f, Math.Min(1f, intensity / 100f));

                ColorEffect effect = active
                    ? BlendEffect(IdentityEffect(), EffectForFilter(filterType), t)
                    : IdentityEffect();

                // Try user32 SetMagnificationDesktopColorEffect first (NegativeScreen approach)
                bool ok1 = false;
                try { ok1 = SetMagnificationDesktopColorEffect(ref effect); }
                catch { }

                // Also try magnification.dll as backup
                bool ok2 = false;
                try
                {
                    if (!_magInitialized) _magInitialized = MagInitialize();
                    if (_magInitialized)  ok2 = MagSetFullscreenColorEffect(ref effect);
                }
                catch { }

                Logger.Info("DisplayManager.ApplyColorFilter ft=" + filterType +
                            " i=" + intensity + " user32=" + ok1 + " mag=" + ok2 +
                            " user32err=" + (ok1 ? 0 : Marshal.GetLastWin32Error()));

                // Registry + WM_SETTINGCHANGE on background thread — never block the UI
                int _ft = filterType, _in = intensity, _cb = colorBoost;
                bool _act = active;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate(object _) {
                    try {
                        Registry.SetValue(CF_KEY, "HotkeyEnabled",            1,                    RegistryValueKind.DWord);
                        Registry.SetValue(CF_KEY, "SystemColorFiltersEnabled", 1,                    RegistryValueKind.DWord);
                        Registry.SetValue(CF_KEY, "FilterType",                Math.Max(0, _ft),    RegistryValueKind.DWord);
                        Registry.SetValue(CF_KEY, "Intensity",                 _in,                 RegistryValueKind.DWord);
                        Registry.SetValue(CF_KEY, "ColorBoost",                _cb,                 RegistryValueKind.DWord);
                        Registry.SetValue(CF_KEY, "Active",                    _act ? 1 : 0,        RegistryValueKind.DWord);
                        IntPtr res;
                        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero,
                            "ColorFiltering", SMTO_ABORTIFHUNG | SMTO_NOTIMEOUTIFNOTHUNG, 1000, out res);
                    } catch { }
                }, null);
            }
            catch (Exception ex) { Logger.Error("DisplayManager.ApplyColorFilter", ex); }
        }

        public static void Cleanup()
        {
            try
            {
                var identity = IdentityEffect();
                try { SetMagnificationDesktopColorEffect(ref identity); } catch { }
                if (_magInitialized)
                {
                    try { MagSetFullscreenColorEffect(ref identity); } catch { }
                    try { MagUninitialize(); } catch { }
                    _magInitialized = false;
                }
            }
            catch { }
        }

        // ── Color effect builders ─────────────────────────────────────────────

        static ColorEffect IdentityEffect()
        {
            var e = new ColorEffect();
            e.f00 = 1; e.f11 = 1; e.f22 = 1; e.f33 = 1; e.f44 = 1;
            return e;
        }

        static ColorEffect EffectForFilter(int filterType)
        {
            var e = new ColorEffect();
            switch (filterType)
            {
                case CF_GRAYSCALE:
                    // BT.601 luma — all output channels get the same weighted sum
                    e.f00 = 0.299f; e.f01 = 0.299f; e.f02 = 0.299f;
                    e.f10 = 0.587f; e.f11 = 0.587f; e.f12 = 0.587f;
                    e.f20 = 0.114f; e.f21 = 0.114f; e.f22 = 0.114f;
                    e.f33 = 1; e.f44 = 1;
                    return e;

                case CF_INVERTED:
                    e.f00 = -1; e.f11 = -1; e.f22 = -1; e.f33 = 1; e.f44 = 1;
                    e.f40 =  1; e.f41 =  1; e.f42 =  1;
                    return e;

                case CF_GRAYSCALE_INV:
                    e.f00 = -0.299f; e.f01 = -0.299f; e.f02 = -0.299f;
                    e.f10 = -0.587f; e.f11 = -0.587f; e.f12 = -0.587f;
                    e.f20 = -0.114f; e.f21 = -0.114f; e.f22 = -0.114f;
                    e.f33 = 1; e.f44 = 1;
                    e.f40 = 1; e.f41 = 1; e.f42 = 1;
                    return e;

                case CF_DEUTERANOPIA:
                    e.f00 = 0.625f; e.f01 = 0.375f;
                    e.f10 = 0.700f; e.f11 = 0.300f;
                    e.f21 = 0.300f; e.f22 = 0.700f;
                    e.f33 = 1; e.f44 = 1;
                    return e;

                case CF_PROTANOPIA:
                    e.f00 = 0.567f; e.f01 = 0.433f;
                    e.f10 = 0.558f; e.f11 = 0.442f;
                    e.f21 = 0.242f; e.f22 = 0.758f;
                    e.f33 = 1; e.f44 = 1;
                    return e;

                case CF_TRITANOPIA:
                    e.f00 = 0.950f; e.f01 = 0.050f;
                    e.f11 = 0.433f; e.f12 = 0.567f;
                    e.f21 = 0.475f; e.f22 = 0.525f;
                    e.f33 = 1; e.f44 = 1;
                    return e;

                default: return IdentityEffect();
            }
        }

        static ColorEffect BlendEffect(ColorEffect a, ColorEffect b, float t)
        {
            var r = new ColorEffect();
            float u = 1f - t;
            r.f00 = a.f00*u + b.f00*t; r.f01 = a.f01*u + b.f01*t; r.f02 = a.f02*u + b.f02*t; r.f03 = a.f03*u + b.f03*t; r.f04 = a.f04*u + b.f04*t;
            r.f10 = a.f10*u + b.f10*t; r.f11 = a.f11*u + b.f11*t; r.f12 = a.f12*u + b.f12*t; r.f13 = a.f13*u + b.f13*t; r.f14 = a.f14*u + b.f14*t;
            r.f20 = a.f20*u + b.f20*t; r.f21 = a.f21*u + b.f21*t; r.f22 = a.f22*u + b.f22*t; r.f23 = a.f23*u + b.f23*t; r.f24 = a.f24*u + b.f24*t;
            r.f30 = a.f30*u + b.f30*t; r.f31 = a.f31*u + b.f31*t; r.f32 = a.f32*u + b.f32*t; r.f33 = a.f33*u + b.f33*t; r.f34 = a.f34*u + b.f34*t;
            r.f40 = a.f40*u + b.f40*t; r.f41 = a.f41*u + b.f41*t; r.f42 = a.f42*u + b.f42*t; r.f43 = a.f43*u + b.f43*t; r.f44 = a.f44*u + b.f44*t;
            return r;
        }

        // ── Convenience wrappers ──────────────────────────────────────────────

        public static void ApplyGamma(string deviceName, int tempK, int brightness)
        {
            ScreenOverlay.Apply(tempK, brightness, deviceName);
        }

        public static void ResetAllGamma()  { ScreenOverlay.Reset(); }
        public static void ResetToNormal()  { ScreenOverlay.Reset(); ApplyColorFilter(CF_NONE); }
        public static void RestoreOriginal(){ ResetToNormal(); }

        public static void ApplyPreset(string preset, string deviceName = null)
        {
            int tK; int br; int ft; int intensity; int colorBoost;
            GetPresetValues(preset, out tK, out br, out ft, out intensity, out colorBoost);
            ApplyAll(tK, br, ft, intensity, colorBoost, deviceName);
        }

        // ── Presets ───────────────────────────────────────────────────────────
        //
        // Real-world most-used configs (f.lux telemetry, Night Light data, sleep science):
        //   Normal:  6500K 100% — neutral calibrated daylight baseline
        //   Focus:   5000K 95%  — f.lux top daytime pick, warm without cast
        //   Relax:   4000K 85%  — gentle all-day comfort (most popular Iris setting)
        //   Night:   3400K 80%  — f.lux "at sunset" default
        //   Cinema:  5500K 70%  — D55 film reference white, dimmed for dark room
        //   Bedtime: 2700K 50%  — sleep-science gold standard
        public static readonly string[] PresetNames = { "Normal", "Focus", "Relax", "Night", "Cinema", "Bedtime" };
        public static readonly string[] PresetDescs = {
            "6500K \u2014 Neutral daylight, calibrated display baseline",
            "5000K \u2014 Warm daylight for long work sessions (f.lux default)",
            "4000K \u2014 Gentle all-day comfort, no colour shift",
            "3400K \u2014 Evening warm tone, reduces eye strain after dark",
            "5500K \u2014 Film reference white, dimmed for a dark room",
            "2700K \u2014 Pre-sleep max warmth, blocks shortwave blue light"
        };

        public static void GetPresetValues(string preset, out int tempK, out int brightness,
                                           out int filterType, out int intensity, out int colorBoost)
        {
            filterType = CF_NONE; intensity = 100; colorBoost = 75;
            switch (preset)
            {
                case "Focus":   tempK = 5000; brightness = 95;  break;
                case "Relax":   tempK = 4000; brightness = 85;  break;
                case "Night":   tempK = 3400; brightness = 80;  break;
                case "Cinema":  tempK = 5500; brightness = 70;  break;
                case "Bedtime": tempK = 2700; brightness = 50;  break;
                case "Normal":
                default:        tempK = 6500; brightness = 100; break;
            }
        }

        // ── Unified cycle items (alphabetical order for predictable Alt+Q/Alt+E cycling) ──
        // Type codes:  -2 = Blue Light,  -1 = preset,  0..5 = color filter type
        public static readonly string[] CycleItemNames = {
            "Bedtime", "Blue Light", "Cinema", "Deuteranopia", "Focus",
            "Grayscale", "Grayscale Inverted", "Inverted", "Night", "Normal",
            "Protanopia", "Relax", "Tritanopia"
        };
        public static readonly string[] CycleItemSubs = {
            "2700K \u2014 Pre-sleep warmth",
            "3800K \u2014 Blue light filter",
            "5500K \u2014 Cinema dim",
            "Color filter \u2014 Deuteranopia",
            "5000K \u2014 Warm daylight focus",
            "Color filter \u2014 Greyscale",
            "Color filter \u2014 Greyscale Inverted",
            "Color filter \u2014 Inverted",
            "3400K \u2014 Evening warm tone",
            "6500K \u2014 Neutral daylight",
            "Color filter \u2014 Protanopia",
            "4000K \u2014 All-day comfort",
            "Color filter \u2014 Tritanopia"
        };
        // -2 = Blue Light special,  -1 = preset (look up name in CycleItemNames),  0-5 = filter type
        public static readonly int[] CycleItemType = {
            -1, -2, -1, CF_DEUTERANOPIA, -1,
            CF_GRAYSCALE, CF_GRAYSCALE_INV, CF_INVERTED, -1, -1,
            CF_PROTANOPIA, -1, CF_TRITANOPIA
        };

        /// <summary>Given current settings state, returns the current cycle index (best guess).</summary>
        public static int GetCurrentCycleIdx(string displayPreset, bool blueLightOn, int filterType)
        {
            // If a color filter is active, find the matching filter in the cycle
            if (filterType >= 0 && filterType <= 5)
            {
                for (int i = 0; i < CycleItemType.Length; i++)
                    if (CycleItemType[i] == filterType) return i;
            }
            // If a known preset is set, find it by name
            if (!string.IsNullOrEmpty(displayPreset))
            {
                for (int i = 0; i < CycleItemNames.Length; i++)
                    if (CycleItemType[i] == -1 && string.Equals(CycleItemNames[i], displayPreset, StringComparison.OrdinalIgnoreCase))
                        return i;
            }
            // Blue Light special
            if (blueLightOn)
            {
                for (int i = 0; i < CycleItemType.Length; i++)
                    if (CycleItemType[i] == -2) return i;
            }
            // Default to Normal
            for (int i = 0; i < CycleItemNames.Length; i++)
                if (CycleItemNames[i] == "Normal") return i;
            return 0;
        }

        /// <summary>Apply a cycle item by index. Returns the item name applied.</summary>
        public static string ApplyCycleItem(int idx, string deviceName = null)
        {
            idx = ((idx % CycleItemNames.Length) + CycleItemNames.Length) % CycleItemNames.Length;
            int type = CycleItemType[idx];
            if (type == -1) {
                // Preset
                string presetName = CycleItemNames[idx];
                int tK; int br; int ft; int intensity; int colorBoost;
                GetPresetValues(presetName, out tK, out br, out ft, out intensity, out colorBoost);
                ApplyAll(tK, br, ft, intensity, colorBoost, deviceName);
            } else if (type == -2) {
                // Blue Light Filter (3800K, normal brightness)
                ApplyGamma(deviceName, 3800, 85);
                ApplyColorFilter(CF_NONE, 100, 75);
            } else {
                // Color filter (type = 0..5)
                ApplyGamma(deviceName, 6500, 100);
                ApplyColorFilter(type, 100, 75);
            }
            return CycleItemNames[idx];
        }
        // Legacy overloads
        public static void ApplyAll(int tempK, int brightness, bool grayscale, bool invert,
                                    string deviceName = null)
        {
            int ft = grayscale ? CF_GRAYSCALE : (invert ? CF_INVERTED : CF_NONE);
            ApplyAll(tempK, brightness, ft, 100, 75, deviceName);
        }

        public static void GetPresetValues(string preset, out int tempK, out int brightness,
                                           out bool grayscale, out bool invert)
        {
            int ft; int i; int cb;
            GetPresetValues(preset, out tempK, out brightness, out ft, out i, out cb);
            grayscale = (ft == CF_GRAYSCALE);
            invert    = (ft == CF_INVERTED);
        }
    }
}
