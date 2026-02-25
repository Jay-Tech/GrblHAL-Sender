using GrbLHALSender.Gcode;
using GrbLHALSender.Settings;
using Silk.NET.OpenGLES;
using System;
using System.Runtime.InteropServices;

namespace GrbLHALSender.Views.GcodeGlRenderControl
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct LineVertex
    {
        public float X, Y, Z;      // position
        public float R, G, B, A;   // color
    }

    /// <summary>
    /// World-space positions of axis endpoints, used to project label positions to screen.
    /// </summary>
    internal struct AxisEndpoints
    {
        public Point3D XEnd;
        public Point3D YEnd;
        public Point3D ZEnd;
    }

    internal sealed class GlToolpathRenderer : IDisposable
    {
        private readonly GL _gl;

        // Toolpath
        private uint _toolpathVao;
        private uint _toolpathVbo;
        private int _toolpathVertexCount;

        // Grid
        private uint _gridVao;
        private uint _gridVbo;
        private int _gridVertexCount;

        // Axes
        private uint _axesVao;
        private uint _axesVbo;
        private int _axesVertexCount;

        public GlToolpathRenderer(GL gl)
        {
            _gl = gl;
            _toolpathVao = CreateVao(gl, out _toolpathVbo);
            _gridVao = CreateVao(gl, out _gridVbo);
            _axesVao = CreateVao(gl, out _axesVbo);
        }

        private static uint CreateVao(GL gl, out uint vbo)
        {
            uint vao = gl.GenVertexArray();
            vbo = gl.GenBuffer();

            gl.BindVertexArray(vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

            uint stride = (uint)Marshal.SizeOf<LineVertex>();
            // location 0: position (3 floats at offset 0)
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            // location 1: color (4 floats at offset 12)
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 12);

            gl.BindVertexArray(0);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

            return vao;
        }

        // =====================================================================
        // Static vertex-building methods (pure CPU — safe to call from any thread)
        // =====================================================================

        /// <summary>
        /// Builds toolpath vertex array from segment data. Called on UI thread.
        /// </summary>
        public static LineVertex[] BuildToolpathVertices(ToolpathData toolpath)
        {
            int segCount = toolpath.Segments.Count;
            var vertices = new LineVertex[segCount * 2];

            for (int i = 0; i < segCount; i++)
            {
                var seg = toolpath.Segments[i];
                GetMoveColor(seg.Type, out float r, out float g, out float b);

                vertices[i * 2] = new LineVertex
                {
                    X = seg.Start.X, Y = seg.Start.Y, Z = seg.Start.Z,
                    R = r, G = g, B = b, A = 1f
                };
                vertices[i * 2 + 1] = new LineVertex
                {
                    X = seg.End.X, Y = seg.End.Y, Z = seg.End.Z,
                    R = r, G = g, B = b, A = 1f
                };
            }

            return vertices;
        }

        /// <summary>
        /// Builds grid vertex array. Called on UI thread. Returns (vertices, usedCount).
        /// </summary>
        public static (LineVertex[] vertices, int count) BuildGridVertices(
            ToolpathData? toolpath, MachineSettings? machineSettings, Point3D? wco)
        {
            float gridMinX, gridMaxX, gridMinY, gridMaxY, spacing;
            float gridZ = 0f;

            bool hasMachineBounds = machineSettings != null &&
                                    machineSettings.DisplayXSize > 0 &&
                                    machineSettings.DisplayYSize > 0;

            float wcoX = wco?.X ?? 0f;
            float wcoY = wco?.Y ?? 0f;
            float wcoZ = wco?.Z ?? 0f;

            if (hasMachineBounds)
            {
                gridMinX = 0f - wcoX;
                gridMaxX = (float)machineSettings!.DisplayXSize - wcoX;
                gridMinY = -(float)machineSettings.DisplayYSize - wcoY;
                gridMaxY = 0f - wcoY;
                gridZ = toolpath != null
                    ? toolpath.MinBounds.Z
                    : (machineSettings.DisplayZSize > 0 ? -(float)machineSettings.DisplayZSize - wcoZ : -wcoZ);
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
                spacing = 100f;
                gridMinX = -600f;
                gridMaxX = 600f;
                gridMinY = -600f;
                gridMaxY = 600f;
            }

            // Count lines
            int xLines = 0;
            for (float x = gridMinX; x <= gridMaxX; x += spacing) xLines++;
            int yLines = 0;
            for (float y = gridMinY; y <= gridMaxY; y += spacing) yLines++;

            var vertices = new LineVertex[(xLines + yLines) * 2];
            int idx = 0;

            float gr = 60f / 255f, gg = 60f / 255f, gb = 60f / 255f;

            for (float x = gridMinX; x <= gridMaxX; x += spacing)
            {
                vertices[idx++] = new LineVertex { X = x, Y = gridMinY, Z = gridZ, R = gr, G = gg, B = gb, A = 1f };
                vertices[idx++] = new LineVertex { X = x, Y = gridMaxY, Z = gridZ, R = gr, G = gg, B = gb, A = 1f };
            }

            for (float y = gridMinY; y <= gridMaxY; y += spacing)
            {
                vertices[idx++] = new LineVertex { X = gridMinX, Y = y, Z = gridZ, R = gr, G = gg, B = gb, A = 1f };
                vertices[idx++] = new LineVertex { X = gridMaxX, Y = y, Z = gridZ, R = gr, G = gg, B = gb, A = 1f };
            }

            return (vertices, idx);
        }

        /// <summary>
        /// Builds axes vertex array. Called on UI thread. Returns (vertices, count, endpoints).
        /// The endpoints are the world-space positions of each axis tip, used for label projection.
        /// </summary>
        public static (LineVertex[] vertices, int count, AxisEndpoints endpoints) BuildAxesVertices(
            ToolpathData? toolpath, MachineSettings? machineSettings, Point3D? wco)
        {
            float axisLen;
            if (machineSettings != null && machineSettings.DisplayXSize > 0)
                axisLen = (float)Math.Max(machineSettings.DisplayXSize, machineSettings.DisplayYSize) * 0.1f;
            else if (toolpath != null && toolpath.Segments.Count > 0)
                axisLen = toolpath.MaxDimension * 0.15f;
            else
                axisLen = 50f;

            float wcoX = wco?.X ?? 0f;
            float wcoY = wco?.Y ?? 0f;
            float wcoZ = wco?.Z ?? 0f;

            float originX = -wcoX;
            float originY = -wcoY;
            float originZ = -wcoZ;

            float gridZ = toolpath?.MinBounds.Z
                ?? ((machineSettings != null && machineSettings.DisplayZSize > 0)
                    ? -(float)machineSettings.DisplayZSize - wcoZ
                    : -wcoZ);

            // 3 axes = 6 vertices
            var vertices = new LineVertex[6];

            // X axis - Red
            vertices[0] = new LineVertex { X = originX, Y = originY, Z = gridZ, R = 1f, G = 0f, B = 0f, A = 1f };
            vertices[1] = new LineVertex { X = originX + axisLen, Y = originY, Z = gridZ, R = 1f, G = 0f, B = 0f, A = 1f };

            // Y axis - Green (extends in -Y: back-to-front)
            vertices[2] = new LineVertex { X = originX, Y = originY, Z = gridZ, R = 0f, G = 1f, B = 0f, A = 1f };
            vertices[3] = new LineVertex { X = originX, Y = originY - axisLen, Z = gridZ, R = 0f, G = 1f, B = 0f, A = 1f };

            // Z axis - Blue
            vertices[4] = new LineVertex { X = originX, Y = originY, Z = gridZ, R = 80f / 255f, G = 150f / 255f, B = 1f, A = 1f };
            vertices[5] = new LineVertex { X = originX, Y = originY, Z = originZ, R = 80f / 255f, G = 150f / 255f, B = 1f, A = 1f };

            var endpoints = new AxisEndpoints
            {
                XEnd = new Point3D(originX + axisLen, originY, gridZ),
                YEnd = new Point3D(originX, originY - axisLen, gridZ),
                ZEnd = new Point3D(originX, originY, originZ)
            };

            return (vertices, 6, endpoints);
        }

        // =====================================================================
        // GPU upload methods (must be called on compositor/GL thread only)
        // =====================================================================

        /// <summary>
        /// Uploads pre-built toolpath vertices to GPU. Called on compositor thread.
        /// </summary>
        public void UploadToolpath(LineVertex[]? vertices)
        {
            if (vertices == null || vertices.Length == 0)
            {
                _toolpathVertexCount = 0;
                return;
            }

            _toolpathVertexCount = vertices.Length;
            UploadBuffer(_toolpathVbo, vertices, BufferUsageARB.StaticDraw);
        }

        /// <summary>
        /// Uploads pre-built grid vertices to GPU. Called on compositor thread.
        /// </summary>
        public void UploadGrid(LineVertex[]? vertices, int count)
        {
            if (vertices == null || count <= 0)
            {
                _gridVertexCount = 0;
                return;
            }

            _gridVertexCount = count;
            UploadBuffer(_gridVbo, vertices.AsSpan(0, count), BufferUsageARB.StaticDraw);
        }

        /// <summary>
        /// Uploads pre-built axes vertices to GPU. Called on compositor thread.
        /// </summary>
        public void UploadAxes(LineVertex[]? vertices, int count)
        {
            if (vertices == null || count <= 0)
            {
                _axesVertexCount = 0;
                return;
            }

            _axesVertexCount = count;
            UploadBuffer(_axesVbo, vertices.AsSpan(0, count), BufferUsageARB.StaticDraw);
        }

        private unsafe void UploadBuffer(uint vbo, Span<LineVertex> vertices, BufferUsageARB usage)
        {
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            fixed (LineVertex* ptr = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * Marshal.SizeOf<LineVertex>()), ptr, usage);
            }
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        }

        // =====================================================================
        // Draw methods (must be called on compositor/GL thread only)
        // =====================================================================

        public void DrawGrid(GL gl)
        {
            if (_gridVertexCount == 0) return;
            gl.BindVertexArray(_gridVao);
            gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertexCount);
            gl.BindVertexArray(0);
        }

        public void DrawAxes(GL gl)
        {
            if (_axesVertexCount == 0) return;
            gl.BindVertexArray(_axesVao);
            gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_axesVertexCount);
            gl.BindVertexArray(0);
        }

        public void DrawToolpath(GL gl)
        {
            if (_toolpathVertexCount == 0) return;
            gl.BindVertexArray(_toolpathVao);
            gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_toolpathVertexCount);
            gl.BindVertexArray(0);
        }

        /// <summary>
        /// Draws a range of toolpath segments. Used for completed overlay and selection highlight.
        /// </summary>
        public void DrawToolpathRange(GL gl, int segmentStart, int segmentCount)
        {
            if (_toolpathVertexCount == 0 || segmentCount <= 0) return;
            int firstVertex = segmentStart * 2;
            int vertexCount = segmentCount * 2;
            if (firstVertex + vertexCount > _toolpathVertexCount)
                vertexCount = _toolpathVertexCount - firstVertex;
            if (vertexCount <= 0) return;

            gl.BindVertexArray(_toolpathVao);
            gl.DrawArrays(PrimitiveType.Lines, firstVertex, (uint)vertexCount);
            gl.BindVertexArray(0);
        }

        private static void GetMoveColor(MoveType type, out float r, out float g, out float b)
        {
            switch (type)
            {
                case MoveType.Rapid:
                    r = 0f; g = 200f / 255f; b = 0f;
                    break;
                case MoveType.Cut:
                    r = 230f / 255f; g = 40f / 255f; b = 40f / 255f;
                    break;
                case MoveType.Traverse:
                    r = 60f / 255f; g = 120f / 255f; b = 1f;
                    break;
                default:
                    r = 0f; g = 200f / 255f; b = 0f;
                    break;
            }
        }

        private static float CalculateGridSpacing(float extent)
        {
            if (extent <= 0) return 10f;
            float raw = extent / 10f;
            float magnitude = MathF.Pow(10f, MathF.Floor(MathF.Log10(raw)));
            float normalized = raw / magnitude;

            if (normalized < 1.5f) return magnitude;
            if (normalized < 3.5f) return 2f * magnitude;
            if (normalized < 7.5f) return 5f * magnitude;
            return 10f * magnitude;
        }

        public void Dispose()
        {
            if (_toolpathVbo != 0) { _gl.DeleteBuffer(_toolpathVbo); _toolpathVbo = 0; }
            if (_toolpathVao != 0) { _gl.DeleteVertexArray(_toolpathVao); _toolpathVao = 0; }
            if (_gridVbo != 0) { _gl.DeleteBuffer(_gridVbo); _gridVbo = 0; }
            if (_gridVao != 0) { _gl.DeleteVertexArray(_gridVao); _gridVao = 0; }
            if (_axesVbo != 0) { _gl.DeleteBuffer(_axesVbo); _axesVbo = 0; }
            if (_axesVao != 0) { _gl.DeleteVertexArray(_axesVao); _axesVao = 0; }
        }
    }
}
