using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Windows.Forms;

namespace AngryAudio
{
    public class UpdateDialog : Form
    {
        static readonly Color BG = Color.FromArgb(12, 12, 12);
        static readonly Color BDR = Color.FromArgb(38, 38, 38);
        static readonly Color TXT = Color.FromArgb(220, 220, 220);
        static readonly Color TXT2 = Color.FromArgb(160, 160, 160);
        static readonly Color TXT3 = Color.FromArgb(100, 100, 100);
        static readonly Color ACC = Color.FromArgb(74, 158, 204);
        static readonly Color GREEN = DarkTheme.Green;
        static readonly Color BAR_BG = Color.FromArgb(28, 28, 28);
        static readonly Color BAR_BDR = Color.FromArgb(48, 48, 48);

        private Label _titleLabel;
        private Label _statusLabel;
        private Label _percentLabel;
        private Label _sizeLabel;
        private Panel _progressBar;
        private Panel _mascotPanel;
        private float _progress; // 0.0 to 1.0
        private Timer _shimmerTimer;
        private float _shimmerX = -0.3f;
        private bool _shimmerActive = true;
        private bool _downloadComplete;
        private bool _downloadFailed;
        private string _installerPath;
        private WebClient _client;

        public UpdateDialog(string newVersion)
        {
            Text = "Angry Audio Update";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BG; ForeColor = TXT;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f);
            ClientSize = Dpi.Size(420, 240);
            ShowInTaskbar = true;
            try { using (var bmp = new Bitmap(32, 32)) using (var g = Graphics.FromImage(bmp)) { Mascot.DrawMascot(g, 0, 0, 32); Icon = Icon.FromHandle(bmp.GetHicon()); } } catch { }
            DarkTheme.DarkTitleBar(Handle);

            // Mascot
            _mascotPanel = new Panel { Size = Dpi.Size(50, 50), Location = Dpi.Pt(185, 12), BackColor = Color.Transparent };
            _mascotPanel.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Mascot.DrawMascot(e.Graphics, 0, 0, Dpi.S(50));
            };
            Controls.Add(_mascotPanel);

            // Title
            _titleLabel = new Label {
                Text = "Updating to v" + newVersion,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = ACC, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Size = Dpi.Size(420, 28), Location = Dpi.Pt(0, 66)
            };
            Controls.Add(_titleLabel);

            // Status
            _statusLabel = new Label {
                Text = "Downloading update...",
                Font = new Font("Segoe UI", 9f),
                ForeColor = TXT2, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Size = Dpi.Size(420, 22), Location = Dpi.Pt(0, 96)
            };
            Controls.Add(_statusLabel);

            // Progress bar container
            _progressBar = new Panel {
                Size = Dpi.Size(340, 22),
                Location = Dpi.Pt(40, 130),
                BackColor = Color.Transparent
            };
            _progressBar.Paint += PaintProgressBar;
            Controls.Add(_progressBar);

            // Percentage label
            _percentLabel = new Label {
                Text = "0%",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = ACC, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Size = Dpi.Size(420, 20), Location = Dpi.Pt(0, 158)
            };
            Controls.Add(_percentLabel);

            // Size label
            _sizeLabel = new Label {
                Text = "",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = TXT3, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Size = Dpi.Size(420, 16), Location = Dpi.Pt(0, 178)
            };
            Controls.Add(_sizeLabel);

            // Shimmer timer
            _shimmerTimer = new Timer { Interval = 12 };
            _shimmerTimer.Tick += (s, e) => {
                if (!_shimmerActive) { _shimmerTimer.Stop(); return; }
                _shimmerX += 0.025f;
                if (_shimmerX > 1.3f) _shimmerX = -0.3f;
                _progressBar.Invalidate();
            };
            _shimmerTimer.Start();

            // Prevent closing during download
            FormClosing += (s, e) => {
                if (!_downloadComplete && !_downloadFailed)
                {
                    e.Cancel = true; // Can't close while downloading
                }
            };

