using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GrbLHALSender.Gcode;
using GrbLHALSender.Settings;
using SkiaSharp;
using System;

namespace GrbLHALSender.Views.GcodeRenderControl
{
    public partial class GcodeRenderControl : UserControl
    {
        public static readonly StyledProperty<ToolpathData?> ToolpathProperty =
            AvaloniaProperty.Register<GcodeRenderControl, ToolpathData?>(nameof(Toolpath));

        public static readonly StyledProperty<Point3D?> SpindlePositionProperty =
            AvaloniaProperty.Register<GcodeRenderControl, Point3D?>(nameof(SpindlePosition));

        public static readonly StyledProperty<MachineSettings?> MachineSettingsProperty =
            AvaloniaProperty.Register<GcodeRenderControl, MachineSettings?>(nameof(MachineSettings));

        public static readonly StyledProperty<Point3D?> WorkCoordinateOffsetProperty =
            AvaloniaProperty.Register<GcodeRenderControl, Point3D?>(nameof(WorkCoordinateOffset));

        public static readonly StyledProperty<int> CompletedSegmentIndexProperty =
            AvaloniaProperty.Register<GcodeRenderControl, int>(nameof(CompletedSegmentIndex), defaultValue: -1);

        public static readonly StyledProperty<int> SelectedSegmentIndexProperty =
            AvaloniaProperty.Register<GcodeRenderControl, int>(nameof(SelectedSegmentIndex), defaultValue: -1);

        public static readonly StyledProperty<string> SelectedLineInfoProperty =
            AvaloniaProperty.Register<GcodeRenderControl, string>(nameof(SelectedLineInfo), defaultValue: "");

        public static readonly StyledProperty<bool> UseAntiAliasProperty =
            AvaloniaProperty.Register<GcodeRenderControl, bool>(nameof(UseAntiAlias), defaultValue: true);

        public static readonly StyledProperty<string?> SpindleImagePathProperty =
            AvaloniaProperty.Register<GcodeRenderControl, string?>(nameof(SpindleImagePath), defaultValue: "spindle.png");

        public ToolpathData? Toolpath
        {
            get => GetValue(ToolpathProperty);
            set => SetValue(ToolpathProperty, value);
        }

        public Point3D? SpindlePosition
        {
            get => GetValue(SpindlePositionProperty);
            set => SetValue(SpindlePositionProperty, value);
        }

        public MachineSettings? MachineSettings
        {
            get => GetValue(MachineSettingsProperty);
            set => SetValue(MachineSettingsProperty, value);
        }

        public Point3D? WorkCoordinateOffset
        {
            get => GetValue(WorkCoordinateOffsetProperty);
            set => SetValue(WorkCoordinateOffsetProperty, value);
        }

        public int CompletedSegmentIndex
        {
            get => GetValue(CompletedSegmentIndexProperty);
            set => SetValue(CompletedSegmentIndexProperty, value);
        }

        public int SelectedSegmentIndex
        {
            get => GetValue(SelectedSegmentIndexProperty);
            set => SetValue(SelectedSegmentIndexProperty, value);
        }

        public string SelectedLineInfo
        {
            get => GetValue(SelectedLineInfoProperty);
            set => SetValue(SelectedLineInfoProperty, value);
        }

        public bool UseAntiAlias
        {
            get => GetValue(UseAntiAliasProperty);
            set => SetValue(UseAntiAliasProperty, value);
        }

        public string? SpindleImagePath
        {
            get => GetValue(SpindleImagePathProperty);
            set => SetValue(SpindleImagePathProperty, value);
        }

        private readonly Camera3D _camera = new();
        private readonly ToolpathSceneCache _sceneCache = new();
        private readonly SpindleImageProvider _spindleImageProvider = new();
        private Point? _lastPointerPos;
        private Point? _pressStartPos;
        private bool _isLeftDragging;
        private bool _isRightDragging;
        private bool _isMiddleDragging;
        private bool _fitted;

        public GcodeRenderControl()
        {
            InitializeComponent();
            Background = Brushes.Transparent; // Required for hit testing
            ClipToBounds = true;

            // Load the spindle image from Config folder on startup
            _spindleImageProvider.TryLoad(SpindleImagePath);
        }

