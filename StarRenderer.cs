// StarRenderer.cs — All star/celestial/shield/orbiting rendering methods.
// Part of partial class DarkTheme — colors and fonts are in DarkTheme.cs.
// Contains: PaintStars, PaintCardStars, PaintShootingStar, PaintCelestialEvent,
//           DrawShield, PaintOrbitingStar, HsvToArgb.
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
                float endX = startX + m.DirX * width * 1.2f;
                float endY = startY + m.DirY * height * 1.2f;
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

                    // Spark/debris particles — trail BEHIND the meteor in a cone shape
                    if (m.Brightness > 0.25f && t > 0.05f && t < 0.90f)
                    {
                        int sparkleCount = m.MeteorType == 1 ? 16 : (m.Brightness > 0.65f ? 12 : 8);
                        int sparkSeed = mi * 1000 + (int)(t * 40) + (int)(DateTime.UtcNow.Ticks / 800000);
                        var sparkRng = new Random(sparkSeed);
                        for (int sp = 0; sp < sparkleCount; sp++)
                        {
                            // Particles trail behind the head — "along" goes from head toward tail
                            float along = (float)sparkRng.NextDouble() * 0.8f;
                            // Cone spread: wider further from head, narrow at head
                            float coneWidth = along * m.GlowSize * 1.8f;
                            float scatter = ((float)sparkRng.NextDouble() - 0.5f) * 2f * coneWidth;
                            float sparkX = headX + (tailX - headX) * along + perpX * scatter;
                            float sparkY = headY + (tailY - headY) * along + perpY * scatter;
                            float sparkSize = 0.5f + (float)sparkRng.NextDouble() * 1.5f;
                            int sparkAlpha = (int)(alpha * 0.6f * (1f - along * 0.7f));
                            if (sparkAlpha > 0)
                            {
                                // Realistic colors: white-hot near head, warm further back
                                int sR, sG, sB;
                                if (along < 0.3f) { sR = 255; sG = 255; sB = 255; } // white-hot near head
                                else if (along < 0.6f) { sR = cR; sG = cG; sB = cB; } // meteor's color
                                else { // warm orange/red at tail end — cooling embers
                                    sR = Math.Min(255, cR + 40);
                                    sG = Math.Max(0, cG - 30);
                                    sB = Math.Max(0, cB - 50);
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

                    // RICOCHET → now FLARE (8): bright flare-up mid-flight then dims — like hitting a dense pocket
                    if (m.MeteorType == 8)
                    {
                        float flareT = m.Param1; // flare point (30-70%)
                        float flareDist = Math.Abs(t - flareT);
                        if (flareDist < 0.12f) {
                            float flareI = 1f - flareDist / 0.12f;
                            int fa = (int)(flareI * flareI * alpha * 0.6f);
                            if (fa > 0) {
                                float fr = glowR * (1.5f + flareI * 2.5f);
                                using (var b = new SolidBrush(Color.FromArgb(fa, cR, cG, cB)))
                                    g.FillEllipse(b, headX - fr, headY - fr, fr * 2, fr * 2);
                            }
                        }
                    }

                    // SPIRAL → now EMBER_TRAIL (9): hot embers shed backward in a cone behind the meteor
                    if (m.MeteorType == 9)
                    {
                        var rng2 = new Random((int)(m.StartX * 10000));
                        float dirLen = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (dirLen > 0) {
                            float ndx = dx / dirLen, ndy = dy / dirLen;
                            int numEmbers = 10;
                            for (int ei = 0; ei < numEmbers; ei++) {
                                float et = t - ei * 0.018f;
                                if (et < 0) continue;
                                float ex = startX + dx * et;
                                float ey = startY + dy * et;
                                // Embers drift backward and slightly sideways (cone shape)
                                float drift = ei * 1.5f;
                                float spread = (float)(rng2.NextDouble() - 0.5) * ei * 1.2f;
                                ex -= ndx * drift + perpX * spread;
                                ey -= ndy * drift + perpY * spread;
                                int ea = (int)(alpha * (1f - ei / (float)numEmbers) * 0.5f);
                                float er = 0.8f + (float)rng2.NextDouble() * 0.8f;
                                if (ea > 0) {
                                    int warmR = Math.Min(255, cR + 40), warmG = Math.Max(0, cG - 20);
                                    using (var b = new SolidBrush(Color.FromArgb(ea, warmR, warmG, cB)))
                                        g.FillEllipse(b, ex - er, ey - er, er * 2, er * 2);
                                }
                            }
                        }
                    }

                    // CASCADE (10): breaks into sparks at the end — sparks continue roughly same direction with spread
                    if (m.MeteorType == 10 && t > 0.75f)
                    {
                        float burstT = (t - 0.75f) / 0.25f;
                        var rng2 = new Random((int)(m.StartX * 10000));
                        float dirLen = (float)Math.Sqrt(dx * dx + dy * dy);
                        float ndx = dirLen > 0 ? dx / dirLen : 0, ndy = dirLen > 0 ? dy / dirLen : 1;
                        int numSparks = (int)(6 * (1f - burstT));
                        for (int sp = 0; sp < numSparks; sp++) {
                            // Sparks continue forward with spread — cone behind travel direction
                            float sparkDx = ndx * 0.7f + (float)(rng2.NextDouble() - 0.5) * 0.5f;
                            float sparkDy = ndy * 0.7f + (float)(rng2.NextDouble() - 0.5) * 0.5f;
                            float dist = burstT * m.GlowSize * 4 * (0.5f + (float)rng2.NextDouble());
                            float spx = headX + sparkDx * dist;
                            float spy = headY + sparkDy * dist;
                            int spa = (int)(alpha * (1f - burstT) * 0.6f);
                            if (spa > 0)
                                using (var b = new SolidBrush(Color.FromArgb(spa, cR, cG, cB)))
                                    g.FillEllipse(b, spx - 0.8f, spy - 0.8f, 1.6f, 1.6f);
                        }
                    }

                    // GLITTER (11) → now DEBRIS: small debris particles shed behind in travel direction cone
                    if (m.MeteorType == 11)
                    {
                        var rng2 = new Random((int)(m.StartX * 10000));
                        float dirLen = (float)Math.Sqrt(dx * dx + dy * dy);
                        float ndx = dirLen > 0 ? dx / dirLen : 0, ndy = dirLen > 0 ? dy / dirLen : 1;
                        int numDebris = 8;
                        for (int di = 0; di < numDebris; di++) {
                            float dt = t - di * 0.03f;
                            if (dt < 0 || dt > 1) continue;
                            float dbx = startX + dx * dt;
                            float dby = startY + dy * dt;
                            // Drift backward + slight cone spread
                            float drift = di * 2f;
                            float spread = (float)(rng2.NextDouble() - 0.5) * di * 0.8f;
                            dbx -= ndx * drift + perpX * spread;
                            dby -= ndy * drift + perpY * spread;
                            float fade = (1f - di / (float)numDebris) * (1f - dt * 0.5f);
                            int da = (int)(alpha * fade * 0.35f);
                            if (da > 0)
                                using (var b = new SolidBrush(Color.FromArgb(da, cR, cG, cB)))
                                    g.FillEllipse(b, dbx - 0.5f, dby - 0.5f, 1.2f, 1.2f);
                        }
                    }

                    // SLOWBURN (12): wider glowing trail — like a bright persistent ionization trail
                    if (m.MeteorType == 12)
                    {
                        float scarAlpha = alpha * 0.25f;
                        if (scarAlpha > 3) {
                            using (var p = new Pen(Color.FromArgb((int)scarAlpha, cR, cG, cB), m.Thickness * 2.5f))
                            {
                                p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                                p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                                g.DrawLine(p, tailX, tailY, headX, headY);
                            }
                        }
                    }

                    // SKIMMER (14): shallow grazer — subtle wake glow below (atmospheric skip)
                    if (m.MeteorType == 14 && t > 0.1f && t < 0.9f)
                    {
                        int refA = (int)(alpha * 0.12f);
                        if (refA > 0)
                            using (var p = new Pen(Color.FromArgb(refA, cR, cG, cB), m.Thickness * 0.4f))
                            {
                                p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                                p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                                g.DrawLine(p, tailX, tailY + m.GlowSize, headX, headY + m.GlowSize);
                            }
                    }

                    // TWIN (6): two parallel streaks with shared glow
                    if (m.MeteorType == 6)
                    {
                        float tOff = m.TwinOffset;
                        // Second streak offset perpendicular
                        float h2x = headX + perpX * tOff;
                        float h2y = headY + perpY * tOff;
                        float t2x = tailX + perpX * tOff;
                        float t2y = tailY + perpY * tOff;
                        int twinA = (int)(alpha * 0.7f);
                        using (var p2 = new Pen(Color.FromArgb(twinA, cR, cG, cB), m.Thickness * 0.8f))
                        {
                            p2.StartCap = LineCap.Round; p2.EndCap = LineCap.Round;
                            g.DrawLine(p2, h2x, h2y, t2x, t2y);
                        }
                        // Shared glow between the two
                        float midX = (headX + h2x) / 2, midY = (headY + h2y) / 2;
                        int sharedA = alpha / 5;
                        if (sharedA > 0)
                            using (var b = new SolidBrush(Color.FromArgb(sharedA, cR, cG, cB)))
                                g.FillEllipse(b, midX - glowR, midY - glowR, glowR * 2, glowR * 2);
                    }

                    // SPARKLER (13): leaves a trail of rainbow sparkle points that linger
                    if (m.MeteorType == 13)
                    {
                        int sparkCount = (int)m.Param1;
                        var sRng = new Random((int)(m.StartX * 10000) + (int)(m.StartY * 10000));
                        for (int sp = 0; sp < sparkCount; sp++)
                        {
                            float sparkT = (float)sp / sparkCount;
                            if (sparkT > t) continue;
                            float age = t - sparkT;
                            if (age > 0.4f) continue; // sparkles fade after 40% of flight
                            float sparkFade = 1f - age / 0.4f;
                            float spX = startX + dx * sparkT + ((float)sRng.NextDouble() - 0.5f) * m.GlowSize * 3;
                            float spY = startY + dy * sparkT + ((float)sRng.NextDouble() - 0.5f) * m.GlowSize * 3;
                            float hue = (sparkT * 360 + age * 200) % 360;
                            // Rainbow sparkle in the trail
                            float hueRad = hue * (float)Math.PI / 180f;
                            int spR = (int)(128 + 127 * Math.Sin(hueRad));
                            int spG = (int)(128 + 127 * Math.Sin(hueRad + 2.094f));
                            int spB = (int)(128 + 127 * Math.Sin(hueRad + 4.189f));
                            int spA = (int)(alpha * sparkFade * 0.6f);
                            float spSz = 1.0f + sparkFade * 1.5f;
                            if (spA > 3)
                                using (var b = new SolidBrush(Color.FromArgb(spA, spR, spG, spB)))
                                    g.FillEllipse(b, spX - spSz, spY - spSz, spSz * 2, spSz * 2);
                        }
                    }

                    // SPLITTER (15): one meteor that splits into two diverging paths
                    if (m.MeteorType == 15 && t > m.Param1)
                    {
                        float splitProgress = (t - m.Param1) / (1f - m.Param1);
                        float diverge = splitProgress * m.GlowSize * 2.5f;
                        // Two heads diverging from the split point
                        for (int side = -1; side <= 1; side += 2)
                        {
                            float sHeadX = headX + perpX * diverge * side;
                            float sHeadY = headY + perpY * diverge * side;
                            int sAlpha = (int)(alpha * (1f - splitProgress * 0.3f));
                            float sThick = m.Thickness * (0.7f - splitProgress * 0.2f);
                            // Short trail from split point
                            float splitX = startX + dx * m.Param1;
                            float splitY = startY + dy * m.Param1;
                            using (var p2 = new Pen(Color.FromArgb(sAlpha / 2, cR, cG, cB), Math.Max(0.5f, sThick)))
                            {
                                p2.StartCap = LineCap.Round; p2.EndCap = LineCap.Round;
                                g.DrawLine(p2, sHeadX, sHeadY, splitX, splitY);
                            }
                            // Head glow on each split
                            float sGlow = glowR * (0.6f - splitProgress * 0.2f);
                            using (var b = new SolidBrush(Color.FromArgb(sAlpha / 3, cR, cG, cB)))
                                g.FillEllipse(b, sHeadX - sGlow, sHeadY - sGlow, sGlow * 2, sGlow * 2);
                        }
                    }

                    // CORKSCREW (16): helix spiral trail
                    if (m.MeteorType == 16)
                    {
                        float spiralFreq = m.Param1;
                        int helixPts = 20;
                        for (int hp = 0; hp < helixPts; hp++)
                        {
                            float ht = t - hp * 0.008f;
                            if (ht < 0) continue;
                            float hpX = startX + dx * ht;
                            float hpY = startY + dy * ht;
                            float spiral = (float)Math.Sin(ht * spiralFreq) * m.GlowSize * 1.5f;
                            hpX += perpX * spiral;
                            hpY += perpY * spiral;
                            float hFade = 1f - (float)hp / helixPts;
                            int hpA = (int)(alpha * hFade * 0.4f);
                            if (hpA > 2)
                                using (var b = new SolidBrush(Color.FromArgb(hpA, cR, cG, cB)))
                                    g.FillEllipse(b, hpX - 0.8f, hpY - 0.8f, 1.6f, 1.6f);
                        }
                    }

                    // FADECHAIN (17): pulses brightness multiple times during flight
                    if (m.MeteorType == 17)
                    {
                        float pulses = m.Param1;
                        float pulseWave = (float)Math.Sin(t * pulses * Math.PI * 2);
                        // Extra glow ring that pulses
                        float pulseR = glowR * (1f + pulseWave * 0.8f);
                        int pulseA = (int)(alpha * 0.3f * (0.5f + pulseWave * 0.5f));
                        if (pulseA > 3)
                            using (var b = new SolidBrush(Color.FromArgb(pulseA, cR, cG, cB)))
                                g.FillEllipse(b, headX - pulseR, headY - pulseR, pulseR * 2, pulseR * 2);
                    }

                    // IRIDESCENT (18): white/natural body with rainbow shimmer TRAIL
                    if (m.MeteorType == 18)
                    {
                        int trailPts = 30;
                        for (int tp = 1; tp < trailPts; tp++)
                        {
                            float tt = t - tp * 0.006f;
                            if (tt < 0) continue;
                            float tpX = startX + dx * tt;
                            float tpY = startY + dy * tt;
                            float tFade = 1f - (float)tp / trailPts;
                            // Rainbow hue shifts along trail length
                            float hue = ((float)tp / trailPts * 300 + t * 500) % 360;
                            float hueRad = hue * (float)Math.PI / 180f;
                            int iR = (int)(160 + 95 * Math.Sin(hueRad));
                            int iG = (int)(160 + 95 * Math.Sin(hueRad + 2.094f));
                            int iB = (int)(160 + 95 * Math.Sin(hueRad + 4.189f));
                            int iA = (int)(alpha * tFade * 0.5f);
                            float iSz = m.Thickness * tFade * 0.8f;
                            if (iA > 2 && iSz > 0.3f)
                            {
                                // Core rainbow point
                                using (var b = new SolidBrush(Color.FromArgb(iA, iR, iG, iB)))
                                    g.FillEllipse(b, tpX - iSz, tpY - iSz, iSz * 2, iSz * 2);
                                // Soft glow halo around each point
                                int haloA = iA / 4;
                                float haloSz = iSz * 2.5f;
                                if (haloA > 1)
                                    using (var b = new SolidBrush(Color.FromArgb(haloA, iR, iG, iB)))
                                        g.FillEllipse(b, tpX - haloSz, tpY - haloSz, haloSz * 2, haloSz * 2);
                            }
                        }
                    }

                    // SMOKETRAIL (19): wide, slowly fading smoke-like wake behind the meteor
                    if (m.MeteorType == 19)
                    {
                        int smokePts = 18;
                        for (int sp = 1; sp < smokePts; sp++)
                        {
                            float st = t - sp * 0.012f;
                            if (st < 0) continue;
                            float spX = startX + dx * st;
                            float spY = startY + dy * st;
                            float sFade = 1f - (float)sp / smokePts;
                            float smokeR = m.GlowSize * (1.5f + (1f - sFade) * 3f); // smoke expands as it ages
                            int sA = (int)(alpha * sFade * 0.08f); // very faint
                            if (sA > 1)
                                using (var b = new SolidBrush(Color.FromArgb(sA, 180, 170, 160)))
                                    g.FillEllipse(b, spX - smokeR, spY - smokeR, smokeR * 2, smokeR * 2);
                        }
                    }

                    // EARTHGRAZER (20): extra-wide persistent glow along entire trail, green tinge
                    if (m.MeteorType == 20)
                    {
                        // Green atmospheric glow — like a real earthgrazer
                        int greenA = (int)(alpha * 0.15f);
                        if (greenA > 2)
                        {
                            using (var p2 = new Pen(Color.FromArgb(greenA, 100, 255, 140), m.Thickness * 3f))
                            {
                                p2.StartCap = LineCap.Round; p2.EndCap = LineCap.Round;
                                g.DrawLine(p2, headX, headY, tailX, tailY);
                            }
                        }
                        // Bright persistent ionization trail
                        float farTailX = tailX - dx * trailLen * 0.8f;
                        float farTailY = tailY - dy * trailLen * 0.8f;
                        int ionA = (int)(alpha * 0.1f);
                        if (ionA > 1)
                            using (var p2 = new Pen(Color.FromArgb(ionA, 140, 200, 160), m.Thickness * 0.6f))
                            {
                                p2.StartCap = LineCap.Round; p2.EndCap = LineCap.Round;
                                g.DrawLine(p2, tailX, tailY, farTailX, farTailY);
                            }
                    }

                    // FLASHFREEZE (21): starts hot white/yellow, rapidly transitions to icy blue
                    if (m.MeteorType == 21)
                    {
                        // Ice crystal trail behind — blue tint strengthens with progress
                        float iceAmount = Math.Min(1f, t * 2.5f); // reaches full ice by 40%
                        int iceR = (int)(255 - iceAmount * 115);  // 255 → 140
                        int iceG = (int)(255 - iceAmount * 75);   // 255 → 180
                        int iceB = 255;                            // stays 255
                        int iceA = (int)(alpha * 0.2f);
                        if (iceA > 2) {
                            float iceGlow = glowR * (1.2f + iceAmount * 0.8f);
                            using (var b = new SolidBrush(Color.FromArgb(iceA, iceR, iceG, iceB)))
                                g.FillEllipse(b, headX - iceGlow, headY - iceGlow, iceGlow * 2, iceGlow * 2);
                        }
                        // Tiny ice crystal sparkles
                        if (t > 0.15f) {
                            var iceRng = new Random((int)(m.StartX * 10000));
                            for (int ic = 0; ic < 6; ic++) {
                                float icT = t - ic * 0.02f;
                                if (icT < 0) continue;
                                float icX = startX + dx * icT + ((float)iceRng.NextDouble() - 0.5f) * m.GlowSize * 2;
                                float icY = startY + dy * icT + ((float)iceRng.NextDouble() - 0.5f) * m.GlowSize * 2;
                                int icA = (int)(alpha * 0.4f * (1f - (float)ic / 6));
                                if (icA > 2)
                                    using (var b = new SolidBrush(Color.FromArgb(icA, 180, 220, 255)))
                                        g.FillEllipse(b, icX - 0.6f, icY - 0.6f, 1.2f, 1.2f);
                            }
                        }
                    }

                    // DOUBLEHEAD (22): two heads in slight V formation sharing one trail
                    if (m.MeteorType == 22)
                    {
                        float vOff = m.TwinOffset;
                        float lead = m.GlowSize * 0.6f; // second head slightly behind
                        // Second head — offset perpendicular and slightly behind
                        float h2x = headX + perpX * vOff - dx / pLen * lead;
                        float h2y = headY + perpY * vOff - dy / pLen * lead;
                        // Second head glow
                        int h2Alpha = (int)(alpha * 0.65f);
                        float h2Glow = glowR * 0.7f;
                        using (var b = new SolidBrush(Color.FromArgb(h2Alpha / 2, cR, cG, cB)))
                            g.FillEllipse(b, h2x - h2Glow * 1.2f, h2y - h2Glow * 1.2f, h2Glow * 2.4f, h2Glow * 2.4f);
                        using (var b = new SolidBrush(Color.FromArgb(h2Alpha, 255, 255, 255)))
                            g.FillEllipse(b, h2x - h2Glow * 0.5f, h2y - h2Glow * 0.5f, h2Glow, h2Glow);
                        // Thin trail from second head merging into main trail
                        float mergeX = (tailX + headX) / 2 + perpX * vOff * 0.3f;
                        float mergeY = (tailY + headY) / 2 + perpY * vOff * 0.3f;
                        int trA = h2Alpha / 3;
                        if (trA > 2)
                            using (var p2 = new Pen(Color.FromArgb(trA, cR, cG, cB), m.Thickness * 0.5f))
                            {
                                p2.StartCap = LineCap.Round; p2.EndCap = LineCap.Round;
                                g.DrawLine(p2, h2x, h2y, mergeX, mergeY);
                            }
                    }
                }
                catch { }
            }
        }
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
                    // Wave 4 — Icons & Whimsy
                    case CelestialEvents.EventType.SantaSleigh: PaintSantaSleigh(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.Angel: PaintAngel(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.JackOLantern: PaintJackOLantern(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.Cupid: PaintCupid(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.Starfighter: PaintStarfighter(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.MechBattle: PaintMechBattle(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpaceStation: PaintSpaceStation(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.DeLorean: PaintDeLorean(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.StarCruiser: PaintStarCruiser(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.CosmicEye: PaintCosmicEye(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.TreeRocket: PaintTreeRocket(g, w, h, ev, t, fade); break;

                    case CelestialEvents.EventType.CowMoon: PaintCowMoon(g, w, h, ev, t, fade); break;

                    case CelestialEvents.EventType.SpaceCatsuit: PaintSpaceCatsuit(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.AlienWave: PaintAlienWave(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.RubberDucky: PaintRubberDucky(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.Snowglobe: PaintSnowglobe(g, w, h, ev, t, fade); break;

                    case CelestialEvents.EventType.CosmicSword: PaintCosmicSword(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.GoldfishBowl: PaintGoldfishBowl(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.DiscoBall: PaintDiscoBall(g, w, h, ev, t, fade); break;

                    case CelestialEvents.EventType.SpaceHamster: PaintSpaceHamster(g, w, h, ev, t, fade); break;

                    // Wave 5

                    case CelestialEvents.EventType.SpaceTelescope: PaintSpaceTelescope(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.AsteroidField: PaintAsteroidField(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.GammaRay: PaintGammaRay(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpaceOctopus: PaintSpaceOctopus(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.CosmicTurtle: PaintCosmicTurtle(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpaceBee: PaintSpaceBee(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.RocketPenguin: PaintRocketPenguin(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.CosmicFrog: PaintCosmicFrog(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpaceSquid: PaintSpaceSquid(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.PirateGalleon: PaintPirateGalleon(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.BattleFleet: PaintBattleFleet(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpaceSubmarine: PaintSpaceSubmarine(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.HotAirBalloon: PaintHotAirBalloon(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpaceMotorcycle: PaintSpaceMotorcycle(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.CosmicWizard: PaintCosmicWizard(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpaceSnowman: PaintSpaceSnowman(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.CosmicHourglass: PaintCosmicHourglass(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpaceAnchor: PaintSpaceAnchor(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.CosmicDice: PaintCosmicDice(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.RobotDog: PaintRobotDog(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.RingNebula: PaintRingNebula(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.CosmicLightning: PaintCosmicLightning(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.StarNursery: PaintStarNursery(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.GravityWave: PaintGravityWave(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.HaloRing: PaintHaloRing(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.EasterEgg: PaintEasterEgg(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.TennisRacket: PaintTennisRacket(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SchoolOfFish: PaintSchoolOfFish(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.FallingStar: PaintFallingStar(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpaceViolin: PaintSpaceViolin(g, w, h, ev, t, fade); break;

                    case CelestialEvents.EventType.RobotCrab: PaintRobotCrab(g, w, h, ev, t, fade); break;
                    case CelestialEvents.EventType.SpaceWaldo: PaintSpaceWaldo(g, w, h, ev, t, fade); break;
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
            float dir = ev.DirX > 0 ? 1 : -1;

            // Rainbow trail — 6 color bands BEHIND the cat
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
                    g.DrawLine(p, cx - dir * trailLen, by + waveBob, cx - dir * 4, by + bob);
                }
            }

            // Cat body: pop-tart rectangle (tiny!)
            float bw = 10, bh = 8;
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 200, 150)))
                g.FillRectangle(b, cx - bw / 2, cy - bh / 2 + bob, bw, bh);
            // Frosting
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 130, 160)))
                g.FillRectangle(b, cx - bw / 2 + 1, cy - bh / 2 + 1 + bob, bw - 2, bh - 2);
            // Cat face (dark) — on the FRONT side
            float faceX = cx + dir * bw / 2;
            using (var b = new SolidBrush(Color.FromArgb(a, 80, 80, 80)))
            {
                // Head
                g.FillEllipse(b, faceX - (dir > 0 ? 1 : 5), cy - 3 + bob, 6, 6);
                // Ears
                g.FillPolygon(b, new[] {
                    new PointF(faceX + (dir > 0 ? 0 : -1), cy - 3 + bob),
                    new PointF(faceX + dir * 1, cy - 6 + bob),
                    new PointF(faceX + dir * 3, cy - 2 + bob)
                });
                g.FillPolygon(b, new[] {
                    new PointF(faceX + dir * 3, cy - 3 + bob),
                    new PointF(faceX + dir * 5, cy - 6 + bob),
                    new PointF(faceX + dir * 6, cy - 1 + bob)
                });
            }
            // Eyes
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
            {
                g.FillEllipse(b, faceX + dir * 1, cy - 1 + bob, 2, 2);
                g.FillEllipse(b, faceX + dir * 3, cy - 1 + bob, 2, 2);
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
            int a = (int)(220 * fade);
            float angle = ev.Param1;

            // Subtle glow around astronaut
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.06f), 200, 220, 255)))
                g.FillEllipse(b, cx - 18, cy - 18, 36, 36);

            // Apply tumble rotation
            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle % 360);

            // Helmet (circle) — bright white
            using (var b = new SolidBrush(Color.FromArgb(a, 235, 235, 245)))
                g.FillEllipse(b, -6, -10, 12, 11);
            // Visor — GOLD like real astronauts
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 180, 60)))
                g.FillEllipse(b, -4, -8, 8, 7);
            // Visor reflection
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f), 255, 240, 150)))
                g.FillEllipse(b, -2, -7, 3, 2);

            // Body (rounded rect) — brighter
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 240, 245)))
                g.FillRectangle(b, -5, 1, 10, 9);

            // Backpack — visible
            using (var b = new SolidBrush(Color.FromArgb(a, 180, 180, 195)))
                g.FillRectangle(b, -7, 2, 3, 7);

            // Arms — thicker
            using (var p = new Pen(Color.FromArgb(a, 235, 235, 245), 2.5f))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                float armWave = (float)Math.Sin(angle * 0.05f) * 15;
                g.DrawLine(p, -5, 3, -9, 1 + armWave * 0.3f);
                g.DrawLine(p, 5, 3, 9, 5 - armWave * 0.3f);
            }

            // Legs — thicker
            using (var p = new Pen(Color.FromArgb(a, 235, 235, 245), 2.5f))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, -2, 10, -4, 15);
                g.DrawLine(p, 2, 10, 4, 15);
            }

            // Boots
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 200, 210))) {
                g.FillEllipse(b, -5.5f, 14, 3, 2);
                g.FillEllipse(b, 2.5f, 14, 3, 2);
            }

            g.Restore(state);

            // Tether line trailing behind
            int tetherA = (int)(80 * fade);
            float tetherEndX = cx - 40 - (float)Math.Sin(t * 10) * 10;
            float tetherEndY = cy + 20 + (float)Math.Cos(t * 8) * 8;
            using (var p = new Pen(Color.FromArgb(tetherA, 200, 200, 210), 0.8f))
                g.DrawBezier(p, cx, cy, cx - 15, cy + 5, cx - 25, cy + 15, tetherEndX, tetherEndY);
        }

        static void PaintEclipse(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
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
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            float orbitR = ev.Param2;
            float angle = ev.Param1 * (float)(Math.PI / 180);
            int a = (int)(210 * fade);

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
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(200 * fade);
            float dir = ev.DirX > 0 ? 1 : -1;

            // Rocket body (pointing in travel direction)
            // Nose cone
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 60, 60)))
                g.FillPolygon(b, new[] {
                    new PointF(cx + dir * 10, cy),
                    new PointF(cx + dir * 3, cy - 4),
                    new PointF(cx + dir * 3, cy + 4)
                });
            // Body
            using (var b = new SolidBrush(Color.FromArgb(a, 230, 230, 235)))
                g.FillRectangle(b, dir > 0 ? cx - 9 : cx + 3, cy - 4, 12, 8);
            // Window
            using (var b = new SolidBrush(Color.FromArgb(a, 80, 180, 230)))
                g.FillEllipse(b, cx + dir * 1 - 2, cy - 2, 4, 4);
            using (var b = new SolidBrush(Color.FromArgb(a / 3, 255, 255, 255)))
                g.FillEllipse(b, cx + dir * 1 - 1, cy - 1.5f, 1.5f, 2);
            // Fins
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 50, 50)))
            {
                g.FillPolygon(b, new[] {
                    new PointF(cx - dir * 6, cy - 4), new PointF(cx - dir * 11, cy - 8), new PointF(cx - dir * 9, cy - 4)
                });
                g.FillPolygon(b, new[] {
                    new PointF(cx - dir * 6, cy + 4), new PointF(cx - dir * 11, cy + 8), new PointF(cx - dir * 9, cy + 4)
                });
            }

            // Exhaust flame — flickering, behind the rocket
            float flicker = (float)Math.Sin(ev.Param1 * 8) * 0.3f + 0.7f;
            float flameLen = 12 + flicker * 8;
            int fA = (int)(150 * fade * flicker);
            // Outer flame (orange)
            using (var b = new SolidBrush(Color.FromArgb(fA, 255, 150, 30)))
                g.FillPolygon(b, new[] {
                    new PointF(cx - dir * 9, cy - 3),
                    new PointF(cx - dir * 9, cy + 3),
                    new PointF(cx - dir * (9 + flameLen), cy + (float)Math.Sin(ev.Param1 * 12) * 2)
                });
            // Inner flame (white-yellow)
            using (var b = new SolidBrush(Color.FromArgb(fA, 255, 240, 150)))
                g.FillPolygon(b, new[] {
                    new PointF(cx - dir * 9, cy - 1.5f),
                    new PointF(cx - dir * 9, cy + 1.5f),
                    new PointF(cx - dir * (9 + flameLen * 0.6f), cy + (float)Math.Sin(ev.Param1 * 15) * 1)
                });

            // Smoke trail particles
            var rng = new Random(ev.Seed + (int)(t * 10));
            for (int i = 0; i < 6; i++)
            {
                float age = (float)rng.NextDouble();
                float sx = cx - dir * (15 + age * 40) + (float)(rng.NextDouble() - 0.5) * 8;
                float sy = cy + (float)(rng.NextDouble() - 0.5) * 6;
                float sr = 2 + age * 4;
                int sa = (int)(40 * (1f - age) * fade);
                using (var b = new SolidBrush(Color.FromArgb(sa, 180, 180, 190)))
                    g.FillEllipse(b, sx - sr, sy - sr, sr * 2, sr * 2);
            }
        }

        static void PaintSatellite(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(210 * fade);
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
            float panelW = 12, panelH = 6;
            float wobble = (float)Math.Sin(panelAngle * 0.05f) * 2;

            // Left panel — brighter blue
            using (var b = new SolidBrush(Color.FromArgb(a, 90, 120, 200)))
                g.FillRectangle(b, cx - 3 - panelW, cy - panelH / 2 + wobble, panelW, panelH);
            // Panel grid lines
            using (var p = new Pen(Color.FromArgb(a / 2, 100, 120, 180), 0.5f))
            {
                for (int i = 1; i < 3; i++)
                    g.DrawLine(p, cx - 3 - panelW + i * panelW / 3, cy - panelH / 2 + wobble,
                        cx - 3 - panelW + i * panelW / 3, cy + panelH / 2 + wobble);
            }

            // Right panel — brighter blue
            using (var b = new SolidBrush(Color.FromArgb(a, 90, 120, 200)))
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
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(210 * fade);
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
            float bob = (float)Math.Sin(t * 8) * 3;
            baseY += bob;
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
            float angle = (float)Math.Atan2(-ev.DirY, -ev.DirX);

            float bodyFade;
            if (t < 0.06f) bodyFade = t / 0.06f;
            else if (t > 0.92f) bodyFade = (1f - t) / 0.08f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            float tailLen = ev.Param1 * Math.Max(w, h) * 0.1f;

            // === DUST TAIL — wider, diffuse, curved slightly ===
            var rng = new Random(ev.Seed);
            for (int d = 0; d < 20; d++) {
                float dT = d / 20f;
                float dist = dT * tailLen * 1.2f;
                float spread = dT * 12;
                float curveOff = dT * dT * 8;
                float dx = cx + (float)Math.Cos(angle + 0.15f) * dist + (float)(rng.NextDouble() - 0.5) * spread;
                float dy = cy + (float)Math.Sin(angle + 0.15f) * dist + (float)(rng.NextDouble() - 0.5) * spread + curveOff;
                float dFade = (1f - dT) * bodyFade;
                int da = (int)(a * 0.1f * dFade);
                if (da > 0) {
                    float dsz = 3 + dT * 4;
                    using (var b = new SolidBrush(Color.FromArgb(da, 200, 180, 140)))
                        g.FillEllipse(b, dx - dsz, dy - dsz, dsz * 2, dsz * 2);
                }
            }

            // === ION TAIL — narrow, bright blue, straight ===
            for (int i = 0; i < 18; i++) {
                float iT = i / 18f;
                float dist = iT * tailLen;
                float tx = cx + (float)Math.Cos(angle) * dist;
                float ty = cy + (float)Math.Sin(angle) * dist;
                float iFade = (1f - iT) * bodyFade;
                int ia = (int)(a * 0.35f * iFade);
                if (ia <= 0) continue;
                float sz = 4 - iT * 2.5f;
                using (var b = new SolidBrush(Color.FromArgb(ia, 140, 200, 255)))
                    g.FillEllipse(b, tx - sz, ty - sz, sz * 2, sz * 2);
                // Bright center streak
                float csz = sz * 0.4f;
                using (var b = new SolidBrush(Color.FromArgb((int)(ia * 0.6f), 200, 230, 255)))
                    g.FillEllipse(b, tx - csz, ty - csz, csz * 2, csz * 2);
            }

            // === COMA — fuzzy envelope around nucleus ===
            for (int gl = 4; gl >= 1; gl--) {
                float gr = 4 + gl * 4;
                int ga = (int)(a * 0.08f * bodyFade / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 180, 220, 255)))
                        g.FillEllipse(b, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            // === NUCLEUS — bright core with hot center ===
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.8f * bodyFade), 200, 230, 255)))
                g.FillEllipse(b, cx - 4, cy - 4, 8, 8);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * bodyFade), 240, 250, 255)))
                g.FillEllipse(b, cx - 2.5f, cy - 2.5f, 5, 5);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.4f * bodyFade), 255, 255, 255)))
                g.FillEllipse(b, cx - 1.2f, cy - 1.2f, 2.4f, 2.4f);

            // === JETS — gas jets from nucleus ===
            for (int jet = 0; jet < 3; jet++) {
                float jetAngle = angle + (float)Math.PI + (jet - 1) * 0.4f + (float)Math.Sin(t * 20 + jet) * 0.2f;
                float jetLen = 6 + (float)Math.Sin(t * 30 + jet * 2) * 3;
                float jx = cx + (float)Math.Cos(jetAngle) * jetLen;
                float jy = cy + (float)Math.Sin(jetAngle) * jetLen;
                int ja = (int)(a * 0.25f * bodyFade);
                using (var p = new Pen(Color.FromArgb(ja, 160, 210, 255), 0.8f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx, cy, jx, jy);
                }
            }
        }

        static void PaintSpaceWhale(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 210);
            float wave = (float)Math.Sin(t * 14) * 6;
            cy += wave;
            float dir = ev.DirX > 0 ? 1 : -1;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            // === BIOLUMINESCENT AURA ===
            for (int gl = 3; gl >= 1; gl--) {
                float gr = 20 + gl * 10;
                float glPulse = 0.3f + 0.4f * (float)Math.Sin(t * 6 + gl);
                int ga = (int)(a * 0.05f * glPulse * bodyFade / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 100, 150, 220)))
                        g.FillEllipse(b, cx - gr, cy - gr * 0.5f, gr * 2, gr);
            }

            // === BODY — massive, streamlined — BRIGHTENED ===
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.85f * bodyFade), 80, 110, 170)))
                g.FillEllipse(b, cx - 22 * dir, cy - 7, 44, 14);
            // Underbelly lighter
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * bodyFade), 130, 170, 230)))
                g.FillEllipse(b, cx - 16 * dir, cy + 1, 32, 6);

            // Ventral grooves
            for (int groove = 0; groove < 6; groove++) {
                float gx = cx - 10 * dir + groove * 4 * dir;
                int ga2 = (int)(a * 0.25f * bodyFade);
                using (var p = new Pen(Color.FromArgb(ga2, 90, 130, 190), 0.5f))
                    g.DrawLine(p, gx, cy + 2, gx - dir, cy + 5);
            }

            // === PECTORAL FINS — graceful, long ===
            for (int fin = -1; fin <= 1; fin += 2) {
                float finWave = (float)Math.Sin(t * 10 + fin * 0.5f) * 4;
                float finTipX = cx - 5 * dir + fin * 2;
                float finTipY = cy + fin * (8 + finWave);
                PointF[] finShape = {
                    new PointF(cx - 3 * dir, cy + fin * 3),
                    new PointF(finTipX - 8 * dir, finTipY),
                    new PointF(finTipX - 12 * dir, finTipY - fin * 2)
                };
                int fa = (int)(a * 0.65f * bodyFade);
                using (var b = new SolidBrush(Color.FromArgb(fa, 75, 110, 170)))
                    g.FillPolygon(b, finShape);
            }

            // === HEAD ===
            float headX = cx + 16 * dir;
            // Eye
            float eyePulse = 0.5f + 0.5f * (float)Math.Sin(t * 8);
            float eyeX = headX + 3 * dir, eyeY = cy - 2;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f * eyePulse * bodyFade), 150, 200, 255)))
                g.FillEllipse(b, eyeX - 3, eyeY - 3, 6, 6);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.8f * bodyFade), 180, 220, 255)))
                g.FillEllipse(b, eyeX - 1.5f, eyeY - 1.5f, 3, 3);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * bodyFade), 20, 30, 50)))
                g.FillEllipse(b, eyeX - 0.8f, eyeY - 0.8f, 1.6f, 1.6f);

            // Mouth line
            using (var p = new Pen(Color.FromArgb((int)(a * 0.3f * bodyFade), 40, 60, 100), 0.5f))
                g.DrawArc(p, headX - 2, cy, 8 * dir, 4, dir > 0 ? 10 : 170, 60);

            // === TAIL FLUKE — powerful, curved ===
            float tailX = cx - 22 * dir;
            float flukeWave = (float)Math.Sin(t * 18) * 5;
            PointF[] topFluke = {
                new PointF(tailX, cy),
                new PointF(tailX - 4 * dir, cy - 3 + flukeWave),
                new PointF(tailX - 10 * dir, cy - 6 + flukeWave * 1.5f),
                new PointF(tailX - 6 * dir, cy - 1 + flukeWave * 0.5f)
            };
            PointF[] botFluke = {
                new PointF(tailX, cy),
                new PointF(tailX - 4 * dir, cy + 3 + flukeWave),
                new PointF(tailX - 10 * dir, cy + 6 + flukeWave * 1.5f),
                new PointF(tailX - 6 * dir, cy + 1 + flukeWave * 0.5f)
            };
            int flukeA = (int)(a * 0.75f * bodyFade);
            using (var b = new SolidBrush(Color.FromArgb(flukeA, 70, 100, 160)))
                g.FillPolygon(b, topFluke);
            using (var b = new SolidBrush(Color.FromArgb(flukeA, 70, 100, 160)))
                g.FillPolygon(b, botFluke);

            // === BIOLUMINESCENT SPOTS — along body ===
            var spotRng = new Random(ev.Seed + 100);
            for (int spot = 0; spot < 8; spot++) {
                float sx = cx + (float)(spotRng.NextDouble() - 0.5) * 36 * dir;
                float sy = cy + (float)(spotRng.NextDouble() - 0.3) * 8;
                float spotPulse = (float)Math.Max(0, Math.Sin(t * 12 + spot * 2.3f));
                if (spotPulse < 0.3f) continue;
                int sa = (int)(a * 0.35f * spotPulse * bodyFade);
                using (var b = new SolidBrush(Color.FromArgb(sa, 120, 180, 255)))
                    g.FillEllipse(b, sx - 1.5f, sy - 1.5f, 3, 3);
                using (var b = new SolidBrush(Color.FromArgb(sa / 3, 120, 180, 255)))
                    g.FillEllipse(b, sx - 3, sy - 3, 6, 6);
            }

            // === SONG BUBBLES — ethereal sound waves ===
            for (int bub = 0; bub < 5; bub++) {
                float bubLife = (t * 2 + bub * 0.18f) % 1.0f;
                float bubX = headX + dir * (5 + bubLife * 15) + (float)Math.Sin(t * 10 + bub) * 4;
                float bubY = cy - 4 - bubLife * 12;
                float bubR = 1.5f + bubLife * 2;
                float bubFade = bubLife < 0.1f ? bubLife / 0.1f : (1f - bubLife) / 0.9f;
                int ba = (int)(a * 0.2f * bubFade * bodyFade);
                if (ba > 0) {
                    using (var p = new Pen(Color.FromArgb(ba, 150, 200, 255), 0.4f))
                        g.DrawEllipse(p, bubX - bubR, bubY - bubR, bubR * 2, bubR * 2);
                }
            }

            // === SPARKLE WAKE ===
            var wakeRng = new Random(ev.Seed);
            for (int sp = 0; sp < 8; sp++) {
                float spLife = (t * 3 + sp * 0.1f) % 1.0f;
                float spx = cx - dir * (20 + spLife * 30) + (float)(wakeRng.NextDouble() - 0.5) * 12;
                float spy = cy + (float)(wakeRng.NextDouble() - 0.5) * 10;
                float spFade = (1f - spLife) * bodyFade;
                int spa = (int)(a * 0.2f * spFade);
                if (spa > 0)
                    using (var b = new SolidBrush(Color.FromArgb(spa, 130, 180, 255)))
                        g.FillEllipse(b, spx - 1, spy - 1, 2, 2);
            }
        }

        static void PaintFirefly(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            int a = (int)(fade * 210);
            int count = (int)ev.Param1;
            var rng = new Random(ev.Seed);
            float baseCx = ev.X * w, baseCy = ev.Y * h;

            float op;
            if (t < 0.15f) op = t / 0.15f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));

            // === AMBIENT GLOW CLOUD ===
            int cloudA = (int)(a * 0.02f * op);
            if (cloudA > 0)
                using (var b = new SolidBrush(Color.FromArgb(cloudA, 180, 255, 100)))
                    g.FillEllipse(b, baseCx - 50, baseCy - 35, 100, 70);

            for (int i = 0; i < count; i++) {
                float offX = rng.Next(-45, 45), offY = rng.Next(-35, 35);
                float driftX = (float)Math.Sin(t * 6 + i * 1.7) * 18;
                float driftY = (float)Math.Cos(t * 5 + i * 2.3) * 12;
                float fx = baseCx + offX + driftX;
                float fy = baseCy + offY + driftY;

                // Each firefly has its own blink pattern
                float blinkPhase = (t * 8 + i * 2.1f) % 1.0f;
                float pulse;
                if (blinkPhase < 0.15f) pulse = blinkPhase / 0.15f;
                else if (blinkPhase < 0.35f) pulse = 1f;
                else if (blinkPhase < 0.5f) pulse = (0.5f - blinkPhase) / 0.15f;
                else pulse = 0.05f;

                int fa = (int)(a * pulse * op);
                if (fa <= 2) continue;

                // Glow halo
                using (var b = new SolidBrush(Color.FromArgb(fa / 5, 150, 255, 80)))
                    g.FillEllipse(b, fx - 6, fy - 6, 12, 12);
                // Medium glow
                using (var b = new SolidBrush(Color.FromArgb(fa / 3, 180, 255, 100)))
                    g.FillEllipse(b, fx - 3.5f, fy - 3.5f, 7, 7);
                // Bright core
                using (var b = new SolidBrush(Color.FromArgb(fa, 220, 255, 150)))
                    g.FillEllipse(b, fx - 1.5f, fy - 1.5f, 3, 3);
                // Hot center
                if (pulse > 0.5f)
                    using (var b = new SolidBrush(Color.FromArgb((int)(fa * 0.5f), 255, 255, 220)))
                        g.FillEllipse(b, fx - 0.8f, fy - 0.8f, 1.6f, 1.6f);

                // Tiny body silhouette when lit
                if (pulse > 0.3f) {
                    int bodyA = (int)(a * 0.2f * pulse * op);
                    using (var b = new SolidBrush(Color.FromArgb(bodyA, 40, 50, 30)))
                        g.FillEllipse(b, fx - 0.8f, fy - 1.5f, 1.6f, 3);
                    // Wings
                    float wingFlap = (float)Math.Sin(t * 60 + i * 5) * 1.5f;
                    using (var p = new Pen(Color.FromArgb(bodyA / 2, 100, 120, 80), 0.3f)) {
                        g.DrawLine(p, fx, fy - 0.5f, fx - 2 - wingFlap, fy - 1.5f);
                        g.DrawLine(p, fx, fy - 0.5f, fx + 2 + wingFlap, fy - 1.5f);
                    }
                }

                // Trail motes
                if (pulse > 0.2f) {
                    for (int tr = 1; tr <= 3; tr++) {
                        float trX = fx - driftX * tr * 0.1f;
                        float trY = fy - driftY * tr * 0.1f;
                        int tra = (int)(fa * 0.15f * (1f - tr * 0.25f));
                        if (tra > 0)
                            using (var b = new SolidBrush(Color.FromArgb(tra, 180, 255, 100)))
                                g.FillEllipse(b, trX - 0.8f, trY - 0.8f, 1.6f, 1.6f);
                    }
                }
            }
        }

        static void PaintPulsar(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 210);
            float beamAngle = ev.Param1 + t * 400;
            float rad = (float)(beamAngle * Math.PI / 180);

            float op;
            if (t < 0.12f) op = t / 0.12f;
            else if (t > 0.88f) op = (1f - t) / 0.12f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));

            float beamLen = Math.Min(w, h) * 0.3f;
            float beamPulse = 0.7f + 0.3f * (float)Math.Sin(t * 60);

            // === MAGNETOSPHERE — faint toroidal field ===
            for (int ring = 0; ring < 3; ring++) {
                float ringR = 15 + ring * 8;
                float ringRot = t * (50 + ring * 20);
                int ringA = (int)(a * 0.05f * op / (ring + 1));
                if (ringA > 0)
                    using (var p = new Pen(Color.FromArgb(ringA, 120, 160, 255), 0.5f))
                        g.DrawArc(p, cx - ringR, cy - ringR * 0.4f, ringR * 2, ringR * 0.8f, ringRot, 120);
            }

            // === BEAMS — twin relativistic jets ===
            for (int side = 0; side < 2; side++) {
                float sign = side == 0 ? 1 : -1;
                float bx = cx + (float)Math.Cos(rad) * beamLen * sign;
                float by = cy + (float)Math.Sin(rad) * beamLen * sign;

                // Wide beam glow
                int wideA = (int)(a * 0.15f * beamPulse * op);
                using (var p = new Pen(Color.FromArgb(wideA, 100, 150, 255), 6f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx, cy, bx, by);
                }
                // Medium beam
                using (var p = new Pen(Color.FromArgb((int)(a * 0.35f * beamPulse * op), 150, 190, 255), 2.5f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx, cy, bx, by);
                }
                // Core beam
                using (var p = new Pen(Color.FromArgb((int)(a * 0.6f * beamPulse * op), 220, 235, 255), 1.0f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx, cy, bx, by);
                }

                // Beam particles
                for (int bp = 0; bp < 5; bp++) {
                    float bpDist = ((t * 8 + bp * 0.2f) % 1.0f) * beamLen;
                    float bpx = cx + (float)Math.Cos(rad) * bpDist * sign;
                    float bpy = cy + (float)Math.Sin(rad) * bpDist * sign;
                    float bpFade = 1f - bpDist / beamLen;
                    int bpa = (int)(a * 0.4f * bpFade * op);
                    if (bpa > 0)
                        using (var b = new SolidBrush(Color.FromArgb(bpa, 200, 225, 255)))
                            g.FillEllipse(b, bpx - 1.5f, bpy - 1.5f, 3, 3);
                }
            }

            // === CORE — ultra-dense neutron star ===
            // Outer glow
            for (int gl = 3; gl >= 1; gl--) {
                float gr = 4 + gl * 4;
                float glPulse = 0.5f + 0.5f * (float)Math.Sin(t * 30 + gl);
                int ga = (int)(a * 0.1f * glPulse * op / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 160, 200, 255)))
                        g.FillEllipse(b, cx - gr, cy - gr, gr * 2, gr * 2);
            }
            // Core surface
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * op), 220, 235, 255)))
                g.FillEllipse(b, cx - 3.5f, cy - 3.5f, 7, 7);
            // Hot spots
            float spotAngle = t * 200;
            for (int hs = 0; hs < 2; hs++) {
                float hsA2 = (float)((spotAngle + hs * 180) * Math.PI / 180);
                float hsx = cx + (float)Math.Cos(hsA2) * 2;
                float hsy = cy + (float)Math.Sin(hsA2) * 2;
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.7f * op), 255, 255, 255)))
                    g.FillEllipse(b, hsx - 1, hsy - 1, 2, 2);
            }

            // === SWEEP FLASH — when beam crosses "observer" ===
            float sweepAngle = (beamAngle % 360) / 360f;
            if (sweepAngle > 0.48f && sweepAngle < 0.52f) {
                float flashT = 1f - Math.Abs(sweepAngle - 0.5f) / 0.02f;
                float flashR = 10 + flashT * 15;
                int flashA = (int)(a * 0.25f * flashT * op);
                using (var b = new SolidBrush(Color.FromArgb(flashA, 200, 225, 255)))
                    g.FillEllipse(b, cx - flashR, cy - flashR, flashR * 2, flashR * 2);
            }
        }

        static void PaintNebula(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 210);
            int scheme = (int)ev.Param1 % 3;
            var rng = new Random(ev.Seed);

            float op;
            if (t < 0.15f) op = t / 0.15f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));

            // Color schemes: purple/pink, blue/teal, red/gold
            int[][] colors = {
                new[] {120,50,180, 200,80,160, 160,100,220},
                new[] {50,120,180, 80,180,200, 100,160,220},
                new[] {180,60,60, 200,120,50, 220,100,80}
            };
            int[] sc = colors[scheme];

            // === LARGE CLOUD LAYERS — overlapping translucent blobs ===
            for (int layer = 0; layer < 3; layer++) {
                for (int blob = 0; blob < 6; blob++) {
                    float ox = (float)(rng.NextDouble() - 0.5) * 60 + (float)Math.Sin(t * 2 + blob + layer) * 6;
                    float oy = (float)(rng.NextDouble() - 0.5) * 45 + (float)Math.Cos(t * 1.5 + blob * 1.3 + layer) * 5;
                    float sz = 18 + (float)rng.NextDouble() * 22;
                    float breathe = 1f + 0.1f * (float)Math.Sin(t * 3 + blob * 0.7f + layer);
                    sz *= breathe;
                    int ci = layer * 3;
                    int ba = (int)(a * 0.2f * op * (1f - layer * 0.15f));
                    if (ba > 0)
                        using (var b = new SolidBrush(Color.FromArgb(ba, sc[ci % 9], sc[(ci + 1) % 9], sc[(ci + 2) % 9])))
                            g.FillEllipse(b, cx + ox - sz / 2, cy + oy - sz / 2, sz, sz * 0.7f);
                }
            }

            // === BRIGHT KNOTS — dense concentrations ===
            var knotRng = new Random(ev.Seed + 50);
            for (int knot = 0; knot < 5; knot++) {
                float kx = cx + (float)(knotRng.NextDouble() - 0.5) * 40 + (float)Math.Sin(t * 4 + knot * 1.8f) * 3;
                float ky = cy + (float)(knotRng.NextDouble() - 0.5) * 30 + (float)Math.Cos(t * 3 + knot * 2.1f) * 2;
                float kPulse = 0.4f + 0.6f * (float)Math.Max(0, Math.Sin(t * 6 + knot * 2.5f));
                float ksz = 5 + (float)knotRng.NextDouble() * 8;
                int ka = (int)(a * 0.25f * kPulse * op);
                if (ka > 0) {
                    using (var b = new SolidBrush(Color.FromArgb(ka, sc[3], sc[4], sc[5])))
                        g.FillEllipse(b, kx - ksz, ky - ksz * 0.6f, ksz * 2, ksz * 1.2f);
                    // Bright core
                    float coreR = ksz * 0.3f;
                    using (var b = new SolidBrush(Color.FromArgb((int)(ka * 0.7f), 255, 240, 240)))
                        g.FillEllipse(b, kx - coreR, ky - coreR, coreR * 2, coreR * 2);
                }
            }

            // === EMBEDDED STARS — young stars forming in the gas ===
            var starRng = new Random(ev.Seed + 100);
            for (int star = 0; star < 8; star++) {
                float sx = cx + (float)(starRng.NextDouble() - 0.5) * 50;
                float sy = cy + (float)(starRng.NextDouble() - 0.5) * 35;
                float sPulse = 0.3f + 0.7f * (float)Math.Max(0, Math.Sin(t * 15 + star * 2.7f));
                int sa = (int)(a * 0.5f * sPulse * op);
                if (sa > 0) {
                    using (var b = new SolidBrush(Color.FromArgb(sa / 3, 255, 255, 220)))
                        g.FillEllipse(b, sx - 2, sy - 2, 4, 4);
                    using (var b = new SolidBrush(Color.FromArgb(sa, 255, 255, 240)))
                        g.FillEllipse(b, sx - 0.8f, sy - 0.8f, 1.6f, 1.6f);
                }
            }

            // === DARK LANES — dust absorption ===
            var dustRng = new Random(ev.Seed + 200);
            for (int dl = 0; dl < 3; dl++) {
                float dlx = cx + (float)(dustRng.NextDouble() - 0.5) * 35;
                float dly = cy + (float)(dustRng.NextDouble() - 0.5) * 25;
                float dlsz = 10 + (float)dustRng.NextDouble() * 12;
                dustRng.NextDouble(); // consume for determinism
                int dla = (int)(a * 0.12f * op);
                if (dla > 0)
                    using (var b = new SolidBrush(Color.FromArgb(dla, 5, 5, 10)))
                        g.FillEllipse(b, dlx - dlsz, dly - dlsz * 0.3f, dlsz * 2, dlsz * 0.6f);
            }
        }

        static void PaintSpaceJellyfish(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            float pulse = (float)Math.Sin(t * 22);
            float bellR = 10 + pulse * 2;
            float bob = (float)Math.Sin(t * 12) * 2;
            cy += bob;
            int a = (int)(fade * 210);

            // Ambient glow
            for (int gl = 3; gl >= 1; gl--) {
                float gr = bellR + gl * 7;
                int ga = (int)(a * 0.04f * bodyFade / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 180, 100, 220)))
                        g.FillEllipse(b, cx - gr, cy - gr * 0.7f, gr * 2, gr * 1.4f);
            }

            // Bell
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.4f * bodyFade), 180, 100, 220)))
                g.FillEllipse(b, cx - bellR, cy - bellR * 0.6f, bellR * 2, bellR * 1.1f);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.2f * bodyFade), 220, 170, 255)))
                g.FillEllipse(b, cx - bellR * 0.6f, cy - bellR * 0.35f, bellR * 1.2f, bellR * 0.6f);
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f * bodyFade), 200, 140, 255), 1.2f))
                g.DrawArc(p, cx - bellR, cy - bellR * 0.4f, bellR * 2, bellR * 1.1f, 10, 160);

            // Internal radial pattern
            for (int rad = 0; rad < 6; rad++) {
                float rAngle = (float)(rad * Math.PI / 6 + Math.PI * 0.08);
                float rPulse = 0.3f + 0.5f * (float)Math.Max(0, Math.Sin(t * 25 + rad * 1.5f));
                int ra = (int)(a * 0.15f * rPulse * bodyFade);
                float rx = cx + (float)Math.Cos(rAngle) * bellR * 0.7f;
                float ry = cy + (float)Math.Sin(rAngle) * bellR * 0.4f;
                if (ra > 0)
                    using (var p = new Pen(Color.FromArgb(ra, 200, 160, 255), 0.4f))
                        g.DrawLine(p, cx, cy - bellR * 0.1f, rx, ry);
            }

            // Tentacles — 6 flowing tendrils
            float tentBase = cy + bellR * 0.4f;
            for (int ten = 0; ten < 6; ten++) {
                float tx = cx - 7 + ten * 2.8f;
                float tPhase = ten * 0.6f;
                for (int seg = 0; seg < 12; seg++) {
                    float sy = tentBase + seg * 2.8f;
                    float wave1 = (float)Math.Sin(t * 20 + tPhase + seg * 0.4f) * (2.5f + seg * 0.2f);
                    float wave2 = (float)Math.Sin(t * 14 + tPhase + seg * 0.3f) * 1.5f;
                    float sx = tx + wave1 + wave2;
                    float segFade = 1f - seg * 0.07f;
                    int sa = (int)(a * 0.35f * segFade * bodyFade);
                    if (sa <= 0) continue;
                    Color tc = ten % 2 == 0 ? Color.FromArgb(sa, 200, 120, 255) : Color.FromArgb(sa, 150, 100, 240);
                    using (var b = new SolidBrush(tc))
                        g.FillEllipse(b, sx - 0.8f, sy - 0.5f, 1.6f, 1f);
                    if (seg % 3 == 0) {
                        float nodePulse = (float)Math.Max(0, Math.Sin(t * 30 + ten + seg * 0.5f));
                        int na = (int)(sa * 0.5f * nodePulse);
                        if (na > 0)
                            using (var b = new SolidBrush(Color.FromArgb(na, 230, 200, 255)))
                                g.FillEllipse(b, sx - 1.2f, sy - 0.7f, 2.4f, 1.4f);
                    }
                }
            }
        }

        static void PaintMeteorShower(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            int a = (int)(fade * 200);
            var rng = new Random(ev.Seed);
            float cx = ev.X * w, cy = ev.Y * h;

            float op;
            if (t < 0.10f) op = t / 0.10f;
            else if (t > 0.88f) op = (1f - t) / 0.12f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));

            // Radiant point glow
            int radA = (int)(a * 0.05f * op);
            if (radA > 0)
                using (var b = new SolidBrush(Color.FromArgb(radA, 255, 230, 180)))
                    g.FillEllipse(b, cx - 20, cy - 15, 40, 30);

            for (int i = 0; i < 12; i++) {
                float delay = (float)rng.NextDouble() * 0.7f;
                float speed = 0.3f + (float)rng.NextDouble() * 0.3f;
                float mt = (t - delay) / speed;
                if (mt < 0 || mt > 1) continue;

                float angle = (float)(rng.NextDouble() * 0.6 + 0.5); // roughly same direction
                float startX = cx + (float)(rng.NextDouble() - 0.5) * 60;
                float startY = cy + (float)(rng.NextDouble() - 0.5) * 30;
                float len = 60 + (float)rng.NextDouble() * 80;
                float mx = startX + (float)Math.Cos(angle) * len * mt;
                float my = startY + (float)Math.Sin(angle) * len * mt;

                float mFade = mt < 0.1f ? mt / 0.1f : mt > 0.7f ? (1f - mt) / 0.3f : 1f;
                int ma = (int)(a * 0.7f * mFade * op);
                if (ma <= 0) continue;

                float thickness = 1.5f + (float)rng.NextDouble() * 1.0f;
                float trailLen = 10 + (float)rng.NextDouble() * 8;
                float trailX = mx - (float)Math.Cos(angle) * trailLen;
                float trailY = my - (float)Math.Sin(angle) * trailLen;

                // Trail glow
                using (var p = new Pen(Color.FromArgb(ma / 4, 255, 220, 150), thickness + 2)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, trailX, trailY, mx, my);
                }
                // Trail core
                using (var p = new Pen(Color.FromArgb(ma, 255, 240, 200), thickness * 0.6f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, trailX, trailY, mx, my);
                }
                // Head
                using (var b = new SolidBrush(Color.FromArgb(ma, 255, 250, 230)))
                    g.FillEllipse(b, mx - 1.5f, my - 1.5f, 3, 3);

                // Sparkle burst on some meteors
                if (mt > 0.5f && i % 3 == 0) {
                    float burstT = (mt - 0.5f) / 0.5f;
                    for (int sp = 0; sp < 4; sp++) {
                        float spAngle = (float)(sp * Math.PI / 2 + rng.NextDouble());
                        float spDist = burstT * 6;
                        float spx = mx + (float)Math.Cos(spAngle) * spDist;
                        float spy = my + (float)Math.Sin(spAngle) * spDist;
                        int spa = (int)(ma * 0.3f * (1f - burstT));
                        if (spa > 0)
                            using (var b = new SolidBrush(Color.FromArgb(spa, 255, 220, 150)))
                                g.FillEllipse(b, spx - 0.8f, spy - 0.8f, 1.6f, 1.6f);
                    }
                }
            }
        }

        static void PaintGhostShip(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float flicker = 0.7f + 0.3f * (float)Math.Sin(t * 12) * (float)Math.Sin(t * 19);
            int a = (int)(fade * 220 * Math.Max(0.5f, flicker));
            float dir = ev.DirX > 0 ? 1 : -1;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            // === SPECTRAL GLOW — visible ghostly aura ===
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.1f * bodyFade), 120, 180, 240)))
                g.FillEllipse(b, cx - 25, cy - 18, 50, 28);

            // === HULL — translucent but VISIBLE ===
            PointF[] hull = {
                new PointF(cx + 18 * dir, cy - 1), new PointF(cx + 14 * dir, cy + 5),
                new PointF(cx - 14 * dir, cy + 5), new PointF(cx - 18 * dir, cy + 1),
                new PointF(cx - 10 * dir, cy - 4), new PointF(cx + 10 * dir, cy - 4)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.45f * bodyFade), 100, 160, 210)))
                g.FillPolygon(b, hull);
            // Hull outline — glowing edges
            using (var p = new Pen(Color.FromArgb((int)(a * 0.6f * bodyFade), 140, 190, 230), 1f))
                g.DrawPolygon(p, hull);

            // === MASTS — tall, visible ===
            using (var p = new Pen(Color.FromArgb((int)(a * 0.7f * bodyFade), 140, 180, 220), 1.2f)) {
                g.DrawLine(p, cx, cy - 3, cx + dir * 1, cy - 22);
                g.DrawLine(p, cx - 7 * dir, cy - 2, cx - 6 * dir, cy - 16);
            }

            // === GHOSTLY SAILS — translucent but visible, animated billowing ===
            float sailWave = (float)Math.Sin(t * 10) * 4;
            // Main sail
            PointF[] mainSail = {
                new PointF(cx + dir * 1, cy - 20),
                new PointF(cx + dir * (10 + sailWave), cy - 14),
                new PointF(cx + dir * (9 + sailWave * 0.7f), cy - 5),
                new PointF(cx + dir * 1, cy - 4)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * bodyFade), 150, 200, 240)))
                g.FillPolygon(b, mainSail);
            // Sail glow edges
            using (var p = new Pen(Color.FromArgb((int)(a * 0.35f * bodyFade), 180, 220, 255), 0.6f)) {
                g.DrawLine(p, mainSail[0].X, mainSail[0].Y, mainSail[1].X, mainSail[1].Y);
                g.DrawLine(p, mainSail[2].X, mainSail[2].Y, mainSail[3].X, mainSail[3].Y);
            }
            // Fore sail — smaller
            PointF[] foreSail = {
                new PointF(cx - 6 * dir, cy - 15),
                new PointF(cx - 6 * dir + dir * (6 + sailWave * 0.6f), cy - 11),
                new PointF(cx - 6 * dir + dir * (5 + sailWave * 0.4f), cy - 4),
                new PointF(cx - 6 * dir, cy - 3)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.4f * bodyFade), 140, 190, 230)))
                g.FillPolygon(b, foreSail);

            // === GHOST LIGHTS — bright, pulsing orbs ===
            for (int light = 0; light < 4; light++) {
                float lx = cx - 10 * dir + light * 7 * dir;
                float ly = cy - 5 + (float)Math.Sin(t * 10 + light * 2) * 2;
                float lPulse = 0.4f + 0.6f * (float)Math.Max(0, Math.Sin(t * 18 + light * 2.5f));
                int la = (int)(a * 0.6f * lPulse * bodyFade);
                using (var b = new SolidBrush(Color.FromArgb(la / 3, 120, 210, 255)))
                    g.FillEllipse(b, lx - 4, ly - 4, 8, 8);
                using (var b = new SolidBrush(Color.FromArgb(la, 180, 230, 255)))
                    g.FillEllipse(b, lx - 1.5f, ly - 1.5f, 3, 3);
            }

            // === ECTOPLASM TRAIL — visible wispy trail ===
            for (int ecto = 0; ecto < 10; ecto++) {
                float eLife = (t * 3 + ecto * 0.09f) % 1.0f;
                float ex = cx - dir * (15 + eLife * 30) + (float)Math.Sin(t * 12 + ecto) * 5;
                float ey = cy + (float)Math.Sin(t * 8 + ecto * 1.5f) * 6;
                float eFade = (1f - eLife) * bodyFade;
                int ea = (int)(a * 0.2f * eFade);
                float esz = 2 + eLife * 5;
                if (ea > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ea, 100, 170, 220)))
                        g.FillEllipse(b, ex - esz, ey - esz * 0.5f, esz * 2, esz);
            }
        }

        static void PaintStarPhoenix(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 200);
            float dir = ev.DirX > 0 ? 1 : -1;

            float bodyFade;
            if (t < 0.06f) bodyFade = t / 0.06f;
            else if (t > 0.92f) bodyFade = (1f - t) / 0.08f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            float wingPhase = (float)Math.Sin(t * 16) * 10;

            // === FIRE TRAIL — cascading embers ===
            var rng = new Random(ev.Seed + (int)(t * 15));
            for (int tr = 0; tr < 15; tr++) {
                float trLife = (t * 4 + tr * 0.06f) % 1.0f;
                float trx = cx - dir * (8 + trLife * 40) + (float)(rng.NextDouble() - 0.5) * 10;
                float trY2 = cy + (float)(rng.NextDouble() - 0.5) * 8 + trLife * 5;
                float trFade = (1f - trLife) * bodyFade;
                int tra = (int)(a * 0.2f * trFade);
                if (tra <= 0) continue;
                float trsz = 1.5f + (1f - trLife) * 2;
                Color trc = trLife < 0.3f ? Color.FromArgb(tra, 255, 240, 150) :
                    trLife < 0.6f ? Color.FromArgb(tra, 255, 160, 50) : Color.FromArgb(tra, 200, 60, 20);
                using (var b = new SolidBrush(trc))
                    g.FillEllipse(b, trx - trsz, trY2 - trsz, trsz * 2, trsz * 2);
            }

            // === WINGS — fiery, sweeping arcs ===
            for (int side = -1; side <= 1; side += 2) {
                float wingY = wingPhase * side;
                // Wing arc — multiple feather strokes
                for (int feather = 0; feather < 6; feather++) {
                    float fT = feather / 6f;
                    float fLen = 12 + feather * 3;
                    float fAngle = (float)(side * (-0.5 - fT * 0.3) + Math.Sin(t * 16 + feather * 0.3f) * 0.15);
                    float fx1 = cx - dir * feather * 1.5f;
                    float fy1 = cy + side * feather * 0.5f;
                    float fx2 = fx1 - dir * fLen * 0.5f + (float)Math.Cos(fAngle) * fLen * 0.3f;
                    float fy2 = fy1 + (float)Math.Sin(fAngle) * fLen + wingY * fT;
                    int fa = (int)(a * (0.6f - feather * 0.05f) * bodyFade);
                    int fr = 255, fg = (int)(180 - feather * 20), fb = (int)(30 + feather * 5);
                    fg = Math.Max(0, fg); fb = Math.Min(255, fb);
                    using (var p = new Pen(Color.FromArgb(fa, fr, fg, fb), 2.0f - feather * 0.15f)) {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        g.DrawLine(p, fx1, fy1, fx2, fy2);
                    }
                    // Feather tip ember
                    using (var b = new SolidBrush(Color.FromArgb(fa / 3, 255, 200, 80)))
                        g.FillEllipse(b, fx2 - 1.5f, fy2 - 1.5f, 3, 3);
                }
            }

            // === BODY — radiant core ===
            // Body glow
            for (int gl = 3; gl >= 1; gl--) {
                float gr = 4 + gl * 4;
                int ga = (int)(a * 0.08f * bodyFade / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 255, 160, 40)))
                        g.FillEllipse(b, cx - gr, cy - gr, gr * 2, gr * 2);
            }
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * bodyFade), 255, 210, 80)))
                g.FillEllipse(b, cx - 3.5f, cy - 3.5f, 7, 7);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * bodyFade), 255, 255, 200)))
                g.FillEllipse(b, cx - 1.5f, cy - 1.5f, 3, 3);

            // === HEAD — beak and eye ===
            float headX = cx + dir * 5;
            // Beak
            using (var p = new Pen(Color.FromArgb((int)(a * 0.7f * bodyFade), 255, 200, 60), 1.0f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, headX, cy, headX + dir * 4, cy + 0.5f);
            }
            // Eye
            float eyePulse = 0.6f + 0.4f * (float)Math.Sin(t * 30);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * eyePulse * bodyFade), 255, 255, 200)))
                g.FillEllipse(b, headX - 0.8f, cy - 1.5f, 1.6f, 1.6f);

            // === TAIL PLUMES — long flowing fire feathers ===
            for (int plume = 0; plume < 5; plume++) {
                float pWave = (float)Math.Sin(t * 20 + plume * 1.2f) * (3 + plume);
                float pLen = 12 + plume * 3;
                float px = cx - dir * (5 + plume * 2);
                float py = cy + pWave;
                float ptx = px - dir * pLen;
                float pty = py + pWave * 0.5f;
                int pa = (int)(a * (0.4f - plume * 0.05f) * bodyFade);
                using (var p = new Pen(Color.FromArgb(pa, 255, 120 + plume * 15, 20), 1.2f - plume * 0.1f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawBezier(p, px, py, px - dir * pLen * 0.3f, py + pWave * 0.3f,
                        ptx + dir * pLen * 0.2f, pty, ptx, pty);
                }
            }
        }

        static void PaintBlackhole(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 210);

            float op;
            if (t < 0.15f) op = t / 0.15f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));

            // === GRAVITATIONAL LENSING — distorted ring ===
            for (int lens = 3; lens >= 1; lens--) {
                float lr = 20 + lens * 6;
                int la = (int)(a * 0.03f * op / lens);
                if (la > 0)
                    using (var p = new Pen(Color.FromArgb(la, 200, 140, 255), 0.5f))
                        g.DrawEllipse(p, cx - lr, cy - lr * 0.45f, lr * 2, lr * 0.9f);
            }

            // === ACCRETION DISK — multi-layered spinning rings ===
            var rng = new Random(ev.Seed);
            for (int ring = 6; ring >= 0; ring--) {
                float r = 8 + ring * 4.5f;
                float rot = t * (250 - ring * 35);
                float thickness = 1.8f - ring * 0.15f;
                int ra = (int)(a * (0.6f - ring * 0.06f) * op);
                if (ra <= 0) continue;

                // Doppler shift — one side brighter (approaching) one dimmer (receding)
                float dopplerAngle = (float)(rot * Math.PI / 180);
                for (int seg = 0; seg < 8; seg++) {
                    float segStart = (rot + seg * 45) % 360;
                    float segMid = (float)((segStart + 22.5f) * Math.PI / 180);
                    float doppler = 0.5f + 0.5f * (float)Math.Cos(segMid - dopplerAngle);
                    int segA = (int)(ra * (0.4f + 0.6f * doppler));
                    int rr = Math.Min(255, 180 + ring * 12);
                    int rb = Math.Max(0, 255 - ring * 25);
                    using (var p = new Pen(Color.FromArgb(segA, rr, 100 + ring * 5, rb), thickness))
                        g.DrawArc(p, cx - r, cy - r * 0.4f, r * 2, r * 0.8f, segStart, 40);
                }
            }

            // === HOT INNER DISK — white-hot innermost ring ===
            float innerR = 7;
            float innerPulse = 0.7f + 0.3f * (float)Math.Sin(t * 20);
            int innerA = (int)(a * 0.4f * innerPulse * op);
            using (var p = new Pen(Color.FromArgb(innerA, 255, 220, 200), 1.2f))
                g.DrawEllipse(p, cx - innerR, cy - innerR * 0.4f, innerR * 2, innerR * 0.8f);

            // === PHOTON SPHERE — light orbiting at critical radius ===
            for (int ph = 0; ph < 4; ph++) {
                float phAngle = t * 300 + ph * 90;
                float phRad = (float)(phAngle * Math.PI / 180);
                float phR = 7.5f;
                float phx = cx + (float)Math.Cos(phRad) * phR;
                float phy = cy + (float)Math.Sin(phRad) * phR * 0.4f;
                int pha = (int)(a * 0.3f * op);
                using (var b = new SolidBrush(Color.FromArgb(pha, 255, 240, 220)))
                    g.FillEllipse(b, phx - 1, phy - 1, 2, 2);
            }

            // === EVENT HORIZON — pure black void ===
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.95f * op), 2, 2, 5)))
                g.FillEllipse(b, cx - 5.5f, cy - 5.5f, 11, 11);
            // Shadow edge — slightly visible
            using (var p = new Pen(Color.FromArgb((int)(a * 0.15f * op), 100, 60, 160), 0.5f))
                g.DrawEllipse(p, cx - 6, cy - 6, 12, 12);

            // === MATTER INFALL — particles spiraling in ===
            for (int inf = 0; inf < 8; inf++) {
                float infAngle = (float)(rng.NextDouble() * Math.PI * 2) + t * 15;
                float infDist = 30 * (1f - ((t * 3 + inf * 0.12f) % 1.0f));
                if (infDist < 6) continue;
                float infx = cx + (float)Math.Cos(infAngle + infDist * 0.1f) * infDist;
                float infy = cy + (float)Math.Sin(infAngle + infDist * 0.1f) * infDist * 0.4f;
                float infFade = (infDist - 6) / 24f;
                int infa = (int)(a * 0.3f * infFade * op);
                if (infa > 0)
                    using (var b = new SolidBrush(Color.FromArgb(infa, 200, 150, 255)))
                        g.FillEllipse(b, infx - 1, infy - 1, 2, 2);
            }

            // === RELATIVISTIC JET — faint vertical jet ===
            for (int jSide = -1; jSide <= 1; jSide += 2) {
                float jetLen = 25 * op;
                for (int jp = 0; jp < 6; jp++) {
                    float jpDist = (jp + 1) * jetLen / 6;
                    float jpx = cx + (float)Math.Sin(t * 10 + jp * 0.5f) * 2;
                    float jpy = cy + jSide * jpDist;
                    int jpa = (int)(a * 0.1f * (1f - jp / 7f) * op);
                    if (jpa > 0)
                        using (var b = new SolidBrush(Color.FromArgb(jpa, 160, 120, 255)))
                            g.FillEllipse(b, jpx - 1.5f, jpy - 1, 3, 2);
                }
            }
        }

        static void PaintAuroraWave(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            int a = (int)(fade * 210);
            int scheme = (int)ev.Param1 % 3;
            float baseY = ev.Y * h;

            float op;
            if (t < 0.15f) op = t / 0.15f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));

            // Color schemes
            int[][] colors = {
                new[] {30,255,80, 80,255,150, 50,200,100},     // green
                new[] {150,50,255, 200,80,255, 120,100,255},    // purple
                new[] {50,150,255, 80,200,255, 100,180,255}     // blue
            };
            int[] sc = colors[scheme];

            // === CURTAIN RIBBONS — 5 overlapping layers ===
            for (int layer = 0; layer < 5; layer++) {
                float yOff = layer * 8 - 16;
                float layerBright = 0.5f + 0.3f * (float)Math.Sin(t * 5 + layer * 1.2f);
                int layerA = (int)(a * layerBright * op * (0.8f - layer * 0.08f));
                if (layerA <= 0) continue;

                int ci = (layer * 3) % 9;
                int cr = sc[ci], cg2 = sc[ci + 1], cb = sc[ci + 2];

                // Draw as series of vertical strips for curtain effect
                int strips = 20;
                for (int strip = 0; strip < strips; strip++) {
                    float stripX = (strip / (float)(strips - 1)) * w;
                    float fold1 = (float)Math.Sin(t * 6 + strip * 0.4f + layer * 1.3f) * 15;
                    float fold2 = (float)Math.Sin(t * 4 + strip * 0.6f + layer * 0.8f) * 8;
                    float stripY = baseY + yOff + fold1 + fold2;
                    float stripH = 12 + (float)Math.Sin(t * 3 + strip * 0.3f + layer) * 6;

                    // Brightness variation across width
                    float xBright = 0.5f + 0.5f * (float)Math.Sin(t * 8 + strip * 0.7f + layer * 2);
                    int sa = (int)(layerA * 0.3f * xBright);
                    if (sa <= 0) continue;

                    float stripW = w / strips + 2;
                    // Top portion — brighter
                    using (var b = new SolidBrush(Color.FromArgb(sa, cr, cg2, cb)))
                        g.FillRectangle(b, stripX, stripY, stripW, stripH * 0.4f);
                    // Middle — main color
                    using (var b = new SolidBrush(Color.FromArgb((int)(sa * 0.7f), cr, cg2, cb)))
                        g.FillRectangle(b, stripX, stripY + stripH * 0.4f, stripW, stripH * 0.3f);
                    // Bottom — fading
                    using (var b = new SolidBrush(Color.FromArgb(sa / 3, cr, cg2, cb)))
                        g.FillRectangle(b, stripX, stripY + stripH * 0.7f, stripW, stripH * 0.3f);
                }
            }

            // === SHIMMER SPARKLES ===
            var sparkRng = new Random(ev.Seed + (int)(t * 12));
            for (int sp = 0; sp < 8; sp++) {
                float spx = (float)sparkRng.NextDouble() * w;
                float spy = baseY + (float)(sparkRng.NextDouble() - 0.5) * 40;
                float spBright = (float)Math.Max(0, Math.Sin(t * 25 + sp * 3.1f));
                if (spBright < 0.5f) continue;
                int spa = (int)(a * 0.5f * spBright * op);
                using (var b = new SolidBrush(Color.FromArgb(spa, 200, 255, 200)))
                    g.FillEllipse(b, spx - 1, spy - 1, 2, 2);
            }
        }

        static void PaintSpaceTrain(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            int a = (int)(fade * 210);
            int cars = (int)ev.Param1;
            float dir = ev.DirX > 0 ? 1 : -1;

            for (int c = 0; c < cars; c++) {
                float delay = c * 0.06f;
                float ct = t - delay;
                float cx = (ev.X + ev.DirX * ct) * w;
                float cy = (ev.Y + ev.DirY * ct) * h + (float)Math.Sin(ct * 10 + c * 0.8f) * 2;
                int ca = (int)(a * Math.Max(0, Math.Min(1, 1 - c * 0.06f)));
                if (ca <= 0) continue;

                if (c == 0) {
                    // === LOCOMOTIVE — engine with headlight and smokestack ===
                    // Headlight beam
                    float beamLen = 25;
                    for (int bl = 3; bl >= 1; bl--) {
                        int ba = ca / (8 * bl);
                        float bw = bl * 3;
                        if (ba > 0)
                            using (var b = new SolidBrush(Color.FromArgb(ba, 255, 250, 200)))
                                g.FillEllipse(b, cx + dir * 6, cy - bw, beamLen * dir, bw * 2);
                    }
                    // Engine body
                    using (var b = new SolidBrush(Color.FromArgb(ca, 255, 200, 60)))
                        g.FillRectangle(b, cx - 7, cy - 4, 14, 8);
                    using (var p = new Pen(Color.FromArgb(ca / 2, 255, 240, 150), 0.6f))
                        g.DrawRectangle(p, cx - 7, cy - 4, 14, 8);
                    // Smokestack
                    using (var b = new SolidBrush(Color.FromArgb(ca, 200, 170, 50)))
                        g.FillRectangle(b, cx - 2 * dir, cy - 6, 3, 3);
                    // Headlight
                    float hlPulse = 0.7f + 0.3f * (float)Math.Sin(t * 30);
                    using (var b = new SolidBrush(Color.FromArgb((int)(ca * hlPulse), 255, 255, 220)))
                        g.FillEllipse(b, cx + dir * 5.5f, cy - 2, 3, 3);
                    // Cowcatcher
                    using (var p = new Pen(Color.FromArgb(ca * 2 / 3, 200, 180, 60), 0.8f)) {
                        g.DrawLine(p, cx + dir * 7, cy - 3, cx + dir * 9, cy - 4);
                        g.DrawLine(p, cx + dir * 7, cy + 3, cx + dir * 9, cy + 4);
                    }
                    // Steam puffs
                    for (int puff = 0; puff < 3; puff++) {
                        float pLife = (t * 4 + puff * 0.3f) % 1.0f;
                        float px = cx - 2 * dir + (float)Math.Sin(t * 8 + puff) * 2 - dir * pLife * 8;
                        float py = cy - 7 - pLife * 8;
                        float psz = 2 + pLife * 4;
                        int pa2 = (int)(ca * 0.15f * (1f - pLife));
                        if (pa2 > 0)
                            using (var b = new SolidBrush(Color.FromArgb(pa2, 200, 210, 220)))
                                g.FillEllipse(b, px - psz, py - psz, psz * 2, psz * 2);
                    }
                } else {
                    // === PASSENGER/CARGO CARS ===
                    Color carBody = c % 3 == 1 ? Color.FromArgb(ca, 80, 130, 190) :
                        c % 3 == 2 ? Color.FromArgb(ca, 100, 160, 180) : Color.FromArgb(ca, 90, 140, 170);
                    using (var b = new SolidBrush(carBody))
                        g.FillRectangle(b, cx - 6, cy - 3.5f, 12, 7);
                    using (var p = new Pen(Color.FromArgb(ca / 3, 180, 210, 240), 0.5f))
                        g.DrawRectangle(p, cx - 6, cy - 3.5f, 12, 7);

                    // Windows — warm lit
                    for (int wi = 0; wi < 3; wi++) {
                        float wx = cx - 4 + wi * 3.5f;
                        float windowPulse = 0.6f + 0.4f * (float)Math.Sin(t * 10 + c * 2 + wi * 1.5f);
                        int wa = (int)(ca * 0.8f * windowPulse);
                        using (var b = new SolidBrush(Color.FromArgb(wa, 220, 240, 255)))
                            g.FillRectangle(b, wx, cy - 2, 2, 2.5f);
                    }

                    // Roof detail
                    using (var p = new Pen(Color.FromArgb(ca / 4, 150, 180, 210), 0.4f))
                        g.DrawLine(p, cx - 5, cy - 3.5f, cx + 5, cy - 3.5f);
                }

                // === COUPLING between cars ===
                if (c < cars - 1) {
                    float nextCx = (ev.X + ev.DirX * (ct - 0.06f)) * w;
                    int coupA = ca / 3;
                    using (var p = new Pen(Color.FromArgb(coupA, 150, 170, 190), 0.5f))
                        g.DrawLine(p, cx - dir * 6, cy, nextCx + dir * 6, cy);
                }

                // Wheels — energy sparks underneath
                for (int wh = -1; wh <= 1; wh += 2) {
                    float whx = cx + wh * 4;
                    float sparkle = (float)Math.Max(0, Math.Sin(t * 50 + c * 3 + wh * 2));
                    int wa2 = (int)(ca * 0.3f * sparkle);
                    if (wa2 > 0)
                        using (var b = new SolidBrush(Color.FromArgb(wa2, 200, 220, 255)))
                            g.FillEllipse(b, whx - 1, cy + 3.5f, 2, 1.5f);
                }
            }
        }

        static void PaintOrb(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int colorIdx = (int)ev.Param1 % 4;
            int[][] orbColors = {
                new[]{100,200,255}, new[]{255,180,80}, new[]{180,100,255}, new[]{100,255,150}
            };
            int[] oc = orbColors[colorIdx];

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            float pulse = 0.75f + 0.25f * (float)Math.Sin(t * 12);
            float r = 7 * pulse * bodyFade;
            float bob = (float)Math.Sin(t * 8) * 2;
            cy += bob;
            int a = (int)(fade * 210);

            // === AMBIENT FIELD ===
            for (int gl = 4; gl >= 1; gl--) {
                float gr = r + gl * 6;
                float glPulse = 0.4f + 0.6f * (float)Math.Sin(t * 8 + gl);
                int ga = (int)(a * 0.04f * glPulse * bodyFade / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, oc[0], oc[1], oc[2])))
                        g.FillEllipse(b, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            // === ORBITING RINGS ===
            for (int ring = 0; ring < 3; ring++) {
                float ringR = r + 3 + ring * 3;
                float ringRot = t * (80 + ring * 40) + ring * 60;
                int ra = (int)(a * 0.2f * bodyFade);
                using (var p = new Pen(Color.FromArgb(ra, oc[0], oc[1], oc[2]), 0.5f))
                    g.DrawArc(p, cx - ringR, cy - ringR * 0.4f, ringR * 2, ringR * 0.8f, ringRot, 120);
            }

            // === ORB BODY ===
            // Outer orb
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.4f * bodyFade), oc[0], oc[1], oc[2])))
                g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
            // Inner glow
            float ir = r * 0.65f;
            int irR = Math.Min(255, oc[0] + 40), irG = Math.Min(255, oc[1] + 40), irB = Math.Min(255, oc[2] + 40);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * bodyFade), irR, irG, irB)))
                g.FillEllipse(b, cx - ir, cy - ir, ir * 2, ir * 2);
            // Hot core
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.7f * bodyFade), 255, 255, 255)))
                g.FillEllipse(b, cx - r * 0.25f, cy - r * 0.25f, r * 0.5f, r * 0.5f);
            // Highlight crescent
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.2f * bodyFade), 255, 255, 255)))
                g.FillEllipse(b, cx - r * 0.5f, cy - r * 0.6f, r * 0.8f, r * 0.5f);

            // === ENERGY MOTES — orbiting particles ===
            for (int mote = 0; mote < 6; mote++) {
                float mAngle = t * 10 + mote * (float)(Math.PI * 2 / 6);
                float mDist = r + 4 + (float)Math.Sin(t * 6 + mote * 2) * 3;
                float mx = cx + (float)Math.Cos(mAngle) * mDist;
                float my = cy + (float)Math.Sin(mAngle) * mDist * 0.5f;
                float mPulse = 0.3f + 0.7f * (float)Math.Max(0, Math.Sin(t * 20 + mote * 2.5f));
                int ma = (int)(a * 0.35f * mPulse * bodyFade);
                if (ma > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ma, oc[0], oc[1], oc[2])))
                        g.FillEllipse(b, mx - 1.2f, my - 1.2f, 2.4f, 2.4f);
            }
        }

        static void PaintLaserGrid(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 210);
            float rot = ev.Param1 + t * 50;
            float rad = (float)(rot * Math.PI / 180);

            float op;
            if (t < 0.15f) op = t / 0.15f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));

            float gridSz = Math.Min(w, h) * 0.22f * op;
            int lines = 6;
            float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);

            // === GRID GLOW FIELD ===
            int fieldA = (int)(a * 0.03f * op);
            if (fieldA > 0)
                using (var b = new SolidBrush(Color.FromArgb(fieldA, 50, 255, 150)))
                    g.FillEllipse(b, cx - gridSz, cy - gridSz, gridSz * 2, gridSz * 2);

            // === GRID LINES — with pulse animation ===
            for (int i = 0; i < lines; i++) {
                float offset = (i - lines / 2f) * gridSz / lines;
                float linePulse = 0.4f + 0.6f * (float)Math.Max(0, Math.Sin(t * 15 + i * 1.5f));
                int la = (int)(a * 0.5f * linePulse * op);
                if (la <= 0) continue;

                // Horizontal line (rotated)
                float x1 = cx + cos * (-gridSz / 2) - sin * offset;
                float y1 = cy + sin * (-gridSz / 2) + cos * offset;
                float x2 = cx + cos * (gridSz / 2) - sin * offset;
                float y2 = cy + sin * (gridSz / 2) + cos * offset;

                // Glow
                using (var p = new Pen(Color.FromArgb(la / 3, 50, 255, 150), 2.5f))
                    g.DrawLine(p, x1, y1, x2, y2);
                // Core
                using (var p = new Pen(Color.FromArgb(la, 100, 255, 180), 0.6f))
                    g.DrawLine(p, x1, y1, x2, y2);

                // Vertical line (rotated)
                x1 = cx + cos * offset - sin * (-gridSz / 2);
                y1 = cy + sin * offset + cos * (-gridSz / 2);
                x2 = cx + cos * offset - sin * (gridSz / 2);
                y2 = cy + sin * offset + cos * (gridSz / 2);

                using (var p = new Pen(Color.FromArgb(la / 3, 50, 255, 150), 2.5f))
                    g.DrawLine(p, x1, y1, x2, y2);
                using (var p = new Pen(Color.FromArgb(la, 100, 255, 180), 0.6f))
                    g.DrawLine(p, x1, y1, x2, y2);
            }

            // === INTERSECTION NODES — bright at crosspoints ===
            for (int ix = 0; ix < lines; ix++) {
                for (int iy = 0; iy < lines; iy++) {
                    float offX = (ix - lines / 2f) * gridSz / lines;
                    float offY = (iy - lines / 2f) * gridSz / lines;
                    float nx = cx + cos * offX - sin * offY;
                    float ny = cy + sin * offX + cos * offY;
                    float nPulse = (float)Math.Max(0, Math.Sin(t * 20 + ix * 1.3f + iy * 1.7f));
                    if (nPulse < 0.3f) continue;
                    int na = (int)(a * 0.4f * nPulse * op);
                    using (var b = new SolidBrush(Color.FromArgb(na, 150, 255, 200)))
                        g.FillEllipse(b, nx - 1.5f, ny - 1.5f, 3, 3);
                }
            }

            // === SCAN LINE — sweeping pulse across grid ===
            float scanPhase = (t * 3) % 1.0f;
            float scanOffset = (scanPhase - 0.5f) * gridSz;
            float sx1 = cx + cos * (-gridSz / 2) - sin * scanOffset;
            float sy1 = cy + sin * (-gridSz / 2) + cos * scanOffset;
            float sx2 = cx + cos * (gridSz / 2) - sin * scanOffset;
            float sy2 = cy + sin * (gridSz / 2) + cos * scanOffset;
            float scanBright = 1f - Math.Abs(scanPhase - 0.5f) * 2;
            int scanA = (int)(a * 0.4f * scanBright * op);
            if (scanA > 0) {
                using (var p = new Pen(Color.FromArgb(scanA, 150, 255, 220), 1.5f))
                    g.DrawLine(p, sx1, sy1, sx2, sy2);
            }
        }

        static void PaintSpaceDolphin(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 220);
            float leap = (float)Math.Sin(t * 12) * 14;
            cy += leap;
            float dir = ev.DirX > 0 ? 1 : -1;

            float bodyFade;
            if (t < 0.06f) bodyFade = t / 0.06f;
            else if (t > 0.92f) bodyFade = (1f - t) / 0.08f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            // Body glow aura
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.1f * bodyFade), 130, 190, 250)))
                g.FillEllipse(b, cx - 18, cy - 9, 36, 18);

            // === MAIN BODY — bright silver-blue, clearly visible ===
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * bodyFade), 140, 180, 230)))
                g.FillEllipse(b, cx - 12, cy - 5, 24, 10);

            // Belly — lighter silver
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * bodyFade), 200, 225, 250)))
                g.FillEllipse(b, cx - 8, cy, 16, 4);

            // === DORSAL FIN — prominent ===
            PointF[] dorsal = {
                new PointF(cx - 2, cy - 4),
                new PointF(cx + 1, cy - 11),
                new PointF(cx + 5, cy - 4.5f)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.8f * bodyFade), 130, 170, 225)))
                g.FillPolygon(b, dorsal);

            // === HEAD + ROSTRUM ===
            float headX = cx + dir * 10;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * bodyFade), 145, 185, 230)))
                g.FillEllipse(b, headX - 4, cy - 3, 8, 6);
            // Beak/rostrum
            PointF[] beak = {
                new PointF(headX + dir * 3, cy - 1),
                new PointF(headX + dir * 9, cy + 0.5f),
                new PointF(headX + dir * 3, cy + 2)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.85f * bodyFade), 140, 180, 225)))
                g.FillPolygon(b, beak);

            // Eye — bright and distinct
            float eyeX = headX + dir * 2, eyeY = cy - 1;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * bodyFade), 30, 40, 80)))
                g.FillEllipse(b, eyeX - 1.5f, eyeY - 1.5f, 3, 3);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * bodyFade), 255, 255, 255)))
                g.FillEllipse(b, eyeX - 0.5f + dir * 0.3f, eyeY - 1, 1, 1);

            // Smile
            using (var p = new Pen(Color.FromArgb((int)(a * 0.4f * bodyFade), 80, 120, 180), 0.5f))
                g.DrawArc(p, headX + dir * 1, cy, 4, 3, dir > 0 ? 0 : 180, 90);

            // === PECTORAL FINS ===
            for (int pf = -1; pf <= 1; pf += 2) {
                float pfWave = (float)Math.Sin(t * 14 + pf) * 3;
                PointF[] pfin = {
                    new PointF(cx - 2 * dir, cy + pf * 2),
                    new PointF(cx - 7 * dir + pfWave * 0.3f, cy + pf * (6 + pfWave)),
                    new PointF(cx - 4 * dir, cy + pf * 2.5f)
                };
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * bodyFade), 130, 175, 225)))
                    g.FillPolygon(b, pfin);
            }

            // === TAIL FLUKE ===
            float tailX2 = cx - 12 * dir;
            float flukeW = (float)Math.Sin(t * 18) * 5;
            PointF[] fluke1 = { new PointF(tailX2, cy), new PointF(tailX2 - 6 * dir, cy - 5 + flukeW), new PointF(tailX2 - 3 * dir, cy) };
            PointF[] fluke2 = { new PointF(tailX2, cy), new PointF(tailX2 - 6 * dir, cy + 5 + flukeW), new PointF(tailX2 - 3 * dir, cy) };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.7f * bodyFade), 125, 170, 220)))
                g.FillPolygon(b, fluke1);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.7f * bodyFade), 125, 170, 220)))
                g.FillPolygon(b, fluke2);

            // === BUBBLE TRAIL ===
            for (int bub = 0; bub < 5; bub++) {
                float bLife = (t * 3.5f + bub * 0.18f) % 1.0f;
                float bx = cx - dir * (8 + bLife * 15) + (float)Math.Sin(t * 15 + bub * 2) * 4;
                float by = cy - 3 - bLife * 10;
                float br = 1.2f + bLife * 1.5f;
                float bFade = bLife < 0.1f ? bLife / 0.1f : (1f - bLife) / 0.9f;
                int ba = (int)(a * 0.25f * bFade * bodyFade);
                if (ba > 0)
                    using (var p = new Pen(Color.FromArgb(ba, 180, 220, 255), 0.5f))
                        g.DrawEllipse(p, bx - br, by - br, br * 2, br * 2);
            }
        }

        static void PaintStardust(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            int a = (int)(fade * 210);
            float cx = ev.X * w, cy = ev.Y * h;
            var rng = new Random(ev.Seed);

            float op;
            if (t < 0.15f) op = t / 0.15f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));

            // === AMBIENT CLOUD — faint glowing backdrop ===
            for (int cl = 0; cl < 4; cl++) {
                float clx = cx + (float)(rng.NextDouble() - 0.5) * 50;
                float cly = cy + (float)(rng.NextDouble() - 0.5) * 35;
                float clsz = 20 + (float)rng.NextDouble() * 15;
                float clPulse = 0.3f + 0.3f * (float)Math.Sin(t * 3 + cl * 1.5f);
                int cla = (int)(a * 0.04f * clPulse * op);
                Color clc = cl % 3 == 0 ? Color.FromArgb(cla, 255, 220, 150) :
                    cl % 3 == 1 ? Color.FromArgb(cla, 180, 160, 255) : Color.FromArgb(cla, 160, 255, 200);
                if (cla > 0)
                    using (var b = new SolidBrush(clc))
                        g.FillEllipse(b, clx - clsz, cly - clsz * 0.7f, clsz * 2, clsz * 1.4f);
            }

            // === DUST PARTICLES — 25 individual motes ===
            var dustRng = new Random(ev.Seed + 50);
            for (int i = 0; i < 25; i++) {
                float ox = (float)(dustRng.NextDouble() - 0.5) * 70;
                float oy = (float)(dustRng.NextDouble() - 0.5) * 50;
                float driftX = (float)Math.Sin(t * 3.5 + i * 0.8) * 10;
                float driftY = (float)Math.Cos(t * 2.8 + i * 1.1) * 7;
                float fx = cx + ox + driftX;
                float fy = cy + oy + driftY;

                float pulse = (float)Math.Max(0, Math.Sin(t * 7 + i * 2.1));
                float twinkle = pulse > 0.4f ? 1f : pulse / 0.4f;
                int pa = (int)(a * 0.6f * twinkle * op);
                if (pa <= 2) continue;

                Color[] dustColors = {
                    Color.FromArgb(pa, 255, 230, 160), Color.FromArgb(pa, 200, 190, 255),
                    Color.FromArgb(pa, 190, 255, 210), Color.FromArgb(pa, 255, 200, 200)
                };
                Color dc = dustColors[i % 4];

                // Glow halo
                using (var b = new SolidBrush(Color.FromArgb(pa / 5, dc.R, dc.G, dc.B)))
                    g.FillEllipse(b, fx - 3, fy - 3, 6, 6);
                // Particle
                float sz = 1.5f + twinkle * 1.5f;
                using (var b = new SolidBrush(dc))
                    g.FillEllipse(b, fx - sz * 0.5f, fy - sz * 0.5f, sz, sz);

                // Twinkle cross on brightest particles
                if (twinkle > 0.7f) {
                    int crossA = (int)(pa * 0.3f * (twinkle - 0.7f) * 3.3f);
                    using (var p = new Pen(Color.FromArgb(crossA, dc.R, dc.G, dc.B), 0.3f)) {
                        g.DrawLine(p, fx - 2, fy, fx + 2, fy);
                        g.DrawLine(p, fx, fy - 2, fx, fy + 2);
                    }
                }
            }
        }

        static void PaintSolarFlare(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 210);
            bool blue = ev.Param1 > 0.5f;

            float op;
            if (t < 0.12f) op = t / 0.12f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));

            int cr1, cg1, cb1, cr2, cg2, cb2;
            if (blue) { cr1=100; cg1=150; cb1=255; cr2=80; cg2=120; cb2=220; }
            else { cr1=255; cg1=160; cb1=50; cr2=255; cg2=100; cb2=20; }

            // === CORONAL MASS — expanding plasma cloud ===
            float grow = Math.Min(1, t * 3) * op;
            for (int gl = 4; gl >= 1; gl--) {
                float gr = (8 + gl * 8) * grow;
                int ga = (int)(a * 0.04f * op / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, cr2, cg2, cb2)))
                        g.FillEllipse(b, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            // === FLARE TENDRILS — arcing plasma streams ===
            var rng = new Random(ev.Seed);
            for (int tendril = 0; tendril < 7; tendril++) {
                float angle = (float)(tendril * 51.4 + t * 80) * (float)Math.PI / 180;
                float len = (18 + (float)Math.Sin(t * 10 + tendril * 1.8f) * 10) * grow;
                float midAngle = angle + (float)Math.Sin(t * 15 + tendril * 2) * 0.4f;

                float mx = cx + (float)Math.Cos(midAngle) * len * 0.5f;
                float my = cy + (float)Math.Sin(midAngle) * len * 0.5f;
                float ex = cx + (float)Math.Cos(angle) * len;
                float ey = cy + (float)Math.Sin(angle) * len;

                float tPulse = 0.5f + 0.5f * (float)Math.Sin(t * 12 + tendril * 2.3f);
                int ta = (int)(a * 0.3f * tPulse * op);

                // Tendril glow
                using (var p = new Pen(Color.FromArgb(ta / 3, cr2, cg2, cb2), 3.5f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawBezier(p, cx, cy, mx + (float)Math.Sin(t * 20 + tendril) * 4, my, ex, ey, ex, ey);
                }
                // Tendril core
                using (var p = new Pen(Color.FromArgb(ta, cr1, cg1, cb1), 1.2f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawBezier(p, cx, cy, mx + (float)Math.Sin(t * 20 + tendril) * 4, my, ex, ey, ex, ey);
                }

                // Tip sparkle
                if (tPulse > 0.6f) {
                    int tipA = (int)(ta * 0.5f);
                    using (var b = new SolidBrush(Color.FromArgb(tipA, 255, 255, 220)))
                        g.FillEllipse(b, ex - 1.5f, ey - 1.5f, 3, 3);
                }
            }

            // === CORE — brilliant star surface ===
            float coreR = 5 * op;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.7f * op), cr1, cg1, cb1)))
                g.FillEllipse(b, cx - coreR, cy - coreR, coreR * 2, coreR * 2);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * op), 255, 255, 240)))
                g.FillEllipse(b, cx - coreR * 0.5f, cy - coreR * 0.5f, coreR, coreR);

            // === EJECTA PARTICLES ===
            for (int ej = 0; ej < 10; ej++) {
                float ejAngle = (float)(rng.NextDouble() * Math.PI * 2);
                float ejDist = ((t * 4 + ej * 0.1f) % 1.0f) * 25 * grow;
                float ejx = cx + (float)Math.Cos(ejAngle) * ejDist;
                float ejy = cy + (float)Math.Sin(ejAngle) * ejDist;
                float ejFade = 1f - ejDist / (25 * grow + 0.01f);
                int eja = (int)(a * 0.25f * ejFade * op);
                if (eja > 0)
                    using (var b = new SolidBrush(Color.FromArgb(eja, cr1, cg1, cb1)))
                        g.FillEllipse(b, ejx - 1, ejy - 1, 2, 2);
            }
        }

        static void PaintSpaceButterfly(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float dir = ev.DirX > 0 ? 1 : -1;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            float wingFlap = (float)Math.Sin(t * 22);
            float wingSpread = 8 + Math.Abs(wingFlap) * 5;
            float bob = (float)Math.Sin(t * 15) * 2;
            cy += bob;
            int a = (int)(fade * 210);

            // === SPARKLE DUST TRAIL ===
            for (int dust = 0; dust < 10; dust++) {
                float dLife = (t * 5 + dust * 0.09f) % 1.0f;
                float dx = cx - dir * (4 + dLife * 25) + (float)Math.Sin(t * 25 + dust * 2) * 4;
                float dy = cy + (float)Math.Sin(t * 18 + dust * 1.5f) * 5;
                float dFade = (1f - dLife) * bodyFade;
                int da = (int)(a * 0.15f * dFade);
                if (da > 0) {
                    Color dc = dust % 3 == 0 ? Color.FromArgb(da, 255, 180, 230) :
                        dust % 3 == 1 ? Color.FromArgb(da, 230, 180, 255) : Color.FromArgb(da, 255, 220, 180);
                    using (var b = new SolidBrush(dc))
                        g.FillEllipse(b, dx - 1, dy - 1, 2, 2);
                }
            }

            // === WINGS — 4 wings (upper and lower pairs) ===
            for (int side = -1; side <= 1; side += 2) {
                float sideSpread = wingSpread * side;

                // Upper wing — larger
                float uwTipX = cx + sideSpread * 1.2f;
                float uwTipY = cy - 3 - Math.Abs(sideSpread) * 0.4f;
                PointF[] upperWing = {
                    new PointF(cx, cy - 2),
                    new PointF(cx + sideSpread * 0.5f, cy - wingSpread * 0.7f),
                    new PointF(uwTipX, uwTipY),
                    new PointF(cx + sideSpread * 0.4f, cy)
                };
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * bodyFade), 255, 140, 200)))
                    g.FillPolygon(b, upperWing);
                // Wing pattern
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f * bodyFade), 255, 200, 230)))
                    g.FillEllipse(b, cx + sideSpread * 0.5f - 2, cy - wingSpread * 0.3f - 2, 4, 3);
                // Wing edge highlight
                using (var p = new Pen(Color.FromArgb((int)(a * 0.25f * bodyFade), 255, 200, 240), 0.5f))
                    g.DrawLine(p, upperWing[1].X, upperWing[1].Y, upperWing[2].X, upperWing[2].Y);

                // Lower wing — smaller, different color
                float lwTipX = cx + sideSpread * 0.9f;
                float lwTipY = cy + 3 + Math.Abs(sideSpread) * 0.3f;
                PointF[] lowerWing = {
                    new PointF(cx, cy + 1),
                    new PointF(cx + sideSpread * 0.3f, cy),
                    new PointF(lwTipX, lwTipY),
                    new PointF(cx + sideSpread * 0.2f, cy + 3)
                };
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * bodyFade), 200, 140, 255)))
                    g.FillPolygon(b, lowerWing);
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.2f * bodyFade), 230, 200, 255)))
                    g.FillEllipse(b, cx + sideSpread * 0.4f - 1.5f, cy + 1, 3, 2);

                // Wing spots — eye markings
                float spotX = cx + sideSpread * 0.6f;
                float spotY = cy - wingSpread * 0.2f;
                float spotPulse = 0.4f + 0.6f * (float)Math.Max(0, Math.Sin(t * 15 + side * 2));
                int spotA = (int)(a * 0.4f * spotPulse * bodyFade);
                using (var b = new SolidBrush(Color.FromArgb(spotA, 30, 20, 60)))
                    g.FillEllipse(b, spotX - 1.5f, spotY - 1.5f, 3, 3);
                using (var p = new Pen(Color.FromArgb(spotA, 255, 220, 100), 0.4f))
                    g.DrawEllipse(p, spotX - 2, spotY - 2, 4, 4);
            }

            // === BODY — slender thorax ===
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.8f * bodyFade), 255, 240, 210)))
                g.FillEllipse(b, cx - 1.5f, cy - 4, 3, 8);

            // === ANTENNAE — curling ===
            for (int ant = -1; ant <= 1; ant += 2) {
                float antWave = (float)Math.Sin(t * 20 + ant) * 2;
                float tipX = cx + ant * (3 + antWave) + dir * 2;
                float tipY = cy - 8 - Math.Abs(antWave);
                int antA = (int)(a * 0.5f * bodyFade);
                using (var p = new Pen(Color.FromArgb(antA, 200, 180, 220), 0.4f))
                    g.DrawBezier(p, cx, cy - 4, cx + ant, cy - 6, tipX - ant * 0.5f, tipY + 2, tipX, tipY);
                // Tip bead
                using (var b = new SolidBrush(Color.FromArgb(antA, 255, 220, 160)))
                    g.FillEllipse(b, tipX - 0.8f, tipY - 0.8f, 1.6f, 1.6f);
            }
        }

        static void PaintLighthouse(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 200);
            float beamAngle = ev.Param1 + t * 180;

            float op;
            if (t < 0.15f) op = t / 0.15f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));

            // Tower — tapered, red/white striped
            PointF[] tower = {
                new PointF(cx - 4, cy + 2), new PointF(cx + 4, cy + 2),
                new PointF(cx + 3, cy + 16), new PointF(cx - 3, cy + 16)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * op), 220, 220, 230)))
                g.FillPolygon(b, tower);
            // Red stripes
            for (int stripe = 0; stripe < 3; stripe++) {
                float sy = cy + 4 + stripe * 4.5f;
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.7f * op), 200, 50, 50)))
                    g.FillRectangle(b, cx - 3.5f + stripe * 0.15f, sy, 7 - stripe * 0.3f, 2);
            }

            // Lamp room — bright box at top
            using (var b = new SolidBrush(Color.FromArgb((int)(a * op), 240, 240, 250)))
                g.FillRectangle(b, cx - 5, cy - 3, 10, 5);
            using (var p = new Pen(Color.FromArgb((int)(a * op), 180, 180, 200), 0.6f))
                g.DrawRectangle(p, cx - 5, cy - 3, 10, 5);

            // Roof cap
            PointF[] roof = { new PointF(cx, cy - 6), new PointF(cx - 5, cy - 3), new PointF(cx + 5, cy - 3) };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * op), 180, 50, 50)))
                g.FillPolygon(b, roof);

            // Rotating beam
            float rad = (float)(beamAngle * Math.PI / 180);
            float beamLen = Math.Min(w, h) * 0.3f;
            float bx = cx + (float)Math.Cos(rad) * beamLen;
            float by = cy + (float)Math.Sin(rad) * beamLen;
            using (var p = new Pen(Color.FromArgb((int)(a * 0.25f * op), 255, 255, 200), 6f))
                g.DrawLine(p, cx, cy, bx, by);
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f * op), 255, 255, 220), 2f))
                g.DrawLine(p, cx, cy, bx, by);

            // Light source glow
            float pulse = 0.7f + 0.3f * (float)Math.Sin(t * 20);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f * pulse * op), 255, 255, 200)))
                g.FillEllipse(b, cx - 8, cy - 8, 16, 16);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * pulse * op), 255, 255, 220)))
                g.FillEllipse(b, cx - 3, cy - 3, 6, 6);

            // Base/rock
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * op), 120, 110, 100)))
                g.FillEllipse(b, cx - 8, cy + 14, 16, 5);
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
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
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
            cy += (float)Math.Sin(t * 12) * 3f;
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
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(210 * fade);

            // Phase: 0-0.20 portal opens (ring expands + energy crackle)
            //        0.20-0.75 stable (spinning, event horizon ripples, particles sucked in)
            //        0.75-1.0 collapse (shrinks with energy burst)
            float openT, portalSize;
            if (t < 0.20f) { openT = t / 0.20f; portalSize = openT; }
            else if (t < 0.75f) { openT = 1f; portalSize = 1f; }
            else { openT = 1f; portalSize = (1f - t) / 0.25f; }
            portalSize = Math.Max(0, Math.Min(1, portalSize));
            if (portalSize <= 0.01f) return;

            float baseR = 22 * portalSize;
            float spin = t * 200;
            float spinRad = (float)(spin * Math.PI / 180);

            // === OUTER AMBIENT GLOW ===
            for (int gl = 4; gl >= 1; gl--) {
                float gr = baseR + gl * 12;
                int ga = (int)(a * 0.04f * portalSize / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 80, 160, 255)))
                        g.FillEllipse(b, cx - gr, cy - gr * 0.55f, gr * 2, gr * 1.1f);
            }

            // === ENERGY RING STRUCTURE — segmented spinning rings ===
            for (int ring = 0; ring < 4; ring++) {
                float rr = baseR + ring * 5;
                float thickness = 1.8f - ring * 0.3f;
                int ra = (int)(a * (0.7f - ring * 0.12f) * portalSize);
                if (ra <= 0) continue;
                float ringSpinDeg = spin * (1.2f - ring * 0.15f) + ring * 30;
                Color ringColor;
                switch (ring) {
                    case 0: ringColor = Color.FromArgb(ra, 140, 200, 255); break;
                    case 1: ringColor = Color.FromArgb(ra, 100, 160, 255); break;
                    case 2: ringColor = Color.FromArgb(ra, 130, 120, 255); break;
                    default: ringColor = Color.FromArgb(ra, 80, 140, 220); break;
                }
                for (int seg = 0; seg < 6; seg++) {
                    float segStart = ringSpinDeg + seg * 60;
                    using (var p = new Pen(ringColor, thickness))
                        g.DrawArc(p, cx - rr, cy - rr * 0.45f, rr * 2, rr * 0.9f, segStart, 42);
                }
            }

            // === CHEVRON ENERGY NODES ===
            int chevrons = 8;
            for (int c = 0; c < chevrons; c++) {
                float cAngle = (float)(c * Math.PI * 2 / chevrons) + spinRad * 0.3f;
                float cX = cx + (float)Math.Cos(cAngle) * (baseR + 2);
                float cY = cy + (float)Math.Sin(cAngle) * (baseR + 2) * 0.45f;
                float chevPulse;
                if (t < 0.20f) {
                    float lockTime = c / (float)chevrons * 0.18f;
                    chevPulse = t > lockTime ? Math.Min(1, (t - lockTime) / 0.04f) : 0;
                } else {
                    chevPulse = 0.6f + 0.4f * (float)Math.Sin(t * 30 + c * 1.5f);
                }
                int ca = (int)(a * chevPulse * portalSize);
                if (ca <= 0) continue;
                float cs = 3f + chevPulse * 2f;
                using (var b = new SolidBrush(Color.FromArgb(ca / 3, 120, 200, 255)))
                    g.FillEllipse(b, cX - cs * 1.5f, cY - cs * 1.5f, cs * 3, cs * 3);
                using (var b = new SolidBrush(Color.FromArgb(ca, 200, 230, 255)))
                    g.FillEllipse(b, cX - cs * 0.5f, cY - cs * 0.5f, cs, cs);
                if (chevPulse > 0.7f)
                    using (var b = new SolidBrush(Color.FromArgb((int)(ca * 0.7f), 255, 255, 255)))
                        g.FillEllipse(b, cX - 1.5f, cY - 1.5f, 3, 3);
            }

            // === EVENT HORIZON — rippling liquid-light fill ===
            float horizonR = baseR * 0.85f;
            if (horizonR > 1) {
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f * portalSize), 15, 25, 60)))
                    g.FillEllipse(b, cx - horizonR, cy - horizonR * 0.45f, horizonR * 2, horizonR * 0.9f);
                for (int rip = 0; rip < 5; rip++) {
                    float ripPhase = (t * 8 + rip * 0.6f) % 1.0f;
                    float ripR = ripPhase * horizonR;
                    int ripA = (int)(a * 0.25f * (1f - ripPhase) * portalSize);
                    if (ripA > 0 && ripR > 1)
                        using (var p = new Pen(Color.FromArgb(ripA, 100, 180, 255), 0.8f))
                            g.DrawEllipse(p, cx - ripR, cy - ripR * 0.45f, ripR * 2, ripR * 0.9f);
                }
                float centerGlow = horizonR * 0.4f;
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.15f * portalSize), 150, 210, 255)))
                    g.FillEllipse(b, cx - centerGlow, cy - centerGlow * 0.45f, centerGlow * 2, centerGlow * 0.9f);
                float coreR = horizonR * 0.15f;
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.2f * portalSize), 220, 240, 255)))
                    g.FillEllipse(b, cx - coreR, cy - coreR * 0.45f, coreR * 2, coreR * 0.9f);
            }

            // === PARTICLE STREAMS spiraling in ===
            var rng = new Random(ev.Seed);
            for (int p2 = 0; p2 < 14; p2++) {
                float pAngle = (float)(rng.NextDouble() * Math.PI * 2);
                float pDist = baseR + 15 + (float)rng.NextDouble() * 35;
                float pSpeed = 0.5f + (float)rng.NextDouble() * 1.5f;
                float pLife = (t * pSpeed + (float)rng.NextDouble()) % 1.0f;
                float pCurDist = pDist * (1f - pLife);
                if (pCurDist < 2) continue;
                float pSpin = pAngle + pLife * 4f;
                float px = cx + (float)Math.Cos(pSpin) * pCurDist;
                float py = cy + (float)Math.Sin(pSpin) * pCurDist * 0.45f;
                float pFade = pLife < 0.2f ? pLife / 0.2f : pLife > 0.8f ? (1f - pLife) / 0.2f : 1f;
                int pa = (int)(a * 0.5f * pFade * portalSize);
                if (pa <= 0) continue;
                float pSz = 1.5f + (1f - pLife) * 1.5f;
                using (var b = new SolidBrush(Color.FromArgb(pa, 160, 210, 255)))
                    g.FillEllipse(b, px - pSz, py - pSz, pSz * 2, pSz * 2);
                float trailPx = cx + (float)Math.Cos(pSpin - 0.3f) * (pCurDist + 4);
                float trailPy = cy + (float)Math.Sin(pSpin - 0.3f) * (pCurDist + 4) * 0.45f;
                using (var pen = new Pen(Color.FromArgb(pa / 3, 120, 180, 255), 0.6f))
                    g.DrawLine(pen, px, py, trailPx, trailPy);
            }

            // === ENERGY CRACKLE during opening ===
            if (t < 0.25f && t > 0.05f) {
                var crackRng = new Random(ev.Seed + (int)(t * 50));
                int crackCount = (int)(6 * (t / 0.25f));
                for (int cr = 0; cr < crackCount; cr++) {
                    float cAngle2 = (float)(crackRng.NextDouble() * Math.PI * 2);
                    float cLen = baseR * 0.5f + (float)crackRng.NextDouble() * baseR;
                    float cx1 = cx + (float)Math.Cos(cAngle2) * baseR * 0.3f;
                    float cy1 = cy + (float)Math.Sin(cAngle2) * baseR * 0.3f * 0.45f;
                    float crX = cx1, crY = cy1;
                    for (int seg = 0; seg < 3; seg++) {
                        float nx = crX + (float)Math.Cos(cAngle2 + (crackRng.NextDouble() - 0.5) * 1.5) * (cLen / 3);
                        float ny = crY + (float)Math.Sin(cAngle2 + (crackRng.NextDouble() - 0.5) * 1.5) * (cLen / 3) * 0.45f;
                        int cra = (int)(a * 0.6f * (1f - t / 0.25f));
                        using (var pen = new Pen(Color.FromArgb(cra, 180, 220, 255), 1.2f))
                            g.DrawLine(pen, crX, crY, nx, ny);
                        crX = nx; crY = ny;
                    }
                }
            }

            // === COLLAPSE BURST ===
            if (t > 0.78f && t < 0.88f) {
                float burstT = (t - 0.78f) / 0.10f;
                float burstR = baseR * (1f + burstT * 2f);
                int burstA = (int)(a * 0.4f * (1f - burstT));
                using (var b = new SolidBrush(Color.FromArgb(burstA, 150, 200, 255)))
                    g.FillEllipse(b, cx - burstR, cy - burstR * 0.45f, burstR * 2, burstR * 0.9f);
            }
        }
        static void PaintTimeTraveler(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            int a = (int)(210 * fade);

            // Phase: 0-0.10 materialize (clock fades in with temporal distortion)
            //        0.10-0.85 traveling (moving, clock spinning, time echoes trail behind)
            //        0.85-1.0 dematerialize (fades with temporal flash)
            float bodyFade;
            if (t < 0.10f) bodyFade = t / 0.10f;
            else if (t < 0.85f) bodyFade = 1f;
            else bodyFade = (1f - t) / 0.15f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            float spin = t * 300; // clock hand spin speed
            float spinRad = (float)(spin * Math.PI / 180);

            // === TEMPORAL DISTORTION FIELD — warped space around traveler ===
            for (int df = 3; df >= 1; df--) {
                float dfR = 10 + df * 7;
                float warp = (float)Math.Sin(t * 25 + df * 2) * 3;
                int dfA = (int)(a * 0.06f * bodyFade / df);
                if (dfA > 0)
                    using (var b = new SolidBrush(Color.FromArgb(dfA, 200, 180, 100)))
                        g.FillEllipse(b, cx - dfR + warp, cy - dfR * 0.7f, dfR * 2, dfR * 1.4f);
            }

            // === POCKET WATCH BODY — outer ring with engravings ===
            float watchR = 7 * bodyFade;
            if (watchR > 0.5f) {
                // Watch case glow
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.15f * bodyFade), 220, 200, 120)))
                    g.FillEllipse(b, cx - watchR * 1.8f, cy - watchR * 1.8f, watchR * 3.6f, watchR * 3.6f);

                // Outer ring
                using (var p = new Pen(Color.FromArgb((int)(a * 0.9f * bodyFade), 200, 180, 100), 1.5f))
                    g.DrawEllipse(p, cx - watchR, cy - watchR, watchR * 2, watchR * 2);

                // Inner ring
                using (var p = new Pen(Color.FromArgb((int)(a * 0.6f * bodyFade), 180, 160, 80), 0.8f))
                    g.DrawEllipse(p, cx - watchR * 0.75f, cy - watchR * 0.75f, watchR * 1.5f, watchR * 1.5f);

                // Watch face — dark interior
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f * bodyFade), 30, 25, 15)))
                    g.FillEllipse(b, cx - watchR * 0.7f, cy - watchR * 0.7f, watchR * 1.4f, watchR * 1.4f);

                // Hour markers (12 tick marks)
                for (int hr = 0; hr < 12; hr++) {
                    float hrAngle = (float)(hr * Math.PI * 2 / 12);
                    float innerD = watchR * 0.6f, outerD = watchR * 0.78f;
                    float ix = cx + (float)Math.Cos(hrAngle) * innerD;
                    float iy = cy + (float)Math.Sin(hrAngle) * innerD;
                    float ox = cx + (float)Math.Cos(hrAngle) * outerD;
                    float oy = cy + (float)Math.Sin(hrAngle) * outerD;
                    int ta = (int)(a * 0.7f * bodyFade);
                    using (var p = new Pen(Color.FromArgb(ta, 220, 200, 120), 0.6f))
                        g.DrawLine(p, ix, iy, ox, oy);
                }

                // Clock hands — spinning at different rates
                float hourAngle = spinRad * 0.3f;
                float minuteAngle = spinRad;
                float secondAngle = spinRad * 4f;

                // Hour hand
                using (var p = new Pen(Color.FromArgb((int)(a * 0.8f * bodyFade), 220, 200, 120), 1.2f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx, cy, cx + (float)Math.Cos(hourAngle) * watchR * 0.35f,
                        cy + (float)Math.Sin(hourAngle) * watchR * 0.35f);
                }
                // Minute hand
                using (var p = new Pen(Color.FromArgb((int)(a * 0.7f * bodyFade), 200, 180, 100), 0.8f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx, cy, cx + (float)Math.Cos(minuteAngle) * watchR * 0.55f,
                        cy + (float)Math.Sin(minuteAngle) * watchR * 0.55f);
                }
                // Second hand — golden glowing
                float sx2 = cx + (float)Math.Cos(secondAngle) * watchR * 0.6f;
                float sy2 = cy + (float)Math.Sin(secondAngle) * watchR * 0.6f;
                using (var p = new Pen(Color.FromArgb((int)(a * 0.6f * bodyFade), 255, 220, 100), 0.5f))
                    g.DrawLine(p, cx, cy, sx2, sy2);
                // Second hand tip glow
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.4f * bodyFade), 255, 240, 150)))
                    g.FillEllipse(b, sx2 - 1.5f, sy2 - 1.5f, 3, 3);

                // Center jewel
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * bodyFade), 255, 220, 100)))
                    g.FillEllipse(b, cx - 1.2f, cy - 1.2f, 2.4f, 2.4f);
            }

            // === CHAIN — links trailing behind ===
            float dir = ev.DirX > 0 ? -1 : 1;
            for (int link = 0; link < 6; link++) {
                float ld = (link + 1) * 4;
                float lx = cx + dir * ld + (float)Math.Sin(t * 20 + link) * 1.5f;
                float ly = cy + link * 1.5f + (float)Math.Sin(t * 15 + link * 0.8f) * 1f;
                int la = (int)(a * 0.5f * bodyFade * (1f - link * 0.12f));
                if (la > 0)
                    using (var p = new Pen(Color.FromArgb(la, 180, 160, 90), 0.6f))
                        g.DrawEllipse(p, lx - 1.5f, ly - 1f, 3, 2);
            }

            // === TIME ECHOES — ghostly afterimages at past positions ===
            for (int echo = 1; echo <= 8; echo++) {
                float echoT = t - echo * 0.015f;
                if (echoT < 0) continue;
                float ex = ev.X * w + ev.DirX * echoT * w * 1.4f;
                float ey = ev.Y * h + ev.DirY * echoT * h * 1.4f;
                float echoFade = (1f - echo * 0.11f) * bodyFade;
                int ea = (int)(a * 0.15f * echoFade);
                if (ea <= 0) continue;
                float er = 5 * echoFade;
                // Echo distortion — each echo slightly warped
                using (var p = new Pen(Color.FromArgb(ea, 180, 170, 120), 0.6f))
                    g.DrawEllipse(p, ex - er, ey - er, er * 2, er * 2);
                // Echo clock hands frozen at past angles
                float pastSpin = echoT * 300;
                float pastAngle = (float)(pastSpin * Math.PI / 180);
                using (var p = new Pen(Color.FromArgb(ea / 2, 200, 190, 130), 0.4f))
                    g.DrawLine(p, ex, ey, ex + (float)Math.Cos(pastAngle) * er * 0.5f,
                        ey + (float)Math.Sin(pastAngle) * er * 0.5f);
            }

            // === TEMPORAL PARTICLES — golden motes swirling around ===
            var rng = new Random(ev.Seed);
            for (int p2 = 0; p2 < 10; p2++) {
                float pAngle = (float)(rng.NextDouble() * Math.PI * 2) + t * 15;
                float pDist = 8 + (float)rng.NextDouble() * 12;
                float pBob = (float)Math.Sin(t * 20 + p2 * 1.7f) * 3;
                float px = cx + (float)Math.Cos(pAngle) * pDist;
                float py = cy + (float)Math.Sin(pAngle) * pDist * 0.6f + pBob;
                float pBright = 0.4f + 0.6f * (float)Math.Max(0, Math.Sin(t * 30 + p2 * 2.1f));
                int pa = (int)(a * 0.4f * pBright * bodyFade);
                if (pa <= 0) continue;
                using (var b = new SolidBrush(Color.FromArgb(pa, 255, 230, 130)))
                    g.FillEllipse(b, px - 1, py - 1, 2, 2);
            }

            // === MATERIALIZE FLASH ===
            if (t < 0.12f) {
                float flashT = t / 0.12f;
                float flashR = 15 * (1f - flashT);
                int flashA = (int)(a * 0.35f * (1f - flashT));
                using (var b = new SolidBrush(Color.FromArgb(flashA, 220, 200, 120)))
                    g.FillEllipse(b, cx - flashR, cy - flashR, flashR * 2, flashR * 2);
            }

            // === DEMATERIALIZE FLASH ===
            if (t > 0.87f) {
                float dT = (t - 0.87f) / 0.13f;
                float dR = 5 + dT * 25;
                int dA = (int)(a * 0.3f * (1f - dT));
                using (var b = new SolidBrush(Color.FromArgb(dA, 240, 220, 140)))
                    g.FillEllipse(b, cx - dR, cy - dR, dR * 2, dR * 2);
            }
        }
        static void PaintSpacePirate(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            int a = (int)(210 * fade);
            float bob = (float)Math.Sin(t * 20) * 2.5f;
            cy += bob;
            float dir = ev.DirX > 0 ? 1 : -1;

            // Phase fading
            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            // === ENGINE GLOW — exhaust flare behind ship ===
            float exX = cx - dir * 14;
            float flicker = 0.6f + 0.4f * (float)Math.Sin(t * 80);
            for (int gl = 3; gl >= 1; gl--) {
                float gr = 3 + gl * 4;
                int ga = (int)(a * 0.08f * flicker * bodyFade / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 255, 120, 30)))
                        g.FillEllipse(b, exX - gr - dir * gl * 2, cy - gr * 0.5f, gr * 2, gr);
            }
            // Engine core
            int ecA = (int)(a * 0.7f * flicker * bodyFade);
            using (var b = new SolidBrush(Color.FromArgb(ecA, 255, 180, 50)))
                g.FillEllipse(b, exX - 2, cy - 1.5f, 4, 3);
            using (var b = new SolidBrush(Color.FromArgb(ecA / 2, 255, 255, 200)))
                g.FillEllipse(b, exX - 1, cy - 0.8f, 2, 1.6f);

            // === HULL — sleek dark body with plating ===
            // Main hull
            PointF[] hull = {
                new PointF(cx + dir * 12, cy),
                new PointF(cx + dir * 4, cy - 4),
                new PointF(cx - dir * 8, cy - 3),
                new PointF(cx - dir * 12, cy),
                new PointF(cx - dir * 8, cy + 3),
                new PointF(cx + dir * 4, cy + 3)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.85f * bodyFade), 50, 40, 35)))
                g.FillPolygon(b, hull);

            // Hull plating lines
            using (var p = new Pen(Color.FromArgb((int)(a * 0.3f * bodyFade), 80, 65, 50), 0.5f)) {
                g.DrawLine(p, cx - dir * 2, cy - 3, cx - dir * 2, cy + 2.5f);
                g.DrawLine(p, cx + dir * 4, cy - 3.5f, cx + dir * 4, cy + 2.5f);
            }

            // Hull edge highlight
            using (var p = new Pen(Color.FromArgb((int)(a * 0.4f * bodyFade), 100, 80, 60), 0.6f))
                g.DrawLine(p, cx + dir * 12, cy, cx + dir * 4, cy - 4);

            // === SAIL — tattered, billowing ===
            float sailWave = (float)Math.Sin(t * 25) * 3;
            float sailWave2 = (float)Math.Sin(t * 18 + 1) * 2;
            // Mast
            using (var p = new Pen(Color.FromArgb((int)(a * 0.7f * bodyFade), 90, 75, 55), 0.8f))
                g.DrawLine(p, cx, cy - 3.5f, cx, cy - 16);

            // Main sail
            PointF[] sail = {
                new PointF(cx + 1, cy - 14),
                new PointF(cx + dir * 8 + sailWave, cy - 10 + sailWave2),
                new PointF(cx + dir * 7 + sailWave * 0.6f, cy - 5),
                new PointF(cx + 1, cy - 4)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * bodyFade), 35, 30, 40)))
                g.FillPolygon(b, sail);

            // Sail tatter marks
            using (var p = new Pen(Color.FromArgb((int)(a * 0.3f * bodyFade), 55, 45, 55), 0.4f)) {
                g.DrawLine(p, cx + dir * 3 + sailWave * 0.3f, cy - 12, cx + dir * 5 + sailWave * 0.5f, cy - 8);
                g.DrawLine(p, cx + dir * 2, cy - 8, cx + dir * 4 + sailWave * 0.4f, cy - 6);
            }

            // === SKULL EMBLEM on sail ===
            float skX = cx + dir * 4 + sailWave * 0.4f, skY = cy - 9.5f + sailWave2 * 0.3f;
            int skA = (int)(a * 0.5f * bodyFade);
            using (var b = new SolidBrush(Color.FromArgb(skA, 200, 200, 200)))
                g.FillEllipse(b, skX - 1.5f, skY - 1.5f, 3, 3);
            using (var p = new Pen(Color.FromArgb(skA, 200, 200, 200), 0.4f)) {
                g.DrawLine(p, skX - 2, skY + 1.5f, skX + 2, skY + 1.5f);
                g.DrawLine(p, skX - 1.5f, skY + 2, skX + 1.5f, skY + 2);
            }

            // === CROW'S NEST LANTERN — flickering ===
            float lanternFlicker = 0.5f + 0.5f * (float)Math.Sin(t * 90);
            int lnA = (int)(a * 0.7f * lanternFlicker * bodyFade);
            using (var b = new SolidBrush(Color.FromArgb(lnA, 255, 200, 80)))
                g.FillEllipse(b, cx - 1, cy - 17, 2, 2);
            using (var b = new SolidBrush(Color.FromArgb(lnA / 3, 255, 200, 80)))
                g.FillEllipse(b, cx - 3, cy - 19, 6, 6);

            // === CANNON FLASHES — periodic ===
            float cannonCycle = (t * 8) % 1.0f;
            if (cannonCycle < 0.08f) {
                float flashT = cannonCycle / 0.08f;
                float flashR = 4 * (1f - flashT);
                int flashA = (int)(a * 0.6f * (1f - flashT) * bodyFade);
                float flashX = cx + dir * 6, flashY = cy + 1;
                using (var b = new SolidBrush(Color.FromArgb(flashA, 255, 200, 80)))
                    g.FillEllipse(b, flashX - flashR, flashY - flashR, flashR * 2, flashR * 2);
                // Cannonball
                float cbDist = flashT * 20;
                int cbA = (int)(a * 0.5f * (1f - flashT) * bodyFade);
                using (var b = new SolidBrush(Color.FromArgb(cbA, 180, 180, 180)))
                    g.FillEllipse(b, flashX + dir * cbDist - 1, flashY - 1, 2, 2);
            }

            // === SMOKE TRAIL ===
            var rng = new Random(ev.Seed + (int)(t * 15));
            for (int sm = 0; sm < 8; sm++) {
                float smLife = (t * 3 + sm * 0.12f) % 1.0f;
                float smX = cx - dir * (10 + smLife * 25) + (float)(rng.NextDouble() - 0.5) * 8;
                float smY = cy - 2 - smLife * 8 + (float)(rng.NextDouble() - 0.5) * 4;
                float smFade = smLife < 0.2f ? smLife / 0.2f : (1f - smLife) / 0.8f;
                int smA = (int)(a * 0.08f * smFade * bodyFade);
                float smR = 2 + smLife * 5;
                if (smA > 0)
                    using (var b = new SolidBrush(Color.FromArgb(smA, 100, 90, 80)))
                        g.FillEllipse(b, smX - smR, smY - smR, smR * 2, smR * 2);
            }

            // === PLUNDER SPARKLES — gold dust trail ===
            for (int sp = 0; sp < 6; sp++) {
                float spLife = (t * 4 + sp * 0.15f) % 1.0f;
                float spX = cx - dir * (5 + spLife * 30);
                float spY = cy + 2 + spLife * 6 + (float)Math.Sin(t * 40 + sp * 2) * 3;
                float spFade = (1f - spLife);
                int spA = (int)(a * 0.35f * spFade * bodyFade);
                if (spA > 0)
                    using (var b = new SolidBrush(Color.FromArgb(spA, 255, 220, 80)))
                        g.FillEllipse(b, spX - 0.8f, spY - 0.8f, 1.6f, 1.6f);
            }
        }
        static void PaintCrystalDragon(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            int a = (int)(210 * fade);
            float dir = ev.DirX > 0 ? 1 : -1;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            float undulate = (float)Math.Sin(t * 25) * 2;

            // === CRYSTAL BODY — prismatic segmented serpent ===
            float[] segX = new float[16], segY = new float[16];
            for (int seg = 0; seg < 16; seg++) {
                float d = seg * 4.5f;
                segX[seg] = cx - dir * d;
                segY[seg] = cy + (float)Math.Sin(t * 28 - seg * 0.7f) * (3 + seg * 0.15f) + undulate * (seg * 0.1f);
            }

            // Body glow aura
            for (int seg = 0; seg < 16; seg++) {
                float auraR = (6 - seg * 0.2f) * bodyFade;
                int auraA = (int)(a * 0.04f * bodyFade * (1f - seg * 0.04f));
                Color auraColor = seg % 3 == 0 ? Color.FromArgb(auraA, 120, 200, 255)
                    : seg % 3 == 1 ? Color.FromArgb(auraA, 180, 140, 255) : Color.FromArgb(auraA, 255, 180, 220);
                if (auraA > 0)
                    using (var b = new SolidBrush(auraColor))
                        g.FillEllipse(b, segX[seg] - auraR, segY[seg] - auraR, auraR * 2, auraR * 2);
            }

            // Crystal segments — faceted gem-like body
            for (int seg = 15; seg >= 0; seg--) {
                float sz = (4.5f - seg * 0.2f) * bodyFade;
                int sa = (int)(a * (1f - seg * 0.04f) * bodyFade);
                if (sa <= 0 || sz <= 0) continue;

                // Prismatic color cycling
                float hueShift = t * 15 + seg * 0.8f;
                int cr, cg, cb;
                float phase = (hueShift % 3f) / 3f;
                if (phase < 0.33f) {
                    float bl = phase / 0.33f;
                    cr = (int)(120 + 60 * (1 - bl)); cg = (int)(180 + 40 * bl); cb = 255;
                } else if (phase < 0.66f) {
                    float bl = (phase - 0.33f) / 0.33f;
                    cr = (int)(140 + 80 * bl); cg = (int)(160 - 20 * bl); cb = (int)(255 - 55 * bl);
                } else {
                    float bl = (phase - 0.66f) / 0.34f;
                    cr = (int)(220 - 100 * bl); cg = (int)(140 + 40 * bl); cb = (int)(200 + 55 * bl);
                }
                cr = Math.Max(0, Math.Min(255, cr));
                cg = Math.Max(0, Math.Min(255, cg));
                cb = Math.Max(0, Math.Min(255, cb));

                // Main crystal segment
                using (var b = new SolidBrush(Color.FromArgb(sa, cr, cg, cb)))
                    g.FillEllipse(b, segX[seg] - sz, segY[seg] - sz * 0.6f, sz * 2, sz * 1.2f);

                // Crystal facet highlight — bright line across each segment
                if (seg < 12) {
                    float facetPulse = 0.3f + 0.7f * (float)Math.Max(0, Math.Sin(t * 40 + seg * 1.5f));
                    int fa = (int)(sa * 0.5f * facetPulse);
                    using (var b = new SolidBrush(Color.FromArgb(fa, 255, 255, 255)))
                        g.FillEllipse(b, segX[seg] - sz * 0.3f, segY[seg] - sz * 0.25f, sz * 0.8f, sz * 0.3f);
                }

                // Crystal spines — jutting crystals on alternate segments
                if (seg % 3 == 0 && seg < 12) {
                    float spineLen = 4 + (float)Math.Sin(t * 20 + seg) * 1.5f;
                    float spineAngle = (float)(Math.PI * -0.4 + Math.Sin(t * 10 + seg * 0.5f) * 0.2);
                    float spX = segX[seg] + (float)Math.Cos(spineAngle) * spineLen;
                    float spY = segY[seg] + (float)Math.Sin(spineAngle) * spineLen;
                    int spA = (int)(sa * 0.6f);
                    using (var p = new Pen(Color.FromArgb(spA, cr, cg, cb), 1.2f)) {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        g.DrawLine(p, segX[seg], segY[seg], spX, spY);
                    }
                    // Spine tip glow
                    using (var b = new SolidBrush(Color.FromArgb(spA / 2, 255, 255, 255)))
                        g.FillEllipse(b, spX - 1.5f, spY - 1.5f, 3, 3);
                }
            }

            // === HEAD — angular crystal skull with glowing eyes ===
            float headSz = 5.5f * bodyFade;
            // Head crystal shape
            PointF[] head = {
                new PointF(cx + dir * 6, cy),
                new PointF(cx + dir * 2, cy - headSz * 0.8f),
                new PointF(cx - dir * 2, cy - headSz * 0.5f),
                new PointF(cx - dir * 2, cy + headSz * 0.5f),
                new PointF(cx + dir * 2, cy + headSz * 0.6f)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.85f * bodyFade), 140, 200, 255)))
                g.FillPolygon(b, head);
            using (var p = new Pen(Color.FromArgb((int)(a * 0.4f * bodyFade), 200, 230, 255), 0.6f))
                g.DrawPolygon(p, head);

            // Eyes — twin glowing gems
            float eyePulse = 0.6f + 0.4f * (float)Math.Sin(t * 50);
            int eyeA = (int)(a * 0.9f * eyePulse * bodyFade);
            for (int eye = -1; eye <= 1; eye += 2) {
                float eyeX = cx + dir * 3, eyeY = cy + eye * 1.8f;
                using (var b = new SolidBrush(Color.FromArgb(eyeA / 3, 255, 255, 255)))
                    g.FillEllipse(b, eyeX - 3, eyeY - 3, 6, 6);
                using (var b = new SolidBrush(Color.FromArgb(eyeA, 255, 255, 255)))
                    g.FillEllipse(b, eyeX - 1.2f, eyeY - 1.2f, 2.4f, 2.4f);
            }

            // === HORN / CROWN CRYSTALS ===
            for (int horn = 0; horn < 3; horn++) {
                float hAngle = (float)(-Math.PI * 0.3 + horn * 0.25f);
                float hLen = (6 + horn * 2) * bodyFade;
                float hx = cx + dir * 2 + (float)Math.Cos(hAngle) * hLen * dir;
                float hy = cy - headSz * 0.5f + (float)Math.Sin(hAngle) * hLen;
                int ha = (int)(a * 0.7f * bodyFade);
                using (var p = new Pen(Color.FromArgb(ha, 180, 220, 255), 1.0f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx + dir * 2, cy - headSz * 0.4f, hx, hy);
                }
                using (var b = new SolidBrush(Color.FromArgb(ha / 2, 220, 240, 255)))
                    g.FillEllipse(b, hx - 1.5f, hy - 1.5f, 3, 3);
            }

            // === BREATH — prismatic crystal mist ===
            var rng = new Random(ev.Seed + (int)(t * 20));
            for (int br = 0; br < 8; br++) {
                float brLife = (t * 5 + br * 0.12f) % 1.0f;
                float brDist = brLife * 18;
                float brSpread = brLife * 8;
                float bx = cx + dir * (7 + brDist) + (float)(rng.NextDouble() - 0.5) * brSpread;
                float by = cy + (float)(rng.NextDouble() - 0.5) * brSpread;
                float brFade = brLife < 0.15f ? brLife / 0.15f : (1f - brLife) / 0.85f;
                int bra = (int)(a * 0.25f * brFade * bodyFade);
                if (bra <= 0) continue;
                float bsz = 1.5f + brLife * 3;
                Color brColor = br % 3 == 0 ? Color.FromArgb(bra, 150, 220, 255)
                    : br % 3 == 1 ? Color.FromArgb(bra, 200, 160, 255) : Color.FromArgb(bra, 255, 200, 230);
                using (var b = new SolidBrush(brColor))
                    g.FillEllipse(b, bx - bsz, by - bsz, bsz * 2, bsz * 2);
            }

            // === LIGHT REFRACTION SPARKLES — prismatic scatter ===
            var sparkRng = new Random(ev.Seed + 100);
            for (int sp = 0; sp < 6; sp++) {
                int srcSeg = sparkRng.Next(12);
                float spBright = (float)Math.Max(0, Math.Sin(t * 35 + sp * 2.7f));
                if (spBright < 0.4f) continue;
                float spDist = 5 + (float)sparkRng.NextDouble() * 8;
                float spAngle = (float)(sparkRng.NextDouble() * Math.PI * 2);
                float spx = segX[srcSeg] + (float)Math.Cos(spAngle) * spDist;
                float spy = segY[srcSeg] + (float)Math.Sin(spAngle) * spDist;
                int spa = (int)(a * 0.4f * spBright * bodyFade);
                using (var b = new SolidBrush(Color.FromArgb(spa, 255, 255, 255)))
                    g.FillEllipse(b, spx - 1, spy - 1, 2, 2);
                // Tiny refraction cross
                using (var p = new Pen(Color.FromArgb(spa / 2, 200, 220, 255), 0.4f)) {
                    g.DrawLine(p, spx - 2, spy, spx + 2, spy);
                    g.DrawLine(p, spx, spy - 2, spx, spy + 2);
                }
            }
        }
        static void PaintQuantumRift(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(210 * fade);
            float openAmount;
            if (t < 0.15f) openAmount = t / 0.15f;
            else if (t < 0.80f) openAmount = 1f;
            else openAmount = (1f - t) / 0.20f;
            openAmount = Math.Max(0, Math.Min(1, openAmount));
            if (openAmount <= 0.01f) return;

            var rng = new Random(ev.Seed);

            // Generate CRACK PATH — jagged zigzag
            int crackSegs = 12;
            float[] crackX = new float[crackSegs], crackY = new float[crackSegs];
            float crackLen = 50 * openAmount;
            float crackAngle = (float)(rng.NextDouble() * Math.PI * 0.4 - 0.2 + Math.PI * 0.5);
            for (int i = 0; i < crackSegs; i++) {
                float segT = (i - crackSegs / 2f) / (crackSegs / 2f);
                float baseX = cx + (float)Math.Cos(crackAngle) * segT * crackLen;
                float baseY = cy + (float)Math.Sin(crackAngle) * segT * crackLen;
                float perpAngle = crackAngle + (float)(Math.PI / 2);
                float jag = (float)(rng.NextDouble() - 0.5) * 16 * openAmount;
                crackX[i] = baseX + (float)Math.Cos(perpAngle) * jag;
                crackY[i] = baseY + (float)Math.Sin(perpAngle) * jag;
            }

            // === LIGHT BLEEDING THROUGH ===
            for (int i = 0; i < crackSegs - 1; i++) {
                float midX = (crackX[i] + crackX[i + 1]) / 2;
                float midY = (crackY[i] + crackY[i + 1]) / 2;
                float glowPulse = 0.6f + 0.4f * (float)Math.Sin(t * 20 + i * 1.3f);
                float glowR = (8 + glowPulse * 6) * openAmount;
                int ga = (int)(a * 0.08f * glowPulse);
                using (var b = new SolidBrush(Color.FromArgb(ga, 140, 60, 255)))
                    g.FillEllipse(b, midX - glowR, midY - glowR, glowR * 2, glowR * 2);
                float innerR = glowR * 0.4f;
                using (var b = new SolidBrush(Color.FromArgb(ga * 2, 180, 160, 255)))
                    g.FillEllipse(b, midX - innerR, midY - innerR, innerR * 2, innerR * 2);
            }

            // === THE CRACK — outer violet, inner white-blue ===
            for (int i = 0; i < crackSegs - 1; i++) {
                int lineA = (int)(a * 0.6f * openAmount);
                using (var p = new Pen(Color.FromArgb(lineA, 160, 100, 255), 3f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, crackX[i], crackY[i], crackX[i + 1], crackY[i + 1]);
                }
            }
            for (int i = 0; i < crackSegs - 1; i++) {
                int lineA = (int)(a * 0.9f * openAmount);
                using (var p = new Pen(Color.FromArgb(lineA, 220, 210, 255), 1.2f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, crackX[i], crackY[i], crackX[i + 1], crackY[i + 1]);
                }
            }

            // === BRANCHING FRACTURES ===
            var branchRng = new Random(ev.Seed + 500);
            for (int br = 0; br < 6; br++) {
                int attachPt = branchRng.Next(2, crackSegs - 2);
                float brAngle = crackAngle + (float)(Math.PI / 2) * (branchRng.NextDouble() > 0.5 ? 1 : -1);
                brAngle += (float)(branchRng.NextDouble() - 0.5) * 0.8f;
                float brLen = (8 + (float)branchRng.NextDouble() * 15) * openAmount;
                float bx1 = crackX[attachPt], by1 = crackY[attachPt];
                float bx2 = bx1 + (float)Math.Cos(brAngle) * brLen * 0.5f + (float)(branchRng.NextDouble() - 0.5) * 6;
                float by2 = by1 + (float)Math.Sin(brAngle) * brLen * 0.5f;
                float bx3 = bx2 + (float)Math.Cos(brAngle) * brLen * 0.5f + (float)(branchRng.NextDouble() - 0.5) * 4;
                float by3 = by2 + (float)Math.Sin(brAngle) * brLen * 0.5f;
                int bra = (int)(a * 0.4f * openAmount);
                using (var p = new Pen(Color.FromArgb(bra, 150, 100, 255), 1.5f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, bx1, by1, bx2, by2);
                    g.DrawLine(p, bx2, by2, bx3, by3);
                }
                using (var b = new SolidBrush(Color.FromArgb(bra / 4, 180, 140, 255)))
                    g.FillEllipse(b, bx2 - 4, by2 - 4, 8, 8);
            }

            // === PARTICLES ESCAPING ===
            var ptRng = new Random(ev.Seed + (int)(t * 30));
            for (int pt = 0; pt < 12; pt++) {
                int srcSeg = ptRng.Next(crackSegs);
                float ptLife = (t * 2f + (float)ptRng.NextDouble()) % 1.0f;
                float perpAng = crackAngle + (float)(Math.PI / 2) * (ptRng.NextDouble() > 0.5 ? 1 : -1);
                float ptDist = ptLife * 30 * openAmount;
                float ptDrift = (float)(ptRng.NextDouble() - 0.5) * 15;
                float px = crackX[srcSeg] + (float)Math.Cos(perpAng) * ptDist + ptDrift;
                float py = crackY[srcSeg] + (float)Math.Sin(perpAng) * ptDist;
                float ptFade = ptLife < 0.1f ? ptLife / 0.1f : (1f - ptLife);
                int pa = (int)(a * 0.5f * ptFade * openAmount);
                if (pa <= 0) continue;
                float ptSz = 1.5f + (1f - ptLife) * 2f;
                Color ptColor = pt % 3 == 0 ? Color.FromArgb(pa, 180, 130, 255)
                    : pt % 3 == 1 ? Color.FromArgb(pa, 130, 180, 255) : Color.FromArgb(pa, 255, 200, 255);
                using (var b = new SolidBrush(ptColor))
                    g.FillEllipse(b, px - ptSz, py - ptSz, ptSz * 2, ptSz * 2);
            }

            // === DESTABILIZATION FLICKER ===
            if (t > 0.50f && t < 0.80f) {
                float flickerPhase = (t - 0.50f) / 0.30f;
                var flickRng = new Random(ev.Seed + (int)(t * 80));
                if (flickRng.NextDouble() < 0.4f + flickerPhase * 0.4f) {
                    int flashSeg = flickRng.Next(crackSegs);
                    float flashR = 6 + (float)flickRng.NextDouble() * 10;
                    int flashA = (int)(a * 0.5f * (float)flickRng.NextDouble());
                    using (var b = new SolidBrush(Color.FromArgb(flashA, 200, 180, 255)))
                        g.FillEllipse(b, crackX[flashSeg] - flashR, crackY[flashSeg] - flashR, flashR * 2, flashR * 2);
                }
            }

            // === SEAL FLASH ===
            if (t > 0.80f && t < 0.92f) {
                float sealT = (t - 0.80f) / 0.12f;
                float flashR = 20 + sealT * 30;
                int flashA = (int)(a * 0.35f * (1f - sealT));
                using (var b = new SolidBrush(Color.FromArgb(flashA, 180, 160, 255)))
                    g.FillEllipse(b, cx - flashR, cy - flashR, flashR * 2, flashR * 2);
            }
        }
        static void PaintCosmicDancer(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            int a = (int)(210 * fade);
            float spin = t * 120;
            float spinRad = (float)(spin * Math.PI / 180);

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            // === RIBBON TRAIL — flowing silk ribbons that trace the dance ===
            for (int rib = 0; rib < 2; rib++) {
                float ribOffset = rib * (float)Math.PI;
                for (int seg = 0; seg < 20; seg++) {
                    float segT = seg / 20f;
                    float pastT = t - seg * 0.008f;
                    if (pastT < 0) continue;
                    float pastSpin = pastT * 120 * (float)Math.PI / 180;
                    float ribbonR = 10 + (float)Math.Sin(pastT * 30 + rib * 3) * 4;
                    float rx = cx + (float)Math.Cos(pastSpin * 2 + ribOffset) * ribbonR;
                    float ry = cy + (float)Math.Sin(pastSpin * 3 + ribOffset) * ribbonR * 0.6f;
                    float ribFade = (1f - segT) * bodyFade;
                    int ra = (int)(a * 0.25f * ribFade);
                    if (ra <= 0) continue;
                    Color ribColor = rib == 0 ? Color.FromArgb(ra, 255, 180, 220) : Color.FromArgb(ra, 180, 200, 255);
                    float rsz = (2.5f - segT * 1.5f);
                    using (var b = new SolidBrush(ribColor))
                        g.FillEllipse(b, rx - rsz, ry - rsz * 0.4f, rsz * 2, rsz * 0.8f);
                }
            }

            // === AURA — pulsing glow around dancer ===
            float auraPulse = 0.5f + 0.5f * (float)Math.Sin(t * 15);
            for (int gl = 3; gl >= 1; gl--) {
                float gr = 8 + gl * 5;
                int ga = (int)(a * 0.05f * auraPulse * bodyFade / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 255, 200, 240)))
                        g.FillEllipse(b, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            // === BODY — elegant figure in mid-pirouette ===
            // Torso
            float torsoBend = (float)Math.Sin(t * 25) * 0.3f;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * bodyFade), 255, 200, 230)))
                g.FillEllipse(b, cx - 2, cy - 5 + torsoBend, 4, 9);

            // Head
            float headBob = (float)Math.Sin(t * 30) * 1.5f;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * bodyFade), 255, 210, 235)))
                g.FillEllipse(b, cx - 2.2f, cy - 8 + headBob, 4.4f, 4);

            // Arms — graceful arc positions that rotate
            for (int arm = -1; arm <= 1; arm += 2) {
                float armAngle = spinRad * 0.8f + arm * 1.2f + (float)Math.Sin(t * 20) * 0.5f;
                float armLen = 8;
                float elbowX = cx + (float)Math.Cos(armAngle) * armLen * 0.5f;
                float elbowY = cy - 3 + (float)Math.Sin(armAngle) * armLen * 0.3f;
                float handX = elbowX + (float)Math.Cos(armAngle + arm * 0.8f) * armLen * 0.5f;
                float handY = elbowY + (float)Math.Sin(armAngle + arm * 0.8f) * armLen * 0.3f;
                int armA = (int)(a * 0.7f * bodyFade);
                using (var p = new Pen(Color.FromArgb(armA, 255, 200, 230), 0.8f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx, cy - 3, elbowX, elbowY);
                    g.DrawLine(p, elbowX, elbowY, handX, handY);
                }
                // Fingertip sparkle
                float sparkle = (float)Math.Max(0, Math.Sin(t * 50 + arm * 3));
                int spA = (int)(a * 0.5f * sparkle * bodyFade);
                if (spA > 0)
                    using (var b = new SolidBrush(Color.FromArgb(spA, 255, 255, 220)))
                        g.FillEllipse(b, handX - 1.5f, handY - 1.5f, 3, 3);
            }

            // Legs — en pointe position
            float legPhase = (float)Math.Sin(t * 22);
            for (int leg = -1; leg <= 1; leg += 2) {
                float legAngle = leg * 0.4f + legPhase * 0.3f * leg;
                float kneeX = cx + (float)Math.Sin(legAngle) * 3;
                float kneeY = cy + 6;
                float footX = kneeX + (float)Math.Sin(legAngle + leg * 0.5f) * 2;
                float footY = cy + 11;
                int legA = (int)(a * 0.7f * bodyFade);
                using (var p = new Pen(Color.FromArgb(legA, 255, 190, 220), 0.8f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx, cy + 3, kneeX, kneeY);
                    g.DrawLine(p, kneeX, kneeY, footX, footY);
                }
            }

            // === STARDUST SCATTER — sparkles flung from movement ===
            var rng = new Random(ev.Seed);
            for (int sd = 0; sd < 12; sd++) {
                float sdAngle = (float)(rng.NextDouble() * Math.PI * 2) + t * 10;
                float sdDist = 12 + (float)rng.NextDouble() * 15;
                float sdPulse = (float)Math.Max(0, Math.Sin(t * 25 + sd * 2.3f));
                if (sdPulse < 0.3f) continue;
                float sdx = cx + (float)Math.Cos(sdAngle) * sdDist;
                float sdy = cy + (float)Math.Sin(sdAngle) * sdDist * 0.7f;
                int sda = (int)(a * 0.3f * sdPulse * bodyFade);
                Color sdColor = sd % 3 == 0 ? Color.FromArgb(sda, 255, 220, 255) :
                    sd % 3 == 1 ? Color.FromArgb(sda, 220, 200, 255) : Color.FromArgb(sda, 255, 255, 220);
                using (var b = new SolidBrush(sdColor))
                    g.FillEllipse(b, sdx - 1, sdy - 1, 2, 2);
            }

            // === AFTERIMAGE ECHO — faint silhouettes of past poses ===
            for (int echo = 1; echo <= 5; echo++) {
                float et = t - echo * 0.02f;
                if (et < 0) continue;
                float ex = ev.X * w + ev.DirX * et * w * 1.4f;
                float ey = ev.Y * h + ev.DirY * et * h * 1.4f;
                int ea = (int)(a * 0.08f * (1f - echo * 0.17f) * bodyFade);
                if (ea <= 0) continue;
                using (var b = new SolidBrush(Color.FromArgb(ea, 255, 180, 220)))
                    g.FillEllipse(b, ex - 3, ey - 5, 6, 12);
            }
        }
        static void PaintPlasmaSnake(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            int a = (int)(210 * fade);
            float dir = ev.DirX > 0 ? 1 : -1;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            int segCount = 24;
            float[] segX = new float[segCount], segY = new float[segCount];
            for (int seg = 0; seg < segCount; seg++) {
                float d = seg * 3.5f;
                segX[seg] = cx - dir * d;
                segY[seg] = cy + (float)Math.Sin(t * 35 - seg * 0.55f) * (5 + seg * 0.25f);
            }

            // === PLASMA GLOW FIELD — electric aura around body ===
            for (int seg = 0; seg < segCount; seg += 2) {
                float pulse = 0.5f + 0.5f * (float)Math.Sin(t * 60 + seg * 1.2f);
                float glowR = (5 + pulse * 4) * bodyFade;
                int ga = (int)(a * 0.04f * pulse * bodyFade);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 50, 255, 150)))
                        g.FillEllipse(b, segX[seg] - glowR, segY[seg] - glowR, glowR * 2, glowR * 2);
            }

            // === BODY SEGMENTS — pulsing plasma orbs ===
            for (int seg = segCount - 1; seg >= 0; seg--) {
                float sz = (3.5f - seg * 0.1f) * bodyFade;
                float pulse = 0.6f + 0.4f * (float)Math.Sin(t * 70 + seg * 0.8f);
                int sa = (int)(a * (1f - seg * 0.03f) * bodyFade * pulse);
                if (sa <= 0 || sz <= 0) continue;

                // Plasma color — shifts from green to teal along body
                int pr = (int)(30 + seg * 1.5f);
                int pg = (int)(255 - seg * 3);
                int pb = (int)(130 + seg * 4);
                pr = Math.Max(0, Math.Min(255, pr));
                pg = Math.Max(0, Math.Min(255, pg));
                pb = Math.Max(0, Math.Min(255, pb));

                // Outer glow per segment
                using (var b = new SolidBrush(Color.FromArgb(sa / 4, pr, pg, pb)))
                    g.FillEllipse(b, segX[seg] - sz * 1.5f, segY[seg] - sz * 0.8f, sz * 3, sz * 1.6f);

                // Core segment
                using (var b = new SolidBrush(Color.FromArgb(sa, pr, pg, pb)))
                    g.FillEllipse(b, segX[seg] - sz, segY[seg] - sz * 0.5f, sz * 2, sz);

                // Hot center
                using (var b = new SolidBrush(Color.FromArgb((int)(sa * 0.4f), 200, 255, 220)))
                    g.FillEllipse(b, segX[seg] - sz * 0.3f, segY[seg] - sz * 0.15f, sz * 0.6f, sz * 0.3f);
            }

            // === ELECTRICAL ARCS between segments ===
            var arcRng = new Random(ev.Seed + (int)(t * 40));
            for (int arc = 0; arc < 8; arc++) {
                int s1 = arcRng.Next(segCount - 3);
                int s2 = s1 + 1 + arcRng.Next(2);
                if (s2 >= segCount) continue;
                float arcBright = (float)arcRng.NextDouble();
                if (arcBright < 0.4f) continue;
                int arcA = (int)(a * 0.5f * arcBright * bodyFade);
                float midX = (segX[s1] + segX[s2]) / 2 + (float)(arcRng.NextDouble() - 0.5) * 6;
                float midY = (segY[s1] + segY[s2]) / 2 + (float)(arcRng.NextDouble() - 0.5) * 6;
                using (var p = new Pen(Color.FromArgb(arcA, 100, 255, 200), 0.7f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, segX[s1], segY[s1], midX, midY);
                    g.DrawLine(p, midX, midY, segX[s2], segY[s2]);
                }
            }

            // === SIDE DISCHARGE — periodic electric bolts shooting out ===
            for (int bolt = 0; bolt < 4; bolt++) {
                float boltPhase = (t * 12 + bolt * 0.25f) % 1.0f;
                if (boltPhase > 0.15f) continue;
                float boltT = boltPhase / 0.15f;
                int srcSeg = (bolt * 5 + 2) % segCount;
                if (srcSeg >= segCount) continue;
                int boltSide = bolt % 2 == 0 ? 1 : -1;
                float bLen = (8 + bolt * 3) * (1f - boltT);
                float bAngle = (float)(Math.PI / 2 * boltSide) + (float)Math.Sin(t * 100 + bolt) * 0.5f;
                float bx = segX[srcSeg] + (float)Math.Cos(bAngle) * bLen;
                float by = segY[srcSeg] + (float)Math.Sin(bAngle) * bLen;
                int ba = (int)(a * 0.6f * (1f - boltT) * bodyFade);
                using (var p = new Pen(Color.FromArgb(ba, 150, 255, 200), 1.0f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, segX[srcSeg], segY[srcSeg], bx, by);
                }
                using (var b = new SolidBrush(Color.FromArgb(ba / 2, 200, 255, 220)))
                    g.FillEllipse(b, bx - 2, by - 2, 4, 4);
            }

            // === HEAD — forked tongue with plasma eyes ===
            float headSz = 4.5f * bodyFade;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * bodyFade), 40, 220, 140)))
                g.FillEllipse(b, cx - headSz * 0.8f, cy - headSz * 0.5f, headSz * 2, headSz);

            // Eyes — glowing plasma
            float eyePulse = 0.7f + 0.3f * (float)Math.Sin(t * 60);
            for (int eye = -1; eye <= 1; eye += 2) {
                float eyeX = cx + dir * 2, eyeY = cy + eye * 1.5f;
                int eyeA = (int)(a * 0.8f * eyePulse * bodyFade);
                using (var b = new SolidBrush(Color.FromArgb(eyeA, 255, 50, 50)))
                    g.FillEllipse(b, eyeX - 1, eyeY - 0.8f, 2, 1.6f);
            }

            // Forked tongue
            float tongueFlick = (float)Math.Sin(t * 80) * 3;
            using (var p = new Pen(Color.FromArgb((int)(a * 0.6f * bodyFade), 255, 80, 80), 0.5f)) {
                g.DrawLine(p, cx + dir * 4, cy, cx + dir * 7, cy + tongueFlick * 0.5f);
                g.DrawLine(p, cx + dir * 7, cy + tongueFlick * 0.5f, cx + dir * 9, cy - 1 + tongueFlick);
                g.DrawLine(p, cx + dir * 7, cy + tongueFlick * 0.5f, cx + dir * 9, cy + 1 + tongueFlick);
            }

            // === PLASMA DRIP TRAIL ===
            for (int drip = 0; drip < 6; drip++) {
                float dLife = (t * 6 + drip * 0.15f) % 1.0f;
                int srcSeg2 = (drip * 4) % segCount;
                if (srcSeg2 >= segCount) continue;
                float dx = segX[srcSeg2] + (float)Math.Sin(t * 30 + drip) * 3;
                float dy = segY[srcSeg2] + dLife * 12;
                float dFade = (1f - dLife);
                int da = (int)(a * 0.2f * dFade * bodyFade);
                if (da > 0)
                    using (var b = new SolidBrush(Color.FromArgb(da, 80, 255, 170)))
                        g.FillEllipse(b, dx - 1, dy - 1, 2, 2);
            }
        }
        static void PaintStarSurfer(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            int a = (int)(210 * fade);
            float dir = ev.DirX > 0 ? 1 : -1;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            float lean = (float)Math.Sin(t * 22) * 4;
            float boardTilt = (float)Math.Sin(t * 18) * 0.15f;

            // === COSMIC WAKE — energy trail behind board ===
            for (int wk = 0; wk < 15; wk++) {
                float wkLife = (t * 5 + wk * 0.06f) % 1.0f;
                float wkX = cx - dir * (8 + wkLife * 35);
                float wkY = cy + 4 + (float)Math.Sin(t * 40 + wk * 1.3f) * (2 + wkLife * 4);
                float wkFade = (1f - wkLife) * bodyFade;
                int wa = (int)(a * 0.15f * wkFade);
                if (wa <= 0) continue;
                float wsz = 1.5f + wkLife * 3;
                Color wColor = wk % 3 == 0 ? Color.FromArgb(wa, 255, 220, 100) :
                    wk % 3 == 1 ? Color.FromArgb(wa, 255, 180, 80) : Color.FromArgb(wa, 255, 240, 160);
                using (var b = new SolidBrush(wColor))
                    g.FillEllipse(b, wkX - wsz, wkY - wsz * 0.5f, wsz * 2, wsz);
            }

            // === BOARD — glowing energy surfboard ===
            float boardLen = 10;
            float boardY = cy + 3;
            // Board glow
            for (int gl = 3; gl >= 1; gl--) {
                float gr = 2 + gl * 3;
                int ga = (int)(a * 0.06f * bodyFade / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 255, 200, 50)))
                        g.FillEllipse(b, cx - boardLen - gr, boardY - gr * 0.4f, (boardLen + gr) * 2, gr * 0.8f);
            }

            // Board body
            PointF[] board = {
                new PointF(cx + dir * boardLen, boardY + boardTilt * 10),
                new PointF(cx + dir * (boardLen - 2), boardY - 1.5f + boardTilt * 8),
                new PointF(cx - dir * (boardLen - 2), boardY - 1.5f - boardTilt * 8),
                new PointF(cx - dir * boardLen, boardY - boardTilt * 10),
                new PointF(cx - dir * (boardLen - 2), boardY + 1.5f - boardTilt * 8),
                new PointF(cx + dir * (boardLen - 2), boardY + 1.5f + boardTilt * 8)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.8f * bodyFade), 90, 70, 40)))
                g.FillPolygon(b, board);

            // Board edge glow
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f * bodyFade), 255, 220, 80), 0.8f))
                g.DrawPolygon(p, board);

            // Board energy stripe
            float stripePulse = 0.5f + 0.5f * (float)Math.Sin(t * 40);
            using (var p = new Pen(Color.FromArgb((int)(a * 0.4f * stripePulse * bodyFade), 255, 240, 150), 0.6f))
                g.DrawLine(p, cx - dir * (boardLen - 3), boardY, cx + dir * (boardLen - 3), boardY);

            // === SURFER FIGURE ===
            float surferX = cx + lean * 0.3f;

            // Legs — crouched stance
            using (var p = new Pen(Color.FromArgb((int)(a * 0.7f * bodyFade), 200, 200, 220), 0.8f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, surferX - 2, boardY - 1, surferX - 1, boardY - 5);
                g.DrawLine(p, surferX + 2, boardY - 1, surferX + 1, boardY - 5);
            }

            // Body
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.85f * bodyFade), 200, 200, 220)))
                g.FillEllipse(b, surferX - 2, boardY - 11, 4, 7);

            // Head
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.85f * bodyFade), 210, 210, 225)))
                g.FillEllipse(b, surferX - 2, boardY - 14, 4, 3.5f);

            // Arms — outstretched for balance
            float armWave = (float)Math.Sin(t * 28) * 2;
            using (var p = new Pen(Color.FromArgb((int)(a * 0.7f * bodyFade), 200, 200, 220), 0.8f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, surferX, boardY - 9, surferX - 6 + lean * 0.2f, boardY - 8 + armWave);
                g.DrawLine(p, surferX, boardY - 9, surferX + 6 + lean * 0.2f, boardY - 10 - armWave);
            }

            // === STARDUST SPRAY — kicked up from board ===
            var rng = new Random(ev.Seed + (int)(t * 25));
            for (int sp = 0; sp < 8; sp++) {
                float spLife = (t * 8 + sp * 0.12f) % 1.0f;
                float spAngle = (float)(-Math.PI * 0.3 + rng.NextDouble() * Math.PI * 0.6);
                float spDist = spLife * 12;
                float spx = cx - dir * boardLen + (float)Math.Cos(spAngle) * spDist * (-dir);
                float spy = boardY + (float)Math.Sin(spAngle) * spDist - spLife * 5;
                float spFade = (1f - spLife) * bodyFade;
                int spa = (int)(a * 0.3f * spFade);
                if (spa > 0)
                    using (var b = new SolidBrush(Color.FromArgb(spa, 255, 230, 120)))
                        g.FillEllipse(b, spx - 1, spy - 1, 2, 2);
            }

            // === COSMIC WIND LINES — streaking past ===
            for (int wl = 0; wl < 5; wl++) {
                float wlY = cy - 12 + wl * 6;
                float wlPhase = (t * 15 + wl * 0.3f) % 1.0f;
                float wlX = cx + dir * 20 - wlPhase * 40 * dir;
                float wlLen = 6 + (float)Math.Sin(t * 20 + wl) * 2;
                int wlA = (int)(a * 0.15f * (1f - Math.Abs(wlPhase - 0.5f) * 2) * bodyFade);
                if (wlA > 0)
                    using (var p = new Pen(Color.FromArgb(wlA, 200, 220, 255), 0.5f))
                        g.DrawLine(p, wlX, wlY, wlX - dir * wlLen, wlY);
            }
        }
        static void PaintVoidMoth(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            int a = (int)(200 * fade);
            float dir = ev.DirX > 0 ? 1 : -1;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            float wingFlap = (float)Math.Sin(t * 45); // -1 to 1, wing beat cycle
            float wingSpread = 8 + wingFlap * 6; // wing extension

            // === VOID ABSORPTION FIELD — dark zone that dims nearby stars ===
            for (int vf = 4; vf >= 1; vf--) {
                float vr = 8 + vf * 6;
                int va = (int)(a * 0.06f * bodyFade / vf);
                if (va > 0)
                    using (var b = new SolidBrush(Color.FromArgb(va, 20, 10, 40)))
                        g.FillEllipse(b, cx - vr, cy - vr, vr * 2, vr * 2);
            }

            // === WINGS — dark ethereal membranes with void patterns ===
            for (int side = -1; side <= 1; side += 2) {
                float wingTip = wingSpread * (0.8f + 0.2f * Math.Abs(wingFlap));

                // Upper wing
                PointF[] upperWing = {
                    new PointF(cx, cy - 2),
                    new PointF(cx + side * wingTip * 0.4f, cy - wingTip * 0.7f),
                    new PointF(cx + side * wingTip, cy - wingTip * 0.3f),
                    new PointF(cx + side * wingTip * 0.6f, cy)
                };
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.75f * bodyFade), 80, 50, 140)))
                    g.FillPolygon(b, upperWing);

                // Lower wing
                PointF[] lowerWing = {
                    new PointF(cx, cy + 1),
                    new PointF(cx + side * wingTip * 0.5f, cy),
                    new PointF(cx + side * wingTip * 0.8f, cy + wingTip * 0.4f),
                    new PointF(cx + side * wingTip * 0.3f, cy + wingTip * 0.5f)
                };
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.65f * bodyFade), 90, 55, 150)))
                    g.FillPolygon(b, lowerWing);

                // Wing edge glow — faint violet outline
                using (var p = new Pen(Color.FromArgb((int)(a * 0.5f * bodyFade), 160, 100, 240), 0.8f)) {
                    g.DrawLine(p, upperWing[0].X, upperWing[0].Y, upperWing[1].X, upperWing[1].Y);
                    g.DrawLine(p, upperWing[1].X, upperWing[1].Y, upperWing[2].X, upperWing[2].Y);
                    g.DrawLine(p, lowerWing[1].X, lowerWing[1].Y, lowerWing[2].X, lowerWing[2].Y);
                }

                // Wing eye spots — hypnotic patterns
                for (int spot = 0; spot < 3; spot++) {
                    float spotT = (spot + 1) / 4f;
                    float spotX = cx + side * wingTip * spotT * 0.7f;
                    float spotY = cy - wingTip * 0.2f * (1f - spotT) + spot * 2;
                    float spotPulse = 0.4f + 0.6f * (float)Math.Max(0, Math.Sin(t * 30 + spot * 2 + side));
                    int spotA = (int)(a * 0.5f * spotPulse * bodyFade);
                    float spotR = 2 - spot * 0.4f;

                    // Dark center
                    using (var b = new SolidBrush(Color.FromArgb(spotA, 10, 5, 20)))
                        g.FillEllipse(b, spotX - spotR, spotY - spotR, spotR * 2, spotR * 2);
                    // Violet ring
                    using (var p = new Pen(Color.FromArgb(spotA, 120, 60, 200), 0.5f))
                        g.DrawEllipse(p, spotX - spotR * 1.3f, spotY - spotR * 1.3f, spotR * 2.6f, spotR * 2.6f);
                    // Inner glow
                    using (var b = new SolidBrush(Color.FromArgb(spotA / 3, 160, 100, 255)))
                        g.FillEllipse(b, spotX - spotR * 0.4f, spotY - spotR * 0.4f, spotR * 0.8f, spotR * 0.8f);
                }
            }

            // === BODY — slender thorax/abdomen ===
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.85f * bodyFade), 100, 70, 160)))
                g.FillEllipse(b, cx - 1.8f, cy - 4, 3.6f, 8);
            // Segmented markings
            for (int mark = 0; mark < 3; mark++) {
                int ma = (int)(a * 0.3f * bodyFade);
                using (var p = new Pen(Color.FromArgb(ma, 80, 50, 120), 0.4f))
                    g.DrawLine(p, cx - 1.5f, cy - 2 + mark * 2, cx + 1.5f, cy - 2 + mark * 2);
            }

            // === ANTENNAE — feathery, sensing void currents ===
            float antennaWave = (float)Math.Sin(t * 35) * 3;
            for (int ant = -1; ant <= 1; ant += 2) {
                float tipX = cx + ant * 4 + antennaWave * ant;
                float tipY = cy - 8 - Math.Abs(antennaWave);
                int antA = (int)(a * 0.6f * bodyFade);
                using (var p = new Pen(Color.FromArgb(antA, 80, 50, 130), 0.5f)) {
                    float midX = cx + ant * 2, midY = cy - 6;
                    g.DrawBezier(p, cx, cy - 4, midX, midY, tipX - ant, tipY + 2, tipX, tipY);
                }
                // Antenna tip glow
                float tipPulse = 0.3f + 0.7f * (float)Math.Max(0, Math.Sin(t * 40 + ant * 2));
                using (var b = new SolidBrush(Color.FromArgb((int)(antA * 0.5f * tipPulse), 150, 100, 255)))
                    g.FillEllipse(b, tipX - 1.5f, tipY - 1.5f, 3, 3);
            }

            // === VOID DUST — dark particles trailing ===
            var rng = new Random(ev.Seed);
            for (int vd = 0; vd < 10; vd++) {
                float vdLife = (t * 4 + vd * 0.09f) % 1.0f;
                float vdAngle = (float)(rng.NextDouble() * Math.PI * 2);
                float vdDist = 5 + vdLife * 20;
                float vdx = cx - dir * vdDist * 0.7f + (float)Math.Cos(vdAngle) * vdDist * 0.3f;
                float vdy = cy + (float)Math.Sin(vdAngle) * vdDist * 0.5f;
                float vdFade = vdLife < 0.15f ? vdLife / 0.15f : (1f - vdLife) / 0.85f;
                int vda = (int)(a * 0.15f * vdFade * bodyFade);
                if (vda > 0)
                    using (var b = new SolidBrush(Color.FromArgb(vda, 60, 30, 100)))
                        g.FillEllipse(b, vdx - 1.5f, vdy - 1.5f, 3, 3);
            }

            // === LIGHT ABSORPTION — faint "eaten" star sparkles being dimmed ===
            var absRng = new Random(ev.Seed + 200);
            for (int ab = 0; ab < 4; ab++) {
                float abPhase = (t * 6 + ab * 0.25f) % 1.0f;
                if (abPhase > 0.3f) continue;
                float abT = abPhase / 0.3f;
                float abDist = 15 * (1f - abT);
                float abAngle = (float)(absRng.NextDouble() * Math.PI * 2);
                float abx = cx + (float)Math.Cos(abAngle) * abDist;
                float aby = cy + (float)Math.Sin(abAngle) * abDist;
                int abA = (int)(a * 0.3f * (1f - abT) * bodyFade);
                // Fading star being consumed
                using (var b = new SolidBrush(Color.FromArgb(abA, 200, 180, 255)))
                    g.FillEllipse(b, abx - 1, aby - 1, 2, 2);
            }
        }
        static void PaintNeonJellyfish(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            float pulse = (float)Math.Sin(t * 30); // bell contraction cycle
            float bellR = 8 + pulse * 2; // bell radius pulsing
            float bob = (float)Math.Sin(t * 18) * 2; // gentle vertical bob
            cy += bob;
            int a = (int)(fade * 210);

            // === BIOLUMINESCENT GLOW FIELD ===
            for (int gl = 4; gl >= 1; gl--) {
                float gr = bellR + gl * 8;
                float glowPulse = 0.4f + 0.6f * (float)Math.Max(0, Math.Sin(t * 25 + gl));
                int ga = (int)(a * 0.04f * glowPulse * bodyFade / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 255, 50, 200)))
                        g.FillEllipse(b, cx - gr, cy - gr * 0.8f, gr * 2, gr * 1.6f);
            }

            // === BELL — translucent dome with internal structure ===
            // Outer bell
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * bodyFade), 255, 60, 200)))
                g.FillEllipse(b, cx - bellR, cy - bellR * 0.7f, bellR * 2, bellR * 1.2f);

            // Inner bell — lighter highlight
            float innerR = bellR * 0.7f;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.25f * bodyFade), 255, 150, 230)))
                g.FillEllipse(b, cx - innerR, cy - innerR * 0.5f, innerR * 2, innerR * 0.8f);

            // Bell rim — brighter edge
            using (var p = new Pen(Color.FromArgb((int)(a * 0.6f * bodyFade), 255, 100, 220), 1.0f))
                g.DrawArc(p, cx - bellR, cy - bellR * 0.5f, bellR * 2, bellR * 1.2f, 10, 160);

            // Radial channels inside bell
            for (int ch = 0; ch < 8; ch++) {
                float chAngle = (float)(ch * Math.PI / 8 + Math.PI * 0.05);
                float chPulse = 0.3f + 0.7f * (float)Math.Max(0, Math.Sin(t * 35 + ch * 1.5f));
                int cha = (int)(a * 0.2f * chPulse * bodyFade);
                float chEndX = cx + (float)Math.Cos(chAngle) * bellR * 0.8f;
                float chEndY = cy + (float)Math.Sin(chAngle) * bellR * 0.5f;
                if (cha > 0)
                    using (var p = new Pen(Color.FromArgb(cha, 255, 180, 240), 0.5f))
                        g.DrawLine(p, cx, cy - bellR * 0.1f, chEndX, chEndY);
            }

            // === GONADS — four glowing organs inside bell ===
            for (int gon = 0; gon < 4; gon++) {
                float gAngle = (float)(gon * Math.PI / 2 + Math.PI * 0.25);
                float gDist = bellR * 0.4f;
                float gx = cx + (float)Math.Cos(gAngle) * gDist;
                float gy = cy + (float)Math.Sin(gAngle) * gDist * 0.5f;
                float gPulse = 0.5f + 0.5f * (float)Math.Sin(t * 20 + gon * 1.5f);
                int ga2 = (int)(a * 0.4f * gPulse * bodyFade);
                using (var b = new SolidBrush(Color.FromArgb(ga2, 255, 200, 255)))
                    g.FillEllipse(b, gx - 1.5f, gy - 1.2f, 3, 2.4f);
                using (var b = new SolidBrush(Color.FromArgb(ga2 / 3, 255, 220, 255)))
                    g.FillEllipse(b, gx - 3, gy - 2.5f, 6, 5);
            }

            // === ORAL ARMS — thick central tentacles ===
            float tentacleBase = cy + bellR * 0.5f;
            for (int arm = 0; arm < 4; arm++) {
                float armX = cx - 3 + arm * 2;
                float armWave = (float)Math.Sin(t * 22 + arm * 0.8f) * 3;
                float armWave2 = (float)Math.Sin(t * 15 + arm * 1.2f) * 2;
                for (int seg = 0; seg < 10; seg++) {
                    float segY2 = tentacleBase + seg * 3;
                    float segX = armX + armWave * (seg / 10f) + armWave2 * (seg / 8f);
                    float segFade = 1f - seg * 0.08f;
                    int sa = (int)(a * 0.45f * segFade * bodyFade);
                    if (sa <= 0) continue;
                    float ssz = 1.8f - seg * 0.1f;
                    Color armColor = arm % 2 == 0 ? Color.FromArgb(sa, 255, 80, 220) : Color.FromArgb(sa, 200, 100, 255);
                    using (var b = new SolidBrush(armColor))
                        g.FillEllipse(b, segX - ssz, segY2 - ssz * 0.4f, ssz * 2, ssz * 0.8f);
                }
            }

            // === TRAILING TENTACLES — long, thin, flowing ===
            for (int ten = 0; ten < 8; ten++) {
                float tenX = cx - 5.5f + ten * 1.6f;
                float tenPhaseOff = ten * 0.7f;
                for (int seg = 0; seg < 14; seg++) {
                    float segY2 = tentacleBase + 4 + seg * 2.5f;
                    float wave1 = (float)Math.Sin(t * 25 + tenPhaseOff + seg * 0.4f) * (2 + seg * 0.2f);
                    float wave2 = (float)Math.Sin(t * 18 + tenPhaseOff + seg * 0.3f) * 1.5f;
                    float segX = tenX + wave1 + wave2;
                    float segFade = 1f - seg * 0.06f;
                    int sa = (int)(a * 0.3f * segFade * bodyFade);
                    if (sa <= 0) continue;
                    Color tenColor = ten % 3 == 0 ? Color.FromArgb(sa, 255, 80, 220) :
                        ten % 3 == 1 ? Color.FromArgb(sa, 80, 200, 255) : Color.FromArgb(sa, 200, 80, 255);
                    using (var b = new SolidBrush(tenColor))
                        g.FillEllipse(b, segX - 0.7f, segY2 - 0.4f, 1.4f, 0.8f);

                    // Bioluminescent nodes on tentacles
                    if (seg % 4 == 0) {
                        float nodePulse = (float)Math.Max(0, Math.Sin(t * 40 + ten + seg * 0.5f));
                        int na = (int)(sa * 0.6f * nodePulse);
                        if (na > 0)
                            using (var b = new SolidBrush(Color.FromArgb(na, 255, 200, 255)))
                                g.FillEllipse(b, segX - 1.2f, segY2 - 0.8f, 2.4f, 1.6f);
                    }
                }
            }

            // === EXPELLED PARTICLES — luminous mucus droplets ===
            var rng = new Random(ev.Seed + (int)(t * 15));
            for (int ep = 0; ep < 6; ep++) {
                float epLife = (t * 3 + ep * 0.15f) % 1.0f;
                float epAngle = (float)(rng.NextDouble() * Math.PI * 0.6 + Math.PI * 0.2);
                float epDist = epLife * 20;
                float epx = cx + (float)Math.Cos(epAngle) * epDist * 0.5f;
                float epy = tentacleBase + 10 + (float)Math.Sin(epAngle) * epDist;
                float epFade = (1f - epLife) * bodyFade;
                int epa = (int)(a * 0.2f * epFade);
                if (epa > 0) {
                    using (var b = new SolidBrush(Color.FromArgb(epa, 255, 150, 230)))
                        g.FillEllipse(b, epx - 1, epy - 1, 2, 2);
                }
            }
        }
        static void PaintGalaxySpiral(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(200 * fade);
            float op;
            if (t < 0.15f) op = t / 0.15f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));
            float spin = t * 40;
            float tiltY = 0.45f;

            // === GALACTIC CORE GLOW ===
            for (int gl = 5; gl >= 1; gl--) {
                float gr = (3 + gl * 5) * op;
                int ga = (int)(a * 0.06f * op / (gl * 0.7f));
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 255, 240, 200)))
                        g.FillEllipse(b, cx - gr, cy - gr * tiltY, gr * 2, gr * 2 * tiltY);
            }
            float coreR2 = 4 * op;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * op), 255, 250, 220)))
                g.FillEllipse(b, cx - coreR2, cy - coreR2 * tiltY, coreR2 * 2, coreR2 * 2 * tiltY);
            float nucR = 2 * op;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.4f * op), 255, 255, 255)))
                g.FillEllipse(b, cx - nucR, cy - nucR * tiltY, nucR * 2, nucR * 2 * tiltY);

            // === SPIRAL ARMS ===
            var rng = new Random(ev.Seed);
            for (int arm = 0; arm < 2; arm++) {
                float armOffset = arm * 180;
                for (int pt = 0; pt < 50; pt++) {
                    float theta = (float)((spin + armOffset + pt * 8) * Math.PI / 180);
                    float spiralR = (3 + pt * 0.7f + (float)Math.Pow(pt * 0.1f, 1.3f)) * op;
                    float armWidth = (2 + pt * 0.15f) * op;
                    float perpOffset = (float)(rng.NextDouble() - 0.5) * armWidth * 2;
                    float perpAngle = theta + (float)(Math.PI / 2);
                    float px = cx + (float)Math.Cos(theta) * spiralR + (float)Math.Cos(perpAngle) * perpOffset;
                    float py = cy + (float)Math.Sin(theta) * spiralR * tiltY + (float)Math.Sin(perpAngle) * perpOffset * tiltY;
                    float distFade = 1f - pt / 55f;
                    int pa = (int)(a * 0.4f * distFade * op);
                    if (pa <= 0) continue;
                    float sz;
                    Color starColor;
                    int colorRoll = (pt + arm * 7) % 8;
                    switch (colorRoll) {
                        case 0: starColor = Color.FromArgb(pa, 200, 215, 255); sz = 2.2f; break;
                        case 1: starColor = Color.FromArgb(pa, 255, 230, 200); sz = 1.8f; break;
                        case 2: starColor = Color.FromArgb(pa, 180, 200, 255); sz = 1.5f; break;
                        case 3: starColor = Color.FromArgb(pa, 255, 200, 220); sz = 1.6f; break;
                        case 4: starColor = Color.FromArgb(pa, 220, 220, 240); sz = 2.0f; break;
                        case 5: starColor = Color.FromArgb(pa, 160, 190, 255); sz = 1.3f; break;
                        default: starColor = Color.FromArgb(pa, 240, 235, 220); sz = 1.7f; break;
                    }
                    using (var b = new SolidBrush(starColor))
                        g.FillEllipse(b, px - sz * 0.5f, py - sz * 0.5f, sz, sz);
                    if (pt % 4 == 0 && pt < 35) {
                        float nebR = 4 + (float)rng.NextDouble() * 3;
                        int nebA = (int)(pa * 0.15f);
                        Color nebColor = colorRoll == 3
                            ? Color.FromArgb(nebA, 255, 150, 180)
                            : Color.FromArgb(nebA, 150, 180, 255);
                        using (var b = new SolidBrush(nebColor))
                            g.FillEllipse(b, px - nebR, py - nebR * tiltY, nebR * 2, nebR * 2 * tiltY);
                    }
                }
            }

            // === DUST LANES ===
            for (int dl = 0; dl < 2; dl++) {
                float dlOffset = dl * 180 + 90;
                for (int pt = 0; pt < 20; pt++) {
                    float theta = (float)((spin + dlOffset + pt * 10) * Math.PI / 180);
                    float spiralR = (5 + pt * 0.8f) * op;
                    float px = cx + (float)Math.Cos(theta) * spiralR;
                    float py = cy + (float)Math.Sin(theta) * spiralR * tiltY;
                    float dustR = (2 + pt * 0.1f) * op;
                    int da = (int)(a * 0.04f * (1f - pt / 25f) * op);
                    if (da > 0)
                        using (var b = new SolidBrush(Color.FromArgb(da, 10, 10, 20)))
                            g.FillEllipse(b, px - dustR, py - dustR * tiltY, dustR * 2, dustR * 2 * tiltY);
                }
            }

            // === GLOBULAR CLUSTERS ===
            var clusterRng = new Random(ev.Seed + 200);
            for (int gc = 0; gc < 8; gc++) {
                float gcAngle = (float)(clusterRng.NextDouble() * Math.PI * 2) + spin * 0.01f;
                float gcDist = (20 + (float)clusterRng.NextDouble() * 15) * op;
                float gcx = cx + (float)Math.Cos(gcAngle) * gcDist;
                float gcy = cy + (float)Math.Sin(gcAngle) * gcDist * tiltY;
                int gca = (int)(a * 0.25f * (0.3f + (float)clusterRng.NextDouble() * 0.5f) * op);
                if (gca > 0)
                    using (var b = new SolidBrush(Color.FromArgb(gca, 255, 245, 220)))
                        g.FillEllipse(b, gcx - 1, gcy - 1, 2, 2);
            }
        }
        static void PaintMagicCarpet(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            float wave1 = (float)Math.Sin(t * 25) * 2.5f;
            float wave2 = (float)Math.Sin(t * 18 + 1.5f) * 1.5f;

            // === MAGIC DUST TRAIL — golden sparkles behind ===
            var rng = new Random(ev.Seed);
            for (int dust = 0; dust < 15; dust++) {
                float dLife = (t * 4 + dust * 0.06f) % 1.0f;
                float dx = cx - dir * (8 + dLife * 35) + (float)(rng.NextDouble() - 0.5) * 10;
                float dy = cy + 2 + dLife * 10 + (float)Math.Sin(t * 30 + dust * 1.5f) * 3;
                float dFade = (1f - dLife) * bodyFade;
                int da = (int)(a * 0.2f * dFade);
                if (da <= 0) continue;
                Color dc = dust % 3 == 0 ? Color.FromArgb(da, 255, 220, 80) :
                    dust % 3 == 1 ? Color.FromArgb(da, 255, 180, 60) : Color.FromArgb(da, 255, 240, 150);
                using (var b = new SolidBrush(dc))
                    g.FillEllipse(b, dx - 1, dy - 1, 2, 2);
            }

            // === CARPET — ornate rug with pattern and fringe ===
            float carpetW = 14, carpetH = 5;

            // Carpet glow
            for (int gl = 2; gl >= 1; gl--) {
                float gr = gl * 5;
                int ga = (int)(a * 0.05f * bodyFade / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 200, 100, 50)))
                        g.FillEllipse(b, cx - carpetW - gr, cy - gr * 0.4f, (carpetW + gr) * 2, gr);
            }

            // Carpet body — perspective quad with wave
            PointF[] carpet = {
                new PointF(cx - carpetW, cy + wave1),
                new PointF(cx - carpetW * 0.6f, cy - carpetH * 0.4f + wave2),
                new PointF(cx + carpetW * 0.6f, cy - carpetH * 0.4f - wave2),
                new PointF(cx + carpetW, cy - wave1),
            };
            // Deep red base
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.75f * bodyFade), 140, 30, 30)))
                g.FillPolygon(b, carpet);

            // Border pattern — gold edge
            using (var p = new Pen(Color.FromArgb((int)(a * 0.6f * bodyFade), 220, 180, 60), 1.0f))
                g.DrawPolygon(p, carpet);

            // Inner border
            float inset = 0.8f;
            PointF[] innerBorder = {
                new PointF(cx - carpetW * inset, cy + wave1 * inset),
                new PointF(cx - carpetW * 0.5f, cy - carpetH * 0.3f + wave2 * inset),
                new PointF(cx + carpetW * 0.5f, cy - carpetH * 0.3f - wave2 * inset),
                new PointF(cx + carpetW * inset, cy - wave1 * inset),
            };
            using (var p = new Pen(Color.FromArgb((int)(a * 0.4f * bodyFade), 200, 160, 50), 0.6f))
                g.DrawPolygon(p, innerBorder);

            // Central medallion
            float medX = cx, medY = cy - carpetH * 0.15f;
            float medPulse = 0.5f + 0.5f * (float)Math.Sin(t * 20);
            int medA = (int)(a * 0.5f * medPulse * bodyFade);
            using (var b = new SolidBrush(Color.FromArgb(medA, 220, 180, 60)))
                g.FillEllipse(b, medX - 3, medY - 1.5f, 6, 3);
            using (var b = new SolidBrush(Color.FromArgb(medA / 2, 255, 220, 100)))
                g.FillEllipse(b, medX - 1.5f, medY - 0.8f, 3, 1.6f);

            // Corner tassels — dangling threads
            float[] tasselX = { carpet[0].X, carpet[3].X };
            float[] tasselY = { carpet[0].Y, carpet[3].Y };
            for (int ts = 0; ts < 2; ts++) {
                for (int thread = 0; thread < 3; thread++) {
                    float tLen = 4 + thread;
                    float tWave = (float)Math.Sin(t * 30 + ts * 2 + thread) * 2;
                    float tx = tasselX[ts] + (thread - 1) * 1.5f;
                    float ty = tasselY[ts];
                    float tx2 = tx + tWave;
                    float ty2 = ty + tLen;
                    int ta = (int)(a * 0.5f * bodyFade);
                    using (var p = new Pen(Color.FromArgb(ta, 220, 180, 60), 0.5f))
                        g.DrawLine(p, tx, ty, tx2, ty2);
                    using (var b = new SolidBrush(Color.FromArgb(ta, 255, 220, 80)))
                        g.FillEllipse(b, tx2 - 0.8f, ty2 - 0.8f, 1.6f, 1.6f);
                }
            }

            // === RIDER SILHOUETTE — sitting cross-legged ===
            float riderX = cx, riderY = cy - carpetH * 0.4f;
            // Body — brighter purple robe
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.85f * bodyFade), 120, 80, 180)))
                g.FillEllipse(b, riderX - 3, riderY - 4, 6, 5);
            // Head with turban — brighter
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.85f * bodyFade), 140, 100, 190)))
                g.FillEllipse(b, riderX - 2.5f, riderY - 8, 5, 4.5f);
            // Turban jewel
            float jewelPulse = 0.5f + 0.5f * (float)Math.Sin(t * 40);
            int ja = (int)(a * 0.6f * jewelPulse * bodyFade);
            using (var b = new SolidBrush(Color.FromArgb(ja, 255, 50, 50)))
                g.FillEllipse(b, riderX - 1, riderY - 7, 2, 2);
            using (var b = new SolidBrush(Color.FromArgb(ja / 3, 255, 100, 100)))
                g.FillEllipse(b, riderX - 2.5f, riderY - 8.5f, 5, 5);

            // Crossed legs
            using (var p = new Pen(Color.FromArgb((int)(a * 0.7f * bodyFade), 120, 80, 180), 0.8f)) {
                g.DrawLine(p, riderX - 1, riderY, riderX - 4, riderY + 1.5f);
                g.DrawLine(p, riderX + 1, riderY, riderX + 4, riderY + 1.5f);
            }
        }
        static void PaintSpaceLantern(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            float drift = (float)Math.Sin(t * 12) * 1.5f;
            float bob = (float)Math.Sin(t * 15) * 2;
            cx += drift; cy += bob;
            int a = (int)(fade * 210);
            float flicker = 0.6f + 0.4f * (float)Math.Sin(t * 60 + (float)Math.Sin(t * 23) * 3);

            // === WARM LIGHT AURA — layered glow ===
            for (int gl = 5; gl >= 1; gl--) {
                float gr = 5 + gl * 7;
                float glPulse = flicker * (0.5f + 0.5f * (float)Math.Sin(t * 15 + gl));
                int ga = (int)(a * 0.05f * glPulse * bodyFade / (gl * 0.7f));
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 255, 180, 60)))
                        g.FillEllipse(b, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            // === LANTERN FRAME — delicate wire structure ===
            float lanW = 8, lanH = 10;

            // Top cap
            using (var p = new Pen(Color.FromArgb((int)(a * 0.6f * bodyFade), 180, 140, 80), 0.8f))
                g.DrawArc(p, cx - lanW * 0.6f, cy - lanH - 1, lanW * 1.2f, 3, 180, 180);

            // Hanging ring
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f * bodyFade), 160, 130, 70), 0.6f))
                g.DrawArc(p, cx - 1.5f, cy - lanH - 3, 3, 3, 180, 180);

            // Side ribs
            for (int rib = -1; rib <= 1; rib += 2) {
                float ribX = rib * lanW;
                int ra = (int)(a * 0.5f * bodyFade);
                using (var p = new Pen(Color.FromArgb(ra, 170, 130, 70), 0.6f))
                    g.DrawBezier(p, cx + ribX * 0.5f, cy - lanH, cx + ribX, cy - lanH * 0.3f,
                        cx + ribX, cy + lanH * 0.3f, cx + ribX * 0.5f, cy + lanH * 0.7f);
            }

            // Bottom cap
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f * bodyFade), 160, 120, 70), 0.7f))
                g.DrawArc(p, cx - lanW * 0.4f, cy + lanH * 0.5f, lanW * 0.8f, 3, 0, 180);

            // === PAPER PANELS — warm glowing panes ===
            for (int panel = 0; panel < 4; panel++) {
                float panelAngle = (float)(panel * Math.PI / 2 + Math.PI * 0.25);
                float panelW = (float)Math.Abs(Math.Cos(panelAngle)) * lanW * 0.8f;
                if (panelW < 1) continue;
                float panelX = cx + (float)Math.Sin(panelAngle) * lanW * 0.3f;
                float panelPulse = flicker * (0.7f + 0.3f * (float)Math.Sin(t * 25 + panel * 1.5f));
                int pa = (int)(a * 0.35f * panelPulse * bodyFade);
                using (var b = new SolidBrush(Color.FromArgb(pa, 255, 200, 100)))
                    g.FillEllipse(b, panelX - panelW, cy - lanH * 0.6f, panelW * 2, lanH * 1.2f);
            }

            // === INNER FLAME — dancing core ===
            float flameH = 4 + flicker * 2;
            float flameW2 = 2 + flicker;
            float flameWobble = (float)Math.Sin(t * 70) * 1.5f;

            // Flame outer
            int fa = (int)(a * 0.6f * flicker * bodyFade);
            using (var b = new SolidBrush(Color.FromArgb(fa, 255, 160, 40)))
                g.FillEllipse(b, cx - flameW2 + flameWobble, cy - flameH * 0.5f, flameW2 * 2, flameH);

            // Flame inner — hot white
            float innerFH = flameH * 0.5f;
            using (var b = new SolidBrush(Color.FromArgb((int)(fa * 0.6f), 255, 240, 180)))
                g.FillEllipse(b, cx - flameW2 * 0.5f + flameWobble * 0.5f, cy - innerFH * 0.3f, flameW2, innerFH);

            // Flame tip
            using (var b = new SolidBrush(Color.FromArgb((int)(fa * 0.3f), 255, 255, 220)))
                g.FillEllipse(b, cx - 0.8f + flameWobble * 0.3f, cy - flameH * 0.4f, 1.6f, flameH * 0.3f);

            // === EMBER PARTICLES — rising from lantern ===
            var rng = new Random(ev.Seed);
            for (int em = 0; em < 8; em++) {
                float emLife = (t * 3 + em * 0.12f) % 1.0f;
                float emX = cx + (float)(rng.NextDouble() - 0.5) * 6 + (float)Math.Sin(t * 20 + em * 2) * 3;
                float emY = cy - lanH - 2 - emLife * 20;
                float emFade = emLife < 0.1f ? emLife / 0.1f : (1f - emLife) / 0.9f;
                int ema = (int)(a * 0.3f * emFade * bodyFade);
                if (ema <= 0) continue;
                float esz = 1.2f * (1f - emLife * 0.5f);
                using (var b = new SolidBrush(Color.FromArgb(ema, 255, 200, 80)))
                    g.FillEllipse(b, emX - esz * 0.5f, emY - esz * 0.5f, esz, esz);
            }

            // === LIGHT MOTHS — tiny creatures circling the lantern ===
            for (int moth = 0; moth < 3; moth++) {
                float mAngle = t * 15 + moth * (float)(Math.PI * 2 / 3);
                float mDist = 10 + (float)Math.Sin(t * 8 + moth * 2) * 4;
                float mx = cx + (float)Math.Cos(mAngle) * mDist;
                float my = cy + (float)Math.Sin(mAngle) * mDist * 0.6f;
                float mWing = (float)Math.Sin(t * 80 + moth * 5) * 2;
                int ma = (int)(a * 0.3f * bodyFade);
                using (var b = new SolidBrush(Color.FromArgb(ma, 200, 200, 180)))
                    g.FillEllipse(b, mx - 0.5f + mWing, my - 0.5f, 1 + Math.Abs(mWing), 1);
                using (var b = new SolidBrush(Color.FromArgb(ma, 200, 200, 180)))
                    g.FillEllipse(b, mx - 0.5f - mWing, my - 0.5f, 1 + Math.Abs(mWing), 1);
            }
        }
        static void PaintCosmicOwl(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            // Wing flap — slow, powerful strokes
            float wingPhase = (float)Math.Sin(t * 20);
            float wingSpread = 10 + wingPhase * 7;

            // === WISDOM AURA — faint golden halo ===
            for (int gl = 3; gl >= 1; gl--) {
                float gr = 10 + gl * 6;
                float glPulse = 0.4f + 0.6f * (float)Math.Max(0, Math.Sin(t * 10 + gl));
                int ga = (int)(a * 0.04f * glPulse * bodyFade / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 255, 220, 100)))
                        g.FillEllipse(b, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            // === WINGS — broad, layered feathers ===
            for (int side = -1; side <= 1; side += 2) {
                // Wing base shape
                float wingTipX = cx + side * wingSpread;
                float wingTipY = cy + wingPhase * 3 * side;

                // Primary feathers — 5 layered
                for (int feather = 0; feather < 5; feather++) {
                    float fT = feather / 5f;
                    float fx = cx + side * (3 + wingSpread * fT);
                    float fy = cy + wingPhase * fT * 3 * side + feather * 1.5f;
                    float fLen = (wingSpread * 0.3f) * (1f - fT * 0.3f);
                    float fAngle = (float)(side > 0 ? -0.3 + wingPhase * 0.15 : Math.PI + 0.3 - wingPhase * 0.15);
                    float fTipX = fx + (float)Math.Cos(fAngle) * fLen;
                    float fTipY = fy + (float)Math.Sin(fAngle) * fLen;
                    int fa = (int)(a * (0.55f - feather * 0.05f) * bodyFade);
                    Color fColor = feather % 2 == 0 ? Color.FromArgb(fa, 150, 130, 100) : Color.FromArgb(fa, 140, 120, 90);
                    using (var p = new Pen(fColor, 1.8f - feather * 0.2f)) {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        g.DrawLine(p, fx, fy, fTipX, fTipY);
                    }
                }

                // Wing membrane
                PointF[] wing = {
                    new PointF(cx, cy),
                    new PointF(cx + side * wingSpread * 0.4f, cy - 4 + wingPhase * side),
                    new PointF(wingTipX, wingTipY),
                    new PointF(cx + side * wingSpread * 0.5f, cy + 4 + wingPhase * side * 0.5f)
                };
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.55f * bodyFade), 140, 120, 90)))
                    g.FillPolygon(b, wing);
            }

            // === BODY — round, fluffy ===
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * bodyFade), 150, 130, 100)))
                g.FillEllipse(b, cx - 5, cy - 4, 10, 10);
            // Breast feathers — lighter
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.55f * bodyFade), 200, 180, 150)))
                g.FillEllipse(b, cx - 3, cy - 1, 6, 6);

            // === HEAD — large, round with facial disc ===
            float headY = cy - 6;
            // Head shape
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * bodyFade), 155, 135, 105)))
                g.FillEllipse(b, cx - 5.5f, headY - 4, 11, 8);

            // Facial disc — heart-shaped lighter area
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * bodyFade), 210, 190, 160)))
                g.FillEllipse(b, cx - 4, headY - 2.5f, 8, 5.5f);

            // Ear tufts
            for (int tuft = -1; tuft <= 1; tuft += 2) {
                float tX = cx + tuft * 4;
                int ta = (int)(a * 0.6f * bodyFade);
                using (var p = new Pen(Color.FromArgb(ta, 100, 85, 60), 1.0f)) {
                    g.DrawLine(p, tX, headY - 3, tX + tuft * 2, headY - 7);
                    g.DrawLine(p, tX + tuft * 0.5f, headY - 3.5f, tX + tuft * 2.5f, headY - 6);
                }
            }

            // === EYES — large, golden, hypnotic with concentric rings ===
            for (int eye = -1; eye <= 1; eye += 2) {
                float eyeX = cx + eye * 2.2f, eyeY = headY - 0.5f;
                float eyePulse = 0.5f + 0.5f * (float)Math.Sin(t * 8 + eye);
                int eyeBaseA = (int)(a * 0.9f * bodyFade);

                // Outer eye glow
                using (var b = new SolidBrush(Color.FromArgb(eyeBaseA / 4, 255, 200, 50)))
                    g.FillEllipse(b, eyeX - 4, eyeY - 4, 8, 8);

                // Eye orb
                using (var b = new SolidBrush(Color.FromArgb(eyeBaseA, 255, 210, 60)))
                    g.FillEllipse(b, eyeX - 2.5f, eyeY - 2.5f, 5, 5);

                // Concentric iris rings
                for (int ring = 0; ring < 3; ring++) {
                    float rr = 2f - ring * 0.6f;
                    int ra = (int)(eyeBaseA * (0.4f + ring * 0.15f));
                    using (var p = new Pen(Color.FromArgb(ra, 200, 150, 30), 0.4f))
                        g.DrawEllipse(p, eyeX - rr, eyeY - rr, rr * 2, rr * 2);
                }

                // Pupil — deep dark slit
                using (var b = new SolidBrush(Color.FromArgb(eyeBaseA, 15, 10, 5)))
                    g.FillEllipse(b, eyeX - 0.8f, eyeY - 1.5f, 1.6f, 3);

                // Pupil highlight
                using (var b = new SolidBrush(Color.FromArgb((int)(eyeBaseA * 0.7f * eyePulse), 255, 255, 255)))
                    g.FillEllipse(b, eyeX - 0.6f + eye * 0.4f, eyeY - 1.3f, 1, 1);
            }

            // Beak
            int beakA = (int)(a * 0.7f * bodyFade);
            using (var b = new SolidBrush(Color.FromArgb(beakA, 160, 140, 80))) {
                PointF[] beak = { new PointF(cx, headY + 1), new PointF(cx - 1, headY + 2.5f), new PointF(cx + 1, headY + 2.5f) };
                g.FillPolygon(b, beak);
            }

            // === WISDOM SPARKLES — knowledge motes orbiting ===
            for (int ws = 0; ws < 8; ws++) {
                float wsAngle = t * 6 + ws * (float)(Math.PI * 2 / 8);
                float wsDist = 12 + (float)Math.Sin(t * 10 + ws * 1.5f) * 4;
                float wsx = cx + (float)Math.Cos(wsAngle) * wsDist;
                float wsy = cy + (float)Math.Sin(wsAngle) * wsDist * 0.6f;
                float wsPulse = 0.3f + 0.7f * (float)Math.Max(0, Math.Sin(t * 20 + ws * 2.5f));
                int wsa = (int)(a * 0.35f * wsPulse * bodyFade);
                if (wsa > 0)
                    using (var b = new SolidBrush(Color.FromArgb(wsa, 255, 230, 120)))
                        g.FillEllipse(b, wsx - 1, wsy - 1, 2, 2);
            }

            // === STARDUST WAKE — feathery trail ===
            for (int tr = 0; tr < 8; tr++) {
                float trLife = (t * 5 + tr * 0.11f) % 1.0f;
                float trx = cx - dir * (6 + trLife * 25) + (float)Math.Sin(t * 20 + tr * 2) * 3;
                float trY2 = cy + (float)Math.Sin(t * 15 + tr * 1.5f) * 4;
                float trFade = (1f - trLife) * bodyFade;
                int tra = (int)(a * 0.12f * trFade);
                if (tra > 0)
                    using (var b = new SolidBrush(Color.FromArgb(tra, 255, 220, 100)))
                        g.FillEllipse(b, trx - 1.5f, trY2 - 0.8f, 3, 1.6f);
            }
        }
        static void PaintWarpDrive(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(200 * fade);
            float intensity;
            if (t < 0.15f) intensity = t / 0.15f;
            else if (t < 0.70f) intensity = 1f;
            else intensity = (1f - t) / 0.30f;
            intensity = Math.Max(0, Math.Min(1, intensity));

            var rng = new Random(ev.Seed);
            bool inWarp = t >= 0.20f && t <= 0.70f;
            float warpDepth = inWarp ? Math.Min(1, (t - 0.20f) / 0.10f) : 0;

            // === STAR STREAKS ===
            int streakCount = inWarp ? 40 : 20;
            for (int s = 0; s < streakCount; s++) {
                float sAngle = (float)(rng.NextDouble() * Math.PI * 2);
                float baseDist = 6 + (float)rng.NextDouble() * 35;
                float innerR, outerR;
                if (t < 0.15f) {
                    float stretch = 1f + intensity * 3f;
                    innerR = baseDist * intensity;
                    outerR = innerR + stretch;
                } else if (inWarp) {
                    innerR = (3 + (float)rng.NextDouble() * 8) * intensity;
                    outerR = innerR + (15 + (float)rng.NextDouble() * 40) * warpDepth;
                } else {
                    innerR = baseDist * intensity * 0.5f;
                    outerR = innerR + 5 * intensity;
                }
                float sx1 = cx + (float)Math.Cos(sAngle) * innerR;
                float sy1 = cy + (float)Math.Sin(sAngle) * innerR * 0.55f;
                float sx2 = cx + (float)Math.Cos(sAngle) * outerR;
                float sy2 = cy + (float)Math.Sin(sAngle) * outerR * 0.55f;
                float brightness = 0.4f + (float)rng.NextDouble() * 0.6f;
                float pulse = 0.7f + 0.3f * (float)Math.Sin(t * 40 + s * 2.3f);
                int sa = (int)(a * brightness * pulse * intensity);
                if (sa <= 0) continue;
                Color streakColor;
                switch (s % 5) {
                    case 0: streakColor = Color.FromArgb(sa, 255, 255, 255); break;
                    case 1: streakColor = Color.FromArgb(sa, 180, 210, 255); break;
                    case 2: streakColor = Color.FromArgb(sa, 140, 190, 255); break;
                    case 3: streakColor = Color.FromArgb(sa, 220, 230, 255); break;
                    default: streakColor = Color.FromArgb(sa, 200, 200, 240); break;
                }
                float thickness = inWarp ? (1.0f + (float)rng.NextDouble() * 1.5f) : 0.8f;
                using (var p = new Pen(streakColor, thickness)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, sx1, sy1, sx2, sy2);
                }
                if (inWarp && brightness > 0.6f) {
                    int headA = (int)(sa * 0.5f);
                    using (var b = new SolidBrush(Color.FromArgb(headA, 220, 235, 255)))
                        g.FillEllipse(b, sx2 - 1.5f, sy2 - 1.5f, 3, 3);
                }
            }

            // === TUNNEL WALLS ===
            if (inWarp) {
                for (int ring = 0; ring < 5; ring++) {
                    float ringPhase = (t * 6 + ring * 0.4f) % 1.0f;
                    float ringR = 15 + ringPhase * 50;
                    int ringA = (int)(a * 0.12f * (1f - ringPhase) * warpDepth);
                    if (ringA > 0)
                        using (var p = new Pen(Color.FromArgb(ringA, 120, 170, 255), 0.8f))
                            g.DrawEllipse(p, cx - ringR, cy - ringR * 0.55f, ringR * 2, ringR * 1.1f);
                }
            }

            // === CENTRAL CORE ===
            float coreR = 5 * intensity;
            if (coreR > 0.5f) {
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.1f * intensity), 140, 190, 255)))
                    g.FillEllipse(b, cx - coreR * 3, cy - coreR * 2, coreR * 6, coreR * 4);
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f * intensity), 180, 215, 255)))
                    g.FillEllipse(b, cx - coreR, cy - coreR * 0.6f, coreR * 2, coreR * 1.2f);
                float hotR = coreR * 0.4f;
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * intensity), 240, 245, 255)))
                    g.FillEllipse(b, cx - hotR, cy - hotR * 0.6f, hotR * 2, hotR * 1.2f);
            }

            // === JUMP FLASH ===
            if (t > 0.14f && t < 0.25f) {
                float flashT = (t - 0.14f) / 0.11f;
                float flashR = 20 + flashT * 60;
                int flashA = (int)(a * 0.5f * (flashT < 0.3f ? flashT / 0.3f : (1f - flashT) / 0.7f));
                using (var b = new SolidBrush(Color.FromArgb(flashA, 220, 240, 255)))
                    g.FillEllipse(b, cx - flashR, cy - flashR * 0.55f, flashR * 2, flashR * 1.1f);
                float innerFlash = flashR * 0.3f;
                using (var b = new SolidBrush(Color.FromArgb(flashA * 2 / 3, 255, 255, 255)))
                    g.FillEllipse(b, cx - innerFlash, cy - innerFlash * 0.55f, innerFlash * 2, innerFlash * 1.1f);
            }

            // === EXIT FLASH ===
            if (t > 0.68f && t < 0.78f) {
                float exitT = (t - 0.68f) / 0.10f;
                float exitR = 15 + exitT * 40;
                int exitA = (int)(a * 0.35f * (1f - exitT));
                using (var b = new SolidBrush(Color.FromArgb(exitA, 200, 225, 255)))
                    g.FillEllipse(b, cx - exitR, cy - exitR * 0.55f, exitR * 2, exitR * 1.1f);
            }
        }
        static void PaintSpaceKoi(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            int a = (int)(210 * fade);
            float dir = ev.DirX > 0 ? 1 : -1;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            cy += (float)Math.Sin(t * 20) * 2;

            // === COSMIC WATER RIPPLES — ethereal pond effect ===
            for (int rip = 0; rip < 4; rip++) {
                float ripPhase = (t * 3 + rip * 0.25f) % 1.0f;
                float ripR = 5 + ripPhase * 25;
                int ripA = (int)(a * 0.08f * (1f - ripPhase) * bodyFade);
                if (ripA > 0)
                    using (var p = new Pen(Color.FromArgb(ripA, 180, 220, 255), 0.6f))
                        g.DrawEllipse(p, cx - ripR, cy - ripR * 0.3f, ripR * 2, ripR * 0.6f);
            }

            // === TAIL — flowing, fan-shaped ===
            float tailBase = cx - dir * 7;
            float tailWave = (float)Math.Sin(t * 40) * 5;
            float tailWave2 = (float)Math.Sin(t * 35 + 1) * 3;

            // Tail fan — multiple flowing fins
            for (int fin = 0; fin < 5; fin++) {
                float finAngle = (float)(-0.4 + fin * 0.2) + tailWave * 0.05f;
                float finLen = 8 + fin * 1.5f - Math.Abs(fin - 2) * 0.8f;
                float ftx = tailBase - dir * finLen + tailWave * (fin * 0.15f);
                float fty = cy + (float)Math.Sin(finAngle) * finLen * 0.8f + tailWave2 * (fin * 0.1f);
                int finA = (int)(a * (0.5f - Math.Abs(fin - 2) * 0.05f) * bodyFade);
                Color finC = fin % 2 == 0 ? Color.FromArgb(finA, 255, 100, 40) : Color.FromArgb(finA, 255, 140, 60);
                PointF[] finShape = {
                    new PointF(tailBase, cy),
                    new PointF(tailBase - dir * finLen * 0.5f, cy + (fin - 2) * 2 + tailWave * 0.3f),
                    new PointF(ftx, fty)
                };
                using (var b = new SolidBrush(finC))
                    g.FillPolygon(b, finShape);
            }

            // Tail edge shimmer
            float shimmer = 0.5f + 0.5f * (float)Math.Sin(t * 50);
            int shA = (int)(a * 0.3f * shimmer * bodyFade);
            using (var p = new Pen(Color.FromArgb(shA, 255, 200, 150), 0.5f))
                g.DrawLine(p, tailBase - dir * 8 + tailWave * 0.5f, cy - 4, tailBase - dir * 10 + tailWave * 0.7f, cy + 4);

            // === BODY — sleek koi shape ===
            // Body glow
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.1f * bodyFade), 255, 150, 80)))
                g.FillEllipse(b, cx - 10, cy - 6, 20, 12);

            // Main body
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.85f * bodyFade), 255, 120, 50)))
                g.FillEllipse(b, cx - 7, cy - 3.5f, 14, 7);

            // White patches (kohaku pattern)
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * bodyFade), 255, 240, 230))) {
                g.FillEllipse(b, cx - 3, cy - 2.5f, 6, 4);
                g.FillEllipse(b, cx + 2, cy - 1, 4, 2.5f);
            }

            // Scale pattern — subtle overlapping arcs
            for (int sc = 0; sc < 6; sc++) {
                float scX = cx - 4 + sc * 2;
                float scPulse = 0.3f + 0.3f * (float)Math.Sin(t * 15 + sc * 1.2f);
                int scA = (int)(a * 0.15f * scPulse * bodyFade);
                using (var p = new Pen(Color.FromArgb(scA, 255, 180, 120), 0.3f))
                    g.DrawArc(p, scX - 1.5f, cy - 1.5f, 3, 3, 200, 140);
            }

            // Dorsal fin
            float dorsalWave = (float)Math.Sin(t * 28) * 1.5f;
            PointF[] dorsal = {
                new PointF(cx - 2, cy - 3),
                new PointF(cx, cy - 6 + dorsalWave),
                new PointF(cx + 3, cy - 3.5f)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * bodyFade), 255, 100, 40)))
                g.FillPolygon(b, dorsal);

            // === HEAD ===
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * bodyFade), 255, 130, 55)))
                g.FillEllipse(b, cx + dir * 3, cy - 3, 7, 6);

            // Eye
            float eyeX = cx + dir * 6, eyeY = cy - 1;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * bodyFade), 20, 20, 20)))
                g.FillEllipse(b, eyeX - 1.2f, eyeY - 1.2f, 2.4f, 2.4f);
            // Eye shine
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * bodyFade), 255, 255, 255)))
                g.FillEllipse(b, eyeX - 0.4f + dir * 0.3f, eyeY - 0.8f, 0.8f, 0.8f);

            // Mouth
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f * bodyFade), 200, 80, 40), 0.5f))
                g.DrawArc(p, cx + dir * 6.5f, eyeY + 0.5f, 2, 1.5f, dir > 0 ? 0 : 180, 120);

            // Barbels (whiskers)
            for (int barb = -1; barb <= 1; barb += 2) {
                float bWave = (float)Math.Sin(t * 25 + barb) * 2;
                int ba = (int)(a * 0.4f * bodyFade);
                using (var p = new Pen(Color.FromArgb(ba, 255, 150, 80), 0.4f))
                    g.DrawBezier(p, cx + dir * 7, cy + 0.5f * barb, cx + dir * 9, cy + barb * 2 + bWave,
                        cx + dir * 10, cy + barb * 3 + bWave, cx + dir * 11, cy + barb * 2.5f + bWave * 0.5f);
            }

            // === BUBBLE TRAIL ===
            for (int bub = 0; bub < 6; bub++) {
                float bubLife = (t * 4 + bub * 0.15f) % 1.0f;
                float bubX = cx - dir * (4 + bubLife * 20) + (float)Math.Sin(t * 20 + bub * 2) * 3;
                float bubY = cy - 3 - bubLife * 10;
                float bubR = 1 + bubLife * 1.5f;
                float bubFade = bubLife < 0.1f ? bubLife / 0.1f : (1f - bubLife) / 0.9f;
                int bubA = (int)(a * 0.2f * bubFade * bodyFade);
                if (bubA > 0) {
                    using (var p = new Pen(Color.FromArgb(bubA, 200, 220, 255), 0.4f))
                        g.DrawEllipse(p, bubX - bubR, bubY - bubR, bubR * 2, bubR * 2);
                    using (var b = new SolidBrush(Color.FromArgb(bubA / 3, 255, 255, 255)))
                        g.FillEllipse(b, bubX - bubR * 0.3f, bubY - bubR * 0.5f, bubR * 0.5f, bubR * 0.4f);
                }
            }
        }
        static void PaintCelestialHarp(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(200 * fade);

            float op;
            if (t < 0.15f) op = t / 0.15f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));

            // === ETHEREAL GLOW — golden light behind harp ===
            for (int gl = 4; gl >= 1; gl--) {
                float gr = 10 + gl * 8;
                float glPulse = 0.4f + 0.6f * (float)Math.Sin(t * 8 + gl * 0.5f);
                int ga = (int)(a * 0.04f * glPulse * op / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 255, 230, 150)))
                        g.FillEllipse(b, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            // === FRAME — elegant curved pillar and neck ===
            float harpH = 22 * op, harpW = 14 * op;

            // Pillar (left curved column)
            int frameA = (int)(a * 0.7f * op);
            using (var p = new Pen(Color.FromArgb(frameA, 220, 190, 110), 1.5f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawBezier(p, cx - harpW * 0.5f, cy + harpH * 0.4f,
                    cx - harpW * 0.6f, cy - harpH * 0.1f,
                    cx - harpW * 0.4f, cy - harpH * 0.35f,
                    cx - harpW * 0.1f, cy - harpH * 0.45f);
            }

            // Neck (top curve)
            using (var p = new Pen(Color.FromArgb(frameA, 220, 190, 110), 1.2f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawBezier(p, cx - harpW * 0.1f, cy - harpH * 0.45f,
                    cx + harpW * 0.15f, cy - harpH * 0.5f,
                    cx + harpW * 0.35f, cy - harpH * 0.4f,
                    cx + harpW * 0.45f, cy - harpH * 0.15f);
            }

            // Soundboard (base)
            using (var p = new Pen(Color.FromArgb(frameA, 200, 170, 90), 1.0f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, cx - harpW * 0.5f, cy + harpH * 0.4f, cx + harpW * 0.4f, cy + harpH * 0.1f);
            }

            // Decorative crown at top
            float crownX = cx - harpW * 0.1f, crownY = cy - harpH * 0.45f;
            float crownPulse = 0.5f + 0.5f * (float)Math.Sin(t * 15);
            int crownA = (int)(a * 0.5f * crownPulse * op);
            using (var b = new SolidBrush(Color.FromArgb(crownA, 255, 240, 160)))
                g.FillEllipse(b, crownX - 2.5f, crownY - 2.5f, 5, 5);
            using (var b = new SolidBrush(Color.FromArgb(crownA / 3, 255, 240, 160)))
                g.FillEllipse(b, crownX - 5, crownY - 5, 10, 10);

            // === STRINGS — 9 glowing strings with vibration ===
            int stringCount = 9;
            for (int s = 0; s < stringCount; s++) {
                float sT = s / (float)(stringCount - 1);

                // String attachment points
                float topX = cx - harpW * 0.05f + sT * harpW * 0.5f;
                float topY = cy - harpH * (0.45f - sT * 0.3f);
                float botX = cx - harpW * 0.45f + sT * harpW * 0.8f;
                float botY = cy + harpH * (0.35f - sT * 0.25f);

                // String vibration — each string has its own frequency
                float vib = (float)Math.Sin(t * (60 + s * 8) + s * 1.5f) * (1.5f + s * 0.3f) * op;
                // Pluck wave — sequential plucking animation
                float pluckPhase = (t * 4 + s * 0.1f) % 1.0f;
                float pluckAmp = pluckPhase < 0.3f ? (float)Math.Sin(pluckPhase / 0.3f * Math.PI) * 3 : 0;
                vib += pluckAmp;

                // String glow
                float stringBright = 0.3f + 0.4f * (float)Math.Max(0, Math.Sin(t * 50 + s * 2f));
                if (pluckPhase < 0.3f) stringBright += 0.3f * (1f - pluckPhase / 0.3f);

                int sa = (int)(a * 0.6f * stringBright * op);
                Color stringColor = s % 3 == 0 ? Color.FromArgb(sa, 255, 240, 160)
                    : s % 3 == 1 ? Color.FromArgb(sa, 220, 200, 255) : Color.FromArgb(sa, 200, 230, 255);

                float midX = (topX + botX) / 2 + vib;
                float midY = (topY + botY) / 2;
                using (var p = new Pen(stringColor, 0.6f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawBezier(p, topX, topY, midX + vib * 0.5f, midY - (botY - topY) * 0.2f,
                        midX + vib * 0.5f, midY + (botY - topY) * 0.2f, botX, botY);
                }

                // String glow halo
                if (stringBright > 0.5f) {
                    int glowA = (int)(sa * 0.2f);
                    using (var p = new Pen(stringColor.A > 0 ? Color.FromArgb(glowA, stringColor.R, stringColor.G, stringColor.B) : Color.FromArgb(glowA, 255, 240, 160), 2.5f))
                        g.DrawBezier(p, topX, topY, midX + vib * 0.3f, midY - (botY - topY) * 0.2f,
                            midX + vib * 0.3f, midY + (botY - topY) * 0.2f, botX, botY);
                }
            }

            // === MUSIC NOTES — rising from strings ===
            var rng = new Random(ev.Seed);
            for (int note = 0; note < 8; note++) {
                float noteLife = (t * 2.5f + note * 0.12f) % 1.0f;
                float noteX = cx + (float)(rng.NextDouble() - 0.5) * harpW + (float)Math.Sin(t * 10 + note * 2) * 6;
                float noteY = cy - harpH * 0.2f - noteLife * 30;
                float noteFade = noteLife < 0.1f ? noteLife / 0.1f : (1f - noteLife) / 0.9f;
                int na = (int)(a * 0.3f * noteFade * op);
                if (na <= 0) continue;
                float nsz = 1.5f + (1f - noteLife) * 0.5f;
                Color nc = note % 3 == 0 ? Color.FromArgb(na, 255, 240, 180)
                    : note % 3 == 1 ? Color.FromArgb(na, 200, 180, 255) : Color.FromArgb(na, 180, 220, 255);
                using (var b = new SolidBrush(nc))
                    g.FillEllipse(b, noteX - nsz, noteY - nsz, nsz * 2, nsz * 2);
                // Note stem
                using (var p = new Pen(Color.FromArgb(na / 2, nc.R, nc.G, nc.B), 0.3f))
                    g.DrawLine(p, noteX + nsz * 0.7f, noteY, noteX + nsz * 0.7f, noteY - nsz * 2);
            }

            // === HARMONIC WAVES — concentric sound ripples ===
            for (int hw = 0; hw < 3; hw++) {
                float hwPhase = (t * 2 + hw * 0.33f) % 1.0f;
                float hwR = 5 + hwPhase * 30;
                int hwA = (int)(a * 0.08f * (1f - hwPhase) * op);
                if (hwA > 0)
                    using (var p = new Pen(Color.FromArgb(hwA, 255, 230, 160), 0.5f))
                        g.DrawEllipse(p, cx - hwR, cy - hwR * 0.6f, hwR * 2, hwR * 1.2f);
            }
        }
        static void PaintMeteorDragon(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            int a = (int)(210 * fade);
            float dir = ev.DirX > 0 ? 1 : -1;

            float bodyFade;
            if (t < 0.08f) bodyFade = t / 0.08f;
            else if (t > 0.90f) bodyFade = (1f - t) / 0.10f;
            else bodyFade = 1f;
            bodyFade = Math.Max(0, Math.Min(1, bodyFade));

            // === FIRE TRAIL — blazing ember wake ===
            var trailRng = new Random(ev.Seed + 50);
            for (int tr = 0; tr < 20; tr++) {
                float trLife = (t * 5 + tr * 0.045f) % 1.0f;
                float trDist = 5 + trLife * 40;
                float trSpread = trLife * 12;
                float trx = cx - dir * trDist + (float)(trailRng.NextDouble() - 0.5) * trSpread;
                float trY2 = cy + (float)(trailRng.NextDouble() - 0.5) * trSpread;
                float trFade = trLife < 0.1f ? trLife / 0.1f : (1f - trLife) / 0.9f;
                int tra = (int)(a * 0.2f * trFade * bodyFade);
                if (tra <= 0) continue;
                float trsz = 1.5f + (1f - trLife) * 2.5f;
                Color trColor = trLife < 0.3f ? Color.FromArgb(tra, 255, 220, 100) :
                    trLife < 0.6f ? Color.FromArgb(tra, 255, 150, 40) : Color.FromArgb(tra, 200, 60, 20);
                using (var b = new SolidBrush(trColor))
                    g.FillEllipse(b, trx - trsz, trY2 - trsz, trsz * 2, trsz * 2);
            }

            // === BODY — muscular serpentine with heat glow ===
            int segCount = 18;
            float[] segX = new float[segCount], segY = new float[segCount];
            for (int seg = 0; seg < segCount; seg++) {
                float d = seg * 4;
                segX[seg] = cx - dir * d;
                segY[seg] = cy + (float)Math.Sin(t * 32 - seg * 0.65f) * (3 + seg * 0.2f);
            }

            // Heat distortion aura
            for (int seg = 0; seg < segCount; seg += 2) {
                float heatPulse = 0.4f + 0.6f * (float)Math.Sin(t * 50 + seg * 1.1f);
                float heatR = (5 + heatPulse * 3) * bodyFade;
                int ha = (int)(a * 0.04f * heatPulse * bodyFade);
                if (ha > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ha, 255, 100, 20)))
                        g.FillEllipse(b, segX[seg] - heatR, segY[seg] - heatR, heatR * 2, heatR * 2);
            }

            // Body segments — molten scales
            for (int seg = segCount - 1; seg >= 0; seg--) {
                float sz = (4.2f - seg * 0.15f) * bodyFade;
                int sr = (int)(255);
                int sg2 = (int)(120 - seg * 5);
                int sb = (int)(30 + seg * 3);
                sg2 = Math.Max(0, Math.Min(255, sg2));
                sb = Math.Max(0, Math.Min(255, sb));

                int sa = (int)(a * (1f - seg * 0.035f) * bodyFade);
                if (sa <= 0 || sz <= 0) continue;

                // Scale glow
                using (var b = new SolidBrush(Color.FromArgb(sa / 3, sr, sg2, sb)))
                    g.FillEllipse(b, segX[seg] - sz * 1.4f, segY[seg] - sz * 0.7f, sz * 2.8f, sz * 1.4f);

                // Main scale
                using (var b = new SolidBrush(Color.FromArgb(sa, sr, sg2, sb)))
                    g.FillEllipse(b, segX[seg] - sz, segY[seg] - sz * 0.5f, sz * 2, sz);

                // Lava vein highlight
                if (seg % 2 == 0 && seg < 14) {
                    float veinPulse = 0.3f + 0.7f * (float)Math.Max(0, Math.Sin(t * 40 + seg));
                    int va = (int)(sa * 0.4f * veinPulse);
                    using (var b = new SolidBrush(Color.FromArgb(va, 255, 220, 80)))
                        g.FillEllipse(b, segX[seg] - sz * 0.4f, segY[seg] - sz * 0.15f, sz * 0.8f, sz * 0.3f);
                }

                // Back ridges — spiky
                if (seg % 2 == 0 && seg < 14) {
                    float ridgeH = (4 - seg * 0.2f) * bodyFade;
                    float ridgeAngle = (float)(Math.PI * -0.45 + Math.Sin(t * 15 + seg * 0.3f) * 0.15);
                    float rx = segX[seg] + (float)Math.Cos(ridgeAngle) * ridgeH * 0.3f;
                    float ry = segY[seg] + (float)Math.Sin(ridgeAngle) * ridgeH;
                    int ra = (int)(sa * 0.6f);
                    using (var p = new Pen(Color.FromArgb(ra, 255, 140, 40), 1.2f)) {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        g.DrawLine(p, segX[seg], segY[seg] - sz * 0.3f, rx, ry);
                    }
                }
            }

            // === HEAD — fierce, angular ===
            float headSz = 5.5f * bodyFade;
            PointF[] head = {
                new PointF(cx + dir * 7, cy),
                new PointF(cx + dir * 3, cy - headSz),
                new PointF(cx - dir * 1, cy - headSz * 0.6f),
                new PointF(cx - dir * 2, cy),
                new PointF(cx - dir * 1, cy + headSz * 0.5f),
                new PointF(cx + dir * 3, cy + headSz * 0.6f)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.9f * bodyFade), 255, 100, 30)))
                g.FillPolygon(b, head);

            // Eyes — burning embers
            float eyePulse = 0.6f + 0.4f * (float)Math.Sin(t * 55);
            for (int eye = -1; eye <= 1; eye += 2) {
                float eyeX = cx + dir * 4, eyeY = cy + eye * 2;
                int eyeA = (int)(a * 0.9f * eyePulse * bodyFade);
                using (var b = new SolidBrush(Color.FromArgb(eyeA / 3, 255, 255, 100)))
                    g.FillEllipse(b, eyeX - 3, eyeY - 3, 6, 6);
                using (var b = new SolidBrush(Color.FromArgb(eyeA, 255, 255, 150)))
                    g.FillEllipse(b, eyeX - 1.2f, eyeY - 1, 2.4f, 2);
            }

            // Horns
            for (int horn = -1; horn <= 1; horn += 2) {
                float hLen = 7 * bodyFade;
                float hAngle = (float)(Math.PI * (-0.3 + horn * 0.15));
                float hx = cx + dir * 2 + (float)Math.Cos(hAngle) * hLen * dir;
                float hy = cy - headSz * 0.5f + (float)Math.Sin(hAngle) * hLen;
                int ha = (int)(a * 0.7f * bodyFade);
                using (var p = new Pen(Color.FromArgb(ha, 200, 80, 20), 1.2f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx + dir * 2, cy - headSz * 0.5f + horn * 1.5f, hx, hy);
                }
            }

            // === FIRE BREATH — cone of flame ===
            var breathRng = new Random(ev.Seed + (int)(t * 30));
            float breathCycle = (float)Math.Sin(t * 15);
            if (breathCycle > 0) {
                for (int fb = 0; fb < 10; fb++) {
                    float fbLife = (float)breathRng.NextDouble();
                    float fbDist = fbLife * 18 * breathCycle;
                    float fbSpread = fbLife * 8;
                    float fbx = cx + dir * (8 + fbDist);
                    float fby = cy + (float)(breathRng.NextDouble() - 0.5) * fbSpread;
                    int fba = (int)(a * 0.4f * (1f - fbLife) * breathCycle * bodyFade);
                    if (fba <= 0) continue;
                    float fbsz = 2 + (1f - fbLife) * 2;
                    Color fbc = fbLife < 0.3f ? Color.FromArgb(fba, 255, 255, 180) :
                        fbLife < 0.6f ? Color.FromArgb(fba, 255, 200, 50) : Color.FromArgb(fba, 255, 120, 20);
                    using (var b = new SolidBrush(fbc))
                        g.FillEllipse(b, fbx - fbsz, fby - fbsz, fbsz * 2, fbsz * 2);
                }
            }
        }
        static void PaintNorthernLights(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(200 * fade);
            float op;
            if (t < 0.15f) op = t / 0.15f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));

            float curtainWidth = 90 * op;
            float curtainHeight = 60 * op;
            int columns = 24;

            // === CURTAIN RIBBONS ===
            for (int col = 0; col < columns; col++) {
                float colT = col / (float)(columns - 1);
                float baseX = cx - curtainWidth / 2 + colT * curtainWidth;
                float fold = (float)Math.Sin(t * 8 + colT * 4) * 12 * op;
                float fold2 = (float)Math.Sin(t * 5 + colT * 6 + 1.5f) * 6 * op;
                float xPos = baseX + fold + fold2;
                float heightMul = 0.6f + 0.4f * (float)Math.Sin(t * 3 + colT * 8);
                float topY = cy - curtainHeight * 0.3f * heightMul;
                float bottomY = cy + curtainHeight * 0.7f * heightMul;
                float ribbonH = bottomY - topY;
                if (ribbonH < 2) continue;
                float edgeFactor = Math.Abs(colT - 0.5f) * 2;
                float brightPulse = 0.5f + 0.5f * (float)Math.Sin(t * 15 + colT * 7);

                int segments = 8;
                for (int seg = 0; seg < segments; seg++) {
                    float segT = seg / (float)(segments - 1);
                    float segY = topY + segT * ribbonH;
                    float segH = ribbonH / segments + 1;
                    int segR, segG, segB;
                    if (segT < 0.25f) {
                        float blend = segT / 0.25f;
                        segR = (int)(180 - blend * 100 + edgeFactor * 60);
                        segG = (int)(50 + blend * 150);
                        segB = (int)(200 - blend * 80);
                    } else if (segT < 0.65f) {
                        segR = (int)(50 + edgeFactor * 40);
                        segG = (int)(220 + brightPulse * 35);
                        segB = (int)(100 + edgeFactor * 50);
                    } else {
                        float fadeT = (segT - 0.65f) / 0.35f;
                        segR = (int)(50 * (1f - fadeT));
                        segG = (int)(200 * (1f - fadeT));
                        segB = (int)(80 * (1f - fadeT));
                    }
                    float colBright = brightPulse * (0.5f + 0.5f * (float)Math.Sin(t * 20 + col * 0.8f));
                    float vertFade = segT < 0.1f ? segT / 0.1f : segT > 0.85f ? (1f - segT) / 0.15f : 1f;
                    int segA = (int)(a * 0.35f * op * colBright * vertFade);
                    if (segA <= 0) continue;
                    segR = Math.Max(0, Math.Min(255, segR));
                    segG = Math.Max(0, Math.Min(255, segG));
                    segB = Math.Max(0, Math.Min(255, segB));
                    float ribbonWidth = (2.5f + 2f * (float)Math.Sin(segT * Math.PI)) * op;
                    float segWobble = (float)Math.Sin(t * 12 + segT * 5 + colT * 3) * 2 * op;
                    using (var b = new SolidBrush(Color.FromArgb(segA, segR, segG, segB)))
                        g.FillRectangle(b, xPos + segWobble - ribbonWidth / 2, segY, ribbonWidth, segH);
                }

                if (col % 3 == 0 && brightPulse > 0.7f) {
                    float hlH = ribbonH * 0.4f;
                    float hlY = topY + ribbonH * 0.2f;
                    int hlA = (int)(a * 0.15f * (brightPulse - 0.7f) * 3.3f * op);
                    using (var p = new Pen(Color.FromArgb(hlA, 100, 255, 140), 0.8f)) {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        float wobTop = (float)Math.Sin(t * 10 + colT * 4) * 3;
                        float wobBot = (float)Math.Sin(t * 10 + colT * 4 + 2) * 3;
                        g.DrawLine(p, xPos + wobTop, hlY, xPos + wobBot, hlY + hlH);
                    }
                }
            }

            // === SHIMMER SPARKLES ===
            var sparkRng = new Random(ev.Seed + (int)(t * 20));
            for (int sp = 0; sp < 10; sp++) {
                float spx = cx + (float)(sparkRng.NextDouble() - 0.5) * curtainWidth;
                float spy = cy + (float)(sparkRng.NextDouble() - 0.5) * curtainHeight * 0.6f;
                float spBright = (float)Math.Max(0, Math.Sin(t * 30 + sp * 3.7f));
                if (spBright < 0.5f) continue;
                int spa = (int)(a * 0.4f * spBright * op);
                using (var b = new SolidBrush(Color.FromArgb(spa, 200, 255, 200)))
                    g.FillEllipse(b, spx - 1, spy - 1, 2, 2);
            }
        }
        static void PaintDarkMatter(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(200 * fade);
            float op;
            if (t < 0.15f) op = t / 0.15f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;
            op = Math.Max(0, Math.Min(1, op));
            float massR = 18 * op;

            // === EINSTEIN RING — gravitational lensing ===
            float einsteinR = massR * 1.2f;
            if (einsteinR > 2) {
                for (int ring = 3; ring >= 0; ring--) {
                    float rr = einsteinR + ring * 3;
                    float pulse = 0.6f + 0.4f * (float)Math.Sin(t * 8 + ring * 1.5f);
                    int ra = (int)(a * 0.18f * pulse * op);
                    if (ra > 0)
                        using (var p = new Pen(Color.FromArgb(ra, 160, 140, 230), 1.8f - ring * 0.2f))
                            g.DrawEllipse(p, cx - rr, cy - rr, rr * 2, rr * 2);
                }
                float ringPulse = 0.7f + 0.3f * (float)Math.Sin(t * 12);
                int ringA = (int)(a * 0.45f * ringPulse * op);
                using (var p = new Pen(Color.FromArgb(ringA, 200, 180, 255), 2.5f))
                    g.DrawEllipse(p, cx - einsteinR, cy - einsteinR, einsteinR * 2, einsteinR * 2);

                // Lensed arcs
                var arcRng = new Random(ev.Seed + 100);
                for (int arc = 0; arc < 4; arc++) {
                    float arcAngle = (float)(arcRng.NextDouble() * 360);
                    float arcLen = 40 + (float)arcRng.NextDouble() * 60;
                    float arcR = einsteinR + 1 + (float)arcRng.NextDouble() * 3;
                    float arcBright = 0.4f + 0.6f * (float)Math.Sin(t * 6 + arc * 2.3f);
                    int arcA = (int)(a * 0.5f * arcBright * op);
                    if (arcA > 0) {
                        Color arcColor = arc % 2 == 0
                            ? Color.FromArgb(arcA, 220, 200, 255)
                            : Color.FromArgb(arcA, 200, 220, 255);
                        using (var p = new Pen(arcColor, 1.5f))
                            g.DrawArc(p, cx - arcR, cy - arcR, arcR * 2, arcR * 2, arcAngle + t * 5, arcLen);
                    }
                }
            }

            // === DARK CORE ===
            float coreR = massR * 0.7f;
            if (coreR > 1) {
                for (int dl = 4; dl >= 0; dl--) {
                    float dlR = coreR * (0.3f + dl * 0.18f);
                    int dlA = (int)(a * 0.06f * (5 - dl) * op);
                    using (var b = new SolidBrush(Color.FromArgb(dlA, 8, 5, 18)))
                        g.FillEllipse(b, cx - dlR, cy - dlR, dlR * 2, dlR * 2);
                }
            }

            // === BENT STARLIGHT — orbiting, tidally stretched ===
            var rng = new Random(ev.Seed);
            for (int pt = 0; pt < 20; pt++) {
                float baseAngle = (float)(rng.NextDouble() * Math.PI * 2);
                float orbitR = massR + 3 + (float)rng.NextDouble() * 20;
                float orbitSpeed = 1.5f + (float)rng.NextDouble() * 2f;
                float spiralDecay = 1f - (float)Math.Min(0.3f, t * 0.2f);
                float currentR = orbitR * spiralDecay;
                float currentAngle = baseAngle + t * orbitSpeed * 15;
                float px = cx + (float)Math.Cos(currentAngle) * currentR;
                float py = cy + (float)Math.Sin(currentAngle) * currentR;
                float distFromCore = currentR / massR;
                float stretch = distFromCore < 1.5f ? 2f + (1.5f - distFromCore) * 3f : 1f;
                float ptBright = 0.4f + 0.6f * (float)Math.Sin(t * 25 + pt * 2.7f);
                int pa = (int)(a * 0.55f * ptBright * op);
                if (pa <= 0) continue;
                Color ptColor = distFromCore < 1.5f
                    ? Color.FromArgb(pa, 160, 160, 255)
                    : Color.FromArgb(pa, 200, 190, 230);
                if (stretch > 1.5f) {
                    float tangent = currentAngle + (float)(Math.PI / 2);
                    float sx1 = px - (float)Math.Cos(tangent) * stretch;
                    float sy1 = py - (float)Math.Sin(tangent) * stretch;
                    float sx2 = px + (float)Math.Cos(tangent) * stretch;
                    float sy2 = py + (float)Math.Sin(tangent) * stretch;
                    using (var p = new Pen(ptColor, 0.8f)) {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        g.DrawLine(p, sx1, sy1, sx2, sy2);
                    }
                } else {
                    using (var b = new SolidBrush(ptColor))
                        g.FillEllipse(b, px - 1, py - 1, 2, 2);
                }
            }

            // === FRAME-DRAG RIPPLES ===
            float ripplePhase = (t * 4) % 1.0f;
            if (ripplePhase < 0.6f) {
                float ripR = massR + ripplePhase * 30;
                int ripA = (int)(a * 0.14f * (1f - ripplePhase / 0.6f) * op);
                if (ripA > 0)
                    using (var p = new Pen(Color.FromArgb(ripA, 150, 130, 210), 0.8f))
                        g.DrawEllipse(p, cx - ripR, cy - ripR, ripR * 2, ripR * 2);
            }
            float ripplePhase2 = ((t + 0.3f) * 4) % 1.0f;
            if (ripplePhase2 < 0.6f) {
                float ripR = massR + ripplePhase2 * 25;
                int ripA = (int)(a * 0.10f * (1f - ripplePhase2 / 0.6f) * op);
                if (ripA > 0)
                    using (var p = new Pen(Color.FromArgb(ripA, 130, 110, 190), 0.8f))
                        g.DrawEllipse(p, cx - ripR, cy - ripR, ripR * 2, ripR * 2);
            }

            // === PHOTON REDIRECT FLICKERS ===
            var flickRng = new Random(ev.Seed + (int)(t * 15));
            if (flickRng.NextDouble() < 0.35f) {
                float fAngle = (float)(flickRng.NextDouble() * Math.PI * 2);
                float fDist = massR * 0.8f + (float)flickRng.NextDouble() * massR * 0.5f;
                float fx = cx + (float)Math.Cos(fAngle) * fDist;
                float fy = cy + (float)Math.Sin(fAngle) * fDist;
                int fa = (int)(a * 0.4f * op);
                using (var b = new SolidBrush(Color.FromArgb(fa, 220, 210, 255)))
                    g.FillEllipse(b, fx - 1.5f, fy - 1.5f, 3, 3);
                float sAngle = fAngle + (float)(Math.PI / 2) * (flickRng.NextDouble() > 0.5 ? 1 : -1);
                float sLen = 4 + (float)flickRng.NextDouble() * 6;
                using (var p = new Pen(Color.FromArgb(fa / 2, 200, 190, 255), 0.6f))
                    g.DrawLine(p, fx, fy, fx + (float)Math.Cos(sAngle) * sLen, fy + (float)Math.Sin(sAngle) * sLen);
            }
        }

        // ===== WAVE 4 — ICONS & WHIMSY =====

        static void PaintSantaSleigh(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;
            float bob = (float)Math.Sin(t * 12) * 3;
            cy += bob;

            // Reindeer — 3 tiny deer in formation ahead of sleigh
            for (int d = 0; d < 3; d++) {
                float dx = cx + dir * (28 + d * 14), dy = cy - 4 + (float)Math.Sin(t * 16 + d * 1.5) * 2;
                int da = (int)(a * 0.7f);
                // body
                using (var b = new SolidBrush(Color.FromArgb(da, 160, 120, 80)))
                    g.FillEllipse(b, dx - 4, dy - 2, 8, 4);
                // legs — tiny lines
                using (var p = new Pen(Color.FromArgb(da, 140, 100, 60), 0.7f)) {
                    g.DrawLine(p, dx - 2, dy + 2, dx - 2, dy + 5);
                    g.DrawLine(p, dx + 2, dy + 2, dx + 2, dy + 5);
                }
                // antlers
                using (var p = new Pen(Color.FromArgb(da, 180, 140, 90), 0.6f)) {
                    g.DrawLine(p, dx, dy - 2, dx - 1, dy - 5);
                    g.DrawLine(p, dx, dy - 2, dx + 1, dy - 5);
                }
                // Rudolph nose on lead deer
                if (d == 2) {
                    float pulse = 0.6f + 0.4f * (float)Math.Sin(t * 20);
                    using (var b = new SolidBrush(Color.FromArgb((int)(a * pulse), 255, 60, 40)))
                        g.FillEllipse(b, dx + dir * 5 - 1.5f, dy - 1.5f, 3, 3);
                    using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f * pulse), 255, 80, 50)))
                        g.FillEllipse(b, dx + dir * 5 - 3, dy - 3, 6, 6);
                }
            }

            // Reins — thin lines connecting deer to sleigh
            using (var p = new Pen(Color.FromArgb((int)(a * 0.3f), 200, 180, 140), 0.5f)) {
                g.DrawLine(p, cx + dir * 28, cy - 4, cx + dir * 10, cy);
                g.DrawLine(p, cx + dir * 42, cy - 4, cx + dir * 28, cy - 4);
            }

            // Sleigh body — curved base
            PointF[] sleigh = dir > 0
                ? new[] { new PointF(cx - 8, cy - 3), new PointF(cx + 8, cy - 3), new PointF(cx + 10, cy + 3), new PointF(cx - 4, cy + 3), new PointF(cx - 8, cy) }
                : new[] { new PointF(cx + 8, cy - 3), new PointF(cx - 8, cy - 3), new PointF(cx - 10, cy + 3), new PointF(cx + 4, cy + 3), new PointF(cx + 8, cy) };
            using (var b = new SolidBrush(Color.FromArgb(a, 180, 30, 30)))
                g.FillPolygon(b, sleigh);
            // Runner
            using (var p = new Pen(Color.FromArgb(a, 220, 200, 100), 1f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, cx - 8 * dir, cy + 3, cx + 12 * dir, cy + 4);
            }

            // Santa — tiny figure in sleigh
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 40, 40)))
                g.FillEllipse(b, cx - 3, cy - 9, 6, 6); // body
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 200, 170)))
                g.FillEllipse(b, cx - 2, cy - 12, 4, 4); // face
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 220, 220)))
                g.FillEllipse(b, cx - 2.5f, cy - 14, 5, 3); // hat brim
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 30, 30)))
                g.FillEllipse(b, cx - 1.5f, cy - 16, 3, 3); // hat top

            // Star trail behind sleigh
            var rng = new Random(ev.Seed);
            for (int s = 0; s < 12; s++) {
                float st = s / 12f;
                float sx = cx - dir * (10 + st * 30) + (float)(rng.NextDouble() - 0.5) * 6;
                float sy = cy + (float)(rng.NextDouble() - 0.5) * 8;
                float sparkFade = (1f - st) * fade;
                int sa = (int)(sparkFade * 120);
                if (sa > 0) {
                    float ssz = 1 + (float)rng.NextDouble() * 2;
                    using (var b = new SolidBrush(Color.FromArgb(sa, 255, 230, 100)))
                        g.FillEllipse(b, sx - ssz, sy - ssz, ssz * 2, ssz * 2);
                }
            }
        }

        static void PaintAngel(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 210);
            float bob = (float)Math.Sin(t * 10) * 4;
            cy += bob;
            float wingPhase = (float)Math.Sin(t * 14) * 0.3f;

            // Outer golden glow
            for (int gl = 3; gl >= 1; gl--) {
                float gr = 14 + gl * 6;
                int ga = (int)(a * 0.08f / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 255, 240, 160)))
                        g.FillEllipse(b, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            // Wings — larger, more prominent, golden-white
            float wingSpan = 15 + wingPhase * 5;
            for (int side = -1; side <= 1; side += 2) {
                PointF[] wing = new[] {
                    new PointF(cx, cy - 2),
                    new PointF(cx + side * wingSpan, cy - 10 - wingPhase * side * 3),
                    new PointF(cx + side * wingSpan * 0.8f, cy + 3),
                    new PointF(cx, cy + 2)
                };
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.55f), 255, 250, 220)))
                    g.FillPolygon(b, wing);
                // Wing edge glow
                using (var p = new Pen(Color.FromArgb((int)(a * 0.4f), 255, 230, 150), 0.8f)) {
                    g.DrawLine(p, wing[0].X, wing[0].Y, wing[1].X, wing[1].Y);
                    g.DrawLine(p, wing[1].X, wing[1].Y, wing[2].X, wing[2].Y);
                }
            }

            // Body — flowing robe
            PointF[] robe = new[] {
                new PointF(cx - 3, cy - 2), new PointF(cx + 3, cy - 2),
                new PointF(cx + 5, cy + 10), new PointF(cx - 5, cy + 10)
            };
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 240, 255)))
                g.FillPolygon(b, robe);

            // Head
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 230, 200)))
                g.FillEllipse(b, cx - 3, cy - 8, 6, 6);

            // Halo — golden ring above head — PROMINENT
            float haloPulse = 0.7f + 0.3f * (float)Math.Sin(t * 18);
            using (var p = new Pen(Color.FromArgb((int)(a * haloPulse), 255, 220, 80), 1.8f))
                g.DrawEllipse(p, cx - 5.5f, cy - 14, 11, 4);
            // Halo glow
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f * haloPulse), 255, 240, 120), 3.5f))
                g.DrawEllipse(p, cx - 6, cy - 14.5f, 12, 5);

            // Sparkle trail
            var rng = new Random(ev.Seed);
            for (int s = 0; s < 8; s++) {
                float sx = cx + (float)(rng.NextDouble() - 0.5) * 20;
                float sy = cy + 10 + (float)rng.NextDouble() * 12;
                float sp = (float)Math.Sin(t * 20 + s * 1.3f);
                int sa = (int)(a * 0.3f * Math.Max(0, sp));
                if (sa > 0) {
                    using (var b = new SolidBrush(Color.FromArgb(sa, 255, 250, 200)))
                        g.FillEllipse(b, sx - 1, sy - 1, 2, 2);
                }
            }
        }

        static void PaintJackOLantern(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float flicker = 0.7f + 0.3f * (float)Math.Sin(t * 25 + (float)Math.Sin(t * 40) * 2);

            // Inner glow — orange light from inside
            for (int gl = 3; gl >= 1; gl--) {
                float gr = 8 + gl * 5;
                int ga = (int)(a * 0.06f * flicker / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 255, 140, 20)))
                        g.FillEllipse(b, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            // Pumpkin body — slightly squished circle
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 120, 20)))
                g.FillEllipse(b, cx - 8, cy - 6, 16, 12);
            // Ridges
            using (var p = new Pen(Color.FromArgb((int)(a * 0.3f), 180, 90, 10), 0.6f)) {
                g.DrawLine(p, cx, cy - 6, cx, cy + 6);
                g.DrawLine(p, cx - 4, cy - 5, cx - 4, cy + 5);
                g.DrawLine(p, cx + 4, cy - 5, cx + 4, cy + 5);
            }

            // Stem
            using (var b = new SolidBrush(Color.FromArgb(a, 80, 140, 40)))
                g.FillRectangle(b, cx - 1.5f, cy - 9, 3, 4);

            // Face — carved triangles with glow
            int fa = (int)(a * flicker);
            // Eyes
            PointF[] leftEye = { new PointF(cx - 5, cy - 3), new PointF(cx - 2, cy - 3), new PointF(cx - 3.5f, cy) };
            PointF[] rightEye = { new PointF(cx + 2, cy - 3), new PointF(cx + 5, cy - 3), new PointF(cx + 3.5f, cy) };
            using (var b = new SolidBrush(Color.FromArgb(fa, 255, 200, 50))) {
                g.FillPolygon(b, leftEye);
                g.FillPolygon(b, rightEye);
            }
            // Mouth — jagged grin
            PointF[] mouth = {
                new PointF(cx - 5, cy + 2), new PointF(cx - 3, cy + 1), new PointF(cx - 1, cy + 3),
                new PointF(cx + 1, cy + 1), new PointF(cx + 3, cy + 3), new PointF(cx + 5, cy + 2),
                new PointF(cx + 4, cy + 4), new PointF(cx - 4, cy + 4)
            };
            using (var b = new SolidBrush(Color.FromArgb(fa, 255, 200, 50)))
                g.FillPolygon(b, mouth);
        }

        static void PaintCupid(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;
            float bob = (float)Math.Sin(t * 12) * 3;
            float wingFlap = (float)Math.Sin(t * 18) * 0.3f;
            cy += bob;

            // Wings — bigger, more visible pink
            for (int side = -1; side <= 1; side += 2) {
                float wSpan = 10 + wingFlap * side * 4;
                PointF[] wing = {
                    new PointF(cx, cy - 3), new PointF(cx + side * wSpan, cy - 8 - wingFlap * 2),
                    new PointF(cx + side * wSpan * 0.6f, cy + 2), new PointF(cx, cy + 1)
                };
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f), 255, 200, 220)))
                    g.FillPolygon(b, wing);
                using (var p = new Pen(Color.FromArgb((int)(a * 0.3f), 255, 160, 190), 0.5f)) {
                    g.DrawLine(p, wing[0].X, wing[0].Y, wing[1].X, wing[1].Y);
                    g.DrawLine(p, wing[1].X, wing[1].Y, wing[2].X, wing[2].Y);
                }
            }

            // Body — cherub, slightly bigger
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 210, 190)))
                g.FillEllipse(b, cx - 4, cy - 3, 8, 8);
            // Head
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 215, 195)))
                g.FillEllipse(b, cx - 3, cy - 7, 6, 6);

            // Bow — arc in front
            float bowX = cx + dir * 6;
            using (var p = new Pen(Color.FromArgb(a, 180, 140, 80), 0.8f))
                g.DrawArc(p, bowX - 2, cy - 5, 4, 10, dir > 0 ? -90 : 90, 180);
            // Bowstring
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f), 200, 200, 200), 0.4f))
                g.DrawLine(p, bowX, cy - 5, bowX, cy + 5);

            // Arrow — shoots at t > 0.25 (earlier so you can see it)
            if (t > 0.25f) {
                float arrowT = (t - 0.25f) / 0.75f;
                float ax = bowX + dir * arrowT * 60;
                float ay = cy + (float)Math.Sin(arrowT * 3) * 2;
                // Arrow shaft
                using (var p = new Pen(Color.FromArgb((int)(a * (1f - arrowT)), 255, 100, 120), 0.8f)) {
                    p.StartCap = LineCap.Round;
                    g.DrawLine(p, ax, ay, ax - dir * 6, ay);
                }
                // Heart tip
                int ha = (int)(a * 0.8f * (1f - arrowT));
                if (ha > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ha, 255, 60, 80)))
                        g.FillEllipse(b, ax - 2, ay - 2, 4, 4);
                // Trail sparkles
                for (int s = 0; s < 5; s++) {
                    float sd = s * 4f;
                    float sa2 = (1f - arrowT) * (1f - s / 5f);
                    int sAlpha = (int)(a * 0.3f * sa2);
                    if (sAlpha > 0)
                        using (var b = new SolidBrush(Color.FromArgb(sAlpha, 255, 150, 180)))
                            g.FillEllipse(b, ax - dir * sd - 1, ay - 1, 2, 2);
                }
            }
        }

        static void PaintStarfighter(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;
            float bank = (float)Math.Sin(t * 8) * 2;
            cy += bank;

            // S-foils — 4 wing tips in X formation
            float wingSpread = 10;
            PointF[] bodyPts = {
                new PointF(cx + dir * 8, cy),          // nose
                new PointF(cx - dir * 2, cy - 2),      // top body
                new PointF(cx - dir * 6, cy - 1),      // rear top
                new PointF(cx - dir * 6, cy + 1),      // rear bottom
                new PointF(cx - dir * 2, cy + 2)       // bottom body
            };
            using (var b = new SolidBrush(Color.FromArgb(a, 180, 180, 190)))
                g.FillPolygon(b, bodyPts);

            // Upper wings
            for (int side = -1; side <= 1; side += 2) {
                PointF[] wing = {
                    new PointF(cx - dir * 2, cy + side * 2),
                    new PointF(cx - dir * 5, cy + side * wingSpread),
                    new PointF(cx - dir * 7, cy + side * wingSpread),
                    new PointF(cx - dir * 6, cy + side * 2)
                };
                using (var b = new SolidBrush(Color.FromArgb(a, 160, 160, 170)))
                    g.FillPolygon(b, wing);
                // Engine glow on wing tip
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f), 255, 120, 80)))
                    g.FillEllipse(b, cx - dir * 7 - 1.5f, cy + side * wingSpread - 1.5f, 3, 3);
            }

            // Main engine glow
            float enginePulse = 0.7f + 0.3f * (float)Math.Sin(t * 30);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.7f * enginePulse), 255, 140, 60)))
                g.FillEllipse(b, cx - dir * 8 - 2, cy - 2, 4, 4);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f * enginePulse), 255, 100, 40)))
                g.FillEllipse(b, cx - dir * 10 - 3, cy - 3, 6, 6);

            // Laser bolts — periodic
            float laserCycle = (t * 8) % 1f;
            if (laserCycle < 0.3f) {
                float lx = cx + dir * (10 + laserCycle * 80);
                int la = (int)(a * 0.8f * (1f - laserCycle / 0.3f));
                using (var p = new Pen(Color.FromArgb(la, 255, 50, 50), 1.2f)) {
                    g.DrawLine(p, lx, cy - 1, lx + dir * 6, cy - 1);
                    g.DrawLine(p, lx, cy + 1, lx + dir * 6, cy + 1);
                }
            }
        }

        static void PaintMechBattle(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float clash = (float)Math.Sin(t * 16) * 3;

            for (int m = 0; m < 2; m++) {
                float mx = cx + (m == 0 ? -8 - clash : 8 + clash);
                float my = cy + (m == 0 ? -2 : 2);
                Color col = m == 0 ? Color.FromArgb(a, 100, 140, 220) : Color.FromArgb(a, 220, 100, 100);

                // Torso
                using (var b = new SolidBrush(col))
                    g.FillRectangle(b, mx - 3, my - 4, 6, 8);
                // Head
                using (var b = new SolidBrush(col))
                    g.FillRectangle(b, mx - 2, my - 6, 4, 3);
                // Eye slit
                using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 200)))
                    g.FillRectangle(b, mx - 1.5f, my - 5.5f, 3, 1);
                // Legs
                using (var p = new Pen(col, 1.2f)) {
                    g.DrawLine(p, mx - 2, my + 4, mx - 3, my + 9);
                    g.DrawLine(p, mx + 2, my + 4, mx + 3, my + 9);
                    // Feet
                    g.DrawLine(p, mx - 3, my + 9, mx - 5, my + 9);
                    g.DrawLine(p, mx + 3, my + 9, mx + 5, my + 9);
                }
                // Arms reaching toward opponent
                float armDir = m == 0 ? 1 : -1;
                using (var p = new Pen(col, 1f)) {
                    g.DrawLine(p, mx + armDir * 3, my - 2, mx + armDir * 7, my - 4 + clash * 0.3f);
                }
                // Weapon — beam sword
                float swordGlow = 0.6f + 0.4f * (float)Math.Sin(t * 22 + m * 3);
                Color swordCol = m == 0 ? Color.FromArgb((int)(a * swordGlow), 100, 200, 255) : Color.FromArgb((int)(a * swordGlow), 255, 100, 100);
                using (var p = new Pen(swordCol, 1.5f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, mx + armDir * 7, my - 4, mx + armDir * 12, my - 7);
                }
            }

            // Clash sparks at center
            float sparkBurst = (float)Math.Abs(Math.Sin(t * 16));
            var rng = new Random(ev.Seed + (int)(t * 10));
            for (int s = 0; s < 6; s++) {
                float sx = cx + (float)(rng.NextDouble() - 0.5) * 10 * sparkBurst;
                float sy = cy - 4 + (float)(rng.NextDouble() - 0.5) * 8 * sparkBurst;
                int sa = (int)(a * 0.6f * sparkBurst);
                if (sa > 0) {
                    using (var b = new SolidBrush(Color.FromArgb(sa, 255, 240, 150)))
                        g.FillEllipse(b, sx - 1, sy - 1, 2, 2);
                }
            }
        }

        static void PaintSpaceStation(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);

            // Main truss — horizontal beam
            using (var p = new Pen(Color.FromArgb(a, 200, 200, 210), 1.4f))
                g.DrawLine(p, cx - 18, cy, cx + 18, cy);

            // Pressurized modules — center cluster
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 220, 230)))
                g.FillRectangle(b, cx - 5, cy - 3, 10, 6);
            using (var b = new SolidBrush(Color.FromArgb(a, 210, 210, 220)))
                g.FillRectangle(b, cx - 3, cy - 5, 6, 10);

            // Solar panel arrays — 4 panels at ends of truss
            for (int side = -1; side <= 1; side += 2) {
                float px = cx + side * 15;
                // Each side has 2 panels (top and bottom)
                for (int panel = -1; panel <= 1; panel += 2) {
                    float py = cy + panel * 6;
                    using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.85f), 100, 130, 210)))
                        g.FillRectangle(b, px - 5, py - 4, 10, 3);
                    // Panel grid lines
                    using (var p = new Pen(Color.FromArgb((int)(a * 0.4f), 80, 110, 180), 0.3f)) {
                        g.DrawLine(p, px - 5, py - 2.5f, px + 5, py - 2.5f);
                        g.DrawLine(p, px, py - 4, px, py - 1);
                    }
                }
            }

            // Radiator panels — smaller, angled
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f), 200, 180, 160)))
                g.FillRectangle(b, cx - 9, cy - 2, 3, 4);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f), 200, 180, 160)))
                g.FillRectangle(b, cx + 6, cy - 2, 3, 4);

            // Blinking light
            float blink = (float)Math.Sin(t * 20) > 0 ? 1f : 0.2f;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * blink), 255, 50, 50)))
                g.FillEllipse(b, cx - 1, cy - 6, 2, 2);
        }

        static void PaintDeLorean(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;

            // Time flash — bright burst at start and end
            float flashIntensity = 0;
            if (t < 0.08f) flashIntensity = 1f - t / 0.08f;
            else if (t > 0.92f) flashIntensity = (t - 0.92f) / 0.08f;
            if (flashIntensity > 0) {
                for (int fl = 3; fl >= 1; fl--) {
                    float fr = 15 + fl * 10;
                    int fa = (int)(a * 0.15f * flashIntensity / fl);
                    if (fa > 0)
                        using (var b = new SolidBrush(Color.FromArgb(fa, 100, 180, 255)))
                            g.FillEllipse(b, cx - fr, cy - fr, fr * 2, fr * 2);
                }
            }

            // Car body — DeLorean wedge shape
            PointF[] body = dir > 0
                ? new[] { new PointF(cx + 10, cy), new PointF(cx + 6, cy - 4), new PointF(cx - 4, cy - 4), new PointF(cx - 8, cy - 2), new PointF(cx - 8, cy + 2), new PointF(cx + 10, cy + 2) }
                : new[] { new PointF(cx - 10, cy), new PointF(cx - 6, cy - 4), new PointF(cx + 4, cy - 4), new PointF(cx + 8, cy - 2), new PointF(cx + 8, cy + 2), new PointF(cx - 10, cy + 2) };
            using (var b = new SolidBrush(Color.FromArgb(a, 190, 190, 200)))
                g.FillPolygon(b, body);

            // Windshield
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f), 140, 200, 255)))
                g.FillRectangle(b, cx - 1, cy - 3.5f, 4 * dir, 2);

            // Wheels
            using (var b = new SolidBrush(Color.FromArgb(a, 40, 40, 40))) {
                g.FillEllipse(b, cx + dir * 5 - 1.5f, cy + 1.5f, 3, 3);
                g.FillEllipse(b, cx - dir * 5 - 1.5f, cy + 1.5f, 3, 3);
            }

            // Fire trails from wheels — the iconic twin flame trails
            float flamePulse = 0.7f + 0.3f * (float)Math.Sin(t * 30);
            for (int trail = 0; trail < 2; trail++) {
                float ty = cy + 3;
                float tx = cx + (trail == 0 ? dir * 5 : -dir * 5);
                for (int f = 0; f < 10; f++) {
                    float ft = f / 10f;
                    float fx = tx - dir * (f * 3 + 2);
                    float fy = ty + (float)Math.Sin(f + t * 20) * 1.5f;
                    float fFade = (1f - ft) * flamePulse;
                    int fa = (int)(a * 0.5f * fFade);
                    if (fa > 0) {
                        Color fc = ft < 0.3f ? Color.FromArgb(fa, 255, 200, 50) : Color.FromArgb(fa, 255, 100, 30);
                        using (var b = new SolidBrush(fc))
                            g.FillEllipse(b, fx - 1.5f, fy - 1, 3, 2);
                    }
                }
            }

            // Blue time-energy aura around car
            float aura = 0.3f + 0.3f * (float)Math.Sin(t * 15);
            using (var p = new Pen(Color.FromArgb((int)(a * aura), 80, 160, 255), 1.5f))
                g.DrawEllipse(p, cx - 12, cy - 6, 24, 12);
        }

        static void PaintStarCruiser(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;

            // Massive angular wedge — star destroyer silhouette
            PointF[] hull = dir > 0
                ? new[] {
                    new PointF(cx + 20, cy),             // nose point
                    new PointF(cx - 14, cy - 10),        // top rear
                    new PointF(cx - 14, cy + 10)          // bottom rear
                }
                : new[] {
                    new PointF(cx - 20, cy),
                    new PointF(cx + 14, cy - 10),
                    new PointF(cx + 14, cy + 10)
                };
            using (var b = new SolidBrush(Color.FromArgb(a, 120, 125, 140)))
                g.FillPolygon(b, hull);

            // Bridge tower on top
            using (var b = new SolidBrush(Color.FromArgb(a, 140, 145, 160)))
                g.FillRectangle(b, cx - 4, cy - 13, 8, 4);
            using (var b = new SolidBrush(Color.FromArgb(a, 155, 160, 175)))
                g.FillRectangle(b, cx - 2, cy - 15, 4, 2);

            // Surface detail — tiny panel lines
            using (var p = new Pen(Color.FromArgb((int)(a * 0.3f), 170, 175, 190), 0.3f)) {
                for (int line = 0; line < 5; line++) {
                    float ly = cy - 8 + line * 4;
                    g.DrawLine(p, cx - 10, ly, cx + 10 * dir, ly);
                }
            }

            // Engine glow — huge rear engines
            float engPulse = 0.6f + 0.4f * (float)Math.Sin(t * 20);
            for (int eng = -2; eng <= 2; eng++) {
                float ey = cy + eng * 3;
                float ex = cx - dir * 14;
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * engPulse), 130, 180, 255)))
                    g.FillEllipse(b, ex - 2, ey - 1.5f, 4, 3);
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.2f * engPulse), 100, 160, 255)))
                    g.FillEllipse(b, ex - dir * 3 - 3, ey - 3, 6, 6);
            }

            // Running lights
            float blink = (float)Math.Sin(t * 15) > 0 ? 1f : 0.15f;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * blink), 255, 50, 50)))
                g.FillEllipse(b, cx + dir * 18, cy - 0.5f, 1.5f, 1.5f);
        }

        static void PaintCosmicEye(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 210);

            // Eye opens from t=0.1 to t=0.3, looks around 0.3-0.7, closes 0.7-0.9
            float openness;
            if (t < 0.1f) openness = 0;
            else if (t < 0.3f) openness = (t - 0.1f) / 0.2f;
            else if (t < 0.7f) openness = 1f;
            else if (t < 0.9f) openness = 1f - (t - 0.7f) / 0.2f;
            else openness = 0;

            // Blinks at t=0.45 and t=0.6
            float blinkDip = 0;
            float b1 = Math.Abs(t - 0.45f); if (b1 < 0.02f) blinkDip = 1f - b1 / 0.02f;
            float b2 = Math.Abs(t - 0.6f); if (b2 < 0.02f) blinkDip = Math.Max(blinkDip, 1f - b2 / 0.02f);
            openness *= (1f - blinkDip * 0.9f);

            if (openness < 0.01f) return;

            float eyeW = 16, eyeH = 10 * openness;

            // Eerie glow
            for (int gl = 3; gl >= 1; gl--) {
                float gr = 12 + gl * 7;
                int ga = (int)(a * 0.04f * openness / gl);
                if (ga > 0)
                    using (var br = new SolidBrush(Color.FromArgb(ga, 100, 200, 160)))
                        g.FillEllipse(br, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            // Eye white (almond shape via ellipse clipping)
            using (var br = new SolidBrush(Color.FromArgb((int)(a * 0.7f * openness), 230, 230, 220)))
                g.FillEllipse(br, cx - eyeW, cy - eyeH, eyeW * 2, eyeH * 2);

            // Iris — looks around during mid phase
            float lookX = 0, lookY = 0;
            if (t > 0.3f && t < 0.7f) {
                float lookT = (t - 0.3f) / 0.4f;
                lookX = (float)Math.Sin(lookT * Math.PI * 3) * 4;
                lookY = (float)Math.Cos(lookT * Math.PI * 2) * 2;
            }
            float irisR = 6 * openness;
            using (var br = new SolidBrush(Color.FromArgb((int)(a * openness), 40, 180, 120)))
                g.FillEllipse(br, cx + lookX - irisR, cy + lookY - irisR, irisR * 2, irisR * 2);

            // Pupil
            float pupilR = 3 * openness;
            using (var br = new SolidBrush(Color.FromArgb((int)(a * openness), 10, 10, 10)))
                g.FillEllipse(br, cx + lookX - pupilR, cy + lookY - pupilR, pupilR * 2, pupilR * 2);

            // Pupil highlight
            using (var br = new SolidBrush(Color.FromArgb((int)(a * 0.7f * openness), 255, 255, 255)))
                g.FillEllipse(br, cx + lookX - 1, cy + lookY - 2, 2, 2);

            // Eyelid lines (top and bottom)
            using (var p = new Pen(Color.FromArgb((int)(a * 0.6f), 60, 50, 70), 1f)) {
                g.DrawArc(p, cx - eyeW, cy - eyeH, eyeW * 2, eyeH * 2, 180, 180); // top lid
                g.DrawArc(p, cx - eyeW, cy - eyeH, eyeW * 2, eyeH * 2, 0, 180);   // bottom lid
            }
        }

        static void PaintTreeRocket(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float wobble = (float)Math.Sin(t * 10) * 2;
            cx += wobble;

            // Rocket flame out the bottom
            float flamePulse = 0.6f + 0.4f * (float)Math.Sin(t * 28);
            for (int f = 0; f < 8; f++) {
                float ft = f / 8f;
                float fy = cy + 12 + ft * 18;
                float fSpread = 1 + ft * 5;
                float fFade = (1f - ft) * flamePulse;
                int fa = (int)(a * 0.5f * fFade);
                if (fa > 0) {
                    Color fc = ft < 0.4f ? Color.FromArgb(fa, 255, 220, 80) : Color.FromArgb(fa, 255, 100, 30);
                    using (var b = new SolidBrush(fc))
                        g.FillEllipse(b, cx - fSpread, fy - 1.5f, fSpread * 2, 3);
                }
            }

            // Tree trunk (also the "fuselage")
            using (var b = new SolidBrush(Color.FromArgb(a, 120, 80, 40)))
                g.FillRectangle(b, cx - 2, cy + 4, 4, 8);

            // Evergreen layers — 3 triangle tiers
            for (int tier = 0; tier < 3; tier++) {
                float ty = cy - 2 - tier * 6;
                float bw = 8 - tier * 2;
                PointF[] tri = {
                    new PointF(cx, ty - 5),
                    new PointF(cx - bw, ty + 3),
                    new PointF(cx + bw, ty + 3)
                };
                using (var b = new SolidBrush(Color.FromArgb(a, 40, 140 + tier * 20, 50)))
                    g.FillPolygon(b, tri);
            }

            // Star on top
            float starPulse = 0.6f + 0.4f * (float)Math.Sin(t * 20);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * starPulse), 255, 230, 80)))
                g.FillEllipse(b, cx - 2, cy - 22, 4, 4);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f * starPulse), 255, 240, 120)))
                g.FillEllipse(b, cx - 4, cy - 24, 8, 8);

            // Smoke trail
            var rng = new Random(ev.Seed);
            for (int s = 0; s < 6; s++) {
                float sy = cy + 28 + s * 6 + (float)(rng.NextDouble() - 0.5) * 4;
                float sx = cx + (float)(rng.NextDouble() - 0.5) * (4 + s * 2);
                float sz = 2 + s * 0.8f;
                int sa = (int)(a * 0.12f * (1f - s / 6f));
                if (sa > 0)
                    using (var b = new SolidBrush(Color.FromArgb(sa, 200, 200, 200)))
                        g.FillEllipse(b, sx - sz, sy - sz, sz * 2, sz * 2);
            }
        }

        static void PaintCowMoon(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 210);

            // Crescent moon — stationary, bigger
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 230, 180)))
                g.FillEllipse(b, cx - 12, cy - 12, 24, 24);
            // Cut out crescent
            using (var b = new SolidBrush(Color.FromArgb(a, 15, 15, 25)))
                g.FillEllipse(b, cx - 4, cy - 13, 22, 24);

            // Moon glow
            for (int gl = 2; gl >= 1; gl--) {
                float gr = 14 + gl * 6;
                int ga = (int)(a * 0.06f / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 240, 230, 180)))
                        g.FillEllipse(b, cx - gr, cy - gr, gr * 2, gr * 2);
            }

            // Cow — arcing jump over the moon — BIGGER
            float jumpT;
            if (t < 0.2f) jumpT = 0;
            else if (t > 0.8f) jumpT = 1f;
            else jumpT = (t - 0.2f) / 0.6f;

            float cowAngle = jumpT * (float)Math.PI;
            float jumpRadius = 28;
            float cowX = cx + (float)Math.Cos(Math.PI - cowAngle) * jumpRadius;
            float cowY = cy - (float)Math.Sin(cowAngle) * jumpRadius - 4;

            if (t > 0.15f && t < 0.85f) {
                int ca = (int)(a * Math.Min(1f, Math.Min((t - 0.15f) / 0.05f, (0.85f - t) / 0.05f)));
                // Cow body — BIGGER
                using (var b = new SolidBrush(Color.FromArgb(ca, 245, 245, 245)))
                    g.FillEllipse(b, cowX - 7, cowY - 4, 14, 8);
                // Spots — bigger, more distinct
                using (var b = new SolidBrush(Color.FromArgb(ca, 40, 40, 40))) {
                    g.FillEllipse(b, cowX - 4, cowY - 3, 5, 4);
                    g.FillEllipse(b, cowX + 2, cowY - 1, 3, 3);
                }
                // Head
                using (var b = new SolidBrush(Color.FromArgb(ca, 245, 245, 245)))
                    g.FillEllipse(b, cowX + 5, cowY - 5, 6, 5);
                // Horns
                using (var p = new Pen(Color.FromArgb(ca, 200, 180, 140), 0.8f)) {
                    g.DrawLine(p, cowX + 6, cowY - 5, cowX + 5, cowY - 7);
                    g.DrawLine(p, cowX + 9, cowY - 5, cowX + 10, cowY - 7);
                }
                // Eye
                using (var b = new SolidBrush(Color.FromArgb(ca, 20, 20, 20)))
                    g.FillEllipse(b, cowX + 8, cowY - 4, 1.5f, 1.5f);
                // Legs — visible sticks
                using (var p = new Pen(Color.FromArgb(ca, 220, 220, 220), 1f)) {
                    g.DrawLine(p, cowX - 4, cowY + 4, cowX - 4, cowY + 8);
                    g.DrawLine(p, cowX + 4, cowY + 4, cowX + 4, cowY + 8);
                    g.DrawLine(p, cowX - 1, cowY + 4, cowX - 1, cowY + 7);
                    g.DrawLine(p, cowX + 1, cowY + 4, cowX + 1, cowY + 7);
                }
                // Udder
                using (var b = new SolidBrush(Color.FromArgb(ca, 255, 180, 200)))
                    g.FillEllipse(b, cowX - 1.5f, cowY + 3, 3, 2.5f);
                // Tail
                using (var p = new Pen(Color.FromArgb(ca, 200, 200, 200), 0.6f))
                    g.DrawBezier(p, cowX - 7, cowY, cowX - 10, cowY - 2, cowX - 11, cowY + 2, cowX - 9, cowY + 4);
            }
        }


        static void PaintSpaceCatsuit(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;
            float drift = (float)Math.Sin(t * 6) * 3;
            cy += drift;

            // Astronaut helmet — glass bubble
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f), 200, 220, 240)))
                g.FillEllipse(b, cx - 6, cy - 7, 12, 11);
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f), 180, 190, 200), 0.8f))
                g.DrawEllipse(p, cx - 6, cy - 7, 12, 11);

            // Visor reflection
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.15f), 160, 200, 255)))
                g.FillEllipse(b, cx - 4, cy - 5, 5, 4);

            // Cat face inside helmet
            // Fur
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 160, 60)))
                g.FillEllipse(b, cx - 4, cy - 4, 8, 7);
            // Ears poking up
            PointF[] leftEar = { new PointF(cx - 3, cy - 4), new PointF(cx - 5, cy - 8), new PointF(cx - 1, cy - 5) };
            PointF[] rightEar = { new PointF(cx + 3, cy - 4), new PointF(cx + 5, cy - 8), new PointF(cx + 1, cy - 5) };
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 140, 40))) {
                g.FillPolygon(b, leftEar);
                g.FillPolygon(b, rightEar);
            }
            // Eyes — big green cat eyes
            using (var b = new SolidBrush(Color.FromArgb(a, 80, 220, 80))) {
                g.FillEllipse(b, cx - 3.5f, cy - 2, 3, 2.5f);
                g.FillEllipse(b, cx + 0.5f, cy - 2, 3, 2.5f);
            }
            // Pupils — slits
            using (var b = new SolidBrush(Color.FromArgb(a, 10, 10, 10))) {
                g.FillEllipse(b, cx - 2.2f, cy - 1.5f, 0.8f, 1.8f);
                g.FillEllipse(b, cx + 1.5f, cy - 1.5f, 0.8f, 1.8f);
            }
            // Nose + mouth
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 120, 130)))
                g.FillEllipse(b, cx - 0.7f, cy + 0.5f, 1.4f, 1);
            // Whiskers
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f), 255, 255, 255), 0.3f)) {
                g.DrawLine(p, cx - 2, cy + 0.5f, cx - 7, cy - 0.5f);
                g.DrawLine(p, cx - 2, cy + 1, cx - 7, cy + 1.5f);
                g.DrawLine(p, cx + 2, cy + 0.5f, cx + 7, cy - 0.5f);
                g.DrawLine(p, cx + 2, cy + 1, cx + 7, cy + 1.5f);
            }

            // Spacesuit body — small rounded body
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 220, 230)))
                g.FillEllipse(b, cx - 4, cy + 3, 8, 6);
            // Tail floating out of suit
            float tailWave = (float)Math.Sin(t * 10) * 3;
            using (var p = new Pen(Color.FromArgb(a, 255, 140, 40), 1.2f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawBezier(p, cx - dir * 3, cy + 6, cx - dir * 6, cy + 8 + tailWave,
                    cx - dir * 9, cy + 5 + tailWave, cx - dir * 10, cy + 3);
            }
        }

        static void PaintAlienWave(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);

            // Bubble/porthole
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.15f), 100, 200, 180)))
                g.FillEllipse(b, cx - 10, cy - 10, 20, 20);
            using (var p = new Pen(Color.FromArgb((int)(a * 0.6f), 120, 220, 200), 1.2f))
                g.DrawEllipse(p, cx - 10, cy - 10, 20, 20);
            // Porthole glass shine
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.15f), 200, 255, 240)))
                g.FillEllipse(b, cx - 7, cy - 8, 5, 4);

            // Alien face — big eyes, small body
            using (var b = new SolidBrush(Color.FromArgb(a, 120, 200, 120)))
                g.FillEllipse(b, cx - 5, cy - 3, 10, 8);
            // Big eyes
            using (var b = new SolidBrush(Color.FromArgb(a, 20, 20, 20))) {
                g.FillEllipse(b, cx - 4, cy - 2, 3.5f, 4);
                g.FillEllipse(b, cx + 0.5f, cy - 2, 3.5f, 4);
            }
            // Eye shine
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 255, 200))) {
                g.FillEllipse(b, cx - 3, cy - 1, 1.2f, 1.2f);
                g.FillEllipse(b, cx + 1.5f, cy - 1, 1.2f, 1.2f);
            }
            // Mouth — little smile
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f), 80, 160, 80), 0.5f))
                g.DrawArc(p, cx - 2, cy + 2, 4, 2, 0, 180);

            // Waving hand — arm moves back and forth
            float handX = cx + 6 + (float)Math.Sin(t * 16) * 3;
            float handY = cy - 2 + (float)Math.Cos(t * 16) * 2;
            using (var p = new Pen(Color.FromArgb(a, 120, 200, 120), 0.8f))
                g.DrawLine(p, cx + 4, cy + 1, handX, handY);
            // Hand — three fingers
            for (int f = -1; f <= 1; f++) {
                using (var p = new Pen(Color.FromArgb(a, 120, 200, 120), 0.5f))
                    g.DrawLine(p, handX, handY, handX + 1.5f, handY - 2 + f * 1.5f);
            }

            // Zip away at end
            if (t > 0.8f) {
                float zipT = (t - 0.8f) / 0.2f;
                int za = (int)(a * 0.4f * (1f - zipT));
                for (int s = 0; s < 5; s++) {
                    float sx = cx - ev.DirX * s * 6 * zipT;
                    float sy = cy - ev.DirY * s * 6 * zipT;
                    if (za > 0)
                        using (var b = new SolidBrush(Color.FromArgb(za / (s + 1), 120, 220, 200)))
                            g.FillEllipse(b, sx - 2, sy - 2, 4, 4);
                }
            }
        }

        static void PaintRubberDucky(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 220);
            float bob = (float)Math.Sin(t * 8) * 3;
            float dir = ev.DirX > 0 ? 1 : -1;
            cy += bob;

            // Soft glow underneath
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.08f), 255, 230, 80)))
                g.FillEllipse(b, cx - 14, cy - 10, 28, 20);

            // Body — big round yellow, slightly wider than tall
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 210, 30)))
                g.FillEllipse(b, cx - 9, cy - 3, 18, 12);
            // Body highlight — lighter yellow stripe
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.4f), 255, 240, 120)))
                g.FillEllipse(b, cx - 5, cy - 2, 10, 5);

            // Head — round, overlapping body, positioned toward travel direction
            float headX = cx + dir * 5;
            float headY = cy - 6;
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 220, 40)))
                g.FillEllipse(b, headX - 5.5f, headY - 5, 11, 10);
            // Head highlight
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f), 255, 250, 150)))
                g.FillEllipse(b, headX - 3, headY - 4, 5, 4);

            // Beak — bright orange, pointing in travel direction
            PointF[] beak = {
                new PointF(headX + dir * 5, headY + 1),
                new PointF(headX + dir * 10, headY + 2),
                new PointF(headX + dir * 5, headY + 3.5f)
            };
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 140, 0)))
                g.FillPolygon(b, beak);
            // Beak upper/lower division line
            using (var p = new Pen(Color.FromArgb((int)(a * 0.3f), 200, 100, 0), 0.4f))
                g.DrawLine(p, headX + dir * 5, headY + 2.2f, headX + dir * 9, headY + 2.2f);

            // Eye — on the side facing the viewer (toward travel direction)
            float eyeX = headX + dir * 2.5f;
            float eyeY = headY - 0.5f;
            using (var b = new SolidBrush(Color.FromArgb(a, 15, 15, 15)))
                g.FillEllipse(b, eyeX - 1.5f, eyeY - 1.5f, 3, 3);
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                g.FillEllipse(b, eyeX - 0.5f, eyeY - 1.2f, 1.2f, 1.2f);

            // Tail — little uptick at the back
            float tailX = cx - dir * 8;
            PointF[] tail = {
                new PointF(tailX, cy + 1),
                new PointF(tailX - dir * 3, cy - 2),
                new PointF(tailX - dir * 1, cy + 2)
            };
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 215, 35)))
                g.FillPolygon(b, tail);

            // Water ripple ring at base — cosmic blue glow
            float ringPulse = 0.5f + 0.3f * (float)Math.Sin(t * 12);
            using (var p = new Pen(Color.FromArgb((int)(a * 0.35f * ringPulse), 120, 200, 255), 1f))
                g.DrawEllipse(p, cx - 12, cy + 6, 24, 6);
            using (var p = new Pen(Color.FromArgb((int)(a * 0.15f * ringPulse), 120, 200, 255), 0.5f))
                g.DrawEllipse(p, cx - 15, cy + 5, 30, 8);
        }

        static void PaintSnowglobe(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 200);
            float drift = (float)Math.Sin(t * 5) * 2;
            cx += drift;

            // Globe — glass sphere
            float gr = 14;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.2f), 180, 220, 255)))
                g.FillEllipse(b, cx - gr, cy - gr - 2, gr * 2, gr * 2);
            using (var p = new Pen(Color.FromArgb((int)(a * 0.6f), 200, 220, 240), 1.2f))
                g.DrawEllipse(p, cx - gr, cy - gr - 2, gr * 2, gr * 2);
            // Glass shine
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.25f), 255, 255, 255)))
                g.FillEllipse(b, cx - 8, cy - 12, 6, 5);

            // Base — wooden pedestal
            using (var b = new SolidBrush(Color.FromArgb(a, 120, 80, 50)))
                g.FillRectangle(b, cx - 10, cy + 11, 20, 5);
            using (var b = new SolidBrush(Color.FromArgb(a, 100, 65, 40)))
                g.FillRectangle(b, cx - 8, cy + 16, 16, 3);

            // Tiny tree inside
            PointF[] tree = { new PointF(cx, cy - 4), new PointF(cx - 4, cy + 6), new PointF(cx + 4, cy + 6) };
            using (var b = new SolidBrush(Color.FromArgb(a, 40, 130, 50)))
                g.FillPolygon(b, tree);
            using (var b = new SolidBrush(Color.FromArgb(a, 90, 60, 30)))
                g.FillRectangle(b, cx - 1, cy + 6, 2, 3);

            // Snow falling inside globe
            var rng = new Random(ev.Seed);
            for (int s = 0; s < 20; s++) {
                float sx = cx + (float)(rng.NextDouble() - 0.5) * 24;
                float sy = cy - 12 + (float)rng.NextDouble() * 22;
                // Check if inside globe radius
                float ddx = sx - cx, ddy = sy - (cy - 2);
                if (ddx * ddx + ddy * ddy > gr * gr) continue;
                float snowY = sy + ((float)(t * 40 + s * 3) % 22) - 11;
                float snowX = sx + (float)Math.Sin(t * 4 + s) * 2;
                // Re-check bounds after animation
                ddx = snowX - cx; ddy = snowY - (cy - 2);
                if (ddx * ddx + ddy * ddy > (gr - 1) * (gr - 1)) continue;
                int sa = (int)(a * (0.3f + (float)rng.NextDouble() * 0.4f));
                using (var b = new SolidBrush(Color.FromArgb(sa, 255, 255, 255)))
                    g.FillEllipse(b, snowX - 0.8f, snowY - 0.8f, 1.6f, 1.6f);
            }
        }


        static void PaintCosmicSword(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;
            float bob = (float)Math.Sin(t * 5) * 3;
            float glow = 0.6f + 0.4f * (float)Math.Sin(t * 12);
            cy += bob;

            // Sword is oriented horizontally, pointing in travel direction
            // Blade glow — outer
            using (var p = new Pen(Color.FromArgb((int)(a * 0.2f * glow), 140, 200, 255), 6f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, cx - dir * 4, cy, cx + dir * 18, cy);
            }
            // Blade glow — inner
            using (var p = new Pen(Color.FromArgb((int)(a * 0.4f * glow), 180, 220, 255), 3f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, cx - dir * 4, cy, cx + dir * 18, cy);
            }
            // Blade core — bright white-blue
            using (var p = new Pen(Color.FromArgb((int)(a * glow), 220, 240, 255), 1.5f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Triangle;
                g.DrawLine(p, cx - dir * 3, cy, cx + dir * 17, cy);
            }

            // Cross guard — golden horizontal bar
            using (var p = new Pen(Color.FromArgb(a, 220, 190, 100), 1.8f))
                g.DrawLine(p, cx - dir * 4, cy - 4, cx - dir * 4, cy + 4);
            // Guard gems
            using (var b = new SolidBrush(Color.FromArgb((int)(a * glow), 100, 180, 255)))
                g.FillEllipse(b, cx - dir * 4 - 1.5f, cy - 5.5f, 3, 3);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * glow), 100, 180, 255)))
                g.FillEllipse(b, cx - dir * 4 - 1.5f, cy + 2.5f, 3, 3);

            // Handle/grip
            using (var p = new Pen(Color.FromArgb(a, 160, 130, 70), 2f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, cx - dir * 5, cy, cx - dir * 11, cy);
            }

            // Pommel — round end cap
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 170, 80)))
                g.FillEllipse(b, cx - dir * 13, cy - 2, 4, 4);

            // Energy particles along blade
            for (int p2 = 0; p2 < 8; p2++) {
                float pt = ((t * 8 + p2 * 0.25f) % 1f);
                float px = cx + dir * (pt * 17);
                float py = cy + (float)Math.Sin(t * 20 + p2 * 1.5f) * 3;
                int pa = (int)(a * 0.4f * glow * (0.3f + 0.7f * (float)Math.Sin(t * 15 + p2)));
                if (pa > 0)
                    using (var b = new SolidBrush(Color.FromArgb(Math.Max(0, pa), 180, 230, 255)))
                        g.FillEllipse(b, px - 1, py - 1, 2, 2);
            }

            // Blade edge shimmer
            float shimmer = (float)Math.Sin(t * 25) * 0.5f + 0.5f;
            using (var p = new Pen(Color.FromArgb((int)(a * 0.3f * shimmer), 255, 255, 255), 0.5f))
                g.DrawLine(p, cx + dir * 2, cy - 0.5f, cx + dir * 15, cy - 0.5f);
        }

        static void PaintGoldfishBowl(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 200);
            float drift = (float)Math.Sin(t * 5) * 2;
            cy += drift;

            // Water sphere — floating globe of water
            float sr = 12;
            // Outer water glow
            for (int gl = 2; gl >= 1; gl--) {
                float glr = sr + gl * 4;
                int ga = (int)(a * 0.08f / gl);
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 100, 180, 240)))
                        g.FillEllipse(b, cx - glr, cy - glr, glr * 2, glr * 2);
            }
            // Water body
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.4f), 80, 160, 220)))
                g.FillEllipse(b, cx - sr, cy - sr, sr * 2, sr * 2);
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f), 120, 200, 250), 1f))
                g.DrawEllipse(p, cx - sr, cy - sr, sr * 2, sr * 2);

            // Goldfish — swimming inside
            float fishAngle = t * 8;
            float fishX = cx + (float)Math.Cos(fishAngle) * 5;
            float fishY = cy + (float)Math.Sin(fishAngle * 0.7f) * 4;
            float fishDir = (float)Math.Cos(fishAngle) > 0 ? 1 : -1;

            // Body
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 140, 30)))
                g.FillEllipse(b, fishX - 3, fishY - 2, 6, 4);
            // Tail
            float tailWag = (float)Math.Sin(t * 20) * 2;
            PointF[] tail = {
                new PointF(fishX - fishDir * 3, fishY),
                new PointF(fishX - fishDir * 6, fishY - 2 + tailWag),
                new PointF(fishX - fishDir * 6, fishY + 2 + tailWag)
            };
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 120, 20)))
                g.FillPolygon(b, tail);
            // Eye
            using (var b = new SolidBrush(Color.FromArgb(a, 20, 20, 20)))
                g.FillEllipse(b, fishX + fishDir * 1, fishY - 1.5f, 1.5f, 1.5f);

            // Bubbles
            for (int bbl = 0; bbl < 4; bbl++) {
                float bt = ((t * 3 + bbl * 0.4f) % 1f);
                float bx = fishX + (float)Math.Sin(bbl * 2.5f) * 3;
                float by = fishY - bt * 10;
                float bsz = 1 + bt * 1.5f;
                int ba = (int)(a * 0.3f * (1f - bt));
                if (ba > 0)
                    using (var p = new Pen(Color.FromArgb(ba, 180, 220, 255), 0.4f))
                        g.DrawEllipse(p, bx - bsz, by - bsz, bsz * 2, bsz * 2);
            }

            // Water surface shimmer
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.1f), 200, 240, 255)))
                g.FillEllipse(b, cx - 6, cy - 10, 8, 3);
        }

        static void PaintDiscoBall(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 220);
            float spin = t * 360 * 2;

            float op;
            if (t < 0.15f) op = t / 0.15f;
            else if (t > 0.85f) op = (1f - t) / 0.15f;
            else op = 1f;

            // Light beams shooting outward — BRIGHT rotating colored beams
            Color[] beamColors = { Color.Red, Color.Yellow, Color.Cyan, Color.Magenta, Color.Lime, Color.Orange };
            for (int beam = 0; beam < 10; beam++) {
                float bAngle = (spin + beam * 36) * (float)(Math.PI / 180);
                float bLen = 30 + (float)Math.Sin(t * 15 + beam) * 10;
                float bx = cx + (float)Math.Cos(bAngle) * bLen;
                float by = cy + (float)Math.Sin(bAngle) * bLen;
                Color bc = beamColors[beam % beamColors.Length];
                int ba = (int)(a * 0.35f * op);
                using (var p = new Pen(Color.FromArgb(ba, bc.R, bc.G, bc.B), 1.2f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx + (float)Math.Cos(bAngle) * 10, cy + (float)Math.Sin(bAngle) * 10, bx, by);
                }
                // Bright dot at end
                using (var b = new SolidBrush(Color.FromArgb((int)(ba * 1.5f), bc.R, bc.G, bc.B)))
                    g.FillEllipse(b, bx - 2, by - 2, 4, 4);
            }

            // Ball body — bigger silver sphere
            float br = 10;
            using (var b = new SolidBrush(Color.FromArgb((int)(a * op), 200, 200, 215)))
                g.FillEllipse(b, cx - br, cy - br, br * 2, br * 2);

            // Mirror facets — brighter
            for (int row = -3; row <= 3; row++) {
                for (int col = -3; col <= 3; col++) {
                    float fx = cx + col * 3.8f;
                    float fy = cy + row * 3.8f;
                    float ddist = (fx - cx) * (fx - cx) + (fy - cy) * (fy - cy);
                    if (ddist > br * br) continue;
                    float facetBright = 0.4f + 0.5f * (float)Math.Sin(spin * 0.05f + row * 1.5f + col * 1.2f);
                    int fa = (int)(a * facetBright * op);
                    using (var b = new SolidBrush(Color.FromArgb(fa, 240, 240, 250)))
                        g.FillRectangle(b, fx - 1.5f, fy - 1.5f, 3, 3);
                }
            }

            // Highlight
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * op), 255, 255, 255)))
                g.FillEllipse(b, cx - 5, cy - 6, 5, 4);

            // String going up
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f * op), 200, 200, 200), 0.6f))
                g.DrawLine(p, cx, cy - br, cx, cy - br - 12);
        }

        static void PaintSpaceHamster(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float wheelSpin = t * 360 * 3;

            // Wheel — bright metallic circle frame
            float wr = 12;
            // Wheel glow
            using (var p = new Pen(Color.FromArgb((int)(a * 0.15f), 200, 200, 220), 3f))
                g.DrawEllipse(p, cx - wr, cy - wr, wr * 2, wr * 2);
            using (var p = new Pen(Color.FromArgb(a, 200, 200, 215), 1.5f))
                g.DrawEllipse(p, cx - wr, cy - wr, wr * 2, wr * 2);
            // Wheel spokes — brighter
            for (int spoke = 0; spoke < 6; spoke++) {
                float sAngle = (wheelSpin + spoke * 60) * (float)(Math.PI / 180);
                using (var p = new Pen(Color.FromArgb((int)(a * 0.5f), 190, 190, 205), 0.6f))
                    g.DrawLine(p, cx, cy, cx + (float)Math.Cos(sAngle) * wr, cy + (float)Math.Sin(sAngle) * wr);
            }

            // Wheel stand
            using (var p = new Pen(Color.FromArgb(a, 190, 190, 205), 1.2f)) {
                g.DrawLine(p, cx - 6, cy + wr, cx - 10, cy + wr + 6);
                g.DrawLine(p, cx + 6, cy + wr, cx + 10, cy + wr + 6);
                g.DrawLine(p, cx - 10, cy + wr + 6, cx + 10, cy + wr + 6);
            }

            // Hamster — running inside wheel, at the bottom
            float hBounce = (float)Math.Sin(wheelSpin * 0.1f * (float)(Math.PI / 180)) * 1.5f;
            float hx = cx, hy = cy + 3 + hBounce;

            // Body — chubby round, bright warm orange-brown
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 195, 140)))
                g.FillEllipse(b, hx - 5, hy - 3.5f, 10, 7);
            // Belly — lighter
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f), 255, 235, 200)))
                g.FillEllipse(b, hx - 3, hy - 1, 6, 4);

            // Head — slightly larger, rounder
            float headX = hx + 3, headY = hy - 4;
            using (var b = new SolidBrush(Color.FromArgb(a, 245, 200, 145)))
                g.FillEllipse(b, headX - 4, headY - 3.5f, 8, 7);

            // Cheek pouches — the defining hamster feature! Puffy cheeks
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 200, 160)))
                g.FillEllipse(b, headX - 4.5f, headY - 0.5f, 4, 3.5f);
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 200, 160)))
                g.FillEllipse(b, headX + 0.5f, headY - 0.5f, 4, 3.5f);

            // Ears — round, pink inside
            for (int ear = -1; ear <= 1; ear += 2) {
                using (var b = new SolidBrush(Color.FromArgb(a, 240, 190, 140)))
                    g.FillEllipse(b, headX + ear * 3 - 1.5f, headY - 5, 3, 3);
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f), 255, 170, 170)))
                    g.FillEllipse(b, headX + ear * 3 - 1, headY - 4.5f, 2, 2);
            }

            // Eyes — big, dark, shiny
            for (int eye = -1; eye <= 1; eye += 2) {
                float ex = headX + eye * 1.8f, ey = headY - 1.5f;
                using (var b = new SolidBrush(Color.FromArgb(a, 15, 15, 15)))
                    g.FillEllipse(b, ex - 1.3f, ey - 1.3f, 2.6f, 2.6f);
                using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                    g.FillEllipse(b, ex - 0.3f, ey - 1, 1, 1);
            }

            // Nose — tiny pink triangle
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 150, 160)))
                g.FillEllipse(b, headX - 0.8f, headY + 0.5f, 1.6f, 1.2f);

            // Tiny feet churning
            float legCycle = wheelSpin * 0.1f * (float)(Math.PI / 180);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.8f), 255, 185, 150))) {
                g.FillEllipse(b, hx - 3 + (float)Math.Sin(legCycle) * 2, hy + 3, 2, 1.5f);
                g.FillEllipse(b, hx + 1 + (float)Math.Sin(legCycle + 2) * 2, hy + 3, 2, 1.5f);
            }
        }

        /// <summary>Paints a shooting star orbiting a button's rounded-rect perimeter.
        /// Meant to be painted on the PARENT surface so glow extends freely.</summary>

        // ===== WAVE 5 — 29 NEW EVENTS =====

        static void PaintSpaceTelescope(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 210);
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            float dir = ev.DirX > 0 ? 1 : -1;
            // Main tube
            using (var b = new SolidBrush(Color.FromArgb(a, 190, 190, 200)))
                g.FillRectangle(b, cx - 8, cy - 3, 16, 6);
            // Primary mirror end — gold hexagonal
            using (var b = new SolidBrush(Color.FromArgb(a, 230, 200, 80)))
                g.FillEllipse(b, cx + dir * 6, cy - 4, 8, 8);
            // Solar panels — two large blue panels
            for (int side = -1; side <= 1; side += 2) {
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.8f), 80, 120, 200)))
                    g.FillRectangle(b, cx - 3, cy + side * 5, 10, 4 * Math.Abs(side));
                using (var p = new Pen(Color.FromArgb((int)(a * 0.4f), 60, 100, 180), 0.3f))
                    g.DrawRectangle(p, cx - 3, cy + side * 5, 10, 4);
            }
            // Sunshield
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f), 200, 180, 140)))
                g.FillRectangle(b, cx - dir * 6, cy - 5, 4, 10);
        }

        static void PaintAsteroidField(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 210);
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            var rng = new Random(ev.Seed);
            for (int rock = 0; rock < 12; rock++) {
                float rx = cx + (float)(rng.NextDouble() - 0.5) * 60;
                float ry = cy + (float)(rng.NextDouble() - 0.5) * 30;
                float sz = 2 + (float)rng.NextDouble() * 5;
                int ra = (int)(a * (0.6f + (float)rng.NextDouble() * 0.4f));
                int gray = 100 + rng.Next(80);
                using (var b = new SolidBrush(Color.FromArgb(ra, gray, gray - 10, gray - 20)))
                    g.FillEllipse(b, rx - sz, ry - sz * 0.7f, sz * 2, sz * 1.4f);
                // Crater dot
                using (var b = new SolidBrush(Color.FromArgb(ra / 3, gray - 30, gray - 40, gray - 50)))
                    g.FillEllipse(b, rx - sz * 0.3f, ry - sz * 0.2f, sz * 0.5f, sz * 0.4f);
            }
        }

        static void PaintGammaRay(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 220);
            float op; if (t < 0.1f) op = t / 0.1f; else if (t > 0.8f) op = (1f - t) / 0.2f; else op = 1f;
            op = Math.Max(0, Math.Min(1, op));
            float angle = ev.Seed % 360 * (float)(Math.PI / 180);
            float beamLen = Math.Max(w, h) * 0.6f;
            // Double jet — opposite directions
            for (int jet = -1; jet <= 1; jet += 2) {
                float bx = cx + (float)Math.Cos(angle) * beamLen * jet;
                float by = cy + (float)Math.Sin(angle) * beamLen * jet;
                using (var p = new Pen(Color.FromArgb((int)(a * 0.15f * op), 100, 150, 255), 8f))
                    g.DrawLine(p, cx, cy, bx, by);
                using (var p = new Pen(Color.FromArgb((int)(a * 0.4f * op), 150, 200, 255), 3f))
                    g.DrawLine(p, cx, cy, bx, by);
                using (var p = new Pen(Color.FromArgb((int)(a * 0.7f * op), 220, 240, 255), 1f))
                    g.DrawLine(p, cx, cy, bx, by);
            }
            // Source glow
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f * op), 180, 220, 255)))
                g.FillEllipse(b, cx - 8, cy - 8, 16, 16);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * op), 240, 250, 255)))
                g.FillEllipse(b, cx - 3, cy - 3, 6, 6);
        }

        static void PaintSpaceOctopus(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 210);
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            // Head — big dome
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 80, 180)))
                g.FillEllipse(b, cx - 8, cy - 10, 16, 13);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.4f), 240, 140, 220)))
                g.FillEllipse(b, cx - 4, cy - 8, 8, 6);
            // Eyes
            for (int eye = -1; eye <= 1; eye += 2) {
                using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 240)))
                    g.FillEllipse(b, cx + eye * 3 - 2, cy - 4, 4, 3.5f);
                using (var b = new SolidBrush(Color.FromArgb(a, 20, 20, 30)))
                    g.FillEllipse(b, cx + eye * 3 - 0.8f, cy - 3.5f, 2, 2.5f);
            }
            // 8 Tentacles — wavy, trailing
            for (int ten = 0; ten < 8; ten++) {
                float baseAngle = (float)(ten * Math.PI / 4 + Math.PI * 0.1);
                float wave = (float)Math.Sin(t * 20 + ten * 1.2f) * 4;
                float tx = cx + (float)Math.Cos(baseAngle) * (6 + wave);
                float ty = cy + 3 + (float)Math.Sin(baseAngle) * 3 + Math.Abs((float)Math.Sin(t * 12 + ten)) * 8;
                int ta = (int)(a * 0.7f);
                Color tc = ten % 2 == 0 ? Color.FromArgb(ta, 180, 70, 160) : Color.FromArgb(ta, 200, 90, 180);
                using (var p = new Pen(tc, 1.2f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    float midX = (cx + tx) / 2 + wave * 0.5f;
                    float midY = (cy + ty) / 2 + 2;
                    g.DrawBezier(p, cx + (ten - 3.5f) * 1.5f, cy + 1, midX, midY, tx + wave * 0.3f, ty - 2, tx, ty);
                }
            }
        }

        static void PaintCosmicTurtle(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 230);
            float dir = ev.DirX > 0 ? 1 : -1;
            float legWalk = (float)Math.Sin(t * 12) * 3;
            float headBob = (float)Math.Sin(t * 8) * 1.5f;

            // Legs — 4 stubby, ANIMATED walking, drawn UNDER shell
            for (int leg = 0; leg < 4; leg++) {
                float lx = cx + (leg < 2 ? -6 + leg * 4 : 2 + (leg - 2) * 4) * dir;
                float ly = cy + 5 + ((leg % 2 == 0) ? legWalk : -legWalk);
                using (var b = new SolidBrush(Color.FromArgb(a, 140, 200, 120)))
                    g.FillEllipse(b, lx - 2, ly, 4, 4);
            }

            // Tail — behind, wagging
            float tailWag = (float)Math.Sin(t * 10) * 2;
            using (var p = new Pen(Color.FromArgb(a, 140, 200, 120), 1.2f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, cx - dir * 11, cy + 2, cx - dir * 15, cy + 1 + tailWag);
            }

            // Shell — large dome, BRIGHT green with golden star pattern
            using (var b = new SolidBrush(Color.FromArgb(a, 120, 180, 100)))
                g.FillEllipse(b, cx - 11, cy - 9, 22, 15);
            // Shell rim — darker outline
            using (var p = new Pen(Color.FromArgb(a, 90, 140, 70), 1f))
                g.DrawEllipse(p, cx - 11, cy - 9, 22, 15);
            // Shell pattern — hex plates with star sparkles
            for (int plate = 0; plate < 6; plate++) {
                float px = cx - 7 + plate * 3.2f;
                float py = cy - 4 + (float)Math.Sin(plate * 1.5f) * 2;
                using (var p = new Pen(Color.FromArgb((int)(a * 0.4f), 80, 130, 60), 0.6f))
                    g.DrawEllipse(p, px - 2.5f, py - 2, 5, 4);
                // ANIMATED star sparkle on each plate
                float sparkle = (float)Math.Max(0, Math.Sin(t * 12 + plate * 2.5f));
                if (sparkle > 0.2f) {
                    int sa = (int)(a * 0.6f * sparkle);
                    using (var b = new SolidBrush(Color.FromArgb(sa, 255, 255, 180)))
                        g.FillEllipse(b, px - 1, py - 1, 2, 2);
                    // Cross sparkle
                    using (var p = new Pen(Color.FromArgb(sa / 2, 255, 255, 200), 0.4f)) {
                        g.DrawLine(p, px - 2, py, px + 2, py);
                        g.DrawLine(p, px, py - 2, px, py + 2);
                    }
                }
            }

            // Head — poking out front, BOBBING
            float headX = cx + dir * 11;
            float headY = cy - 1 + headBob;
            using (var b = new SolidBrush(Color.FromArgb(a, 150, 210, 130)))
                g.FillEllipse(b, headX - 4, headY - 3, 8, 6);
            // Eye
            using (var b = new SolidBrush(Color.FromArgb(a, 20, 20, 20)))
                g.FillEllipse(b, headX + dir * 1.5f, headY - 1.5f, 2, 2);
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                g.FillEllipse(b, headX + dir * 2, headY - 1.5f, 0.8f, 0.8f);
            // Smile
            using (var p = new Pen(Color.FromArgb((int)(a * 0.4f), 80, 140, 60), 0.4f))
                g.DrawArc(p, headX - 1, headY, 3, 2, 0, 180);
        }

        static void PaintSpaceBee(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 220);
            float dir = ev.DirX > 0 ? 1 : -1;
            var rng = new Random(ev.Seed);
            for (int bee = 0; bee < 7; bee++) {
                float bx = cx + (float)(rng.NextDouble() - 0.5) * 35;
                float by = cy + (float)(rng.NextDouble() - 0.5) * 22;
                bx += (float)Math.Sin(t * 20 + bee * 2) * 5;
                by += (float)Math.Cos(t * 16 + bee * 1.7f) * 4;
                // Body — BIGGER yellow with black stripes
                using (var b = new SolidBrush(Color.FromArgb(a, 255, 220, 40)))
                    g.FillEllipse(b, bx - 3.5f, by - 2, 7, 4);
                // Two black stripes
                using (var b = new SolidBrush(Color.FromArgb(a, 30, 25, 10))) {
                    g.FillRectangle(b, bx - 1, by - 1.8f, 1.2f, 3.6f);
                    g.FillRectangle(b, bx + 1.2f, by - 1.5f, 1, 3);
                }
                // Head — dark
                using (var b = new SolidBrush(Color.FromArgb(a, 40, 35, 15)))
                    g.FillEllipse(b, bx + dir * 3, by - 1.2f, 2.5f, 2.4f);
                // Wings — fluttering fast, BIGGER
                float wingFlap = (float)Math.Sin(t * 80 + bee * 3) * 3;
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.45f), 230, 245, 255))) {
                    g.FillEllipse(b, bx - 2, by - 4 - Math.Abs(wingFlap), 4, 3 + Math.Abs(wingFlap));
                }
                // Stinger
                using (var p = new Pen(Color.FromArgb(a, 40, 30, 10), 0.5f))
                    g.DrawLine(p, bx - dir * 3.5f, by, bx - dir * 5, by);
            }
        }

        static void PaintRocketPenguin(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 220);
            float dir = ev.DirX > 0 ? 1 : -1;
            float bob = (float)Math.Sin(t * 12) * 2;
            cy += bob;
            // Jetpack — on back
            using (var b = new SolidBrush(Color.FromArgb(a, 160, 160, 170)))
                g.FillRectangle(b, cx - dir * 5, cy - 2, 3, 6);
            // Jetpack flame
            float flamePulse = 0.6f + 0.4f * (float)Math.Sin(t * 30);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * flamePulse), 255, 150, 30)))
                g.FillEllipse(b, cx - dir * 7, cy + 2, 4, 6 + flamePulse * 3);
            // Body — black oval
            using (var b = new SolidBrush(Color.FromArgb(a, 30, 30, 40)))
                g.FillEllipse(b, cx - 5, cy - 5, 10, 12);
            // White belly
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 240, 245)))
                g.FillEllipse(b, cx - 3, cy - 2, 6, 8);
            // Head
            using (var b = new SolidBrush(Color.FromArgb(a, 30, 30, 40)))
                g.FillEllipse(b, cx - 4, cy - 9, 8, 6);
            // Eyes
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                g.FillEllipse(b, cx + dir * 1 - 1, cy - 7, 2.5f, 2.5f);
            using (var b = new SolidBrush(Color.FromArgb(a, 10, 10, 10)))
                g.FillEllipse(b, cx + dir * 1.5f - 0.5f, cy - 6.5f, 1.2f, 1.2f);
            // Beak — orange
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 160, 30)))
                g.FillEllipse(b, cx + dir * 3, cy - 6, 3, 2);
            // Flippers — waving excitedly, BIG visible movement
            float flipWave = (float)Math.Sin(t * 18) * 15;
            for (int side = -1; side <= 1; side += 2) {
                float flapY = cy - 1 + flipWave * side * 0.4f;
                using (var p = new Pen(Color.FromArgb(a, 30, 30, 40), 2.5f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, cx + side * 4, cy - 1, cx + side * 9, flapY);
                }
            }
            // Feet — orange
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 160, 30))) {
                g.FillEllipse(b, cx - 3, cy + 6, 3, 1.5f);
                g.FillEllipse(b, cx + 1, cy + 6, 3, 1.5f);
            }
        }

        static void PaintCosmicFrog(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 210);
            float hop = -(float)Math.Abs(Math.Sin(t * 10)) * 12;
            cy += hop;
            // Body — bright green
            using (var b = new SolidBrush(Color.FromArgb(a, 80, 200, 80)))
                g.FillEllipse(b, cx - 6, cy - 3, 12, 8);
            // Head — wide
            using (var b = new SolidBrush(Color.FromArgb(a, 90, 210, 90)))
                g.FillEllipse(b, cx - 7, cy - 7, 14, 7);
            // Big bulging eyes — the defining frog feature
            for (int eye = -1; eye <= 1; eye += 2) {
                float ex = cx + eye * 5;
                using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 230)))
                    g.FillEllipse(b, ex - 2.5f, cy - 9, 5, 4);
                using (var b = new SolidBrush(Color.FromArgb(a, 20, 20, 20)))
                    g.FillEllipse(b, ex - 1, cy - 8, 2.5f, 2.5f);
            }
            // Mouth line — wide smile
            using (var p = new Pen(Color.FromArgb((int)(a * 0.4f), 40, 120, 40), 0.5f))
                g.DrawArc(p, cx - 5, cy - 4, 10, 4, 0, 180);
            // Back legs — bent for hopping
            for (int side = -1; side <= 1; side += 2) {
                using (var p = new Pen(Color.FromArgb(a, 70, 180, 70), 1.5f)) {
                    g.DrawLine(p, cx + side * 4, cy + 4, cx + side * 7, cy + 2 - hop * 0.3f);
                    g.DrawLine(p, cx + side * 7, cy + 2 - hop * 0.3f, cx + side * 5, cy + 6 - hop * 0.2f);
                }
            }
        }

        static void PaintSpaceSquid(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            // Mantle — torpedo shape
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 100, 120)))
                g.FillEllipse(b, cx - 4, cy - 10, 8, 14);
            // Fins at top
            for (int side = -1; side <= 1; side += 2) {
                float finWave = (float)Math.Sin(t * 14) * 2 * side;
                PointF[] fin = { new PointF(cx, cy - 6), new PointF(cx + side * 6 + finWave, cy - 8), new PointF(cx + side * 2, cy - 2) };
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.7f), 220, 120, 140)))
                    g.FillPolygon(b, fin);
            }
            // Eyes — big and bright
            for (int eye = -1; eye <= 1; eye += 2) {
                using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 220)))
                    g.FillEllipse(b, cx + eye * 1.5f - 1.5f, cy - 4, 3, 3);
                using (var b = new SolidBrush(Color.FromArgb(a, 15, 15, 20)))
                    g.FillEllipse(b, cx + eye * 1.5f - 0.7f, cy - 3.5f, 1.5f, 2);
            }
            // 8 short tentacles + 2 long ones
            for (int ten = 0; ten < 10; ten++) {
                float baseX = cx + (ten - 4.5f) * 1.2f;
                float wave = (float)Math.Sin(t * 18 + ten * 1.3f) * 3;
                float tentLen = ten < 2 || ten > 7 ? 14 : 7; // 2 long feeding tentacles
                float ty = cy + 4 + tentLen + wave;
                int ta = (int)(a * 0.6f);
                using (var p = new Pen(Color.FromArgb(ta, 190, 90, 110), ten < 2 || ten > 7 ? 0.8f : 1f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawBezier(p, baseX, cy + 3, baseX + wave * 0.3f, cy + 3 + tentLen * 0.4f, baseX + wave, cy + 3 + tentLen * 0.7f, baseX + wave, ty);
                }
            }
        }

        static void PaintPirateGalleon(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 230);
            float dir = ev.DirX > 0 ? 1 : -1;
            float bob = (float)Math.Sin(t * 6) * 3;
            cy += bob;

            // Hull — wooden ship shape, bigger
            PointF[] hull = {
                new PointF(cx + dir * 16, cy + 2), new PointF(cx + dir * 12, cy + 8),
                new PointF(cx - dir * 12, cy + 8), new PointF(cx - dir * 14, cy + 3),
                new PointF(cx - dir * 10, cy), new PointF(cx + dir * 10, cy)
            };
            using (var b = new SolidBrush(Color.FromArgb(a, 160, 100, 55)))
                g.FillPolygon(b, hull);
            // Hull stripe
            using (var p = new Pen(Color.FromArgb(a, 200, 160, 80), 0.8f))
                g.DrawLine(p, cx - dir * 10, cy + 3, cx + dir * 13, cy + 3);

            // Mast — taller
            using (var p = new Pen(Color.FromArgb(a, 180, 140, 80), 1.5f))
                g.DrawLine(p, cx, cy, cx, cy - 22);
            // Cross spar
            using (var p = new Pen(Color.FromArgb(a, 170, 130, 70), 1f))
                g.DrawLine(p, cx - dir * 1, cy - 18, cx + dir * 12, cy - 18);
            using (var p = new Pen(Color.FromArgb(a, 170, 130, 70), 1f))
                g.DrawLine(p, cx - dir * 1, cy - 8, cx + dir * 10, cy - 8);

            // Main sail — big, billowing, ANIMATED
            float sailBillow = 3 + (float)Math.Sin(t * 8) * 2;
            PointF[] sail = {
                new PointF(cx, cy - 18), new PointF(cx + dir * (11 + sailBillow), cy - 14),
                new PointF(cx + dir * (10 + sailBillow), cy - 8), new PointF(cx, cy - 8)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.8f), 230, 230, 245)))
                g.FillPolygon(b, sail);

            // Lower sail
            float sailBillow2 = 2 + (float)Math.Sin(t * 8 + 1) * 1.5f;
            PointF[] sail2 = {
                new PointF(cx, cy - 8), new PointF(cx + dir * (9 + sailBillow2), cy - 5),
                new PointF(cx + dir * (8 + sailBillow2), cy), new PointF(cx, cy)
            };
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.7f), 220, 220, 235)))
                g.FillPolygon(b, sail2);

            // Skull on sail — bigger, more visible
            float skullX = cx + dir * (4 + sailBillow * 0.3f);
            using (var b = new SolidBrush(Color.FromArgb(a, 245, 245, 245)))
                g.FillEllipse(b, skullX - 2.5f, cy - 15, 5, 4);
            using (var b = new SolidBrush(Color.FromArgb(a, 20, 20, 20))) {
                g.FillEllipse(b, skullX - 1.5f, cy - 14, 1.2f, 1.2f);
                g.FillEllipse(b, skullX + 0.5f, cy - 14, 1.2f, 1.2f);
            }
            // Crossbones
            using (var p = new Pen(Color.FromArgb(a, 240, 240, 240), 0.5f)) {
                g.DrawLine(p, skullX - 2, cy - 11, skullX + 2, cy - 13);
                g.DrawLine(p, skullX + 2, cy - 11, skullX - 2, cy - 13);
            }

            // Flag at top — Jolly Roger, fluttering
            float flagWave = (float)Math.Sin(t * 14) * 2;
            using (var b = new SolidBrush(Color.FromArgb(a, 20, 20, 25)))
                g.FillRectangle(b, cx + dir * 1, cy - 25, 6 * dir, 3 + flagWave * 0.2f);

            // Bowsprit — pointy front
            using (var p = new Pen(Color.FromArgb(a, 180, 140, 80), 1f))
                g.DrawLine(p, cx + dir * 10, cy, cx + dir * 20, cy - 3);

            // Cosmic trail — glowing wake
            var rng = new Random(ev.Seed);
            for (int s = 0; s < 6; s++) {
                float sx = cx - dir * (10 + s * 5) + (float)(rng.NextDouble() - 0.5) * 4;
                float sy = cy + 3 + (float)(rng.NextDouble() - 0.5) * 4;
                int sa = (int)(a * 0.2f * (1f - s / 6f));
                using (var b = new SolidBrush(Color.FromArgb(sa, 100, 180, 255)))
                    g.FillEllipse(b, sx - 2, sy - 1, 4, 2);
            }
        }

        static void PaintBattleFleet(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 220);
            float bob = (float)Math.Sin(t * 6) * 2;
            cy += bob;
            float dir = ev.DirX > 0 ? 1 : -1;

            // Warp-in flash at start
            if (t < 0.08f) {
                int fa = (int)(a * (1f - t / 0.08f) * 0.3f);
                using (var b = new SolidBrush(Color.FromArgb(fa, 150, 200, 255)))
                    g.FillEllipse(b, cx - 30, cy - 15, 60, 30);
            }

            // V-formation of 9 ships — BIGGER
            for (int ship = 0; ship < 9; ship++) {
                int row = ship < 1 ? 0 : ship < 3 ? 1 : ship < 5 ? 2 : ship < 7 ? 3 : 4;
                int col = ship < 1 ? 0 : (ship % 2 == 0 ? -1 : 1) * ((row + 1) / 2);
                float sx = cx - dir * row * 10;
                float sy = cy + col * 7;
                float enginePulse = 0.6f + 0.4f * (float)Math.Sin(t * 25 + ship * 0.5f);

                // Engine trail — blue streaks behind each ship
                for (int tr = 0; tr < 4; tr++) {
                    float trX = sx - dir * (4 + tr * 3);
                    int trA = (int)(a * 0.3f * enginePulse * (1f - tr * 0.2f));
                    if (trA > 0)
                        using (var b = new SolidBrush(Color.FromArgb(trA, 100, 180, 255)))
                            g.FillEllipse(b, trX - 1, sy - 0.8f, 2, 1.6f);
                }

                // Ship body — bigger wedge triangle
                PointF[] body = {
                    new PointF(sx + dir * 6, sy),
                    new PointF(sx - dir * 3, sy - 3),
                    new PointF(sx - dir * 3, sy + 3)
                };
                using (var b = new SolidBrush(Color.FromArgb(a, 190, 195, 210)))
                    g.FillPolygon(b, body);

                // Cockpit glow
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f), 150, 200, 255)))
                    g.FillEllipse(b, sx + dir * 2 - 1, sy - 0.8f, 2, 1.6f);

                // Engine glow — brighter
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.7f * enginePulse), 100, 180, 255)))
                    g.FillEllipse(b, sx - dir * 4, sy - 1.2f, 2.4f, 2.4f);
            }
        }

        static void PaintSpaceSubmarine(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 220);
            float dir = ev.DirX > 0 ? 1 : -1;
            float bob = (float)Math.Sin(t * 6) * 3;
            cy += bob;
            // Hull — yellow! (Beatles reference)
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 220, 40)))
                g.FillEllipse(b, cx - 12, cy - 5, 24, 10);
            // Conning tower
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 210, 30)))
                g.FillRectangle(b, cx - 2, cy - 9, 5, 5);
            // Periscope
            using (var p = new Pen(Color.FromArgb(a, 240, 200, 30), 0.8f))
                g.DrawLine(p, cx, cy - 9, cx, cy - 13);
            using (var p = new Pen(Color.FromArgb(a, 240, 200, 30), 0.8f))
                g.DrawLine(p, cx, cy - 13, cx + dir * 3, cy - 13);
            // Portholes — 3 circular windows
            for (int port = -1; port <= 1; port++) {
                float px = cx + port * 6;
                using (var b = new SolidBrush(Color.FromArgb(a, 100, 180, 220)))
                    g.FillEllipse(b, px - 2, cy - 2, 4, 4);
                using (var p = new Pen(Color.FromArgb(a, 220, 190, 30), 0.6f))
                    g.DrawEllipse(p, px - 2, cy - 2, 4, 4);
            }
            // Propeller at back
            float propSpin = t * 360 * 4;
            float propX = cx - dir * 12;
            for (int blade = 0; blade < 3; blade++) {
                float bAngle = (propSpin + blade * 120) * (float)(Math.PI / 180);
                float bx = propX + (float)Math.Cos(bAngle) * 3;
                float by = cy + (float)Math.Sin(bAngle) * 3;
                using (var p = new Pen(Color.FromArgb(a, 200, 180, 40), 0.8f))
                    g.DrawLine(p, propX, cy, bx, by);
            }
            // Bubbles
            for (int bub = 0; bub < 4; bub++) {
                float bt = ((t * 3 + bub * 0.3f) % 1f);
                float bubX = cx - dir * (8 + bt * 15);
                float bubY = cy - 4 - bt * 10;
                float bsz = 1 + bt;
                int ba = (int)(a * 0.3f * (1f - bt));
                if (ba > 0)
                    using (var p = new Pen(Color.FromArgb(ba, 200, 230, 255), 0.4f))
                        g.DrawEllipse(p, bubX - bsz, bubY - bsz, bsz * 2, bsz * 2);
            }
        }

        static void PaintHotAirBalloon(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 230);
            float sway = (float)Math.Sin(t * 6) * 3;
            cx += sway;

            float ballR = 13;
            // Balloon envelope — solid base color
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 50, 50)))
                g.FillEllipse(b, cx - ballR, cy - ballR, ballR * 2, ballR * 2);

            // Horizontal color bands — these stay inside the circle naturally
            Color[] bands = { Color.FromArgb(a, 255, 200, 40), Color.FromArgb(a, 50, 200, 60), Color.FromArgb(a, 50, 120, 220) };
            for (int band = 0; band < 3; band++) {
                float by = cy - ballR * 0.4f + band * ballR * 0.45f;
                // Calculate width at this Y using circle equation
                float dy = by - cy;
                float halfW = (float)Math.Sqrt(Math.Max(0, ballR * ballR - dy * dy));
                if (halfW > 1)
                    using (var b = new SolidBrush(bands[band]))
                        g.FillRectangle(b, cx - halfW, by, halfW * 2, ballR * 0.35f);
            }

            // Balloon outline
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f), 180, 150, 80), 0.8f))
                g.DrawEllipse(p, cx - ballR, cy - ballR, ballR * 2, ballR * 2);
            // Highlight
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.2f), 255, 255, 255)))
                g.FillEllipse(b, cx - 7, cy - ballR + 2, 7, 5);

            // Ropes
            using (var p = new Pen(Color.FromArgb((int)(a * 0.6f), 200, 180, 120), 0.6f)) {
                g.DrawLine(p, cx - 5, cy + ballR - 2, cx - 3, cy + ballR + 7);
                g.DrawLine(p, cx + 5, cy + ballR - 2, cx + 3, cy + ballR + 7);
            }
            // Basket
            using (var b = new SolidBrush(Color.FromArgb(a, 180, 130, 60)))
                g.FillRectangle(b, cx - 4, cy + ballR + 7, 8, 5);
            using (var p = new Pen(Color.FromArgb(a, 140, 100, 40), 0.6f))
                g.DrawRectangle(p, cx - 4, cy + ballR + 7, 8, 5);
            // Flame — bright and animated
            float flamePulse = 0.6f + 0.4f * (float)Math.Sin(t * 25);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.7f * flamePulse), 255, 200, 50)))
                g.FillEllipse(b, cx - 2, cy + ballR + 2, 4, 6);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.4f * flamePulse), 255, 255, 180)))
                g.FillEllipse(b, cx - 1, cy + ballR + 3, 2, 4);
        }

        static void PaintSpaceMotorcycle(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;
            // Wheels — two circles
            float wheelR = 4;
            for (int wh = -1; wh <= 1; wh += 2) {
                float wx = cx + wh * dir * 7;
                using (var p = new Pen(Color.FromArgb(a, 200, 200, 210), 1.2f))
                    g.DrawEllipse(p, wx - wheelR, cy + 2, wheelR * 2, wheelR * 2);
                using (var b = new SolidBrush(Color.FromArgb(a, 180, 180, 190)))
                    g.FillEllipse(b, wx - 1, cy + wheelR + 1, 2, 2);
            }
            // Frame — connecting wheels
            using (var p = new Pen(Color.FromArgb(a, 220, 60, 60), 1.5f)) {
                g.DrawLine(p, cx - dir * 7, cy + 4, cx, cy);
                g.DrawLine(p, cx, cy, cx + dir * 7, cy + 4);
            }
            // Engine/tank
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 50, 50)))
                g.FillEllipse(b, cx - 3, cy - 1, 6, 4);
            // Rider — visible leather suit
            using (var b = new SolidBrush(Color.FromArgb(a, 80, 80, 100)))
                g.FillEllipse(b, cx + dir * 1, cy - 7, 5, 6);
            // Helmet — shiny
            using (var b = new SolidBrush(Color.FromArgb(a, 100, 100, 120)))
                g.FillEllipse(b, cx + dir * 1.5f, cy - 11, 5, 5);
            // Visor — bright reflective
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.8f), 130, 220, 255)))
                g.FillEllipse(b, cx + dir * 3, cy - 10, 2.5f, 2.5f);
            // Flame trail
            float flamePulse = 0.6f + 0.4f * (float)Math.Sin(t * 30);
            for (int f = 0; f < 8; f++) {
                float ft = f / 8f;
                float fx = cx - dir * (10 + ft * 20);
                float fy = cy + 5 + (float)Math.Sin(t * 25 + f) * 2;
                int fa = (int)(a * 0.4f * (1f - ft) * flamePulse);
                if (fa > 0)
                    using (var b = new SolidBrush(Color.FromArgb(fa, 255, 100 + (int)(ft * 100), 30)))
                        g.FillEllipse(b, fx - 1.5f, fy - 1, 3, 2);
            }
        }

        static void PaintCosmicWizard(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 220);
            float dir = ev.DirX > 0 ? 1 : -1;
            float drift = (float)Math.Sin(t * 6) * 3;
            cy += drift;
            // Robe — flowing purple
            PointF[] robe = {
                new PointF(cx - 4, cy - 2), new PointF(cx + 4, cy - 2),
                new PointF(cx + 6, cy + 12), new PointF(cx - 6, cy + 12)
            };
            using (var b = new SolidBrush(Color.FromArgb(a, 100, 50, 180)))
                g.FillPolygon(b, robe);
            // Head
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 210, 180)))
                g.FillEllipse(b, cx - 3, cy - 7, 6, 6);
            // Beard — white
            using (var b = new SolidBrush(Color.FromArgb(a, 230, 230, 235)))
                g.FillEllipse(b, cx - 2, cy - 2, 4, 5);
            // Pointy hat — THE defining wizard feature
            PointF[] hat = { new PointF(cx, cy - 18), new PointF(cx - 5, cy - 5), new PointF(cx + 5, cy - 5) };
            using (var b = new SolidBrush(Color.FromArgb(a, 80, 40, 160)))
                g.FillPolygon(b, hat);
            // Stars on hat
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.8f), 255, 230, 100))) {
                g.FillEllipse(b, cx - 2, cy - 13, 2, 2);
                g.FillEllipse(b, cx + 1, cy - 9, 1.5f, 1.5f);
            }
            // Wand — in hand, shooting stars
            float wandX = cx + dir * 7, wandY = cy - 1;
            using (var p = new Pen(Color.FromArgb(a, 180, 140, 80), 1f))
                g.DrawLine(p, cx + dir * 3, cy, wandX, wandY - 4);
            // Wand tip sparkle
            float sparkle = 0.5f + 0.5f * (float)Math.Sin(t * 25);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * sparkle), 255, 255, 200)))
                g.FillEllipse(b, wandX - 2, wandY - 6, 4, 4);
            // Star particles from wand
            var rng = new Random(ev.Seed + (int)(t * 10));
            for (int s = 0; s < 5; s++) {
                float st = ((t * 8 + s * 0.2f) % 1f);
                float sx = wandX + dir * st * 15 + (float)(rng.NextDouble() - 0.5) * 6;
                float sy = wandY - 5 + (float)(rng.NextDouble() - 0.5) * 6 - st * 5;
                int sa = (int)(a * 0.5f * (1f - st));
                if (sa > 0)
                    using (var b = new SolidBrush(Color.FromArgb(sa, 255, 230, 100)))
                        g.FillEllipse(b, sx - 1, sy - 1, 2, 2);
            }
        }

        static void PaintSpaceSnowman(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 220);
            float bob = (float)Math.Sin(t * 6) * 2;
            cy += bob;
            // Bottom ball — biggest
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 245, 255)))
                g.FillEllipse(b, cx - 8, cy + 2, 16, 12);
            // Middle ball
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 245, 255)))
                g.FillEllipse(b, cx - 6, cy - 6, 12, 10);
            // Head — smallest
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 245, 255)))
                g.FillEllipse(b, cx - 4.5f, cy - 13, 9, 8);
            // Top hat
            using (var b = new SolidBrush(Color.FromArgb(a, 30, 30, 35)))
                g.FillRectangle(b, cx - 4, cy - 19, 8, 7);
            using (var b = new SolidBrush(Color.FromArgb(a, 30, 30, 35)))
                g.FillRectangle(b, cx - 5.5f, cy - 13, 11, 2);
            // Eyes — coal
            using (var b = new SolidBrush(Color.FromArgb(a, 20, 20, 20))) {
                g.FillEllipse(b, cx - 2.5f, cy - 11, 2, 2);
                g.FillEllipse(b, cx + 1, cy - 11, 2, 2);
            }
            // Carrot nose
            PointF[] nose = { new PointF(cx, cy - 9), new PointF(cx + 4, cy - 8.5f), new PointF(cx, cy - 8) };
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 140, 30)))
                g.FillPolygon(b, nose);
            // Scarf — red
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 40, 40)))
                g.FillRectangle(b, cx - 6, cy - 6, 12, 2.5f);
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 40, 40)))
                g.FillRectangle(b, cx + 4, cy - 6, 2.5f, 6);
            // Buttons
            for (int btn = 0; btn < 3; btn++)
                using (var b = new SolidBrush(Color.FromArgb(a, 20, 20, 20)))
                    g.FillEllipse(b, cx - 0.8f, cy - 3 + btn * 3, 1.6f, 1.6f);
            // Stick arms
            using (var p = new Pen(Color.FromArgb(a, 120, 80, 40), 1f)) {
                g.DrawLine(p, cx - 6, cy - 2, cx - 14, cy - 6);
                g.DrawLine(p, cx + 6, cy - 2, cx + 14, cy - 6);
            }
        }

        static void PaintCosmicHourglass(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 210);
            float op; if (t < 0.1f) op = t / 0.1f; else if (t > 0.9f) op = (1f - t) / 0.1f; else op = 1f;
            // Frame — golden outline
            using (var p = new Pen(Color.FromArgb((int)(a * op), 220, 190, 100), 1.5f)) {
                // Top triangle
                g.DrawLine(p, cx - 7, cy - 12, cx + 7, cy - 12);
                g.DrawLine(p, cx - 7, cy - 12, cx, cy);
                g.DrawLine(p, cx + 7, cy - 12, cx, cy);
                // Bottom triangle
                g.DrawLine(p, cx - 7, cy + 12, cx + 7, cy + 12);
                g.DrawLine(p, cx - 7, cy + 12, cx, cy);
                g.DrawLine(p, cx + 7, cy + 12, cx, cy);
            }
            // Sand in top — depleting
            float sandLevel = 1f - (t % 1f);
            int sandA = (int)(a * 0.6f * op);
            if (sandLevel > 0.1f) {
                float topH = 10 * sandLevel;
                float topW = 5 * sandLevel;
                using (var b = new SolidBrush(Color.FromArgb(sandA, 255, 220, 130)))
                    g.FillPolygon(b, new[] { new PointF(cx - topW, cy - 12 + (10 - topH)), new PointF(cx + topW, cy - 12 + (10 - topH)), new PointF(cx, cy - 1) });
            }
            // Sand in bottom — accumulating
            float botH = 10 * (1f - sandLevel);
            float botW = 5 * (1f - sandLevel);
            if (botH > 0.5f)
                using (var b = new SolidBrush(Color.FromArgb(sandA, 255, 220, 130)))
                    g.FillPolygon(b, new[] { new PointF(cx - botW, cy + 12), new PointF(cx + botW, cy + 12), new PointF(cx, cy + 12 - botH) });
            // Falling sand stream — animated particles
            for (int grain = 0; grain < 6; grain++) {
                float gLife = ((t * 12 + grain * 0.15f) % 1f);
                float gy = cy - 1 + gLife * 3;
                float gx = cx + (float)Math.Sin(t * 30 + grain * 2) * 0.5f;
                int ga = (int)(a * 0.7f * op * (1f - gLife * 0.5f));
                if (ga > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ga, 255, 230, 140)))
                        g.FillEllipse(b, gx - 0.6f, gy - 0.6f, 1.2f, 1.2f);
            }
            using (var p = new Pen(Color.FromArgb((int)(a * 0.6f * op), 255, 230, 140), 1f))
                g.DrawLine(p, cx, cy - 1, cx, cy + 2);
            // Glow
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.06f * op), 255, 220, 130)))
                g.FillEllipse(b, cx - 12, cy - 15, 24, 30);
        }

        static void PaintSpaceAnchor(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float tumble = (float)Math.Sin(t * 3) * 12;

            // Glow aura
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.08f), 160, 200, 240)))
                g.FillEllipse(b, cx - 20, cy - 24, 40, 44);

            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(tumble);

            // Ring at top — big, bright
            using (var p = new Pen(Color.FromArgb(a, 230, 235, 250), 2.5f))
                g.DrawEllipse(p, -5, -20, 10, 8);
            // Shaft — thick bright vertical line
            using (var p = new Pen(Color.FromArgb(a, 230, 235, 250), 3f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, 0, -13, 0, 10);
            }
            // Cross bar — wide
            using (var p = new Pen(Color.FromArgb(a, 230, 235, 250), 3f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, -9, -5, 9, -5);
            }
            // Flukes — big curved hooks, the DEFINING feature
            using (var p = new Pen(Color.FromArgb(a, 230, 235, 250), 3f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Triangle;
                g.DrawArc(p, -12, 4, 12, 12, 0, 130);
                g.DrawArc(p, 0, 4, 12, 12, 50, 130);
            }

            g.Restore(state);

            // Cosmic chain links trailing up — animated sway
            for (int link = 0; link < 5; link++) {
                float ly = cy - 22 - link * 6;
                float lx = cx + (float)Math.Sin(t * 6 + link * 1.2f) * 3;
                int la = (int)(a * 0.6f * (1f - link * 0.12f));
                using (var p = new Pen(Color.FromArgb(la, 200, 215, 240), 1.4f))
                    g.DrawEllipse(p, lx - 2.5f, ly - 3, 5, 6);
            }
        }

        static void PaintCosmicDice(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);

            // Cosmic glow between dice
            float glow = 0.5f + 0.5f * (float)Math.Sin(t * 15);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.12f * glow), 200, 180, 255)))
                g.FillEllipse(b, cx - 14, cy - 14, 28, 28);

            for (int die = 0; die < 2; die++) {
                float baseX = cx + (die == 0 ? -8 : 8);
                float baseY = cy + (die == 0 ? -4 : 4);
                float rot = t * 180 + die * 120; // VISIBLE rotation

                var state = g.Save();
                g.TranslateTransform(baseX, baseY);
                g.RotateTransform(rot % 360);

                // Die body — white rounded square
                using (var b = new SolidBrush(Color.FromArgb(a, 245, 245, 250)))
                    g.FillRectangle(b, -6, -6, 12, 12);
                using (var p = new Pen(Color.FromArgb(a, 180, 180, 200), 0.8f))
                    g.DrawRectangle(p, -6, -6, 12, 12);

                // Pips change as it "rolls" — face changes based on time
                int face = 1 + ((int)(t * 8 + die * 3) % 6);
                int pipA = (int)(a * 0.95f);
                float pr = 1.3f;
                if (face == 1 || face == 3 || face == 5)
                    using (var b = new SolidBrush(Color.FromArgb(pipA, 20, 20, 30)))
                        g.FillEllipse(b, -pr, -pr, pr * 2, pr * 2);
                if (face >= 2) {
                    using (var b = new SolidBrush(Color.FromArgb(pipA, 20, 20, 30))) {
                        g.FillEllipse(b, 2, -4, pr * 2, pr * 2);
                        g.FillEllipse(b, -2 - pr * 2, 2, pr * 2, pr * 2);
                    }
                }
                if (face >= 4) {
                    using (var b = new SolidBrush(Color.FromArgb(pipA, 20, 20, 30))) {
                        g.FillEllipse(b, -2 - pr * 2, -4, pr * 2, pr * 2);
                        g.FillEllipse(b, 2, 2, pr * 2, pr * 2);
                    }
                }
                if (face == 6) {
                    using (var b = new SolidBrush(Color.FromArgb(pipA, 20, 20, 30))) {
                        g.FillEllipse(b, -2 - pr * 2, -1, pr * 2, pr * 2);
                        g.FillEllipse(b, 2, -1, pr * 2, pr * 2);
                    }
                }
                g.Restore(state);
            }
        }

        static void PaintRobotDog(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;
            float walk = (float)Math.Sin(t * 15) * 2;
            // Body — boxy metallic
            using (var b = new SolidBrush(Color.FromArgb(a, 180, 190, 210)))
                g.FillRectangle(b, cx - 7, cy - 3, 14, 7);
            // Head — box with snout
            float headX = cx + dir * 7;
            using (var b = new SolidBrush(Color.FromArgb(a, 190, 200, 220)))
                g.FillRectangle(b, headX - 4, cy - 6, 8, 5);
            // Snout
            using (var b = new SolidBrush(Color.FromArgb(a, 170, 180, 200)))
                g.FillRectangle(b, headX + dir * 3, cy - 4, 3 * Math.Abs(dir), 3);
            // Eye — LED red
            float eyePulse = 0.5f + 0.5f * (float)Math.Sin(t * 20);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * eyePulse), 255, 50, 50)))
                g.FillEllipse(b, headX + dir * 1, cy - 5, 2, 2);
            // Ears — antenna
            using (var p = new Pen(Color.FromArgb(a, 200, 210, 230), 0.8f)) {
                g.DrawLine(p, headX - 2, cy - 6, headX - 3, cy - 9);
                g.DrawLine(p, headX + 2, cy - 6, headX + 3, cy - 9);
            }
            // Legs — 4 mechanical, walking animation
            for (int leg = 0; leg < 4; leg++) {
                float lx = cx - 5 + leg * 3.5f;
                float phase = (leg % 2 == 0) ? walk : -walk;
                using (var p = new Pen(Color.FromArgb(a, 160, 170, 190), 1.5f))
                    g.DrawLine(p, lx, cy + 4, lx + phase * 0.3f, cy + 9 + phase);
            }
            // Tail — wagging antenna
            float tailWag = (float)Math.Sin(t * 20) * 10;
            using (var p = new Pen(Color.FromArgb(a, 200, 210, 230), 0.8f))
                g.DrawLine(p, cx - dir * 7, cy - 2, cx - dir * 10, cy - 5 + tailWag * 0.3f);
            // Tail tip LED
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f), 100, 200, 255)))
                g.FillEllipse(b, cx - dir * 10 - 1, cy - 6 + tailWag * 0.3f, 2, 2);
        }

        static void PaintRingNebula(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 200);
            float op; if (t < 0.15f) op = t / 0.15f; else if (t > 0.85f) op = (1f - t) / 0.15f; else op = 1f;
            float baseR = 20;
            // Outer glow
            for (int gl = 4; gl >= 1; gl--) {
                float gr = baseR + gl * 6;
                int ga = (int)(a * 0.04f * op / gl);
                if (ga > 0) using (var b = new SolidBrush(Color.FromArgb(ga, 100, 200, 180)))
                    g.FillEllipse(b, cx - gr, cy - gr * 0.7f, gr * 2, gr * 1.4f);
            }
            // Ring body — donut shape via two ellipses
            for (int ring = 0; ring < 3; ring++) {
                float rr = baseR - ring * 3;
                float thick = 3 - ring * 0.5f;
                float pulse = 0.6f + 0.4f * (float)Math.Sin(t * 8 + ring * 2);
                int ra = (int)(a * 0.35f * pulse * op);
                Color rc = ring == 0 ? Color.FromArgb(ra, 100, 220, 180) : ring == 1 ? Color.FromArgb(ra, 150, 180, 230) : Color.FromArgb(ra, 200, 120, 200);
                using (var p = new Pen(rc, thick))
                    g.DrawEllipse(p, cx - rr, cy - rr * 0.5f, rr * 2, rr);
            }
            // Central star
            float starPulse = 0.5f + 0.5f * (float)Math.Sin(t * 15);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.6f * starPulse * op), 220, 240, 255)))
                g.FillEllipse(b, cx - 2, cy - 1.5f, 4, 3);
        }

        static void PaintCosmicLightning(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 220);
            float op; if (t < 0.05f) op = t / 0.05f; else if (t > 0.7f) op = (1f - t) / 0.3f; else op = 1f;
            // Multiple lightning bolts — flash pattern
            float flashPhase = (t * 12) % 1f;
            if (flashPhase < 0.4f) {
                float flashA = flashPhase < 0.1f ? flashPhase / 0.1f : (0.4f - flashPhase) / 0.3f;
                var rng = new Random(ev.Seed + (int)(t * 8));
                for (int bolt = 0; bolt < 3; bolt++) {
                    float bx = cx + (float)(rng.NextDouble() - 0.5) * 60;
                    float by = cy - 30;
                    int segments = 6 + rng.Next(4);
                    float prevX = bx, prevY = by;
                    for (int seg = 0; seg < segments; seg++) {
                        float nx = prevX + (float)(rng.NextDouble() - 0.5) * 15;
                        float ny = prevY + 8 + (float)rng.NextDouble() * 6;
                        int la = (int)(a * 0.6f * flashA * op);
                        using (var p = new Pen(Color.FromArgb(la, 180, 200, 255), 2f))
                            g.DrawLine(p, prevX, prevY, nx, ny);
                        using (var p = new Pen(Color.FromArgb((int)(la * 0.4f), 140, 170, 255), 4f))
                            g.DrawLine(p, prevX, prevY, nx, ny);
                        prevX = nx; prevY = ny;
                    }
                }
                // Flash glow
                int glowA = (int)(a * 0.1f * flashA * op);
                using (var b = new SolidBrush(Color.FromArgb(glowA, 160, 180, 255)))
                    g.FillEllipse(b, cx - 40, cy - 35, 80, 70);
            }
        }

        static void PaintStarNursery(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 200);
            float op; if (t < 0.15f) op = t / 0.15f; else if (t > 0.85f) op = (1f - t) / 0.15f; else op = 1f;
            // Gas cloud — warm pinkish nebula
            var rng = new Random(ev.Seed);
            for (int cloud = 0; cloud < 6; cloud++) {
                float clx = cx + (float)(rng.NextDouble() - 0.5) * 40;
                float cly = cy + (float)(rng.NextDouble() - 0.5) * 30;
                float csz = 15 + (float)rng.NextDouble() * 10;
                float pulse = 0.4f + 0.3f * (float)Math.Sin(t * 5 + cloud * 1.5f);
                int ca = (int)(a * 0.08f * pulse * op);
                using (var b = new SolidBrush(Color.FromArgb(ca, 255, 140, 160)))
                    g.FillEllipse(b, clx - csz, cly - csz * 0.6f, csz * 2, csz * 1.2f);
            }
            // Baby stars being born — bright points pulsing
            for (int star = 0; star < 8; star++) {
                float sx = cx + (float)(rng.NextDouble() - 0.5) * 50;
                float sy = cy + (float)(rng.NextDouble() - 0.5) * 30;
                float birthPhase = (t * 3 + star * 0.12f) % 1f;
                float brightness = birthPhase < 0.5f ? birthPhase * 2 : 2f - birthPhase * 2;
                int sa = (int)(a * 0.7f * brightness * op);
                float sz = 1 + brightness * 2;
                if (sa > 0) {
                    using (var b = new SolidBrush(Color.FromArgb(sa / 3, 255, 200, 180)))
                        g.FillEllipse(b, sx - sz * 2, sy - sz * 2, sz * 4, sz * 4);
                    using (var b = new SolidBrush(Color.FromArgb(sa, 255, 240, 220)))
                        g.FillEllipse(b, sx - sz, sy - sz, sz * 2, sz * 2);
                }
            }
        }

        static void PaintGravityWave(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = ev.X * w, cy = ev.Y * h;
            int a = (int)(fade * 210);
            float op; if (t < 0.1f) op = t / 0.1f; else if (t > 0.8f) op = (1f - t) / 0.2f; else op = 1f;
            // Expanding ripple rings
            for (int ring = 0; ring < 6; ring++) {
                float phase = (t * 4 + ring * 0.15f) % 1f;
                float rr = phase * Math.Max(w, h) * 0.4f;
                float ringFade = (1f - phase);
                int ra = (int)(a * 0.2f * ringFade * op);
                if (ra > 0)
                    using (var p = new Pen(Color.FromArgb(ra, 150, 180, 255), 1.5f - phase))
                        g.DrawEllipse(p, cx - rr, cy - rr * 0.5f, rr * 2, rr);
            }
            // Source — two orbiting masses
            float orbitAngle = t * 600 * (float)(Math.PI / 180);
            float orbitR = 6 * (1f - t * 0.5f);
            for (int mass = 0; mass < 2; mass++) {
                float ma = orbitAngle + mass * (float)Math.PI;
                float mx = cx + (float)Math.Cos(ma) * orbitR;
                float my = cy + (float)Math.Sin(ma) * orbitR * 0.4f;
                using (var b = new SolidBrush(Color.FromArgb((int)(a * op), 200, 220, 255)))
                    g.FillEllipse(b, mx - 2, my - 2, 4, 4);
            }
        }

        static void PaintHaloRing(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 210);
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            // Massive ring structure — Halo-style ring world
            float ringW = 50, ringH = 20;
            // Ring body — metallic arc
            using (var p = new Pen(Color.FromArgb(a, 160, 170, 190), 3f))
                g.DrawArc(p, cx - ringW, cy - ringH, ringW * 2, ringH * 2, 200, 140);
            // Inner surface — with tiny lights (cities)
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f), 130, 150, 170), 1.5f))
                g.DrawArc(p, cx - ringW + 2, cy - ringH + 2, ringW * 2 - 4, ringH * 2 - 4, 200, 140);
            // City lights on the inner surface
            for (int city = 0; city < 12; city++) {
                float cityAngle = (200 + city * 11.7f) * (float)(Math.PI / 180);
                float cityR = ringW - 1;
                float cityX = cx + (float)Math.Cos(cityAngle) * cityR;
                float cityY = cy + (float)Math.Sin(cityAngle) * (ringH - 1);
                float cityBright = 0.3f + 0.7f * (float)Math.Sin(t * 15 + city * 2.3f);
                int ca = (int)(a * 0.5f * cityBright);
                if (ca > 0)
                    using (var b = new SolidBrush(Color.FromArgb(ca, 255, 240, 180)))
                        g.FillEllipse(b, cityX - 0.8f, cityY - 0.8f, 1.6f, 1.6f);
            }
            // Atmospheric haze on inner surface
            using (var p = new Pen(Color.FromArgb((int)(a * 0.08f), 100, 180, 220), 6f))
                g.DrawArc(p, cx - ringW + 3, cy - ringH + 3, ringW * 2 - 6, ringH * 2 - 6, 210, 120);
        }

        static void PaintEasterEgg(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 220);
            float tumble = (float)Math.Sin(t * 6) * 12;
            float bob = (float)Math.Sin(t * 10) * 3;
            cy += bob;

            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(tumble);

            // Egg shape — tall oval
            using (var b = new SolidBrush(Color.FromArgb(a, 200, 160, 220)))
                g.FillEllipse(b, -7, -10, 14, 18);
            // Zigzag stripe pattern
            using (var p = new Pen(Color.FromArgb(a, 255, 200, 80), 1.2f)) {
                for (int z = 0; z < 5; z++) {
                    float zy = -6 + z * 3;
                    float zw = 5 - Math.Abs(z - 2) * 0.8f;
                    g.DrawLine(p, -zw, zy, 0, zy + 1.5f);
                    g.DrawLine(p, 0, zy + 1.5f, zw, zy);
                }
            }
            // Polka dots
            using (var b = new SolidBrush(Color.FromArgb(a, 100, 200, 255))) {
                g.FillEllipse(b, -4, -5, 2.5f, 2.5f);
                g.FillEllipse(b, 2, -2, 2, 2);
                g.FillEllipse(b, -2, 3, 2.5f, 2.5f);
            }

            g.Restore(state);

            // Sparkles around
            var rng = new Random(ev.Seed);
            for (int s = 0; s < 6; s++) {
                float sp = (float)Math.Sin(t * 18 + s * 2);
                if (sp > 0.2f) {
                    float sx = cx + (float)(rng.NextDouble() - 0.5) * 24;
                    float sy = cy + (float)(rng.NextDouble() - 0.5) * 28;
                    int sa = (int)(a * 0.4f * (sp - 0.2f));
                    using (var b = new SolidBrush(Color.FromArgb(sa, 255, 255, 220)))
                        g.FillEllipse(b, sx - 1, sy - 1, 2, 2);
                }
            }
        }

        static void PaintTennisRacket(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;
            // Racket head — oval frame
            using (var p = new Pen(Color.FromArgb(a, 200, 60, 60), 1.5f))
                g.DrawEllipse(p, cx - 7, cy - 10, 14, 16);
            // Strings — grid
            for (int sv = -2; sv <= 2; sv++)
                using (var p = new Pen(Color.FromArgb((int)(a * 0.4f), 230, 230, 230), 0.3f))
                    g.DrawLine(p, cx + sv * 2.5f, cy - 8, cx + sv * 2.5f, cy + 4);
            for (int sh = -2; sh <= 2; sh++)
                using (var p = new Pen(Color.FromArgb((int)(a * 0.4f), 230, 230, 230), 0.3f))
                    g.DrawLine(p, cx - 5, cy - 2 + sh * 2.5f, cx + 5, cy - 2 + sh * 2.5f);
            // Handle
            using (var p = new Pen(Color.FromArgb(a, 180, 140, 80), 2.5f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, cx, cy + 6, cx, cy + 14);
            }
            // Tennis ball — bouncing off
            float ballPhase = (t * 6) % 1f;
            float ballX = cx + dir * (ballPhase * 30);
            float ballY = cy - 2 - (float)Math.Sin(ballPhase * Math.PI) * 15;
            int ballA = (int)(a * (1f - ballPhase * 0.7f));
            using (var b = new SolidBrush(Color.FromArgb(ballA, 200, 255, 50)))
                g.FillEllipse(b, ballX - 3, ballY - 3, 6, 6);
            using (var p = new Pen(Color.FromArgb(ballA, 240, 255, 150), 0.5f))
                g.DrawArc(p, ballX - 2, ballY - 3, 4, 6, -60, 120);
        }

        static void PaintSchoolOfFish(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            float bob = (float)Math.Sin(t * 8) * 3;
            cy += bob;
            int a = (int)(fade * 210);
            float dir = ev.DirX > 0 ? 1 : -1;
            var rng = new Random(ev.Seed);
            for (int fish = 0; fish < 15; fish++) {
                float fx = cx + (float)(rng.NextDouble() - 0.5) * 40;
                float fy = cy + (float)(rng.NextDouble() - 0.5) * 25;
                // School movement — each fish follows slightly different wave
                fx += (float)Math.Sin(t * 10 + fish * 0.8f) * 5;
                fy += (float)Math.Cos(t * 8 + fish * 1.1f) * 3;
                float fishWave = (float)Math.Sin(t * 25 + fish * 2) * 1;
                // Fish body — tiny colored fish
                int hue = (fish * 37) % 3;
                Color fc = hue == 0 ? Color.FromArgb(a, 100, 180, 255) : hue == 1 ? Color.FromArgb(a, 255, 160, 80) : Color.FromArgb(a, 80, 220, 160);
                using (var b = new SolidBrush(fc))
                    g.FillEllipse(b, fx - 2.5f, fy - 1.2f + fishWave, 5, 2.4f);
                // Tail
                PointF[] tail = {
                    new PointF(fx - dir * 2.5f, fy + fishWave),
                    new PointF(fx - dir * 5, fy - 1.5f + fishWave),
                    new PointF(fx - dir * 5, fy + 1.5f + fishWave)
                };
                using (var b = new SolidBrush(fc))
                    g.FillPolygon(b, tail);
                // Eye
                using (var b = new SolidBrush(Color.FromArgb(a, 20, 20, 20)))
                    g.FillEllipse(b, fx + dir * 0.8f, fy - 0.5f + fishWave, 1, 1);
            }
        }

        static void PaintFallingStar(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 240);
            float angle = (float)Math.Atan2(ev.DirY, ev.DirX);

            // Bright head
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                g.FillEllipse(b, cx - 2.5f, cy - 2.5f, 5, 5);
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.4f), 255, 255, 220)))
                g.FillEllipse(b, cx - 5, cy - 5, 10, 10);

            // Long bright trail behind
            float tailAngle = angle + (float)Math.PI;
            for (int seg = 0; seg < 20; seg++) {
                float d = seg * 4f;
                float tx = cx + (float)Math.Cos(tailAngle) * d;
                float ty = cy + (float)Math.Sin(tailAngle) * d;
                float segFade = 1f - seg / 20f;
                int sa = (int)(a * 0.6f * segFade);
                float sz = 2f * segFade + 0.5f;
                if (sa > 0)
                    using (var b = new SolidBrush(Color.FromArgb(sa, 255, 240, 180)))
                        g.FillEllipse(b, tx - sz, ty - sz, sz * 2, sz * 2);
            }

            // Sparkle burst around head
            var rng = new Random(ev.Seed + (int)(t * 20));
            for (int s = 0; s < 6; s++) {
                float sAngle = (float)(rng.NextDouble() * Math.PI * 2);
                float sDist = 2 + (float)rng.NextDouble() * 6;
                float sx = cx + (float)Math.Cos(sAngle) * sDist;
                float sy = cy + (float)Math.Sin(sAngle) * sDist;
                float sp = (float)Math.Sin(t * 30 + s * 2);
                if (sp > 0.3f)
                    using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f * sp), 255, 255, 255)))
                        g.FillEllipse(b, sx - 0.8f, sy - 0.8f, 1.6f, 1.6f);
            }
        }

        static void PaintSpaceViolin(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 210);
            float bob = (float)Math.Sin(t * 6) * 2;
            cy += bob;

            // Body — hourglass/figure-8 shape, warm wood color
            using (var b = new SolidBrush(Color.FromArgb(a, 180, 110, 50)))
                g.FillEllipse(b, cx - 5, cy - 2, 10, 7);
            using (var b = new SolidBrush(Color.FromArgb(a, 180, 110, 50)))
                g.FillEllipse(b, cx - 4, cy - 8, 8, 7);
            // Waist
            using (var b = new SolidBrush(Color.FromArgb(a, 170, 100, 45)))
                g.FillRectangle(b, cx - 2.5f, cy - 3, 5, 4);
            // F-holes
            using (var p = new Pen(Color.FromArgb((int)(a * 0.5f), 100, 60, 20), 0.5f)) {
                g.DrawArc(p, cx - 3, cy - 3, 2.5f, 4, -30, 240);
                g.DrawArc(p, cx + 0.5f, cy - 3, 2.5f, 4, -210, 240);
            }
            // Highlight
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.2f), 255, 220, 160)))
                g.FillEllipse(b, cx - 3, cy - 6, 4, 3);

            // Neck + scroll
            using (var b = new SolidBrush(Color.FromArgb(a, 160, 95, 40)))
                g.FillRectangle(b, cx - 1, cy - 14, 2, 7);
            // Scroll
            using (var b = new SolidBrush(Color.FromArgb(a, 160, 95, 40)))
                g.FillEllipse(b, cx - 2, cy - 16, 4, 3);

            // Strings
            using (var p = new Pen(Color.FromArgb((int)(a * 0.6f), 220, 200, 160), 0.3f)) {
                for (int s = -1; s <= 1; s++)
                    g.DrawLine(p, cx + s * 0.8f, cy - 13, cx + s * 1.2f, cy + 3);
            }

            // Musical notes floating away
            for (int note = 0; note < 4; note++) {
                float nLife = (t * 3 + note * 0.25f) % 1f;
                float nx = cx + 8 + nLife * 15 + (float)Math.Sin(t * 8 + note * 2) * 4;
                float ny = cy - 5 - nLife * 10;
                int na = (int)(a * 0.5f * (1f - nLife));
                if (na > 2) {
                    using (var b = new SolidBrush(Color.FromArgb(na, 255, 230, 150)))
                        g.FillEllipse(b, nx - 1.5f, ny - 1, 3, 2);
                    using (var p = new Pen(Color.FromArgb(na, 255, 230, 150), 0.5f))
                        g.DrawLine(p, nx + 1.5f, ny, nx + 1.5f, ny - 4);
                }
            }
        }

        static void PaintRobotCrab(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 220);
            float scuttle = (float)Math.Sin(t * 12) * 2;
            cy += scuttle;

            // Body — wide metallic dome
            using (var b = new SolidBrush(Color.FromArgb(a, 180, 60, 60)))
                g.FillEllipse(b, cx - 8, cy - 4, 16, 8);
            // Metallic highlight
            using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f), 255, 150, 150)))
                g.FillEllipse(b, cx - 4, cy - 3, 8, 3);
            // Rivets
            for (int rivet = -2; rivet <= 2; rivet++) {
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.5f), 140, 140, 160)))
                    g.FillEllipse(b, cx + rivet * 3 - 0.5f, cy - 0.5f, 1.5f, 1.5f);
            }

            // Eyes on stalks — robotic, glowing
            for (int eye = -1; eye <= 1; eye += 2) {
                float eyeX = cx + eye * 4;
                float eyeWobble = (float)Math.Sin(t * 15 + eye) * 1.5f;
                // Stalk
                using (var p = new Pen(Color.FromArgb(a, 160, 160, 175), 1f))
                    g.DrawLine(p, eyeX, cy - 3, eyeX + eye * 1, cy - 8 + eyeWobble);
                // Eye — glowing LED
                float eyePulse = 0.6f + 0.4f * (float)Math.Sin(t * 20 + eye * 3);
                using (var b = new SolidBrush(Color.FromArgb((int)(a * eyePulse), 255, 80, 80)))
                    g.FillEllipse(b, eyeX + eye * 1 - 1.5f, cy - 9.5f + eyeWobble, 3, 3);
                using (var b = new SolidBrush(Color.FromArgb((int)(a * 0.3f * eyePulse), 255, 100, 100)))
                    g.FillEllipse(b, eyeX + eye * 1 - 2.5f, cy - 10.5f + eyeWobble, 5, 5);
            }

            // Claws — big, menacing, mechanical
            for (int claw = -1; claw <= 1; claw += 2) {
                float clawX = cx + claw * 10;
                float clawOpen = 2 + (float)Math.Sin(t * 8 + claw * 2) * 1.5f;
                // Arm
                using (var p = new Pen(Color.FromArgb(a, 170, 170, 185), 1.5f)) {
                    p.StartCap = LineCap.Round;
                    g.DrawLine(p, cx + claw * 7, cy, clawX, cy - 2);
                }
                // Upper pincer
                using (var p = new Pen(Color.FromArgb(a, 200, 70, 70), 1.5f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, clawX, cy - 2, clawX + claw * 4, cy - 2 - clawOpen);
                }
                // Lower pincer
                using (var p = new Pen(Color.FromArgb(a, 200, 70, 70), 1.5f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, clawX, cy - 2, clawX + claw * 4, cy - 2 + clawOpen);
                }
            }

            // Legs — 3 per side, scuttling
            for (int side = -1; side <= 1; side += 2) {
                for (int leg = 0; leg < 3; leg++) {
                    float legPhase = (float)Math.Sin(t * 18 + leg * 2 + side) * 3;
                    float lx = cx + side * (3 + leg * 2.5f);
                    using (var p = new Pen(Color.FromArgb((int)(a * 0.8f), 160, 160, 175), 0.8f))
                        g.DrawLine(p, lx, cy + 3, lx + side * 3, cy + 7 + legPhase);
                }
            }
        }

        static void PaintSpaceWaldo(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx = (ev.X + ev.DirX * t) * w, cy = (ev.Y + ev.DirY * t) * h;
            int a = (int)(fade * 220);
            float bob = (float)Math.Sin(t * 8) * 2;
            cy += bob;

            // He's tiny and hard to spot — that's the point!
            // But still needs to be recognizable when you DO see him

            // Body — red and white striped shirt (THE defining feature)
            for (int stripe = 0; stripe < 5; stripe++) {
                float sy = cy - 4 + stripe * 2;
                Color sc = stripe % 2 == 0 ? Color.FromArgb(a, 220, 30, 30) : Color.FromArgb(a, 240, 240, 240);
                using (var b = new SolidBrush(sc))
                    g.FillRectangle(b, cx - 3, sy, 6, 2);
            }

            // Legs — blue jeans
            using (var b = new SolidBrush(Color.FromArgb(a, 60, 80, 160)))
                g.FillRectangle(b, cx - 3, cy + 6, 2.5f, 5);
            using (var b = new SolidBrush(Color.FromArgb(a, 60, 80, 160)))
                g.FillRectangle(b, cx + 0.5f, cy + 6, 2.5f, 5);
            // Shoes
            using (var b = new SolidBrush(Color.FromArgb(a, 50, 40, 30)))
                g.FillRectangle(b, cx - 3.5f, cy + 11, 3, 1.5f);
            using (var b = new SolidBrush(Color.FromArgb(a, 50, 40, 30)))
                g.FillRectangle(b, cx + 0.5f, cy + 11, 3, 1.5f);

            // Head — round, skin tone
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 200, 170)))
                g.FillEllipse(b, cx - 3, cy - 8, 6, 5);

            // Glasses — round, black frames
            using (var p = new Pen(Color.FromArgb(a, 20, 20, 20), 0.6f)) {
                g.DrawEllipse(p, cx - 3, cy - 7, 2.5f, 2.5f);
                g.DrawEllipse(p, cx + 0.5f, cy - 7, 2.5f, 2.5f);
                g.DrawLine(p, cx - 0.5f, cy - 5.8f, cx + 0.5f, cy - 5.8f);
            }

            // Red and white beanie/hat (THE other defining feature)
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 30, 30)))
                g.FillRectangle(b, cx - 3.5f, cy - 10, 7, 2);
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 240, 240)))
                g.FillRectangle(b, cx - 3.5f, cy - 10, 7, 1);
            // Pompom
            using (var b = new SolidBrush(Color.FromArgb(a, 220, 30, 30)))
                g.FillEllipse(b, cx - 1, cy - 11.5f, 2, 2);

            // Wave — one arm up waving VISIBLY back and forth
            float handX = cx + 4 + (float)Math.Sin(t * 14) * 4;
            float handY = cy - 10 + (float)Math.Cos(t * 14) * 3;
            // Upper arm
            using (var p = new Pen(Color.FromArgb(a, 240, 200, 170), 1.5f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, cx + 3, cy - 2, cx + 5, cy - 6);
            }
            // Forearm waving
            using (var p = new Pen(Color.FromArgb(a, 240, 200, 170), 1.5f)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, cx + 5, cy - 6, handX, handY);
            }
            // Hand — little circle
            using (var b = new SolidBrush(Color.FromArgb(a, 240, 200, 170)))
                g.FillEllipse(b, handX - 1, handY - 1, 2.5f, 2.5f);
            // Other arm down holding walking stick
            using (var p = new Pen(Color.FromArgb(a, 240, 200, 170), 1f))
                g.DrawLine(p, cx - 3, cy - 1, cx - 5, cy + 3);
            using (var p = new Pen(Color.FromArgb(a, 140, 100, 50), 0.8f))
                g.DrawLine(p, cx - 5, cy + 3, cx - 5, cy + 10);

            // Subtle "?" floating above — where's Waldo hint
            float qPulse = 0.3f + 0.4f * (float)Math.Sin(t * 10);
            int qa = (int)(a * qPulse);
            using (var p = new Pen(Color.FromArgb(qa, 255, 255, 200), 0.6f)) {
                g.DrawArc(p, cx - 2, cy - 16, 4, 3, 180, 270);
                g.FillEllipse(new SolidBrush(Color.FromArgb(qa, 255, 255, 200)), cx - 0.5f, cy - 12.5f, 1, 1);
            }
        }

        public static void PaintOrbitingStar(Graphics g, int w, int h, float phase, int cornerRadius)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int radius = cornerRadius;
            float straightW = w - 1 - 2 * radius;
            float straightH = h - 1 - 2 * radius;
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
                PointF pt = OrbitPoint(tt, w, h, radius, straightW, straightH, cornerArc, perimeter);

                if (i == 0)
                {
                    // Star head — brilliant white with visible glow
                    for (int glow = 5; glow >= 0; glow--)
                    {
                        float sz = 6 + glow * 4;
                        int alpha = glow == 0 ? 255 : glow == 1 ? 210 : Math.Max(12, (int)(160.0 / (glow + 1)));
                        using (var brush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                            g.FillEllipse(brush, pt.X - sz / 2, pt.Y - sz / 2, sz, sz);
                    }

                    // Cross sparkle — visible arms
                    int sparkAlpha = (int)(230 + 25 * Math.Sin(phase * 4));
                    float armLen = 12 + 4 * (float)Math.Sin(phase * 3);
                    using (var pen = new Pen(Color.FromArgb(sparkAlpha, 255, 255, 255), 2f))
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
                    float sz = 6f * fade;
                    int alpha = (int)(230 * fade * fade);
                    if (alpha < 5) continue;

                    // Rainbow trail via HSV rotation
                    float hue = ((float)i / trailCount * 360 + phase * 60) % 360;
                    Color rainbow = HsvToColor(hue, 0.85f, 1.0f, alpha);
                    using (var brush = new SolidBrush(rainbow))
                        g.FillEllipse(brush, pt.X - sz / 2, pt.Y - sz / 2, sz, sz);

                    // White glow halo
                    if (fade > 0.4f)
                    {
                        float glowSz = sz * 2.2f;
                        int glowA = (int)(45 * fade);
                        using (var brush = new SolidBrush(Color.FromArgb(glowA, 255, 255, 255)))
                            g.FillEllipse(brush, pt.X - glowSz / 2, pt.Y - glowSz / 2, glowSz, glowSz);
                    }

                    // Rainbow micro-sparkles
                    if (fade > 0.3f && i % 3 == 0)
                    {
                        float ox = (float)(Math.Sin(i * 7.3 + phase * 2.5) * 6);
                        float oy = (float)(Math.Cos(i * 5.1 + phase * 2.5) * 6);
                        float msz = 3f * fade;
                        int mAlpha = (int)(120 * fade);
                        float mHue = ((float)(i + 15) / trailCount * 360 + phase * 80) % 360;
                        Color mColor = HsvToColor(mHue, 0.9f, 1.0f, mAlpha);
                        using (var brush = new SolidBrush(mColor))
                            g.FillEllipse(brush, pt.X + ox - msz / 2, pt.Y + oy - msz / 2, msz, msz);
                    }
                }
            }

            // Soft white border glow
            using (var pen = new Pen(Color.FromArgb(50, 255, 255, 255), 1.5f))
            using (var path = RoundedRect(new Rectangle(0, 0, w - 1, h - 1), radius))
                g.DrawPath(pen, path);
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

}
