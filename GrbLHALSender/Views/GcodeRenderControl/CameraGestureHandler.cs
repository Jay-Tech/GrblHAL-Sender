using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Collections.Generic;

namespace GrbLHALSender.Views.GcodeRenderControl
{
    /// <summary>
    /// Mouse and touch camera control for the toolpath views. Both the Skia and the OpenGL
    /// render control delegate their pointer overrides here so the two cannot drift apart —
    /// the OpenGL one grew gestures first, and the difference was invisible until someone
    /// switched renderers.
    ///
    /// Mouse: left drag orbits, right/middle drag pans, wheel zooms, click selects a segment.
    /// Touch: one finger orbits, two fingers pinch-zoom and pan at the same time, a single tap
    /// selects a segment and a double tap resets the view.
    ///
    /// Touch is handled separately from mouse because Avalonia raises a full press/move/release
    /// stream per finger, each with its own pointer id, and single-position mouse state cannot
    /// represent more than one of them.
    ///
    /// Everything here runs on the UI thread; none of this state is safe to read from a render
    /// thread.
    /// </summary>
    public sealed class CameraGestureHandler
    {
        private readonly Control _owner;
        private readonly Camera3D _camera;
        private readonly Action _invalidate;
        private readonly Action<float, float> _segmentClicked;
        private readonly Action _resetView;

        // Touch contacts currently on the glass, in the order they landed.
        private readonly List<(int Id, Point Pos)> _touches = new();
        private Point _lastPinchCentroid;
        private double _lastPinchSpread;
        private bool _multiTouchActive;
        private bool _touchDragStarted;

        private Point? _lastPointerPos;
        private Point? _pressStartPos;
        private bool _isLeftDragging;
        private bool _isRightDragging;
        private bool _isMiddleDragging;

        private long _lastTapTicks;
        private bool _hasPreviousTap;
        private Point _lastTapPos;

        // A click must land within this much of the press to count as a click and not a drag.
        private const double MouseClickSlopSq = 25.0;
        // Touch slop is far wider than the mouse's: a fingertip covers several mm and rolls as
        // it lifts, so 5 px would turn most taps into drags. Orbiting is held off until the
        // finger passes this too, otherwise a tap nudges the scene on its way out.
        private const double TouchTapSlopSq = 144.0;
        // Double-tap window. Deliberately looser than a mouse double-click: fingers are blunt,
        // and the Pi's touchscreen reports a few px of jitter between contacts.
        private const double DoubleTapGapMs = 400.0;
        private const double DoubleTapSlopSq = 1600.0;
        // Below this finger separation the spread ratio is mostly noise, so pinch is ignored.
        private const double MinPinchSpread = 24.0;

        /// <param name="owner">Control the gestures act on; used for hit coordinates and capture.</param>
        /// <param name="camera">Camera the gestures drive.</param>
        /// <param name="invalidate">Requests a repaint (InvalidateVisual, or the GL dirty flag).</param>
        /// <param name="segmentClicked">Runs the segment hit-test for a click or tap, in control coordinates.</param>
        /// <param name="resetView">Returns the view to its freshly-loaded framing (double tap).</param>
        public CameraGestureHandler(
            Control owner,
            Camera3D camera,
            Action invalidate,
            Action<float, float> segmentClicked,
            Action resetView)
        {
            _owner = owner;
            _camera = camera;
            _invalidate = invalidate;
            _segmentClicked = segmentClicked;
            _resetView = resetView;
        }

        private static bool IsTouch(PointerEventArgs e) => e.Pointer.Type == PointerType.Touch;

        private int IndexOfTouch(int id)
        {
            for (int i = 0; i < _touches.Count; i++)
            {
                if (_touches[i].Id == id) return i;
            }
            return -1;
        }

