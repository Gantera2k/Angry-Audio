// Controls.cs — Shared reusable UI controls used across multiple forms.
// Contains: ToggleSwitch, SlickSlider, BufferedPanel, ScrollPanel,
//           PaddedNumericUpDown, SplashForm, NudDefocusFilter.
//
// These controls are used by OptionsForm, WelcomeForm, and Installer.
// If you need to change how a toggle looks, change it HERE — one place.
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AngryAudio
{
    public class ToggleSwitch : Control
    {
        private bool _checked;
        private bool _hovering;
        public event EventHandler CheckedChanged;
        public bool Checked {
            get { return _checked; }
            set { _checked = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }
        public ToggleSwitch() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            Size = Dpi.Size(40, 20);
            UpdateRegion();
        }
        /// <summary>Delegate to paint parent card background for true transparency on starfield cards.</summary>
        public Action<Graphics, Control> PaintParentBg;


        void UpdateRegion() {
            int w = Width, h = Height;
            var path = new GraphicsPath();
            if (h > 0 && w > 0) {
                path.AddArc(0, 0, h, h, 90, 180);
                path.AddArc(w - h, 0, h, h, 270, 180);
                path.CloseFigure();
            }
            Region = new Region(path);
            path.Dispose();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateRegion(); }
        protected override void OnMouseEnter(EventArgs e) { _hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovering = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (PaintParentBg != null) PaintParentBg(g, this);
            else { using (var b = new SolidBrush(DarkTheme.GlassFlat)) g.FillRectangle(b, ClientRectangle); }
            int w = Width, h = Height;
            Color bg = _checked
                ? (_hovering ? Color.FromArgb(130, 210, 255) : DarkTheme.Accent)
                : (_hovering ? Color.FromArgb(90, 90, 90) : Color.FromArgb(55, 55, 55));
            using (var b = new SolidBrush(bg)) {
                g.FillEllipse(b, 0, 0, h, h); g.FillEllipse(b, w - h, 0, h, h);
                g.FillRectangle(b, h / 2f, 0, w - h, h);
            }
            // Hover glow ring
            if (_hovering) {
                Color glowC = _checked ? DarkTheme.Accent : Color.FromArgb(120, 120, 120);
                using (var p = new Pen(Color.FromArgb(50, glowC.R, glowC.G, glowC.B), 2f)) {
                    g.DrawEllipse(p, -1, -1, h + 2, h + 2);
                    g.DrawEllipse(p, w - h - 1, -1, h + 2, h + 2);
                }
            }
            float kd = h - Dpi.S(4), kx = _checked ? w - kd - Dpi.S(2) : Dpi.S(2);
            using (var b = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                g.FillEllipse(b, kx + 1, Dpi.S(3), kd, kd);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, Dpi.S(2), kd, kd);
        }
        protected override void OnClick(EventArgs e) {
            _checked = !_checked; Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            base.OnClick(e);
        }
    }

    /// <summary>
    /// Premium custom slider — pill-shaped track, circle thumb, accent fill, hover glow.
    /// Drop-in replacement for TrackBar. Uses Value/Minimum/Maximum/ValueChanged.
    /// </summary>
    public class SlickSlider : Control
    {
        private int _min, _max = 100, _val = 50;
        private bool _dragging, _hovering;
        public event EventHandler ValueChanged;
        /// <summary>Fires once when the user releases the slider thumb — use for heavy operations like COM enforcement.</summary>
        public event EventHandler DragCompleted;
        /// <summary>Fire DragCompleted from external card mouse handler.</summary>
        public void FireDragCompleted() { if (DragCompleted != null) DragCompleted(this, EventArgs.Empty); }
        public int Minimum { get { return _min; } set { _min = value; InvalidateWithParent(); } }
        public int Maximum { get { return _max; } set { _max = value; InvalidateWithParent(); } }
        public int Value {
            get { return _val; }
            set { int nv = Math.Max(_min, Math.Min(_max, value)); if (nv != _val) { _val = nv; InvalidateWithParent(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); } }
        }

        static readonly Color TRACK_BG = Color.FromArgb(36, 36, 36);
        static readonly Color FILL_CLR = DarkTheme.Accent;
        static readonly Color THUMB_CLR = Color.White;

        public SlickSlider() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            Size = Dpi.Size(200, 30);
            BackColor = DarkTheme.GlassFlat;
        }

        // Delegate to paint parent card background for seamless transparency
        public Action<Graphics, Control> PaintParentBg;


        void InvalidateWithParent() {
            Invalidate();
            if (Parent != null) Parent.Invalidate(Bounds, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e) {
            // Intentionally empty — all painting in OnPaint to prevent seam artifacts
        }

        float Pct { get { return _max > _min ? (float)(_val - _min) / (_max - _min) : 0f; } }
        int ThumbR { get { return Dpi.S(7); } }
        int TrackH { get { return Dpi.S(4); } }
        int TrackY { get { return Height / 2; } }
        int TrackLeft { get { return ThumbR + 2; } }
        int TrackRight { get { return Width - ThumbR - 2; } }
        int ThumbX { get { return TrackLeft + (int)((TrackRight - TrackLeft) * Pct); } }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            // Fill background with own BackColor — set to GlassFlat for card sliders, row bg for app sliders
            using (var b = new SolidBrush(BackColor))
                g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Track + thumb
            int tl = TrackLeft, tr = TrackRight, ty = TrackY, th = TrackH, thr = ThumbR;
            int tx = ThumbX;
            float dim = Enabled ? 1.0f : 0.35f; // Dim when disabled (unlocked app)

            // Track background (pill shape)
            using (var b = new SolidBrush(Color.FromArgb((int)(TRACK_BG.A * dim), TRACK_BG.R, TRACK_BG.G, TRACK_BG.B)))
                FillPill(g, b, tl, ty - th/2, tr - tl, th);

            // Filled portion
            if (tx > tl + 2) {
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * dim), FILL_CLR.R, FILL_CLR.G, FILL_CLR.B)))
                    FillPill(g, b, tl, ty - th/2, tx - tl, th);
            }

            // Thumb shadow
            if (Enabled) {
                using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    g.FillEllipse(b, tx - thr + 1, ty - thr + 1, thr * 2, thr * 2);
            }

            // Thumb hover glow — constrained to control height
            if (Enabled && (_hovering || _dragging)) {
                int maxGlow = Height / 2 - 1; // Stay within control bounds
                int glowR = Math.Min(thr + Dpi.S(4), maxGlow);
                using (var b = new SolidBrush(Color.FromArgb(60, DarkTheme.Accent.R, DarkTheme.Accent.G, DarkTheme.Accent.B)))
                    g.FillEllipse(b, tx - glowR, ty - glowR, glowR * 2, glowR * 2);
            }

            // Thumb
            int thumbAlpha = Enabled ? 255 : 90;
            using (var b = new SolidBrush(Color.FromArgb(thumbAlpha, THUMB_CLR.R, THUMB_CLR.G, THUMB_CLR.B)))
                g.FillEllipse(b, tx - thr, ty - thr, thr * 2, thr * 2);

            // Thumb accent dot
            if (Enabled) {
                int dotR = Dpi.S(2);
                using (var b = new SolidBrush(FILL_CLR))
                    g.FillEllipse(b, tx - dotR, ty - dotR, dotR * 2, dotR * 2);
            }
        }

        void FillPill(Graphics g, Brush b, int x, int y, int w, int h) {
            if (w < h) w = h;
            if (w <= 0 || h <= 0) return;
            var path = new GraphicsPath();
            path.AddArc(x, y, h, h, 90, 180);
            path.AddArc(x + w - h, y, h, h, 270, 180);
            path.CloseFigure();
            g.FillPath(b, path);
            path.Dispose();
        }

        int XToValue(int x) {
            float pct = (float)(x - TrackLeft) / Math.Max(1, TrackRight - TrackLeft);
            pct = Math.Max(0f, Math.Min(1f, pct));
            return _min + (int)(pct * (_max - _min));
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) { _dragging = true; Value = XToValue(e.X); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            if (_dragging) Value = XToValue(e.X);
            bool wasHover = _hovering;
            int dx = e.X - ThumbX, dy = e.Y - TrackY;
            _hovering = dx * dx + dy * dy <= (ThumbR + Dpi.S(4)) * (ThumbR + Dpi.S(4));
            if (_hovering != wasHover) Invalidate();
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { bool wasDragging = _dragging; _dragging = false; Invalidate(); if (wasDragging && DragCompleted != null) DragCompleted(this, EventArgs.Empty); base.OnMouseUp(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovering = false; Invalidate(); base.OnMouseLeave(e); }
    }

    /// <summary>
    /// <summary>Panel that scrolls content via mouse wheel without showing a scrollbar.</summary>
    class ScrollPanel : Panel
    {
        private int _scrollY;
        private int _maxScroll;

        public ScrollPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AutoScroll = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Compute true content height from original (unscrolled) positions
            int contentH = 0;
            if (_originalTops != null && _originalTops.Length == Controls.Count) {
                for (int i = 0; i < Controls.Count; i++) {
                    int bottom = _originalTops[i] + Controls[i].Height;
                    if (bottom > contentH) contentH = bottom;
                }
            } else {
                foreach (Control c in Controls) {
                    int bottom = c.Top + c.Height;
                    if (bottom > contentH) contentH = bottom;
                }
            }
            _maxScroll = Math.Max(0, contentH - ClientSize.Height);
            if (_maxScroll == 0) { _scrollY = 0; return; }
            _scrollY = Math.Max(0, Math.Min(_maxScroll, _scrollY - e.Delta));
            UpdateChildPositions();
            base.OnMouseWheel(e);
        }

        private int[] _originalTops;
        
        public void ResetScroll()
        {
            _scrollY = 0;
            _originalTops = null;
        }

        private void UpdateChildPositions()
        {
            if (_originalTops == null || _originalTops.Length != Controls.Count) {
                _originalTops = new int[Controls.Count];
                for (int i = 0; i < Controls.Count; i++)
                    _originalTops[i] = Controls[i].Top + _scrollY; // store absolute position
            }
            SuspendLayout();
            for (int i = 0; i < Controls.Count; i++)
                Controls[i].Top = _originalTops[i] - _scrollY;
            ResumeLayout();
        }
    }

    /// Panel with DoubleBuffered enabled to prevent flicker during animation repaints.
    /// Set TransparentBackground = true to let parent content show through.
    /// </summary>
    public class BufferedPanel : Panel
    {
        public bool TransparentBackground { get; set; }
        public BufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }
    }

    /// <summary>CheckBox that draws ONLY its icon with no background. Parent content shows through because
    /// we skip double-buffering (no private back buffer) and skip all background painting.</summary>
    public class TransparentCheckBox : CheckBox
    {
        public Action<Graphics, Control> PaintParentBg;
        /// <summary>Use this instead of Paint += to render the icon.</summary>
        public event PaintEventHandler CardPaint;
        public TransparentCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, false);
            BackColor = Color.Transparent;
            Appearance = Appearance.Button;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.CheckedBackColor = Color.Transparent;
            FlatAppearance.MouseDownBackColor = Color.Transparent;
            FlatAppearance.MouseOverBackColor = Color.Transparent;
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Skip — let parent content show through
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            // Skip PaintParentBg — parent already painted the background including flash/effects
            // Just draw the icon
            if (CardPaint != null) CardPaint(this, e);
        }
    }

    /// <summary>Lightweight icon button painted directly on a card — no child control, no compositing issues.
    /// The card's Paint handler draws it, the card's mouse events handle clicks and hover.</summary>
    public class CardIcon
    {
        public int W, H; // unscaled size
        public bool Checked;
        public bool Hover;
        public bool IsEye; // true = eye icon, false = speaker icon
        public bool IsLock; // true = lock icon (Checked=locked, unchecked=unlocked)
        public Action<bool> OnChange;
        public Panel Card;
        // Pixel position (already DPI-scaled)
        public int PixelX, PixelY;

        public Rectangle Bounds { get { return new Rectangle(PixelX, PixelY, Dpi.S(W), Dpi.S(H)); } }

        /// <summary>Set position from unscaled design coords (same as Dpi.Pt)</summary>
        public void SetPos(int x, int y) { PixelX = Dpi.S(x); PixelY = Dpi.S(y); }

        public bool HitTest(Point p) { return Bounds.Contains(p); }

        public void Toggle() { Checked = !Checked; if (OnChange != null) OnChange(Checked); if (Card != null) Card.Invalidate(); }

        public void Paint(Graphics g, Color accent) {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var b = Bounds;
            float cx = b.X + b.Width / 2f, cy = b.Y + b.Height / 2f;
            Color col = Checked ? (Hover ? Color.FromArgb(140, 220, 255) : accent) : (Hover ? Color.FromArgb(90, 90, 90) : Color.FromArgb(55, 55, 55));
            float sc = Dpi.S(1);
            if (IsLock) PaintLock(g, cx, cy, Checked, col, sc);
            else if (IsEye) PaintEye(g, cx, cy, Checked, col, sc);
            else PaintSpeaker(g, cx, cy, Checked, col, sc);
        }

        static void PaintLock(Graphics g, float cx, float cy, bool locked, Color col, float sc) {
            // Body: rounded rectangle
            float bw = 8 * sc, bh = 6 * sc;
            float bx = cx - bw / 2, by = cy - 1 * sc;
            using (var br = new SolidBrush(col)) {
                g.FillRectangle(br, bx, by, bw, bh);
                // Rounded top corners
                g.FillEllipse(br, bx, by - 0.5f * sc, 2 * sc, 2 * sc);
                g.FillEllipse(br, bx + bw - 2 * sc, by - 0.5f * sc, 2 * sc, 2 * sc);
            }
            // Shackle: arc above body
            float shW = 5 * sc, shH = 5 * sc;
            float shX = cx - shW / 2, shY = by - shH + 1 * sc;
            using (var p = new Pen(col, 1.5f * sc)) {
                p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                if (locked) {
                    // Closed shackle — full arc
                    g.DrawArc(p, shX, shY, shW, shH, 180, 180);
                    g.DrawLine(p, shX, shY + shH / 2, shX, by);
                    g.DrawLine(p, shX + shW, shY + shH / 2, shX + shW, by);
                } else {
                    // Open shackle — arc + right leg lifted
                    g.DrawArc(p, shX, shY - 2 * sc, shW, shH, 180, 180);
                    g.DrawLine(p, shX, shY - 2 * sc + shH / 2, shX, by);
                }
            }
            // Keyhole dot
            float kr = 1 * sc;
            using (var br = new SolidBrush(Color.FromArgb(20, 20, 20)))
                g.FillEllipse(br, cx - kr, by + bh * 0.3f, kr * 2, kr * 2);
        }

        static void PaintEye(Graphics g, float cx, float cy, bool on, Color col, float sc) {
            float ew = 7 * sc;
            using (var path = new System.Drawing.Drawing2D.GraphicsPath()) {
                path.AddBezier(cx - ew, cy, cx - ew * 0.4f, cy - 6 * sc, cx + ew * 0.4f, cy - 6 * sc, cx + ew, cy);
                path.AddBezier(cx + ew, cy, cx + ew * 0.4f, cy + 6 * sc, cx - ew * 0.4f, cy + 6 * sc, cx - ew, cy);
                using (var p = new Pen(col, 1.5f * sc)) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawPath(p, path); }
            }
            if (on) { float pr = 2.5f * sc; using (var b = new SolidBrush(col)) g.FillEllipse(b, cx - pr, cy - pr, pr * 2, pr * 2); }
            else { using (var p = new Pen(col, 1.5f * sc)) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawLine(p, cx - 6 * sc, cy + 6 * sc, cx + 6 * sc, cy - 6 * sc); } }
        }

        static void PaintSpeaker(Graphics g, float cx, float cy, bool on, Color col, float sc) {
            float bx = cx - 7 * sc;
            using (var b = new SolidBrush(col)) g.FillRectangle(b, bx, cy - 2 * sc, 2.5f * sc, 4 * sc);
            var cone = new PointF[] { new PointF(bx + 2 * sc, cy - 2 * sc), new PointF(bx + 5.5f * sc, cy - 4.5f * sc), new PointF(bx + 5.5f * sc, cy + 4.5f * sc), new PointF(bx + 2 * sc, cy + 2 * sc) };
            using (var b = new SolidBrush(col)) g.FillPolygon(b, cone);
            if (on) {
                using (var p = new Pen(Color.FromArgb(200, col.R, col.G, col.B), 1.2f * sc)) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawArc(p, bx + 6.5f * sc, cy - 2.5f * sc, 3 * sc, 5 * sc, -70, 140); }
                using (var p = new Pen(Color.FromArgb(160, col.R, col.G, col.B), 1.1f * sc)) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawArc(p, bx + 8.5f * sc, cy - 4 * sc, 4 * sc, 8 * sc, -70, 140); }
                using (var p = new Pen(Color.FromArgb(110, col.R, col.G, col.B), 1f * sc)) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawArc(p, bx + 10.5f * sc, cy - 5.5f * sc, 5 * sc, 11 * sc, -70, 140); }
            } else {
                using (var p = new Pen(col, 1.5f * sc)) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawLine(p, cx - 6 * sc, cy + 6 * sc, cx + 6 * sc, cy - 6 * sc); }
            }
        }
    }

    /// <summary>Visual toggle painted in card's Paint handler. Linked to a real (hidden) ToggleSwitch for state and events.</summary>
    public class CardToggle
    {
        public int PixelX, PixelY;
        public ToggleSwitch Source; // hidden control that holds state + events
        public bool Hover;
        public Panel Card;

        public Rectangle Bounds { get { return new Rectangle(PixelX, PixelY, Dpi.S(40), Dpi.S(20)); } }
        public bool HitTest(Point p) { return Bounds.Contains(p); }
        public void Click() { if (Source != null) Source.Checked = !Source.Checked; if (Card != null) Card.Invalidate(); }

        public void Paint(Graphics g, Color accent) {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = Bounds;
            int w = r.Width, h = r.Height;
            bool on = Source != null && Source.Checked;
            Color bg = on ? (Hover ? Color.FromArgb(130, 210, 255) : accent) : (Hover ? Color.FromArgb(90, 90, 90) : Color.FromArgb(55, 55, 55));
            using (var b = new SolidBrush(bg)) {
                g.FillEllipse(b, r.X, r.Y, h, h);
                g.FillEllipse(b, r.X + w - h, r.Y, h, h);
                g.FillRectangle(b, r.X + h / 2f, r.Y, w - h, h);
            }
            if (Hover) {
                Color glowC = on ? accent : Color.FromArgb(120, 120, 120);
                using (var p = new Pen(Color.FromArgb(50, glowC.R, glowC.G, glowC.B), 2f)) {
                    g.DrawEllipse(p, r.X - 1, r.Y - 1, h + 2, h + 2);
                    g.DrawEllipse(p, r.X + w - h - 1, r.Y - 1, h + 2, h + 2);
                }
            }
            float kd = h - Dpi.S(4), kx = on ? r.X + w - kd - Dpi.S(2) : r.X + Dpi.S(2);
            using (var b = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                g.FillEllipse(b, kx + 1, r.Y + Dpi.S(3), kd, kd);
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, kx, r.Y + Dpi.S(2), kd, kd);
        }
    }

    /// <summary>Slider painted in card's Paint handler. Linked to a real (hidden) SlickSlider for state and events.
    /// Card mouse handlers manage drag/hover — no child control, no stale background during animations.</summary>
    public class CardSlider
    {
        public int PixelX, PixelY, PixelW, PixelH;
        public SlickSlider Source; // hidden control that holds state + events
        public bool Hover;
        public bool Dragging;
        public Panel Card;

        static readonly Color TRACK_BG = Color.FromArgb(36, 36, 36);
        static readonly Color FILL_CLR = DarkTheme.Accent;
        static readonly Color THUMB_CLR = Color.White;

        public Rectangle Bounds { get { return new Rectangle(PixelX, PixelY, PixelW, PixelH); } }
        public bool HitTest(Point p) { return Bounds.Contains(p); }

        int ThumbR { get { return Dpi.S(7); } }
        int TrackH { get { return Dpi.S(4); } }
        int TrackY { get { return PixelY + PixelH / 2; } }
        int TrackLeft { get { return PixelX + ThumbR + 2; } }
        int TrackRight { get { return PixelX + PixelW - ThumbR - 2; } }
        float Pct { get { return Source != null && Source.Maximum > Source.Minimum ? (float)(Source.Value - Source.Minimum) / (Source.Maximum - Source.Minimum) : 0f; } }
        int ThumbX { get { return TrackLeft + (int)((TrackRight - TrackLeft) * Pct); } }

        public int XToValue(int x) {
            float pct = (float)(x - TrackLeft) / Math.Max(1, TrackRight - TrackLeft);
            pct = Math.Max(0f, Math.Min(1f, pct));
            return Source.Minimum + (int)(pct * (Source.Maximum - Source.Minimum));
        }

        /// <summary>Returns true if point is near the thumb (for hover detection).</summary>
        public bool ThumbHitTest(Point p) {
            int dx = p.X - ThumbX, dy = p.Y - TrackY;
            return dx * dx + dy * dy <= (ThumbR + Dpi.S(4)) * (ThumbR + Dpi.S(4));
        }

        public void Paint(Graphics g, Color accent) {
            if (Source == null) return;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int tl = TrackLeft, tr = TrackRight, ty = TrackY, th = TrackH, thr = ThumbR;
            int tx = ThumbX;
            bool enabled = Source.Enabled;
            float dim = enabled ? 1.0f : 0.35f;

            // Track background (pill shape)
            using (var b = new SolidBrush(Color.FromArgb((int)(TRACK_BG.A * dim), TRACK_BG.R, TRACK_BG.G, TRACK_BG.B)))
                FillPill(g, b, tl, ty - th/2, tr - tl, th);

            // Filled portion
            if (tx > tl + 2) {
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * dim), FILL_CLR.R, FILL_CLR.G, FILL_CLR.B)))
                    FillPill(g, b, tl, ty - th/2, tx - tl, th);
            }

            // Thumb shadow
            if (enabled) {
                using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    g.FillEllipse(b, tx - thr + 1, ty - thr + 1, thr * 2, thr * 2);
            }

            // Thumb hover glow
            if (enabled && (Hover || Dragging)) {
                int maxGlow = PixelH / 2 - 1;
                int glowR = Math.Min(thr + Dpi.S(4), maxGlow);
                using (var b = new SolidBrush(Color.FromArgb(60, accent.R, accent.G, accent.B)))
                    g.FillEllipse(b, tx - glowR, ty - glowR, glowR * 2, glowR * 2);
            }

            // Thumb
            int thumbAlpha = enabled ? 255 : 90;
            using (var b = new SolidBrush(Color.FromArgb(thumbAlpha, THUMB_CLR.R, THUMB_CLR.G, THUMB_CLR.B)))
                g.FillEllipse(b, tx - thr, ty - thr, thr * 2, thr * 2);

            // Thumb accent dot
            if (enabled) {
                int dotR = Dpi.S(2);
                using (var b = new SolidBrush(FILL_CLR))
                    g.FillEllipse(b, tx - dotR, ty - dotR, dotR * 2, dotR * 2);
            }
        }

        static void FillPill(Graphics g, Brush b, int x, int y, int w, int h) {
            if (w < h) w = h;
            if (w <= 0 || h <= 0) return;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(x, y, h, h, 90, 180);
            path.AddArc(x + w - h, y, h, h, 270, 180);
            path.CloseFigure();
            g.FillPath(b, path);
            path.Dispose();
        }
    }

    /// <summary>Message filter to defocus NumericUpDown when clicking elsewhere.</summary>
    public class NudDefocusFilter : IMessageFilter
    {
        private Form _form;
        public NudDefocusFilter(Form form) { _form = form; }
        public bool PreFilterMessage(ref Message m) {
            // WM_LBUTTONDOWN = 0x0201
            if (m.Msg == 0x0201) {
                var active = _form.ActiveControl;
                if (active is NumericUpDown || (active != null && active.Parent is NumericUpDown)) {
                    var ctrl = Control.FromHandle(m.HWnd);
                    if (ctrl != null && !(ctrl is NumericUpDown) && !(ctrl != null && ctrl.Parent is NumericUpDown)) {
                        _form.ActiveControl = null;
                    }
                }
            }
            return false;
        }
    }

    public class PaddedNumericUpDown : NumericUpDown
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        private const int WM_PASTE = 0x0302;

        public PaddedNumericUpDown() {
            HandleCreated += (s, e) => {
                try {
                    SetWindowTheme(Handle, "DarkMode_Explorer", null);
                    foreach (Control c in Controls) {
                        try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
                    }
                } catch { }
            };
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            // Intercept Ctrl+V — only paste digits
            if (e.Control && e.KeyCode == Keys.V) {
                e.SuppressKeyPress = true;
                try {
                    string clip = System.Windows.Forms.Clipboard.GetText();
                    string digits = "";
                    foreach (char c in clip) { if (char.IsDigit(c)) digits += c; }
                    if (digits.Length > 0) {
                        int v;
                        if (int.TryParse(digits, out v)) {
                            v = Math.Max((int)Minimum, Math.Min((int)Maximum, v));
                            Value = v;
                        }
                    }
                } catch { }
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e) {
            // Allow only digits, backspace, and control characters
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != '\b' && !char.IsControl(e.KeyChar)) {
                e.Handled = true;
                return;
            }
            base.OnKeyPress(e);
        }

        protected override void WndProc(ref Message m) {
            // Block WM_PASTE from context menu — handled by OnKeyDown for Ctrl+V
            if (m.Msg == WM_PASTE) {
                try {
                    string clip = System.Windows.Forms.Clipboard.GetText();
                    string digits = "";
                    foreach (char c in clip) { if (char.IsDigit(c)) digits += c; }
                    if (digits.Length > 0) {
                        int v;
                        if (int.TryParse(digits, out v)) {
                            v = Math.Max((int)Minimum, Math.Min((int)Maximum, v));
                            Value = v;
                        }
                    }
                } catch { }
                return; // swallow the message
            }
            base.WndProc(ref m);
        }

        protected override void UpdateEditText()
        {
            int iv = (int)Value;
            Text = iv < 10 ? "0" + iv : iv.ToString();
        }
    }

    public class SplashForm : Form
    {
        private Timer _closeTimer, _fadeTimer;
        private float _progress = 1f;
        private bool _fadingOut;
        private Settings _settings;
        private int _displayMs;
        private DateTime _startTime;
        public int DisplayMs { get { return _displayMs; } set { _displayMs = value; } }

        public SplashForm(Settings settings)
        {
            _settings = settings ?? new Settings();
            _displayMs = 3000;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = true; TopMost = true;
            Activated += (s, e) => { if (TopMost) TopMost = false; };
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = DarkTheme.BG;
            DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
            ClientSize = Dpi.Size(300, 300);
            Opacity = 0; _startTime = DateTime.UtcNow;

            _fadeTimer = new Timer { Interval = 30 };
            _fadeTimer.Tick += (s, e) => {
                double elapsed = (DateTime.UtcNow - _startTime).TotalMilliseconds;
                _progress = Math.Max(0f, 1f - (float)(elapsed / _displayMs));
                if (_fadingOut)
                {
                    Opacity = Math.Max(Opacity - 0.06, 0);
                    if (Opacity <= 0.01) { _fadeTimer.Stop(); Hide(); Close(); }
                }
                else
                {
                    if (Opacity < 0.95) Opacity = Math.Min(Opacity + 0.1, 0.95);
                }
                Invalidate();
            };
            _closeTimer = new Timer { Interval = _displayMs };
            _closeTimer.Tick += (s, e) => { _closeTimer.Stop(); _fadingOut = true; };
            Click += (s, e) => { _closeTimer.Stop(); _fadingOut = true; };
            _fadeTimer.Start(); _closeTimer.Start();
        }

        // Mascot drawing now handled by Mascot.cs

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;

            using (var gb = new LinearGradientBrush(new Point(0, 0), new Point(0, h),
                DarkTheme.InputBG, DarkTheme.BG))
                g.FillRectangle(gb, 0, 0, w, h);
            using (var p = new Pen(Color.FromArgb(30, 30, 30), 1)) g.DrawRectangle(p, 0, 0, w - 1, h - 1);

            // Mascot — compact
            int mascotSz = Dpi.S(90);
            int mascotY = Dpi.S(14);
            Mascot.DrawMascot(g, (w - mascotSz) / 2f, mascotY, mascotSz);

            // Title
            int titleY = mascotY + mascotSz + Dpi.S(2);
            using (var f = new Font("Segoe UI", 13, FontStyle.Bold)) {
                var sz1 = g.MeasureString("Angry ", f); var sz2 = g.MeasureString("Audio", f);
                float totalW = sz1.Width + sz2.Width, tx = (w - totalW) / 2f;
                using (var b = new SolidBrush(Color.White)) g.DrawString("Angry", f, b, tx, titleY);
                using (var b = new SolidBrush(DarkTheme.Accent)) g.DrawString("Audio", f, b, tx + sz1.Width, titleY);
            }

            // Version pill + copyright
            int infoY = titleY + Dpi.S(28);
            using (var f = new Font("Segoe UI", 6.5f)) {
                string vTxt = "v" + AppVersion.Version + "  \u00B7  " + AppVersion.Copyright;
                var vs = g.MeasureString(vTxt, f);
                float pillW = vs.Width + Dpi.S(12), pillH = vs.Height + Dpi.S(4);
                float vx = (w - pillW) / 2f;
                using (var rr = DarkTheme.RoundedRect(new Rectangle((int)vx, infoY, (int)pillW, (int)pillH), Dpi.S(4)))
                using (var b = new SolidBrush(Color.FromArgb(24, 24, 24)))
                    g.FillPath(b, rr);
                using (var b = new SolidBrush(DarkTheme.Txt4))
                    g.DrawString(vTxt, f, b, vx + Dpi.S(6), infoY + Dpi.S(2));
            }

            // PTT — golden shield + label
            int pttY = infoY + Dpi.S(26);
            bool vaActive = _settings.VoiceActivityEnabled;
            bool pttActive = !vaActive && (_settings.PushToTalkEnabled || _settings.PushToMuteEnabled || _settings.PushToToggleEnabled);
            string pttLabel = vaActive ? "Voice Activity" : _settings.PushToTalkEnabled ? "Push-to-Talk" : _settings.PushToMuteEnabled ? "Push-to-Mute" : _settings.PushToToggleEnabled ? "Push-to-Toggle" : "Off";
            Color gold = Color.FromArgb(218, 175, 62);
            Color goldDim = Color.FromArgb(55, 50, 35);
            Color goldText = Color.FromArgb(235, 215, 145);
            Color goldTextDim = Color.FromArgb(60, 58, 45);
            Color vaBlue = Color.FromArgb(80, 180, 255);
            Color vaText = Color.FromArgb(140, 210, 255);
            bool anyMicMode = pttActive || vaActive;
            Color shieldCol = vaActive ? vaBlue : (pttActive ? gold : goldDim);
            Color labelCol = vaActive ? vaText : (pttActive ? goldText : goldTextDim);
            float shSz = Dpi.S(22);
            float shShieldX = w / 2f - shSz / 2f;
            DarkTheme.DrawShield(g, shShieldX, pttY, shSz, shieldCol, anyMicMode);
            using (var f = new Font("Segoe UI", 8.5f, FontStyle.Bold)) using (var b = new SolidBrush(labelCol)) {
                var tsz = g.MeasureString(pttLabel, f);
                g.DrawString(pttLabel, f, b, (w - tsz.Width) / 2f, pttY + shSz + Dpi.S(2));
            }

            // Two-column status: AFK PROTECTION | ENFORCEMENT
            int colY = pttY + Dpi.S(46);
            int colMargin = Dpi.S(12);
            int colW = (w - colMargin * 2) / 2;
            int col1X = colMargin;
            int col2X = col1X + colW;
            int iconSz = Dpi.S(13);

            bool afkAny = _settings.AfkMicMuteEnabled || _settings.AfkSpeakerMuteEnabled;
            DrawColHeader(g, col1X, colY, colW, "AFK PROTECTION", afkAny);
            DrawStatusRow(g, col1X, colY + Dpi.S(16), colW, iconSz, "Mic", 0, _settings.AfkMicMuteEnabled);
            DrawStatusRow(g, col1X, colY + Dpi.S(34), colW, iconSz, "Spk", 1, _settings.AfkSpeakerMuteEnabled);

            using (var p = new Pen(Color.FromArgb(35, 35, 35), 1)) g.DrawLine(p, col2X, colY, col2X, colY + Dpi.S(48));

            bool enfAny = _settings.MicEnforceEnabled || _settings.SpeakerEnforceEnabled;
            DrawColHeader(g, col2X, colY, colW, "ENFORCEMENT", enfAny);
            DrawStatusRow(g, col2X, colY + Dpi.S(16), colW, iconSz, "Mic", 0, _settings.MicEnforceEnabled);
            DrawStatusRow(g, col2X, colY + Dpi.S(34), colW, iconSz, "Spk", 1, _settings.SpeakerEnforceEnabled);

            // Progress bar + dismiss text
            int barY = h - Dpi.S(30);
            using (var b = new SolidBrush(Color.FromArgb(26, 26, 26))) g.FillRectangle(b, 0, barY, w, Dpi.S(3));
            using (var b = new SolidBrush(DarkTheme.Accent)) g.FillRectangle(b, 0, barY, (int)(w * _progress), Dpi.S(3));
            double remaining = Math.Max(0, _displayMs - (DateTime.UtcNow - _startTime).TotalMilliseconds);
            int sec = (int)Math.Ceiling(remaining / 1000.0);
            using (var f = new Font("Segoe UI", 7f)) using (var b = new SolidBrush(Color.FromArgb(80, 80, 80))) {
                string txt = "Closing in " + sec + "s \u00B7 Click to Dismiss";
                var ts = g.MeasureString(txt, f); g.DrawString(txt, f, b, (w - ts.Width) / 2f, barY + Dpi.S(6));
            }
        }

        private void DrawCleanShield(Graphics g, float x, float y, float sz, Color fill, bool active) {
            float s = sz / 20f;
            var path = new GraphicsPath();
            // Clean symmetrical shield
            path.AddLine(x + 10*s, y, x + 18*s, y + 4*s);
            path.AddLine(x + 18*s, y + 4*s, x + 18*s, y + 10*s);
            path.AddBezier(x + 18*s, y + 10*s, x + 17*s, y + 15*s, x + 12*s, y + 18*s, x + 10*s, y + 20*s);
            path.AddBezier(x + 10*s, y + 20*s, x + 8*s, y + 18*s, x + 3*s, y + 15*s, x + 2*s, y + 10*s);
            path.AddLine(x + 2*s, y + 10*s, x + 2*s, y + 4*s);
            path.CloseFigure();
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            // Subtle highlight edge
            using (var p = new Pen(Color.FromArgb(active ? 50 : 20, 255, 255, 255), 0.8f * s)) g.DrawPath(p, path);
            if (active) {
                using (var p = new Pen(Color.White, 1.6f * s)) {
                    p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLine(p, x + 6.5f*s, y + 10.5f*s, x + 9*s, y + 13.5f*s);
                    g.DrawLine(p, x + 9*s, y + 13.5f*s, x + 14*s, y + 7*s);
                }
            }
            path.Dispose();
        }
        private void DrawColHeader(Graphics g, int x, int y, int colW, string heading, bool active) {
            using (var f = new Font("Segoe UI", 6.5f, FontStyle.Bold))
            using (var b = new SolidBrush(active ? DarkTheme.Accent : Color.FromArgb(60, 60, 60))) {
                var hs = g.MeasureString(heading, f);
                g.DrawString(heading, f, b, x + (colW - hs.Width) / 2f, y);
            }
        }
        // iconType: 0=mic, 1=speaker, 2=camera
        private void DrawStatusRow(Graphics g, int x, int y, int colW, int iconSz, string label, int iconType, bool active) {
            Color activeCol = Color.FromArgb(200, 200, 200), offCol = Color.FromArgb(55, 55, 55);
            Color iconCol = active ? activeCol : offCol;
            int cx = x + colW / 2;
            DarkTheme.DrawShield(g, cx - Dpi.S(32), y - Dpi.S(1), iconSz, active ? DarkTheme.Green : Color.FromArgb(42, 42, 42), active);
            if (iconType == 0) DrawCleanMic(g, cx - Dpi.S(14), y, iconSz, iconCol);
            else if (iconType == 1) DrawCleanSpeaker(g, cx - Dpi.S(14), y, iconSz, iconCol);
            using (var f = new Font("Segoe UI", 8f)) using (var br = new SolidBrush(iconCol))
                g.DrawString(label, f, br, cx + Dpi.S(2), y);
        }
        private void DrawCleanMic(Graphics g, float x, float y, float sz, Color c) {
            float s = sz / 16f;
            using (var b = new SolidBrush(c)) {
                // Mic body — rounded rect via path
                var body = new GraphicsPath();
                float bw = 4.5f*s, bh = 8*s, bx = x + 5.75f*s, by = y + 1*s, br = 2.2f*s;
                body.AddArc(bx, by, br*2, br*2, 180, 90); body.AddArc(bx+bw-br*2, by, br*2, br*2, 270, 90);
                body.AddArc(bx+bw-br*2, by+bh-br*2, br*2, br*2, 0, 90); body.AddArc(bx, by+bh-br*2, br*2, br*2, 90, 90);
                body.CloseFigure(); g.FillPath(b, body); body.Dispose();
                // Stand
                g.FillRectangle(b, x + 7.25f*s, y + 11.5f*s, 1.5f*s, 2.5f*s);
                g.FillRectangle(b, x + 5*s, y + 13.5f*s, 6*s, 1*s);
            }
            // Arc
            using (var p = new Pen(c, 1.2f*s)) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawArc(p, x + 3.5f*s, y + 4*s, 9*s, 8*s, 0, 180); }
        }
        private void DrawCleanSpeaker(Graphics g, float x, float y, float sz, Color c) {
            float s = sz / 16f;
            using (var b = new SolidBrush(c)) {
                g.FillRectangle(b, x + 3*s, y + 5.5f*s, 2.5f*s, 5*s);
                var cone = new GraphicsPath();
                cone.AddPolygon(new PointF[] { new PointF(x+5.5f*s,y+5.5f*s), new PointF(x+9*s,y+2.5f*s), new PointF(x+9*s,y+13.5f*s), new PointF(x+5.5f*s,y+10.5f*s) });
                g.FillPath(b, cone); cone.Dispose();
            }
            using (var p = new Pen(c, 1.2f*s)) { p.StartCap = System.Drawing.Drawing2D.LineCap.Round; p.EndCap = System.Drawing.Drawing2D.LineCap.Round; g.DrawArc(p, x + 10*s, y + 5*s, 3.5f*s, 6*s, -55, 110); }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00000008 | 0x08000000 | 0x00000080; return cp; } }
        protected override void Dispose(bool d) { if (d) { if (_fadeTimer != null) _fadeTimer.Dispose(); if (_closeTimer != null) _closeTimer.Dispose(); } base.Dispose(d); }
    }

}
