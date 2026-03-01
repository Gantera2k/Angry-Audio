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
            int count = Math.Max(12, Math.Min(150, (w * h) / 2000));
            var arr = new StarData[count];
            for (int i = 0; i < count; i++) {
                arr[i].X = rng.Next(w);
                arr[i].Y = rng.Next(h);
                int tier = rng.Next(100);
                if (tier < 50) {
                    arr[i].Radius = 0.6f + (float)(rng.NextDouble() * 0.5);
                    arr[i].BaseAlpha = (int)((55 + rng.Next(35)) * alphaMul);
                    arr[i].R = arr[i].G = arr[i].B = 255;
                } else if (tier < 82) {
                    arr[i].Radius = 0.8f + (float)(rng.NextDouble() * 0.7);
                    arr[i].BaseAlpha = (int)((70 + rng.Next(50)) * alphaMul);
                    arr[i].R = arr[i].G = arr[i].B = 255;
                } else {
                    arr[i].Radius = 1.0f + (float)(rng.NextDouble() * 0.8);
                    arr[i].BaseAlpha = (int)((65 + rng.Next(55)) * alphaMul);
                    arr[i].R = DarkTheme.Accent.R; arr[i].G = DarkTheme.Accent.G; arr[i].B = DarkTheme.Accent.B;
                }
                arr[i].Twinkles = rng.Next(100) < 45;
                arr[i].Phase = rng.NextDouble() * Math.PI * 2;
                arr[i].Speed = 0.06 + rng.NextDouble() * 0.08;
            }
            return arr;
        }

        void RenderStars(Bitmap bmp, StarData[] stars, bool twinkleOnly, int tick)
        {
            using (var g = Graphics.FromImage(bmp)) {
                g.Clear(Color.Transparent);
                for (int i = 0; i < stars.Length; i++) {
                    var s = stars[i];
                    if (twinkleOnly && !s.Twinkles) continue;
                    if (!twinkleOnly && s.Twinkles) continue;

                    int alpha = s.BaseAlpha;
                    if (s.Twinkles && tick > 0) {
                        double wave = Math.Sin(tick * s.Speed + s.Phase);
                        alpha = (int)(s.BaseAlpha * (0.4 + 0.8 * (wave * 0.5 + 0.5)) * 2.0);
                        alpha = Math.Max(15, Math.Min(220, alpha));
                    }
                    if (alpha <= 0) continue;

                    using (var b = new SolidBrush(Color.FromArgb(alpha, s.R, s.G, s.B))) {
                        if (s.Radius <= 0.9f)
                            g.FillRectangle(b, s.X, s.Y, 1, 1);
                        else
                            g.FillEllipse(b, s.X - s.Radius, s.Y - s.Radius, s.Radius * 2, s.Radius * 2);
                    }
                }
            }
        }

        void EnsureBase(int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            if (_base != null && _cacheW == w && _cacheH == h) return;
            _cacheW = w; _cacheH = h;

            _base?.Dispose(); _baseDim?.Dispose();
            _twinkleOverlay?.Dispose(); _twinkleOverlayDim?.Dispose();

            _starsNorm = ComputeStars(w, h, 1.0f);
            _starsDim = ComputeStars(w, h, 0.35f);

            _base = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _baseDim = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _twinkleOverlay = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _twinkleOverlayDim = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

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
            EnsureBase(w, h);
            EnsureTwinkle();
            using (var bg = new SolidBrush(DarkTheme.BG))
                g.FillRectangle(bg, 0, 0, childW, childH);
            if (_base != null) g.DrawImage(_base, -ox, -oy);
            if (_twinkleOverlay != null) g.DrawImage(_twinkleOverlay, -ox, -oy);
            using (var tint = new SolidBrush(TINT))
                g.FillRectangle(tint, 0, 0, childW, childH);
            if (_baseDim != null) g.DrawImage(_baseDim, -ox, -oy);
            if (_twinkleOverlayDim != null) g.DrawImage(_twinkleOverlayDim, -ox, -oy);
        }

        public void Dispose()
        {
            Shooting?.Stop(); Shooting?.Dispose();
            Celestial?.Stop(); Celestial?.Dispose();
            _base?.Dispose(); _baseDim?.Dispose();
            _twinkleOverlay?.Dispose(); _twinkleOverlayDim?.Dispose();
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
            _timer = new Timer { Interval = 33 };
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
            _timer = new Timer { Interval = 33 };
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

}