        /// <summary>
        /// Midpoint and separation of the first two fingers down. Extra fingers are ignored so
        /// that a palm edge landing on the screen mid-pinch does not drag the gesture around.
        /// </summary>
        private (Point Centroid, double Spread) GetPinchMetrics()
        {
            var a = _touches[0].Pos;
            var b = _touches[1].Pos;
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            return (new Point((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0), Math.Sqrt(dx * dx + dy * dy));
        }

        private void SeedPinchMetrics()
        {
            var metrics = GetPinchMetrics();
            _lastPinchCentroid = metrics.Centroid;
            _lastPinchSpread = metrics.Spread;
        }

        /// <summary>
        /// Drops all gesture state once the last finger or button is gone.
        /// </summary>
        private void EndAllGestures()
        {
            _multiTouchActive = false;
            _touchDragStarted = false;
            _isLeftDragging = false;
            _isRightDragging = false;
            _isMiddleDragging = false;
            _lastPointerPos = null;
            _pressStartPos = null;
        }

        public void PointerPressed(PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(_owner);

            if (IsTouch(e))
            {
                // Capture per finger. Without it a gesture that strays past the edge of the
                // control - easy to do on a small panel - stops delivering moves and releases,
                // leaving the finger stuck in _touches forever.
                e.Pointer.Capture(_owner);
                _touches.Add((e.Pointer.Id, point.Position));

                if (_touches.Count == 1)
                {
                    _lastPointerPos = point.Position;
                    _pressStartPos = point.Position;
                    _isLeftDragging = true;
                    _touchDragStarted = false;
                }
                else
                {
                    // Second finger down: abandon the orbit the first finger started so the
                    // scene does not lurch, and stay in multi-touch mode until the whole hand
                    // lifts (see PointerReleased).
                    _isLeftDragging = false;
                    _lastPointerPos = null;
                    _pressStartPos = null;
                    _multiTouchActive = true;
                    SeedPinchMetrics();
                }

                e.Handled = true;
                return;
            }

            _lastPointerPos = point.Position;
            _pressStartPos = point.Position;

            if (point.Properties.IsLeftButtonPressed)
                _isLeftDragging = true;
            if (point.Properties.IsRightButtonPressed)
                _isRightDragging = true;
            if (point.Properties.IsMiddleButtonPressed)
                _isMiddleDragging = true;

            e.Handled = true;
        }

        public void PointerMoved(PointerEventArgs e)
        {
            if (IsTouch(e))
            {
                int index = IndexOfTouch(e.Pointer.Id);
                if (index < 0) return;

                var touchPos = e.GetCurrentPoint(_owner).Position;
                _touches[index] = (e.Pointer.Id, touchPos);

                if (_touches.Count >= 2)
                {
                    var metrics = GetPinchMetrics();

                    // Pinch: feed the raw spread ratio to the camera so the model tracks the
                    // fingers, rather than stepping in fixed mouse-wheel-sized increments.
                    if (_lastPinchSpread > MinPinchSpread && metrics.Spread > MinPinchSpread)
                        _camera.ZoomByFactor((float)(metrics.Spread / _lastPinchSpread));

                    // Two-finger drag pans. The deltas are negated because Camera3D.Pan moves
                    // the camera, and on a touchscreen the scene is expected to follow the
                    // fingers instead of running away from them.
                    var panX = (float)(metrics.Centroid.X - _lastPinchCentroid.X);
                    var panY = (float)(metrics.Centroid.Y - _lastPinchCentroid.Y);
                    if (panX != 0f || panY != 0f)
                        _camera.Pan(-panX, -panY);

                    _lastPinchCentroid = metrics.Centroid;
                    _lastPinchSpread = metrics.Spread;
                    _invalidate();
                }
                else if (!_multiTouchActive && _isLeftDragging && _lastPointerPos != null)
                {
                    if (!_touchDragStarted && _pressStartPos.HasValue)
                    {
                        var slopX = touchPos.X - _pressStartPos.Value.X;
                        var slopY = touchPos.Y - _pressStartPos.Value.Y;
                        if (slopX * slopX + slopY * slopY >= TouchTapSlopSq)
                            _touchDragStarted = true;
                    }

                    if (_touchDragStarted)
                    {
                        _camera.Rotate((float)(touchPos.X - _lastPointerPos.Value.X),
                                       (float)(touchPos.Y - _lastPointerPos.Value.Y));
                        _invalidate();
                    }

                    // Tracked even while inside the slop so the orbit picks up smoothly from
                    // where the finger is now, instead of jumping by the slop distance.
                    _lastPointerPos = touchPos;
                }

                e.Handled = true;
                return;
            }

            if (_lastPointerPos == null) return;

            var currentPos = e.GetCurrentPoint(_owner).Position;
            var deltaX = (float)(currentPos.X - _lastPointerPos.Value.X);
            var deltaY = (float)(currentPos.Y - _lastPointerPos.Value.Y);

            if (_isLeftDragging)
            {
                _camera.Rotate(deltaX, deltaY);
                _invalidate();
            }
            else if (_isRightDragging || _isMiddleDragging)
            {
                _camera.Pan(deltaX, deltaY);
                _invalidate();
            }

            _lastPointerPos = currentPos;
            e.Handled = true;
        }

        public void PointerReleased(PointerReleasedEventArgs e)
        {
            var releasePos = e.GetCurrentPoint(_owner).Position;

            if (IsTouch(e))
            {
                int index = IndexOfTouch(e.Pointer.Id);
                if (index >= 0) _touches.RemoveAt(index);

                // Only a lone finger that never crossed the slop counts as a tap - never the
                // tail end of a pinch, which would otherwise select a segment on every zoom.
                if (!_multiTouchActive && !_touchDragStarted && _touches.Count == 0)
                    HandleTap(releasePos);

                if (_touches.Count == 0)
                {
                    EndAllGestures();
                }
                else if (_touches.Count == 1)
                {
                    // Down to one finger, but _multiTouchActive stays set: resuming the orbit
                    // here would snap the scene sideways every time a pinch ends unevenly.
                    _lastPointerPos = null;
                }
                else
                {
                    // Three or more fingers and one left: the pair being tracked may have
                    // changed, so re-seed rather than read a bogus delta on the next move.
                    SeedPinchMetrics();
                }

                e.Handled = true;
                return;
            }

            // Click vs drag: under the slop it is a click, and selects a segment.
            if (_pressStartPos.HasValue)
            {
                var dx = releasePos.X - _pressStartPos.Value.X;
                var dy = releasePos.Y - _pressStartPos.Value.Y;
                if (dx * dx + dy * dy < MouseClickSlopSq)
                    _segmentClicked((float)releasePos.X, (float)releasePos.Y);
            }

            EndAllGestures();
            e.Handled = true;
        }

        /// <summary>
        /// A capture can be revoked out from under us (window deactivated, another control
        /// grabbing the pointer). Without this the finger stays in <see cref="_touches"/> and
        /// every later gesture is measured against a contact no longer on the glass.
        /// </summary>
        public void PointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            int index = IndexOfTouch(e.Pointer.Id);
            if (index >= 0) _touches.RemoveAt(index);

            if (_touches.Count == 0)
                EndAllGestures();
            else if (_touches.Count >= 2)
                SeedPinchMetrics();
        }

        public void PointerWheelChanged(PointerWheelEventArgs e)
        {
            _camera.Zoom((float)e.Delta.Y);
            _invalidate();
            e.Handled = true;
        }

        /// <summary>
        /// Single tap selects a segment; a second tap in the same place resets the view. The
        /// first tap of a pair still runs its selection - deferring it would put a
        /// double-tap-length delay on every ordinary tap, which is worse than the stray
        /// selection a reset leaves behind.
        /// </summary>
        private void HandleTap(Point position)
        {
            long now = Environment.TickCount64;
            double tapDx = position.X - _lastTapPos.X;
            double tapDy = position.Y - _lastTapPos.Y;

            if (_hasPreviousTap &&
                now - _lastTapTicks <= DoubleTapGapMs &&
                tapDx * tapDx + tapDy * tapDy <= DoubleTapSlopSq)
            {
                _hasPreviousTap = false; // a third tap starts a fresh pair
                _resetView();
                return;
            }

            _hasPreviousTap = true;
            _lastTapTicks = now;
            _lastTapPos = position;
            _segmentClicked((float)position.X, (float)position.Y);
        }
    }
}
