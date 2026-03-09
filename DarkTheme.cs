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
        // Centralized font system — use these instead of 'new Font' everywhere
        public static readonly Font Body     = new Font("Segoe UI", 9f);
        public static readonly Font BodyBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font Small    = new Font("Segoe UI", 7.5f);
        public static readonly Font SmallBold = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        public static readonly Font Heading  = new Font("Segoe UI", 10f, FontStyle.Bold);
        public static readonly Font Caption  = new Font("Segoe UI", 8f);
        public static readonly Font Tiny     = new Font("Segoe UI", 7f);
        public static readonly Font TinyItalic = new Font("Segoe UI", 7f, FontStyle.Italic);
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
        // Commonly used derived colors — centralized to avoid rogue RGB values
        public static readonly Color InputHover = Color.FromArgb(28, 28, 28);  // Input field hover
        public static readonly Color BtnBgDark = Color.FromArgb(20, 20, 20);   // Dark button background
        public static readonly Color SepLine   = Color.FromArgb(40, 40, 40);   // Separator lines
        public static readonly Color DotInactive = Color.FromArgb(50, 50, 50); // Inactive page dots
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

        /// <summary>Paints a premium breathing glow around a segment rectangle.
        /// Uses rounded corners, anti-aliased edges, and a sine-wave pulse (2s period).
        /// Call from any paint handler — uses DateTime for phase, no extra timer needed.</summary>
        public static void PaintBreathingGlow(Graphics g, Rectangle rect, Color accent, int cornerRadius)
        {
            var oldSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Breathing pulse: 2-second period, oscillates between 0.55 and 1.0
            double phase = (DateTime.UtcNow.Ticks / 10000.0) % 2000.0; // 0-2000ms
            float breath = 0.55f + 0.45f * (float)Math.Sin(phase * Math.PI * 2.0 / 2000.0);

            int rad = cornerRadius > 0 ? cornerRadius : Dpi.S(6);

            // Outer glow layers — graduated, soft falloff with rounded rects
            for (int layer = 5; layer >= 0; layer--) {
                int expand = Dpi.S(layer * 2 + 2);
                float layerFade = 1f - (layer / 6f); // inner layers brighter
                int alpha = (int)(breath * layerFade * 28);
                if (alpha <= 0) continue;
                var outerRect = new Rectangle(rect.X - expand, rect.Y - expand, rect.Width + expand * 2, rect.Height + expand * 2);
                using (var path = RoundedRect(outerRect, rad + expand / 2))
                using (var b = new SolidBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B)))
                    g.FillPath(b, path);
            }

            // Inner fill — very subtle tint inside the segment
            {
                int fillAlpha = (int)(breath * 12);
                using (var path = RoundedRect(rect, rad))
                using (var b = new SolidBrush(Color.FromArgb(fillAlpha, accent.R, accent.G, accent.B)))
                    g.FillPath(b, path);
            }

            // Border — rounded, breathing opacity
            {
                int borderAlpha = (int)(breath * 180);
                using (var path = RoundedRect(rect, rad))
                using (var p = new Pen(Color.FromArgb(borderAlpha, accent.R, accent.G, accent.B), Dpi.PenW(1.5f)))
                    g.DrawPath(p, path);
            }

            // Inner highlight edge — thin bright line at top for depth
            {
                int hlAlpha = (int)(breath * 45);
                var hlRect = new Rectangle(rect.X + Dpi.S(2), rect.Y + 1, rect.Width - Dpi.S(4), Dpi.S(1));
                using (var b = new SolidBrush(Color.FromArgb(hlAlpha, 255, 255, 255)))
                    g.FillRectangle(b, hlRect);
            }

            g.SmoothingMode = oldSmooth;
        }

        /// <summary>
        /// Paints a subtle, deterministic star field across a panel background.
        /// Uses a seeded RNG so stars are stable (no flicker on repaint).
        /// </summary>
    }
}
