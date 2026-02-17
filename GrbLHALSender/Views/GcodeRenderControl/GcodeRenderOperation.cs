using System;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using GrbLHALSender.Gcode;
using GrbLHALSender.Settings;
using SkiaSharp;

namespace GrbLHALSender.Views.GcodeRenderControl
{
    /// <summary>
    /// Caches the static toolpath scene (grid + axes + toolpath segments) as an SKPicture.
    /// The cache is invalidated when the camera, toolpath, machine settings, or control size change.
    /// When only the spindle position changes, the cached scene is replayed cheaply and only the
    /// spindle indicator is redrawn — avoiding the per-frame cost of projecting thousands of segments.
    /// </summary>
    public class ToolpathSceneCache
    {
        private SKPicture? _cachedScene;
        private Matrix4x4 _cachedViewProj;
        private float _cachedWidth;
        private float _cachedHeight;
        private ToolpathData? _cachedToolpath;
        private MachineSettings? _cachedMachineSettings;
        private Point3D? _cachedWco;
        private int _cachedCompletedIndex;
        private int _cachedSelectedIndex;

        public SKPicture? GetOrCreate(
            ToolpathData? toolpath, Camera3D camera, MachineSettings? machineSettings,
            Point3D? wco, float width, float height, int completedSegmentIndex = -1,
            int selectedSegmentIndex = -1)
        {
            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix(width, height);
            var viewProj = view * proj;

            // Check if cache is still valid
            if (_cachedScene != null &&
                _cachedViewProj == viewProj &&
                Math.Abs(_cachedWidth - width) < 0.5f &&
                Math.Abs(_cachedHeight - height) < 0.5f &&
                ReferenceEquals(_cachedToolpath, toolpath) &&
                ReferenceEquals(_cachedMachineSettings, machineSettings) &&
                Equals(_cachedWco, wco) &&
                _cachedCompletedIndex == completedSegmentIndex &&
                _cachedSelectedIndex == selectedSegmentIndex)
            {
                return _cachedScene;
            }

            // Rebuild the cached scene
            _cachedScene?.Dispose();

            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0, 0, width, height));

            GcodeRenderOperation.DrawStaticScene(canvas, viewProj, width, height, toolpath, machineSettings, wco, completedSegmentIndex, selectedSegmentIndex);

            _cachedScene = recorder.EndRecording();
            _cachedViewProj = viewProj;
            _cachedWidth = width;
            _cachedHeight = height;
            _cachedToolpath = toolpath;
            _cachedMachineSettings = machineSettings;
            _cachedWco = wco;
            _cachedCompletedIndex = completedSegmentIndex;
            _cachedSelectedIndex = selectedSegmentIndex;

