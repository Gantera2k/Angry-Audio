// UpdateDialog.cs — Sleek, Toast-style update downloader for Angry Audio.
// Borderless, topmost, fade-in/fade-out. No modal blocking.
// Auto-hides after installation launch or error dismissal.
//
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Windows.Forms;

namespace AngryAudio
{
    public class UpdateDialog : Form
    {
        // Win32 to show without stealing focus
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        // ── Colors (match Toast.cs palette) ─────────────────────────────────
        static readonly Color BG       = Color.FromArgb(20, 20, 20);
        static readonly Color BDR      = Color.FromArgb(44, 44, 44);
        static readonly Color TXT      = Color.FromArgb(230, 230, 230);
        static readonly Color TXT2     = Color.FromArgb(130, 130, 130);
        static readonly Color TXT3     = Color.FromArgb(80,  80,  80);
        static readonly Color ACC      = DarkTheme.Accent;
        static readonly Color GREEN    = DarkTheme.Green;
        static readonly Color AMBER    = Color.FromArgb(255, 185, 70);
        static readonly Color BTN_BG   = Color.FromArgb(34, 34, 34);
        static readonly Color BTN_HOVER = Color.FromArgb(48, 48, 48);

        // ── Layout constants (match Toast.cs dimensions) ─────────────────────
        const int W       = 300;   // form width (Toast.cs = 300)
        const int PAD     = 10;    // horizontal padding (Toast.cs MARGIN = 10)
        const int CORNER  = 10;    // rounded corner radius
        const int MASCOT  = 32;    // mascot size (Toast.cs = 32)
        const int TEXT_X  = 50;    // text left edge (PAD + MASCOT + gap)
        const int TITLE_Y = 8;     // title top (match Toast.cs)
        const int SUB_Y   = 28;    // subtitle top
        const int BAR_Y   = 50;    // progress bar Y
        const int BAR_H   = 12;    // progress bar height — thick enough to see
        const int BTN_H   = 22;    // button height (Toast.cs = 22)

        // ── State ─────────────────────────────────────────────────────────────
        private float   _progress;         // displayed (smoothly animated)
        private float   _targetProgress;   // actual download progress (jumps)
        private string  _titleText;
        private string  _statusText;
        private string  _detailText;
        private Color   _titleColor;
        private bool    _downloadComplete;
        private bool    _downloadFailed;
        private bool    _pendingComplete;  // download done, waiting for bar to fill
        private bool    _showRetry;
        private bool    _showClose;
        private bool    _showInstall;      // "Right Meow!" button
        private bool    _retryHover;
        private bool    _closeHover;
        private bool    _installHover;
        private Rectangle _retryRect, _closeRect;


        private string  _newVersion;
        private string  _installerPath;
        private WebClient _client;

        // Fade state
        private float   _opacity;
        private bool    _fadingIn  = true;
        private bool    _fadingOut = false;
        private Timer   _fadeTimer;
        private Timer   _repaintTimer;
        private int     _formH;   // computed height

        // Timer for auto-dismiss
        private DateTime _completeTime;
        private int      _holdMs = 15000; // 15 seconds to dismiss like long warning toasts
        private Timer    _holdTimer;

