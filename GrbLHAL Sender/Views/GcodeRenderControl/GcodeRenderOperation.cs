using System;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using GrbLHAL_Sender.Gcode;
using SkiaSharp;

namespace GrbLHAL_Sender.Views.GcodeRenderControl
{
    public class GcodeRenderOperation : ICustomDrawOperation
    {
        private readonly ToolpathData? _toolpath;
        private readonly Camera3D _camera;

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

        public GcodeRenderOperation(Rect bounds, ToolpathData? toolpath, Camera3D camera)
        {
            Bounds = bounds;
            _toolpath = toolpath;
            _camera = camera;
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

            // Build view-projection matrix
            var view = _camera.GetViewMatrix();
            var proj = _camera.GetProjectionMatrix(width, height);
            var viewProj = view * proj;

            // Draw grid at Z=0
            DrawGrid(canvas, viewProj, width, height);

            // Draw axis indicators
            DrawAxes(canvas, viewProj, width, height);

            // Draw toolpath segments if loaded
            if (_toolpath != null)
            {
                foreach (var segment in _toolpath.Segments)
                {
                    var p1 = ProjectToScreen(segment.Start, viewProj, width, height);
                    var p2 = ProjectToScreen(segment.End, viewProj, width, height);

                    if (!p1.HasValue || !p2.HasValue) continue;

                    var paint = segment.Type switch
                    {
                        MoveType.Rapid => RapidPaint,
                        MoveType.Cut => CutPaint,
                        MoveType.Traverse => TraversePaint,
                        _ => RapidPaint
                    };

                    canvas.DrawLine(p1.Value, p2.Value, paint);
                }
            }

            canvas.Restore();
        }

        private void DrawGrid(SKCanvas canvas, Matrix4x4 viewProj, float width, float height)
        {
            float gridMinX, gridMaxX, gridMinY, gridMaxY, spacing;

            if (_toolpath != null && _toolpath.Segments.Count > 0)
            {
                float gridExtent = _toolpath.MaxDimension * 0.7f;
                spacing = CalculateGridSpacing(gridExtent);
                gridMinX = MathF.Floor((_toolpath.MinBounds.X - spacing) / spacing) * spacing;
                gridMaxX = MathF.Ceiling((_toolpath.MaxBounds.X + spacing) / spacing) * spacing;
                gridMinY = MathF.Floor((_toolpath.MinBounds.Y - spacing) / spacing) * spacing;
                gridMaxY = MathF.Ceiling((_toolpath.MaxBounds.Y + spacing) / spacing) * spacing;
            }
            else
            {
                // Default grid when no toolpath loaded
                spacing = 100f;
                gridMinX = -600f;
                gridMaxX = 600f;
                gridMinY = -600f;
                gridMaxY = 600f;
            }

            for (float x = gridMinX; x <= gridMaxX; x += spacing)
            {
                var p1 = ProjectToScreen(new Point3D(x, gridMinY, 0), viewProj, width, height);
                var p2 = ProjectToScreen(new Point3D(x, gridMaxY, 0), viewProj, width, height);
                if (p1.HasValue && p2.HasValue)
                    canvas.DrawLine(p1.Value, p2.Value, GridPaint);
            }

            for (float y = gridMinY; y <= gridMaxY; y += spacing)
            {
                var p1 = ProjectToScreen(new Point3D(gridMinX, y, 0), viewProj, width, height);
                var p2 = ProjectToScreen(new Point3D(gridMaxX, y, 0), viewProj, width, height);
                if (p1.HasValue && p2.HasValue)
                    canvas.DrawLine(p1.Value, p2.Value, GridPaint);
            }
        }

        private void DrawAxes(SKCanvas canvas, Matrix4x4 viewProj, float width, float height)
        {
            float axisLen = _toolpath != null && _toolpath.Segments.Count > 0
                ? _toolpath.MaxDimension * 0.15f
                : 50f;
            var origin = ProjectToScreen(new Point3D(0, 0, 0), viewProj, width, height);
            if (!origin.HasValue) return;

            // X axis - Red
            var xEnd = ProjectToScreen(new Point3D(axisLen, 0, 0), viewProj, width, height);
            if (xEnd.HasValue)
            {
                using var xPaint = new SKPaint { Color = SKColors.Red, StrokeWidth = 2.5f, IsAntialias = true };
                canvas.DrawLine(origin.Value, xEnd.Value, xPaint);
                canvas.DrawText("X", xEnd.Value.X + 4, xEnd.Value.Y - 4,
                    new SKFont(SKTypeface.Default, 14), xPaint);
            }

            // Y axis - Green
            var yEnd = ProjectToScreen(new Point3D(0, axisLen, 0), viewProj, width, height);
            if (yEnd.HasValue)
            {
                using var yPaint = new SKPaint { Color = SKColors.Lime, StrokeWidth = 2.5f, IsAntialias = true };
                canvas.DrawLine(origin.Value, yEnd.Value, yPaint);
                canvas.DrawText("Y", yEnd.Value.X + 4, yEnd.Value.Y - 4,
                    new SKFont(SKTypeface.Default, 14), yPaint);
            }

            // Z axis - Blue
            var zEnd = ProjectToScreen(new Point3D(0, 0, axisLen), viewProj, width, height);
            if (zEnd.HasValue)
            {
                using var zPaint = new SKPaint { Color = new SKColor(80, 150, 255), StrokeWidth = 2.5f, IsAntialias = true };
                canvas.DrawLine(origin.Value, zEnd.Value, zPaint);
                canvas.DrawText("Z", zEnd.Value.X + 4, zEnd.Value.Y - 4,
                    new SKFont(SKTypeface.Default, 14), zPaint);
            }
        }

        private static SKPoint? ProjectToScreen(Point3D point, Matrix4x4 viewProj, float width, float height)
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