            return _cachedScene;
        }

        public Matrix4x4 CachedViewProj => _cachedViewProj;

        public void Invalidate()
        {
            _cachedScene?.Dispose();
            _cachedScene = null;
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

        private static readonly SKPaint RapidPaint = new()
        {
            Color = new SKColor(0, 200, 0),
            StrokeWidth = 1f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        private static readonly SKPaint CutPaint = new()
        {
            Color = new SKColor(230, 40, 40),
            StrokeWidth = 1.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        private static readonly SKPaint TraversePaint = new()
        {
            Color = new SKColor(60, 120, 255),
            StrokeWidth = 1f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        private static readonly SKPaint CompletedPaint = new()
        {
            Color = new SKColor(203, 203, 212),
            StrokeWidth = 3f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        private static readonly SKPaint SelectedPaint = new()
        {
            Color = new SKColor(255, 255, 0),
            StrokeWidth = 3f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        private static readonly SKPaint GridPaint = new()
        {
            Color = new SKColor(60, 60, 60),
            StrokeWidth = 0.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        private static readonly SKPaint BackgroundPaint = new()
        {
            Color = new SKColor(25, 25, 30)
        };

        private static readonly SKPaint SpindleShaftPaint = new()
        {
            Color = new SKColor(180, 180, 190),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        private static readonly SKPaint SpindleBitPaint = new()
        {
            Color = new SKColor(220, 180, 50),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        private static readonly SKPaint SpindleOutlinePaint = new()
        {
            Color = new SKColor(100, 100, 110),
            StrokeWidth = 1.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        public GcodeRenderOperation(Rect bounds, ToolpathData? toolpath, Camera3D camera,
            ToolpathSceneCache sceneCache,
            Point3D? spindlePosition = null, MachineSettings? machineSettings = null,
            Point3D? workCoordinateOffset = null, int completedSegmentIndex = -1,
            int selectedSegmentIndex = -1)
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

            // Get or create the cached static scene (grid + axes + toolpath)
            var cachedScene = _sceneCache.GetOrCreate(
                _toolpath, _camera, _machineSettings, _wco, width, height, _completedSegmentIndex, _selectedSegmentIndex);

            if (cachedScene != null)
            {
                // Replay the cached scene — no per-segment projection needed
                canvas.DrawPicture(cachedScene);
            }

            // Draw spindle on top (this is the only per-frame dynamic element)
            if (_spindlePosition.HasValue)
            {
                var viewProj = _sceneCache.CachedViewProj;
                DrawSpindle(canvas, viewProj, width, height, _spindlePosition.Value);
            }

            canvas.Restore();
        }

        /// <summary>
        /// Draws the static scene elements (grid, axes, toolpath segments).
        /// Called by the cache when rebuilding — NOT called every frame.
        /// </summary>
        internal static void DrawStaticScene(SKCanvas canvas, Matrix4x4 viewProj,
            float width, float height, ToolpathData? toolpath,
            MachineSettings? machineSettings, Point3D? wco, int completedSegmentIndex = -1,
            int selectedSegmentIndex = -1)
        {
            DrawGrid(canvas, viewProj, width, height, machineSettings, toolpath, wco);
            DrawAxes(canvas, viewProj, width, height, machineSettings, toolpath, wco);

            // Draw toolpath segments
            if (toolpath != null)
            {
                for (int i = 0; i < toolpath.Segments.Count; i++)
                {
                    var segment = toolpath.Segments[i];
                    var p1 = ProjectToScreen(segment.Start, viewProj, width, height);
                    var p2 = ProjectToScreen(segment.End, viewProj, width, height);

                    if (!p1.HasValue || !p2.HasValue) continue;

                    SKPaint paint;
                    if (selectedSegmentIndex >= 0 && i == selectedSegmentIndex)
                    {
                        paint = SelectedPaint;
                    }
                    else if (completedSegmentIndex >= 0 && i < completedSegmentIndex)
                    {
                        paint = CompletedPaint;
                    }
                    else
                    {
                        paint = segment.Type switch
                        {
                            MoveType.Rapid => RapidPaint,
                            MoveType.Cut => CutPaint,
                            MoveType.Traverse => TraversePaint,
                            _ => RapidPaint
                        };
                    }

                    canvas.DrawLine(p1.Value, p2.Value, paint);
                }
            }
        }

        private static void DrawGrid(SKCanvas canvas, Matrix4x4 viewProj, float width, float height,
            MachineSettings? machineSettings, ToolpathData? toolpath, Point3D? wco)
        {
            float gridMinX, gridMaxX, gridMinY, gridMaxY, spacing;
            float gridZ = 0f;

            // Use machine dimensions ($130/$131/$132) if available
            // CNC Z convention: Z=0 is home (top of travel), work surface is at Z=-ZSize
            bool hasMachineBounds = machineSettings != null &&
                                    machineSettings.XSize > 0 &&
                                    machineSettings.YSize > 0;

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
                gridMaxX = (float)machineSettings!.XSize - wcoX;
                gridMinY = -(float)machineSettings.YSize - wcoY;
                gridMaxY = 0f - wcoY;
                // Grid Z: work surface is at machine Z=-ZSize, in work coords: -ZSize - wcoZ
                gridZ = machineSettings.ZSize > 0 ? -(float)machineSettings.ZSize - wcoZ : -wcoZ;
                float maxDim = MathF.Max((float)machineSettings.XSize, (float)machineSettings.YSize);
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

            for (float x = gridMinX; x <= gridMaxX; x += spacing)
            {
                var p1 = ProjectToScreen(new Point3D(x, gridMinY, gridZ), viewProj, width, height);
                var p2 = ProjectToScreen(new Point3D(x, gridMaxY, gridZ), viewProj, width, height);
                if (p1.HasValue && p2.HasValue)
                    canvas.DrawLine(p1.Value, p2.Value, GridPaint);
            }

            for (float y = gridMinY; y <= gridMaxY; y += spacing)
            {
                var p1 = ProjectToScreen(new Point3D(gridMinX, y, gridZ), viewProj, width, height);
                var p2 = ProjectToScreen(new Point3D(gridMaxX, y, gridZ), viewProj, width, height);
                if (p1.HasValue && p2.HasValue)
                    canvas.DrawLine(p1.Value, p2.Value, GridPaint);
            }
        }

        private static void DrawAxes(SKCanvas canvas, Matrix4x4 viewProj, float width, float height,
            MachineSettings? machineSettings, ToolpathData? toolpath, Point3D? wco)
        {
            float axisLen;
            if (machineSettings != null && machineSettings.XSize > 0)
                axisLen = (float)Math.Max(machineSettings.XSize, machineSettings.YSize) * 0.1f;
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

            // CNC Z convention: Z=0 is home (top), work surface is at Z=-ZSize
            // Work surface in work coords: -ZSize - wcoZ
            float gridZ = (machineSettings != null && machineSettings.ZSize > 0)
                ? -(float)machineSettings.ZSize - wcoZ
                : originZ;

            // X/Y axes originate at back-left corner on the work surface
            var xyOrigin = ProjectToScreen(new Point3D(originX, originY, gridZ), viewProj, width, height);
            if (!xyOrigin.HasValue) return;

            // X axis - Red (on work surface)
            var xEnd = ProjectToScreen(new Point3D(originX + axisLen, originY, gridZ), viewProj, width, height);
            if (xEnd.HasValue)
            {
                using var xPaint = new SKPaint { Color = SKColors.Red, StrokeWidth = 2.5f, IsAntialias = true };
                canvas.DrawLine(xyOrigin.Value, xEnd.Value, xPaint);
                canvas.DrawText("X", xEnd.Value.X + 4, xEnd.Value.Y - 4,
                    new SKFont(SKTypeface.Default, 14), xPaint);
            }

            // Y axis - Green (on work surface, extends in -Y direction: back-to-front)
            var yEnd = ProjectToScreen(new Point3D(originX, originY - axisLen, gridZ), viewProj, width, height);
            if (yEnd.HasValue)
            {
                using var yPaint = new SKPaint { Color = SKColors.Lime, StrokeWidth = 2.5f, IsAntialias = true };
                canvas.DrawLine(xyOrigin.Value, yEnd.Value, yPaint);
                canvas.DrawText("Y", yEnd.Value.X + 4, yEnd.Value.Y - 4,
                    new SKFont(SKTypeface.Default, 14), yPaint);
            }

            // Z axis - Blue (extends from work surface up to machine home Z=0)
            // Machine home Z=0 in work coords = -wcoZ
            var zBottom = xyOrigin; // starts at grid/work surface
            var zTop = ProjectToScreen(new Point3D(originX, originY, originZ), viewProj, width, height);
            if (zTop.HasValue)
            {
                using var zPaint = new SKPaint { Color = new SKColor(80, 150, 255), StrokeWidth = 2.5f, IsAntialias = true };
                canvas.DrawLine(zBottom.Value, zTop.Value, zPaint);
                canvas.DrawText("Z", zTop.Value.X + 4, zTop.Value.Y - 4,
                    new SKFont(SKTypeface.Default, 14), zPaint);
            }
        }

        private void DrawSpindle(SKCanvas canvas, Matrix4x4 viewProj, float width, float height, Point3D pos)
        {
            // Scale spindle dimensions relative to the visible scene size
            // so it remains visible regardless of the toolpath dimensions
            float scaleRef = _toolpath != null && _toolpath.MaxDimension > 0
                ? _toolpath.MaxDimension
                : 600f;

            float bitLength = scaleRef * 0.03f;     // Cone/bit tip length (~3% of scene)
            float shaftLength = scaleRef * 0.07f;    // Shaft/collet length (~7% of scene)
            float bitRadius = scaleRef * 0.008f;     // Bit radius at base of cone
            float shaftRadius = scaleRef * 0.015f;   // Shaft radius

            // CNC convention: Z negative is down (into material), Z=0 is top surface
            // Spindle body extends ABOVE the tip in positive Z direction (away from material)
            var tip = pos;
            var bitTop = new Point3D(pos.X, pos.Y, pos.Z + bitLength);
            var shaftTop = new Point3D(pos.X, pos.Y, pos.Z + bitLength + shaftLength);

            var tipScreen = ProjectToScreen(tip, viewProj, width, height);
            var bitTopScreen = ProjectToScreen(bitTop, viewProj, width, height);
            var shaftTopScreen = ProjectToScreen(shaftTop, viewProj, width, height);

            if (!tipScreen.HasValue || !bitTopScreen.HasValue || !shaftTopScreen.HasValue) return;

            // Calculate perpendicular direction for width in screen space
            float dx = bitTopScreen.Value.X - tipScreen.Value.X;
            float dy = bitTopScreen.Value.Y - tipScreen.Value.Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);

            // If the bit projects too small (e.g. looking straight down), use a fixed screen-space size
            if (len < 3f)
            {
                // Draw a simple marker when viewed from directly above
                DrawSpindleTopView(canvas, tipScreen.Value);
                return;
            }

            // Perpendicular in screen space
            float perpX = -dy / len;
            float perpY = dx / len;

            // Scale radius based on projection
            float scale = len / bitLength;
            float screenBitRadius = bitRadius * scale;
            float screenShaftRadius = shaftRadius * scale;

            // Clamp radius to reasonable screen sizes
            screenBitRadius = Math.Clamp(screenBitRadius, 3f, 40f);
            screenShaftRadius = Math.Clamp(screenShaftRadius, 5f, 60f);

            // Draw the cone (bit) as a triangle: tip → two base corners
            using var bitPath = new SKPath();
            bitPath.MoveTo(tipScreen.Value);
            bitPath.LineTo(bitTopScreen.Value.X + perpX * screenBitRadius,
                          bitTopScreen.Value.Y + perpY * screenBitRadius);
            bitPath.LineTo(bitTopScreen.Value.X - perpX * screenBitRadius,
                          bitTopScreen.Value.Y - perpY * screenBitRadius);
            bitPath.Close();

            canvas.DrawPath(bitPath, SpindleBitPaint);
            canvas.DrawPath(bitPath, SpindleOutlinePaint);

            // Draw the shaft as a rectangle from bitTop to shaftTop
            float sdx = shaftTopScreen.Value.X - bitTopScreen.Value.X;
            float sdy = shaftTopScreen.Value.Y - bitTopScreen.Value.Y;
            float slen = MathF.Sqrt(sdx * sdx + sdy * sdy);
            if (slen >= 1f)
            {
                float sPerpX = -sdy / slen;
                float sPerpY = sdx / slen;

                using var shaftPath = new SKPath();
                shaftPath.MoveTo(bitTopScreen.Value.X + sPerpX * screenShaftRadius,
                                bitTopScreen.Value.Y + sPerpY * screenShaftRadius);
                shaftPath.LineTo(shaftTopScreen.Value.X + sPerpX * screenShaftRadius,
                                shaftTopScreen.Value.Y + sPerpY * screenShaftRadius);
                shaftPath.LineTo(shaftTopScreen.Value.X - sPerpX * screenShaftRadius,
                                shaftTopScreen.Value.Y - sPerpY * screenShaftRadius);
                shaftPath.LineTo(bitTopScreen.Value.X - sPerpX * screenShaftRadius,
                                bitTopScreen.Value.Y - sPerpY * screenShaftRadius);
                shaftPath.Close();

                canvas.DrawPath(shaftPath, SpindleShaftPaint);
                canvas.DrawPath(shaftPath, SpindleOutlinePaint);
            }

            // Draw a small crosshair at the tip for precision
            DrawCrosshair(canvas, tipScreen.Value);
        }

        private static void DrawSpindleTopView(SKCanvas canvas, SKPoint tipScreen)
        {
            // When looking straight down, draw a circular indicator with crosshair
            float outerRadius = 12f;
            float innerRadius = 4f;

            using var outerPaint = new SKPaint
            {
                Color = new SKColor(220, 180, 50, 180),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f
            };
            using var innerPaint = new SKPaint
            {
                Color = new SKColor(220, 180, 50),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawCircle(tipScreen.X, tipScreen.Y, outerRadius, outerPaint);
            canvas.DrawCircle(tipScreen.X, tipScreen.Y, innerRadius, innerPaint);
            DrawCrosshair(canvas, tipScreen);
        }

        private static void DrawCrosshair(SKCanvas canvas, SKPoint position)
        {
            float crossSize = 8f;
            using var crossPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 0),
                StrokeWidth = 1.5f,
                IsAntialias = true,
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
