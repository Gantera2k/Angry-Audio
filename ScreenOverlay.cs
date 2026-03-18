// ScreenOverlay.cs
// Transparent topmost overlay windows for screen temperature and brightness adjustment.
//
// This is the reliable fallback when SetDeviceGammaRamp is blocked by the GPU driver
// (AMD, Nvidia, Intel drivers frequently block it when HDR, G-Sync, FreeSync, or
// certain display configurations are active).
//
// Implementation:
//   • One Form per Screen (per-monitor capability)
//   • FormBorderStyle.None, ShowInTaskbar = false, TopMost = true
//   • AllowTransparency = true, BackColor = computed color, Opacity = computed alpha
//   • WS_EX_TRANSPARENT + WS_EX_NOACTIVATE → clicks pass through, never steals focus
//
// Color math:
//   Temperature: KelvinToRGB gives (rMul, gMul, bMul). The screen "needs" gMul and bMul
//   channels reduced. We overlay an amber/warm color at an alpha that approximates this.
//   At 2700K (maximum warmth): blue reduced ~66%. Overlay color = warm amber, ~35% opacity.
//   At 6500K: no overlay (completely hidden).
//
//   Brightness: black overlay at alpha = (100 - brightness%) * 0.85.
//   At 100%: no overlay. At 20%: ~68% black overlay.
//
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AngryAudio
{
    internal static class ScreenOverlay
    {
        // Extended window styles for click-through behaviour
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
        const int GWL_EXSTYLE     = -20;
        const int WS_EX_LAYERED   = 0x00080000;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_NOACTIVATE  = 0x08000000;
        const int WS_EX_TOOLWINDOW  = 0x00000080;

        // ── State ─────────────────────────────────────────────────────────────
        static readonly Dictionary<string, OverlayForm> _overlays = new Dictionary<string, OverlayForm>();
        static bool _enabled;
        static int  _tempK      = 6500;
        static int  _brightness = 100;
        static Dictionary<string, KeyValuePair<int, int>> _overrides = new Dictionary<string, KeyValuePair<int, int>>();

        // ── Public API ────────────────────────────────────────────────────────

        public static void Apply(int tempK, int brightness, string deviceName = null)
        {
            if (string.IsNullOrEmpty(deviceName)) {
                _tempK = tempK;
                _brightness = brightness;
                _overrides.Clear();
            } else {
                _overrides[deviceName] = new KeyValuePair<int, int>(tempK, brightness);
            }
            _enabled = true;
            UpdateAll();
        }

        public static void Reset()
        {
            _enabled = false;
            foreach (var o in _overlays.Values) { try { o.Hide(); } catch { } }
        }

        public static void Dispose()
        {
            foreach (var o in _overlays.Values) { try { o.Close(); o.Dispose(); } catch { } }
            _overlays.Clear();
        }

        // ── Internal ──────────────────────────────────────────────────────────

        static void UpdateAll()
        {
            foreach (Screen scr in Screen.AllScreens)
            {
                string key = scr.DeviceName;
                int tk = _overrides.ContainsKey(key) ? _overrides[key].Key : _tempK;
                int br = _overrides.ContainsKey(key) ? _overrides[key].Value : _brightness;

                Color overlayColor;
                double opacity;
                ComputeOverlay(tk, br, out overlayColor, out opacity);

                bool anyEffect = opacity > 0.005;

                OverlayForm frm;
                if (!_overlays.TryGetValue(key, out frm) || frm.IsDisposed)
                {
                    frm = new OverlayForm();
                    MakeClickThrough(frm);
                    _overlays[key] = frm;
                }

                frm.Bounds = scr.Bounds;

                if (!_enabled || !anyEffect)
                {
                    if (frm.Visible) frm.Hide();
                    continue;
                }

                frm.BackColor = overlayColor;
                frm.Opacity   = Math.Min(1.0, Math.Max(0.0, opacity));

                if (!frm.Visible)
                {
                    frm.Show();
                    frm.TopMost = true;
                }
                else
                {
                    frm.Refresh();
                }
            }
        }

        /// <summary>
        /// Compute the overlay color and opacity for the given temperature and brightness.
        /// Both effects are combined into a single overlay using alpha compositing.
        /// </summary>
        static void ComputeOverlay(int tempK, int brightness, out Color color, out double opacity)
        {
            // ── Temperature component ─────────────────────────────────────────
            // warmth = 0 at 6500K (neutral), 1 at 2700K (max warmth)
            double warmth = Math.Max(0, 1.0 - (tempK - 2700.0) / (6500.0 - 2700.0));
            warmth = warmth * warmth; // quadratic for more natural curve

            // Warm amber color (2700K reference)
            const int WARM_R = 255, WARM_G = 147, WARM_B = 41;
            double warmAlpha = warmth * 0.42; // max 42% tint at 2700K

            // ── Brightness component ──────────────────────────────────────────
            double dimAlpha = Math.Max(0, (100 - brightness) / 100.0) * 0.88; // max 88% black at 20%

            // ── Combine ───────────────────────────────────────────────────────
            // Blend warm color over black proportionally
            double totalAlpha = 1.0 - (1.0 - warmAlpha) * (1.0 - dimAlpha);
            if (totalAlpha < 0.004) { color = Color.Black; opacity = 0; return; }

            double r = (WARM_R * warmAlpha + 0 * (1 - warmAlpha));
            double g = (WARM_G * warmAlpha + 0 * (1 - warmAlpha));
            double b = (WARM_B * warmAlpha + 0 * (1 - warmAlpha));

            // Mix with dim black contribution
            double w = dimAlpha / totalAlpha;
            r = r * (1 - w);
            g = g * (1 - w);
            b = b * (1 - w);

            color   = Color.FromArgb(Clamp(r), Clamp(g), Clamp(b));
            opacity = totalAlpha;
        }

        static int Clamp(double v) { int i = (int)v; return i < 0 ? 0 : i > 255 ? 255 : i; }

        static void MakeClickThrough(Form frm)
        {
            int ex = GetWindowLong(frm.Handle, GWL_EXSTYLE);
            SetWindowLong(frm.Handle, GWL_EXSTYLE,
                ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        // ── OverlayForm ───────────────────────────────────────────────────────

        sealed class OverlayForm : Form
        {
            public OverlayForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar   = false;
                TopMost         = true;
                AllowTransparency = true;
                BackColor = Color.Black;
                Opacity = 0;
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
                    return cp;
                }
            }
        }
    }
}
