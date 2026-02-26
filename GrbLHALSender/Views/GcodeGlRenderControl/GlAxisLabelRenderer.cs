using Silk.NET.OpenGLES;
using SkiaSharp;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace GrbLHALSender.Views.GcodeGlRenderControl
{
    /// <summary>
    /// Renders "X", "Y", "Z" text labels at axis endpoints using textured screen-aligned quads.
    /// Labels are rendered once to small SkiaSharp bitmaps, uploaded as GL textures,
    /// then drawn each frame at projected screen positions using an orthographic shader.
    /// </summary>
    internal sealed class GlAxisLabelRenderer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct TextVertex
        {
            public float X, Y;     // screen position
            public float U, V;     // texture coordinates
        }

        private readonly GL _gl;

        // Shader program for textured quads
        private uint _program;
        private int _locOrtho;
        private int _locTexture;
        private int _locTintColor;

        // Shared VAO/VBO for a single quad (reused for each label)
        private uint _vao;
        private uint _vbo;

        // One texture per axis label
        private uint _texX;
        private uint _texY;
        private uint _texZ;
        private int _labelWidth;
        private int _labelHeight;

        public GlAxisLabelRenderer(GL gl)
        {
            _gl = gl;

            // Compile text shader program
            uint vs = GlShaders.CompileShader(gl, ShaderType.VertexShader, GlShaders.TextVertexSource);
            uint fs = GlShaders.CompileShader(gl, ShaderType.FragmentShader, GlShaders.TextFragmentSource);
            _program = GlShaders.LinkProgram(gl, vs, fs);
            gl.DeleteShader(vs);
            gl.DeleteShader(fs);

            _locOrtho = gl.GetUniformLocation(_program, "uOrtho");
            _locTexture = gl.GetUniformLocation(_program, "uTexture");
            _locTintColor = gl.GetUniformLocation(_program, "uTintColor");

            // Create VAO/VBO for a quad (4 vertices, drawn as triangle strip)
            _vao = gl.GenVertexArray();
            _vbo = gl.GenBuffer();

            gl.BindVertexArray(_vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

            uint stride = (uint)Marshal.SizeOf<TextVertex>();
            // location 0: position (2 floats)
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
            // location 1: texcoord (2 floats at offset 8)
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 8);

            gl.BindVertexArray(0);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

            // Generate label textures
            _texX = CreateLabelTexture(gl, "X", out _labelWidth, out _labelHeight);
            _texY = CreateLabelTexture(gl, "Y", out _, out _);
            _texZ = CreateLabelTexture(gl, "Z", out _, out _);
        }

        /// <summary>
        /// Renders a text string to a small bitmap and uploads it as an OpenGL texture.
        /// Returns the texture handle. All labels are the same size for consistency.
        /// </summary>
        private static unsafe uint CreateLabelTexture(GL gl, string text, out int width, out int height)
        {
            // Render text with SkiaSharp to a small bitmap
            const float fontSize = 16f;
            const int padding = 2;

            using var font = new SKFont(SKTypeface.Default, fontSize);
            using var paint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };

            // Measure text bounds
            var bounds = new SKRect();
            font.MeasureText(text, out bounds, paint);

            width = (int)MathF.Ceiling(bounds.Width) + padding * 2;
            height = (int)MathF.Ceiling(fontSize) + padding * 2;

            // Ensure power-of-two-ish sizes (not strictly required for ES 3.0, but good practice)
            if (width < 4) width = 4;
            if (height < 4) height = 4;

            using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            // Draw text centered vertically
            float textX = padding - bounds.Left;
            float textY = padding + fontSize * 0.8f; // baseline offset
            canvas.DrawText(text, textX, textY, font, paint);
            canvas.Flush();

            // Upload to GL texture
            uint tex = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, tex);

            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

            var pixels = bitmap.GetPixelSpan();
            fixed (byte* ptr = pixels)
            {
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                    (uint)width, (uint)height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }

            gl.BindTexture(TextureTarget.Texture2D, 0);

            return tex;
        }

        /// <summary>
        /// Draws axis labels at the given screen positions. Call after the main 3D scene is drawn.
        /// Pass null for any position to skip that label (e.g. if behind camera).
        /// </summary>
        public unsafe void Draw(GL gl, float viewportWidth, float viewportHeight,
            Vector2? xLabelScreenPos, Vector2? yLabelScreenPos, Vector2? zLabelScreenPos)
        {
            if (viewportWidth <= 0 || viewportHeight <= 0) return;

            gl.Disable(EnableCap.DepthTest);
            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha); // premultiplied alpha

            gl.UseProgram(_program);

            // Orthographic projection: maps screen coords (0,0)-(w,h) to clip space
            var ortho = Matrix4x4.CreateOrthographicOffCenter(0, viewportWidth, viewportHeight, 0, -1, 1);
            float* orthoPtr = (float*)&ortho;
            gl.UniformMatrix4(_locOrtho, 1, false, orthoPtr);

            gl.Uniform1(_locTexture, 0); // texture unit 0
            gl.ActiveTexture(TextureUnit.Texture0);

            // Draw each label with its axis color
            if (xLabelScreenPos.HasValue)
                DrawLabel(gl, _texX, xLabelScreenPos.Value, 1.0f, 0.0f, 0.0f); // Red
            if (yLabelScreenPos.HasValue)
                DrawLabel(gl, _texY, yLabelScreenPos.Value, 0.0f, 1.0f, 0.0f); // Green (Lime)
            if (zLabelScreenPos.HasValue)
                DrawLabel(gl, _texZ, zLabelScreenPos.Value, 80f / 255f, 150f / 255f, 1.0f); // Blue

            gl.Disable(EnableCap.Blend);
            gl.Enable(EnableCap.DepthTest);
        }

        /// <summary>
        /// Draws a single label quad at the given screen position with the specified tint color.
        /// The label is offset slightly (+4, -4) from the position for readability.
        /// </summary>
        private unsafe void DrawLabel(GL gl, uint texture, Vector2 screenPos, float r, float g, float b)
        {
            // Offset the label slightly from the axis endpoint (same as SkiaSharp: +4, -4)
            float x = screenPos.X + 4f;
            float y = screenPos.Y - _labelHeight + 4f;
            float w = _labelWidth;
            float h = _labelHeight;

            // Build quad vertices (triangle strip: TL, TR, BL, BR)
            var verts = new TextVertex[4];
            verts[0] = new TextVertex { X = x,     Y = y,     U = 0f, V = 0f }; // top-left
            verts[1] = new TextVertex { X = x + w, Y = y,     U = 1f, V = 0f }; // top-right
            verts[2] = new TextVertex { X = x,     Y = y + h, U = 0f, V = 1f }; // bottom-left
            verts[3] = new TextVertex { X = x + w, Y = y + h, U = 1f, V = 1f }; // bottom-right

            // Upload quad
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (TextVertex* ptr = verts)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(4 * Marshal.SizeOf<TextVertex>()), ptr, BufferUsageARB.DynamicDraw);
            }
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

            // Set tint color
            gl.Uniform4(_locTintColor, r, g, b, 1.0f);

            // Bind texture and draw
            gl.BindTexture(TextureTarget.Texture2D, texture);
            gl.BindVertexArray(_vao);
            gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            gl.BindVertexArray(0);
            gl.BindTexture(TextureTarget.Texture2D, 0);
        }

        public void Dispose()
        {
            if (_texX != 0) { _gl.DeleteTexture(_texX); _texX = 0; }
            if (_texY != 0) { _gl.DeleteTexture(_texY); _texY = 0; }
            if (_texZ != 0) { _gl.DeleteTexture(_texZ); _texZ = 0; }
            if (_vbo != 0) { _gl.DeleteBuffer(_vbo); _vbo = 0; }
            if (_vao != 0) { _gl.DeleteVertexArray(_vao); _vao = 0; }
            if (_program != 0) { _gl.DeleteProgram(_program); _program = 0; }
        }
    }
}