        static GcodeRenderControl()
        {
            // Only properties that require a full scene cache rebuild are in AffectsRender.
            // SpindlePosition, CompletedSegmentIndex, and SelectedSegmentIndex are NOT here —
            // they are dynamic overlays drawn on top of the cached static scene (no cache rebuild).
            // This keeps the render control from flooding the compositor with invalidations
            // during job execution (~10 Hz spindle + ~5 Hz progress = too many renders).
            AffectsRender<GcodeRenderControl>(ToolpathProperty,
                MachineSettingsProperty, WorkCoordinateOffsetProperty);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ToolpathProperty || change.Property == MachineSettingsProperty ||
                change.Property == WorkCoordinateOffsetProperty)
            {
                _fitted = false;
                if (change.Property == ToolpathProperty)
                {
                    SelectedSegmentIndex = -1;
                    SelectedLineInfo = "";
                }
                InvalidateVisual();
            }
            else if (change.Property == UseAntiAliasProperty)
            {
                _sceneCache.Invalidate();
                InvalidateVisual();
            }
            else if (change.Property == SpindleImagePathProperty)
            {
                _spindleImageProvider.TryLoad(SpindleImagePath);
                InvalidateVisual();
            }
            else if (change.Property == SpindlePositionProperty ||
                     change.Property == CompletedSegmentIndexProperty ||
                     change.Property == SelectedSegmentIndexProperty)
            {
                // Lightweight repaint — spindle + overlays use pre-projected coordinates,
                // no cache rebuild needed. Avalonia coalesces multiple InvalidateVisual()
                // calls within the same frame, so spindle + progress arriving close together
                // result in a single render pass.
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

            var toolpath = Toolpath;
            var machine = MachineSettings;

            var wco = WorkCoordinateOffset;

            if (!_fitted)
            {
                if (toolpath != null && toolpath.Segments.Count > 0)
                {
                    // Toolpath loaded — fit to toolpath (uses machine bounds if available)
                    _camera.FitToView(toolpath, (float)Bounds.Width, (float)Bounds.Height, machine, wco);
                    _fitted = true;
                }
                else if (machine != null && machine.DisplayXSize > 0 && machine.DisplayYSize > 0)
                {
                    // No toolpath yet — fit to machine grid at startup
                    _camera.FitToMachine(machine, (float)Bounds.Width, (float)Bounds.Height, wco);
                    _fitted = true;
                }
            }

            var op = new GcodeRenderOperation(
                new Rect(0, 0, Bounds.Width, Bounds.Height),
                toolpath,
                _camera,
                _sceneCache,
                SpindlePosition,
                machine,
                WorkCoordinateOffset,
                CompletedSegmentIndex,
                SelectedSegmentIndex,
                _spindleImageProvider.Bitmap,
                UseAntiAlias);

            context.Custom(op);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
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

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_lastPointerPos == null) return;

            var currentPos = e.GetCurrentPoint(this).Position;
            var deltaX = (float)(currentPos.X - _lastPointerPos.Value.X);
            var deltaY = (float)(currentPos.Y - _lastPointerPos.Value.Y);

            if (_isLeftDragging)
            {
                _camera.Rotate(deltaX, deltaY);
                InvalidateVisual();
            }
            else if (_isRightDragging || _isMiddleDragging)
            {
                _camera.Pan(deltaX, deltaY);
                InvalidateVisual();
            }

            _lastPointerPos = currentPos;
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            // Detect click vs drag: if pointer moved less than 5px, treat as a click
            if (_pressStartPos.HasValue)
            {
                var releasePos = e.GetCurrentPoint(this).Position;
                var dx = releasePos.X - _pressStartPos.Value.X;
                var dy = releasePos.Y - _pressStartPos.Value.Y;
                if (dx * dx + dy * dy < 25) // 5px squared
                {
                    OnSegmentClicked((float)releasePos.X, (float)releasePos.Y);
                }
            }

            _isLeftDragging = false;
            _isRightDragging = false;
            _isMiddleDragging = false;
            _lastPointerPos = null;
            _pressStartPos = null;
            e.Handled = true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            _camera.Zoom((float)e.Delta.Y);
            InvalidateVisual();
            e.Handled = true;
        }

        /// <summary>
        /// Hit-tests click against all toolpath segments projected to screen space.
        /// Finds the closest segment within a 10px tolerance and selects it.
        /// </summary>
        private void OnSegmentClicked(float screenX, float screenY)
        {
            var toolpath = Toolpath;
            if (toolpath == null || toolpath.Segments.Count == 0) return;

            var viewProj = _sceneCache.CachedViewProj;
            float width = (float)Bounds.Width;
            float height = (float)Bounds.Height;

            float bestDistSq = float.MaxValue;
            int bestIndex = -1;

            for (int i = 0; i < toolpath.Segments.Count; i++)
            {
                var seg = toolpath.Segments[i];
                var p1 = GcodeRenderOperation.ProjectToScreen(seg.Start, viewProj, width, height);
                var p2 = GcodeRenderOperation.ProjectToScreen(seg.End, viewProj, width, height);

                if (!p1.HasValue || !p2.HasValue) continue;

                float distSq = DistanceToLineSegmentSq(screenX, screenY, p1.Value, p2.Value);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIndex = i;
                }
            }

            // 10px tolerance (squared = 100)
            if (bestIndex >= 0 && bestDistSq <= 100f)
            {
                SelectedSegmentIndex = bestIndex;
                var segment = toolpath.Segments[bestIndex];
                SelectedLineInfo = $"Line: {segment.SourceLineIndex + 1}";
            }
            else
            {
                // Clicked empty space — clear selection
                SelectedSegmentIndex = -1;
                SelectedLineInfo = "";
            }
        }

        /// <summary>
        /// Computes the squared distance from point (px, py) to line segment (p1 → p2).
        /// </summary>
        private static float DistanceToLineSegmentSq(float px, float py, SKPoint p1, SKPoint p2)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float lenSq = dx * dx + dy * dy;

            if (lenSq < 0.001f)
            {
                // Degenerate segment (start == end): distance to point
                float ex = px - p1.X;
                float ey = py - p1.Y;
                return ex * ex + ey * ey;
            }

            // Project point onto the line, clamped to [0,1]
            float t = ((px - p1.X) * dx + (py - p1.Y) * dy) / lenSq;
            t = Math.Clamp(t, 0f, 1f);

            // Closest point on segment
            float closestX = p1.X + t * dx;
            float closestY = p1.Y + t * dy;

            float distX = px - closestX;
            float distY = py - closestY;
            return distX * distX + distY * distY;
        }
    }
}