        // ── Constructor ───────────────────────────────────────────────────────
        private UpdateDialog(string newVersion)
        {
            _newVersion  = newVersion;
            _titleText   = "Updating to v" + newVersion;
            _statusText  = "Downloading...";
            _detailText  = "Preparing...";
            _titleColor  = ACC;

            // Fixed standardized height for all states so the form never resizes
            _formH = 122;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            TopMost         = true;
            StartPosition   = FormStartPosition.Manual;
            BackColor       = BG;
            DoubleBuffered  = true;
            AutoScaleMode   = AutoScaleMode.None;
            WinForms_SetStyle();
            Opacity         = 0;
            ClientSize      = Dpi.Size(W, _formH);

            DarkTheme.ApplyRoundedRegion(this, Dpi.S(CORNER));
            PositionBottomRight();

            // Repaint timer — also drives smooth progress bar lerp
            _repaintTimer = new Timer { Interval = 16 };
            _repaintTimer.Tick += (s, e) => {
                // Smooth lerp toward target (fills ~30 frames = ~0.5s for full bar)
                if (_progress < _targetProgress) {
                    _progress += Math.Max(0.008f, (_targetProgress - _progress) * 0.12f);
                    if (_targetProgress - _progress < 0.005f) _progress = _targetProgress;
                }
                // If download finished and bar caught up, fire completion
                if (_pendingComplete && _progress >= 0.995f) {
                    _pendingComplete = false;
                    _progress = 1f;
                    FireComplete();
                }
                Invalidate();
            };
            _repaintTimer.Start();

            // Fade-in timer
            _fadeTimer = new Timer { Interval = 10 };
            _fadeTimer.Tick += FadeTick;
            _fadingIn = true;
            _fadeTimer.Start();

            // Mouse for retry/close hit-testing
            MouseMove  += (s, e) => { CheckHover(e.Location); };
            MouseLeave += (s, e) => { _retryHover = _closeHover = false; Invalidate(); };
            MouseClick += (s, e) => { HandleClick(e.Location); };

            // Can't close during active download
            FormClosing += (s, e) => {
                if (!_downloadComplete && !_downloadFailed) e.Cancel = true;
            };

            // Start download immediately
            StartDownload(newVersion);
        }

        void WinForms_SetStyle()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
        }

