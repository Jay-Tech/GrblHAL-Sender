using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using GrbLHALSender.Gcode;
using GrbLHALSender.Settings;
using SkiaSharp;
using System;
using System.Numerics;

namespace GrbLHALSender.Views.GcodeRenderControl
{
    /// <summary>
    /// Two-layer cache for the toolpath scene:
    /// 1. Static layer (grid + axes + all toolpath segments in normal colors) — rasterized to
    ///    an SKBitmap so that dynamic frames only perform a single DrawBitmap (constant-time).
    ///    Only invalidated when camera, toolpath, machine settings, WCO, or control size change.
    /// 2. Projected screen coordinates — cached alongside the static layer so the progress
    ///    overlay and selection highlight can be drawn cheaply without re-projecting.
    ///
    /// When only CompletedSegmentIndex or SpindlePosition change, the cached bitmap is blitted
    /// and only the overlay segments are redrawn using pre-projected coordinates.
    /// </summary>
    public class ToolpathSceneCache
    {
        private SKBitmap? _cachedBitmap;
        private Matrix4x4 _cachedViewProj;
        private float _cachedWidth;
        private float _cachedHeight;
        private ToolpathData? _cachedToolpath;
        private MachineSettings? _cachedMachineSettings;
        private Point3D? _cachedWco;

        // Pre-projected screen coordinates for each segment (start and end).
        // Null entries indicate segments behind the camera.
        private SKPoint?[]? _projectedStarts;
        private SKPoint?[]? _projectedEnds;

        // Reusable paint for blitting the cached bitmap (no per-frame allocation).
        private static readonly SKPaint BitmapPaint = new()
        {
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false
        };

        public SKBitmap? GetOrCreate(
            ToolpathData? toolpath, Camera3D camera, MachineSettings? machineSettings,
            Point3D? wco, float width, float height, bool useAntiAlias)
        {
            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix(width, height);
            var viewProj = view * proj;

            int pixelW = (int)MathF.Ceiling(width);
            int pixelH = (int)MathF.Ceiling(height);
            if (pixelW <= 0 || pixelH <= 0) return null;

            // Check if cache is still valid
            if (_cachedBitmap != null &&
                _cachedViewProj == viewProj &&
                _cachedBitmap.Width == pixelW &&
                _cachedBitmap.Height == pixelH &&
                ReferenceEquals(_cachedToolpath, toolpath) &&
                ReferenceEquals(_cachedMachineSettings, machineSettings) &&
                Equals(_cachedWco, wco))
            {
                return _cachedBitmap;
            }

            // Rebuild: dispose old bitmap if size changed, otherwise reuse allocation
            if (_cachedBitmap == null || _cachedBitmap.Width != pixelW || _cachedBitmap.Height != pixelH)
            {
                _cachedBitmap?.Dispose();
                _cachedBitmap = new SKBitmap(pixelW, pixelH, SKColorType.Rgba8888, SKAlphaType.Premul);
            }

            // Pre-project all segment screen coordinates
            ProjectAllSegments(toolpath, viewProj, width, height);

            // Render the static scene into the bitmap
            using var canvas = new SKCanvas(_cachedBitmap);
            canvas.Clear(SKColors.Transparent);

            GcodeRenderOperation.DrawStaticScene(canvas, viewProj, width, height,
                toolpath, machineSettings, wco, _projectedStarts, _projectedEnds, useAntiAlias);

            canvas.Flush();

            _cachedViewProj = viewProj;
            _cachedWidth = width;
            _cachedHeight = height;
            _cachedToolpath = toolpath;
            _cachedMachineSettings = machineSettings;
            _cachedWco = wco;

            return _cachedBitmap;
        }

        public Matrix4x4 CachedViewProj => _cachedViewProj;
        public SKPoint?[]? ProjectedStarts => _projectedStarts;
        public SKPoint?[]? ProjectedEnds => _projectedEnds;

