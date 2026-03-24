// StarBackground.cs — THE single star system used by ALL surfaces.
// Contains: StarBackground (cache + paint API), ShootingStar, CelestialEvents.
//
// Architecture: A dedicated background RENDER THREAD produces composite
// bitmaps (twinkle + shooting stars + celestial events) at ~30fps.
// Paint() just blits pre-rendered bitmaps — <1ms, never skips.
// ShootingStar/CelestialEvents use System.Threading.Timer for UI-independent state.
//
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
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
        static readonly int SEED = new Random().Next(100000);
        static readonly Color TINT = DarkTheme.GlassTint;

        // --- Render thread compositing ---
        // Double-buffered composites: rendered on background thread, read by Paint()
        Bitmap _compositeNorm, _compositeDim;       // back buffers (being rendered)
        Bitmap _compositeFrontNorm, _compositeFrontDim; // front buffers (read by Paint)
        readonly object _compositeLock = new object();
        Thread _renderThread;
        volatile bool _renderRunning;
        volatile bool _isPaused;
        Action _invalidateCallback;

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

        // --- Animated drift clouds (high quality multi-layer) ---
        struct CloudPatch
        {
            public float X, Y;       // center position
            public float W, H;       // outer ellipse size
            public float Vx, Vy;     // velocity (px/frame)
            public byte R, G, B;
            public int Alpha;        // max alpha at inner core
        }
        CloudPatch[] _clouds;
        const int CLOUD_UPDATE_INTERVAL = 10;   // ~160ms at 60fps = ~6fps cloud updates

        // --- Moon + planets ---
        struct CelestialBody
        {
            public float X, Y;
            public float Radius;     // body radius
            public float GlowRadius; // outer glow radius
            public float Vx, Vy;
            public byte R, G, B;
            public int BodyAlpha;    // body alpha
            public int GlowAlpha;   // glow halo alpha
            public bool IsMoon;      // true = larger, with crescent shading
        }
        CelestialBody[] _bodies;
        int _cloudW, _cloudH;

        // --- Star flip/rotation ---
        int _flipMode;  // 0=normal, 1=flipH, 2=flipV, 3=rotate180
        static readonly Random _themeRng = new Random();

        void InitCloudsAndBodies(int w, int h)
        {
            _cloudW = w; _cloudH = h;
            var rng = new Random(SEED + 9999);

            // --- High quality clouds: each patch is 3 overlapping layers ---
            int patchCount = 8 + rng.Next(5); // 8-12 cloud patches
            _clouds = new CloudPatch[patchCount * 3]; // 3 layers per patch
            Color[] palette = new Color[] {
                Color.FromArgb(50, 70, 130),   // Deep blue
                Color.FromArgb(75, 45, 105),   // Purple
                Color.FromArgb(40, 80, 100),   // Teal
                Color.FromArgb(85, 50, 80),    // Dusty rose
                Color.FromArgb(35, 65, 95),    // Steel blue
                Color.FromArgb(65, 50, 100),   // Lavender
                Color.FromArgb(45, 85, 75),    // Sea green
                Color.FromArgb(90, 55, 65),    // Warm grey
            };
            for (int p = 0; p < patchCount; p++)
            {
                var col = palette[rng.Next(palette.Length)];
                float cx = (float)(rng.NextDouble() * w);
                float cy = (float)(rng.NextDouble() * h);
                float baseW = w * (0.18f + (float)rng.NextDouble() * 0.28f);
                float baseH = h * (0.14f + (float)rng.NextDouble() * 0.22f);
                float vx = (float)(rng.NextDouble() - 0.5) * 0.4f;
                float vy = (float)(rng.NextDouble() - 0.5) * 0.2f;

                // Layer 0: Outer wisp — large, very faint
                int idx = p * 3;
                _clouds[idx].X = cx + (float)(rng.NextDouble() - 0.5) * baseW * 0.15f;
                _clouds[idx].Y = cy + (float)(rng.NextDouble() - 0.5) * baseH * 0.15f;
                _clouds[idx].W = baseW * 1.5f;
                _clouds[idx].H = baseH * 1.4f;
                _clouds[idx].Vx = vx; _clouds[idx].Vy = vy;
                _clouds[idx].R = col.R; _clouds[idx].G = col.G; _clouds[idx].B = col.B;
                _clouds[idx].Alpha = 4 + rng.Next(4); // 4-7 alpha

                // Layer 1: Mid haze — medium, gentle
                _clouds[idx+1].X = cx + (float)(rng.NextDouble() - 0.5) * baseW * 0.1f;
                _clouds[idx+1].Y = cy + (float)(rng.NextDouble() - 0.5) * baseH * 0.1f;
                _clouds[idx+1].W = baseW * 1.0f;
                _clouds[idx+1].H = baseH * 0.9f;
                _clouds[idx+1].Vx = vx; _clouds[idx+1].Vy = vy;
                _clouds[idx+1].R = col.R; _clouds[idx+1].G = col.G; _clouds[idx+1].B = col.B;
                _clouds[idx+1].Alpha = 7 + rng.Next(6); // 7-12 alpha

                // Layer 2: Inner core — smaller, brightest
                _clouds[idx+2].X = cx;
                _clouds[idx+2].Y = cy;
                _clouds[idx+2].W = baseW * 0.5f;
                _clouds[idx+2].H = baseH * 0.45f;
                _clouds[idx+2].Vx = vx; _clouds[idx+2].Vy = vy;
                _clouds[idx+2].R = (byte)Math.Min(255, col.R + 20);
                _clouds[idx+2].G = (byte)Math.Min(255, col.G + 15);
                _clouds[idx+2].B = (byte)Math.Min(255, col.B + 25);
                _clouds[idx+2].Alpha = 10 + rng.Next(8); // 10-17 alpha
            }

            // --- Moon + tiny planets ---
            int planetCount = 2 + rng.Next(2); // 2-3 planets + 1 moon
            _bodies = new CelestialBody[1 + planetCount];

            // Moon — larger, slow drift, warm glow
            _bodies[0].X = w * (0.15f + (float)rng.NextDouble() * 0.7f);
            _bodies[0].Y = h * (0.1f + (float)rng.NextDouble() * 0.3f); // upper portion
            _bodies[0].Radius = 14f + (float)rng.NextDouble() * 10f;    // 14-24px
            _bodies[0].GlowRadius = _bodies[0].Radius * 4.0f;
            _bodies[0].Vx = (float)(rng.NextDouble() - 0.5) * 0.06f;   // very slow
            _bodies[0].Vy = (float)(rng.NextDouble() - 0.5) * 0.03f;
            _bodies[0].R = 225; _bodies[0].G = 220; _bodies[0].B = 210; // warm white
            _bodies[0].BodyAlpha = 45 + rng.Next(20);
            _bodies[0].GlowAlpha = 12 + rng.Next(6);
            _bodies[0].IsMoon = true;

            // Tiny planets — small colored dots with glow
            Color[] planetColors = new Color[] {
                Color.FromArgb(140, 180, 220), // Pale blue
                Color.FromArgb(200, 160, 130), // Sandy
                Color.FromArgb(160, 140, 190), // Lavender
                Color.FromArgb(130, 190, 170), // Teal
                Color.FromArgb(210, 170, 150), // Warm pink
            };
            for (int pl = 0; pl < planetCount; pl++)
            {
                int bi = 1 + pl;
                var pc = planetColors[rng.Next(planetColors.Length)];
                _bodies[bi].X = (float)(rng.NextDouble() * w);
                _bodies[bi].Y = (float)(rng.NextDouble() * h);
                _bodies[bi].Radius = 2.5f + (float)rng.NextDouble() * 3f; // 2.5-5.5px
                _bodies[bi].GlowRadius = _bodies[bi].Radius * 3f;
                _bodies[bi].Vx = (float)(rng.NextDouble() - 0.5) * 0.15f;
                _bodies[bi].Vy = (float)(rng.NextDouble() - 0.5) * 0.08f;
                _bodies[bi].R = pc.R; _bodies[bi].G = pc.G; _bodies[bi].B = pc.B;
                _bodies[bi].BodyAlpha = 50 + rng.Next(30);
                _bodies[bi].GlowAlpha = 10 + rng.Next(8);
                _bodies[bi].IsMoon = false;
            }
        }

        void AdvanceCloudsAndBodies()
        {
            if (_clouds != null)
            {
                for (int i = 0; i < _clouds.Length; i++)
                {
                    _clouds[i].X += _clouds[i].Vx;
                    _clouds[i].Y += _clouds[i].Vy;
                    float margin = _clouds[i].W * 0.6f;
                    if (_clouds[i].X > _cloudW + margin) _clouds[i].X = -margin;
                    if (_clouds[i].X < -margin) _clouds[i].X = _cloudW + margin;
                    float marginY = _clouds[i].H * 0.6f;
                    if (_clouds[i].Y > _cloudH + marginY) _clouds[i].Y = -marginY;
                    if (_clouds[i].Y < -marginY) _clouds[i].Y = _cloudH + marginY;
                }
            }
            if (_bodies != null)
            {
                for (int i = 0; i < _bodies.Length; i++)
                {
                    _bodies[i].X += _bodies[i].Vx;
                    _bodies[i].Y += _bodies[i].Vy;
                    float margin = _bodies[i].GlowRadius;
                    if (_bodies[i].X > _cloudW + margin) _bodies[i].X = -margin;
                    if (_bodies[i].X < -margin) _bodies[i].X = _cloudW + margin;
                    if (_bodies[i].Y > _cloudH + margin) _bodies[i].Y = -margin;
                    if (_bodies[i].Y < -margin) _bodies[i].Y = _cloudH + margin;
                }
            }
        }

        void RenderCloudsAndBodies(Graphics g, int w, int h, float alphaMul)
        {
            // --- Clouds: multi-layer soft fading ---
            if (_clouds != null)
            {
                for (int i = 0; i < _clouds.Length; i++)
                {
                    var c = _clouds[i];
                    int a = (int)(c.Alpha * alphaMul);
                    if (a < 1) continue;
                    float left = c.X - c.W / 2;
                    float top = c.Y - c.H / 2;
                    if (left + c.W < 0 || left > w || top + c.H < 0 || top > h) continue;
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(left, top, c.W, c.H);
                        using (var pgb = new PathGradientBrush(path))
                        {
                            pgb.CenterColor = Color.FromArgb(a, c.R, c.G, c.B);
                            pgb.SurroundColors = new Color[] { Color.FromArgb(0, c.R, c.G, c.B) };
                            pgb.FocusScales = new PointF(0.15f, 0.15f);
                            g.FillPath(pgb, path);
                        }
                    }
                }
            }

            // --- Moon + planets ---
            if (_bodies != null)
            {
                for (int i = 0; i < _bodies.Length; i++)
                {
                    var b = _bodies[i];
                    float bx = b.X, by = b.Y;
                    if (bx + b.GlowRadius < 0 || bx - b.GlowRadius > w ||
                        by + b.GlowRadius < 0 || by - b.GlowRadius > h) continue;

                    int ga = (int)(b.GlowAlpha * alphaMul);
                    int ba = (int)(b.BodyAlpha * alphaMul);

                    // Outer glow halo
                    if (ga > 0)
                    {
                        float gr = b.GlowRadius;
                        using (var path = new GraphicsPath())
                        {
                            path.AddEllipse(bx - gr, by - gr, gr * 2, gr * 2);
                            using (var pgb = new PathGradientBrush(path))
                            {
                                pgb.CenterColor = Color.FromArgb(ga, b.R, b.G, b.B);
                                pgb.SurroundColors = new Color[] { Color.FromArgb(0, b.R, b.G, b.B) };
                                pgb.FocusScales = new PointF(0.0f, 0.0f);
                                g.FillPath(pgb, path);
                            }
                        }
                    }

                    // Body
                    if (ba > 0)
                    {
                        using (var brush = new SolidBrush(Color.FromArgb(ba, b.R, b.G, b.B)))
                            g.FillEllipse(brush, bx - b.Radius, by - b.Radius, b.Radius * 2, b.Radius * 2);

                        // Moon gets a subtle crescent shadow on one side
                        if (b.IsMoon)
                        {
                            int shadowA = (int)(ba * 0.5f);
                            if (shadowA > 0)
                            {
                                float offset = b.Radius * 0.35f;
                                using (var shadow = new SolidBrush(Color.FromArgb(shadowA, 5, 5, 15)))
                                    g.FillEllipse(shadow, bx - b.Radius + offset, by - b.Radius - offset * 0.3f, b.Radius * 1.8f, b.Radius * 2f);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Renders just the cloud patches to a cached bitmap. Called ~6fps.</summary>
        void RenderCloudLayer(Graphics g, int w, int h, float alphaMul)
        {
            if (_clouds == null) return;
            for (int i = 0; i < _clouds.Length; i++)
            {
                var c = _clouds[i];
                int a = (int)(c.Alpha * alphaMul);
                if (a < 1) continue;
                float left = c.X - c.W / 2;
                float top = c.Y - c.H / 2;
                if (left + c.W < 0 || left > w || top + c.H < 0 || top > h) continue;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(left, top, c.W, c.H);
                    using (var pgb = new PathGradientBrush(path))
                    {
                        pgb.CenterColor = Color.FromArgb(a, c.R, c.G, c.B);
                        pgb.SurroundColors = new Color[] { Color.FromArgb(0, c.R, c.G, c.B) };
                        pgb.FocusScales = new PointF(0.15f, 0.15f);
                        g.FillPath(pgb, path);
                    }
                }
            }
        }

        /// <summary>Renders ONLY moon + planets (no clouds). Used for static base layer.</summary>
        void RenderBodiesOnly(Graphics g, int w, int h, float alphaMul)
        {
            if (_bodies == null) return;
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                float bx = b.X, by = b.Y;
                if (bx + b.GlowRadius < 0 || bx - b.GlowRadius > w ||
                    by + b.GlowRadius < 0 || by - b.GlowRadius > h) continue;

                int ga = (int)(b.GlowAlpha * alphaMul);
                int ba2 = (int)(b.BodyAlpha * alphaMul);

                // Outer glow halo
                if (ga > 0)
                {
                    float gr = b.GlowRadius;
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(bx - gr, by - gr, gr * 2, gr * 2);
                        using (var pgb = new PathGradientBrush(path))
                        {
                            pgb.CenterColor = Color.FromArgb(ga, b.R, b.G, b.B);
                            pgb.SurroundColors = new Color[] { Color.FromArgb(0, b.R, b.G, b.B) };
                            pgb.FocusScales = new PointF(0.0f, 0.0f);
                            g.FillPath(pgb, path);
                        }
                    }
                }

                // Body
                if (ba2 > 0)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(ba2, b.R, b.G, b.B)))
                        g.FillEllipse(brush, bx - b.Radius, by - b.Radius, b.Radius * 2, b.Radius * 2);

                    if (b.IsMoon)
                    {
                        int shadowA = (int)(ba2 * 0.5f);
                        if (shadowA > 0)
                        {
                            float offset = b.Radius * 0.35f;
                            using (var shadow = new SolidBrush(Color.FromArgb(shadowA, 5, 5, 15)))
                                g.FillEllipse(shadow, bx - b.Radius + offset, by - b.Radius - offset * 0.3f, b.Radius * 1.8f, b.Radius * 2f);
                        }
                    }
                }
            }
        }
        /// <summary>Picks a random flip/rotation and forces a full rebuild.</summary>
        public void SetRandomTheme()
        {
            _flipMode = _themeRng.Next(5); // 0=none, 1=flipX, 2=flipY, 3=rotate180, 4=flipXY
            _cacheW = 0; _cacheH = 0;
        }

        public StarBackground(Action invalidate)
        {
            _invalidateCallback = invalidate;
            _flipMode = _themeRng.Next(5);
            Shooting = new ShootingStar(null);
            Shooting.Start();
            Celestial = new CelestialEvents(null);
            Celestial.Start();
            _renderRunning = true;
            _renderThread = new Thread(RenderLoop);
            _renderThread.IsBackground = true;
            _renderThread.Name = "StarRender";
            _renderThread.Priority = ThreadPriority.BelowNormal;
            _renderThread.Start();
        }



        /// <summary>Background render loop — produces composite bitmaps at ~30fps.</summary>
        void RenderLoop()
        {
            while (_renderRunning)
            {
                try
                {
                    Thread.Sleep(16); // ~60fps
                    if (!_renderRunning) break;
                    if (_isPaused) continue;

                    int w = _cacheW, h = _cacheH;
                    if (w <= 0 || h <= 0) continue;

                    // Advance twinkle + drifting clouds
                    _twinkleTick++;
                    _twinkleDirty = true;
                    if (_twinkleTick % CLOUD_UPDATE_INTERVAL == 0)
                        AdvanceCloudsAndBodies();

                    // Ensure twinkle overlays are current
                    if (_twinkleDirty && _twinkleOverlay != null && _starsNorm != null)
                    {
                        _twinkleDirty = false;
                        RenderStars(_twinkleOverlay, _starsNorm, true, _twinkleTick);
                        RenderStars(_twinkleOverlayDim, _starsDim, true, _twinkleTick);
                    }

                    // Create back buffers if needed
                    if (_compositeNorm == null || _compositeNorm.Width != w || _compositeNorm.Height != h)
                    {
                        if (_compositeNorm != null) _compositeNorm.Dispose();
                        if (_compositeDim != null) _compositeDim.Dispose();
                        _compositeNorm = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                        _compositeDim = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                    }

                    // Render composite: twinkle + shooting stars + celestial events
                    RenderComposite(_compositeNorm, _twinkleOverlay, w, h, false);
                    RenderComposite(_compositeDim, _twinkleOverlayDim, w, h, true);

                    // Swap front/back buffers atomically
                    lock (_compositeLock)
                    {
                        var tmpN = _compositeFrontNorm;
                        var tmpD = _compositeFrontDim;
                        _compositeFrontNorm = _compositeNorm;
                        _compositeFrontDim = _compositeDim;
                        _compositeNorm = tmpN; // reuse as next back buffer
                        _compositeDim = tmpD;
                    }

                    // Trigger UI repaint
                    if (_invalidateCallback != null)
                        try { _invalidateCallback(); } catch { }
                }
                catch { }
            }
        }

        void RenderComposite(Bitmap target, Bitmap twinkleLayer, int w, int h, bool dim)
        {
            using (var g = Graphics.FromImage(target))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingMode = CompositingMode.SourceOver;

                // Drifting nebula clouds (animated layer)
                RenderCloudLayer(g, w, h, dim ? 0.35f : 1.0f);

                // Twinkle stars
                if (twinkleLayer != null)
                    g.DrawImage(twinkleLayer, 0, 0);

                // Shooting stars
                if (Shooting != null)
                    DarkTheme.PaintShootingStar(g, w, h, Shooting);

                // Celestial events
                if (Celestial != null)
                    DarkTheme.PaintCelestialEvent(g, w, h, Celestial);
            }
        }

        StarData[] ComputeStars(int w, int h, float alphaMul)
        {
            var rng = new Random(SEED);
            // Dense starfield — rich galaxy sky
            int count = Math.Max(60, Math.Min(600, (w * h) / 600));
            var arr = new StarData[count];
            Color accent = DarkTheme.Accent;
            Color secondary = Color.FromArgb(180, 210, 255);
            // Spectral class colors (B-V color index inspired)
            Color[] spectralColors = new Color[] {
                Color.FromArgb(155, 175, 255), // O/B — hot blue
                Color.FromArgb(200, 215, 255), // A — blue-white
                Color.FromArgb(255, 245, 238), // F — warm white
                Color.FromArgb(255, 225, 180), // G — sun yellow
                Color.FromArgb(255, 190, 130), // K — orange
                Color.FromArgb(255, 155, 110), // M — cool red
            };
            for (int i = 0; i < count; i++) {
                arr[i].X = rng.Next(w);
                arr[i].Y = rng.Next(h);
                int tier = rng.Next(100);
                if (tier < 30) {
                    // Faint dust — barely visible pinpricks
                    arr[i].Radius = 0.25f + (float)(rng.NextDouble() * 0.25);
                    arr[i].BaseAlpha = (int)((18 + rng.Next(20)) * alphaMul);
                    arr[i].R = arr[i].G = arr[i].B = 255;
                } else if (tier < 52) {
                    // Small white stars
                    arr[i].Radius = 0.45f + (float)(rng.NextDouble() * 0.4);
                    arr[i].BaseAlpha = (int)((40 + rng.Next(30)) * alphaMul);
                    arr[i].R = arr[i].G = arr[i].B = 255;
                } else if (tier < 68) {
                    // Medium white
                    arr[i].Radius = 0.7f + (float)(rng.NextDouble() * 0.5);
                    arr[i].BaseAlpha = (int)((60 + rng.Next(40)) * alphaMul);
                    arr[i].R = arr[i].G = arr[i].B = 255;
                } else if (tier < 76) {
                    // Spectral colored — realistic star temperatures
                    var sc = spectralColors[rng.Next(spectralColors.Length)];
                    arr[i].Radius = 0.6f + (float)(rng.NextDouble() * 0.6);
                    arr[i].BaseAlpha = (int)((55 + rng.Next(40)) * alphaMul);
                    arr[i].R = sc.R; arr[i].G = sc.G; arr[i].B = sc.B;
                } else if (tier < 82) {
                    // Blue-white — cool secondary
                    arr[i].Radius = 0.7f + (float)(rng.NextDouble() * 0.5);
                    arr[i].BaseAlpha = (int)((55 + rng.Next(40)) * alphaMul);
                    arr[i].R = secondary.R; arr[i].G = secondary.G; arr[i].B = secondary.B;
                } else if (tier < 87) {
                    // Warm yellow-white
                    arr[i].Radius = 0.8f + (float)(rng.NextDouble() * 0.5);
                    arr[i].BaseAlpha = (int)((55 + rng.Next(40)) * alphaMul);
                    arr[i].R = 255; arr[i].G = 240; arr[i].B = 200;
                } else if (tier < 91) {
                    // Warm orange
                    arr[i].Radius = 0.9f + (float)(rng.NextDouble() * 0.5);
                    arr[i].BaseAlpha = (int)((50 + rng.Next(35)) * alphaMul);
                    arr[i].R = 255; arr[i].G = 200; arr[i].B = 150;
                } else if (tier < 94) {
                    // Bright prominent star — stands out
                    arr[i].Radius = 1.4f + (float)(rng.NextDouble() * 0.8);
                    arr[i].BaseAlpha = (int)((100 + rng.Next(50)) * alphaMul);
                    arr[i].R = arr[i].G = arr[i].B = 255;
                } else if (tier < 97) {
                    // Accent-colored — theme-driven
                    arr[i].Radius = 1.0f + (float)(rng.NextDouble() * 0.7);
                    arr[i].BaseAlpha = (int)((65 + rng.Next(50)) * alphaMul);
                    arr[i].R = accent.R; arr[i].G = accent.G; arr[i].B = accent.B;
                } else {
                    // Landmark stars — very bright, large, with spikes
                    arr[i].Radius = 1.8f + (float)(rng.NextDouble() * 1.0);
                    arr[i].BaseAlpha = (int)((130 + rng.Next(60)) * alphaMul);
                    var sc2 = spectralColors[rng.Next(3)]; // bias toward hot stars
                    arr[i].R = sc2.R; arr[i].G = sc2.G; arr[i].B = sc2.B;
                }
                arr[i].Twinkles = rng.Next(100) < 55;
                arr[i].Phase = rng.NextDouble() * Math.PI * 2;
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

                    if (s.Radius <= 0.5f) {
                        // Sub-pixel stars: soft anti-aliased dot via alpha blending
                        int subA = (int)(alpha * (s.Radius / 0.5f) * 0.8f);
                        if (subA > 2) {
                            using (var b = new SolidBrush(Color.FromArgb(subA, s.R, s.G, s.B)))
                                g.FillEllipse(b, s.X - 0.5f, s.Y - 0.5f, 1f, 1f);
                        }
                    } else if (s.Radius <= 1.0f) {
                        using (var b = new SolidBrush(Color.FromArgb(alpha, s.R, s.G, s.B)))
                            g.FillEllipse(b, s.X - s.Radius, s.Y - s.Radius, s.Radius * 2, s.Radius * 2);
                    } else {
                        // Bright stars: core + soft glow halo + cross spikes for the brightest
                        using (var b = new SolidBrush(Color.FromArgb(alpha, s.R, s.G, s.B)))
                            g.FillEllipse(b, s.X - s.Radius, s.Y - s.Radius, s.Radius * 2, s.Radius * 2);
                        // Soft glow
                        int glowAlpha = alpha / 4;
                        if (glowAlpha > 2) {
                            float gr = s.Radius * 2.5f;
                            using (var path = new GraphicsPath()) {
                                path.AddEllipse(s.X - gr, s.Y - gr, gr * 2, gr * 2);
                                using (var pgb = new PathGradientBrush(path)) {
                                    pgb.CenterColor = Color.FromArgb(glowAlpha, s.R, s.G, s.B);
                                    pgb.SurroundColors = new Color[] { Color.FromArgb(0, s.R, s.G, s.B) };
                                    g.FillPath(pgb, path);
                                }
                            }
                        }
                        // Cross spikes for very bright stars (radius > 1.5)
                        if (s.Radius > 1.5f && alpha > 100) {
                            int spikeA = alpha / 6;
                            float spikeLen = s.Radius * 3.5f;
                            using (var pen = new Pen(Color.FromArgb(spikeA, s.R, s.G, s.B), 0.6f)) {
                                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                                g.DrawLine(pen, s.X - spikeLen, s.Y, s.X + spikeLen, s.Y);
                                g.DrawLine(pen, s.X, s.Y - spikeLen, s.X, s.Y + spikeLen);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Renders static nebula patches for galaxy color — purple, blue, teal (one-time).</summary>
        void RenderStaticNebula(Bitmap bmp, int w, int h, float alphaMul)
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rng = new Random(SEED + 7777);
                int patchCount = 8 + rng.Next(5); // 8-12 patches for rich nebula
                Color[] nebulaColors = new Color[] {
                    Color.FromArgb(60, 40, 120),   // Deep purple
                    Color.FromArgb(40, 50, 130),   // Royal blue
                    Color.FromArgb(80, 45, 110),   // Violet
                    Color.FromArgb(30, 70, 100),   // Deep teal
                    Color.FromArgb(70, 35, 95),    // Dark magenta
                    Color.FromArgb(45, 60, 115),   // Midnight blue
                    Color.FromArgb(55, 50, 100),   // Lavender
                    Color.FromArgb(50, 35, 85),    // Dusty purple
                    Color.FromArgb(35, 55, 80),    // Steel blue
                };

                // Large center glow — gives the sky depth
                float cxG = w * (0.35f + (float)rng.NextDouble() * 0.3f);
                float cyG = h * (0.3f + (float)rng.NextDouble() * 0.3f);
                float radiusG = Math.Max(w, h) * 0.7f;
                int glowAlpha = (int)(12 * alphaMul);
                if (glowAlpha > 0)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(cxG - radiusG, cyG - radiusG, radiusG * 2, radiusG * 2);
                        using (var pgb = new PathGradientBrush(path))
                        {
                            pgb.CenterColor = Color.FromArgb(glowAlpha, 30, 22, 60);
                            pgb.SurroundColors = new Color[] { Color.FromArgb(0, 15, 12, 35) };
                            pgb.FocusScales = new PointF(0.0f, 0.0f);
                            g.FillPath(pgb, path);
                        }
                    }
                }

                // Secondary off-center glow for asymmetry
                float cx2 = w * (0.6f + (float)rng.NextDouble() * 0.3f);
                float cy2 = h * (0.5f + (float)rng.NextDouble() * 0.3f);
                float r2 = Math.Max(w, h) * 0.5f;
                int ga2 = (int)(8 * alphaMul);
                if (ga2 > 0)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(cx2 - r2, cy2 - r2, r2 * 2, r2 * 2);
                        using (var pgb = new PathGradientBrush(path))
                        {
                            pgb.CenterColor = Color.FromArgb(ga2, 20, 35, 65);
                            pgb.SurroundColors = new Color[] { Color.FromArgb(0, 10, 18, 35) };
                            pgb.FocusScales = new PointF(0.0f, 0.0f);
                            g.FillPath(pgb, path);
                        }
                    }
                }

                // Nebula cloud patches — multi-layered for depth
                for (int p = 0; p < patchCount; p++)
                {
                    float cx = (float)(rng.NextDouble() * w);
                    float cy = (float)(rng.NextDouble() * h);
                    Color nc = nebulaColors[rng.Next(nebulaColors.Length)];
                    int layers = 5 + rng.Next(4); // 5-8 layers per patch for smoother falloff
                    for (int layer = 0; layer < layers; layer++)
                    {
                        float ox = cx + (float)(rng.NextDouble() - 0.5) * w * 0.18f;
                        float oy = cy + (float)(rng.NextDouble() - 0.5) * h * 0.18f;
                        float rw = w * (0.12f + (float)rng.NextDouble() * 0.25f);
                        float rh = h * (0.10f + (float)rng.NextDouble() * 0.20f);
                        int baseA = (int)((8 + rng.Next(12)) * alphaMul);
                        if (baseA < 1) continue;
                        baseA = Math.Min(baseA, 30);
                        using (var path = new GraphicsPath())
                        {
                            path.AddEllipse(ox - rw / 2, oy - rh / 2, rw, rh);
                            using (var pgb = new PathGradientBrush(path))
                            {
                                pgb.CenterColor = Color.FromArgb(baseA, nc.R, nc.G, nc.B);
                                pgb.SurroundColors = new Color[] { Color.FromArgb(0, nc.R, nc.G, nc.B) };
                                pgb.FocusScales = new PointF(0.15f, 0.15f);
                                g.FillPath(pgb, path);
                            }
                        }
                    }
                }

                // Subtle star dust lane — a faint milky way-like band
                float bandY = h * (0.3f + (float)rng.NextDouble() * 0.4f);
                float bandH = h * 0.35f;
                float bandAngle = -15f + (float)rng.NextDouble() * 30f; // slight tilt
                var gs = g.Save();
                g.TranslateTransform(w / 2f, bandY);
                g.RotateTransform(bandAngle);
                g.TranslateTransform(-w / 2f, -bandY);
                int bandAlpha = (int)(6 * alphaMul);
                if (bandAlpha > 0)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(-w * 0.1f, bandY - bandH / 2, w * 1.2f, bandH);
                        using (var pgb = new PathGradientBrush(path))
                        {
                            pgb.CenterColor = Color.FromArgb(bandAlpha, 40, 35, 70);
                            pgb.SurroundColors = new Color[] { Color.FromArgb(0, 20, 18, 35) };
                            pgb.FocusScales = new PointF(0.3f, 0.1f);
                            g.FillPath(pgb, path);
                        }
                    }
                }
                g.Restore(gs);
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

            // Create base bitmaps
            _base = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _baseDim = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _twinkleOverlay = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _twinkleOverlayDim = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            // Nebula backdrop (painted first, stars go on top)
            RenderStaticNebula(_base, w, h, 1.0f);
            RenderStaticNebula(_baseDim, w, h, 0.35f);

            // Stars
            InitCloudsAndBodies(w, h);
            RenderStars(_base, _starsNorm, false, 0);
            RenderStars(_baseDim, _starsDim, false, 0);

            // Moon + planets on top of stars (static)
            using (var gBase = Graphics.FromImage(_base))
            {
                gBase.SmoothingMode = SmoothingMode.AntiAlias;
                RenderBodiesOnly(gBase, w, h, 1.0f);
            }
            using (var gDim = Graphics.FromImage(_baseDim))
            {
                gDim.SmoothingMode = SmoothingMode.AntiAlias;
                RenderBodiesOnly(gDim, w, h, 0.35f);
            }

            // 4. Apply actual bitmap flip/rotation — visually dramatic
            RotateFlipType rft;
            switch (_flipMode)
            {
                case 1: rft = RotateFlipType.RotateNoneFlipX; break;
                case 2: rft = RotateFlipType.RotateNoneFlipY; break;
                case 3: rft = RotateFlipType.Rotate180FlipNone; break;
                case 4: rft = RotateFlipType.RotateNoneFlipXY; break;
                default: rft = RotateFlipType.RotateNoneFlipNone; break;
            }
            if (rft != RotateFlipType.RotateNoneFlipNone)
            {
                _base.RotateFlip(rft);
                _baseDim.RotateFlip(rft);
            }

            _twinkleDirty = true;
        }

        // EnsureTwinkle is now handled by the render thread

        /// <summary>Blits pre-rendered base + composite. Fast (<1ms), never skips.</summary>
        public void Paint(Graphics g, int w, int h, int ox = 0, int oy = 0, bool dim = false, bool shootingStar = true)
        {
            try {
                EnsureBase(w, h);
                var b = dim ? _baseDim : _base;
                if (b != null) g.DrawImage(b, -ox, -oy);
                lock (_compositeLock)
                {
                    var comp = dim ? _compositeFrontDim : _compositeFrontNorm;
                    if (comp != null) g.DrawImage(comp, -ox, -oy);
                }
            } catch { try { g.ResetTransform(); } catch { } }
        }

        public void PaintGlassTint(Graphics g, int w, int h, System.Drawing.Drawing2D.GraphicsPath path)
        {
            EnsureBase(w, h);
            using (var tint = new SolidBrush(TINT))
                g.FillPath(tint, path);
            var oldClip = g.Clip;
            g.SetClip(path);
            if (_baseDim != null) g.DrawImage(_baseDim, 0, 0);
            lock (_compositeLock)
            {
                if (_compositeFrontDim != null) g.DrawImage(_compositeFrontDim, 0, 0);
            }
            g.Clip = oldClip;
        }

        public void PaintChildBg(Graphics g, int w, int h, int ox, int oy, int childW, int childH)
        {
            try {
                EnsureBase(w, h);
                using (var bg = new SolidBrush(DarkTheme.BG))
                    g.FillRectangle(bg, 0, 0, childW, childH);
                if (_base != null) g.DrawImage(_base, -ox, -oy);
                lock (_compositeLock)
                {
                    if (_compositeFrontNorm != null) g.DrawImage(_compositeFrontNorm, -ox, -oy);
                }

                using (var tint = new SolidBrush(TINT))
                    g.FillRectangle(tint, 0, 0, childW, childH);
                if (_baseDim != null) g.DrawImage(_baseDim, -ox, -oy);
                lock (_compositeLock)
                {
                    if (_compositeFrontDim != null) g.DrawImage(_compositeFrontDim, -ox, -oy);
                }
            } catch { try { g.ResetTransform(); } catch { } }
        }

        public void Pause()
        {
            _isPaused = true;
            if (Shooting != null) Shooting.Pause();
            if (Celestial != null) Celestial.Pause();
        }

        public void Resume()
        {
            _isPaused = false;
            if (Shooting != null) Shooting.Resume();
            if (Celestial != null) Celestial.Resume();
            
            // Force an immediate frame redraw
            lock (_compositeLock) {
                if (_invalidateCallback != null)
                    try { _invalidateCallback(); } catch { }
            }
        }

        public void Dispose()
        {
            _renderRunning = false;
            if (_renderThread != null) try { _renderThread.Join(500); } catch { }
            if (Shooting != null) { Shooting.Stop(); Shooting.Dispose(); }
            if (Celestial != null) { Celestial.Stop(); Celestial.Dispose(); }
            if (_base != null) _base.Dispose(); if (_baseDim != null) _baseDim.Dispose();
            if (_twinkleOverlay != null) _twinkleOverlay.Dispose(); if (_twinkleOverlayDim != null) _twinkleOverlayDim.Dispose();
            lock (_compositeLock)
            {
                if (_compositeFrontNorm != null) _compositeFrontNorm.Dispose();
                if (_compositeFrontDim != null) _compositeFrontDim.Dispose();
                if (_compositeNorm != null) _compositeNorm.Dispose();
                if (_compositeDim != null) _compositeDim.Dispose();
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
            public int MeteorType;      // 0=streak, 1=fireball, 2=comet, 3=whisper, 4=bolt, 5=fragment, 6=twin, 7=phantom, 8=flare, 9=ember_trail, 10=cascade, 11=debris, 12=slowburn, 13=sparkler, 14=skimmer, 15=splitter, 16=corkscrew, 17=fadechain, 18=iridescent, 19=smoketrail, 20=earthgrazer, 21=flashfreeze, 22=doublehead
            public float TwinOffset;    // for twin type — perpendicular offset
            public float FragAngle;     // for fragment type — burst angle
            public float Param1;        // multipurpose: spiral frequency, ricochet bounce point, etc.
        }

        public Meteor[] Stars = new Meteor[75]; // 75 slots for dev-click meteor storm

        private static readonly Random _rng = new Random();
        private System.Threading.Timer _timer;
        private const int TICK_MS = 30;
        private Action _invalidateCallback;

        public ShootingStar(Action invalidateCallback)
        {
            _invalidateCallback = invalidateCallback;
            _timer = new System.Threading.Timer(OnTickThreaded, null, System.Threading.Timeout.Infinite, TICK_MS);
            for (int i = 0; i < NaturalSlots; i++)
                ScheduleNext(i);
        }

        public const int NaturalSlots = 5; // Only first 5 slots auto-spawn; rest reserved for force-launch
        public void Start() { _timer.Change(0, TICK_MS); }
        public void Stop() { _timer.Change(System.Threading.Timeout.Infinite, TICK_MS); for (int i = 0; i < Stars.Length; i++) Stars[i].Active = false; }
        public void Pause() { _timer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite); }
        public void Resume() { _timer.Change(0, TICK_MS); }

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

        private void OnTickThreaded(object state)
        {
            bool needRepaint = false;
            for (int i = 0; i < Stars.Length; i++)
            {
                Stars[i].ElapsedMs += TICK_MS;
                if (!Stars[i].Active && i < NaturalSlots)
                {
                    if (Stars[i].ElapsedMs >= Stars[i].CooldownMs)
                    { LaunchStar(i); needRepaint = true; }
                }
                else
                {
                    Stars[i].Progress += Stars[i].Speed * TICK_MS / 30f;
                    if (Stars[i].Progress >= 1f)
                        ScheduleNext(i);
                    needRepaint = true;
                }
            }
            if (needRepaint && _invalidateCallback != null) { try { _invalidateCallback(); } catch { } }
        }

        public void Dispose()
        {
            if (_timer != null) { _timer.Change(System.Threading.Timeout.Infinite, 0); _timer.Dispose(); }
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
        private System.Threading.Timer _timer;
        private const int TICK_MS = 30;
        private Action _invalidateCallback;

        public CelestialEvents(Action invalidateCallback)
        {
            _invalidateCallback = invalidateCallback;
            _cooldownMs = 1000 + _rng.Next(4000);
            _elapsedMs = 0;
            _timer = new System.Threading.Timer(OnTickThreaded, null, System.Threading.Timeout.Infinite, TICK_MS);
        }

        public const int NaturalSlots = 2; // Slots 0-1 auto-spawn; rest reserved for force-launch
        public void Start() { _timer.Change(0, TICK_MS); }
        public void Stop() { _timer.Change(System.Threading.Timeout.Infinite, TICK_MS); for(int i=0;i<Events.Length;i++) Events[i].Active=false; }
        public void Pause() { _timer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite); }
        public void Resume() { _timer.Change(0, TICK_MS); }

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
            _cooldownMs = 1000 + _rng.Next(4000);
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

        void OnTickThreaded(object state)
        {
            _elapsedMs += TICK_MS;
            // Auto-spawn in slots 0 and 1
            for (int slot = 0; slot < NaturalSlots; slot++)
            {
                if (!Events[slot].Active)
                {
                    if (_elapsedMs >= _cooldownMs) { LaunchEvent(slot); _cooldownMs = 1000 + _rng.Next(4000); _elapsedMs = 0; }
                }
            }

            bool anyActive = false;
            float dt = TICK_MS / 30f;
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
                if (Events[i].Progress >= 1f) { Events[i].Active = false; if (i < NaturalSlots) { _cooldownMs = 1000 + _rng.Next(4000); _elapsedMs = 0; } }
            }
            if ((anyActive || !Events[0].Active) && _invalidateCallback != null) { try { _invalidateCallback(); } catch { } }
        }

        public void Dispose() { if (_timer != null) { _timer.Change(System.Threading.Timeout.Infinite, 0); _timer.Dispose(); } }
    }

}
