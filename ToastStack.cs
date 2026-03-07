using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AngryAudio
{
    /// <summary>
    /// Manages a stack of toast popups in the bottom-right corner.
    /// New toasts push existing ones up. When a toast closes, remaining ones
    /// smoothly slide down to fill the gap. Buttery smooth.
    /// </summary>
    public static class ToastStack
    {
        private static readonly List<Form> _toasts = new List<Form>();
        private static readonly object _lock = new object();
        private static Timer _animTimer;
        private static readonly Dictionary<Form, int> _targetY = new Dictionary<Form, int>();
        private const int Gap = 4; // pixels between toasts
        private const int AnimStepPx = 8; // pixels per tick

        /// <summary>
        /// Register a toast form. Sets its initial position and pushes others up.
        /// Call BEFORE showing the form.
        /// </summary>
        public static void Register(Form toast)
        {
            lock (_lock)
            {
                // Remove any disposed forms
                _toasts.RemoveAll(f => f == null || f.IsDisposed);

                _toasts.Add(toast);
                toast.FormClosed += OnToastClosed;

                // Position all toasts from bottom up
                RecalcPositions();
            }
        }

        /// <summary>
        /// Immediately remove a toast from the stack (e.g. when dismissing programmatically).
        /// </summary>
        public static void Unregister(Form toast)
        {
            lock (_lock)
            {
                _toasts.Remove(toast);
                _targetY.Remove(toast);
                toast.FormClosed -= OnToastClosed;
                RecalcPositions();
            }
        }

        /// <summary>
        /// Dismiss all toasts except those matching excludeType. Uses fast-fade Dismiss() if available.
        /// </summary>
        public static void DismissAllExcept(Type excludeType)
        {
            List<Form> toDismiss;
            lock (_lock)
            {
                _toasts.RemoveAll(f => f == null || f.IsDisposed);
                toDismiss = new List<Form>(_toasts);
            }
            foreach (var t in toDismiss)
            {
                if (t == null || t.IsDisposed) continue;
                if (excludeType != null && excludeType.IsInstanceOfType(t)) continue;
                try
                {
                    var dismiss = t.GetType().GetMethod("Dismiss", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (dismiss != null) dismiss.Invoke(t, null);
                    else t.Close();
                }
                catch { try { t.Close(); } catch { } }
            }
        }

        private static void OnToastClosed(object sender, FormClosedEventArgs e)
        {
            var form = sender as Form;
            if (form == null) return;
            lock (_lock)
            {
                _toasts.Remove(form);
                _targetY.Remove(form);
                form.FormClosed -= OnToastClosed;
                RecalcPositions();
            }
        }

        /// <summary>
        /// Recalculate target Y positions for all toasts, stacking from bottom.
        /// Starts the animation timer if any toast needs to move.
        /// </summary>
        private static void RecalcPositions()
        {
            _toasts.RemoveAll(f => f == null || f.IsDisposed);

            var wa = Screen.PrimaryScreen.WorkingArea;
            int gap = Dpi.S(Gap);
            int margin = Dpi.S(4);
            int bottomMargin = Dpi.S(8); // margin from bottom of working area (above taskbar)
            int y = wa.Bottom - bottomMargin;

            // Stack from bottom: newest toast at bottom, older ones pushed up
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                if (t.IsDisposed) continue;
                y -= t.Height;
                _targetY[t] = y;

                // Set X position (always right-aligned)
                int targetX = wa.Right - t.Width - margin;
                if (t.Left != targetX)
                {
                    try { t.Left = targetX; } catch { }
                }

                // If this is brand new (not yet shown), snap to position immediately
                if (t.Top == 0 || Math.Abs(t.Top - wa.Bottom) < margin * 2)
                {
                    try { t.Top = y; } catch { }
                }

                y -= gap;
            }

            EnsureAnimTimer();
        }

        private static void EnsureAnimTimer()
        {
            if (_animTimer != null) return;
            _animTimer = new Timer { Interval = 16 }; // ~60fps
            _animTimer.Tick += AnimTick;
            _animTimer.Start();
        }

        private static void AnimTick(object sender, EventArgs e)
        {
            bool anyMoving = false;
            int step = Dpi.S(AnimStepPx);

            lock (_lock)
            {
                _toasts.RemoveAll(f => f == null || f.IsDisposed);

                foreach (var t in _toasts)
                {
                    if (t.IsDisposed || !_targetY.ContainsKey(t)) continue;

                    int target = _targetY[t];
                    int current = t.Top;
                    int diff = target - current;

                    if (Math.Abs(diff) <= 1)
                    {
                        if (current != target)
                            try { t.Top = target; } catch { }
                        continue;
                    }

                    anyMoving = true;
                    // Move toward target — ease with larger steps when far, smaller when close
                    int move = Math.Max(1, Math.Min(step, Math.Abs(diff) / 2 + 1));
                    if (diff < 0) move = -move;
                    try { t.Top = current + move; } catch { }
                }

                if (!anyMoving)
                {
                    _animTimer.Stop();
                    _animTimer.Dispose();
                    _animTimer = null;
                }
            }
        }
    }
}
