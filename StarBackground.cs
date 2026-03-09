// StarBackground.cs — THE single star system used by ALL surfaces.
// Contains: StarBackground (cache + paint API), ShootingStar, CelestialEvents.
//
// Every form creates one StarBackground and calls the same methods:
//   _stars.Paint(g, w, h, ox, oy)          — starfield at offset
//   _stars.PaintGlassTint(g, w, h, path)   — frosted glass card (installer)
//   _stars.PaintChildBg(g, w, h, ox, oy, cw, ch) — child control bg
//   _stars.Tick()                            — advance twinkle animation
//
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AngryAudio
{
    /// <summary>
    /// THE single star background system — used by ALL surfaces.
    /// Handles star caching, shooting stars, celestial events, and card glass tint.
    /// 
    /// Usage (all surfaces):
    ///   var stars = new StarBackground(invalidateCallback);
    ///   
    ///   // In paint timer: stars.Tick();
    ///   
    ///   // Paint full background (form-level, no offset):
    ///   stars.Paint(g, w, h);
    ///   
    ///   // Paint at offset (child panel in Options/Welcome):
    ///   stars.Paint(g, w, h, offsetX, offsetY);
    ///   stars.Paint(g, w, h, offsetX, offsetY, dim: true, shootingStar: false);
    ///   
    ///   // Paint frosted glass card (installer single-surface):
    ///   stars.PaintGlassTint(g, w, h, path);
    ///   
    ///   // Paint child control bg (toggle/slider inside card):
    ///   stars.PaintChildBg(g, w, h, offsetX, offsetY, childW, childH);
    /// </summary>
    public class StarBackground : IDisposable
    {
        // Static base — built once on resize, never on tick
        Bitmap _base, _baseDim;
        // Twinkle overlay — repainted each tick (only twinkling stars)
        Bitmap _twinkleOverlay, _twinkleOverlayDim;
        int _cacheW, _cacheH;
        int _twinkleTick;
        bool _twinkleDirty = true;
        public ShootingStar Shooting;
        public CelestialEvents Celestial;
        const int SEED = 42;
        static readonly Color TINT = DarkTheme.GlassTint;

        // Pre-computed star data — computed once on resize
        struct StarData {
            public int X, Y;
            public float Radius;
            public int BaseAlpha;
            public byte R, G, B;
            public bool Twinkles;
            public double Phase, Speed;
        }
        StarData[] _starsNorm, _starsDim;

        public StarBackground(Action invalidate)
        {
            Shooting = new ShootingStar(invalidate);
            Shooting.Start();
            Celestial = new CelestialEvents(invalidate);
            Celestial.Start();
        }

        public void Tick() { _twinkleTick++; _twinkleDirty = true; }

        StarData[] ComputeStars(int w, int h, float alphaMul)
        {
            var rng = new Random(SEED);
            // Denser starfield — more stars for a richer sky
            int count = Math.Max(20, Math.Min(280, (w * h) / 1200));
            var arr = new StarData[count];
            for (int i = 0; i < count; i++) {
                arr[i].X = rng.Next(w);
                arr[i].Y = rng.Next(h);
                int tier = rng.Next(100);
                if (tier < 35) {
                    // Tiny dim dust — faintest specks, the deep background
                    arr[i].Radius = 0.4f + (float)(rng.NextDouble() * 0.3);
                    arr[i].BaseAlpha = (int)((30 + rng.Next(25)) * alphaMul);
                    arr[i].R = arr[i].G = arr[i].B = 255;
                } else if (tier < 58) {
                    // Small white stars — classic
                    arr[i].Radius = 0.6f + (float)(rng.NextDouble() * 0.5);
                    arr[i].BaseAlpha = (int)((55 + rng.Next(35)) * alphaMul);
                    arr[i].R = arr[i].G = arr[i].B = 255;
                } else if (tier < 73) {
                    // Medium white stars
                    arr[i].Radius = 0.8f + (float)(rng.NextDouble() * 0.7);
                    arr[i].BaseAlpha = (int)((70 + rng.Next(50)) * alphaMul);
                    arr[i].R = arr[i].G = arr[i].B = 255;
                } else if (tier < 79) {
                    // Cool blue-white stars (B-type) — icy shimmer
                    arr[i].Radius = 0.7f + (float)(rng.NextDouble() * 0.6);
                    arr[i].BaseAlpha = (int)((60 + rng.Next(45)) * alphaMul);
                    arr[i].R = 180; arr[i].G = 210; arr[i].B = 255;
                } else if (tier < 84) {
                    // Warm yellow stars (G-type, sun-like)
                    arr[i].Radius = 0.8f + (float)(rng.NextDouble() * 0.6);
                    arr[i].BaseAlpha = (int)((55 + rng.Next(40)) * alphaMul);
                    arr[i].R = 255; arr[i].G = 240; arr[i].B = 200;
                } else if (tier < 88) {
                    // Orange-warm stars (K-type)
                    arr[i].Radius = 0.9f + (float)(rng.NextDouble() * 0.5);
                    arr[i].BaseAlpha = (int)((50 + rng.Next(35)) * alphaMul);
                    arr[i].R = 255; arr[i].G = 200; arr[i].B = 150;
                } else if (tier < 91) {
                    // Red giant hints (M-type) — subtle warm red
                    arr[i].Radius = 1.1f + (float)(rng.NextDouble() * 0.7);
                    arr[i].BaseAlpha = (int)((45 + rng.Next(30)) * alphaMul);
                    arr[i].R = 255; arr[i].G = 160; arr[i].B = 130;
                } else if (tier < 94) {
                    // Bright beacon stars — rare, larger, attention-getting
                    arr[i].Radius = 1.3f + (float)(rng.NextDouble() * 0.8);
                    arr[i].BaseAlpha = (int)((90 + rng.Next(50)) * alphaMul);
                    arr[i].R = arr[i].G = arr[i].B = 255;
                } else {
                    // Accent-colored stars — app theme color
                    arr[i].Radius = 1.0f + (float)(rng.NextDouble() * 0.8);
                    arr[i].BaseAlpha = (int)((65 + rng.Next(55)) * alphaMul);
                    arr[i].R = DarkTheme.Accent.R; arr[i].G = DarkTheme.Accent.G; arr[i].B = DarkTheme.Accent.B;
                }
                // More stars twinkle, with varied speeds for depth
                arr[i].Twinkles = rng.Next(100) < 55;
                arr[i].Phase = rng.NextDouble() * Math.PI * 2;
                // Varied twinkle speeds — some slow breathe, some fast shimmer
                arr[i].Speed = 0.03 + rng.NextDouble() * 0.12;
            }
            return arr;
        }

        void RenderStars(Bitmap bmp, StarData[] stars, bool twinkleOnly, int tick)
        {
            using (var g = Graphics.FromImage(bmp)) {
                // Don't clear — nebula clouds may already be painted
                if (!twinkleOnly) { /* base layer already has nebula, just add stars on top */ }
                else { g.Clear(Color.Transparent); } // twinkle overlay always starts clean
                for (int i = 0; i < stars.Length; i++) {
                    var s = stars[i];
                    if (twinkleOnly && !s.Twinkles) continue;
                    if (!twinkleOnly && s.Twinkles) continue;

                    int alpha = s.BaseAlpha;
                    if (s.Twinkles && tick > 0) {
                        // Multi-harmonic twinkle: primary wave + secondary shimmer + rare bright flicker
                        double primary = Math.Sin(tick * s.Speed + s.Phase);
                        double secondary = Math.Sin(tick * s.Speed * 2.3 + s.Phase * 1.7) * 0.3;
                        // Occasional bright flash — like atmospheric scintillation
                        double flicker = Math.Pow(Math.Max(0, Math.Sin(tick * s.Speed * 0.37 + s.Phase * 3.1)), 8) * 0.5;
                        double wave = primary + secondary + flicker;
                        wave = Math.Max(-1, Math.Min(1.5, wave)); // allow slight over-bright for flicker
                        alpha = (int)(s.BaseAlpha * (0.3 + 0.85 * (wave * 0.5 + 0.5)) * 2.0);
                        alpha = Math.Max(10, Math.Min(240, alpha));
                    }
                    if (alpha <= 0) continue;

                    using (var b = new SolidBrush(Color.FromArgb(alpha, s.R, s.G, s.B))) {
                        if (s.Radius <= 0.7f)
                            g.FillRectangle(b, s.X, s.Y, 1, 1);
                        else if (s.Radius <= 1.2f)
                            g.FillEllipse(b, s.X - s.Radius, s.Y - s.Radius, s.Radius * 2, s.Radius * 2);
                        else {
                            // Larger stars get a subtle glow halo
                            g.FillEllipse(b, s.X - s.Radius, s.Y - s.Radius, s.Radius * 2, s.Radius * 2);
                            int glowAlpha = alpha / 5;
                            if (glowAlpha > 3) {
                                float gr = s.Radius * 2.2f;
                                using (var gb = new SolidBrush(Color.FromArgb(glowAlpha, s.R, s.G, s.B)))
                                    g.FillEllipse(gb, s.X - gr, s.Y - gr, gr * 2, gr * 2);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Renders subtle nebula/gas cloud patches — very faint, organic, behind everything.
        /// Uses seeded random for consistency. Multiple soft elliptical patches with varied colors.
        /// </summary>
        void RenderNebulaClouds(Bitmap bmp, int w, int h, float alphaMul)
        {
            using (var g = Graphics.FromImage(bmp)) {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var rng = new Random(SEED + 7777); // Different seed from stars
                int patchCount = 3 + rng.Next(3);  // 3-5 nebula patches

                // Nebula color palette — very subtle, space-like
                Color[] nebulaColors = new Color[] {
                    Color.FromArgb(60, 80, 120),   // Deep blue
                    Color.FromArgb(80, 50, 100),   // Purple
                    Color.FromArgb(50, 80, 90),    // Teal-grey
                    Color.FromArgb(90, 60, 80),    // Dusty rose
                    Color.FromArgb(40, 70, 90),    // Steel blue
                    Color.FromArgb(70, 55, 95),    // Lavender-dark
                };

                for (int p = 0; p < patchCount; p++)
                {
                    float cx = (float)(rng.NextDouble() * w);
                    float cy = (float)(rng.NextDouble() * h);
                    Color nc = nebulaColors[rng.Next(nebulaColors.Length)];

                    // Each patch is made of several overlapping soft ellipses
                    int layers = 4 + rng.Next(4);
                    for (int layer = 0; layer < layers; layer++)
                    {
                        float ox = cx + (float)(rng.NextDouble() - 0.5) * w * 0.15f;
                        float oy = cy + (float)(rng.NextDouble() - 0.5) * h * 0.15f;
                        float rw = w * (0.08f + (float)rng.NextDouble() * 0.18f);
                        float rh = h * (0.06f + (float)rng.NextDouble() * 0.14f);

                        // Very low alpha — these are barely-there wisps
                        int baseA = (int)((4 + rng.Next(6)) * alphaMul);
                        if (baseA < 1) continue;

                        using (var path = new System.Drawing.Drawing2D.GraphicsPath()) {
                            path.AddEllipse(ox - rw / 2, oy - rh / 2, rw, rh);
                            using (var pgb = new System.Drawing.Drawing2D.PathGradientBrush(path)) {
                                pgb.CenterColor = Color.FromArgb(baseA, nc.R, nc.G, nc.B);
                                pgb.SurroundColors = new Color[] { Color.FromArgb(0, nc.R, nc.G, nc.B) };
                                pgb.FocusScales = new PointF(0.3f, 0.3f);
                                g.FillPath(pgb, path);
                            }
                        }
                    }
                }
            }
        }

        void EnsureBase(int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            if (_base != null && _cacheW == w && _cacheH == h) return;
            _cacheW = w; _cacheH = h;

            if (_base != null) _base.Dispose(); if (_baseDim != null) _baseDim.Dispose();
            if (_twinkleOverlay != null) _twinkleOverlay.Dispose(); if (_twinkleOverlayDim != null) _twinkleOverlayDim.Dispose();

            _starsNorm = ComputeStars(w, h, 1.0f);
            _starsDim = ComputeStars(w, h, 0.35f);

            _base = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _baseDim = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _twinkleOverlay = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _twinkleOverlayDim = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            // Paint subtle nebula clouds FIRST (behind stars)
            RenderNebulaClouds(_base, w, h, 1.0f);
            RenderNebulaClouds(_baseDim, w, h, 0.35f);

            RenderStars(_base, _starsNorm, false, 0);      // static stars only
            RenderStars(_baseDim, _starsDim, false, 0);
            _twinkleDirty = true;
        }

        void EnsureTwinkle()
        {
            if (!_twinkleDirty) return;
            _twinkleDirty = false;
            if (_twinkleOverlay == null || _starsNorm == null) return;
            RenderStars(_twinkleOverlay, _starsNorm, true, _twinkleTick);
            RenderStars(_twinkleOverlayDim, _starsDim, true, _twinkleTick);
        }

        public void Paint(Graphics g, int w, int h, int ox = 0, int oy = 0, bool dim = false, bool shootingStar = true)
        {
            try {
                EnsureBase(w, h);
                EnsureTwinkle();
                var b = dim ? _baseDim : _base;
                var t = dim ? _twinkleOverlayDim : _twinkleOverlay;
                if (b != null) g.DrawImage(b, -ox, -oy);
                if (t != null) g.DrawImage(t, -ox, -oy);
                if (shootingStar) {
                    g.TranslateTransform(-ox, -oy);
                    if (Shooting != null) DarkTheme.PaintShootingStar(g, w, h, Shooting);
                    if (Celestial != null) DarkTheme.PaintCelestialEvent(g, w, h, Celestial);
                    g.ResetTransform();
                }
            } catch { try { g.ResetTransform(); } catch { } }
        }

        public void PaintGlassTint(Graphics g, int w, int h, System.Drawing.Drawing2D.GraphicsPath path)
        {
            EnsureBase(w, h);
            EnsureTwinkle();
            using (var tint = new SolidBrush(TINT))
                g.FillPath(tint, path);
            var oldClip = g.Clip;
            g.SetClip(path);
            if (_baseDim != null) g.DrawImage(_baseDim, 0, 0);
            if (_twinkleOverlayDim != null) g.DrawImage(_twinkleOverlayDim, 0, 0);
            g.Clip = oldClip;
        }

        public void PaintChildBg(Graphics g, int w, int h, int ox, int oy, int childW, int childH)
        {
            try {
                EnsureBase(w, h);
                EnsureTwinkle();
                using (var bg = new SolidBrush(DarkTheme.BG))
                    g.FillRectangle(bg, 0, 0, childW, childH);
                if (_base != null) g.DrawImage(_base, -ox, -oy);
                if (_twinkleOverlay != null) g.DrawImage(_twinkleOverlay, -ox, -oy);
                
                // Add shooting stars and celestial events to child background for transparency parity
                g.TranslateTransform(-ox, -oy);
                if (Shooting != null) DarkTheme.PaintShootingStar(g, w, h, Shooting);
                if (Celestial != null) DarkTheme.PaintCelestialEvent(g, w, h, Celestial);
                g.ResetTransform();

                using (var tint = new SolidBrush(TINT))
                    g.FillRectangle(tint, 0, 0, childW, childH);
                if (_baseDim != null) g.DrawImage(_baseDim, -ox, -oy);
                if (_twinkleOverlayDim != null) g.DrawImage(_twinkleOverlayDim, -ox, -oy);
                
                // Dimmed shooting stars too
                g.TranslateTransform(-ox, -oy);
                if (Shooting != null) DarkTheme.PaintShootingStar(g, w, h, Shooting);
                if (Celestial != null) DarkTheme.PaintCelestialEvent(g, w, h, Celestial);
                g.ResetTransform();
            } catch { try { g.ResetTransform(); } catch { } }
        }

        public void Dispose()
        {
            if (Shooting != null) { Shooting.Stop(); Shooting.Dispose(); }
            if (Celestial != null) { Celestial.Stop(); Celestial.Dispose(); }
            if (_base != null) _base.Dispose(); if (_baseDim != null) _baseDim.Dispose();
            if (_twinkleOverlay != null) _twinkleOverlay.Dispose(); if (_twinkleOverlayDim != null) _twinkleOverlayDim.Dispose();
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
            public int MeteorType;      // 0=streak, 1=fireball, 2=comet, 3=whisper, 4=bolt, 5=fragment, 6=twin, 7=phantom, 8=flare, 9=ember_trail, 10=cascade, 11=debris, 12=slowburn, 13=sparkler, 14=skimmer, 15=splitter, 16=corkscrew, 17=fadechain, 18=iridescent, 19=smoketrail, 20=earthgrazer, 21=flashfreeze, 22=doublehead
            public float TwinOffset;    // for twin type — perpendicular offset
            public float FragAngle;     // for fragment type — burst angle
            public float Param1;        // multipurpose: spiral frequency, ricochet bounce point, etc.
        }

        public Meteor[] Stars = new Meteor[75]; // 75 slots for dev-click meteor storm

        private static readonly Random _rng = new Random();
        private Timer _timer;
        private Action _invalidateCallback;

        public ShootingStar(Action invalidateCallback)
        {
            _invalidateCallback = invalidateCallback;
            _timer = new Timer { Interval = 30 };
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

        /// <summary>Brightness boost for click-spawned stars. Position/direction already set by LaunchStar.</summary>
        private void ForceVisible(int idx)
        {
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

            // Spawn zone: bias toward upper-right area (radiant source) with spread
            double spawnRoll = _rng.NextDouble();
            if (spawnRoll < 0.30) {
                // 30% — Top edge: full width
                Stars[idx].StartX = (float)_rng.NextDouble();
                Stars[idx].StartY = -0.02f - (float)_rng.NextDouble() * 0.05f;
            } else if (spawnRoll < 0.50) {
                // 20% — Right edge: full height
                Stars[idx].StartX = 1.02f + (float)_rng.NextDouble() * 0.05f;
                Stars[idx].StartY = (float)_rng.NextDouble() * 0.85f;
            } else if (spawnRoll < 0.65) {
                // 15% — Left edge
                Stars[idx].StartX = -0.02f - (float)_rng.NextDouble() * 0.05f;
                Stars[idx].StartY = (float)_rng.NextDouble() * 0.7f;
            } else if (spawnRoll < 0.80) {
                // 15% — ON SCREEN ignition: anywhere
                Stars[idx].StartX = 0.1f + (float)_rng.NextDouble() * 0.8f;
                Stars[idx].StartY = 0.05f + (float)_rng.NextDouble() * 0.7f;
            } else {
                // 20% — Upper-right quadrant: radiant cluster for meteor shower feel
                Stars[idx].StartX = 0.5f + (float)_rng.NextDouble() * 0.55f;
                Stars[idx].StartY = -0.05f + (float)_rng.NextDouble() * 0.35f;
            }

            // Direction: LOOSE RADIANT — all generally upper-right to lower-left
            // with natural angular spread like a real meteor shower
            // Base direction: pointing toward lower-left (~225 degrees)
            float baseAngle = 3.93f; // ~225 degrees in radians (lower-left)
            // Spread: +/- ~30 degrees for loose radiant feel
            float angleSpread = -0.52f + (float)_rng.NextDouble() * 1.04f; // +/- 30deg
            float angle = baseAngle + angleSpread;
            float speed2d = 0.40f + (float)_rng.NextDouble() * 0.35f; // how far the meteor travels
            Stars[idx].DirX = (float)Math.Cos(angle) * speed2d;
            Stars[idx].DirY = -(float)Math.Sin(angle) * speed2d; // negative because screen Y is inverted
            // Slight per-meteor jitter for organic feel
            Stars[idx].DirX += -0.02f + (float)_rng.NextDouble() * 0.04f;
            Stars[idx].DirY += -0.02f + (float)_rng.NextDouble() * 0.04f;

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
            if (roll < 0.03) {
                // 3% — FIREBALL: massive, slow, long trail, dramatic glow
                Stars[idx].MeteorType = 1;
                Stars[idx].TrailLength = 0.35f + (float)_rng.NextDouble() * 0.12f;
                Stars[idx].Brightness = 0.9f + (float)_rng.NextDouble() * 0.1f;
                Stars[idx].Thickness = 3.0f + (float)_rng.NextDouble() * 1.0f;
                Stars[idx].GlowSize = 9f + (float)_rng.NextDouble() * 4f;
                Stars[idx].Speed = 0.0108f + (float)_rng.NextDouble() * 0.0060f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.3f;
            } else if (roll < 0.07) {
                // 4% — COMET: medium body, very long fading tail
                Stars[idx].MeteorType = 2;
                Stars[idx].TrailLength = 0.35f + (float)_rng.NextDouble() * 0.12f;
                Stars[idx].Brightness = 0.65f + (float)_rng.NextDouble() * 0.25f;
                Stars[idx].Thickness = 1.8f + (float)_rng.NextDouble() * 0.8f;
                Stars[idx].GlowSize = 5f + (float)_rng.NextDouble() * 3f;
                Stars[idx].Speed = 0.0132f + (float)_rng.NextDouble() * 0.0072f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.3f;
            } else if (roll < 0.12) {
                // 5% — BOLT: very fast, very short, bright — like lightning
                Stars[idx].MeteorType = 4;
                Stars[idx].TrailLength = 0.10f + (float)_rng.NextDouble() * 0.06f;
                Stars[idx].Brightness = 0.8f + (float)_rng.NextDouble() * 0.2f;
                Stars[idx].Thickness = 1.2f + (float)_rng.NextDouble() * 0.6f;
                Stars[idx].GlowSize = 3f + (float)_rng.NextDouble() * 2f;
                Stars[idx].Speed = 0.0330f + (float)_rng.NextDouble() * 0.0150f;
                Stars[idx].Warmth = 0f;
            } else if (roll < 0.16) {
                // 4% — FRAGMENT: splits into 2-3 pieces near end
                Stars[idx].MeteorType = 5;
                Stars[idx].TrailLength = 0.15f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.7f + (float)_rng.NextDouble() * 0.25f;
                Stars[idx].Thickness = 1.8f + (float)_rng.NextDouble() * 0.8f;
                Stars[idx].GlowSize = 5f + (float)_rng.NextDouble() * 3f;
                Stars[idx].Speed = 0.0150f + (float)_rng.NextDouble() * 0.0072f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.3f;
            } else if (roll < 0.20) {
                // 4% — PHANTOM: very dim, slow, ethereal
                Stars[idx].MeteorType = 7;
                Stars[idx].TrailLength = 0.18f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.2f + (float)_rng.NextDouble() * 0.15f;
                Stars[idx].Thickness = 0.8f + (float)_rng.NextDouble() * 0.5f;
                Stars[idx].GlowSize = 6f + (float)_rng.NextDouble() * 4f;
                Stars[idx].Speed = 0.0072f + (float)_rng.NextDouble() * 0.0048f;
                Stars[idx].Warmth = 0f;
            } else if (roll < 0.24) {
                // 4% — FLARE: bright flare-up mid-flight
                Stars[idx].MeteorType = 8;
                Stars[idx].TrailLength = 0.14f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.7f + (float)_rng.NextDouble() * 0.25f;
                Stars[idx].Thickness = 1.4f + (float)_rng.NextDouble() * 0.6f;
                Stars[idx].GlowSize = 4f + (float)_rng.NextDouble() * 3f;
                Stars[idx].Speed = 0.0160f + (float)_rng.NextDouble() * 0.0080f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.3f;
                Stars[idx].Param1 = 0.3f + (float)_rng.NextDouble() * 0.4f;
            } else if (roll < 0.28) {
                // 4% — EMBER_TRAIL: sheds hot embers behind in a cone
                Stars[idx].MeteorType = 9;
                Stars[idx].TrailLength = 0.16f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.6f + (float)_rng.NextDouble() * 0.3f;
                Stars[idx].Thickness = 1.2f + (float)_rng.NextDouble() * 0.6f;
                Stars[idx].GlowSize = 4f + (float)_rng.NextDouble() * 2f;
                Stars[idx].Speed = 0.0140f + (float)_rng.NextDouble() * 0.0070f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.2f;
                Stars[idx].Param1 = 12f + (float)_rng.NextDouble() * 20f;
            } else if (roll < 0.31) {
                // 3% — CASCADE: triggers a burst of tiny sparks at the end
                Stars[idx].MeteorType = 10;
                Stars[idx].TrailLength = 0.12f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.75f + (float)_rng.NextDouble() * 0.2f;
                Stars[idx].Thickness = 1.6f + (float)_rng.NextDouble() * 0.8f;
                Stars[idx].GlowSize = 5f + (float)_rng.NextDouble() * 3f;
                Stars[idx].Speed = 0.0150f + (float)_rng.NextDouble() * 0.0080f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.3f;
            } else if (roll < 0.34) {
                // 3% — DEBRIS: sheds small debris particles behind
                Stars[idx].MeteorType = 11;
                Stars[idx].TrailLength = 0.20f + (float)_rng.NextDouble() * 0.10f;
                Stars[idx].Brightness = 0.55f + (float)_rng.NextDouble() * 0.3f;
                Stars[idx].Thickness = 1.0f + (float)_rng.NextDouble() * 0.5f;
                Stars[idx].GlowSize = 3f + (float)_rng.NextDouble() * 2f;
                Stars[idx].Speed = 0.0120f + (float)_rng.NextDouble() * 0.0060f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.2f;
            } else if (roll < 0.37) {
                // 3% — SLOWBURN: super long, super slow, bright scar across the sky
                Stars[idx].MeteorType = 12;
                Stars[idx].TrailLength = 0.50f + (float)_rng.NextDouble() * 0.15f;
                Stars[idx].Brightness = 0.85f + (float)_rng.NextDouble() * 0.15f;
                Stars[idx].Thickness = 2.5f + (float)_rng.NextDouble() * 1.0f;
                Stars[idx].GlowSize = 8f + (float)_rng.NextDouble() * 4f;
                Stars[idx].Speed = 0.0060f + (float)_rng.NextDouble() * 0.0030f;
                Stars[idx].Warmth = 0.1f + (float)_rng.NextDouble() * 0.2f;
            } else if (roll < 0.40) {
                // 3% — SKIMMER: very shallow angle, skims across the top
                Stars[idx].MeteorType = 14;
                Stars[idx].TrailLength = 0.25f + (float)_rng.NextDouble() * 0.10f;
                Stars[idx].Brightness = 0.6f + (float)_rng.NextDouble() * 0.3f;
                Stars[idx].Thickness = 1.2f + (float)_rng.NextDouble() * 0.5f;
                Stars[idx].GlowSize = 4f + (float)_rng.NextDouble() * 3f;
                Stars[idx].Speed = 0.0200f + (float)_rng.NextDouble() * 0.0100f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.2f;
                // Override direction for shallow angle — still generally radiant-consistent
                Stars[idx].DirX = -0.55f - (float)_rng.NextDouble() * 0.20f;
                Stars[idx].DirY = 0.08f + (float)_rng.NextDouble() * 0.12f;
            } else if (roll < 0.43) {
                // 3% — was TWIN, now regular streak
                Stars[idx].MeteorType = 0;
                Stars[idx].TrailLength = 0.14f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.6f + (float)_rng.NextDouble() * 0.3f;
                Stars[idx].Thickness = 1.2f + (float)_rng.NextDouble() * 0.5f;
                Stars[idx].GlowSize = 4f + (float)_rng.NextDouble() * 2f;
                Stars[idx].Speed = 0.0160f + (float)_rng.NextDouble() * 0.0080f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.2f;
            } else if (roll < 0.46) {
                // 3% — SPARKLER: leaves a trail of rainbow sparkle points that linger and fade
                Stars[idx].MeteorType = 13;
                Stars[idx].TrailLength = 0.12f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.7f + (float)_rng.NextDouble() * 0.25f;
                Stars[idx].Thickness = 1.0f + (float)_rng.NextDouble() * 0.5f;
                Stars[idx].GlowSize = 3f + (float)_rng.NextDouble() * 2f;
                Stars[idx].Speed = 0.0130f + (float)_rng.NextDouble() * 0.0070f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.2f;
                Stars[idx].Param1 = 20f + (float)_rng.NextDouble() * 15f; // sparkle density
            } else if (roll < 0.49) {
                // 3% — SPLITTER: starts as one, splits into two diverging meteors mid-flight
                Stars[idx].MeteorType = 15;
                Stars[idx].TrailLength = 0.16f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.75f + (float)_rng.NextDouble() * 0.2f;
                Stars[idx].Thickness = 1.8f + (float)_rng.NextDouble() * 0.8f;
                Stars[idx].GlowSize = 5f + (float)_rng.NextDouble() * 3f;
                Stars[idx].Speed = 0.0140f + (float)_rng.NextDouble() * 0.0060f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.3f;
                Stars[idx].Param1 = 0.35f + (float)_rng.NextDouble() * 0.25f; // split point (35-60%)
            } else if (roll < 0.52) {
                // 3% — CORKSCREW: spirals slightly as it falls, helix trail
                Stars[idx].MeteorType = 16;
                Stars[idx].TrailLength = 0.15f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.6f + (float)_rng.NextDouble() * 0.3f;
                Stars[idx].Thickness = 1.0f + (float)_rng.NextDouble() * 0.5f;
                Stars[idx].GlowSize = 3f + (float)_rng.NextDouble() * 2f;
                Stars[idx].Speed = 0.0140f + (float)_rng.NextDouble() * 0.0070f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.2f;
                Stars[idx].Param1 = 15f + (float)_rng.NextDouble() * 25f; // spiral frequency
            } else if (roll < 0.55) {
                // 3% — FADECHAIN: pulses in and out multiple times during flight
                Stars[idx].MeteorType = 17;
                Stars[idx].TrailLength = 0.13f + (float)_rng.NextDouble() * 0.07f;
                Stars[idx].Brightness = 0.7f + (float)_rng.NextDouble() * 0.25f;
                Stars[idx].Thickness = 1.3f + (float)_rng.NextDouble() * 0.6f;
                Stars[idx].GlowSize = 4f + (float)_rng.NextDouble() * 2f;
                Stars[idx].Speed = 0.0120f + (float)_rng.NextDouble() * 0.0060f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.2f;
                Stars[idx].Param1 = 3f + (float)_rng.Next(4); // number of pulses
            } else if (roll < 0.58) {
                // 3% — IRIDESCENT: rainbow shimmer trail (the magic trail one!)
                Stars[idx].MeteorType = 18;
                Stars[idx].TrailLength = 0.18f + (float)_rng.NextDouble() * 0.10f;
                Stars[idx].Brightness = 0.65f + (float)_rng.NextDouble() * 0.3f;
                Stars[idx].Thickness = 1.2f + (float)_rng.NextDouble() * 0.6f;
                Stars[idx].GlowSize = 4f + (float)_rng.NextDouble() * 3f;
                Stars[idx].Speed = 0.0110f + (float)_rng.NextDouble() * 0.0060f;
                Stars[idx].Warmth = 0f; // body stays white/natural
                Stars[idx].ColorType = 0; // force white body
            } else if (roll < 0.61) {
                // 3% — SMOKETRAIL: leaves a wide, slowly fading smoke-like wake
                Stars[idx].MeteorType = 19;
                Stars[idx].TrailLength = 0.22f + (float)_rng.NextDouble() * 0.10f;
                Stars[idx].Brightness = 0.6f + (float)_rng.NextDouble() * 0.25f;
                Stars[idx].Thickness = 1.5f + (float)_rng.NextDouble() * 0.7f;
                Stars[idx].GlowSize = 5f + (float)_rng.NextDouble() * 3f;
                Stars[idx].Speed = 0.0100f + (float)_rng.NextDouble() * 0.0050f;
                Stars[idx].Warmth = 0.2f + (float)_rng.NextDouble() * 0.3f;
            } else if (roll < 0.64) {
                // 3% — EARTHGRAZER: long, slow, bright — grazes across most of the sky
                Stars[idx].MeteorType = 20;
                Stars[idx].TrailLength = 0.40f + (float)_rng.NextDouble() * 0.20f;
                Stars[idx].Brightness = 0.8f + (float)_rng.NextDouble() * 0.2f;
                Stars[idx].Thickness = 2.0f + (float)_rng.NextDouble() * 1.0f;
                Stars[idx].GlowSize = 7f + (float)_rng.NextDouble() * 4f;
                Stars[idx].Speed = 0.0050f + (float)_rng.NextDouble() * 0.0025f;
                Stars[idx].Warmth = 0.1f + (float)_rng.NextDouble() * 0.2f;
            } else if (roll < 0.67) {
                // 3% — FLASHFREEZE: bright start that rapidly dims to icy blue
                Stars[idx].MeteorType = 21;
                Stars[idx].TrailLength = 0.12f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.85f + (float)_rng.NextDouble() * 0.15f;
                Stars[idx].Thickness = 1.4f + (float)_rng.NextDouble() * 0.6f;
                Stars[idx].GlowSize = 5f + (float)_rng.NextDouble() * 3f;
                Stars[idx].Speed = 0.0180f + (float)_rng.NextDouble() * 0.0080f;
                Stars[idx].Warmth = 0f;
            } else if (roll < 0.70) {
                // 3% — was DOUBLEHEAD, now regular streak
                Stars[idx].MeteorType = 0;
                Stars[idx].TrailLength = 0.14f + (float)_rng.NextDouble() * 0.08f;
                Stars[idx].Brightness = 0.7f + (float)_rng.NextDouble() * 0.25f;
                Stars[idx].Thickness = 1.3f + (float)_rng.NextDouble() * 0.5f;
                Stars[idx].GlowSize = 4f + (float)_rng.NextDouble() * 2f;
                Stars[idx].Speed = 0.0150f + (float)_rng.NextDouble() * 0.0070f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.2f;
            } else if (roll < 0.80) {
                // 10% — WHISPER: dim quick streak
                Stars[idx].MeteorType = 3;
                Stars[idx].TrailLength = 0.06f + (float)_rng.NextDouble() * 0.05f;
                Stars[idx].Brightness = 0.3f + (float)_rng.NextDouble() * 0.25f;
                Stars[idx].Thickness = 0.8f + (float)_rng.NextDouble() * 0.5f;
                Stars[idx].GlowSize = 2f + (float)_rng.NextDouble() * 2f;
                Stars[idx].Speed = 0.0240f + (float)_rng.NextDouble() * 0.0120f;
                Stars[idx].Warmth = (float)_rng.NextDouble() * 0.2f;
            } else {
                // 20% — STREAK: standard medium meteor (most common)
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
            if (needRepaint) { if (_invalidateCallback != null) { try { _invalidateCallback(); } catch { } } }
        }

        public void Dispose()
        {
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); }
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
            SpaceKoi, CelestialHarp, MeteorDragon, NorthernLights, DarkMatter,
            // Wave 4 — Icons & Whimsy
            SantaSleigh, Angel, JackOLantern, Cupid, Starfighter,
            MechBattle, SpaceStation, DeLorean, StarCruiser, CosmicEye,
            TreeRocket, CowMoon, SpaceCatsuit,
            AlienWave, RubberDucky, Snowglobe, CosmicSword,
            GoldfishBowl, DiscoBall, SpaceHamster,
            // Wave 5 — 30 new events
            FallingStar, SpaceTelescope, AsteroidField, GammaRay,
            SpaceOctopus, CosmicTurtle, SpaceBee, RocketPenguin, CosmicFrog,
            SpaceSquid, PirateGalleon, BattleFleet, SpaceSubmarine, HotAirBalloon,
            SpaceMotorcycle, CosmicWizard, SpaceSnowman, CosmicHourglass, SpaceAnchor,
            CosmicDice, SpaceViolin, RobotDog, RingNebula, CosmicLightning,
            StarNursery, GravityWave, RobotCrab, SpaceWaldo,
            // Extra events
            HaloRing, EasterEgg, TennisRacket, SchoolOfFish
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
            _timer = new Timer { Interval = 30 };
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


        /// <summary>Check if a given event type is already active in any slot (excluding excludeIdx).</summary>
        bool IsTypeActive(EventType type, int excludeIdx)
        {
            for (int i = 0; i < Events.Length; i++)
                if (i != excludeIdx && Events[i].Active && Events[i].Type == type) return true;
            return false;
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
            // Pick a type not already active on screen
            int attempts = 0;
            do {
                Events[_li].Type = allTypes[_rng.Next(allTypes.Length)];
                attempts++;
            } while (attempts < 50 && IsTypeActive(Events[_li].Type, _li));

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
                    {
                        float yPos = 0.2f + (float)_rng.NextDouble() * 0.6f;
                        float slight = -0.08f + (float)_rng.NextDouble() * 0.16f;
                        if (_rng.Next(2) == 0) { Events[_li].X = -0.15f; Events[_li].Y = yPos; Events[_li].DirX = 1.3f; Events[_li].DirY = slight; }
                        else { Events[_li].X = 1.15f; Events[_li].Y = yPos; Events[_li].DirX = -1.3f; Events[_li].DirY = slight; }
                    }
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f;
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
                    {
                        float yPos = 0.2f + (float)_rng.NextDouble() * 0.5f;
                        float slight = -0.08f + (float)_rng.NextDouble() * 0.16f;
                        if (_rng.Next(2) == 0) { Events[_li].X = -0.15f; Events[_li].Y = yPos; Events[_li].DirX = 1.3f; Events[_li].DirY = slight; }
                        else { Events[_li].X = 1.15f; Events[_li].Y = yPos; Events[_li].DirX = -1.3f; Events[_li].DirY = slight; }
                    }
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f;
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
                    Events[_li].Speed = 0.006f + (float)_rng.NextDouble() * 0.003f;
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
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f;
                    Events[_li].Param1 = 0; // wing flap phase
                    break;
                case EventType.Blackhole:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.010f;
                    break;
                case EventType.AuroraWave:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Y = 0.3f + (float)_rng.NextDouble() * 0.4f;
                    Events[_li].Speed = 0.010f;
                    Events[_li].Param1 = _rng.Next(3); // color: 0=green, 1=purple, 2=blue
                    break;
                case EventType.SpaceTrain:
                    {
                        float yPos = 0.2f + (float)_rng.NextDouble() * 0.5f;
                        float slight = -0.04f + (float)_rng.NextDouble() * 0.08f;
                        if (_rng.Next(2) == 0) { Events[_li].X = -0.15f; Events[_li].Y = yPos; Events[_li].DirX = 1.3f; Events[_li].DirY = slight; }
                        else { Events[_li].X = 1.15f; Events[_li].Y = yPos; Events[_li].DirX = -1.3f; Events[_li].DirY = slight; }
                    }
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f;
                    Events[_li].Param1 = 3 + _rng.Next(5); // number of cars
                    break;
                case EventType.CometCluster:
                    RandomTraversal();
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f;
                    Events[_li].Seed = _rng.Next(100000);
                    break;
                case EventType.Orb:
                    RandomTraversal(0.6f, 0.2f);
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f;
                    Events[_li].Param1 = _rng.Next(4); // color type
                    break;
                case EventType.LaserGrid:
                    Events[_li].X = 0.15f + (float)_rng.NextDouble() * 0.7f; Events[_li].Y = 0.15f + (float)_rng.NextDouble() * 0.7f;
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
                    // Force horizontal-only traversal so the rainbow trail looks right
                    {
                        float yPos = 0.2f + (float)_rng.NextDouble() * 0.5f;
                        float slight = -0.08f + (float)_rng.NextDouble() * 0.16f;
                        if (_rng.Next(2) == 0) { Events[_li].X = -0.15f; Events[_li].Y = yPos; Events[_li].DirX = 1.3f; Events[_li].DirY = slight; }
                        else { Events[_li].X = 1.15f; Events[_li].Y = yPos; Events[_li].DirX = -1.3f; Events[_li].DirY = slight; }
                    }
                    Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f;
                    Events[_li].Param1 = 0;
                    break;
                // Wave 3 events — all traversal
                case EventType.Stargate:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.010f; break;
                case EventType.TimeTraveler:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.SpacePirate:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.006f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.CrystalDragon:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.QuantumRift:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.012f; break;
                case EventType.CosmicDancer:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.PlasmaSnake:
                    {
                        float yPos = 0.15f + (float)_rng.NextDouble() * 0.6f;
                        float slight = -0.06f + (float)_rng.NextDouble() * 0.12f;
                        if (_rng.Next(2) == 0) { Events[_li].X = -0.15f; Events[_li].Y = yPos; Events[_li].DirX = 1.3f; Events[_li].DirY = slight; }
                        else { Events[_li].X = 1.15f; Events[_li].Y = yPos; Events[_li].DirX = -1.3f; Events[_li].DirY = slight; }
                    }
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.StarSurfer:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
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
                    Events[_li].X = 0.15f + (float)_rng.NextDouble() * 0.7f; Events[_li].Y = 0.15f + (float)_rng.NextDouble() * 0.4f;
                    Events[_li].Speed = 0.006f; break;
                case EventType.DarkMatter:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.010f; break;
                // Wave 4 — Icons & Whimsy
                case EventType.SantaSleigh:
                    {
                        float yPos = 0.1f + (float)_rng.NextDouble() * 0.4f;
                        float slight = -0.06f + (float)_rng.NextDouble() * 0.12f;
                        if (_rng.Next(2) == 0) { Events[_li].X = -0.15f; Events[_li].Y = yPos; Events[_li].DirX = 1.3f; Events[_li].DirY = slight; }
                        else { Events[_li].X = 1.15f; Events[_li].Y = yPos; Events[_li].DirX = -1.3f; Events[_li].DirY = slight; }
                    }
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.Angel:
                    RandomTraversal(0.4f, 0.3f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f;
                    Events[_li].Param1 = 0; break;
                case EventType.JackOLantern:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.Cupid:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.Starfighter:
                    RandomTraversal(0.6f, 0.15f); Events[_li].Speed = 0.008f + (float)_rng.NextDouble() * 0.004f; break;
                case EventType.MechBattle:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.SpaceStation:
                    RandomTraversal(0.3f, 0.15f); Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f;
                    Events[_li].Param1 = 0; break;
                case EventType.DeLorean:
                    RandomTraversal(0.6f, 0.15f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.StarCruiser:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.CosmicEye:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.008f; break;
                case EventType.TreeRocket:
                    // Force vertical-only (bottom to top) since the tree shape only works vertically
                    {
                        float xPos = 0.15f + (float)_rng.NextDouble() * 0.7f;
                        float slight = -0.08f + (float)_rng.NextDouble() * 0.16f;
                        Events[_li].X = xPos; Events[_li].Y = 1.15f; Events[_li].DirX = slight; Events[_li].DirY = -1.3f;
                    }
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.CowMoon:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.008f; break;

                case EventType.SpaceCatsuit:
                    RandomTraversal(0.4f, 0.3f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.AlienWave:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.RubberDucky:
                    RandomTraversal(0.4f, 0.3f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.Snowglobe:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;

                case EventType.CosmicSword:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.GoldfishBowl:
                    RandomTraversal(0.4f, 0.3f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.DiscoBall:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.010f; break;
                case EventType.SpaceHamster:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                // === Wave 5 ===
                case EventType.SpaceTelescope:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.AsteroidField:
                    { float yPos = 0.2f + (float)_rng.NextDouble() * 0.5f; float slight = -0.06f + (float)_rng.NextDouble() * 0.12f;
                      if (_rng.Next(2)==0) { Events[_li].X=-0.15f; Events[_li].Y=yPos; Events[_li].DirX=1.3f; Events[_li].DirY=slight; }
                      else { Events[_li].X=1.15f; Events[_li].Y=yPos; Events[_li].DirX=-1.3f; Events[_li].DirY=slight; } }
                    Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.GammaRay:
                    Events[_li].X = 0.15f + (float)_rng.NextDouble() * 0.7f; Events[_li].Y = 0.15f + (float)_rng.NextDouble() * 0.7f; Events[_li].Speed = 0.012f; break;
                case EventType.SpaceOctopus:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.CosmicTurtle:
                    RandomTraversal(0.4f, 0.3f); Events[_li].Speed = 0.002f + (float)_rng.NextDouble() * 0.001f; break;
                case EventType.SpaceBee:
                    RandomTraversal(0.5f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.RocketPenguin:
                    { float yPos = 0.2f + (float)_rng.NextDouble() * 0.5f; float slight = -0.06f + (float)_rng.NextDouble() * 0.12f;
                      if (_rng.Next(2)==0) { Events[_li].X=-0.15f; Events[_li].Y=yPos; Events[_li].DirX=1.3f; Events[_li].DirY=slight; }
                      else { Events[_li].X=1.15f; Events[_li].Y=yPos; Events[_li].DirX=-1.3f; Events[_li].DirY=slight; } }
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.CosmicFrog:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.SpaceSquid:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.PirateGalleon:
                    { float yPos = 0.2f + (float)_rng.NextDouble() * 0.4f; float slight = -0.04f + (float)_rng.NextDouble() * 0.08f;
                      if (_rng.Next(2)==0) { Events[_li].X=-0.15f; Events[_li].Y=yPos; Events[_li].DirX=1.3f; Events[_li].DirY=slight; }
                      else { Events[_li].X=1.15f; Events[_li].Y=yPos; Events[_li].DirX=-1.3f; Events[_li].DirY=slight; } }
                    Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.BattleFleet:
                    { float yPos = 0.2f + (float)_rng.NextDouble() * 0.5f; float slight = -0.04f + (float)_rng.NextDouble() * 0.08f;
                      if (_rng.Next(2)==0) { Events[_li].X=-0.15f; Events[_li].Y=yPos; Events[_li].DirX=1.3f; Events[_li].DirY=slight; }
                      else { Events[_li].X=1.15f; Events[_li].Y=yPos; Events[_li].DirX=-1.3f; Events[_li].DirY=slight; } }
                    Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.SpaceSubmarine:
                    { float yPos = 0.3f + (float)_rng.NextDouble() * 0.4f; float slight = -0.04f + (float)_rng.NextDouble() * 0.08f;
                      if (_rng.Next(2)==0) { Events[_li].X=-0.15f; Events[_li].Y=yPos; Events[_li].DirX=1.3f; Events[_li].DirY=slight; }
                      else { Events[_li].X=1.15f; Events[_li].Y=yPos; Events[_li].DirX=-1.3f; Events[_li].DirY=slight; } }
                    Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.HotAirBalloon:
                    { float xPos = 0.15f + (float)_rng.NextDouble() * 0.7f;
                      Events[_li].X=xPos; Events[_li].Y=1.15f; Events[_li].DirX=-0.05f+(float)_rng.NextDouble()*0.1f; Events[_li].DirY=-1.3f; }
                    Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.SpaceMotorcycle:
                    { float yPos = 0.2f + (float)_rng.NextDouble() * 0.5f; float slight = -0.06f + (float)_rng.NextDouble() * 0.12f;
                      if (_rng.Next(2)==0) { Events[_li].X=-0.15f; Events[_li].Y=yPos; Events[_li].DirX=1.3f; Events[_li].DirY=slight; }
                      else { Events[_li].X=1.15f; Events[_li].Y=yPos; Events[_li].DirX=-1.3f; Events[_li].DirY=slight; } }
                    Events[_li].Speed = 0.006f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.CosmicWizard:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.SpaceSnowman:
                    RandomTraversal(0.4f, 0.3f); Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.CosmicHourglass:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.008f; break;
                case EventType.SpaceAnchor:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.CosmicDice:
                    RandomTraversal(0.4f, 0.3f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.RobotDog:
                    { float yPos = 0.3f + (float)_rng.NextDouble() * 0.4f; float slight = -0.04f + (float)_rng.NextDouble() * 0.08f;
                      if (_rng.Next(2)==0) { Events[_li].X=-0.15f; Events[_li].Y=yPos; Events[_li].DirX=1.3f; Events[_li].DirY=slight; }
                      else { Events[_li].X=1.15f; Events[_li].Y=yPos; Events[_li].DirX=-1.3f; Events[_li].DirY=slight; } }
                    Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.RingNebula:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.008f; break;
                case EventType.CosmicLightning:
                    Events[_li].X = 0.15f + (float)_rng.NextDouble() * 0.7f; Events[_li].Y = 0.15f + (float)_rng.NextDouble() * 0.7f;
                    Events[_li].Speed = 0.015f; break;
                case EventType.StarNursery:
                    Events[_li].X = 0.2f + (float)_rng.NextDouble() * 0.6f; Events[_li].Y = 0.2f + (float)_rng.NextDouble() * 0.6f;
                    Events[_li].Speed = 0.008f; break;
                case EventType.GravityWave:
                    Events[_li].X = 0.15f + (float)_rng.NextDouble() * 0.7f; Events[_li].Y = 0.15f + (float)_rng.NextDouble() * 0.7f; Events[_li].Speed = 0.010f; break;
                case EventType.HaloRing:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.EasterEgg:
                    RandomTraversal(0.4f, 0.3f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.TennisRacket:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.005f + (float)_rng.NextDouble() * 0.003f; break;
                case EventType.SchoolOfFish:
                    { float yPos = 0.2f + (float)_rng.NextDouble() * 0.5f; float slight = -0.06f + (float)_rng.NextDouble() * 0.12f;
                      if (_rng.Next(2)==0) { Events[_li].X=-0.15f; Events[_li].Y=yPos; Events[_li].DirX=1.3f; Events[_li].DirY=slight; }
                      else { Events[_li].X=1.15f; Events[_li].Y=yPos; Events[_li].DirX=-1.3f; Events[_li].DirY=slight; } }
                    Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.FallingStar:
                    { float yPos = 0.1f + (float)_rng.NextDouble() * 0.3f; float slight = 0.2f + (float)_rng.NextDouble() * 0.3f;
                      if (_rng.Next(2)==0) { Events[_li].X=-0.1f; Events[_li].Y=yPos; Events[_li].DirX=1.3f; Events[_li].DirY=slight; }
                      else { Events[_li].X=1.1f; Events[_li].Y=yPos; Events[_li].DirX=-1.3f; Events[_li].DirY=slight; } }
                    Events[_li].Speed = 0.008f + (float)_rng.NextDouble() * 0.004f; break;
                case EventType.SpaceViolin:
                    RandomTraversal(0.4f, 0.2f); Events[_li].Speed = 0.004f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.RobotCrab:
                    { float yPos = 0.3f + (float)_rng.NextDouble() * 0.4f; float slight = -0.04f + (float)_rng.NextDouble() * 0.08f;
                      if (_rng.Next(2)==0) { Events[_li].X=-0.15f; Events[_li].Y=yPos; Events[_li].DirX=1.3f; Events[_li].DirY=slight; }
                      else { Events[_li].X=1.15f; Events[_li].Y=yPos; Events[_li].DirX=-1.3f; Events[_li].DirY=slight; } }
                    Events[_li].Speed = 0.003f + (float)_rng.NextDouble() * 0.002f; break;
                case EventType.SpaceWaldo:
                    RandomTraversal(0.4f, 0.3f); Events[_li].Speed = 0.002f + (float)_rng.NextDouble() * 0.001f; break;
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
                if (Events[i].Type == EventType.SpaceStation) Events[i].Param1 += 1.5f * dt;
                if (Events[i].Progress >= 1f) { Events[i].Active = false; if (i == 0) ScheduleNext(); }
            }
            if (anyActive || !Events[0].Active) { if (_invalidateCallback != null) { try { _invalidateCallback(); } catch { } } }
        }

        public void Dispose() { if (_timer != null) { _timer.Stop(); _timer.Dispose(); } }
    }

}
