// DarkMessage.cs — Dark-themed replacement for MessageBox.Show().
// Matches the app's visual style with proper colors and fonts.
//
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AngryAudio
{
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
}