        /// <summary>
        /// Draws the cached bitmap onto the target canvas. Constant-time regardless of segment count.
        /// </summary>
        public void DrawCached(SKCanvas canvas)
        {
            if (_cachedBitmap != null)
                canvas.DrawBitmap(_cachedBitmap, 0, 0, BitmapPaint);
        }

        public void Invalidate()
        {
            _cachedBitmap?.Dispose();
            _cachedBitmap = null;
        }

        private void ProjectAllSegments(ToolpathData? toolpath, Matrix4x4 viewProj, float width, float height)
        {
            if (toolpath == null || toolpath.Segments.Count == 0)
            {
                _projectedStarts = null;
                _projectedEnds = null;
                return;
            }

            int count = toolpath.Segments.Count;
            if (_projectedStarts == null || _projectedStarts.Length != count)
            {
                _projectedStarts = new SKPoint?[count];
                _projectedEnds = new SKPoint?[count];
            }

            for (int i = 0; i < count; i++)
            {
                var seg = toolpath.Segments[i];
                _projectedStarts[i] = GcodeRenderOperation.ProjectToScreen(seg.Start, viewProj, width, height);
                _projectedEnds[i] = GcodeRenderOperation.ProjectToScreen(seg.End, viewProj, width, height);
            }
        }
    }

    public class GcodeRenderOperation : ICustomDrawOperation
    {
        private readonly ToolpathData? _toolpath;
        private readonly Camera3D _camera;
        private readonly Point3D? _spindlePosition;
        private readonly MachineSettings? _machineSettings;
        private readonly Point3D? _wco;
        private readonly ToolpathSceneCache _sceneCache;
        private readonly int _completedSegmentIndex;
        private readonly int _selectedSegmentIndex;
        private readonly SKBitmap? _spindleImage;
        private readonly bool _useAntiAlias;

        // AA-aware paint sets: [0] = AA off, [1] = AA on
        private static readonly SKPaint[] RapidPaints = CreatePaintPair(new SKColor(0, 200, 0), 1f, SKPaintStyle.Stroke);
        private static readonly SKPaint[] CutPaints = CreatePaintPair(new SKColor(230, 40, 40), 1.2f, SKPaintStyle.Stroke);
        private static readonly SKPaint[] TraversePaints = CreatePaintPair(new SKColor(60, 120, 255), 1f, SKPaintStyle.Stroke);
        private static readonly SKPaint[] CompletedPaints = CreatePaintPair(new SKColor(203, 203, 212), 2.4f, SKPaintStyle.Stroke);
        private static readonly SKPaint[] SelectedPaints = CreatePaintPair(new SKColor(255, 255, 0), 3f, SKPaintStyle.Stroke);
        private static readonly SKPaint[] GridPaints = CreatePaintPair(new SKColor(60, 60, 60), 0.5f, SKPaintStyle.Stroke);

        private static readonly SKPaint BackgroundPaint = new()
        {
            Color = new SKColor(25, 25, 30)
        };

        private static readonly SKPaint[] SpindleShaftPaints = CreatePaintPair(new SKColor(180, 180, 190), 0f, SKPaintStyle.Fill);
        private static readonly SKPaint[] SpindleBitPaints = CreatePaintPair(new SKColor(220, 180, 50), 0f, SKPaintStyle.Fill);
        private static readonly SKPaint[] SpindleOutlinePaints = CreatePaintPair(new SKColor(100, 100, 110), 1.5f, SKPaintStyle.Stroke);

        private static SKPaint[] CreatePaintPair(SKColor color, float strokeWidth, SKPaintStyle style)
        {
            return
            [
                new SKPaint { Color = color, StrokeWidth = strokeWidth, IsAntialias = false, Style = style },
                new SKPaint { Color = color, StrokeWidth = strokeWidth, IsAntialias = true, Style = style }
            ];
        }

        private static SKPaint Pick(SKPaint[] pair, bool aa) => pair[aa ? 1 : 0];

