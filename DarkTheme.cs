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
        public static readonly Color InputBG  = Color.FromArgb(18, 18, 18);   // Input field background
        public static readonly Color Accent   = Color.FromArgb(74, 158, 204); // Primary accent blue
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
        public static void PaintStars(Graphics g, int width, int height, int seed = 42)
        {
            if (width <= 0 || height <= 0) return;
            var rng = new Random(seed);
            int count = Math.Max(15, (width * height) / 1600);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (int i = 0; i < count; i++)
            {
                int x = rng.Next(width);
                int y = rng.Next(height);
                int tier = rng.Next(100);
                float radius;
                int alpha;
                Color baseColor;

                if (tier < 60)
                {
                    radius = 0.5f + (float)(rng.NextDouble() * 0.5);
                    alpha = 35 + rng.Next(30);
                    baseColor = Color.White;
                }
                else if (tier < 90)
                {
                    radius = 0.8f + (float)(rng.NextDouble() * 0.7);
                    alpha = 50 + rng.Next(35);
                    baseColor = Color.White;
                }
                else
                {
                    radius = 1.0f + (float)(rng.NextDouble() * 0.8);
                    alpha = 45 + rng.Next(40);
                    baseColor = Accent;
                }

                using (var b = new SolidBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B)))
                    g.FillEllipse(b, x - radius, y - radius, radius * 2, radius * 2);
            }
        }

        /// <summary>
        /// Paints subtle stars inside cards with optional twinkling.
        /// twinkleTick should increment over time for animation; pass 0 for static stars.
        /// </summary>
        public static void PaintCardStars(Graphics g, int width, int height, int seed, int twinkleTick = 0, float alphaMul = 1.0f)
        {
            if (width <= 0 || height <= 0) return;
            var rng = new Random(seed);
            int count = Math.Max(12, Math.Min(150, (width * height) / 2000));
            // Skip anti-alias for background stars — they're tiny, nobody notices
            var oldSmooth = g.SmoothingMode;

            for (int i = 0; i < count; i++)
            {
                int x = rng.Next(width);
                int y = rng.Next(height);
                int tier = rng.Next(100);
                float radius;
                int baseAlpha;
                Color baseColor;

                if (tier < 50)
                {
                    radius = 0.6f + (float)(rng.NextDouble() * 0.5);
                    baseAlpha = 55 + rng.Next(35);
                    baseColor = Color.White;
                }
                else if (tier < 82)
                {
                    radius = 0.8f + (float)(rng.NextDouble() * 0.7);
                    baseAlpha = 70 + rng.Next(50);
                    baseColor = Color.White;
                }
                else
                {
                    radius = 1.0f + (float)(rng.NextDouble() * 0.8);
                    baseAlpha = 65 + rng.Next(55);
                    baseColor = Accent;
                }

                // Twinkle: ~45% of stars have animated brightness
                int alpha = baseAlpha;
                if (twinkleTick > 0 && rng.Next(100) < 45)
                {
                    double phase = rng.NextDouble() * Math.PI * 2;
                    double speed = 0.06 + rng.NextDouble() * 0.08;
                    double wave = Math.Sin(twinkleTick * speed + phase);
                    alpha = (int)(baseAlpha * (0.4 + 0.8 * (wave * 0.5 + 0.5)) * 2.0);
                    alpha = Math.Max(15, Math.Min(220, alpha));
                }

                // Apply alpha multiplier (for frosted glass dimming)
                alpha = (int)(alpha * alphaMul);
                alpha = Math.Max(0, Math.Min(255, alpha));

                if (alpha > 0)
                    using (var b = new SolidBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B)))
                    {
                        if (radius <= 0.9f)
                            g.FillRectangle(b, x, y, 1, 1); // sub-pixel stars: fast 1px rect
                        else
                            g.FillEllipse(b, x - radius, y - radius, radius * 2, radius * 2);
                    }
            }
        }

        /// <summary>
        /// Paints a shooting star streak at the given animation progress (0.0 to 1.0).
        /// The streak travels diagonally across the panel.
        /// </summary>
        public static void PaintShootingStar(Graphics g, int width, int height, ShootingStar star)
        {
            if (width <= 0 || height <= 0 || star == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (int mi = 0; mi < star.Stars.Length; mi++)
            {
                var m = star.Stars[mi];
                if (!m.Active) continue;

                float t = m.Progress;
                float startX = m.StartX * width;
                float startY = m.StartY * height;
                float endX = startX + m.DirX * width * 0.9f;
                float endY = startY + m.DirY * height * 0.9f;
                float dx = endX - startX, dy = endY - startY;

                float headX = startX + dx * t;
                float headY = startY + dy * t;

                // Trail length with fade in/out
                float trailLen = m.TrailLength;
                if (t < 0.2f) trailLen *= t / 0.2f;
                if (t > 0.7f) trailLen *= (1f - t) / 0.3f;

                float tailX = headX - dx * trailLen;
                float tailY = headY - dy * trailLen;

                // Alpha with fade in/out
                int maxAlpha = (int)(180 * m.Brightness);
                int alpha = maxAlpha;
                if (t < 0.15f) alpha = (int)(maxAlpha * t / 0.15f);
                if (t > 0.75f) alpha = (int)(maxAlpha * (1f - t) / 0.25f);
                alpha = Math.Max(0, Math.Min(255, alpha));
                if (alpha <= 0) continue;

                // Get color
                int cR, cG, cB;
                ShootingStar.GetMeteorColor(m.ColorType, m.Warmth, out cR, out cG, out cB);

                // Perpendicular vector for effects
                float pLen = (float)Math.Sqrt(dx * dx + dy * dy);
                float perpX = pLen > 0 ? -dy / pLen : 0;
                float perpY = pLen > 0 ? dx / pLen : 1;

                try
                {
                    // === RENDER BY TYPE ===

                    if (m.MeteorType == 5 && t > 0.55f) // FRAGMENT — splits after 55%
                    {
                        // Main trail up to split point
                        float splitT = 0.55f;
                        float splitX = startX + dx * splitT;
                        float splitY = startY + dy * splitT;
                        using (var p = new Pen(Color.FromArgb(alpha / 2, cR, cG, cB), m.Thickness))
                        {
                            p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                            g.DrawLine(p, splitX, splitY, tailX, tailY);
                        }

                        // 3 fragments diverging from split point
                        float fragProgress = (t - splitT) / (1f - splitT);
                        float fragLen = trailLen * 0.4f;
                        for (int fi = 0; fi < 3; fi++)
                        {
                            float angle = m.FragAngle + (fi - 1) * 0.35f;
                            float fdx = dx * (float)Math.Cos(angle) - dy * (float)Math.Sin(angle);
                            float fdy = dx * (float)Math.Sin(angle) + dy * (float)Math.Cos(angle);
                            float fNorm = (float)Math.Sqrt(fdx * fdx + fdy * fdy);
                            if (fNorm > 0) { fdx /= fNorm; fdy /= fNorm; }
                            float dist = fragProgress * width * 0.12f;
                            float fhX = splitX + fdx * dist;
                            float fhY = splitY + fdy * dist;
                            float ftX = fhX - fdx * fragLen * width * 0.3f;
                            float ftY = fhY - fdy * fragLen * width * 0.3f;
                            int fAlpha = (int)(alpha * (1f - fragProgress * 0.7f));
                            float fThick = m.Thickness * (0.6f - fi * 0.1f);
                            using (var p = new Pen(Color.FromArgb(fAlpha, cR, cG, cB), Math.Max(0.5f, fThick)))
                            {
                                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                                g.DrawLine(p, fhX, fhY, ftX, ftY);
                            }
                            using (var b = new SolidBrush(Color.FromArgb(fAlpha / 2, 255, 220, 180)))
                                g.FillEllipse(b, fhX - 2, fhY - 2, 4, 4);
                        }
                        continue;
                    }

                    // === STANDARD TRAIL (all other types) ===

                    // Main trail line
                    using (var p = new Pen(Color.FromArgb(alpha, cR, cG, cB), m.Thickness))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        g.DrawLine(p, headX, headY, tailX, tailY);
                    }

                    // PHANTOM type: soft wide glow instead of sharp trail
                    if (m.MeteorType == 7)
                    {
                        float gr = m.GlowSize * 1.5f;
                        using (var b = new SolidBrush(Color.FromArgb(alpha / 3, cR, cG, cB)))
                            g.FillEllipse(b, headX - gr, headY - gr, gr * 2, gr * 2);
                        // Faint wide trail aura
                        using (var p = new Pen(Color.FromArgb(alpha / 5, cR, cG, cB), m.Thickness * 4))
                        {
                            p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                            g.DrawLine(p, headX, headY, tailX, tailY);
                        }
                        continue;
                    }

                    // Head glow — core white center + colored outer
                    float glowR = m.GlowSize;
                    int glowA = alpha * 2 / 3;
                    // Outer colored glow
                    using (var b = new SolidBrush(Color.FromArgb(glowA / 2, cR, cG, cB)))
                        g.FillEllipse(b, headX - glowR * 1.3f, headY - glowR * 1.3f, glowR * 2.6f, glowR * 2.6f);
                    // Inner bright core (white-ish)
                    int wcR = Math.Min(255, cR + 60);
                    int wcG = Math.Min(255, cG + 60);
                    int wcB = Math.Min(255, cB + 60);
                    using (var b = new SolidBrush(Color.FromArgb(glowA, wcR, wcG, wcB)))
                        g.FillEllipse(b, headX - glowR * 0.6f, headY - glowR * 0.6f, glowR * 1.2f, glowR * 1.2f);

                    // FIREBALL: extra bloom + heat shimmer
                    if (m.MeteorType == 1)
                    {
                        using (var b = new SolidBrush(Color.FromArgb(alpha / 4, 255, 200, 100)))
                            g.FillEllipse(b, headX - glowR * 2, headY - glowR * 2, glowR * 4, glowR * 4);
                    }

                    // COMET: long gradient tail extension
                    if (m.MeteorType == 2)
                    {
                        float farTailX = tailX - dx * trailLen * 1.2f;
                        float farTailY = tailY - dy * trailLen * 1.2f;
                        for (int layer = 3; layer >= 1; layer--)
                        {
                            int lAlpha = alpha / (layer * 3);
                            float lThick = m.Thickness * 0.2f * layer;
                            using (var p = new Pen(Color.FromArgb(lAlpha, cR, cG, cB), lThick))
                            {
                                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                                g.DrawLine(p, tailX, tailY, farTailX, farTailY);
                            }
                        }
                    }

                    // Faint accent trail extension (all standard types)
                    {
                        int trailAlpha = alpha / 4;
                        float farTailX = tailX - dx * trailLen * 0.5f;
                        float farTailY = tailY - dy * trailLen * 0.5f;
                        using (var p = new Pen(Color.FromArgb(trailAlpha, cR, cG, cB), Math.Max(0.5f, m.Thickness * 0.3f)))
                            g.DrawLine(p, tailX, tailY, farTailX, farTailY);
                    }

                    // Sparkle particles — magical mix of white and colored glitter
                    if (m.Brightness > 0.25f && t > 0.05f && t < 0.90f)
                    {
                        int sparkleCount = m.MeteorType == 1 ? 18 : (m.Brightness > 0.65f ? 14 : 10);
                        int sparkSeed = mi * 1000 + (int)(t * 40) + (int)(DateTime.UtcNow.Ticks / 800000);
                        var sparkRng = new Random(sparkSeed);
                        for (int sp = 0; sp < sparkleCount; sp++)
                        {
                            float along = (float)sparkRng.NextDouble() * 0.85f;
                            float scatter = ((float)sparkRng.NextDouble() - 0.5f) * 2f;
                            float sparkX = headX + (tailX - headX) * along + perpX * scatter * m.GlowSize * 2.8f;
                            float sparkY = headY + (tailY - headY) * along + perpY * scatter * m.GlowSize * 2.8f;
                            float sparkSize = 0.6f + (float)sparkRng.NextDouble() * 2.2f;
                            int sparkAlpha = (int)(alpha * 0.75f * (1f - along * 0.6f));
                            if (sparkAlpha > 0)
                            {
                                // Mix: ~40% white, ~30% meteor color, ~30% random magical colors
                                int colorChoice = sparkRng.Next(10);
                                int sR, sG, sB;
                                if (colorChoice < 4) { sR = 255; sG = 255; sB = 255; } // white-hot
                                else if (colorChoice < 7) { sR = cR; sG = cG; sB = cB; } // meteor's own color
                                else {
                                    // Magical pastels — soft pinks, golds, lavenders, cyans
                                    int magic = sparkRng.Next(5);
                                    if (magic == 0) { sR = 255; sG = 180; sB = 220; }      // soft pink
                                    else if (magic == 1) { sR = 255; sG = 220; sB = 130; }  // warm gold
                                    else if (magic == 2) { sR = 180; sG = 160; sB = 255; }  // lavender
                                    else if (magic == 3) { sR = 130; sG = 230; sB = 255; }  // ice cyan
                                    else { sR = 180; sG = 255; sB = 200; }                   // mint
                                }
                                using (var b = new SolidBrush(Color.FromArgb(sparkAlpha, sR, sG, sB)))
                                    g.FillEllipse(b, sparkX - sparkSize, sparkY - sparkSize, sparkSize * 2, sparkSize * 2);
                            }
                        }
                    }

                    // BOLT: extra bright flash at head
                    if (m.MeteorType == 4 && t > 0.05f && t < 0.5f)
                    {
                        int flashA = (int)(alpha * 0.4f * (1f - t * 2));
                        using (var b = new SolidBrush(Color.FromArgb(flashA, 200, 220, 255)))
                            g.FillEllipse(b, headX - glowR * 2, headY - glowR * 2, glowR * 4, glowR * 4);
                    }
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Manages shooting star animation — supports multiple simultaneous meteors.
    /// </summary>
    public class ShootingStar
    {
        public struct Meteor {
            public bool Active;
            public float Progress;
            public float StartX, StartY, DirX, DirY;
            public int CooldownMs, ElapsedMs;
            public float TrailLength, Brightness, Thickness, GlowSize, Speed, Warmth;
            // Extended variety
            public int ColorType;       // 0=white, 1=blue, 2=gold, 3=teal, 4=rose, 5=violet, 6=ember
            public int MeteorType;      // 0=streak, 1=fireball, 2=comet, 3=whisper, 4=bolt, 5=fragment, 6=twin, 7=phantom
            public float TwinOffset;    // for twin type — perpendicular offset
            public float FragAngle;     // for fragment type — burst angle
        }

        public Meteor[] Stars = new Meteor[75]; // 75 slots for dev-click meteor storm

        private static readonly Random _rng = new Random();
        private Timer _timer;
        private Action _invalidateCallback;

        public ShootingStar(Action invalidateCallback)
        {
            _invalidateCallback = invalidateCallback;
            _timer = new Timer { Interval = 16 };
            _timer.Tick += OnTick;
            for (int i = 0; i < NaturalSlots; i++)
                ScheduleNext(i);
        }

        public const int NaturalSlots = 5; // Only first 5 slots auto-spawn; rest reserved for force-launch
        public void Start() { _timer.Start(); }
        public void Stop() { _timer.Stop(); for (int i = 0; i < Stars.Length; i++) Stars[i].Active = false; }

        /// <summary>Force-launch a meteor immediately. Always succeeds.</summary>
        public void ForceLaunchMeteor()
        {
            // Find empty slot first
            for (int i = 0; i < Stars.Length; i++)
            {
                if (!Stars[i].Active) { LaunchStar(i); ForceVisible(i); return; }
            }
            // All full — overwrite the one with most progress (most faded)
            int oldest = 0; float maxProg = -1;
            for (int i = 0; i < Stars.Length; i++)
            {
                if (Stars[i].Progress > maxProg) { maxProg = Stars[i].Progress; oldest = i; }
            }
            Stars[oldest].Active = false;
            LaunchStar(oldest);
            ForceVisible(oldest);
        }

        /// <summary>Ensure a force-launched meteor starts in visible area and is bright.</summary>
        private void ForceVisible(int idx)
        {
            // Start in visible zone (center-ish), not off-screen
            // Force-launched: spread across top and right edges for full coverage
            if (_rng.NextDouble() < 0.6) {
                // Top edge — full width
                Stars[idx].StartX = (float)_rng.NextDouble();
                Stars[idx].StartY = -0.05f - (float)_rng.NextDouble() * 0.05f;
            } else {
                // Right edge — full height
                Stars[idx].StartX = 1.0f + (float)_rng.NextDouble() * 0.05f;
                Stars[idx].StartY = (float)_rng.NextDouble() * 0.7f;
            }
            Stars[idx].Progress = 0f;
            // Force high brightness so it's always visible
            Stars[idx].Brightness = Math.Max(Stars[idx].Brightness, 0.8f);
            Stars[idx].GlowSize = Math.Max(Stars[idx].GlowSize, 5f);
        }

        private void ScheduleNext(int idx)
        {
            Stars[idx].Active = false;
            Stars[idx].CooldownMs = 1000 + _rng.Next(9000);
            Stars[idx].ElapsedMs = 0;
        }

        private void LaunchStar(int idx)
        {
            Stars[idx].Active = true;
            Stars[idx].Progress = 0f;

            // Meteor shower — all from upper-right radiant toward lower-left
            // Spawn along the top and right edges so they streak across the full screen
            if (_rng.NextDouble() < 0.6) {
                // Spawn from top edge — spread across full width
                Stars[idx].StartX = 0.1f + (float)_rng.NextDouble() * 0.9f;
                Stars[idx].StartY = -0.05f - (float)_rng.NextDouble() * 0.1f;
            } else {
                // Spawn from right edge — spread down the side
                Stars[idx].StartX = 1.0f + (float)_rng.NextDouble() * 0.1f;
                Stars[idx].StartY = (float)_rng.NextDouble() * 0.6f;
            }

            // Direction: upper-right → lower-left with ±15° natural spread
            // Base angle ~225° (toward lower-left), slight random variation
            float spreadX = -0.08f + (float)_rng.NextDouble() * 0.16f; // ±8% lateral spread
            float spreadY = -0.05f + (float)_rng.NextDouble() * 0.10f; // slight vertical spread
            Stars[idx].DirX = -0.35f - (float)_rng.NextDouble() * 0.35f + spreadX;
            Stars[idx].DirY = 0.20f + (float)_rng.NextDouble() * 0.25f + spreadY;

            // Color variety — heavily biased toward white (classic streaks)
            int colorRoll = _rng.Next(100);
            if (colorRoll < 90) Stars[idx].ColorType = 0;       // 90% white (normal rock)
            else if (colorRoll < 92) Stars[idx].ColorType = 1;  // 2% blue (iron)
            else if (colorRoll < 94) Stars[idx].ColorType = 2;  // 2% gold (sodium)
            else if (colorRoll < 96) Stars[idx].ColorType = 3;  // 2% teal (copper)
            else if (colorRoll < 97) Stars[idx].ColorType = 4;  // 1% rose (lithium)
            else if (colorRoll < 98) Stars[idx].ColorType = 5;  // 1% violet (calcium)
            else Stars[idx].ColorType = 6;                       // 2% ember (magnesium)
            Stars[idx].TwinOffset = 3f + (float)_rng.NextDouble() * 5f;
            Stars[idx].FragAngle = 0f;

            // Roll meteor type and properties
            double roll = _rng.NextDouble();
            if (roll < 0.06) {
                // 6% — FIREBALL: massive, slow, long trail, dramatic glow, warm colors
                Stars[idx].MeteorType = 1;
                Stars[idx].TrailLength = 0.35f + (float)_rng.NextDouble() * 0.12f;
                Stars[idx].Brightness = 0.9f + (float)_rng.NextDouble() * 0.1f;
                Stars[idx].Thickness = 3.0f + (float)_rng.NextDouble() * 1.0f;
                Stars[idx].GlowSize = 9f + (float)_rng.NextDouble() * 4f;
                Stars[idx].Speed = 0.0108f + (float)_rng.NextDouble() * 0.0060f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.3f;
                // color set by main roll above
            } else if (roll < 0.14) {
                // 8% — COMET: medium body, very long fading tail
                Stars[idx].MeteorType = 2;
                Stars[idx].TrailLength = 0.35f + (float)_rng.NextDouble() * 0.12f;
                Stars[idx].Brightness = 0.65f + (float)_rng.NextDouble() * 0.25f;
                Stars[idx].Thickness = 1.8f + (float)_rng.NextDouble() * 0.8f;
                Stars[idx].GlowSize = 5f + (float)_rng.NextDouble() * 3f;
                Stars[idx].Speed = 0.0132f + (float)_rng.NextDouble() * 0.0072f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.3f;
            } else if (roll < 0.24) {
                // 10% — BOLT: very fast, very short, bright — like lightning
                Stars[idx].MeteorType = 4;
                Stars[idx].TrailLength = 0.10f + (float)_rng.NextDouble() * 0.06f;
                Stars[idx].Brightness = 0.8f + (float)_rng.NextDouble() * 0.2f;
                Stars[idx].Thickness = 1.2f + (float)_rng.NextDouble() * 0.6f;
                Stars[idx].GlowSize = 3f + (float)_rng.NextDouble() * 2f;
                Stars[idx].Speed = 0.0330f + (float)_rng.NextDouble() * 0.0150f;
                Stars[idx].Warmth = 0f;
                // color set by main roll above
            } else if (roll < 0.32) {
                // 8% — extra STREAK variant: medium speed, medium trail
                Stars[idx].MeteorType = 0;
                Stars[idx].TrailLength = 0.16f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.5f + (float)_rng.NextDouble() * 0.4f;
                Stars[idx].Thickness = 1.0f + (float)_rng.NextDouble() * 0.8f;
                Stars[idx].GlowSize = 3f + (float)_rng.NextDouble() * 2f;
                Stars[idx].Speed = 0.0180f + (float)_rng.NextDouble() * 0.0090f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.3f;
            } else if (roll < 0.40) {
                // 8% — FRAGMENT: splits into 2-3 pieces near end
                Stars[idx].MeteorType = 5;
                Stars[idx].TrailLength = 0.15f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.7f + (float)_rng.NextDouble() * 0.25f;
                Stars[idx].Thickness = 1.8f + (float)_rng.NextDouble() * 0.8f;
                Stars[idx].GlowSize = 5f + (float)_rng.NextDouble() * 3f;
                Stars[idx].Speed = 0.0150f + (float)_rng.NextDouble() * 0.0072f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.3f;
                // color set by main roll above
            } else if (roll < 0.50) {
                // 10% — PHANTOM: very dim, slow, ethereal — barely there
                Stars[idx].MeteorType = 7;
                Stars[idx].TrailLength = 0.18f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.2f + (float)_rng.NextDouble() * 0.15f;
                Stars[idx].Thickness = 0.8f + (float)_rng.NextDouble() * 0.5f;
                Stars[idx].GlowSize = 6f + (float)_rng.NextDouble() * 4f;
                Stars[idx].Speed = 0.0072f + (float)_rng.NextDouble() * 0.0048f;
                Stars[idx].Warmth = 0f;
                // color set by main roll above
            } else if (roll < 0.72) {
                // 22% — WHISPER: dim quick streak
                Stars[idx].MeteorType = 3;
                Stars[idx].TrailLength = 0.06f + (float)_rng.NextDouble() * 0.05f;
                Stars[idx].Brightness = 0.3f + (float)_rng.NextDouble() * 0.25f;
                Stars[idx].Thickness = 0.8f + (float)_rng.NextDouble() * 0.5f;
                Stars[idx].GlowSize = 2f + (float)_rng.NextDouble() * 2f;
                Stars[idx].Speed = 0.0240f + (float)_rng.NextDouble() * 0.0120f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.2f;
            } else {
                // 28% — STREAK: standard medium meteor
                Stars[idx].MeteorType = 0;
                Stars[idx].TrailLength = 0.10f + (float)_rng.NextDouble() * 0.10f;
                Stars[idx].Brightness = 0.5f + (float)_rng.NextDouble() * 0.4f;
                Stars[idx].Thickness = 1.4f + (float)_rng.NextDouble() * 1.0f;
                Stars[idx].GlowSize = 3f + (float)_rng.NextDouble() * 4f;
                Stars[idx].Speed = 0.0180f + (float)_rng.NextDouble() * 0.0108f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.4f;
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            bool needRepaint = false;
            for (int i = 0; i < Stars.Length; i++)
            {
                Stars[i].ElapsedMs += _timer.Interval;
                if (!Stars[i].Active && i < NaturalSlots)
                {
                    if (Stars[i].ElapsedMs >= Stars[i].CooldownMs)
                    { LaunchStar(i); needRepaint = true; }
                }
                else
                {
                    Stars[i].Progress += Stars[i].Speed * _timer.Interval / 30f;
                    if (Stars[i].Progress >= 1f)
                        ScheduleNext(i);
                    needRepaint = true;
                }
            }
            if (needRepaint) try { _invalidateCallback?.Invoke(); } catch { }
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer?.Dispose();
        }

        // Color lookup by type
        public static void GetMeteorColor(int colorType, float warmth, out int cR, out int cG, out int cB)
        {
            switch (colorType)
            {
                case 1: // Blue — icy cold
                    cR = 140; cG = 180; cB = 255; break;
                case 2: // Gold — warm rich
                    cR = 255; cG = 210; cB = 120; break;
                case 3: // Teal — alien aurora
                    cR = 100; cG = 230; cB = 210; break;
                case 4: // Rose — soft pink
                    cR = 240; cG = 160; cB = 200; break;
                case 5: // Violet — deep space
                    cR = 180; cG = 140; cB = 255; break;
                case 6: // Ember — hot orange-red
                    cR = 255; cG = 150; cB = 80; break;
                default: // White — pure clean white with very subtle cool tint
                    cR = 240 + (int)(warmth * 15);
                    cG = 240 + (int)(warmth * 10);
                    cB = 255;
                    break;
            }
        }
    }

    /// <summary>Dark-themed message dialog replacement for MessageBox.</summary>
    public static class DarkMessage
    {
        public static void Show(string text, string title)
        {
            int w = Dpi.S(420);
            int bodyH;
            using (var mf = new Font("Segoe UI", 9.5f))
            using (var tmp = Graphics.FromHwnd(IntPtr.Zero)) {
                var ts = tmp.MeasureString(text, mf, w - Dpi.S(48));
                bodyH = Math.Max(Dpi.S(32), (int)ts.Height + Dpi.S(8));
            }
            int headerH = Dpi.S(42);
            int btnH = Dpi.S(34);
            int bodyZone = Math.Max(bodyH + Dpi.S(16), Dpi.S(52)); // minimum comfortable zone
            int h = headerH + bodyZone + btnH + Dpi.S(16);

            var dlg = new Form {
                Text = title, FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterScreen, ShowInTaskbar = false,
                BackColor = DarkTheme.BG, ForeColor = DarkTheme.Txt,
                ClientSize = new Size(w, h), Font = new Font("Segoe UI", 9.5f),
                TopMost = true, KeyPreview = true
            };

            DarkTheme.ApplyRoundedRegion(dlg, Dpi.S(12));
            dlg.Resize += (s, e) => DarkTheme.ApplyRoundedRegion(dlg, Dpi.S(12));
            dlg.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) dlg.Close(); };

            bool dragging = false; Point dragOff = Point.Empty;

            dlg.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // Background gradient
                using (var gb = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, h),
                    Color.FromArgb(26, 26, 28), Color.FromArgb(14, 14, 16)))
                    g.FillRectangle(gb, 0, 0, w, h);

                // Starfield
                DarkTheme.PaintStars(g, w, h, 7777);

                // Accent glow at top (soft gradient fade)
                using (var gb = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, Dpi.S(6)),
                    Color.FromArgb(60, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B),
                    Color.FromArgb(0, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B)))
                    g.FillRectangle(gb, 0, 0, w, Dpi.S(6));

                // Accent stripe
                using (var p = new Pen(DarkTheme.Accent, Dpi.S(2)))
                    g.DrawLine(p, Dpi.S(12), 1, w - Dpi.S(12), 1);

                // Title bar tint — slightly darker so it reads as a draggable header
                using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    g.FillRectangle(b, 0, 0, w, headerH);

                // Title
                int ty = Dpi.S(14);
                using (var f = new Font("Segoe UI Semibold", 10.5f))
                using (var b = new SolidBrush(DarkTheme.Accent))
                    g.DrawString(title, f, b, Dpi.S(22), ty);

                // Header separator
                int sepY = headerH;
                using (var p = new Pen(Color.FromArgb(36, 36, 36)))
                    g.DrawLine(p, Dpi.S(22), sepY, w - Dpi.S(22), sepY);

                // Footer separator
                int footY = headerH + bodyZone;
                using (var p = new Pen(Color.FromArgb(32, 32, 32)))
                    g.DrawLine(p, Dpi.S(22), footY, w - Dpi.S(22), footY);

                // Body text — vertically centered between the two separators
                using (var f = new Font("Segoe UI", 9.5f))
                using (var b = new SolidBrush(DarkTheme.Txt2)) {
                    float textY = sepY + (bodyZone - bodyH) / 2f;
                    var rect = new RectangleF(Dpi.S(22), textY, w - Dpi.S(44), bodyH);
                    g.DrawString(text, f, b, rect);
                }

                // Rounded border
                using (var rr = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), Dpi.S(12)))
                using (var p = new Pen(Color.FromArgb(48, 48, 48)))
                    g.DrawPath(p, rr);
            };

            dlg.MouseDown += (s, e) => { dragging = true; dragOff = e.Location; };
            dlg.MouseMove += (s, e) => { if (dragging) dlg.Location = new Point(dlg.Left + e.X - dragOff.X, dlg.Top + e.Y - dragOff.Y); };
            dlg.MouseUp += (s, e) => { dragging = false; };

            // OK button — rounded with accent color
            int btnW = Dpi.S(80);
            var btn = new Button { Text = "OK", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat,
                Size = new Size(btnW, btnH),
                BackColor = DarkTheme.Accent, ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(w - btnW - Dpi.S(22), h - btnH - Dpi.S(12)) };
            btn.FlatAppearance.BorderSize = 0;
            using (var btnPath = DarkTheme.RoundedRect(new Rectangle(0, 0, btnW, btnH), Dpi.S(6)))
                btn.Region = new Region(btnPath);
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(
                Math.Min(255, DarkTheme.Accent.R + 30),
                Math.Min(255, DarkTheme.Accent.G + 30),
                Math.Min(255, DarkTheme.Accent.B + 30));
            btn.MouseLeave += (s, e) => btn.BackColor = DarkTheme.Accent;
            dlg.Controls.Add(btn);
            dlg.AcceptButton = btn;

            dlg.ShowDialog();
            dlg.Dispose();
        }

    }

    // =========================================================================
    // Celestial Events — rare spectacular background animations
    // =========================================================================
    public class CelestialEvents
    {
        public enum EventType { 
            Supernova, UFO, NyanCat, Astronaut, Eclipse, Constellation, Wormhole, BinaryStars,
            Rocket, Satellite, WishingStar, Planet, AlienDogfight,
            // New events
            Comet, SpaceWhale, Firefly, Pulsar, Nebula, SpaceJellyfish,
            MeteorShower, GhostShip, StarPhoenix, Blackhole, AuroraWave,
            SpaceTrain, CometCluster, Orb, LaserGrid, SpaceDolphin,
            Stardust, SolarFlare, SpaceButterfly, Lighthouse, Unicorn,
            Stargate, TimeTraveler, SpacePirate, CrystalDragon, QuantumRift,
            CosmicDancer, PlasmaSnake, StarSurfer, VoidMoth, NeonJellyfish,
            GalaxySpiral, MagicCarpet, SpaceLantern, CosmicOwl, WarpDrive,
            SpaceKoi, CelestialHarp, MeteorDragon, NorthernLights, DarkMatter
        }

        public struct CelestialEvent
        {
            public bool Active;
            public EventType Type;
            public float Progress;   // 0→1
            public float X, Y;       // normalized position
            public float DirX, DirY; // normalized direction
            public float Speed;
            public float Param1, Param2, Param3; // type-specific
            public int Seed;         // deterministic random for stable sparkles
        }

        public CelestialEvent[] Events = new CelestialEvent[75];
        public CelestialEvent ActiveEvent { get { for(int i=0;i<Events.Length;i++) if(Events[i].Active) return Events[i]; return new CelestialEvent(); } }
        private int _cooldownMs, _elapsedMs;
        private static readonly Random _rng = new Random();
        private Timer _timer;
        private Action _invalidateCallback;

        public CelestialEvents(Action invalidateCallback)
        {
            _invalidateCallback = invalidateCallback;
            _cooldownMs = 5000 + _rng.Next(10000);
            _elapsedMs = 0;
            _timer = new Timer { Interval = 16 };
            _timer.Tick += OnTick;
        }

        public const int NaturalSlots = 1; // Slot 0 auto-spawns; rest reserved for force-launch
        public void Start() { _timer.Start(); }
        public void Stop() { _timer.Stop(); for(int i=0;i<Events.Length;i++) Events[i].Active=false; }

        /// <summary>Force-launch a random event immediately into any free slot.</summary>
        public bool ForceLaunch()
        {
            for(int i=0;i<Events.Length;i++){if(!Events[i].Active){LaunchEvent(i);return true;}}
            // All full — overwrite most progressed
            int oldest=0;float maxP=-1;for(int i=0;i<Events.Length;i++){if(Events[i].Progress>maxP){maxP=Events[i].Progress;oldest=i;}}
            Events[oldest].Active=false;LaunchEvent(oldest);return true;
        }

        void ScheduleNext()
        {
            Events[0].Active = false;
            _cooldownMs = 10000 + _rng.Next(10000);
            _elapsedMs = 0;
        }


        /// <summary>Randomize entry edge and direction for traversing events.</summary>
        private int _li; // current launch index
        void RandomTraversal(float ySpread = 0.7f, float yOffset = 0.15f)
        {
            int edge = _rng.Next(4);
            float pos = yOffset + (float)_rng.NextDouble() * ySpread;
            float slight = -0.15f + (float)_rng.NextDouble() * 0.3f;
            switch (edge)
            {
                case 0: Events[_li].X = -0.15f; Events[_li].Y = pos; Events[_li].DirX = 1.3f; Events[_li].DirY = slight; break;
                case 1: Events[_li].X = 1.15f; Events[_li].Y = pos; Events[_li].DirX = -1.3f; Events[_li].DirY = slight; break;
                case 2: Events[_li].X = pos; Events[_li].Y = -0.15f; Events[_li].DirX = slight; Events[_li].DirY = 1.3f; break;
                case 3: Events[_li].X = pos; Events[_li].Y = 1.15f; Events[_li].DirX = slight; Events[_li].DirY = -1.3f; break;
            }
        }

        void LaunchEvent(int idx = 0)
        {
            _li = idx;
            Events[_li].Active = true;
            Events[_li].Progress = 0f;
            Events[_li].Seed = _rng.Next(100000);

            EventType[] allTypes = (EventType[])Enum.GetValues(typeof(EventType));
            Events[_li].Type = allTypes[_rng.Next(allTypes.Length)];

            switch (Events[_li].Type)
            {
                case EventType.Supernova:
                    Events[_li].X = 0.15f + (float)_rng.NextDouble() * 0.7f;
                    Events[_li].Y = 0.15f + (float)_rng.NextDouble() * 0.7f;
                    Events[_li].Speed = 0.012f + (float)_rng.NextDouble() * 0.005f;
                    break;
                case EventType.UFO:
                    RandomTraversal(0.5f, 0.1f);
                    Events[_li].Speed = 0.006f + (float)_rng.NextDouble() * 0.003f;
                    Events[_li].Param1 = (float)_rng.NextDouble(); // beam wobble phase
                    break;
                case EventType.NyanCat:
                    RandomTraversal(0.6f, 0.2f);
                    Events[_li].Speed = 0.008f + (float)_rng.NextDouble() * 0.004f;
                    break;
                case EventType.Astronaut:
                    RandomTraversal(0.4f, 0.3f);
                    Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f;
                    Events[_li].Param1 = 0; // tumble angle
                    break;
                case EventType.Eclipse:
                    RandomTraversal(0.4f, 0.2f);
                    Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f;
                    break;
                case EventType.Constellation:
                    Events[_li].X = 0.15f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Y = 0.15f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.010f;
                    Events[_li].Param1 = (float)_rng.NextDouble() * 360; // rotation
                    break;
                case EventType.Wormhole:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.012f + (float)_rng.NextDouble() * 0.004f;
                    break;
                case EventType.BinaryStars:
                    RandomTraversal();
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f;
                    Events[_li].Param1 = 0; // orbit angle
                    Events[_li].Param2 = 8f + (float)_rng.NextDouble() * 8f; // orbit radius
                    break;
                case EventType.Rocket:
                    RandomTraversal(0.4f, 0.3f);
                    Events[_li].Speed = 0.008f + (float)_rng.NextDouble() * 0.004f;
                    Events[_li].Param1 = 0; // flame flicker phase
                    break;
                case EventType.Satellite:
                    RandomTraversal(0.3f, 0.15f);
                    Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f;
                    Events[_li].Param1 = 0; // solar panel rotation
                    Events[_li].Param2 = (float)(_rng.NextDouble() * 360); // blink phase
                    break;
                case EventType.WishingStar:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Y = 0.1f + (float)_rng.NextDouble() * 0.5f;
                    Events[_li].Speed = 0.008f;
                    break;
                case EventType.Planet:
                    RandomTraversal(0.4f, 0.3f);
                    Events[_li].Speed = 0.002f + (float)_rng.NextDouble() * 0.001f;
                    Events[_li].Param1 = _rng.Next(4); // planet type: 0=ringed, 1=gas giant, 2=earth-like, 3=ice
                    break;
                case EventType.AlienDogfight:
                    RandomTraversal(0.5f, 0.2f);
                    Events[_li].Speed = 0.006f + (float)_rng.NextDouble() * 0.003f;
                    Events[_li].Param1 = 0; // chase wobble phase
                    Events[_li].Param2 = 0; // laser cooldown
                    Events[_li].Param3 = (float)_rng.NextDouble(); // laser random seed
                    break;
                // --- New events ---
                case EventType.Comet:
                    RandomTraversal();
                    Events[_li].Speed = 0.010f + (float)_rng.NextDouble() * 0.005f;
                    Events[_li].Param1 = 4f + (float)_rng.NextDouble() * 6f; // tail length
                    break;
                case EventType.SpaceWhale:
                    RandomTraversal(0.5f, 0.2f);
                    Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f;
                    Events[_li].Param1 = 0; // undulation phase
                    break;
                case EventType.Firefly:
                    Events[_li].X = 0.1f + (float)_rng.NextDouble() * 0.8f;
                    Events[_li].Y = 0.1f + (float)_rng.NextDouble() * 0.8f;
                    Events[_li].Speed = 0.012f;
                    Events[_li].Param1 = _rng.Next(5, 12); // number of fireflies
                    Events[_li].Seed = _rng.Next(100000);
                    break;
                case EventType.Pulsar:
                    Events[_li].X = 0.15f + (float)_rng.NextDouble() * 0.7f;
                    Events[_li].Y = 0.15f + (float)_rng.NextDouble() * 0.7f;
                    Events[_li].Speed = 0.014f;
                    Events[_li].Param1 = (float)_rng.NextDouble() * 360; // beam angle
                    break;
                case EventType.Nebula:
                    Events[_li].X = 0.15f + (float)_rng.NextDouble() * 0.7f;
                    Events[_li].Y = 0.15f + (float)_rng.NextDouble() * 0.7f;
                    Events[_li].Speed = 0.008f;
                    Events[_li].Param1 = _rng.Next(3); // color scheme
                    break;
                case EventType.SpaceJellyfish:
                    RandomTraversal(0.5f, 0.2f);
                    Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f;
                    Events[_li].Param1 = 0; // tentacle phase
                    break;
                case EventType.MeteorShower:
                    Events[_li].X = 0.3f + (float)_rng.NextDouble() * 0.4f;
                    Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.3f;
                    Events[_li].Speed = 0.010f;
                    Events[_li].Seed = _rng.Next(100000);
                    break;
                case EventType.GhostShip:
                    RandomTraversal(0.4f, 0.3f);
                    Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f;
                    Events[_li].Param1 = 0; // flicker phase
                    break;
                case EventType.StarPhoenix:
                    RandomTraversal(0.5f, 0.2f);
                    Events[_li].Speed = 0.007f + (float)_rng.NextDouble() * 0.003f;
                    Events[_li].Param1 = 0; // wing flap phase
                    break;
                case EventType.Blackhole:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.010f;
                    break;
                case EventType.AuroraWave:
                    Events[_li].X = 0.5f;
                    Events[_li].Y = 0.3f + (float)_rng.NextDouble() * 0.4f;
                    Events[_li].Speed = 0.010f;
                    Events[_li].Param1 = _rng.Next(3); // color: 0=green, 1=purple, 2=blue
                    break;
                case EventType.SpaceTrain:
                    RandomTraversal(0.6f, 0.15f);
                    Events[_li].Speed = 0.006f + (float)_rng.NextDouble() * 0.003f;
                    Events[_li].Param1 = 3 + _rng.Next(5); // number of cars
                    break;
                case EventType.CometCluster:
                    RandomTraversal();
                    Events[_li].Speed = 0.008f + (float)_rng.NextDouble() * 0.004f;
                    Events[_li].Seed = _rng.Next(100000);
                    break;
                case EventType.Orb:
                    RandomTraversal(0.6f, 0.2f);
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f;
                    Events[_li].Param1 = _rng.Next(4); // color type
                    break;
                case EventType.LaserGrid:
                    Events[_li].X = 0.5f; Events[_li].Y = 0.5f;
                    Events[_li].Speed = 0.012f;
                    Events[_li].Param1 = (float)_rng.NextDouble() * 360; // rotation
                    break;
                case EventType.SpaceDolphin:
                    RandomTraversal(0.5f, 0.2f);
                    Events[_li].Speed = 0.006f + (float)_rng.NextDouble() * 0.003f;
                    Events[_li].Param1 = 0; // leap phase
                    break;
                case EventType.Stardust:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.010f;
                    Events[_li].Seed = _rng.Next(100000);
                    break;
                case EventType.SolarFlare:
                    int flareEdge = _rng.Next(4);
                    Events[_li].X = flareEdge < 2 ? (flareEdge == 0 ? -0.02f : 1.02f) : (float)_rng.NextDouble();
                    Events[_li].Y = flareEdge >= 2 ? (flareEdge == 2 ? -0.02f : 1.02f) : (float)_rng.NextDouble();
                    Events[_li].Speed = 0.014f;
                    Events[_li].Param1 = _rng.Next(2); // 0=orange, 1=blue
                    break;
                case EventType.SpaceButterfly:
                    RandomTraversal(0.5f, 0.2f);
                    Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.003f;
                    Events[_li].Param1 = 0; // wing phase
                    break;
                case EventType.Lighthouse:
                    Events[_li].X = 0.15f + (float)_rng.NextDouble() * 0.7f;
                    Events[_li].Y = 0.15f + (float)_rng.NextDouble() * 0.7f;
                    Events[_li].Speed = 0.012f;
                    Events[_li].Param1 = (float)_rng.NextDouble() * 360; // beam start angle
                    break;
                case EventType.Unicorn:
                    RandomTraversal(0.5f, 0.2f);
                    Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; // slow — majestic
                    Events[_li].Param1 = 0;
                    break;
                // Wave 3 events — all traversal
                case EventType.Stargate:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.010f; break;
                case EventType.TimeTraveler:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.007f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.SpacePirate:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.006f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.CrystalDragon:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.QuantumRift:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.012f; break;
                case EventType.CosmicDancer:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.006f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.PlasmaSnake:
                    RandomTraversal(0.6f, 0.15f); Events[_li].Speed = 0.007f + (float)_rng.NextDouble() * 0.004f; break;
                case EventType.StarSurfer:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.008f + (float)_rng.NextDouble() * 0.004f; break;
                case EventType.VoidMoth:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.NeonJellyfish:
                    RandomTraversal(0.4f, 0.3f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.GalaxySpiral:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.008f; Events[_li].Param1 = 0; break;
                case EventType.MagicCarpet:
                    RandomTraversal(0.4f, 0.3f); Events[_li].Speed = 0.006f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.SpaceLantern:
                    Events[_li].X = 0.1f + (float)_rng.NextDouble() * 0.8f; Events[_li].Y = 1.1f;
                    Events[_li].DirX = -0.1f + (float)_rng.NextDouble() * 0.2f; Events[_li].DirY = -1.2f;
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.CosmicOwl:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.WarpDrive:
                    Events[_li].X = 0.3f + (float)_rng.NextDouble() * 0.4f; Events[_li].Y = 0.3f + (float)_rng.NextDouble() * 0.4f;
                    Events[_li].Speed = 0.015f; break;
                case EventType.SpaceKoi:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.CelestialHarp:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.010f; break;
                case EventType.MeteorDragon:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.007f + (float)_rng.NextDouble() * 0.004f; break;
                case EventType.NorthernLights:
                    Events[_li].X = 0.5f; Events[_li].Y = 0.3f;
                    Events[_li].Speed = 0.006f; break;
                case EventType.DarkMatter:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.010f; break;
            }
        }

        void OnTick(object sender, EventArgs e)
        {
            _elapsedMs += _timer.Interval;
            // Auto-spawn in slot 0 only
            if (!Events[0].Active)
            {
                if (_elapsedMs >= _cooldownMs) LaunchEvent(0);
            }

            bool anyActive = false;
            float dt = _timer.Interval / 30f;
            for (int i = 0; i < Events.Length; i++)
            {
                if (!Events[i].Active) continue;
                anyActive = true;
                Events[i].Progress += Events[i].Speed * dt;
                if (Events[i].Type == EventType.Astronaut) Events[i].Param1 += 2.5f * dt;
                if (Events[i].Type == EventType.BinaryStars) Events[i].Param1 += 4f * dt;
                if (Events[i].Type == EventType.UFO) Events[i].Param1 += 0.08f * dt;
                if (Events[i].Type == EventType.Rocket) Events[i].Param1 += 0.15f * dt;
                if (Events[i].Type == EventType.Satellite) { Events[i].Param1 += 1.5f * dt; Events[i].Param2 += 3f * dt; }
                if (Events[i].Type == EventType.AlienDogfight) { Events[i].Param1 += 0.1f * dt; Events[i].Param2 += 0.03f * dt; }
                if (Events[i].Type == EventType.SpaceWhale) Events[i].Param1 += 0.1f * dt;
                if (Events[i].Type == EventType.SpaceJellyfish) Events[i].Param1 += 0.12f * dt;
                if (Events[i].Type == EventType.StarPhoenix) Events[i].Param1 += 0.15f * dt;
                if (Events[i].Type == EventType.SpaceButterfly) Events[i].Param1 += 0.12f * dt;
                if (Events[i].Type == EventType.SpaceDolphin) Events[i].Param1 += 0.1f * dt;
                if (Events[i].Type == EventType.Unicorn) Events[i].Param1 += 0.1f * dt;
                if (Events[i].Progress >= 1f) { Events[i].Active = false; if (i == 0) ScheduleNext(); }
            }
            if (anyActive || !Events[0].Active) try { _invalidateCallback?.Invoke(); } catch { }
        }

        public void Dispose() { _timer?.Stop(); _timer?.Dispose(); }
    }

    public static partial class DarkTheme
    {
        public static void DrawShield(Graphics g, float x, float y, float sz, Color fill, bool active) {
            float s = sz / 20f;
            var path = new GraphicsPath();
            path.AddLine(x + 10*s, y, x + 18*s, y + 4*s);
            path.AddLine(x + 18*s, y + 4*s, x + 18*s, y + 10*s);
            path.AddBezier(x + 18*s, y + 10*s, x + 17*s, y + 15*s, x + 12*s, y + 18*s, x + 10*s, y + 20*s);
            path.AddBezier(x + 10*s, y + 20*s, x + 8*s, y + 18*s, x + 3*s, y + 15*s, x + 2*s, y + 10*s);
            path.AddLine(x + 2*s, y + 10*s, x + 2*s, y + 4*s);
            path.CloseFigure();
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using (var p = new Pen(Color.FromArgb(active ? 50 : 20, 255, 255, 255), 0.8f * s)) g.DrawPath(p, path);
            if (active) {
                using (var p = new Pen(Color.White, 1.6f * s)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, x + 6.5f*s, y + 10.5f*s, x + 9*s, y + 13.5f*s);
                    g.DrawLine(p, x + 9*s, y + 13.5f*s, x + 14*s, y + 7*s);
                }
            }
            path.Dispose();
        }

        public static void PaintCelestialEvent(Graphics g, int w, int h, CelestialEvents ce)
        {
            if (w <= 0 || h <= 0 || ce == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (int ei = 0; ei < ce.Events.Length; ei++)
            {
                var ev = ce.Events[ei];
                if (!ev.Active) continue;
                float t = ev.Progress;

                float fade = 1f;
                if (t < 0.10f) fade = t / 0.10f;
                if (t > 0.85f) fade = (1f - t) / 0.15f;
                fade = Math.Max(0, Math.Min(1, fade));
                if (fade <= 0) continue;

            try
            {
                switch (ev.Type)
                {
                    case CelestialEvents.EventType.Supernova:
                        PaintSupernova(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.UFO:
                        PaintUFO(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.NyanCat:
                        PaintNyanCat(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Astronaut:
                        PaintAstronaut(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Eclipse:
                        PaintEclipse(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Constellation:
                        PaintConstellation(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Wormhole:
                        PaintWormhole(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.BinaryStars:
                        PaintBinaryStars(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Rocket:
                        PaintRocket(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Satellite:
                        PaintSatellite(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.WishingStar:
                        PaintWishingStar(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Planet:
                        PaintPlanet(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.AlienDogfight:
                        PaintAlienDogfight(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Comet:
                    case CelestialEvents.EventType.CometCluster:
                        PaintComet(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.SpaceWhale:
                        PaintSpaceWhale(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Firefly:
                        PaintFirefly(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Pulsar:
                        PaintPulsar(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Nebula:
                        PaintNebula(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.SpaceJellyfish:
                        PaintSpaceJellyfish(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.MeteorShower:
                        PaintMeteorShower(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.GhostShip:
                        PaintGhostShip(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.StarPhoenix:
                        PaintStarPhoenix(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Blackhole:
                        PaintBlackhole(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.AuroraWave:
                        PaintAuroraWave(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.SpaceTrain:
                        PaintSpaceTrain(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Orb:
                        PaintOrb(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.LaserGrid:
                        PaintLaserGrid(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.SpaceDolphin:
                        PaintSpaceDolphin(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Stardust:
                        PaintStardust(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.SolarFlare:
                        PaintSolarFlare(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.SpaceButterfly:
                        PaintSpaceButterfly(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Lighthouse:
                        PaintLighthouse(g, w, h, ev, t, fade);
                        break;
                    case CelestialEvents.EventType.Unicorn:
                        PaintUnicorn(g, w, h, ev, t, fade);
                        break;
                    // Wave 3
                    case CelestialEvents.EventType.Stargate: PaintStargate(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.TimeTraveler: PaintTimeTraveler(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpacePirate: PaintSpacePirate(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.CrystalDragon: PaintCrystalDragon(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.QuantumRift: PaintQuantumRift(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.CosmicDancer: PaintCosmicDancer(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.PlasmaSnake: PaintPlasmaSnake(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.StarSurfer: PaintStarSurfer(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.VoidMoth: PaintVoidMoth(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.NeonJellyfish: PaintNeonJellyfish(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.GalaxySpiral: PaintGalaxySpiral(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.MagicCarpet: PaintMagicCarpet(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpaceLantern: PaintSpaceLantern(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.CosmicOwl: PaintCosmicOwl(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.WarpDrive: PaintWarpDrive(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpaceKoi: PaintSpaceKoi(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.CelestialHarp: PaintCelestialHarp(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.MeteorDragon: PaintMeteorDragon(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.NorthernLights: PaintNorthernLights(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.DarkMatter: PaintDarkMatter(g, w, h, ev, t, fade); break;
                }
            }
            catch { }
            } // end for each event
        }

        static void PaintSupernova(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;

            // Phase 1 (0-0.15): bright point builds
            // Phase 2 (0.15-0.35): EXPLOSION — massive flash
            // Phase 3 (0.35-1.0): expanding ring fades out
            if (t < 0.15f)
            {
                float bt = t / 0.15f;
                int a = (int)(180 * bt * fade);
                float r = 2 + bt * 4;
                using (var b = new SolidBrush(Color.FromArgb(a, 255, 250, 220)))
                    g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
            }
            else if (t < 0.35f)
            {
                float bt = (t - 0.15f) / 0.20f;
                // Flash: starts huge and bright, shrinks
                float flashR = (30 + bt * 60) * (1f - bt * 0.5f);
                int flashA = (int)(120 * (1f - bt) * fade);
                // White-hot center
                using (var b = new SolidBrush(Color.FromArgb(flashA, 255, 255, 255)))
                    g.FillEllipse(b, cx - flashR * 0.4f, cy - flashR * 0.4f, flashR * 0.8f, flashR * 0.8f);
                // Gold bloom
                using (var b = new SolidBrush(Color.FromArgb(flashA / 2, 255, 220, 120)))
                    g.FillEllipse(b, cx - flashR, cy - flashR, flashR * 2, flashR * 2);
                // Outer red bloom
                using (var b = new SolidBrush(Color.FromArgb(flashA / 4, 255, 140, 80)))
                    g.FillEllipse(b, cx - flashR * 1.5f, cy - flashR * 1.5f, flashR * 3, flashR * 3);

                // Ejecta rays
                var rng = new Random(ev.Seed);
                int rays = 8;
                for (int i = 0; i < rays; i++)
                {
                    float angle = (float)(rng.NextDouble() * Math.PI * 2);
                    float len = flashR * (0.8f + (float)rng.NextDouble() * 1.2f) * bt;
                    float ex = cx + (float)Math.Cos(angle) * len;
                    float ey = cy + (float)Math.Sin(angle) * len;
                    int ra = (int)(100 * (1f - bt) * fade);
                    using (var p = new Pen(Color.FromArgb(ra, 255, 230, 160), 1.5f))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        g.DrawLine(p, cx, cy, ex, ey);
                    }
                }
            }
            else
            {
                float bt = (t - 0.35f) / 0.65f;
                // Expanding ring
                float ringR = 40 + bt * 80;
                float ringThick = Math.Max(0.5f, 3f * (1f - bt));
                int ringA = (int)(80 * (1f - bt) * fade);
                using (var p = new Pen(Color.FromArgb(ringA, 255, 200, 140), ringThick))
                    g.DrawEllipse(p, cx - ringR, cy - ringR, ringR * 2, ringR * 2);
                // Inner fading glow
                float glowR = 10 * (1f - bt);
                int glowA = (int)(60 * (1f - bt) * fade);
                using (var b = new SolidBrush(Color.FromArgb(glowA, 255, 240, 200)))
                    g.FillEllipse(b, cx - glowR, cy - glowR, glowR * 2, glowR * 2);
            }
        }

        static void PaintUFO(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w;
            float cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 25) * 3;
            cy += bob;
            int a = (int)(200 * fade);

            // Saucer body: dark ellipse with metallic rim
            float bw = 18, bh = 7;
            using (var b = new SolidBrush(Color.FromArgb(a, 50, 55, 65)))
                g.FillEllipse(b, cx - bw / 2, cy - bh / 2, bw, bh);
            using (var p = new Pen(Color.FromArgb(a, 140, 160, 180), 1.2f))
                g.DrawEllipse(p, cx - bw / 2, cy - bh / 2, bw, bh);

            // Dome on top
            float dw = 8, dh = 5;
            using (var b = new SolidBrush(Color.FromArgb(a * 2 / 3, 100, 200, 220)))
                g.FillEllipse(b, cx - dw / 2, cy - bh / 2 - dh * 0.6f, dw, dh);

            // Blinking lights along rim
            float blink = (float)Math.Sin(ev.Param1 * 6) * 0.5f + 0.5f;
            int lA = (int)(180 * blink * fade);
            float lR = 1.5f;
            using (var b = new SolidBrush(Color.FromArgb(lA, 100, 255, 150)))
            {
                g.FillEllipse(b, cx - bw / 2 + 2, cy - 1, lR * 2, lR * 2);
                g.FillEllipse(b, cx + bw / 2 - 4, cy - 1, lR * 2, lR * 2);
            }
            int lA2 = (int)(180 * (1f - blink) * fade);
            using (var b = new SolidBrush(Color.FromArgb(lA2, 255, 100, 100)))
                g.FillEllipse(b, cx - 1, cy - 1, lR * 2, lR * 2);

            // Tractor beam
            float beamWobble = (float)Math.Sin(ev.Param1 * 3) * 4;
            int beamA = (int)(40 * fade);
            PointF[] beam = {
                new PointF(cx - 4, cy + bh / 2),
                new PointF(cx + 4, cy + bh / 2),
                new PointF(cx + 12 + beamWobble, cy + 45),
                new PointF(cx - 12 + beamWobble, cy + 45)
            };
            using (var b = new SolidBrush(Color.FromArgb(beamA, 100, 220, 255)))
                g.FillPolygon(b, beam);
            // Beam highlight center
            PointF[] beamInner = {
                new PointF(cx - 2, cy + bh / 2),
                new PointF(cx + 2, cy + bh / 2),
                new PointF(cx + 5 + beamWobble, cy + 40),
                new PointF(cx - 5 + beamWobble, cy + 40)
            };
            using (var b = new SolidBrush(Color.FromArgb(beamA / 2, 200, 240, 255)))
                g.FillPolygon(b, beamInner);
        }

        static void PaintNyanCat(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w;
            float cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 30) * 2;
            cy += bob;
            int a = (int)(220 * fade);

            // Rainbow trail — 6 color bands behind the cat
            Color[] rainbow = {
                Color.FromArgb(a / 2, 255, 50, 50),    // red
                Color.FromArgb(a / 2, 255, 150, 50),   // orange
                Color.FromArgb(a / 2, 255, 255, 50),   // yellow
                Color.FromArgb(a / 2, 50, 255, 50),    // green
                Color.FromArgb(a / 2, 50, 150, 255),   // blue
                Color.FromArgb(a / 2, 150, 50, 255),   // purple
            };
            float bandH = 2f;
            float trailLen = 60 + t * 30;
            float trailStart = cy - bandH * 3;
            for (int i = 0; i < 6; i++)
            {
                float by = trailStart + i * bandH;
                float waveBob = (float)Math.Sin(t * 30 + i * 0.5f) * 1.5f;
                using (var p = new Pen(rainbow[i], bandH))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx - trailLen, by + waveBob, cx - 4, by + bob);
                }
            }

            // Cat body: pop-tart rectangle (tiny!)
            float bw = 10, bh = 8;
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 200, 150)))
                g.FillRectangle(b, cx - bw / 2, cy - bh / 2 + bob, bw, bh);
            // Frosting
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 130, 160)))
                g.FillRectangle(b, cx - bw / 2 + 1, cy - bh / 2 + 1 + bob, bw - 2, bh - 2);
            // Cat face (dark)
            using (var b = new SolidBrush(Color.FromArgb(a, 80, 80, 80)))
            {
                // Head
                g.FillEllipse(b, cx + bw / 2 - 1, cy - 3 + bob, 6, 6);
                // Ears
                g.FillPolygon(b, new[] {
                    new PointF(cx + bw / 2, cy - 3 + bob),
                    new PointF(cx + bw / 2 + 1, cy - 6 + bob),
                    new PointF(cx + bw / 2 + 3, cy - 2 + bob)
                });
                g.FillPolygon(b, new[] {
                    new PointF(cx + bw / 2 + 3, cy - 3 + bob),
                    new PointF(cx + bw / 2 + 5, cy - 6 + bob),
                    new PointF(cx + bw / 2 + 6, cy - 1 + bob)
                });
            }
            // Eyes
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
            {
                g.FillEllipse(b, cx + bw / 2 + 1, cy - 1 + bob, 2, 2);
                g.FillEllipse(b, cx + bw / 2 + 3, cy - 1 + bob, 2, 2);
            }

            // Sparkles around the cat
            var rng = new Random(ev.Seed + (int)(t * 15));
            for (int i = 0; i < 8; i++)
            {
                float sx = cx + (float)(rng.NextDouble() - 0.5) * 50;
                float sy = cy + (float)(rng.NextDouble() - 0.5) * 30 + bob;
                float ss = 1f + (float)rng.NextDouble() * 2f;
                int sa = (int)(120 * fade * (float)rng.NextDouble());
                using (var b = new SolidBrush(Color.FromArgb(sa, 255, 255, 255)))
                    g.FillEllipse(b, sx - ss, sy - ss, ss * 2, ss * 2);
            }
        }

        static void PaintAstronaut(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w;
            float cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(180 * fade);
            float angle = ev.Param1;

            // Apply tumble rotation
            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle % 360);

            // Helmet (circle)
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 220, 230)))
                g.FillEllipse(b, -5, -9, 10, 10);
            // Visor
            using (var b = new SolidBrush(Color.FromArgb(a, 80, 160, 220)))
                g.FillEllipse(b, -3, -7, 7, 6);
            // Visor reflection
            using (var b = new SolidBrush(Color.FromArgb(a / 3, 255, 255, 255)))
                g.FillEllipse(b, -1, -6, 3, 2);

            // Body (rounded rect)
            using (var b = new SolidBrush(Color.FromArgb(a, 230, 230, 235)))
                g.FillRectangle(b, -4, 1, 8, 8);

            // Backpack
            using (var b = new SolidBrush(Color.FromArgb(a, 160, 160, 170)))
                g.FillRectangle(b, -6, 2, 3, 6);

            // Arms
            using (var p = new Pen(Color.FromArgb(a, 220, 220, 230), 2))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                float armWave = (float)Math.Sin(angle * 0.05f) * 15;
                g.DrawLine(p, -4, 3, -8, 1 + armWave * 0.3f);
                g.DrawLine(p, 4, 3, 8, 5 - armWave * 0.3f);
            }

            // Legs
            using (var p = new Pen(Color.FromArgb(a, 220, 220, 230), 2))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, -2, 9, -3, 14);
                g.DrawLine(p, 2, 9, 3, 14);
            }

            // Tether line trailing behind
            g.Restore(state);
            int tetherA = (int)(60 * fade);
            float tetherEndX = cx - 40 - (float)Math.Sin(t * 10) * 10;
            float tetherEndY = cy + 20 + (float)Math.Cos(t * 8) * 8;
            using (var p = new Pen(Color.FromArgb(tetherA, 180, 180, 190), 0.8f))
                g.DrawBezier(p, cx, cy, cx - 15, cy + 5, cx - 25, cy + 15, tetherEndX, tetherEndY);
        }

        static void PaintEclipse(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w;
            float cy = (ev.Y + ev.DirY * t) * h;
            float r = 16;
            int a = (int)(200 * fade);

            // Corona glow — multiple layers
            for (int layer = 4; layer >= 1; layer--)
            {
                float lr = r + layer * 6;
                int la = (int)(35 * fade / layer);
                Color coronaColor = layer > 2
                    ? Color.FromArgb(la, 255, 200, 120)   // outer gold
                    : Color.FromArgb(la, 255, 240, 220);  // inner white
                using (var b = new SolidBrush(coronaColor))
                    g.FillEllipse(b, cx - lr, cy - lr, lr * 2, lr * 2);
            }

            // Corona rays
            var rng = new Random(ev.Seed);
            for (int i = 0; i < 12; i++)
            {
                float angle = (float)(i * Math.PI * 2 / 12 + rng.NextDouble() * 0.3);
                float innerR = r + 4;
                float outerR = r + 12 + (float)rng.NextDouble() * 10;
                float rx1 = cx + (float)Math.Cos(angle) * innerR;
                float ry1 = cy + (float)Math.Sin(angle) * innerR;
                float rx2 = cx + (float)Math.Cos(angle) * outerR;
                float ry2 = cy + (float)Math.Sin(angle) * outerR;
                int ra = (int)(50 * fade);
                using (var p = new Pen(Color.FromArgb(ra, 255, 230, 180), 1.2f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, rx1, ry1, rx2, ry2);
                }
            }

            // Dark moon (the eclipse itself)
            using (var b = new SolidBrush(Color.FromArgb(a, 8, 8, 12)))
                g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);

            // Thin bright rim where sun peeks around edge
            using (var p = new Pen(Color.FromArgb((int)(120 * fade), 255, 250, 230), 1.0f))
                g.DrawEllipse(p, cx - r - 0.5f, cy - r - 0.5f, r * 2 + 1, r * 2 + 1);
        }

        static void PaintConstellation(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            var rng = new Random(ev.Seed);

            // Generate 4-6 star positions in a cluster
            int count = 4 + rng.Next(3);
            float[] sx = new float[count], sy = new float[count];
            for (int i = 0; i < count; i++)
            {
                sx[i] = cx + (float)(rng.NextDouble() - 0.5) * 80;
                sy[i] = cy + (float)(rng.NextDouble() - 0.5) * 60;
            }

            // Phase: 0-0.3 stars brighten, 0.3-0.7 lines draw, 0.7-1.0 fade out
            float starBright, lineBright;
            if (t < 0.3f) { starBright = t / 0.3f; lineBright = 0; }
            else if (t < 0.7f) { starBright = 1f; lineBright = (t - 0.3f) / 0.4f; }
            else { float ft = (t - 0.7f) / 0.3f; starBright = 1f - ft; lineBright = 1f - ft; }

            // Draw connecting lines
            if (lineBright > 0)
            {
                int lineA = (int)(50 * lineBright * fade);
                using (var p = new Pen(Color.FromArgb(lineA, 100, 160, 255), 0.8f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    for (int i = 0; i < count - 1; i++)
                        g.DrawLine(p, sx[i], sy[i], sx[i + 1], sy[i + 1]);
                    // Close the shape sometimes
                    if (count > 4)
                        g.DrawLine(p, sx[count - 1], sy[count - 1], sx[0], sy[0]);
                }
            }

            // Draw stars
            for (int i = 0; i < count; i++)
            {
                int sa = (int)(200 * starBright * fade);
                float sr = 2f + (float)rng.NextDouble() * 1.5f;
                // Star glow
                using (var b = new SolidBrush(Color.FromArgb(sa / 3, 140, 180, 255)))
                    g.FillEllipse(b, sx[i] - sr * 2, sy[i] - sr * 2, sr * 4, sr * 4);
                // Star core
                using (var b = new SolidBrush(Color.FromArgb(sa, 220, 230, 255)))
                    g.FillEllipse(b, sx[i] - sr, sy[i] - sr, sr * 2, sr * 2);
            }
        }

        static void PaintWormhole(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;

            // Phase: 0-0.25 portal opens, 0.25-0.6 meteor flies out, 0.6-1.0 portal closes
            float portalSize;
            if (t < 0.25f) portalSize = t / 0.25f;
            else if (t < 0.6f) portalSize = 1f;
            else portalSize = (1f - t) / 0.4f;
            portalSize = Math.Max(0, Math.Min(1, portalSize));

            float pr = 14 * portalSize;
            if (pr > 0.5f)
            {
                // Swirling rings
                float spin = t * 360 * 3;
                for (int ring = 3; ring >= 0; ring--)
                {
                    float rr = pr * (0.5f + ring * 0.25f);
                    int ra = (int)(60 * fade * portalSize / (ring + 1));
                    Color rc = ring % 2 == 0
                        ? Color.FromArgb(ra, 120, 80, 255)
                        : Color.FromArgb(ra, 80, 200, 255);
                    using (var p = new Pen(rc, 1.5f - ring * 0.2f))
                    {
                        float startAngle = spin + ring * 45;
                        g.DrawArc(p, cx - rr, cy - rr, rr * 2, rr * 2, startAngle, 270);
                    }
                }

                // Core glow
                int coreA = (int)(80 * fade * portalSize);
                using (var b = new SolidBrush(Color.FromArgb(coreA, 150, 120, 255)))
                    g.FillEllipse(b, cx - pr * 0.5f, cy - pr * 0.5f, pr, pr);
            }

            // Meteor flying out (during 0.25-0.75)
            if (t > 0.20f && t < 0.80f)
            {
                float mt = (t - 0.20f) / 0.60f;
                float mFade = 1f;
                if (mt < 0.15f) mFade = mt / 0.15f;
                if (mt > 0.7f) mFade = (1f - mt) / 0.3f;

                // Meteor flies out at angle from portal
                float angle = (float)(ev.Seed % 360 * Math.PI / 180);
                float dist = mt * 120;
                float mx = cx + (float)Math.Cos(angle) * dist;
                float my = cy + (float)Math.Sin(angle) * dist;
                float tx = cx + (float)Math.Cos(angle) * (dist - 20);
                float ty = cy + (float)Math.Sin(angle) * (dist - 20);

                int ma = (int)(160 * mFade * fade);
                using (var p = new Pen(Color.FromArgb(ma, 200, 180, 255), 2f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, mx, my, tx, ty);
                }
                using (var b = new SolidBrush(Color.FromArgb(ma / 2, 180, 150, 255)))
                    g.FillEllipse(b, mx - 3, my - 3, 6, 6);
            }
        }

        static void PaintBinaryStars(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w;
            float cy = (ev.Y + ev.DirY * t) * h;
            float orbitR = ev.Param2;
            float angle = ev.Param1 * (float)(Math.PI / 180);
            int a = (int)(180 * fade);

            // Two stars orbiting their center
            float s1x = cx + (float)Math.Cos(angle) * orbitR;
            float s1y = cy + (float)Math.Sin(angle) * orbitR;
            float s2x = cx - (float)Math.Cos(angle) * orbitR;
            float s2y = cy - (float)Math.Sin(angle) * orbitR;

            // Star 1: blue-white
            using (var b = new SolidBrush(Color.FromArgb(a / 3, 140, 180, 255)))
                g.FillEllipse(b, s1x - 5, s1y - 5, 10, 10);
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 220, 255)))
                g.FillEllipse(b, s1x - 2.5f, s1y - 2.5f, 5, 5);

            // Star 2: warm gold
            using (var b = new SolidBrush(Color.FromArgb(a / 3, 255, 200, 100)))
                g.FillEllipse(b, s2x - 4, s2y - 4, 8, 8);
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 230, 170)))
                g.FillEllipse(b, s2x - 2, s2y - 2, 4, 4);

            // Faint orbit trail
            int trailA = (int)(25 * fade);
            using (var p = new Pen(Color.FromArgb(trailA, 180, 190, 220), 0.6f))
                g.DrawEllipse(p, cx - orbitR, cy - orbitR, orbitR * 2, orbitR * 2);

            // Gravitational lens glow at center
            using (var b = new SolidBrush(Color.FromArgb((int)(20 * fade), 200, 210, 230)))
                g.FillEllipse(b, cx - 3, cy - 3, 6, 6);
        }

        static void PaintRocket(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w;
            float cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(200 * fade);

            // Rocket body (pointing up)
            // Nose cone
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 60, 60)))
                g.FillPolygon(b, new[] {
                    new PointF(cx, cy - 10),
                    new PointF(cx - 4, cy - 3),
                    new PointF(cx + 4, cy - 3)
                });
            // Body
            using (var b = new SolidBrush(Color.FromArgb(a, 230, 230, 235)))
                g.FillRectangle(b, cx - 4, cy - 3, 8, 12);
            // Window
            using (var b = new SolidBrush(Color.FromArgb(a, 80, 180, 230)))
                g.FillEllipse(b, cx - 2, cy - 1, 4, 4);
            using (var b = new SolidBrush(Color.FromArgb(a / 3, 255, 255, 255)))
                g.FillEllipse(b, cx - 1, cy - 0.5f, 2, 1.5f);
            // Fins
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 50, 50)))
            {
                g.FillPolygon(b, new[] {
                    new PointF(cx - 4, cy + 6), new PointF(cx - 8, cy + 11), new PointF(cx - 4, cy + 9)
                });
                g.FillPolygon(b, new[] {
                    new PointF(cx + 4, cy + 6), new PointF(cx + 8, cy + 11), new PointF(cx + 4, cy + 9)
                });
            }

            // Exhaust flame — flickering
            float flicker = (float)Math.Sin(ev.Param1 * 8) * 0.3f + 0.7f;
            float flameH = 12 + flicker * 8;
            // Outer flame (orange)
            int fA = (int)(150 * fade * flicker);
            using (var b = new SolidBrush(Color.FromArgb(fA, 255, 150, 30)))
                g.FillPolygon(b, new[] {
                    new PointF(cx - 3, cy + 9),
                    new PointF(cx + 3, cy + 9),
                    new PointF(cx + (float)Math.Sin(ev.Param1 * 12) * 2, cy + 9 + flameH)
                });
            // Inner flame (white-yellow)
            using (var b = new SolidBrush(Color.FromArgb(fA, 255, 240, 150)))
                g.FillPolygon(b, new[] {
                    new PointF(cx - 1.5f, cy + 9),
                    new PointF(cx + 1.5f, cy + 9),
                    new PointF(cx + (float)Math.Sin(ev.Param1 * 15) * 1, cy + 9 + flameH * 0.6f)
                });

            // Smoke trail particles
            var rng = new Random(ev.Seed + (int)(t * 10));
            for (int i = 0; i < 6; i++)
            {
                float age = (float)rng.NextDouble();
                float sx = cx + (float)(rng.NextDouble() - 0.5) * 12;
                float sy = cy + 15 + age * 40;
                float sr = 2 + age * 4;
                int sa = (int)(40 * (1f - age) * fade);
                using (var b = new SolidBrush(Color.FromArgb(sa, 180, 180, 190)))
                    g.FillEllipse(b, sx - sr, sy - sr, sr * 2, sr * 2);
            }
        }

        static void PaintSatellite(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w;
            float cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(180 * fade);
            float panelAngle = ev.Param1 * (float)(Math.PI / 180);

            // Central body (small box)
            using (var b = new SolidBrush(Color.FromArgb(a, 180, 185, 195)))
                g.FillRectangle(b, cx - 3, cy - 2, 6, 4);

            // Antenna dish on top
            using (var p = new Pen(Color.FromArgb(a, 200, 200, 210), 1f))
            {
                g.DrawLine(p, cx, cy - 2, cx, cy - 6);
                g.DrawArc(p, cx - 3, cy - 9, 6, 4, 180, 180);
            }

            // Solar panels — two rectangles extending from sides, slight wobble
            float panelW = 10, panelH = 5;
            float wobble = (float)Math.Sin(panelAngle * 0.05f) * 2;

            // Left panel
            using (var b = new SolidBrush(Color.FromArgb(a, 60, 80, 140)))
                g.FillRectangle(b, cx - 3 - panelW, cy - panelH / 2 + wobble, panelW, panelH);
            // Panel grid lines
            using (var p = new Pen(Color.FromArgb(a / 2, 100, 120, 180), 0.5f))
            {
                for (int i = 1; i < 3; i++)
                    g.DrawLine(p, cx - 3 - panelW + i * panelW / 3, cy - panelH / 2 + wobble,
                        cx - 3 - panelW + i * panelW / 3, cy + panelH / 2 + wobble);
            }

            // Right panel
            using (var b = new SolidBrush(Color.FromArgb(a, 60, 80, 140)))
                g.FillRectangle(b, cx + 3, cy - panelH / 2 - wobble, panelW, panelH);
            using (var p = new Pen(Color.FromArgb(a / 2, 100, 120, 180), 0.5f))
            {
                for (int i = 1; i < 3; i++)
                    g.DrawLine(p, cx + 3 + i * panelW / 3, cy - panelH / 2 - wobble,
                        cx + 3 + i * panelW / 3, cy + panelH / 2 - wobble);
            }

            // Solar reflection — panels catch light periodically
            float blink = (float)Math.Sin(ev.Param2 * (float)(Math.PI / 180)) * 0.5f + 0.5f;
            if (blink > 0.8f)
            {
                int reflA = (int)(100 * (blink - 0.8f) * 5 * fade);
                using (var b = new SolidBrush(Color.FromArgb(reflA, 255, 255, 255)))
                {
                    g.FillRectangle(b, cx - 3 - panelW, cy - panelH / 2 + wobble, panelW, panelH);
                    g.FillRectangle(b, cx + 3, cy - panelH / 2 - wobble, panelW, panelH);
                }
            }

            // Signal pulse rings (every ~3 seconds)
            float pulse = (t * 30) % 1.0f;
            if (pulse < 0.5f)
            {
                float pr = 4 + pulse * 20;
                int pa = (int)(40 * (1f - pulse * 2) * fade);
                using (var p = new Pen(Color.FromArgb(pa, 100, 200, 255), 0.8f))
                    g.DrawEllipse(p, cx - pr, cy - 6 - pr, pr * 2, pr * 2);
            }
        }

        static void PaintWishingStar(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;

            // Phase 1 (0-0.3): streak in fast
            // Phase 2 (0.3-0.7): stop and pulse/twinkle
            // Phase 3 (0.7-1.0): fade out with sparkle burst
            if (t < 0.3f)
            {
                float bt = t / 0.3f;
                // Fast streak arriving
                float len = 30 * (1f - bt * 0.5f);
                float hx = cx + (1f - bt) * 60;
                float hy = cy - (1f - bt) * 30;
                float tx = hx + len;
                float ty = hy - len * 0.5f;
                int sa = (int)(180 * bt * fade);
                using (var p = new Pen(Color.FromArgb(sa, 255, 250, 230), 1.5f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, hx, hy, tx, ty);
                }
                using (var b = new SolidBrush(Color.FromArgb(sa / 2, 255, 255, 255)))
                    g.FillEllipse(b, hx - 3, hy - 3, 6, 6);
            }
            else if (t < 0.7f)
            {
                float bt = (t - 0.3f) / 0.4f;
                // Stopped — pulsing twinkle
                float pulse = (float)Math.Sin(bt * Math.PI * 6) * 0.3f + 0.7f;
                float starR = 3 + pulse * 3;

                // Cross twinkle shape
                int sa = (int)(200 * pulse * fade);
                // Vertical spike
                using (var p = new Pen(Color.FromArgb(sa, 255, 255, 255), 1.2f))
                {
                    g.DrawLine(p, cx, cy - starR * 2, cx, cy + starR * 2);
                    g.DrawLine(p, cx - starR * 2, cy, cx + starR * 2, cy);
                }
                // Diagonal spikes (shorter)
                int da = (int)(120 * pulse * fade);
                using (var p = new Pen(Color.FromArgb(da, 255, 250, 220), 0.8f))
                {
                    float dr = starR * 1.3f;
                    g.DrawLine(p, cx - dr, cy - dr, cx + dr, cy + dr);
                    g.DrawLine(p, cx + dr, cy - dr, cx - dr, cy + dr);
                }
                // Core glow
                using (var b = new SolidBrush(Color.FromArgb(sa / 2, 255, 250, 200)))
                    g.FillEllipse(b, cx - starR, cy - starR, starR * 2, starR * 2);
                // Outer halo
                using (var b = new SolidBrush(Color.FromArgb((int)(30 * pulse * fade), 255, 240, 180)))
                    g.FillEllipse(b, cx - starR * 3, cy - starR * 3, starR * 6, starR * 6);
            }
            else
            {
                float bt = (t - 0.7f) / 0.3f;
                // Sparkle burst outward
                var rng = new Random(ev.Seed);
                int count = 10;
                for (int i = 0; i < count; i++)
                {
                    float angle = (float)(rng.NextDouble() * Math.PI * 2);
                    float speed = 15 + (float)rng.NextDouble() * 30;
                    float sx = cx + (float)Math.Cos(angle) * speed * bt;
                    float sy = cy + (float)Math.Sin(angle) * speed * bt;
                    float sr = 1.5f * (1f - bt);
                    int sa = (int)(150 * (1f - bt) * fade);
                    using (var b = new SolidBrush(Color.FromArgb(sa, 255, 255, 255)))
                        g.FillEllipse(b, sx - sr, sy - sr, sr * 2, sr * 2);
                }
                // Fading core
                float coreA = 100 * (1f - bt) * fade;
                using (var b = new SolidBrush(Color.FromArgb((int)coreA, 255, 250, 230)))
                    g.FillEllipse(b, cx - 2, cy - 2, 4, 4);
            }
        }

        static void PaintPlanet(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w;
            float cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(190 * fade);
            int planetType = (int)ev.Param1;
            float pr = 12; // planet radius

            Color bodyColor, ringColor, atmoColor;
            switch (planetType)
            {
                case 0: // Ringed (Saturn-like) — golden
                    bodyColor = Color.FromArgb(a, 210, 180, 120);
                    ringColor = Color.FromArgb(a / 2, 200, 180, 140);
                    atmoColor = Color.FromArgb(a / 4, 230, 210, 160);
                    break;
                case 1: // Gas giant (Jupiter-like) — orange/red bands
                    bodyColor = Color.FromArgb(a, 200, 150, 100);
                    ringColor = Color.Empty;
                    atmoColor = Color.FromArgb(a / 4, 220, 170, 120);
                    break;
                case 2: // Earth-like — blue/green
                    bodyColor = Color.FromArgb(a, 70, 130, 180);
                    ringColor = Color.Empty;
                    atmoColor = Color.FromArgb(a / 4, 100, 180, 230);
                    break;
                default: // Ice planet — pale blue
                    bodyColor = Color.FromArgb(a, 160, 200, 230);
                    ringColor = Color.FromArgb(a / 3, 180, 210, 240);
                    atmoColor = Color.FromArgb(a / 4, 180, 220, 250);
                    break;
            }

            // Atmosphere glow
            using (var b = new SolidBrush(atmoColor))
                g.FillEllipse(b, cx - pr * 1.4f, cy - pr * 1.4f, pr * 2.8f, pr * 2.8f);

            // Ring behind planet (ringed types only — draw back half)
            if (ringColor != Color.Empty)
            {
                using (var p = new Pen(ringColor, 2.5f))
                    g.DrawEllipse(p, cx - pr * 1.8f, cy - pr * 0.3f, pr * 3.6f, pr * 0.6f);
            }

            // Planet body
            using (var b = new SolidBrush(bodyColor))
                g.FillEllipse(b, cx - pr, cy - pr, pr * 2, pr * 2);

            // Surface detail bands (gas giant/earth-like)
            if (planetType == 1)
            {
                // Jupiter bands
                int bandA = a / 3;
                using (var p = new Pen(Color.FromArgb(bandA, 180, 120, 70), 1.5f))
                {
                    g.DrawArc(p, cx - pr + 2, cy - 4, pr * 2 - 4, 3, 0, 180);
                    g.DrawArc(p, cx - pr + 2, cy + 3, pr * 2 - 4, 3, 0, 180);
                }
                // Great red spot
                using (var b = new SolidBrush(Color.FromArgb(bandA, 200, 100, 70)))
                    g.FillEllipse(b, cx + 2, cy - 2, 4, 3);
            }
            else if (planetType == 2)
            {
                // Earth continents (rough shapes)
                int landA = a / 2;
                using (var b = new SolidBrush(Color.FromArgb(landA, 80, 160, 80)))
                {
                    g.FillEllipse(b, cx - 5, cy - 5, 6, 7);
                    g.FillEllipse(b, cx + 2, cy - 2, 5, 4);
                }
                // Ice cap
                using (var b = new SolidBrush(Color.FromArgb(landA, 230, 240, 250)))
                    g.FillEllipse(b, cx - 3, cy - pr + 1, 6, 3);
            }

            // Ring in front of planet (ringed types only — draw front half)
            if (ringColor != Color.Empty)
            {
                using (var p = new Pen(ringColor, 2f))
                    g.DrawArc(p, cx - pr * 1.8f, cy - pr * 0.3f, pr * 3.6f, pr * 0.6f, 0, 180);
            }

            // Specular highlight
            using (var b = new SolidBrush(Color.FromArgb(a / 3, 255, 255, 255)))
                g.FillEllipse(b, cx - pr * 0.5f, cy - pr * 0.6f, pr * 0.6f, pr * 0.5f);
        }

        static void PaintAlienDogfight(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            // Two ships chasing each other across the screen
            float baseX = (ev.X + ev.DirX * t) * w;
            float baseY = (ev.Y + ev.DirY * t) * h;
            float wobble = ev.Param1;

            // Ship 1 (green) — leader, weaving
            float s1x = baseX + (float)Math.Sin(wobble * 3) * 15;
            float s1y = baseY + (float)Math.Cos(wobble * 2.3f) * 12;

            // Ship 2 (red) — pursuer, slightly behind and offset
            float s2x = s1x - 35 - (float)Math.Sin(wobble * 2.5f) * 10;
            float s2y = s1y + 8 + (float)Math.Cos(wobble * 3.2f) * 10;

            int a = (int)(200 * fade);

            // === SHIP 1 (green/teal — the runner) ===
            DrawMiniSaucer(g, s1x, s1y, a, Color.FromArgb(a, 50, 200, 160), Color.FromArgb(a, 100, 255, 200));

            // === SHIP 2 (red/orange — the chaser) ===
            DrawMiniSaucer(g, s2x, s2y, a, Color.FromArgb(a, 200, 60, 60), Color.FromArgb(a, 255, 120, 80));

            // === LASERS ===
            var rng = new Random(ev.Seed + (int)(ev.Param2 * 10));
            float laserPhase = ev.Param2 % 1.0f;

            // Ship 2 fires at ship 1 (red lasers)
            if (laserPhase < 0.3f && t > 0.1f && t < 0.85f)
            {
                float lFade = laserPhase < 0.05f ? laserPhase / 0.05f : (0.3f - laserPhase) / 0.25f;
                int lA = (int)(180 * lFade * fade);
                // Aimed laser
                using (var p = new Pen(Color.FromArgb(lA, 255, 60, 60), 1.5f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, s2x + 10, s2y, s1x - 5 + rng.Next(10) - 5, s1y + rng.Next(8) - 4);
                }
                // Glow at muzzle
                using (var b = new SolidBrush(Color.FromArgb(lA / 2, 255, 100, 80)))
                    g.FillEllipse(b, s2x + 8, s2y - 3, 6, 6);
            }

            // Ship 1 fires back (green lasers) — offset phase
            float laser2Phase = (ev.Param2 + 0.5f) % 1.0f;
            if (laser2Phase < 0.25f && t > 0.1f && t < 0.85f)
            {
                float lFade = laser2Phase < 0.05f ? laser2Phase / 0.05f : (0.25f - laser2Phase) / 0.20f;
                int lA = (int)(160 * lFade * fade);
                // Return fire
                using (var p = new Pen(Color.FromArgb(lA, 80, 255, 150), 1.2f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, s1x - 8, s1y, s2x + 5 + rng.Next(10) - 5, s2y + rng.Next(8) - 4);
                }
                using (var b = new SolidBrush(Color.FromArgb(lA / 2, 100, 255, 160)))
                    g.FillEllipse(b, s1x - 12, s1y - 3, 6, 6);
            }

            // Random stray lasers in various directions
            float strayPhase = (ev.Param2 * 3) % 1.0f;
            if (strayPhase < 0.15f && t > 0.15f && t < 0.8f)
            {
                float lFade = strayPhase < 0.03f ? strayPhase / 0.03f : (0.15f - strayPhase) / 0.12f;
                int lA = (int)(100 * lFade * fade);
                // Ship 2 stray shot
                float strayAngle = (float)(rng.NextDouble() * Math.PI * 2);
                float strayLen = 30 + (float)rng.NextDouble() * 40;
                using (var p = new Pen(Color.FromArgb(lA, 255, 80, 80), 1f))
                    g.DrawLine(p, s2x, s2y, s2x + (float)Math.Cos(strayAngle) * strayLen,
                        s2y + (float)Math.Sin(strayAngle) * strayLen);
                // Ship 1 stray shot
                strayAngle = (float)(rng.NextDouble() * Math.PI * 2);
                using (var p = new Pen(Color.FromArgb(lA, 80, 255, 120), 1f))
                    g.DrawLine(p, s1x, s1y, s1x + (float)Math.Cos(strayAngle) * strayLen,
                        s1y + (float)Math.Sin(strayAngle) * strayLen);
            }

            // Tiny explosion sparks when lasers "hit" near ships
            if (t > 0.2f && t < 0.8f)
            {
                var sparkRng = new Random(ev.Seed + (int)(t * 8));
                if (sparkRng.NextDouble() < 0.3)
                {
                    float sparkX = s1x + (float)(sparkRng.NextDouble() - 0.5) * 12;
                    float sparkY = s1y + (float)(sparkRng.NextDouble() - 0.5) * 8;
                    int sa = (int)(120 * fade);
                    using (var b = new SolidBrush(Color.FromArgb(sa, 255, 220, 100)))
                        g.FillEllipse(b, sparkX - 2, sparkY - 2, 4, 4);
                    using (var b = new SolidBrush(Color.FromArgb(sa / 2, 255, 255, 200)))
                        g.FillEllipse(b, sparkX - 4, sparkY - 4, 8, 8);
                }
            }
        }

        // ===== NEW EVENT PAINT METHODS =====

        static void PaintComet(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 200);
            float tailLen = ev.Param1 * Math.Max(w, h) * 0.08f;
            float angle = (float)Math.Atan2(-ev.DirY, -ev.DirX);
            // Tail
            for (int i = 0; i < 12; i++) {
                float d = i / 12f * tailLen;
                float tx = cx + (float)Math.Cos(angle) * d, ty = cy + (float)Math.Sin(angle) * d;
                int ta = (int)(a * (1 - i / 12f) * 0.5f);
                float sz = 6 - i * 0.4f;
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(0, ta), 180, 220, 255)))
                    g.FillEllipse(b, tx - sz, ty - sz, sz * 2, sz * 2);
            }
            // Head
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 240, 255)))
                g.FillEllipse(b, cx - 4, cy - 4, 8, 8);
            using (var b = new SolidBrush(Color.FromArgb(a / 2, 255, 255, 255)))
                g.FillEllipse(b, cx - 2, cy - 2, 4, 4);
        }

        static void PaintSpaceWhale(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 160);
            float wave = (float)Math.Sin(t * 20) * 8;
            cy += wave;
            float facing = ev.DirX > 0 ? 1 : -1;
            // Body
            using (var b = new SolidBrush(Color.FromArgb(a, 60, 80, 130)))
                g.FillEllipse(b, cx - 20 * facing, cy - 6, 40, 12);
            // Eye
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 220, 255)))
                g.FillEllipse(b, cx + 14 * facing, cy - 2, 3, 3);
            // Tail fin
            float tailX = cx - 22 * facing;
            float finWave = (float)Math.Sin(t * 25) * 4;
            using (var p = new Pen(Color.FromArgb(a, 80, 100, 160), 2f))
            { g.DrawLine(p, tailX, cy, tailX - 6 * facing, cy - 5 + finWave);
              g.DrawLine(p, tailX, cy, tailX - 6 * facing, cy + 5 + finWave); }
            // Sparkle trail
            var rng = new Random(ev.Seed);
            for (int i = 0; i < 6; i++) {
                float sx = cx - (10 + rng.Next(30)) * facing + (float)Math.Sin(t * 15 + i) * 3;
                float sy = cy + rng.Next(-8, 8);
                int sa = (int)(a * 0.4 * (0.5 + 0.5 * Math.Sin(t * 20 + i * 2)));
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(0, sa), 150, 200, 255)))
                    g.FillEllipse(b, sx, sy, 2, 2);
            }
        }

        static void PaintFirefly(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            int a = (int)(fade * 180);
            int count = (int)ev.Param1;
            var rng = new Random(ev.Seed);
            float baseCx = ev.X * w, baseCy = ev.Y * h;
            for (int i = 0; i < count; i++) {
                float offX = (rng.Next(-40, 40)), offY = (rng.Next(-30, 30));
                float fx = baseCx + offX + (float)Math.Sin(t * 8 + i * 1.7) * 15;
                float fy = baseCy + offY + (float)Math.Cos(t * 6 + i * 2.3) * 10;
                float pulse = 0.4f + 0.6f * (float)Math.Max(0, Math.Sin(t * 12 + i * 3.1));
                int fa = (int)(a * pulse);
                using (var b = new SolidBrush(Color.FromArgb(fa / 3, 180, 255, 100)))
                    g.FillEllipse(b, fx - 4, fy - 4, 8, 8);
                using (var b = new SolidBrush(Color.FromArgb(fa, 220, 255, 150)))
                    g.FillEllipse(b, fx - 1.5f, fy - 1.5f, 3, 3);
            }
        }

        static void PaintPulsar(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 180);
            float beamAngle = ev.Param1 + t * 400;
            float rad = (float)(beamAngle * Math.PI / 180);
            // Core
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                g.FillEllipse(b, cx - 3, cy - 3, 6, 6);
            using (var b = new SolidBrush(Color.FromArgb(a / 3, 200, 220, 255)))
                g.FillEllipse(b, cx - 8, cy - 8, 16, 16);
            // Beam
            float beamLen = Math.Min(w, h) * 0.3f;
            for (int side = 0; side < 2; side++) {
                float sign = side == 0 ? 1 : -1;
                float ex = cx + (float)Math.Cos(rad) * beamLen * sign;
                float ey = cy + (float)Math.Sin(rad) * beamLen * sign;
                using (var p = new Pen(Color.FromArgb(a / 2, 150, 180, 255), 2f))
                    g.DrawLine(p, cx, cy, ex, ey);
                using (var p = new Pen(Color.FromArgb(a / 4, 150, 180, 255), 5f))
                    g.DrawLine(p, cx, cy, ex, ey);
            }
        }

        static void PaintNebula(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 80);
            Color[] schemes = { Color.FromArgb(a, 100, 50, 150), Color.FromArgb(a, 50, 100, 150), Color.FromArgb(a, 150, 60, 80) };
            Color c = schemes[(int)ev.Param1 % 3];
            var rng = new Random(ev.Seed);
            for (int i = 0; i < 8; i++) {
                float ox = rng.Next(-25, 25) + (float)Math.Sin(t * 3 + i) * 5;
                float oy = rng.Next(-20, 20) + (float)Math.Cos(t * 2 + i * 1.5) * 4;
                float sz = 15 + rng.Next(20);
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(1, a / 2), c.R, c.G, c.B)))
                    g.FillEllipse(b, cx + ox - sz / 2, cy + oy - sz / 2, sz, sz);
            }
        }

        static void PaintSpaceJellyfish(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 160);
            // Bell
            using (var b = new SolidBrush(Color.FromArgb(a / 2, 180, 100, 220)))
                g.FillEllipse(b, cx - 10, cy - 8, 20, 14);
            using (var p = new Pen(Color.FromArgb(a, 200, 140, 255), 1.5f))
                g.DrawArc(p, cx - 10, cy - 8, 20, 14, 180, 180);
            // Tentacles
            for (int i = 0; i < 5; i++) {
                float tx = cx - 8 + i * 4;
                float wave = (float)Math.Sin(t * 15 + i * 2) * 4;
                using (var p = new Pen(Color.FromArgb(a / 2, 180, 140, 255), 1f))
                    g.DrawBezier(p, tx, cy + 4, tx + wave, cy + 12, tx - wave, cy + 20, tx + wave * 0.5f, cy + 28);
            }
            // Glow
            using (var b = new SolidBrush(Color.FromArgb(a / 5, 200, 160, 255)))
                g.FillEllipse(b, cx - 14, cy - 12, 28, 22);
        }

        static void PaintMeteorShower(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            int a = (int)(fade * 200);
            var rng = new Random(ev.Seed);
            float cx = ev.X * w, cy = ev.Y * h;
            for (int i = 0; i < 8; i++) {
                float delay = rng.Next(80) / 100f;
                float mt = t - delay;
                if (mt < 0 || mt > 0.4f) continue;
                float mx = cx + rng.Next(-40, 40) + mt * rng.Next(50, 150);
                float my = cy + rng.Next(-20, 20) + mt * rng.Next(80, 200);
                float ma = a * (1 - mt / 0.4f);
                using (var p = new Pen(Color.FromArgb((int)Math.Max(0, Math.Min(255, ma)), 255, 220, 150), 1.5f))
                    g.DrawLine(p, mx, my, mx - 8, my - 5);
                using (var b = new SolidBrush(Color.FromArgb((int)Math.Max(0, Math.Min(255, ma)), 255, 240, 200)))
                    g.FillEllipse(b, mx - 1, my - 1, 2, 2);
            }
        }

        static void PaintGhostShip(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float flicker = 0.5f + 0.5f * (float)Math.Sin(t * 20);
            int a = (int)(fade * 120 * flicker);
            float facing = ev.DirX > 0 ? 1 : -1;
            // Hull
            using (var p = new Pen(Color.FromArgb(a, 100, 140, 180), 1.5f))
            { g.DrawLine(p, cx - 15 * facing, cy, cx + 15 * facing, cy);
              g.DrawLine(p, cx + 15 * facing, cy, cx + 10 * facing, cy - 8);
              g.DrawLine(p, cx - 15 * facing, cy, cx - 10 * facing, cy - 4); }
            // Mast & sail
            using (var p = new Pen(Color.FromArgb(a, 120, 160, 200), 1f))
            { g.DrawLine(p, cx, cy, cx, cy - 18);
              g.DrawLine(p, cx, cy - 16, cx + 8 * facing, cy - 10); }
            // Ghostly glow
            using (var b = new SolidBrush(Color.FromArgb(a / 4, 100, 180, 255)))
                g.FillEllipse(b, cx - 18, cy - 20, 36, 28);
        }

        static void PaintStarPhoenix(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 200);
            float wingPhase = (float)Math.Sin(t * 18) * 8;
            float facing = ev.DirX > 0 ? 1 : -1;
            // Wings
            using (var p = new Pen(Color.FromArgb(a, 255, 150, 50), 2f)) {
                g.DrawBezier(p, cx, cy, cx - 8 * facing, cy - 10 - wingPhase, cx - 16 * facing, cy - 6 - wingPhase, cx - 20 * facing, cy - 12);
                g.DrawBezier(p, cx, cy, cx - 8 * facing, cy + 10 + wingPhase, cx - 16 * facing, cy + 6 + wingPhase, cx - 20 * facing, cy + 12);
            }
            // Body
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 200, 80)))
                g.FillEllipse(b, cx - 3, cy - 3, 6, 6);
            // Fire trail
            for (int i = 1; i < 6; i++) {
                float tx = cx - i * 5 * facing, ty = cy + (float)Math.Sin(t * 25 + i) * 2;
                int ta = a * (6 - i) / 8;
                using (var b = new SolidBrush(Color.FromArgb(ta, 255, 100 + i * 20, 0)))
                    g.FillEllipse(b, tx - 2, ty - 2, 4, 4);
            }
        }

        static void PaintBlackhole(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 180);
            // Accretion disk rings
            for (int i = 5; i >= 0; i--) {
                float r = 8 + i * 5;
                float rot = t * (200 - i * 30);
                int ra = a * (6 - i) / 8;
                using (var p = new Pen(Color.FromArgb(ra, 180 + i * 10, 100, 255 - i * 20), 1.5f - i * 0.1f))
                    g.DrawArc(p, cx - r, cy - r / 2.5f, r * 2, r * 1.2f, rot % 360, 200);
            }
            // Core — pure black
            using (var b = new SolidBrush(Color.FromArgb(a, 5, 5, 10)))
                g.FillEllipse(b, cx - 6, cy - 6, 12, 12);
            // Event horizon glow
            using (var p = new Pen(Color.FromArgb(a / 2, 200, 120, 255), 1.5f))
                g.DrawEllipse(p, cx - 7, cy - 7, 14, 14);
        }

        static void PaintAuroraWave(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            int a = (int)(fade * 60);
            Color[] colors = { Color.FromArgb(a, 50, 255, 100), Color.FromArgb(a, 150, 50, 255), Color.FromArgb(a, 50, 150, 255) };
            Color c = colors[(int)ev.Param1 % 3];
            float baseY = ev.Y * h;
            for (int layer = 0; layer < 3; layer++) {
                float yOff = layer * 12;
                using (var p = new Pen(Color.FromArgb(Math.Max(1, a - layer * 15), c.R, c.G, c.B), 3 - layer * 0.8f)) {
                    var pts = new PointF[12];
                    for (int i = 0; i < 12; i++) {
                        float px = i / 11f * w;
                        float py = baseY + yOff + (float)Math.Sin(t * 6 + i * 0.8 + layer * 1.5) * 15;
                        pts[i] = new PointF(px, py);
                    }
                    g.DrawCurve(p, pts, 0.4f);
                }
            }
        }

        static void PaintSpaceTrain(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            int a = (int)(fade * 180);
            int cars = (int)ev.Param1;
            float facing = ev.DirX > 0 ? 1 : -1;
            for (int c = 0; c < cars; c++) {
                float delay = c * 0.06f;
                float ct = t - delay;
                float cx = (ev.X + ev.DirX * ct) * w;
                float cy = (ev.Y + ev.DirY * ct) * h + (float)Math.Sin(ct * 12 + c) * 2;
                int ca = (int)(a * Math.Max(0, Math.Min(1, 1 - c * 0.08)));
                Color carColor = c == 0 ? Color.FromArgb(ca, 255, 220, 100) : Color.FromArgb(ca, 100, 150, 200);
                using (var b = new SolidBrush(carColor))
                    g.FillRectangle(b, cx - 6, cy - 3, 12, 6);
                using (var p = new Pen(Color.FromArgb(ca / 2, 200, 220, 255), 0.5f))
                    g.DrawRectangle(p, cx - 6, cy - 3, 12, 6);
                // Windows
                if (c > 0) {
                    using (var b = new SolidBrush(Color.FromArgb(ca, 200, 230, 255)))
                        for (int wi = 0; wi < 2; wi++)
                            g.FillRectangle(b, cx - 3 + wi * 5, cy - 1, 2, 2);
                }
            }
        }

        static void PaintOrb(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 160);
            Color[] orbColors = { Color.FromArgb(a, 100, 200, 255), Color.FromArgb(a, 255, 180, 80), Color.FromArgb(a, 180, 100, 255), Color.FromArgb(a, 100, 255, 150) };
            Color c = orbColors[(int)ev.Param1 % 4];
            float pulse = 0.8f + 0.2f * (float)Math.Sin(t * 15);
            float r = 6 * pulse;
            // Outer glow
            using (var b = new SolidBrush(Color.FromArgb(a / 5, c.R, c.G, c.B)))
                g.FillEllipse(b, cx - r * 2, cy - r * 2, r * 4, r * 4);
            // Inner glow
            using (var b = new SolidBrush(Color.FromArgb(a / 2, c.R, c.G, c.B)))
                g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
            // Core
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                g.FillEllipse(b, cx - r / 2, cy - r / 2, r, r);
        }

        static void PaintLaserGrid(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 80);
            float rot = ev.Param1 + t * 60;
            float rad = (float)(rot * Math.PI / 180);
            float gridSz = Math.Min(w, h) * 0.2f;
            int lines = 5;
            for (int i = 0; i < lines; i++) {
                float offset = (i - lines / 2f) * gridSz / lines;
                float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
                // Horizontal line (rotated)
                float x1 = cx + cos * (-gridSz / 2) - sin * offset;
                float y1 = cy + sin * (-gridSz / 2) + cos * offset;
                float x2 = cx + cos * (gridSz / 2) - sin * offset;
                float y2 = cy + sin * (gridSz / 2) + cos * offset;
                using (var p = new Pen(Color.FromArgb(a, 50, 255, 150), 0.5f))
                    g.DrawLine(p, x1, y1, x2, y2);
                // Vertical line (rotated)
                x1 = cx + cos * offset - sin * (-gridSz / 2);
                y1 = cy + sin * offset + cos * (-gridSz / 2);
                x2 = cx + cos * offset - sin * (gridSz / 2);
                y2 = cy + sin * offset + cos * (gridSz / 2);
                using (var p = new Pen(Color.FromArgb(a, 50, 255, 150), 0.5f))
                    g.DrawLine(p, x1, y1, x2, y2);
            }
        }

        static void PaintSpaceDolphin(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 180);
            float leap = (float)Math.Sin(t * 14) * 12;
            cy += leap;
            float facing = ev.DirX > 0 ? 1 : -1;
            // Body arc
            using (var p = new Pen(Color.FromArgb(a, 100, 160, 220), 2.5f)) {
                g.DrawArc(p, cx - 12 * facing, cy - 8, 24, 16, facing > 0 ? 200 : 340, 160);
            }
            // Dorsal fin
            using (var p = new Pen(Color.FromArgb(a, 120, 180, 240), 1.5f))
                g.DrawLine(p, cx, cy - 4, cx - 3 * facing, cy - 10);
            // Tail
            using (var p = new Pen(Color.FromArgb(a, 80, 140, 200), 1.5f)) {
                g.DrawLine(p, cx - 12 * facing, cy + 2, cx - 16 * facing, cy - 3);
                g.DrawLine(p, cx - 12 * facing, cy + 2, cx - 16 * facing, cy + 7);
            }
            // Splash sparkles
            if (leap < -6) {
                using (var b = new SolidBrush(Color.FromArgb(a / 2, 150, 200, 255)))
                    for (int i = 0; i < 3; i++)
                        g.FillEllipse(b, cx + (i - 1) * 6, cy + 8 + i * 2, 2, 2);
            }
        }

        static void PaintStardust(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            int a = (int)(fade * 120);
            float cx = ev.X * w, cy = ev.Y * h;
            var rng = new Random(ev.Seed);
            for (int i = 0; i < 20; i++) {
                float ox = rng.Next(-35, 35) + (float)Math.Sin(t * 4 + i * 0.9) * 8;
                float oy = rng.Next(-25, 25) + (float)Math.Cos(t * 3 + i * 1.3) * 6;
                float pulse = 0.3f + 0.7f * (float)Math.Max(0, Math.Sin(t * 8 + i * 2.1));
                int pa = (int)(a * pulse);
                Color[] dustColors = { Color.FromArgb(pa, 255, 220, 150), Color.FromArgb(pa, 200, 180, 255), Color.FromArgb(pa, 180, 255, 200) };
                using (var b = new SolidBrush(dustColors[i % 3]))
                    g.FillEllipse(b, cx + ox - 1, cy + oy - 1, 2.5f, 2.5f);
            }
        }

        static void PaintSolarFlare(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 180);
            bool blue = ev.Param1 > 0.5f;
            Color c1 = blue ? Color.FromArgb(a, 100, 150, 255) : Color.FromArgb(a, 255, 150, 50);
            Color c2 = blue ? Color.FromArgb(a / 2, 80, 120, 200) : Color.FromArgb(a / 2, 255, 100, 20);
            // Flare tendrils
            float grow = Math.Min(1, t * 4);
            for (int i = 0; i < 5; i++) {
                float angle = (float)(i * 72 + t * 100) * (float)Math.PI / 180;
                float len = 20 * grow + (float)Math.Sin(t * 12 + i * 2) * 8;
                float ex = cx + (float)Math.Cos(angle) * len;
                float ey = cy + (float)Math.Sin(angle) * len;
                using (var p = new Pen(c2, 2f + (float)Math.Sin(t * 15 + i) * 1))
                    g.DrawLine(p, cx, cy, ex, ey);
            }
            // Core
            using (var b = new SolidBrush(c1))
                g.FillEllipse(b, cx - 4, cy - 4, 8, 8);
        }

        static void PaintSpaceButterfly(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 170);
            float wingFlap = (float)Math.Sin(t * 20) * 0.7f;
            float facing = ev.DirX > 0 ? 1 : -1;
            // Wings (top and bottom pairs)
            for (int side = -1; side <= 1; side += 2) {
                float wingY = cy + side * 5 * (1 - Math.Abs(wingFlap));
                float wingW = 8 * (0.3f + 0.7f * (float)Math.Abs(Math.Cos(t * 20)));
                Color wc = side > 0 ? Color.FromArgb(a, 255, 140, 200) : Color.FromArgb(a, 200, 140, 255);
                using (var b = new SolidBrush(wc))
                    g.FillEllipse(b, cx - wingW - 1, wingY - 3, wingW * 2, 6);
            }
            // Body
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 200)))
                g.FillEllipse(b, cx - 1.5f, cy - 4, 3, 8);
            // Antennae
            using (var p = new Pen(Color.FromArgb(a / 2, 255, 200, 230), 0.5f)) {
                g.DrawLine(p, cx, cy - 4, cx + 4 * facing, cy - 9);
                g.DrawLine(p, cx, cy - 4, cx + 2 * facing, cy - 10);
            }
        }

        static void PaintLighthouse(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 160);
            float beamAngle = ev.Param1 + t * 180;
            // Tower
            using (var p = new Pen(Color.FromArgb(a, 150, 150, 170), 1.5f))
                g.DrawLine(p, cx, cy, cx, cy + 12);
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 200, 220)))
                g.FillRectangle(b, cx - 3, cy - 2, 6, 4);
            // Rotating beam
            float rad = (float)(beamAngle * Math.PI / 180);
            float beamLen = Math.Min(w, h) * 0.25f;
            float bx = cx + (float)Math.Cos(rad) * beamLen;
            float by = cy + (float)Math.Sin(rad) * beamLen;
            using (var p = new Pen(Color.FromArgb(a / 3, 255, 255, 200), 4f))
                g.DrawLine(p, cx, cy, bx, by);
            using (var p = new Pen(Color.FromArgb(a / 2, 255, 255, 220), 1.5f))
                g.DrawLine(p, cx, cy, bx, by);
            // Light source
            float pulse = 0.7f + 0.3f * (float)Math.Sin(t * 20);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * pulse), 255, 255, 200)))
                g.FillEllipse(b, cx - 3, cy - 3, 6, 6);
        }

        static void DrawMiniSaucer(Graphics g, float cx, float cy, int a, Color body, Color lights)
        {
            float bw = 14, bh = 5;
            // Body
            using (var b = new SolidBrush(Color.FromArgb(a * 3 / 4, 45, 50, 60)))
                g.FillEllipse(b, cx - bw / 2, cy - bh / 2, bw, bh);
            using (var p = new Pen(body, 1f))
                g.DrawEllipse(p, cx - bw / 2, cy - bh / 2, bw, bh);
            // Dome
            using (var b = new SolidBrush(Color.FromArgb(a / 2, lights.R, lights.G, lights.B)))
                g.FillEllipse(b, cx - 3, cy - bh / 2 - 3, 6, 4);
            // Engine glow
            using (var b = new SolidBrush(Color.FromArgb(a / 3, lights.R, lights.G, lights.B)))
                g.FillEllipse(b, cx - 2, cy + 1, 4, 3);
        }

        // ====== SPECTACULAR UNICORN ======
        static void PaintUnicorn(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            int a = (int)(220 * fade); float gallop = (float)Math.Sin(t * 60) * 3.5f;
            Color[] rb = { Color.FromArgb(255,50,50), Color.FromArgb(255,140,0), Color.FromArgb(255,240,50),
                Color.FromArgb(50,255,80), Color.FromArgb(50,150,255), Color.FromArgb(160,50,255) };
            // LONG sparkly rainbow trail — 30 segments x 6 bands
            for (int i = 0; i < 6; i++) for (int seg = 0; seg < 30; seg++) {
                float dist = (seg+1)*6f; float sx = cx - ev.DirX*dist;
                float sy = cy - ev.DirY*dist + (i-2.5f)*(2.5f+seg*0.08f) + (float)Math.Sin(t*35+seg*0.4f+i*1.1f)*(1.5f+seg*0.06f);
                int sa = (int)(a*0.45f*(1f-seg/32f)); if(sa<=0) continue;
                float sz = 2.5f+(float)Math.Sin(t*50+seg+i)*0.5f;
                using(var b=new SolidBrush(Color.FromArgb(sa,rb[i].R,rb[i].G,rb[i].B))) g.FillEllipse(b,sx-sz,sy-sz*0.4f,sz*2,sz*0.8f);
            }
            // SPARKLE FIELD — magic particles everywhere
            var sr = new Random(ev.Seed+(int)(t*200));
            for (int sp = 0; sp < 40; sp++) {
                float dist=(float)sr.NextDouble()*180f, spread=(float)(sr.NextDouble()-0.5)*30f;
                float spx=cx-ev.DirX*dist+(float)Math.Sin(t*30+sp)*spread*0.3f;
                float spy=cy-ev.DirY*dist+spread+(float)Math.Cos(t*25+sp*0.7f)*3f;
                float tw=(float)(0.3+0.7*Math.Sin(t*80+sp*2.1));
                int spa=(int)(a*0.6f*tw*(1f-dist/200f)); if(spa<=2) continue;
                Color sc=rb[sp%6]; float ssz=1.2f+tw*1.5f;
                using(var p=new Pen(Color.FromArgb(spa,sc.R,sc.G,sc.B),0.6f)){g.DrawLine(p,spx-ssz,spy,spx+ssz,spy);g.DrawLine(p,spx,spy-ssz,spx,spy+ssz);}
                using(var b=new SolidBrush(Color.FromArgb(spa/2,255,255,255))) g.FillEllipse(b,spx-0.8f,spy-0.8f,1.6f,1.6f);
            }
            cy += gallop;
            // Ambient glow
            for(int gr=3;gr>=1;gr--){float gs=8+gr*6;using(var b=new SolidBrush(Color.FromArgb((int)(a*0.06f*gr),200,180,255))) g.FillEllipse(b,cx-gs,cy-gs*0.6f,gs*2,gs*1.2f);}
            // Body
            using(var b=new SolidBrush(Color.FromArgb(a,245,240,255))) g.FillEllipse(b,cx-7,cy-3.5f,14,7);
            using(var b=new SolidBrush(Color.FromArgb(a/3,255,255,255))) g.FillEllipse(b,cx-4,cy-3,8,3);
            float dir=ev.DirX>0?1:-1;
            // Head
            float hx=cx+dir*7,hy=cy-3;
            using(var b=new SolidBrush(Color.FromArgb(a,248,245,255))) g.FillEllipse(b,hx-3.5f,hy-3.5f,7,7);
            using(var b=new SolidBrush(Color.FromArgb(a,80,50,140))) g.FillEllipse(b,hx+dir*1.5f-0.8f,hy-0.5f,1.6f,1.6f);
            // GOLDEN HORN with sparkle burst
            float htx=hx+dir*5,hty=hy-7;
            using(var p=new Pen(Color.FromArgb(a,255,215,80),1.8f)){p.StartCap=LineCap.Round;p.EndCap=LineCap.Round;g.DrawLine(p,hx+dir*1.5f,hy-2,htx,hty);}
            using(var p=new Pen(Color.FromArgb(a/2,255,255,200),0.8f)) g.DrawLine(p,hx+dir*2,hy-2.5f,htx,hty);
            for(int hs=0;hs<6;hs++){float ang=(float)(hs*Math.PI/3+t*40),hr=3f+(float)Math.Sin(t*70+hs)*2f;
                int hsa=(int)(a*(0.3f+0.5f*(float)Math.Sin(t*80+hs*1.5f)));
                using(var b=new SolidBrush(Color.FromArgb(hsa,255,255,180))) g.FillEllipse(b,htx+(float)Math.Cos(ang)*hr-1,hty+(float)Math.Sin(ang)*hr-1,2,2);}
            // Legs
            for(int leg=0;leg<4;leg++){float lx=cx-4+leg*2.8f,phase=(float)Math.Sin(t*60+leg*1.8f)*3;
                using(var p=new Pen(Color.FromArgb(a*3/4,230,225,245),1f)) g.DrawLine(p,lx,cy+3,lx+phase*0.3f,cy+7+phase);
                if(phase<-1.5f) using(var b=new SolidBrush(Color.FromArgb(a/3,255,215,80))) g.FillEllipse(b,lx-1,cy+7+phase-1,2,2);}
            // Rainbow mane
            for(int m=0;m<8;m++){float mx=cx+(dir>0?-3:3)-ev.DirX*m*2.5f,my=cy-4+(float)Math.Sin(t*45+m*0.9f)*(2+m*0.3f);
                int ma=(int)(a*0.5f*(1f-m*0.08f));float ms=2.5f+(float)Math.Sin(t*55+m)*0.5f;
                using(var b=new SolidBrush(Color.FromArgb(ma,rb[m%6].R,rb[m%6].G,rb[m%6].B))) g.FillEllipse(b,mx-ms,my-ms*0.5f,ms*2,ms);}
            // Rainbow tail
            float tb=cx-dir*8;
            for(int ti=0;ti<12;ti++){float ttx=tb-ev.DirX*ti*3,tty=cy-1+(float)Math.Sin(t*40+ti*0.7f)*(2+ti*0.4f);
                int ta=(int)(a*0.4f*(1f-ti*0.06f));float ts=2f+ti*0.15f;
                using(var b=new SolidBrush(Color.FromArgb(ta,rb[ti%6].R,rb[ti%6].G,rb[ti%6].B))) g.FillEllipse(b,ttx-ts,tty-ts*0.4f,ts*2,ts*0.8f);}
        }

        // ====== WAVE 3: 20 NEW EVENTS ======
        static void PaintStargate(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(180*fade);float op=t<0.2f?t/0.2f:t>0.8f?(1-t)/0.2f:1f;float r=15*op;
            for(int ring=0;ring<5;ring++){float rr=r+ring*4;using(var p=new Pen(Color.FromArgb((int)(a*(0.6f-ring*0.1f)),100,180,255),1.2f)) g.DrawEllipse(p,cx-rr,cy-rr*0.4f,rr*2,rr*0.8f);}
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.15f*op),120,200,255))) g.FillEllipse(b,cx-r,cy-r*0.4f,r*2,r*0.8f);
            var sr=new Random(ev.Seed+(int)(t*100));for(int sp=0;sp<8;sp++){float sa=(float)(sr.NextDouble()*Math.PI*2),sd=r+(float)sr.NextDouble()*6;
                using(var b=new SolidBrush(Color.FromArgb((int)(a*0.5f),200,230,255))) g.FillEllipse(b,cx+(float)Math.Cos(sa+t*20)*sd-1,cy+(float)Math.Sin(sa+t*20)*sd*0.4f-1,2,2);}
        }
        static void PaintTimeTraveler(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(160*fade);
            using(var p=new Pen(Color.FromArgb(a,180,160,100),1f)) g.DrawEllipse(p,cx-5,cy-5,10,10);
            using(var p=new Pen(Color.FromArgb(a,220,200,120),0.8f)){g.DrawLine(p,cx,cy,cx+(float)Math.Cos(t*200)*3,cy+(float)Math.Sin(t*200)*3);g.DrawLine(p,cx,cy,cx+(float)Math.Cos(t*30)*4,cy+(float)Math.Sin(t*30)*4);}
            for(int i=0;i<10;i++){float d=(i+1)*8;int sa=(int)(a*0.3f*(1-i*0.09f));using(var p=new Pen(Color.FromArgb(sa,160,140,80),0.6f)) g.DrawEllipse(p,cx-ev.DirX*d-3,cy-ev.DirY*d-3,6,6);}
        }
        static void PaintSpacePirate(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);cy+=(float)Math.Sin(t*40)*2;
            using(var b=new SolidBrush(Color.FromArgb(a,60,45,30))) g.FillEllipse(b,cx-10,cy-3,20,6);
            float sw=(float)Math.Sin(t*25)*3;PointF[] sail={new PointF(cx-2,cy-3),new PointF(cx+sw,cy-12),new PointF(cx+4,cy-3)};
            using(var b=new SolidBrush(Color.FromArgb(a*3/4,40,35,45))) g.FillPolygon(b,sail);
            using(var b=new SolidBrush(Color.FromArgb(a,200,200,200))) g.FillEllipse(b,cx+sw-1.5f,cy-13,3,3);
            float ex=cx-(ev.DirX>0?10:-10);using(var b=new SolidBrush(Color.FromArgb(a/3,255,100,30))) g.FillEllipse(b,ex-3,cy-2,6,4);
        }
        static void PaintCrystalDragon(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(180*fade);
            for(int seg=0;seg<12;seg++){float d=seg*5,sx=cx-ev.DirX*d,sy=cy-ev.DirY*d+(float)Math.Sin(t*30+seg*0.8f)*3;
                float sz=4f-seg*0.25f;int sa=(int)(a*(1f-seg*0.06f));Color sc=seg%2==0?Color.FromArgb(sa,120,200,255):Color.FromArgb(sa,180,140,255);
                using(var b=new SolidBrush(sc)) g.FillEllipse(b,sx-sz,sy-sz*0.6f,sz*2,sz*1.2f);
                if(seg<6) using(var b=new SolidBrush(Color.FromArgb(sa/3,255,255,255))) g.FillEllipse(b,sx-1,sy-sz*0.4f,2,sz*0.4f);}
            float dir=ev.DirX>0?1:-1;
            using(var p=new Pen(Color.FromArgb(a,160,220,255),1f)){g.DrawLine(p,cx+dir*3,cy-2,cx+dir*6,cy-6);g.DrawLine(p,cx+dir*2,cy-1,cx+dir*5,cy-5);}
            for(int bp=0;bp<5;bp++){float bx=cx+dir*(8+bp*3),by=cy-1+(float)Math.Sin(t*50+bp)*2;int ba=(int)(a*0.4f*(1-bp*0.18f));
                using(var b=new SolidBrush(Color.FromArgb(ba,150,220,255))) g.FillEllipse(b,bx-1,by-1,2,2);}
        }
        static void PaintQuantumRift(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(160*fade);float pulse=t<0.15f?t/0.15f:t>0.85f?(1-t)/0.15f:1f;
            var rng=new Random(ev.Seed+(int)(t*60));
            for(int line=0;line<8;line++){float ang=(float)(line*Math.PI/4+t*15),len=10+(float)rng.NextDouble()*20*pulse;
                float jx=(float)(rng.NextDouble()-0.5)*6,jy=(float)(rng.NextDouble()-0.5)*6;
                Color lc=line%2==0?Color.FromArgb(a,180,100,255):Color.FromArgb(a,100,200,255);
                float ex2=cx+(float)Math.Cos(ang)*len,ey2=cy+(float)Math.Sin(ang)*len;
                using(var p=new Pen(lc,0.8f)){g.DrawLine(p,cx+jx,cy+jy,(cx+ex2)/2+jx*2,(cy+ey2)/2+jy*2);g.DrawLine(p,(cx+ex2)/2+jx*2,(cy+ey2)/2+jy*2,ex2,ey2);}}
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.5f*pulse),200,150,255))) g.FillEllipse(b,cx-4,cy-4,8,8);
        }
        static void PaintCosmicDancer(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);float spin=t*100;
            using(var b=new SolidBrush(Color.FromArgb(a,255,180,220))) g.FillEllipse(b,cx-2,cy-4,4,8);
            for(int limb=0;limb<4;limb++){float la=spin+limb*90,lr=6+(float)Math.Sin(t*45+limb)*2;
                float lx2=cx+(float)Math.Cos(la*Math.PI/180)*lr,ly2=cy+(float)Math.Sin(la*Math.PI/180)*lr;
                using(var p=new Pen(Color.FromArgb(a*3/4,255,200,230),0.8f)) g.DrawLine(p,cx,cy,lx2,ly2);
                using(var b=new SolidBrush(Color.FromArgb(a/2,255,255,200))) g.FillEllipse(b,lx2-1,ly2-1,2,2);}
            for(int r2=0;r2<8;r2++){float d=(r2+1)*5;int ra=(int)(a*0.3f*(1-r2*0.1f));
                using(var b=new SolidBrush(Color.FromArgb(ra,255,180,220))) g.FillEllipse(b,cx-ev.DirX*d-1.5f,cy-ev.DirY*d+(float)Math.Sin(t*50+r2)*4-0.5f,3,1);}
        }
        static void PaintPlasmaSnake(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(180*fade);
            for(int seg=0;seg<20;seg++){float d=seg*4,sx=cx-ev.DirX*d,sy=cy-ev.DirY*d+(float)Math.Sin(t*40+seg*0.6f)*(5+seg*0.2f);
                float sz=3f-seg*0.1f;int sa=(int)(a*(1f-seg*0.04f));int pulse=(int)(sa*(0.7f+0.3f*(float)Math.Sin(t*80+seg)));
                using(var b=new SolidBrush(Color.FromArgb(pulse,50,255,150))) g.FillEllipse(b,sx-sz,sy-sz*0.5f,sz*2,sz);
                if(seg%3==0&&seg<15){float jx=(float)Math.Sin(t*100+seg)*4,jy=(float)Math.Cos(t*90+seg)*3;
                    using(var p=new Pen(Color.FromArgb(sa/2,100,255,200),0.5f)) g.DrawLine(p,sx,sy,sx+jx,sy+jy);}}
            float dir=ev.DirX>0?1:-1;using(var b=new SolidBrush(Color.FromArgb(a,255,50,50))) g.FillEllipse(b,cx+dir*2,cy-1.5f,2,2);
        }
        static void PaintStarSurfer(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);
            using(var b=new SolidBrush(Color.FromArgb(a,80,60,40))) g.FillEllipse(b,cx-8,cy+1,16,3);
            using(var p=new Pen(Color.FromArgb(a/2,255,200,50),0.8f)) g.DrawEllipse(p,cx-8,cy+1,16,3);
            using(var b=new SolidBrush(Color.FromArgb(a,200,200,220))) g.FillEllipse(b,cx-2,cy-6,4,7);
            float lean=(float)Math.Sin(t*30)*3;
            using(var p=new Pen(Color.FromArgb(a*3/4,200,200,220),0.8f)){g.DrawLine(p,cx,cy-3,cx-5,cy-4+lean);g.DrawLine(p,cx,cy-3,cx+5,cy-4-lean);}
            for(int i=0;i<12;i++){float d=(i+1)*5;int sa=(int)(a*0.25f*(1-i*0.07f));
                using(var b=new SolidBrush(Color.FromArgb(sa,255,220,100))) g.FillEllipse(b,cx-ev.DirX*d-1,cy+3-ev.DirY*d+(float)Math.Sin(t*50+i)*2-1,2,2);}
        }
        static void PaintVoidMoth(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(150*fade);float wf=(float)Math.Sin(t*55)*8;
            for(int side=-1;side<=1;side+=2){float wx=cx+side*wf;
                using(var b=new SolidBrush(Color.FromArgb(a*2/3,30,20,50))) g.FillEllipse(b,wx-4+(side>0?0:-4),cy-5,8,10);
                for(int sp=0;sp<3;sp++){int spa=(int)(a*(0.5f+0.3f*(float)Math.Sin(t*60+sp+side)));
                    using(var b=new SolidBrush(Color.FromArgb(spa,100,50,200))) g.FillEllipse(b,wx+side*(1+sp)-1,cy-2+sp*2-1,2,2);}}
            using(var b=new SolidBrush(Color.FromArgb(a,50,30,70))) g.FillEllipse(b,cx-1.5f,cy-3,3,6);
            using(var p=new Pen(Color.FromArgb(a/2,120,80,180),0.5f)){g.DrawLine(p,cx,cy-3,cx-3,cy-7);g.DrawLine(p,cx,cy-3,cx+3,cy-7);}
        }
        static void PaintNeonJellyfish(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);float pulse=(float)Math.Sin(t*35);
            float dr=6+pulse*1.5f;
            using(var b=new SolidBrush(Color.FromArgb(a*2/3,255,50,200))) g.FillEllipse(b,cx-dr,cy-dr*0.7f,dr*2,dr*1.2f);
            using(var b=new SolidBrush(Color.FromArgb(a/4,255,150,230))) g.FillEllipse(b,cx-dr*0.6f,cy-dr*0.4f,dr*1.2f,dr*0.6f);
            for(int ten=0;ten<7;ten++){float tx=cx-5+ten*1.7f;for(int seg=0;seg<8;seg++){
                float sy=cy+dr*0.5f+seg*2.5f+(float)Math.Sin(t*30+ten+seg*0.5f)*2,sx=tx+(float)Math.Sin(t*25+ten*0.7f+seg*0.3f)*1.5f;
                int sa=(int)(a*0.5f*(1-seg*0.1f));Color tc=ten%2==0?Color.FromArgb(sa,255,80,220):Color.FromArgb(sa,80,200,255);
                using(var b=new SolidBrush(tc)) g.FillEllipse(b,sx-0.8f,sy-0.5f,1.6f,1f);}}
        }
        static void PaintGalaxySpiral(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(140*fade);float op=t<0.15f?t/0.15f:t>0.85f?(1-t)/0.15f:1f;float spin=t*60;
            for(int arm=0;arm<3;arm++){float armOff=arm*120;for(int pt=0;pt<30;pt++){
                float ang=(float)((spin+armOff+pt*12)*Math.PI/180),r2=(pt*0.8f+2)*op;
                float px=cx+(float)Math.Cos(ang)*r2,py=cy+(float)Math.Sin(ang)*r2*0.5f;int pa=(int)(a*0.5f*(1-pt*0.025f));
                Color pc=arm==0?Color.FromArgb(pa,150,180,255):arm==1?Color.FromArgb(pa,255,180,150):Color.FromArgb(pa,180,150,255);
                using(var b=new SolidBrush(pc)) g.FillEllipse(b,px-1,py-1,2,2);}}
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.4f*op),255,250,220))) g.FillEllipse(b,cx-3,cy-2,6,4);
        }
        static void PaintMagicCarpet(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);float wave=(float)Math.Sin(t*30)*2;
            PointF[] carpet={new PointF(cx-10,cy+wave),new PointF(cx-6,cy-2-wave),new PointF(cx+6,cy-2+wave),new PointF(cx+10,cy-wave)};
            using(var b=new SolidBrush(Color.FromArgb(a*3/4,120,30,30))) g.FillPolygon(b,carpet);
            using(var b=new SolidBrush(Color.FromArgb(a/2,200,160,50))) g.FillEllipse(b,cx-3,cy-2,6,2);
            for(int i=0;i<8;i++){float d=(i+1)*6;int sa=(int)(a*0.3f*(1-i*0.1f));
                using(var b=new SolidBrush(Color.FromArgb(sa,255,200,80))) g.FillEllipse(b,cx-ev.DirX*d-1,cy-ev.DirY*d+(float)Math.Sin(t*40+i)*3-1,2,2);}
        }
        static void PaintSpaceLantern(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(160*fade);float fl=(float)(0.7+0.3*Math.Sin(t*70));
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.7f*fl),255,180,80))) g.FillEllipse(b,cx-4,cy-5,8,10);
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.4f*fl),255,240,180))) g.FillEllipse(b,cx-2,cy-2,4,4);
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.1f*fl),255,200,100))) g.FillEllipse(b,cx-8,cy-8,16,16);
        }
        static void PaintCosmicOwl(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);float wf=(float)Math.Sin(t*40)*6;
            using(var b=new SolidBrush(Color.FromArgb(a,80,65,50))) g.FillEllipse(b,cx-4,cy-3,8,8);
            for(int side=-1;side<=1;side+=2) using(var b=new SolidBrush(Color.FromArgb(a*3/4,90,75,55))) g.FillEllipse(b,cx+side*4+side*Math.Abs(wf)*0.5f,cy-1+wf*side*0.3f,6,4);
            for(int side=-1;side<=1;side+=2){using(var b=new SolidBrush(Color.FromArgb(a,255,200,50))) g.FillEllipse(b,cx+side*2-1.5f,cy-2,3,3);
                using(var b=new SolidBrush(Color.FromArgb(a,30,20,10))) g.FillEllipse(b,cx+side*2-0.5f,cy-1,1.2f,1.2f);}
            for(int i=0;i<6;i++){float d=(i+1)*6;int sa=(int)(a*0.2f*(1-i*0.12f));
                using(var b=new SolidBrush(Color.FromArgb(sa,255,220,100))) g.FillEllipse(b,cx-ev.DirX*d-1,cy-ev.DirY*d-1,2,2);}
        }
        static void PaintWarpDrive(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(180*fade);float it=t<0.2f?t/0.2f:t>0.7f?(1-t)/0.3f:1f;
            var rng=new Random(ev.Seed);for(int line=0;line<20;line++){float ang=(float)(rng.NextDouble()*Math.PI*2);
                float r1=(8+(float)rng.NextDouble()*5)*it,r2=(r1+15+(float)rng.NextDouble()*20)*it;
                int la=(int)(a*0.5f*(0.5f+(float)Math.Sin(t*60+line)*0.5f));
                using(var p=new Pen(Color.FromArgb(la,150,200,255),0.7f)) g.DrawLine(p,cx+(float)Math.Cos(ang)*r1,cy+(float)Math.Sin(ang)*r1*0.5f,cx+(float)Math.Cos(ang)*r2,cy+(float)Math.Sin(ang)*r2*0.5f);}
            float fr=4*it;using(var b=new SolidBrush(Color.FromArgb((int)(a*0.6f*it),200,230,255))) g.FillEllipse(b,cx-fr,cy-fr*0.5f,fr*2,fr);
        }
        static void PaintSpaceKoi(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);cy+=(float)Math.Sin(t*35)*3;
            using(var b=new SolidBrush(Color.FromArgb(a,255,120,50))) g.FillEllipse(b,cx-6,cy-3,12,6);
            using(var b=new SolidBrush(Color.FromArgb(a*3/4,255,240,230))){g.FillEllipse(b,cx-2,cy-2,5,3);g.FillEllipse(b,cx+2,cy,3,2);}
            float dir=ev.DirX>0?-1:1;float tw=(float)Math.Sin(t*45)*4;
            PointF[] tail={new PointF(cx+dir*6,cy),new PointF(cx+dir*10,cy-3+tw),new PointF(cx+dir*10,cy+3+tw)};
            using(var b=new SolidBrush(Color.FromArgb(a*2/3,255,100,40))) g.FillPolygon(b,tail);
            using(var b=new SolidBrush(Color.FromArgb(a,30,30,30))) g.FillEllipse(b,cx-dir*3.5f,cy-1,1.5f,1.5f);
            for(int i=0;i<5;i++){float bx=cx+dir*(8+i*4),by=cy-2+(float)Math.Sin(t*40+i)*3;int ba=(int)(a*0.2f*(1-i*0.15f));
                using(var p=new Pen(Color.FromArgb(ba,200,220,255),0.5f)) g.DrawEllipse(p,bx-1.5f,by-1.5f,3,3);}
        }
        static void PaintCelestialHarp(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(150*fade);float op=t<0.15f?t/0.15f:t>0.85f?(1-t)/0.15f:1f;
            using(var p=new Pen(Color.FromArgb(a,200,180,100),1.2f)){g.DrawArc(p,cx-12,cy-15,24,30,200,140);g.DrawLine(p,cx-8,cy+10,cx+8,cy-12);}
            for(int s=0;s<7;s++){float sx=cx-6+s*2,vib=(float)Math.Sin(t*80+s*3)*(1+s*0.3f)*op;
                int sa=(int)(a*0.6f*(0.5f+0.5f*(float)Math.Sin(t*60+s*2)));Color sc=s%2==0?Color.FromArgb(sa,255,230,150):Color.FromArgb(sa,200,180,255);
                using(var p=new Pen(sc,0.5f)) g.DrawBezier(p,sx,cy+8,sx+vib*0.5f,cy+2,sx+vib,cy-4,sx+s*0.5f,cy-10);}
            for(int n=0;n<5;n++){float ny=cy-10-n*5-t*30*op,nx=cx+(float)Math.Sin(t*20+n*2)*8;int na=(int)(a*0.4f*(1-n*0.15f));
                if(na>0) using(var b=new SolidBrush(Color.FromArgb(na,255,240,180))) g.FillEllipse(b,nx-1,ny-1,2,2);}
        }
        static void PaintMeteorDragon(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(180*fade);
            for(int seg=0;seg<15;seg++){float d=seg*4,sx=cx-ev.DirX*d,sy=cy-ev.DirY*d+(float)Math.Sin(t*35+seg*0.7f)*3;
                float sz=3.5f-seg*0.15f;int sa=(int)(a*(1f-seg*0.05f));
                Color sc=Color.FromArgb(sa,255,(int)(120-seg*5),(int)(30+seg*3));
                using(var b=new SolidBrush(sc)) g.FillEllipse(b,sx-sz,sy-sz*0.5f,sz*2,sz);
                if(seg%2==0) using(var b=new SolidBrush(Color.FromArgb(sa/3,255,200,50))) g.FillEllipse(b,sx-1,sy-sz*0.3f,2,sz*0.3f);}
            float dir=ev.DirX>0?1:-1;
            using(var p=new Pen(Color.FromArgb(a,255,200,50),1f)){g.DrawLine(p,cx+dir*3,cy-2,cx+dir*7,cy-5);g.DrawLine(p,cx+dir*2,cy-1,cx+dir*6,cy-4);}
            // Fire breath
            for(int f=0;f<6;f++){float fx=cx+dir*(5+f*3),fy=cy-1+(float)Math.Sin(t*70+f)*2;int fa=(int)(a*0.5f*(1-f*0.12f));
                using(var b=new SolidBrush(Color.FromArgb(fa,255,150,30))) g.FillEllipse(b,fx-1.5f,fy-1,3,2);}
        }
        static void PaintNorthernLights(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(120*fade);float op=t<0.15f?t/0.15f:t>0.85f?(1-t)/0.15f:1f;
            Color[] aur={Color.FromArgb(50,255,100),Color.FromArgb(80,200,255),Color.FromArgb(150,50,255),Color.FromArgb(255,100,200)};
            for(int band=0;band<4;band++){for(int pt=0;pt<20;pt++){
                float px=cx-40+pt*4+(float)Math.Sin(t*15+band*2+pt*0.3f)*5;
                float py=cy-15+band*10+(float)Math.Sin(t*20+pt*0.5f+band)*6;
                float stretch=8+(float)Math.Sin(t*25+pt+band)*3;int pa=(int)(a*0.4f*op*(0.5f+0.5f*(float)Math.Sin(t*30+pt+band)));
                using(var b=new SolidBrush(Color.FromArgb(pa,aur[band].R,aur[band].G,aur[band].B))) g.FillEllipse(b,px-2,py-stretch*0.5f,4,stretch);}}
        }
        static void PaintDarkMatter(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(140*fade);float op=t<0.15f?t/0.15f:t>0.85f?(1-t)/0.15f:1f;
            // Invisible mass — seen only by gravitational lensing
            float r=12*op;
            for(int ring=0;ring<4;ring++){float rr=r+ring*5;int ra=(int)(a*0.2f*(1-ring*0.04f));
                using(var p=new Pen(Color.FromArgb(ra,80,60,120),0.8f)) g.DrawEllipse(p,cx-rr,cy-rr,rr*2,rr*2);}
            // Dark core
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.3f*op),20,10,40))) g.FillEllipse(b,cx-r*0.6f,cy-r*0.6f,r*1.2f,r*1.2f);
            // Bent starlight particles orbiting
            var rng=new Random(ev.Seed);
            for(int pt=0;pt<12;pt++){float ang=(float)(rng.NextDouble()*Math.PI*2)+t*20;float pr=r+2+(float)rng.NextDouble()*8;
                float px=cx+(float)Math.Cos(ang)*pr,py=cy+(float)Math.Sin(ang)*pr;
                int pa=(int)(a*0.5f*(0.5f+0.5f*(float)Math.Sin(t*50+pt)));
                using(var b=new SolidBrush(Color.FromArgb(pa,160,140,200))) g.FillEllipse(b,px-1,py-1,2,2);}
        }

        /// <summary>Paints a shooting star orbiting inside a button's perimeter.
        /// Designed to stay within bounds — no clipping. Rainbow trail, white head.</summary>
        public static void PaintOrbitingStar(Graphics g, int w, int h, float phase, int cornerRadius)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // Inset 2px so nothing touches the edge
            int inset = 2;
            int iw = w - inset * 2, ih = h - inset * 2;
            int radius = Math.Max(1, cornerRadius - inset);
            float straightW = iw - 1 - 2 * radius;
            float straightH = ih - 1 - 2 * radius;
            float cornerArc = (float)(Math.PI * radius / 2.0);
            float perimeter = 2 * straightW + 2 * straightH + 4 * cornerArc;

            float t = (phase / (float)(Math.PI * 2));
            t = t - (float)Math.Floor(t);
            int trailCount = 50;
            float trailSpacing = 0.010f;

            for (int i = trailCount; i >= 0; i--)
            {
                float tt = t - i * trailSpacing;
                if (tt < 0) tt += 1.0f;
                PointF pt = OrbitPoint(tt, iw, ih, radius, straightW, straightH, cornerArc, perimeter);
                // Offset by inset
                pt = new PointF(pt.X + inset, pt.Y + inset);

                if (i == 0)
                {
                    // Star head — white, contained glow
                    for (int glow = 3; glow >= 0; glow--)
                    {
                        float sz = 4 + glow * 3;
                        int alpha = glow == 0 ? 255 : glow == 1 ? 200 : Math.Max(20, (int)(140.0 / (glow + 1)));
                        using (var brush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                            g.FillEllipse(brush, pt.X - sz / 2, pt.Y - sz / 2, sz, sz);
                    }

                    // Cross sparkle — short arms that stay in bounds
                    int sparkAlpha = (int)(230 + 25 * Math.Sin(phase * 4));
                    float armLen = 6 + 2 * (float)Math.Sin(phase * 3);
                    using (var pen = new Pen(Color.FromArgb(sparkAlpha, 255, 255, 255), 1.5f))
                    {
                        g.DrawLine(pen, pt.X - armLen, pt.Y, pt.X + armLen, pt.Y);
                        g.DrawLine(pen, pt.X, pt.Y - armLen, pt.X, pt.Y + armLen);
                    }
                    float diagLen = armLen * 0.5f;
                    using (var pen = new Pen(Color.FromArgb(sparkAlpha / 2, 255, 255, 255), 1f))
                    {
                        g.DrawLine(pen, pt.X - diagLen, pt.Y - diagLen, pt.X + diagLen, pt.Y + diagLen);
                        g.DrawLine(pen, pt.X + diagLen, pt.Y - diagLen, pt.X - diagLen, pt.Y + diagLen);
                    }
                }
                else
                {
                    float fade = 1.0f - (float)i / trailCount;
                    float sz = 5f * fade;
                    int alpha = (int)(230 * fade * fade);
                    if (alpha < 5) continue;

                    // Rainbow trail
                    float hue = ((float)i / trailCount * 360 + phase * 60) % 360;
                    Color rainbow = HsvToColor(hue, 0.85f, 1.0f, alpha);
                    using (var brush = new SolidBrush(rainbow))
                        g.FillEllipse(brush, pt.X - sz / 2, pt.Y - sz / 2, sz, sz);

                    // Subtle white glow
                    if (fade > 0.5f)
                    {
                        float glowSz = sz * 1.8f;
                        int glowA = (int)(35 * fade);
                        using (var brush = new SolidBrush(Color.FromArgb(glowA, 255, 255, 255)))
                            g.FillEllipse(brush, pt.X - glowSz / 2, pt.Y - glowSz / 2, glowSz, glowSz);
                    }
                }
            }
        }

        /// <summary>HSV to ARGB color with specified alpha.</summary>
        private static Color HsvToColor(float h, float s, float v, int alpha)
        {
            h = h % 360;
            float c = v * s;
            float x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            float m = v - c;
            float r1, g1, b1;
            if (h < 60) { r1 = c; g1 = x; b1 = 0; }
            else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }
            int r = Math.Min(255, (int)((r1 + m) * 255));
            int g = Math.Min(255, (int)((g1 + m) * 255));
            int b = Math.Min(255, (int)((b1 + m) * 255));
            return Color.FromArgb(alpha, r, g, b);
        }

        private static PointF OrbitPoint(float t, int w, int h, int radius, float straightW, float straightH, float cornerArc, float perimeter)
        {
            float d = t * perimeter;
            if (d < straightW) return new PointF(radius + d, 0);
            d -= straightW;
            if (d < cornerArc)
            {
                float angle = (float)(-Math.PI / 2 + (d / cornerArc) * (Math.PI / 2));
                return new PointF(w - 1 - radius + (float)Math.Cos(angle) * radius, radius + (float)Math.Sin(angle) * radius);
            }
            d -= cornerArc;
            if (d < straightH) return new PointF(w - 1, radius + d);
            d -= straightH;
            if (d < cornerArc)
            {
                float angle = (float)((d / cornerArc) * (Math.PI / 2));
                return new PointF(w - 1 - radius + (float)Math.Cos(angle) * radius, h - 1 - radius + (float)Math.Sin(angle) * radius);
            }
            d -= cornerArc;
            if (d < straightW) return new PointF(w - 1 - radius - d, h - 1);
            d -= straightW;
            if (d < cornerArc)
            {
                float angle = (float)(Math.PI / 2 + (d / cornerArc) * (Math.PI / 2));
                return new PointF(radius + (float)Math.Cos(angle) * radius, h - 1 - radius + (float)Math.Sin(angle) * radius);
            }
            d -= cornerArc;
            if (d < straightH) return new PointF(0, h - 1 - radius - d);
            d -= straightH;
            if (d < cornerArc)
            {
                float angle = (float)(Math.PI + (d / cornerArc) * (Math.PI / 2));
                return new PointF(radius + (float)Math.Cos(angle) * radius, radius + (float)Math.Sin(angle) * radius);
            }
            return new PointF(radius, 0);
        }
    }

    /// <summary>Panel that can paint OVER its child controls by removing WS_CLIPCHILDREN.
    /// Combined with double buffering, this allows glow effects around buttons without clipping or flicker.</summary>
    class GlowPanel : Panel
    {
        public GlowPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }

        protected override CreateParams CreateParams
        {
            get {
                var cp = base.CreateParams;
                // Remove WS_CLIPCHILDREN so we can paint over child controls
                cp.Style &= ~0x02000000; // ~WS_CLIPCHILDREN
                return cp;
            }
        }

        /// <summary>Override to paint glow effects AFTER base paint (which draws children).</summary>
        public Action<Graphics> PaintGlow;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Paint glow ON TOP of children
            PaintGlow?.Invoke(e.Graphics);
        }
    }
}
