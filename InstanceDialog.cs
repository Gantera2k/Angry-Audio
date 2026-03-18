using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AngryAudio
{
    public class InstanceDialog : Form
    {
        public bool UserChoseRestart { get; private set; }

        static readonly Color BG = Color.FromArgb(12, 12, 12);
        static readonly Color BDR = Color.FromArgb(38, 38, 38);
        static readonly Color TXT = Color.FromArgb(220, 220, 220);
        static readonly Color TXT2 = Color.FromArgb(160, 160, 160);
        static readonly Color ACC = Color.FromArgb(74, 158, 204);
        static readonly Color ErrorRed = Color.FromArgb(204, 74, 74);
        static readonly Color BTN_BG = Color.FromArgb(32, 32, 32);
        static readonly Color BTN_HOVER = Color.FromArgb(48, 48, 48);

        public InstanceDialog() : this(null, true) { }

        public InstanceDialog(string customMessage, bool showRestart)
        {
            Text = AppVersion.FullName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BG; ForeColor = TXT;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f);
            ClientSize = Dpi.Size(380, 200);
            try { using (var bmp = new Bitmap(32, 32)) using (var g = Graphics.FromImage(bmp)) { Mascot.DrawMascot(g, 0, 0, 32); Icon = Icon.FromHandle(bmp.GetHicon()); } } catch { }
            DarkTheme.DarkTitleBar(Handle);

            // Mascot
            var mascotPanel = new Panel { Size = Dpi.Size(60, 60), Location = Dpi.Pt(160, 16), BackColor = Color.Transparent };
            mascotPanel.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Mascot.DrawMascot(e.Graphics, 0, 0, Dpi.S(60));
            };
            Controls.Add(mascotPanel);

            // Title
            var title = new Label {
                Text = showRestart ? "Already Running" : "Couldn't Restart",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = showRestart ? ACC : ErrorRed, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Size = Dpi.Size(380, 28), Location = Dpi.Pt(0, 80)
            };
            Controls.Add(title);

            // Message
            var msg = new Label {
                Text = customMessage ?? "Angry Audio is already running in the system tray\nWould you like to restart it?",
                Font = new Font("Segoe UI", 9f),
                ForeColor = TXT2, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Size = Dpi.Size(340, 40), Location = Dpi.Pt(20, 110)
            };
            Controls.Add(msg);

            // Buttons
            int btnW = Dpi.S(120), btnH = Dpi.S(32);
            int btnY = Dpi.S(158);

            if (showRestart)
            {
                var btnRestart = MakeButton("Restart", ACC, Color.White, Dpi.S(80), btnY, btnW, btnH);
                btnRestart.Click += (s, e) => { UserChoseRestart = true; Close(); };
                Controls.Add(btnRestart);

                var btnCancel = MakeButton("Cancel", BTN_BG, TXT2, Dpi.S(380 - 80 - 120), btnY, btnW, btnH);
                btnCancel.Click += (s, e) => { UserChoseRestart = false; Close(); };
                Controls.Add(btnCancel);
            }
            else
            {
                var btnOk = MakeButton("OK", ACC, Color.White, Dpi.S(130), btnY, btnW, btnH);
                btnOk.Click += (s, e) => { UserChoseRestart = false; Close(); };
                Controls.Add(btnOk);
            }
        }

        private Panel MakeButton(string text, Color bg, Color fg, int x, int y, int w, int h)
        {
            var btn = new Panel { Location = new Point(x, y), Size = new Size(w, h) };
            bool hovering = false;

            btn.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
                Color fill = hovering ? (bg == ACC ? Color.FromArgb(94, 178, 224) : BTN_HOVER) : bg;
                using (var brush = new SolidBrush(fill))
                using (var path = RoundedRect(rect, Dpi.S(6)))
                    g.FillPath(brush, path);
                using (var pen = new Pen(bg == ACC ? ACC : BDR))
                using (var path = RoundedRect(rect, Dpi.S(6)))
                    g.DrawPath(pen, path);
                using (var brush = new SolidBrush(fg))
                using (var f = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(text, f, brush, new RectangleF(0, 0, btn.Width, btn.Height), sf);
                }
            };

            btn.MouseEnter += (s, e) => { hovering = true; btn.Invalidate(); };
            btn.MouseLeave += (s, e) => { hovering = false; btn.Invalidate(); };

            return btn;
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
            // Subtle border
            using (var pen = new Pen(BDR))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }
}