        public GcodeRenderOperation(Rect bounds, ToolpathData? toolpath, Camera3D camera,
            ToolpathSceneCache sceneCache,
            Point3D? spindlePosition = null, MachineSettings? machineSettings = null,
            Point3D? workCoordinateOffset = null, int completedSegmentIndex = -1,
            int selectedSegmentIndex = -1, SKBitmap? spindleImage = null,
            bool useAntiAlias = true)
        {
            Bounds = bounds;
            _toolpath = toolpath;
            _camera = camera;
            _sceneCache = sceneCache;
            _spindlePosition = spindlePosition;
            _machineSettings = machineSettings;
            _wco = workCoordinateOffset;
            _completedSegmentIndex = completedSegmentIndex;
            _selectedSegmentIndex = selectedSegmentIndex;
            _spindleImage = spindleImage;
            _useAntiAlias = useAntiAlias;
        }

        public Rect Bounds { get; }

        public bool HitTest(Point p) => Bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null) return;

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;

            float width = (float)Bounds.Width;
            float height = (float)Bounds.Height;

            canvas.Save();

            // Background
            canvas.DrawRect(0, 0, width, height, BackgroundPaint);

            // Get or create the cached static scene bitmap (grid + axes + all toolpath in normal colors).
            // This does NOT include completed/selected overlays — those are drawn dynamically below
            // using pre-projected coordinates, so changing CompletedSegmentIndex never busts this cache.
            // The bitmap is blitted in constant-time regardless of segment count.
            _sceneCache.GetOrCreate(
                _toolpath, _camera, _machineSettings, _wco, width, height, _useAntiAlias);
            _sceneCache.DrawCached(canvas);

            // Draw progress overlay using pre-projected screen coordinates (no re-projection needed)
            DrawProgressOverlay(canvas);

            // Draw spindle on top (dynamic per-frame element)
            if (_spindlePosition.HasValue)
            {
                var viewProj = _sceneCache.CachedViewProj;
                DrawSpindle(canvas, viewProj, width, height, _spindlePosition.Value);
            }

