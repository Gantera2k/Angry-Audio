using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AngryAudio
{
    /// <summary>
    /// DPI scaling helper. Call Dpi.Init() once at startup (after SetProcessDPIAware).
    /// Then use Dpi.S(pixelValue) everywhere instead of raw pixel constants.
    /// Font point sizes do NOT need scaling — they auto-adjust with DPI awareness.
    /// </summary>
    public static class Dpi
    {
        private static float _scale = 1f;
        private static bool _initialized;

        /// <summary>
        /// The DPI scale factor (1.0 at 96 DPI, 1.25 at 120, 1.5 at 144, 2.0 at 192).
        /// </summary>
        public static float Scale { get { return _scale; } }

        /// <summary>
        /// Initialize DPI detection. Must be called AFTER SetProcessDPIAware().
        /// </summary>
        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    _scale = g.DpiX / 96f;
                }
            }
            catch
            {
                _scale = 1f;
            }

            Logger.Info("DPI scale factor: " + _scale + "x (" + (_scale * 96) + " DPI)");
        }

        /// <summary>Scale an integer pixel value.</summary>
        public static int S(int value)
        {
            return (int)(value * _scale + 0.5f);
        }

        /// <summary>Scale a float pixel value.</summary>
        public static float Sf(float value)
        {
            return value * _scale;
        }

        /// <summary>Scale a Size.</summary>
        public static Size Size(int w, int h)
        {
            return new Size(S(w), S(h));
        }

        /// <summary>Scale a Point.</summary>
        public static Point Pt(int x, int y)
        {
            return new Point(S(x), S(y));
        }

        /// <summary>Scale a Padding.</summary>
        public static Padding Pad(int all)
        {
            return new Padding(S(all));
        }

        /// <summary>Scale a Padding.</summary>
        public static Padding Pad(int left, int top, int right, int bottom)
        {
            return new Padding(S(left), S(top), S(right), S(bottom));
        }

        /// <summary>Scale a Rectangle.</summary>
        public static Rectangle Rect(int x, int y, int w, int h)
        {
            return new Rectangle(S(x), S(y), S(w), S(h));
        }

        /// <summary>Scale a RectangleF.</summary>
        public static RectangleF RectF(float x, float y, float w, float h)
        {
            return new RectangleF(Sf(x), Sf(y), Sf(w), Sf(h));
        }

        /// <summary>Scale a pen width.</summary>
        public static float PenW(float width)
        {
            return width * _scale;
        }
    }
}
