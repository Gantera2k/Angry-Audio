// DictationPreview.cs — Bottom-right overlay showing Whisper dictation state.
// Uses UpdateLayeredWindow for buttery smooth per-pixel alpha rendering.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AngryAudio
{
    public class DictationPreview : Form
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left,Top,Right,Bottom; }

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_SHOWWINDOW = 0x0040;
        const uint SWP_NOMOVE     = 0x0002;
        const uint SWP_NOSIZE     = 0x0001;

        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        const int SW_SHOWNOACTIVATE = 4;

        // Per-pixel alpha via UpdateLayeredWindow
        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT2 pptDst, ref SIZE2 pSize,
            IntPtr hdcSrc, ref POINT2 pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll", ExactSpelling = true)]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObj);
        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern bool DeleteObject(IntPtr hObj);
        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern bool DeleteDC(IntPtr hdc);
        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [StructLayout(LayoutKind.Sequential)] struct POINT2 { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE2 { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }
        const int ULW_ALPHA = 0x00000002;
        const int AC_SRC_OVER = 0x00;
        const int AC_SRC_ALPHA = 0x01;

        // ── Shared design constants (uniform with toasts) ──
        static readonly Color BG      = Color.FromArgb(20, 20, 20);
        static readonly Color TXT     = Color.FromArgb(230, 230, 235);
        static readonly Color TXT_DIM = Color.FromArgb(130, 130, 130);
        // State colors
        static readonly Color GREEN   = Color.FromArgb(50, 220, 110);
        static readonly Color BLUE    = Color.FromArgb(90, 160, 255);
        static readonly Color DIM_BLUE = Color.FromArgb(100, 115, 140);
        const int CORNER = 10;

        public enum Mode { Hidden, Listening, Processing, Downloading, Result }
        Mode   _mode = Mode.Hidden;
        string _text = "";
        string _title = "";
        string _sub = "";

        Timer _holdTimer;
        Timer _spinTimer;
        Timer _tickTimer;
        Timer _safetyTimer; // auto-hide after 30s if stuck in Listening
        float _opacity = 0f;
        int _spinAngle = 0;
        int _w, _h;

        public DictationPreview()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            TopMost         = true;
            StartPosition   = FormStartPosition.Manual;
            BackColor       = BG;
            AutoScaleMode   = AutoScaleMode.None;

            _w = Dpi.S(300);
            _h = Dpi.S(68);
            
            Location = new Point(-10000, -10000);
            ClientSize = new Size(_w, _h);

            PositionWindow();

            _holdTimer = new Timer { Interval = 2500 };
            _holdTimer.Tick += (s, e) => { _holdTimer.Stop(); DoHide(); };

            _spinTimer = new Timer { Interval = 16 };
            _spinTimer.Tick += (s, e) => {
                _spinAngle = (_spinAngle + 8) % 360;
                PushLayered();
            };

            _tickTimer = new Timer { Interval = 16 };
            _tickTimer.Tick += (s, e) => {
                if (_mode == Mode.Listening) PushLayered();
            };

            _safetyTimer = new Timer { Interval = 30000 };
            _safetyTimer.Tick += (s, e) => {
                _safetyTimer.Stop();
                if (_mode == Mode.Listening) {
                    Logger.Warn("DictationPreview: safety auto-hide triggered (stuck Listening for 30s).");
                    HideNow();
                }
            };
        }

        // ── State color helper ──
        Color GetAccent() {
            switch (_mode) {
                case Mode.Listening: return GREEN;
                case Mode.Result: return GREEN;
                case Mode.Processing: return DIM_BLUE;
                case Mode.Downloading: return BLUE;
                default: return BLUE;
            }
        }

        // ── Public API ──────────────────────────────────────────────────────

        public void ShowListening()
        {
            _mode = Mode.Listening; _text = "";
            _title = "Listening\u2026";
            _sub = "Release key to transcribe";
            _holdTimer.Stop(); _spinTimer.Stop();
            ResizeForContent();
            DoShow();
            _tickTimer.Start();
            _safetyTimer.Stop(); _safetyTimer.Start(); // reset 30s safety countdown
        }

        public void ShowProcessing()
        {
            _mode = Mode.Processing; _text = "";
            _title = "Processing\u2026";
            _sub = "Transcribing audio";
            _holdTimer.Stop(); _tickTimer.Stop(); _safetyTimer.Stop();
            ResizeForContent();
            DoShow();
            _spinTimer.Start();
        }

        public void ShowDownloading()
        {
            _mode = Mode.Downloading;
            _text = "Downloading Whisper model (~75 MB)\u2026 First use only.";
            _title = _text;
            _sub = "";
            _holdTimer.Stop(); _spinTimer.Stop(); _tickTimer.Stop();
            ResizeForContent();
            DoShow();
            _spinTimer.Start();
        }

        public void ShowResult(string text)
        {
            _spinTimer.Stop(); _tickTimer.Stop(); _safetyTimer.Stop();
            _mode = Mode.Result; _text = text ?? "";
            _title = Truncate(_text, 80);
            _sub = "";
            _holdTimer.Stop();
            ResizeForContent();
            DoShow();
            _holdTimer.Start();
        }

        public void HideNow()
        {
            _holdTimer.Stop(); _spinTimer.Stop(); _tickTimer.Stop(); _safetyTimer.Stop();
            _mode = Mode.Hidden;
            DoHide();
        }

        // ── Internal show/hide ──────────────────────────────────────────────

        void DoShow()
        {
            if (!IsHandleCreated) { var h = Handle; }
            PositionWindow();
            _opacity = 0.88f;
            Visible = true;
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            SetWindowPos(Handle, HWND_TOPMOST, Location.X, Location.Y, _w, _h,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
            PushLayered();
        }

        void DoHide()
        {
            _spinTimer.Stop(); _tickTimer.Stop();
            _opacity = 0f;
            Visible = false;
            ShowWindow(Handle, 0);
        }

        // ── Layout ──────────────────────────────────────────────────────────

        void ResizeForContent()
        {
            int minW = Dpi.S(260);
            _h = Dpi.S(68);

            if ((_mode == Mode.Result || _mode == Mode.Downloading) && _text.Length > 0)
            {
                string display = Truncate(_text, 80);
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                using (var f = new Font("Segoe UI", 9f))
                {
                    var sz = g.MeasureString(display, f, Dpi.S(420));
                    _w = Math.Max(minW, (int)sz.Width + Dpi.S(65));
                    _w = Math.Min(_w, Dpi.S(500));
                    if (sz.Height > Dpi.S(20)) _h = Dpi.S(88);
                }
            }
            else
            {
                _w = Dpi.S(290);
            }

            ClientSize = new Size(_w, _h);
            PositionWindow();
        }

        void PositionWindow()
        {
            var scr = Screen.PrimaryScreen;
            try {
                IntPtr fgWnd = GetForegroundWindow();
                if (fgWnd != IntPtr.Zero) {
                    RECT r;
                    if (GetWindowRect(fgWnd, out r)) {
                        var midPt = new Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
                        var s = Screen.FromPoint(midPt);
                        if (s != null) scr = s;
                    }
                }
            } catch { }
            var area = scr.WorkingArea;
            int x = area.Right  - _w - Dpi.S(16);
            int y = area.Bottom - _h - Dpi.S(32);
            Location = new Point(x, y);
        }

        // ── Rendering — per-pixel alpha via UpdateLayeredWindow ──────────

        protected override void OnPaint(PaintEventArgs e) {
            // All rendering done in PushLayered via UpdateLayeredWindow
        }

        void RenderContent(Graphics g, int w, int h)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.CompositingQuality = CompositingQuality.HighQuality;
            int r = Dpi.S(CORNER);
            Color accent = GetAccent();

            // ── Rounded background fill ──
            using (var path = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), r))
            using (var bgBr = new SolidBrush(Color.FromArgb(255, BG.R, BG.G, BG.B)))
                g.FillPath(bgBr, path);

            // ── Rounded border with glow ──
            using (var path = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), r)) {
                using (var glowPen = new Pen(Color.FromArgb(25, accent), 3f))
                    g.DrawPath(glowPen, path);
                using (var pen = new Pen(Color.FromArgb(120, accent), 1.2f))
                    g.DrawPath(pen, path);
            }

            // ── Accent stripe (gradient fade) ──
            using (var stripeBr = new LinearGradientBrush(new Rectangle(0, Dpi.S(10), Dpi.S(3), Dpi.S(32)),
                accent, Color.FromArgb(0, accent), 90f))
            using (var stripe = new Pen(stripeBr, Dpi.S(2)))
                g.DrawLine(stripe, Dpi.S(2), Dpi.S(12), Dpi.S(2), Dpi.S(42));

            // ── Icon (GDI+ drawn) ──
            int iconSz = Dpi.S(28);
            int iconX = Dpi.S(14);
            int iconY = (h - iconSz) / 2;
            DrawIcon(g, iconX, iconY, iconSz, accent);

            // ── Title ──
            int textX = Dpi.S(52);
            int textW = w - textX - Dpi.S(12);
            using (var fTitle = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            {
                Color titleColor = (_mode == Mode.Processing) ? TXT_DIM : (_mode == Mode.Result ? TXT : accent);
                TextRenderer.DrawText(g, _title, fTitle,
                    new Rectangle(textX, Dpi.S(12), textW, Dpi.S(22)),
                    titleColor, TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            // ── Subtitle ──
            if (!string.IsNullOrEmpty(_sub))
            {
                using (var fSub = new Font("Segoe UI", 7.5f))
                    TextRenderer.DrawText(g, _sub, fSub,
                        new Rectangle(textX, Dpi.S(34), textW, Dpi.S(18)),
                        TXT_DIM, TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }


        }

        private void PushLayered()
        {
            if (!IsHandleCreated) return;
            try
            {
                int w = _w, h = _h;
                if (w <= 0 || h <= 0) return;
                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                        RenderContent(g, w, h);

                    // Apply master opacity by premultiplying alpha
                    byte masterA = (byte)Math.Max(0, Math.Min(255, (int)(_opacity * 255)));
                    if (masterA == 0) { ShowWindow(Handle, 0); return; }
                    var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                    unsafe
                    {
                        byte* scan = (byte*)data.Scan0;
                        int stride = data.Stride;
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                            {
                                int i = y * stride + x * 4;
                                byte a = scan[i + 3];
                                if (a == 0) continue;
                                int na = (a * masterA + 127) / 255;
                                scan[i + 0] = (byte)((scan[i + 0] * na + 127) / 255); // B
                                scan[i + 1] = (byte)((scan[i + 1] * na + 127) / 255); // G
                                scan[i + 2] = (byte)((scan[i + 2] * na + 127) / 255); // R
                                scan[i + 3] = (byte)na;
                            }
                    }
                    bmp.UnlockBits(data);

                    IntPtr screenDC = IntPtr.Zero;
                    IntPtr memDC = IntPtr.Zero;
                    IntPtr hBmp = IntPtr.Zero;
                    IntPtr oldBmp = IntPtr.Zero;
                    try
                    {
                        screenDC = GetDC(IntPtr.Zero);
                        memDC = CreateCompatibleDC(screenDC);
                        hBmp = bmp.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
                        oldBmp = SelectObject(memDC, hBmp);
                        var ptSrc = new POINT2 { x = 0, y = 0 };
                        var ptDst = new POINT2 { x = Location.X, y = Location.Y };
                        var size = new SIZE2 { cx = w, cy = h };
                        var blend = new BLENDFUNCTION
                        {
                            BlendOp = AC_SRC_OVER, BlendFlags = 0,
                            SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA
                        };
                        UpdateLayeredWindow(Handle, screenDC, ref ptDst, ref size, memDC, ref ptSrc, 0, ref blend, ULW_ALPHA);
                    }
                    finally
                    {
                        if (oldBmp != IntPtr.Zero) SelectObject(memDC, oldBmp);
                        if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
                        if (memDC != IntPtr.Zero) DeleteDC(memDC);
                        if (screenDC != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDC);
                    }
                }
            }
            catch { }
        }

        // ── GDI+ drawn icons ──

        void DrawIcon(Graphics g, int x, int y, int sz, Color accent)
        {
            switch (_mode)
            {
                case Mode.Listening:
                    DrawMicIcon(g, x, y, sz, accent);
                    break;
                case Mode.Processing:
                    DrawSpinnerIcon(g, x, y, sz, accent);
                    break;
                case Mode.Downloading:
                    DrawDownloadIcon(g, x, y, sz, accent);
                    break;
                case Mode.Result:
                    DrawCheckIcon(g, x, y, sz, accent);
                    break;
            }
        }

        void DrawMicIcon(Graphics g, int x, int y, int sz, Color col)
        {
            // Outer glow circle
            using (var glowBr = new SolidBrush(Color.FromArgb(25, col)))
                g.FillEllipse(glowBr, x - Dpi.S(3), y - Dpi.S(3), sz + Dpi.S(6), sz + Dpi.S(6));
            // BG circle
            using (var bgBr = new SolidBrush(Color.FromArgb(40, col)))
                g.FillEllipse(bgBr, x, y, sz, sz);
            // Mic body (rounded rect)
            int mw = sz / 4; int mh = sz * 2 / 5;
            int mx = x + (sz - mw) / 2; int my = y + sz / 5;
            using (var micBr = new SolidBrush(col))
            {
                g.FillRectangle(micBr, mx, my, mw, mh);
                g.FillEllipse(micBr, mx - 1, my - mw / 3, mw + 2, mw); // top round
            }
            // Mic stand (arc + stem)
            int arcW = sz / 2; int arcH = sz / 3;
            int arcX = x + (sz - arcW) / 2; int arcY = y + sz / 3;
            using (var arcPen = new Pen(col, Math.Max(1, Dpi.S(2))))
            {
                g.DrawArc(arcPen, arcX, arcY, arcW, arcH, 0, 180);
                int stemX = x + sz / 2;
                g.DrawLine(arcPen, stemX, arcY + arcH / 2 + Dpi.S(1), stemX, y + sz - Dpi.S(4));
                g.DrawLine(arcPen, stemX - Dpi.S(3), y + sz - Dpi.S(4), stemX + Dpi.S(3), y + sz - Dpi.S(4));
            }
        }

        void DrawSpinnerIcon(Graphics g, int x, int y, int sz, Color col)
        {
            // Spinner arc
            int pad = Dpi.S(4);
            using (var pen = new Pen(Color.FromArgb(30, col), Dpi.S(2)))
                g.DrawEllipse(pen, x + pad, y + pad, sz - pad * 2, sz - pad * 2);
            using (var pen = new Pen(col, Dpi.S(2)))
                g.DrawArc(pen, x + pad, y + pad, sz - pad * 2, sz - pad * 2, _spinAngle, 90);
        }

        void DrawDownloadIcon(Graphics g, int x, int y, int sz, Color col)
        {
            // BG circle
            using (var bgBr = new SolidBrush(Color.FromArgb(30, col)))
                g.FillEllipse(bgBr, x, y, sz, sz);
            // Down arrow
            int cx = x + sz / 2; int cy = y + sz / 2;
            int arrowH = sz / 3;
            using (var pen = new Pen(col, Dpi.S(2))) {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx, cy - arrowH / 2, cx, cy + arrowH / 2);
                g.DrawLine(pen, cx - Dpi.S(4), cy + arrowH / 2 - Dpi.S(4), cx, cy + arrowH / 2);
                g.DrawLine(pen, cx + Dpi.S(4), cy + arrowH / 2 - Dpi.S(4), cx, cy + arrowH / 2);
            }
            // Base line
            using (var pen = new Pen(col, Math.Max(1, Dpi.S(2)))) {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - Dpi.S(5), y + sz - Dpi.S(8), cx + Dpi.S(5), y + sz - Dpi.S(8));
            }
        }

        void DrawCheckIcon(Graphics g, int x, int y, int sz, Color col)
        {
            // BG circle
            using (var bgBr = new SolidBrush(Color.FromArgb(30, col)))
                g.FillEllipse(bgBr, x, y, sz, sz);
            // Checkmark
            int cx = x + sz / 2; int cy = y + sz / 2;
            using (var pen = new Pen(col, Math.Max(1, Dpi.S(3)))) {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - Dpi.S(5), cy, cx - Dpi.S(1), cy + Dpi.S(4));
                g.DrawLine(pen, cx - Dpi.S(1), cy + Dpi.S(4), cx + Dpi.S(6), cy - Dpi.S(4));
            }
        }

        // ── Utility ──

        static string Truncate(string t, int max)
        {
            if (string.IsNullOrEmpty(t)) return "";
            t = t.Trim();
            return t.Length > max ? t.Substring(0, max - 1) + "\u2026" : t;
        }

        // ── WinForms overrides ───────────────────────────────────────────────

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x00000008 | 0x08000000 | 0x00000080 | 0x00080000; return cp; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { if (_holdTimer != null) _holdTimer.Dispose(); } catch { }
                try { if (_spinTimer != null) _spinTimer.Dispose(); } catch { }
                try { if (_tickTimer != null) _tickTimer.Dispose(); } catch { }
                try { if (_safetyTimer != null) _safetyTimer.Dispose(); } catch { }

            }
            base.Dispose(disposing);
        }
    }
}