            canvas.Restore();
        }

        /// <summary>
        /// Draws the completed-segment overlay and selection highlight using pre-projected
        /// screen coordinates from the cache. No matrix math — just DrawLine calls with
        /// cached SKPoints. Typically redraws only a fraction of total segments.
        /// </summary>
        private void DrawProgressOverlay(SKCanvas canvas)
        {
            var starts = _sceneCache.ProjectedStarts;
            var ends = _sceneCache.ProjectedEnds;
            if (starts == null || ends == null || _toolpath == null) return;

            int segCount = _toolpath.Segments.Count;
            bool aa = _useAntiAlias;

            // Draw completed segments (overdraws on top of the static scene)
            if (_completedSegmentIndex > 0)
            {
                var completedPaint = Pick(CompletedPaints, aa);
                int limit = Math.Min(_completedSegmentIndex, segCount);
                for (int i = 0; i < limit; i++)
                {
                    if (starts[i].HasValue && ends[i].HasValue)
                        canvas.DrawLine(starts[i]!.Value, ends[i]!.Value, completedPaint);
                }
            }

            // Draw selected segment highlight
            if (_selectedSegmentIndex >= 0 && _selectedSegmentIndex < segCount)
            {
                var p1 = starts[_selectedSegmentIndex];
                var p2 = ends[_selectedSegmentIndex];
                if (p1.HasValue && p2.HasValue)
                    canvas.DrawLine(p1.Value, p2.Value, Pick(SelectedPaints, aa));
            }
        }

        /// <summary>
        /// Draws the static scene elements (grid, axes, toolpath segments in normal colors).
        /// Called by the cache when rebuilding — NOT called every frame.
        /// Uses pre-projected screen coordinates to avoid redundant projection work.
        /// Completed/selected overlays are drawn separately in DrawProgressOverlay().
        /// </summary>
        internal static void DrawStaticScene(SKCanvas canvas, Matrix4x4 viewProj,
            float width, float height, ToolpathData? toolpath,
            MachineSettings? machineSettings, Point3D? wco,
            SKPoint?[]? projectedStarts, SKPoint?[]? projectedEnds, bool useAntiAlias)
        {
            DrawGrid(canvas, viewProj, width, height, machineSettings, toolpath, wco, useAntiAlias);
            DrawAxes(canvas, viewProj, width, height, machineSettings, toolpath, wco, useAntiAlias);

            // Draw toolpath segments using pre-projected screen coordinates
            if (toolpath != null && projectedStarts != null && projectedEnds != null)
            {
                var rapidPaint = Pick(RapidPaints, useAntiAlias);
                var cutPaint = Pick(CutPaints, useAntiAlias);
                var traversePaint = Pick(TraversePaints, useAntiAlias);

                for (int i = 0; i < toolpath.Segments.Count; i++)
                {
                    var p1 = projectedStarts[i];
                    var p2 = projectedEnds[i];
                    if (!p1.HasValue || !p2.HasValue) continue;

                    var paint = toolpath.Segments[i].Type switch
                    {
                        MoveType.Rapid => rapidPaint,
                        MoveType.Cut => cutPaint,
                        MoveType.Traverse => traversePaint,
                        _ => rapidPaint
                    };

                    canvas.DrawLine(p1.Value, p2.Value, paint);
                }
            }
        }

        private static void DrawGrid(SKCanvas canvas, Matrix4x4 viewProj, float width, float height,
            MachineSettings? machineSettings, ToolpathData? toolpath, Point3D? wco, bool useAntiAlias)
        {
            float gridMinX, gridMaxX, gridMinY, gridMaxY, spacing;
            float gridZ = 0f;

            // Use machine dimensions ($130/$131/$132) if available
            // CNC Z convention: Z=0 is home (top of travel), work surface is at Z=-ZSize
            bool hasMachineBounds = machineSettings != null &&
                                    machineSettings.DisplayXSize > 0 &&
                                    machineSettings.DisplayYSize > 0;

            // WCO converts machine coords to work coords: WPos = MPos - WCO
            // The toolpath is in work coordinates, so the grid (machine coords) must
            // be shifted by -WCO to align with the toolpath.
            float wcoX = wco?.X ?? 0f;
            float wcoY = wco?.Y ?? 0f;
            float wcoZ = wco?.Z ?? 0f;

            if (hasMachineBounds)
            {
                // Machine grid in machine coords: X=[0, XSize], Y=[-YSize, 0]
                // Convert to work coords by subtracting WCO
                gridMinX = 0f - wcoX;
                gridMaxX = (float)machineSettings!.DisplayXSize - wcoX;
                gridMinY = -(float)machineSettings.DisplayYSize - wcoY;
                gridMaxY = 0f - wcoY;
                // Grid Z:
                //   File loaded   → bottom of toolpath (MinBounds.Z) so the model sits on the surface.
                //   No file       → machine table = -DisplayZSize in machine coords → work coords: -DisplayZSize - wcoZ
                gridZ = toolpath != null
                    ? toolpath.MinBounds.Z
                    : (machineSettings!.DisplayZSize > 0 ? -(float)machineSettings!.DisplayZSize - wcoZ : -wcoZ);
                float maxDim = MathF.Max((float)machineSettings.DisplayXSize, (float)machineSettings.DisplayYSize);
                spacing = CalculateGridSpacing(maxDim * 0.7f);
            }
            else if (toolpath != null && toolpath.Segments.Count > 0)
            {
                float gridExtent = toolpath.MaxDimension * 0.7f;
                spacing = CalculateGridSpacing(gridExtent);
                gridMinX = MathF.Floor((toolpath.MinBounds.X - spacing) / spacing) * spacing;
                gridMaxX = MathF.Ceiling((toolpath.MaxBounds.X + spacing) / spacing) * spacing;
                gridMinY = MathF.Floor((toolpath.MinBounds.Y - spacing) / spacing) * spacing;
                gridMaxY = MathF.Ceiling((toolpath.MaxBounds.Y + spacing) / spacing) * spacing;
                gridZ = toolpath.MinBounds.Z;
            }
            else
            {
                // Default grid when no toolpath or machine settings
                spacing = 100f;
                gridMinX = -600f;
                gridMaxX = 600f;
                gridMinY = -600f;
                gridMaxY = 600f;
            }

            var gridPaint = Pick(GridPaints, useAntiAlias);

            for (float x = gridMinX; x <= gridMaxX; x += spacing)
            {
                var p1 = ProjectToScreen(new Point3D(x, gridMinY, gridZ), viewProj, width, height);
                var p2 = ProjectToScreen(new Point3D(x, gridMaxY, gridZ), viewProj, width, height);
                if (p1.HasValue && p2.HasValue)
                    canvas.DrawLine(p1.Value, p2.Value, gridPaint);
            }

            for (float y = gridMinY; y <= gridMaxY; y += spacing)
            {
                var p1 = ProjectToScreen(new Point3D(gridMinX, y, gridZ), viewProj, width, height);
                var p2 = ProjectToScreen(new Point3D(gridMaxX, y, gridZ), viewProj, width, height);
                if (p1.HasValue && p2.HasValue)
                    canvas.DrawLine(p1.Value, p2.Value, gridPaint);
            }
        }

        private static void DrawAxes(SKCanvas canvas, Matrix4x4 viewProj, float width, float height,
            MachineSettings? machineSettings, ToolpathData? toolpath, Point3D? wco, bool useAntiAlias)
        {
            float axisLen;
            if (machineSettings != null && machineSettings.DisplayXSize > 0)
                axisLen = (float)Math.Max(machineSettings.DisplayXSize, machineSettings.DisplayYSize) * 0.1f;
            else if (toolpath != null && toolpath.Segments.Count > 0)
                axisLen = toolpath.MaxDimension * 0.15f;
            else
                axisLen = 50f;

            // WCO offset: axes are at machine origin (0,0,0), converted to work coords
            float wcoX = wco?.X ?? 0f;
            float wcoY = wco?.Y ?? 0f;
            float wcoZ = wco?.Z ?? 0f;

            // Machine origin in work coordinates
            float originX = -wcoX;
            float originY = -wcoY;
            float originZ = -wcoZ;

            // Axes sit on the floor:
            //   File loaded   → bottom of toolpath (MinBounds.Z)
            //   No file       → machine table = -DisplayZSize - wcoZ
            float gridZ = toolpath?.MinBounds.Z
                ?? ((machineSettings != null && machineSettings.DisplayZSize > 0)
                    ? -(float)machineSettings.DisplayZSize - wcoZ
                    : -wcoZ);

            // X/Y axes originate at machine origin corner on the work surface (Z=0 work coords)
            var xyOrigin = ProjectToScreen(new Point3D(originX, originY, gridZ), viewProj, width, height);
            if (!xyOrigin.HasValue) return;

            // X axis - Red (on work surface)
            var xEnd = ProjectToScreen(new Point3D(originX + axisLen, originY, gridZ), viewProj, width, height);
            if (xEnd.HasValue)
            {
                using var xPaint = new SKPaint { Color = SKColors.Red, StrokeWidth = 2.5f, IsAntialias = useAntiAlias };
                canvas.DrawLine(xyOrigin.Value, xEnd.Value, xPaint);
                canvas.DrawText("X", xEnd.Value.X + 4, xEnd.Value.Y - 4,
                    new SKFont(SKTypeface.Default, 14), xPaint);
            }

            // Y axis - Green (on work surface, extends in -Y direction: back-to-front)
            var yEnd = ProjectToScreen(new Point3D(originX, originY - axisLen, gridZ), viewProj, width, height);
            if (yEnd.HasValue)
            {
                using var yPaint = new SKPaint { Color = SKColors.Lime, StrokeWidth = 2.5f, IsAntialias = useAntiAlias };
                canvas.DrawLine(xyOrigin.Value, yEnd.Value, yPaint);
                canvas.DrawText("Y", yEnd.Value.X + 4, yEnd.Value.Y - 4,
                    new SKFont(SKTypeface.Default, 14), yPaint);
            }

            // Z axis - Blue (extends from work surface Z=0 up to machine home at originZ=-wcoZ)
            var zBottom = xyOrigin; // starts at grid/work surface
            var zTop = ProjectToScreen(new Point3D(originX, originY, originZ), viewProj, width, height);
            if (zTop.HasValue)
            {
                using var zPaint = new SKPaint { Color = new SKColor(80, 150, 255), StrokeWidth = 2.5f, IsAntialias = useAntiAlias };
                canvas.DrawLine(zBottom.Value, zTop.Value, zPaint);
                canvas.DrawText("Z", zTop.Value.X + 4, zTop.Value.Y - 4,
                    new SKFont(SKTypeface.Default, 14), zPaint);
            }
        }

        private void DrawSpindle(SKCanvas canvas, Matrix4x4 viewProj, float width, float height, Point3D pos)
        {
            // Scale spindle dimensions relative to the visible scene size
            // so it remains visible regardless of the toolpath dimensions.
            // Use toolpath extent first, then machine display dimensions (which respect $13 units),
            // then a small default fallback.
            float scaleRef;
            if (_toolpath != null && _toolpath.MaxDimension > 0)
                scaleRef = _toolpath.MaxDimension;
            else if (_machineSettings != null && _machineSettings.DisplayXSize > 0)
                scaleRef = (float)Math.Max(_machineSettings.DisplayXSize, _machineSettings.DisplayYSize);
            else
                scaleRef = 600f;

            float bitLength = scaleRef * 0.03f;     // Cone/bit tip length (~3% of scene)
            float shaftLength = scaleRef * 0.07f;    // Shaft/collet length (~7% of scene)

            // CNC convention: Z negative is down (into material), Z=0 is top surface
            // Spindle body extends ABOVE the tip in positive Z direction (away from material)
            var tip = pos;
            var bitTop = new Point3D(pos.X, pos.Y, pos.Z + bitLength);
            var shaftTop = new Point3D(pos.X, pos.Y, pos.Z + bitLength + shaftLength);

            var tipScreen = ProjectToScreen(tip, viewProj, width, height);
            var bitTopScreen = ProjectToScreen(bitTop, viewProj, width, height);
            var shaftTopScreen = ProjectToScreen(shaftTop, viewProj, width, height);

            if (!tipScreen.HasValue || !bitTopScreen.HasValue || !shaftTopScreen.HasValue) return;

            // Calculate axis direction and length in screen space
            float dx = shaftTopScreen.Value.X - tipScreen.Value.X;
            float dy = shaftTopScreen.Value.Y - tipScreen.Value.Y;
            float totalScreenLen = MathF.Sqrt(dx * dx + dy * dy);

            // If the spindle projects too small (e.g. looking straight down), use a fixed marker
            if (totalScreenLen < 5f)
            {
                DrawSpindleTopView(canvas, tipScreen.Value, _useAntiAlias);
                return;
            }

            // Try image-based rendering first
            if (_spindleImage != null)
            {
                DrawSpindleImage(canvas, tipScreen.Value, shaftTopScreen.Value, totalScreenLen);
                DrawCrosshair(canvas, tipScreen.Value, _useAntiAlias);
                return;
            }

            // Fallback: procedural drawing
            DrawSpindleProcedural(canvas, tipScreen.Value, bitTopScreen.Value, shaftTopScreen.Value,
                scaleRef, bitLength, _useAntiAlias);
        }

        /// <summary>
        /// Draws the spindle using a user-supplied image, scaled and rotated to match
        /// the 3D projection from tip to shaftTop. The image is centered on the crosshair
        /// (tip position) so the tool tip sits in the middle of the image.
        /// </summary>
        private void DrawSpindleImage(SKCanvas canvas, SKPoint tipScreen, SKPoint shaftTopScreen, float totalScreenLen)
        {
            var img = _spindleImage!;
            float imgAspect = (float)img.Width / img.Height;

            // Target height matches the projected spindle length (tip→shaftTop).
            // Width preserves the image aspect ratio.
            float targetHeight = Math.Clamp(totalScreenLen, 20f, 500f);
            float targetWidth = targetHeight * imgAspect;

            // Angle from tip to shaft-top in screen space.
            // The image's natural orientation has the shaft pointing in the -Y direction
            // (upward on screen), which corresponds to an angle of -PI/2.
            float angleDx = shaftTopScreen.X - tipScreen.X;
            float angleDy = shaftTopScreen.Y - tipScreen.Y;
            float angleRad = MathF.Atan2(angleDy, angleDx);

            // Rotation needed to align the image's natural "up" (-Y, angle=-PI/2)
            // with the actual tip→shaftTop direction: angleRad - (-PI/2) = angleRad + PI/2
            float rotation = angleRad + MathF.PI / 2f;

            canvas.Save();

            // Translate to the tip position (=crosshair), then rotate around it.
            canvas.Translate(tipScreen.X, tipScreen.Y);
            canvas.RotateRadians(rotation);

            // Position the image so its BOTTOM edge sits at (0,0) = the crosshair/tip.
            // The image extends upward (-Y) from the tip, just like the real spindle
            // extends above the bit tip. This puts the bit tip on the crosshair.
            var destRect = new SKRect(
                -targetWidth / 2f,
                -targetHeight,
                targetWidth / 2f,
                0f);

            using var paint = new SKPaint { IsAntialias = _useAntiAlias, FilterQuality = _useAntiAlias ? SKFilterQuality.Medium : SKFilterQuality.Low };
            canvas.DrawBitmap(img, destRect, paint);

            canvas.Restore();
        }

        /// <summary>
        /// Original procedural spindle drawing (cone + shaft + crosshair).
        /// Used as fallback when no spindle image is loaded.
        /// </summary>
        private static void DrawSpindleProcedural(SKCanvas canvas, SKPoint tipScreen,
            SKPoint bitTopScreen, SKPoint shaftTopScreen, float scaleRef, float bitLength, bool aa)
        {
            float bitRadius = scaleRef * 0.008f;
            float shaftRadius = scaleRef * 0.015f;

            float dx = bitTopScreen.X - tipScreen.X;
            float dy = bitTopScreen.Y - tipScreen.Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);

            if (len < 3f)
            {
                DrawSpindleTopView(canvas, tipScreen, aa);
                return;
            }

            // Perpendicular in screen space
            float perpX = -dy / len;
            float perpY = dx / len;

            // Scale radius based on projection
            float scale = len / bitLength;
            float screenBitRadius = Math.Clamp(bitRadius * scale, 3f, 40f);
            float screenShaftRadius = Math.Clamp(shaftRadius * scale, 5f, 60f);

            // Draw the cone (bit) as a triangle: tip → two base corners
            using var bitPath = new SKPath();
            bitPath.MoveTo(tipScreen);
            bitPath.LineTo(bitTopScreen.X + perpX * screenBitRadius,
                          bitTopScreen.Y + perpY * screenBitRadius);
            bitPath.LineTo(bitTopScreen.X - perpX * screenBitRadius,
                          bitTopScreen.Y - perpY * screenBitRadius);
            bitPath.Close();

            canvas.DrawPath(bitPath, Pick(SpindleBitPaints, aa));
            canvas.DrawPath(bitPath, Pick(SpindleOutlinePaints, aa));

            // Draw the shaft as a rectangle from bitTop to shaftTop
            float sdx = shaftTopScreen.X - bitTopScreen.X;
            float sdy = shaftTopScreen.Y - bitTopScreen.Y;
            float slen = MathF.Sqrt(sdx * sdx + sdy * sdy);
            if (slen >= 1f)
            {
                float sPerpX = -sdy / slen;
                float sPerpY = sdx / slen;

                using var shaftPath = new SKPath();
                shaftPath.MoveTo(bitTopScreen.X + sPerpX * screenShaftRadius,
                                bitTopScreen.Y + sPerpY * screenShaftRadius);
                shaftPath.LineTo(shaftTopScreen.X + sPerpX * screenShaftRadius,
                                shaftTopScreen.Y + sPerpY * screenShaftRadius);
                shaftPath.LineTo(shaftTopScreen.X - sPerpX * screenShaftRadius,
                                shaftTopScreen.Y - sPerpY * screenShaftRadius);
                shaftPath.LineTo(bitTopScreen.X - sPerpX * screenShaftRadius,
                                bitTopScreen.Y - sPerpY * screenShaftRadius);
                shaftPath.Close();

                canvas.DrawPath(shaftPath, Pick(SpindleShaftPaints, aa));
                canvas.DrawPath(shaftPath, Pick(SpindleOutlinePaints, aa));
            }

            // Draw a small crosshair at the tip for precision
            DrawCrosshair(canvas, tipScreen, aa);
        }

        private static void DrawSpindleTopView(SKCanvas canvas, SKPoint tipScreen, bool aa)
        {
            // When looking straight down, draw a circular indicator with crosshair
            float outerRadius = 12f;
            float innerRadius = 4f;

            using var outerPaint = new SKPaint
            {
                Color = new SKColor(220, 180, 50, 180),
                IsAntialias = aa,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f
            };
            using var innerPaint = new SKPaint
            {
                Color = new SKColor(220, 180, 50),
                IsAntialias = aa,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawCircle(tipScreen.X, tipScreen.Y, outerRadius, outerPaint);
            canvas.DrawCircle(tipScreen.X, tipScreen.Y, innerRadius, innerPaint);
            DrawCrosshair(canvas, tipScreen, aa);
        }

        private static void DrawCrosshair(SKCanvas canvas, SKPoint position, bool aa)
        {
            float crossSize = 8f;
            using var crossPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 0),
                StrokeWidth = 1.5f,
                IsAntialias = aa,
                Style = SKPaintStyle.Stroke
            };
            canvas.DrawLine(position.X - crossSize, position.Y,
                          position.X + crossSize, position.Y, crossPaint);
            canvas.DrawLine(position.X, position.Y - crossSize,
                          position.X, position.Y + crossSize, crossPaint);
        }

        internal static SKPoint? ProjectToScreen(Point3D point, Matrix4x4 viewProj, float width, float height)
        {
            var v = new Vector4(point.X, point.Y, point.Z, 1f);
            var clip = Vector4.Transform(v, viewProj);

            // Behind camera
            if (clip.W <= 0.001f) return null;

            // Perspective divide → NDC
            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;

            // NDC to screen (flip Y because screen Y goes down)
            float screenX = (ndcX + 1f) * 0.5f * width;
            float screenY = (1f - ndcY) * 0.5f * height;

            return new SKPoint(screenX, screenY);
        }

        private static float CalculateGridSpacing(float extent)
        {
            if (extent <= 0) return 10f;
            // Choose a "nice" spacing: 1, 2, 5, 10, 20, 50, 100...
            float raw = extent / 10f;
            float magnitude = MathF.Pow(10f, MathF.Floor(MathF.Log10(raw)));
            float normalized = raw / magnitude;

            if (normalized < 1.5f) return magnitude;
            if (normalized < 3.5f) return 2f * magnitude;
            if (normalized < 7.5f) return 5f * magnitude;
            return 10f * magnitude;
        }
    }
}
