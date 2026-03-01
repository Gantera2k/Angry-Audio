// DarkTheme.cs — Shared color palette, fonts, and utility methods.
// Single source of truth for all visual constants across the app.
// Part of partial class DarkTheme — rendering methods are in StarRenderer.cs.
//
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AngryAudio
{
    public static partial class DarkTheme
    {
        // === SHARED COLOR PALETTE — single source of truth ===
        public static readonly Color BG       = Color.FromArgb(12, 12, 12);   // Main background
        public static readonly Color CardBG   = Color.FromArgb(22, 22, 22);   // Card frosted glass base
        public static readonly Color GlassTint = Color.FromArgb(170, 22, 22, 22); // Card frosted glass tint (single source of truth)
        public static readonly Color InputBG  = Color.FromArgb(18, 18, 18);   // Input field background
        public static readonly Color Accent   = Color.FromArgb(74, 158, 204); // Primary accent blue

        // Cached fonts for owner-drawn buttons (prevents GDI handle leak in Paint handlers)
        public static readonly Font BtnFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        public static readonly Font BtnFontBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        public static readonly Color Green    = Color.FromArgb(46, 160, 67);  // Success / active
        public static readonly Color Amber    = Color.FromArgb(218, 175, 67); // Badge / warning gold
        /// <summary>Pre-composited flat glass color = CardBG@alpha200 over BG. Use as BackColor for controls on frosted cards.</summary>
        public static readonly Color GlassFlat = Color.FromArgb(20, 20, 20);
        public static readonly Color CardBdr  = Color.FromArgb(38, 38, 38);   // Card border
        public static readonly Color InputBdr = Color.FromArgb(48, 48, 48);   // Input border
        public static readonly Color Txt      = Color.FromArgb(220, 220, 220); // Primary text
        public static readonly Color Txt2     = Color.FromArgb(170, 170, 170); // Secondary text
        public static readonly Color Txt3     = Color.FromArgb(120, 120, 120); // Tertiary text
        public static readonly Color Txt4     = Color.FromArgb(90, 90, 90);    // Quaternary text

        // Legacy aliases (kept for backward compat)
        public static readonly Color BgDark = Color.FromArgb(30, 30, 30);
        public static readonly Color TextLight = Color.FromArgb(224, 224, 224);
        public static readonly Color Border = Color.FromArgb(61, 61, 61);
        public static readonly Color BtnHover = Color.FromArgb(70, 70, 70);
        public static readonly Color Separator = Color.FromArgb(50, 50, 50);
        public static readonly Color ErrorRed = Color.FromArgb(204, 74, 74);

        // === Native hand cursor — crisp at any DPI, unlike WinForms' ancient ole32.dll bitmap ===
        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
        private static Cursor _hand;
        /// <summary>Native system hand cursor (IDC_HAND). Sharp at all DPI scales.</summary>
        public static Cursor Hand {
            get {
                if (_hand == null) {
                    try {
                        IntPtr h = LoadCursor(IntPtr.Zero, 32649); // IDC_HAND
                        if (h != IntPtr.Zero) _hand = new Cursor(h);
                    } catch { }
                    if (_hand == null) _hand = Cursors.Default; // No hand cursor — use default
                }
                return _hand;
            }
        }

        // === Shared utilities ===
        /// <summary>Creates a rounded rectangle GraphicsPath. Caller must dispose.</summary>
        public static GraphicsPath RoundedRect(Rectangle r, int radius) {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Applies a rounded rectangle clipping region to a control.</summary>
        public static void ApplyRoundedRegion(Control c, int radius) {
            if (c.Width <= 0 || c.Height <= 0) return;
            try {
                var path = RoundedRect(new Rectangle(0, 0, c.Width, c.Height), radius);
                var old = c.Region;
                c.Region = new Region(path);
                if (old != null) old.Dispose();
                path.Dispose();
            } catch { }
        }

        /// <summary>
        /// Paints a subtle, deterministic star field across a panel background.
        /// Uses a seeded RNG so stars are stable (no flicker on repaint).
        /// </summary>
    }
}