        void PositionBottomRight()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                wa.Right  - ClientSize.Width  - Dpi.S(8),
                wa.Bottom - ClientSize.Height - Dpi.S(8));
        }

        // ── Fade logic ───────────────────────────────────────────────────────
        void FadeTick(object s, EventArgs e)
        {
            if (_fadingIn) {
                _opacity = Math.Min(_opacity + 0.08f, 0.95f);
                Opacity  = _opacity;
                if (_opacity >= 0.95f) { _fadingIn = false; _fadeTimer.Stop(); }
            } else if (_fadingOut) {
                _opacity = Math.Max(_opacity - 0.08f, 0f);
                Opacity  = _opacity;
                if (_opacity <= 0f) { _fadeTimer.Stop(); try { Close(); } catch {} }
            }
        }

        void StartFadeOut()
        {
            _fadingOut = true;
            _fadingIn  = false;
            _fadeTimer.Interval = 10;
            _fadeTimer.Start();
        }

        // ── Painting ─────────────────────────────────────────────────────────
        protected override void OnPaint(PaintEventArgs ev)
        {
            var g = ev.Graphics;
            g.SmoothingMode      = SmoothingMode.AntiAlias;
            g.TextRenderingHint  = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;
            int r = Dpi.S(CORNER);
            Color acc = AccentNow;

            // ── Background ──────────────────────────────────────────────
            using (var path = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), r))
            using (var br = new SolidBrush(BG))
                g.FillPath(br, path);

            // ── Border chrome ───────────────────────────────────────────
            int sepY = _showInstall ? Dpi.S(56) : (h / 2 + 1);
            using (var path = DarkTheme.RoundedRect(new Rectangle(0, 0, w - 1, h - 1), r)) {
                using (var shineBr = new LinearGradientBrush(new Rectangle(0, 0, w, sepY),
                    Color.FromArgb(12, 255, 255, 255), Color.Transparent, 90f)) {
                    g.SetClip(path);
                    g.FillRectangle(shineBr, 0, 0, w, sepY);
                }
                using (var glowBr = new LinearGradientBrush(new Rectangle(0, 0, w, Dpi.S(4)),
                    Color.FromArgb(50, acc), Color.FromArgb(0, acc), 90f))
                    g.FillRectangle(glowBr, 0, 0, w, Dpi.S(4));
                g.ResetClip();
                using (var glowPen = new Pen(Color.FromArgb(25, acc), 3f))
                    g.DrawPath(glowPen, path);
                using (var pen = new Pen(Color.FromArgb(110, acc), 1.2f))
                    g.DrawPath(pen, path);
            }

            // ── Accent stripe ───────────────────────────────────────────
            using (var stripeBr = new LinearGradientBrush(new Rectangle(0, Dpi.S(6), Dpi.S(3), Dpi.S(30)),
                acc, Color.FromArgb(0, acc), 90f))
            using (var sp = new Pen(stripeBr, Dpi.S(2)))
                g.DrawLine(sp, Dpi.S(2), Dpi.S(8), Dpi.S(2), Dpi.S(38));

            // ── Mascot ──────────────────────────────────────────────────
            try { Mascot.DrawMascot(g, Dpi.S(PAD), Dpi.S(TITLE_Y), Dpi.S(MASCOT)); } catch {}

            // ── Title ───────────────────────────────────────────────────
            int tx = Dpi.S(TEXT_X);
            int tw = w - tx - Dpi.S(PAD);
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
                TextRenderer.DrawText(g, _titleText, f,
                    new Rectangle(tx, Dpi.S(TITLE_Y), tw, Dpi.S(20)),
                    _titleColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // ── Subtitle ────────────────────────────────────────────────
            int subBottom = Dpi.S(SUB_Y + 16);  // subtitle ends at Y=44
            using (var f = new Font("Segoe UI", 7.5f))
                TextRenderer.DrawText(g, _statusText, f,
                    new Rectangle(tx, Dpi.S(SUB_Y), tw, Dpi.S(16)),
                    TXT2, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            int pad = Dpi.S(PAD);

            // ═════════════════════════════════════════════════════════════
            //  DOWNLOAD / FAILED: progress bar + info text
            // ═════════════════════════════════════════════════════════════
            if (!_downloadComplete)
            {
                // Mathematically center the download content block between subBottom (44) and the bottom
                // The Right Meow button in the Complete state is at Y=68, H=22. Centroid = 79.
                // Our progress block is: bar(12) + gap(4) + info(14) = 30px height.
                // To share the exact centroid (79), barY must be 79 - 15 = 64.
                int barX = pad;
                int barW = w - pad * 2;
                int barH = Dpi.S(BAR_H);
                int barY = Dpi.S(64);

                PaintBar(g, barX, barY, barW, barH);

                int infoY = barY + barH + Dpi.S(4);
                string pctStr = _downloadFailed ? "" : ((int)(_progress * 100)) + "%";
                using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, pctStr, f,
                        new Rectangle(barX, infoY, barW / 2, Dpi.S(14)),
                        acc, TextFormatFlags.VerticalCenter);
                using (var f = new Font("Segoe UI", 6.5f))
                    TextRenderer.DrawText(g, _detailText, f,
                        new Rectangle(barX + barW / 2, infoY, barW / 2, Dpi.S(14)),
                        TXT3, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);

                // Failed-state buttons below info
                if (_showRetry || _showClose)
                {
                    int btnY  = infoY + Dpi.S(12);
                    int btnH2 = Dpi.S(BTN_H);
                    int half  = (barW - Dpi.S(6)) / 2;
                    if (_showRetry) {
                        _retryRect = new Rectangle(barX, btnY, half, btnH2);
                        PaintBtn(g, _retryRect, "Retry",
                            _retryHover ? Color.FromArgb(40, ACC.R, ACC.G, ACC.B) : BTN_BG,
                            _retryHover ? Color.White : ACC, ACC);
                    }
                    if (_showClose) {
                        _closeRect = new Rectangle(barX + half + Dpi.S(6), btnY, half, btnH2);
                        PaintBtn(g, _closeRect, "Dismiss",
                            _closeHover ? BTN_HOVER : BTN_BG,
                            _closeHover ? TXT : TXT2, BDR);
                    }
                }
            }

            // ═════════════════════════════════════════════════════════════
            //  COMPLETE: "Right Meow!" — vertically centered + click dismiss
            // ═════════════════════════════════════════════════════════════
            if (_showInstall)
            {
                int btnH = Dpi.S(BTN_H);
                
                // Perfect 12px gaps everywhere:
                // subBottom ends at 44
                // sepY is at 56 (12px gap)
                // btnY is at 68 (12px gap from sepY)
                // dismissY is at 102 (12px gap from btnBottom 90)
                int dismissH = Dpi.S(14);
                int dismissY = Dpi.S(102);
                int btnY = Dpi.S(68);
                
                _retryRect = new Rectangle(pad, btnY, w - pad * 2, btnH);

                // Darken button base color to make shimmer more visible
                Color btnCol = Color.FromArgb(ACC.R / 3, ACC.G / 3, ACC.B / 3);
                if (_installHover) btnCol = Color.FromArgb(ACC.R * 2 / 3, ACC.G * 2 / 3, ACC.B * 2 / 3);
                PaintBtn(g, _retryRect, "Right Meow!", btnCol, Color.White, btnCol);

                // Shimmer sweep - 800ms sweep, 2000ms cooldown (2800ms total cycle)
                int cycleMs = (int)((DateTime.UtcNow.Ticks / 10000) % 2800);
                if (cycleMs <= 800) {
                    float shim = cycleMs / 800f;
                    int bw = _retryRect.Width;
                    int bandW = bw / 2; // wider band for a smoother sweep
                    int cx = (int)(shim * (bw + bandW)) - bandW;
                    using (var clip = DarkTheme.RoundedRect(_retryRect, Dpi.S(4))) {
                        var oldC = g.Clip;
                        g.SetClip(clip, CombineMode.Intersect);
                        try {
                            var sRect = new Rectangle(_retryRect.X + cx, _retryRect.Y, bandW, btnH);
                            using (var lgb = new LinearGradientBrush(
                                new Point(sRect.Left, 0), new Point(sRect.Right, 0),
                                Color.Transparent, Color.Transparent)) {
                                var cb = new ColorBlend(3);
                                cb.Colors    = new[] { Color.FromArgb(0, 255,255,255), Color.FromArgb(60,255,255,255), Color.FromArgb(0,255,255,255) };
                                cb.Positions = new[] { 0f, 0.5f, 1f };
                                lgb.InterpolationColors = cb;
                                g.FillRectangle(lgb, sRect);
                            }
                        } catch {}
                        g.Clip = oldC;
                    }
                }

            }

            if (_downloadComplete || _downloadFailed)
            {
                // ── Timer bar + countdown (Match Toast.cs) ──
                double elapsed = (DateTime.UtcNow - _completeTime).TotalMilliseconds;
                double totalMs = _holdMs + 800; // account for fade-in + fade-out
                double pct = Math.Max(0, 1.0 - elapsed / totalMs);
                int barY = h - Dpi.S(16);
                int barH = Dpi.S(3);
                int barMaxW = w - Dpi.S(24);
                int barX = Dpi.S(12);

                // Track
                Color trackCol = Color.FromArgb(32, 32, 32);
                using (var trackPath = RoundedBar(barX, barY, barMaxW, barH))
                using (var trackBr = new SolidBrush(trackCol))
                    g.FillPath(trackBr, trackPath);
                // Active bar
                int barW = Math.Max(barH, (int)(barMaxW * pct));
                if (barW > barH) {
                    using (var barPath = RoundedBar(barX, barY, barW, barH)) {
                        using (var glwBr = new SolidBrush(Color.FromArgb(20, acc)))
                            g.FillPath(glwBr, RoundedBar(barX, barY - 1, barW, barH + 2));
                        using (var barGrad = new LinearGradientBrush(new Rectangle(barX, barY, barW, barH),
                            acc, Color.FromArgb(acc.R * 3 / 4, acc.G * 3 / 4, acc.B * 3 / 4), 0f))
                            g.FillPath(barGrad, barPath);
                        int dotR = barH + Dpi.S(2);
                        using (var dotBr = new SolidBrush(Color.FromArgb(180, acc)))
                            g.FillEllipse(dotBr, barX + barW - dotR / 2, barY - Dpi.S(1), dotR, dotR);
                    }
                }
                // Countdown text
                int secs = (int)Math.Ceiling(Math.Max(0, totalMs - elapsed) / 1000.0);
                string timerTxt = secs + "s \u2013 Click dismiss";
                using (var fTimer = new Font("Segoe UI", 6.8f))
                {
                    var tsz = TextRenderer.MeasureText(timerTxt, fTimer);
                    TextRenderer.DrawText(g, timerTxt, fTimer,
                        new Point((w - tsz.Width) / 2, barY - Dpi.S(13)),
                        Color.FromArgb(80, 80, 80), TextFormatFlags.NoPrefix);
                }
            }
        }

        void PaintBar(Graphics g, int x, int y, int w, int h)
        {
            Color acc = AccentNow;

            // Track — bright grey, unmissable on dark background
            using (var trackPath = RoundedBar(x, y, w, h))
            using (var br = new SolidBrush(Color.FromArgb(120, 120, 130)))
                g.FillPath(br, trackPath);

            // Fill — solid bright accent (no gradient darkening)
            int fill = Math.Max(h, (int)(w * _progress));
            if (_progress > 0.005f && fill > h) {
                using (var fillPath = RoundedBar(x, y, fill, h)) {
                    using (var br = new SolidBrush(acc))
                        g.FillPath(br, fillPath);

                    // Shimmer (clipped to fill) - 800ms
                    if (!_downloadComplete && !_downloadFailed) {
                        float shimmer = (float)((DateTime.UtcNow.Ticks / 10000) % 800) / 800f;
                        int bandW = Math.Max(fill / 4, Dpi.S(20));
                        int cx = (int)(shimmer * (fill + bandW)) - bandW / 2;
                        var sRect = new Rectangle(x + cx - bandW / 2, y, bandW, h);
                        var oldClip = g.Clip;
                        g.SetClip(fillPath, CombineMode.Intersect);
                        try {
                            using (var lgb = new LinearGradientBrush(
                                new Point(sRect.Left, y), new Point(sRect.Right, y),
                                Color.Transparent, Color.Transparent)) {
                                var cb = new ColorBlend(3);
                                cb.Colors    = new[] { Color.FromArgb(0, 255,255,255), Color.FromArgb(70,255,255,255), Color.FromArgb(0,255,255,255) };
                                cb.Positions = new[] { 0f, 0.5f, 1f };
                                lgb.InterpolationColors = cb;
                                g.FillRectangle(lgb, sRect);
                            }
                        } catch {}
                        g.Clip = oldClip;
                    }
                }
            }
        }

        void PaintBtn(Graphics g, Rectangle r, string text, Color fill, Color fore, Color bdr)
        {
            int rad = Dpi.S(4);
            using (var path = DarkTheme.RoundedRect(r, rad)) {
                using (var br = new SolidBrush(fill)) g.FillPath(br, path);
                using (var p  = new Pen(Color.FromArgb(60, 60, 60), 1f)) g.DrawPath(p, path);
            }
            using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, text, f, r, fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        Color AccentNow { get { return _downloadFailed ? AMBER : ACC; } }

        // ── Interaction ───────────────────────────────────────────────────────
        void CheckHover(Point pt)
        {
            bool nr = _showRetry && _retryRect.Contains(pt);
            bool nc = _showClose && _closeRect.Contains(pt);
            bool ni = _showInstall && _retryRect.Contains(pt);  // reuse _retryRect for install btn
            Cursor = (nr || nc || ni) ? Cursors.Hand : Cursors.Default;
            if (nr != _retryHover || nc != _closeHover || ni != _installHover) {
                _retryHover = nr; _closeHover = nc; _installHover = ni; Invalidate();
            }
        }

        void HandleClick(Point pt)
        {
            if (_showRetry && _retryRect.Contains(pt)) DoRetry();
            if (_showClose && _closeRect.Contains(pt)) StartFadeOut();
            if (_showInstall && _retryRect.Contains(pt)) LaunchInstaller();
            // Click anywhere on the complete popup to dismiss (if not clicking the button)
            if (_downloadComplete && !_retryRect.Contains(pt)) StartFadeOut();
        }

        void DoRetry()
        {
            _downloadFailed = false; _downloadComplete = false;
            _showRetry = false; _showClose = false; _showInstall = false;
            _progress = 0; _targetProgress = 0; _pendingComplete = false;
            
            if (_holdTimer != null) { _holdTimer.Stop(); }

            _titleText  = "Updating to v" + _newVersion;
            _titleColor = ACC;
            _statusText = "Downloading...";
            _detailText = "Preparing...";
            
            // Maintain fixed height while downloading, no timer visual
            _formH = 122;
            ClientSize = Dpi.Size(W, _formH);
            DarkTheme.ApplyRoundedRegion(this, Dpi.S(CORNER));
            PositionBottomRight();
            Invalidate();
            StartDownload(_newVersion);
        }

        // ── Download ──────────────────────────────────────────────────────────
        void StartDownload(string ver)
        {
            _installerPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "Angry_Audio_Setup_v" + ver + ".exe");

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    _client = new WebClient();
                    _client.Headers.Add("User-Agent", "AngryAudio/" + AppVersion.Version);

                    _client.DownloadProgressChanged += (s, e) =>
                    {
                        float pct = e.TotalBytesToReceive > 0
                            ? (float)e.BytesReceived / e.TotalBytesToReceive : 0f;
                        try {
                            BeginInvoke((Action)(() => {
                                _targetProgress = pct;  // bar will smoothly chase this
                                string recv = FormatBytes(e.BytesReceived);
                                _detailText = e.TotalBytesToReceive > 0
                                    ? recv + " / " + FormatBytes(e.TotalBytesToReceive)
                                    : recv;
                            }));
                        } catch {}
                    };

                    _client.DownloadFileCompleted += (s, e) =>
                    {
                        try {
                            BeginInvoke((Action)(() => {
                                if (e.Error != null) { OnFailed(e.Error.Message); return; }
                                if (e.Cancelled)     { OnFailed("Download cancelled."); return; }
                                OnComplete();
                            }));
                        } catch {}
                    };

                    _client.DownloadFileAsync(
                        new Uri("https://github.com/Gantera2k/Angry-Audio/releases/download/Angry/Angry_Audio_Setup.exe"),
                        _installerPath);
                }
                catch (Exception ex)
                {
                    Logger.Error("Update download failed.", ex);
                    try { BeginInvoke((Action)(() => OnFailed(ex.Message))); } catch {}
                }
            });
        }

        void OnComplete()
        {
            // Set target to 100% — the repaint timer will animate there
            _targetProgress = 1f;
            _pendingComplete = true;
            // If bar is already at 100% (very unlikely), fire immediately
            if (_progress >= 0.995f) { _pendingComplete = false; _progress = 1f; FireComplete(); }
        }

        void FireComplete()
        {
            _downloadComplete = true;
            _titleText  = "When is it your turn to make the rules?";
            _titleColor = ACC;
            _statusText = "Update ready...";
            _detailText = "";
            _showInstall = true;
            _completeTime = DateTime.UtcNow;
            
            if (_holdTimer != null) { _holdTimer.Stop(); _holdTimer.Dispose(); }
            _holdTimer = new Timer { Interval = _holdMs };
            _holdTimer.Tick += (s, e) => { _holdTimer.Stop(); StartFadeOut(); };
            _holdTimer.Start();

            // Height expanded to 138 to fit the bottom timer bar
            _formH = 138;
            ClientSize = Dpi.Size(W, _formH);
            DarkTheme.ApplyRoundedRegion(this, Dpi.S(CORNER));
            PositionBottomRight();
            Invalidate();
        }

        void OnFailed(string msg)
        {
            _downloadFailed = true;
            _titleText  = "\u26A0 Update Failed";
            _titleColor = AMBER;
            _statusText = msg.Length > 38 ? msg.Substring(0, 38) + "\u2026" : msg;
            _detailText = "";
            _showRetry  = true;
            _showClose  = true;
            _completeTime = DateTime.UtcNow;
            
            if (_holdTimer != null) { _holdTimer.Stop(); _holdTimer.Dispose(); }
            _holdTimer = new Timer { Interval = _holdMs };
            _holdTimer.Tick += (s, e) => { _holdTimer.Stop(); StartFadeOut(); };
            _holdTimer.Start();

            // Height expanded to 138 to fit the bottom timer bar
            _formH = 138;
            ClientSize = Dpi.Size(W, _formH);
            DarkTheme.ApplyRoundedRegion(this, Dpi.S(CORNER));
            PositionBottomRight();
            Invalidate();
        }

        void LaunchInstaller()
        {
            try {
                _statusText = "Requesting permission\u2026";
                Invalidate();

                // Fade out the popup first, then launch installer
                _fadingOut = true;
                _fadingIn  = false;
                _fadeTimer.Interval = 12;
                _fadeTimer.Tick -= FadeTick;  // remove old handler
                _fadeTimer.Tick += (s, e) => {
                    _opacity = Math.Max(_opacity - 0.08f, 0f);
                    Opacity  = _opacity;
                    if (_opacity <= 0f) {
                        _fadeTimer.Stop();
                        try {
                            var psi = new System.Diagnostics.ProcessStartInfo {
                                FileName = _installerPath, Arguments = "/update", UseShellExecute = true
                            };
                            System.Diagnostics.Process.Start(psi);
                        } catch (Exception ex) {
                            Logger.Error("Failed to launch installer.", ex);
                        }
                        Application.Exit();
                    }
                };
                _fadeTimer.Start();
            } catch (Exception ex) {
                Logger.Error("Failed to launch installer.", ex);
                OnFailed("Failed to launch installer: " + ex.Message);
            }
        }

        // ── WinForms overrides ────────────────────────────────────────────────
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000008   // WS_EX_TOPMOST
                            | 0x00000080   // WS_EX_TOOLWINDOW
                            | 0x08000000;  // WS_EX_NOACTIVATE
                return cp;
            }
        }

        void ShowNoFocus()
        {
            if (!IsHandleCreated) CreateHandle();
            ShowWindow(Handle, 4); // SW_SHOWNOACTIVATE
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040);
        }

        protected override void Dispose(bool d)
        {
            if (d) {
                if (_fadeTimer     != null) _fadeTimer.Dispose();
                if (_repaintTimer  != null) _repaintTimer.Dispose();
                if (_holdTimer     != null) _holdTimer.Dispose();
                if (_client        != null) _client.Dispose();
            }
            base.Dispose(d);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        static GraphicsPath RoundedBar(int x, int y, int w, int h)
        {
            var path = new GraphicsPath();
            int d = h;
            if (w <= d) { path.AddEllipse(x, y, w, h); return path; }
            // Left semicircle: start at 90 (bottom), sweep 180 (clockwise to top)
            path.AddArc(x, y, d, h, 90, 180);
            // Right semicircle: start at 270 (top), sweep 180 (clockwise to bottom)
            path.AddArc(x + w - d, y, d, h, 270, 180);
            path.CloseFigure();
            return path;
        }

        static string FormatBytes(long b)
        {
            if (b >= 1048576) return (b / 1048576.0).ToString("F1") + " MB";
            if (b >= 1024)    return (b / 1024.0).ToString("F0")    + " KB";
            return b + " B";
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Show the sleek update download overlay.
        /// Non-modal: call from background download trigger.
        /// </summary>
        private static UpdateDialog _current;

        public static UpdateDialog ShowUpdate(string newVersion)
        {
            // Close any existing popup first (singleton)
            if (_current != null) {
                try { _current.Close(); _current.Dispose(); } catch {}
                _current = null;
            }
            var dlg = new UpdateDialog(newVersion);
            _current = dlg;
            dlg.FormClosed += (s, e) => { if (_current == dlg) _current = null; };
            dlg.ShowNoFocus();
            return dlg;
        }

        /// <summary>
        /// Background update check. Fires callback on main thread if update found (or always if notifyAlways=true).
        /// URL: https://raw.githubusercontent.com/Gantera2k/Angry-Audio/main/version.txt
        /// </summary>
        public static void CheckAsync(bool notifyAlways, Action<bool, string> onResult)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string latest = null;
                bool   hasUpdate = false;
                try {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    using (var wc = new WebClient()) {
                        wc.Headers.Add("User-Agent", "AngryAudio/" + AppVersion.Version);
                        string raw = wc.DownloadString(
                            "https://raw.githubusercontent.com/Gantera2k/Angry-Audio/main/version.txt");
                        latest    = raw.Trim().Trim('\uFEFF', '\u200B');
                        hasUpdate = CompareVersions(latest, AppVersion.Version) > 0;
                    }
                } catch { /* silent — no Toast on failure at startup */ }

                if (hasUpdate || notifyAlways)
                    try { Application.OpenForms[0].BeginInvoke(onResult, hasUpdate, latest); } catch {}
            });
        }

        private static int CompareVersions(string a, string b)
        {
            var pa = a.Split('.'); var pb = b.Split('.');
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++) {
                int va = i < pa.Length ? int.Parse(pa[i].Trim()) : 0;
                int vb = i < pb.Length ? int.Parse(pb[i].Trim()) : 0;
                if (va != vb) return va - vb;
            }
            return 0;
        }
    }
}
