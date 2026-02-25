using GrbLHALSender.Gcode;
using Silk.NET.OpenGLES;
using System;
using System.Runtime.InteropServices;

namespace GrbLHALSender.Views.GcodeGlRenderControl
{
    internal sealed class GlSpindleRenderer : IDisposable
    {
        private const int Slices = 12;

        private readonly GL _gl;

        // Spindle body (triangles for cone + shaft)
        private uint _bodyVao;
        private uint _bodyVbo;
        private int _bodyVertexCount;

        // Crosshair (2 line segments = 4 vertices)
        private uint _crosshairVao;
        private uint _crosshairVbo;
        private int _crosshairVertexCount;

        // Bit color: (220, 180, 50)
        private const float BitR = 220f / 255f, BitG = 180f / 255f, BitB = 50f / 255f;
        // Shaft color: (180, 180, 190)
        private const float ShaftR = 180f / 255f, ShaftG = 180f / 255f, ShaftB = 190f / 255f;
        // Crosshair color: yellow
        private const float CrossR = 1f, CrossG = 1f, CrossB = 0f;

        public GlSpindleRenderer(GL gl)
        {
            _gl = gl;
            _bodyVao = CreateVao(gl, out _bodyVbo);
            _crosshairVao = CreateVao(gl, out _crosshairVbo);
        }

        private static uint CreateVao(GL gl, out uint vbo)
        {
            uint vao = gl.GenVertexArray();
            vbo = gl.GenBuffer();

            gl.BindVertexArray(vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

            uint stride = (uint)Marshal.SizeOf<LineVertex>();
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 12);

            gl.BindVertexArray(0);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

            return vao;
        }

        public void Update(Point3D pos, float scaleRef)
        {
            float bitLength = scaleRef * 0.03f;
            float shaftLength = scaleRef * 0.07f;
            float bitRadius = scaleRef * 0.008f;
            float shaftRadius = scaleRef * 0.015f;

            float tipZ = pos.Z;
            float bitTopZ = pos.Z + bitLength;
            float shaftTopZ = pos.Z + bitLength + shaftLength;

            // Cone: Slices triangles (3 vertices each)
            // Shaft cylinder: Slices * 2 triangles (quads as 2 tri each, 6 vertices per quad)
            int coneVerts = Slices * 3;
            int shaftVerts = Slices * 6;
            var body = new LineVertex[coneVerts + shaftVerts];
            int idx = 0;

            float step = MathF.PI * 2f / Slices;

            // Cone (triangle fan from tip to base circle at bitTop)
            for (int i = 0; i < Slices; i++)
            {
                float a0 = i * step;
                float a1 = (i + 1) * step;
                float cx0 = pos.X + bitRadius * MathF.Cos(a0);
                float cy0 = pos.Y + bitRadius * MathF.Sin(a0);
                float cx1 = pos.X + bitRadius * MathF.Cos(a1);
                float cy1 = pos.Y + bitRadius * MathF.Sin(a1);

                body[idx++] = new LineVertex { X = pos.X, Y = pos.Y, Z = tipZ, R = BitR, G = BitG, B = BitB, A = 1f };
                body[idx++] = new LineVertex { X = cx0, Y = cy0, Z = bitTopZ, R = BitR, G = BitG, B = BitB, A = 1f };
                body[idx++] = new LineVertex { X = cx1, Y = cy1, Z = bitTopZ, R = BitR, G = BitG, B = BitB, A = 1f };
            }

            // Shaft cylinder (quads from bitTop to shaftTop)
            for (int i = 0; i < Slices; i++)
            {
                float a0 = i * step;
                float a1 = (i + 1) * step;
                float bx0 = pos.X + shaftRadius * MathF.Cos(a0);
                float by0 = pos.Y + shaftRadius * MathF.Sin(a0);
                float bx1 = pos.X + shaftRadius * MathF.Cos(a1);
                float by1 = pos.Y + shaftRadius * MathF.Sin(a1);

                // Triangle 1
                body[idx++] = new LineVertex { X = bx0, Y = by0, Z = bitTopZ, R = ShaftR, G = ShaftG, B = ShaftB, A = 1f };
                body[idx++] = new LineVertex { X = bx1, Y = by1, Z = bitTopZ, R = ShaftR, G = ShaftG, B = ShaftB, A = 1f };
                body[idx++] = new LineVertex { X = bx0, Y = by0, Z = shaftTopZ, R = ShaftR, G = ShaftG, B = ShaftB, A = 1f };

                // Triangle 2
                body[idx++] = new LineVertex { X = bx1, Y = by1, Z = bitTopZ, R = ShaftR, G = ShaftG, B = ShaftB, A = 1f };
                body[idx++] = new LineVertex { X = bx1, Y = by1, Z = shaftTopZ, R = ShaftR, G = ShaftG, B = ShaftB, A = 1f };
                body[idx++] = new LineVertex { X = bx0, Y = by0, Z = shaftTopZ, R = ShaftR, G = ShaftG, B = ShaftB, A = 1f };
            }

            _bodyVertexCount = idx;
            UploadBuffer(_bodyVbo, body.AsSpan(0, idx), BufferUsageARB.DynamicDraw);

            // Crosshair: two short lines crossing at the tip in X and Y
            float crossSize = scaleRef * 0.01f;
            var cross = new LineVertex[4];
            cross[0] = new LineVertex { X = pos.X - crossSize, Y = pos.Y, Z = tipZ, R = CrossR, G = CrossG, B = CrossB, A = 1f };
            cross[1] = new LineVertex { X = pos.X + crossSize, Y = pos.Y, Z = tipZ, R = CrossR, G = CrossG, B = CrossB, A = 1f };
            cross[2] = new LineVertex { X = pos.X, Y = pos.Y - crossSize, Z = tipZ, R = CrossR, G = CrossG, B = CrossB, A = 1f };
            cross[3] = new LineVertex { X = pos.X, Y = pos.Y + crossSize, Z = tipZ, R = CrossR, G = CrossG, B = CrossB, A = 1f };

            _crosshairVertexCount = 4;
            UploadBuffer(_crosshairVbo, cross, BufferUsageARB.DynamicDraw);
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

        public void Draw(GL gl)
        {
            if (_bodyVertexCount == 0) return;
            gl.BindVertexArray(_bodyVao);
            gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_bodyVertexCount);
            gl.BindVertexArray(0);
        }

        public void DrawCrosshair(GL gl)
        {
            if (_crosshairVertexCount == 0) return;
            gl.BindVertexArray(_crosshairVao);
            gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_crosshairVertexCount);
            gl.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (_bodyVbo != 0) { _gl.DeleteBuffer(_bodyVbo); _bodyVbo = 0; }
            if (_bodyVao != 0) { _gl.DeleteVertexArray(_bodyVao); _bodyVao = 0; }
            if (_crosshairVbo != 0) { _gl.DeleteBuffer(_crosshairVbo); _crosshairVbo = 0; }
            if (_crosshairVao != 0) { _gl.DeleteVertexArray(_crosshairVao); _crosshairVao = 0; }
        }
    }
}