            // Start download
            StartDownload(newVersion);
        }

        private void PaintProgressBar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _progressBar.Width, h = _progressBar.Height;
            int radius = h / 2;

            // Background track
            using (var path = RoundedRect(new Rectangle(0, 0, w - 1, h - 1), radius))
            {
                using (var brush = new SolidBrush(BAR_BG))
                    g.FillPath(brush, path);
                using (var pen = new Pen(BAR_BDR))
                    g.DrawPath(pen, path);
            }

            // Filled portion
            int fillW = Math.Max(radius * 2, (int)(w * _progress));
            if (_progress > 0.005f)
            {
                var fillRect = new Rectangle(0, 0, fillW - 1, h - 1);
                using (var path = RoundedRect(fillRect, radius))
                {
                    // Gradient fill
                    Color barColor = _downloadFailed ? Color.FromArgb(204, 74, 74) :
                                     _downloadComplete ? GREEN : ACC;
                    Color barLight = _downloadFailed ? Color.FromArgb(224, 100, 100) :
                                     _downloadComplete ? Color.FromArgb(120, 230, 120) : Color.FromArgb(110, 185, 225);
                    using (var brush = new LinearGradientBrush(
                        new Point(0, 0), new Point(0, h), barLight, barColor))
                        g.FillPath(brush, path);
                }
            }

            // Shimmer overlay on filled portion
            if (_shimmerActive && _progress > 0.02f)
            {
                int bandW = Math.Max(fillW / 3, 16);
                int cx = (int)(_shimmerX * (fillW + bandW)) - bandW / 2;
                if (cx > -bandW && cx < fillW + bandW)
                {
                    var shimmerRect = new Rectangle(cx - bandW / 2, 0, bandW, h);
                    // Clip to filled area
                    var clipPath = RoundedRect(new Rectangle(0, 0, fillW - 1, h - 1), radius);
                    var oldClip = g.Clip;
                    g.SetClip(clipPath, CombineMode.Intersect);
                    try
                    {
                        using (var lgb = new LinearGradientBrush(
                            new Point(shimmerRect.Left, 0), new Point(shimmerRect.Right, 0),
                            Color.Transparent, Color.Transparent))
                        {
                            var cb = new ColorBlend(3);
                            cb.Colors = new[] {
                                Color.FromArgb(0, 255, 255, 255),
                                Color.FromArgb(60, 255, 255, 255),
                                Color.FromArgb(0, 255, 255, 255)
                            };
                            cb.Positions = new[] { 0f, 0.5f, 1f };
                            lgb.InterpolationColors = cb;
                            g.FillRectangle(lgb, shimmerRect);
                        }
                    }
                    catch { }
                    g.Clip = oldClip;
                    clipPath.Dispose();
                }
            }
        }

        private void StartDownload(string newVersion)
        {
            _installerPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Angry_Audio_Setup_v" + newVersion + ".exe");

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
                            ? (float)e.BytesReceived / e.TotalBytesToReceive
                            : 0f;

                        try
                        {
                            BeginInvoke((Action)(() =>
                            {
                                _progress = pct;
                                _percentLabel.Text = ((int)(pct * 100)) + "%";

                                string received = FormatBytes(e.BytesReceived);
                                if (e.TotalBytesToReceive > 0)
                                    _sizeLabel.Text = received + " / " + FormatBytes(e.TotalBytesToReceive);
                                else
                                    _sizeLabel.Text = received;

                                _progressBar.Invalidate();
                            }));
                        }
                        catch { }
                    };

                    _client.DownloadFileCompleted += (s, e) =>
                    {
                        try
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (e.Error != null)
                                {
                                    OnDownloadFailed(e.Error.Message);
                                    return;
                                }
                                if (e.Cancelled)
                                {
                                    OnDownloadFailed("Download cancelled.");
                                    return;
                                }

                                OnDownloadComplete();
                            }));
                        }
                        catch { }
                    };

                    _client.DownloadFileAsync(
                        new Uri("https://github.com/Gantera2k/Angry-Audio/releases/download/Angry/Angry_Audio_Setup.exe"),
                        _installerPath);
                }
                catch (Exception ex)
                {
                    Logger.Error("Update download failed.", ex);
                    try { BeginInvoke((Action)(() => OnDownloadFailed(ex.Message))); } catch { }
                }
            });
        }

        private void OnDownloadComplete()
        {
            _downloadComplete = true;
            _progress = 1.0f;
            _shimmerActive = false;
            _shimmerTimer.Stop();
            _progressBar.Invalidate();

            _titleLabel.Text = "\u2714 Download Complete";
            _titleLabel.ForeColor = GREEN;
            _statusLabel.Text = "Installing update...";
            _percentLabel.Text = "100%";
            _percentLabel.ForeColor = GREEN;

            // Brief pause so user sees 100%, then launch
            var launchTimer = new Timer { Interval = 800 };
            launchTimer.Tick += (s, e) =>
            {
                launchTimer.Stop();
                launchTimer.Dispose();
                LaunchInstaller();
            };
            launchTimer.Start();
        }

        private void OnDownloadFailed(string message)
        {
            _downloadFailed = true;
            _shimmerActive = false;
            _shimmerTimer.Stop();
            _progressBar.Invalidate();

            _titleLabel.Text = "\u26A0 Update Failed";
            _titleLabel.ForeColor = Color.FromArgb(255, 180, 80);
            _statusLabel.Text = "Couldn't download the update.";
            _percentLabel.Text = "";
            _sizeLabel.Text = message.Length > 60 ? message.Substring(0, 60) + "..." : message;
            _sizeLabel.ForeColor = Color.FromArgb(255, 180, 80);

            // Add retry and close buttons
            int btnW = Dpi.S(110), btnH = Dpi.S(30);
            int btnY = Dpi.S(200);

            var retryBtn = new Panel { Size = new Size(btnW, btnH), Location = new Point(Dpi.S(100), btnY) };
            bool retryHover = false;
            retryBtn.Paint += (s2, e2) => {
                e2.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, retryBtn.Width - 1, retryBtn.Height - 1);
                Color fill = retryHover ? Color.FromArgb(94, 178, 224) : ACC;
                using (var brush = new SolidBrush(fill))
                using (var path = RoundedRect(rect, Dpi.S(6)))
                    e2.Graphics.FillPath(brush, path);
                using (var brush = new SolidBrush(Color.White))
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    e2.Graphics.DrawString("Retry", f, brush, new RectangleF(0, 0, retryBtn.Width, retryBtn.Height), sf);
                }
            };
            retryBtn.MouseEnter += (s2, e2) => { retryHover = true; retryBtn.Invalidate(); };
            retryBtn.MouseLeave += (s2, e2) => { retryHover = false; retryBtn.Invalidate(); };
            retryBtn.Click += (s2, e2) => {
                // Reset and retry
                _downloadFailed = false;
                _progress = 0;
                _shimmerActive = true;
                _shimmerX = -0.3f;
                _shimmerTimer.Start();
                _titleLabel.Text = "Updating...";
                _titleLabel.ForeColor = ACC;
                _statusLabel.Text = "Downloading update...";
                _percentLabel.Text = "0%";
                _percentLabel.ForeColor = ACC;
                _sizeLabel.Text = "";
                _sizeLabel.ForeColor = TXT3;
                retryBtn.Parent.Controls.Remove(retryBtn);
                // Remove close button too
                for (int i = Controls.Count - 1; i >= 0; i--)
                    if (Controls[i].Tag != null && Controls[i].Tag.ToString() == "failBtn") Controls.RemoveAt(i);
                StartDownload(_pendingVersion);
            };
            Controls.Add(retryBtn);

            var closeBtn = new Panel { Size = new Size(btnW, btnH), Location = new Point(Dpi.S(220), btnY), Tag = "failBtn" };
            retryBtn.Tag = "failBtn";
            bool closeHover = false;
            closeBtn.Paint += (s2, e2) => {
                e2.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, closeBtn.Width - 1, closeBtn.Height - 1);
                Color fill = closeHover ? Color.FromArgb(48, 48, 48) : Color.FromArgb(32, 32, 32);
                using (var brush = new SolidBrush(fill))
                using (var path = RoundedRect(rect, Dpi.S(6)))
                    e2.Graphics.FillPath(brush, path);
                using (var pen = new Pen(BDR))
                using (var path = RoundedRect(rect, Dpi.S(6)))
                    e2.Graphics.DrawPath(pen, path);
                using (var brush = new SolidBrush(TXT2))
                using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    e2.Graphics.DrawString("Close", f, brush, new RectangleF(0, 0, closeBtn.Width, closeBtn.Height), sf);
                }
            };
            closeBtn.MouseEnter += (s2, e2) => { closeHover = true; closeBtn.Invalidate(); };
            closeBtn.MouseLeave += (s2, e2) => { closeHover = false; closeBtn.Invalidate(); };
            closeBtn.Click += (s2, e2) => { _downloadFailed = true; Close(); };
            Controls.Add(closeBtn);

            ClientSize = Dpi.Size(420, 245);
        }

        private string _pendingVersion;

        private void LaunchInstaller()
        {
            try
            {
                _statusLabel.Text = "Launching installer...";

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _installerPath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);

                // Exit the app — installer handles the rest
                Application.Exit();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to launch installer.", ex);
                OnDownloadFailed("Failed to launch installer: " + ex.Message);
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1048576) return (bytes / 1048576.0).ToString("F1") + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("F0") + " KB";
            return bytes + " B";
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BG);
            using (var pen = new Pen(BDR))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        public static void ShowUpdate(string newVersion)
        {
            var dlg = new UpdateDialog(newVersion);
            dlg._pendingVersion = newVersion;
            dlg.ShowDialog();
        }
    }
}
