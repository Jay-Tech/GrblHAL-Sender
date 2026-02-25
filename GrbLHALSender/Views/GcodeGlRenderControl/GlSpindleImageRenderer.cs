using Silk.NET.OpenGLES;
using SkiaSharp;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace GrbLHALSender.Views.GcodeGlRenderControl
{
    /// <summary>
    /// Renders a user-supplied spindle image as a screen-aligned, rotated textured quad
    /// in the GL scene. The image is loaded from disk via SpindleImageProvider, decoded
    /// with SkiaSharp, and uploaded as a GL texture. Each frame the quad is positioned,
    /// scaled, and rotated to match the 3D spindle projection (tip → shaftTop).
    ///
    /// Uses the same text/label shader (orthographic + texture + tint) from GlShaders.
    /// When no image is available, returns false from Draw() so the caller can fall back
    /// to the procedural 3D spindle (GlSpindleRenderer).
    /// </summary>
    internal sealed class GlSpindleImageRenderer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct TexturedVertex
        {
            public float X, Y;     // screen position
            public float U, V;     // texture coordinates
        }

        private readonly GL _gl;

        // Shader program (shared with axis labels — same source)
        private uint _program;
        private int _locOrtho;
        private int _locTexture;
        private int _locTintColor;

        // Quad VAO/VBO
        private uint _vao;
        private uint _vbo;

        // Spindle image texture
        private uint _texture;
        private float _imageAspect; // width / height
        private bool _hasTexture;

        // Crosshair VAO/VBO (2 lines = 4 vertices, uses the main 3D shader)
        // We don't draw the crosshair here — the caller (GlSpindleRenderer.DrawCrosshair)
        // already handles that when using procedural mode. For image mode we draw a 2D
        // screen-space crosshair using the same textured quad approach with a tiny generated texture.
        private uint _crosshairVao;
        private uint _crosshairVbo;

        public bool HasImage => _hasTexture;

        public GlSpindleImageRenderer(GL gl)
        {
            _gl = gl;

            // Compile text shader program (same shaders as axis labels)
            uint vs = GlShaders.CompileShader(gl, ShaderType.VertexShader, GlShaders.TextVertexSource);
            uint fs = GlShaders.CompileShader(gl, ShaderType.FragmentShader, GlShaders.TextFragmentSource);
            _program = GlShaders.LinkProgram(gl, vs, fs);
            gl.DeleteShader(vs);
            gl.DeleteShader(fs);

            _locOrtho = gl.GetUniformLocation(_program, "uOrtho");
            _locTexture = gl.GetUniformLocation(_program, "uTexture");
            _locTintColor = gl.GetUniformLocation(_program, "uTintColor");

            // Create quad VAO/VBO
            _vao = gl.GenVertexArray();
            _vbo = gl.GenBuffer();

            gl.BindVertexArray(_vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

            uint stride = (uint)Marshal.SizeOf<TexturedVertex>();
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 8);

            gl.BindVertexArray(0);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

            // Create crosshair VAO/VBO (reuses same vertex layout, drawn as lines)
            _crosshairVao = gl.GenVertexArray();
            _crosshairVbo = gl.GenBuffer();

            gl.BindVertexArray(_crosshairVao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _crosshairVbo);

            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 8);

            gl.BindVertexArray(0);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        }

        /// <summary>
        /// Uploads a spindle image bitmap as a GL texture. Call from the compositor thread
        /// (inside OnOpenGlRender or OnOpenGlInit) since it makes GL calls.
        /// The bitmap pixel data is read and uploaded; the caller retains ownership of the bitmap.
        /// </summary>
        public unsafe void UploadTexture(SKBitmap bitmap)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                ClearTexture();
                return;
            }

            // Create texture if needed
            if (_texture == 0)
                _texture = _gl.GenTexture();

            _gl.BindTexture(TextureTarget.Texture2D, _texture);

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

            // Ensure RGBA8888 format for GL upload
            SKBitmap? converted = null;
            SKBitmap source = bitmap;
            if (bitmap.ColorType != SKColorType.Rgba8888)
            {
                converted = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                bitmap.CopyTo(converted, SKColorType.Rgba8888);
                source = converted;
            }

            var pixels = source.GetPixelSpan();
            fixed (byte* ptr = pixels)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                    (uint)source.Width, (uint)source.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }

            _gl.BindTexture(TextureTarget.Texture2D, 0);
            converted?.Dispose();

            _imageAspect = (float)bitmap.Width / bitmap.Height;
            _hasTexture = true;
        }

        /// <summary>
        /// Clears the loaded texture, causing Draw() to return false (fall back to procedural).
        /// </summary>
        public void ClearTexture()
        {
            if (_texture != 0)
            {
                _gl.DeleteTexture(_texture);
                _texture = 0;
            }
            _hasTexture = false;
        }

        /// <summary>
        /// Draws the spindle image at the projected screen position.
        /// Returns true if the image was drawn, false if no image is available
        /// (caller should fall back to procedural GlSpindleRenderer).
        /// </summary>
        /// <param name="gl">GL context</param>
        /// <param name="viewportWidth">Logical viewport width</param>
        /// <param name="viewportHeight">Logical viewport height</param>
        /// <param name="tipScreen">Screen position of spindle tip</param>
        /// <param name="shaftTopScreen">Screen position of shaft top</param>
        public unsafe bool Draw(GL gl, float viewportWidth, float viewportHeight,
            Vector2 tipScreen, Vector2 shaftTopScreen)
        {
            if (!_hasTexture || _texture == 0)
                return false;

            // Calculate screen-space distance and angle
            float dx = shaftTopScreen.X - tipScreen.X;
            float dy = shaftTopScreen.Y - tipScreen.Y;
            float totalScreenLen = MathF.Sqrt(dx * dx + dy * dy);

            // If the spindle projects too small, skip image rendering (caller draws top-view indicator)
            if (totalScreenLen < 5f)
                return false;

            // Target height matches projected spindle length, width preserves aspect ratio
            float targetHeight = Math.Clamp(totalScreenLen, 20f, 500f);
            float targetWidth = targetHeight * _imageAspect;

            // Angle from tip to shaft-top in screen space
            // The image's natural orientation has the shaft pointing up (-Y on screen = angle -PI/2)
            float angleRad = MathF.Atan2(dy, dx);
            float rotation = angleRad + MathF.PI / 2f;

            // Build rotated quad vertices
            // The image is positioned with its bottom edge at the tip (crosshair position),
            // extending upward (in image space) toward the shaft top.
            // Quad corners in image-local space (before rotation):
            //   TL = (-w/2, -h)    TR = (+w/2, -h)
            //   BL = (-w/2,  0)    BR = (+w/2,  0)
            float hw = targetWidth / 2f;
            float cos = MathF.Cos(rotation);
            float sin = MathF.Sin(rotation);

            // Rotate point (lx, ly) around origin, then translate to tipScreen
            Vector2 Rotate(float lx, float ly)
            {
                return new Vector2(
                    tipScreen.X + lx * cos - ly * sin,
                    tipScreen.Y + lx * sin + ly * cos);
            }

            var tl = Rotate(-hw, -targetHeight);
            var tr = Rotate(+hw, -targetHeight);
            var bl = Rotate(-hw, 0f);
            var br = Rotate(+hw, 0f);

            // Triangle strip: TL, TR, BL, BR
            var verts = new TexturedVertex[4];
            verts[0] = new TexturedVertex { X = tl.X, Y = tl.Y, U = 0f, V = 0f };
            verts[1] = new TexturedVertex { X = tr.X, Y = tr.Y, U = 1f, V = 0f };
            verts[2] = new TexturedVertex { X = bl.X, Y = bl.Y, U = 0f, V = 1f };
            verts[3] = new TexturedVertex { X = br.X, Y = br.Y, U = 1f, V = 1f };

            // Setup GL state
            gl.Disable(EnableCap.DepthTest);
            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha); // premultiplied alpha

            gl.UseProgram(_program);

            // Orthographic projection
            var ortho = Matrix4x4.CreateOrthographicOffCenter(0, viewportWidth, viewportHeight, 0, -1, 1);
            float* orthoPtr = (float*)&ortho;
            gl.UniformMatrix4(_locOrtho, 1, false, orthoPtr);

            gl.Uniform1(_locTexture, 0);
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.Uniform4(_locTintColor, 1.0f, 1.0f, 1.0f, 1.0f); // no tint — show original colors

            // Upload quad
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (TexturedVertex* ptr = verts)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(4 * Marshal.SizeOf<TexturedVertex>()), ptr, BufferUsageARB.DynamicDraw);
            }
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

            // Draw
            gl.BindTexture(TextureTarget.Texture2D, _texture);
            gl.BindVertexArray(_vao);
            gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            gl.BindVertexArray(0);
            gl.BindTexture(TextureTarget.Texture2D, 0);

            // Draw crosshair at tip position
            DrawScreenCrosshair(gl, viewportWidth, viewportHeight, tipScreen);

            gl.Disable(EnableCap.Blend);
            gl.Enable(EnableCap.DepthTest);

            return true;
        }

        /// <summary>
        /// Draws a yellow crosshair at the tip position in screen space.
        /// Uses lines rendered through the same orthographic shader.
        /// </summary>
        private unsafe void DrawScreenCrosshair(GL gl, float viewportWidth, float viewportHeight, Vector2 tipScreen)
        {
            const float crossSize = 8f;

            // 2 lines = 4 vertices (use UV=0 since we won't sample texture)
            var verts = new TexturedVertex[4];
            verts[0] = new TexturedVertex { X = tipScreen.X - crossSize, Y = tipScreen.Y, U = 0f, V = 0f };
            verts[1] = new TexturedVertex { X = tipScreen.X + crossSize, Y = tipScreen.Y, U = 0f, V = 0f };
            verts[2] = new TexturedVertex { X = tipScreen.X, Y = tipScreen.Y - crossSize, U = 0f, V = 0f };
            verts[3] = new TexturedVertex { X = tipScreen.X, Y = tipScreen.Y + crossSize, U = 0f, V = 0f };

            // Unbind texture so fragment shader samples (0,0,0,0) from no texture,
            // but we use uTintColor to override. Actually, with no texture bound the
            // texel will be black. Instead, let's create a 1x1 white texture trick:
            // Set tint to yellow and disable texturing by binding a white pixel.
            // Simpler: just use the color override. The fragment shader does texel * uTintColor.
            // With a 1x1 white texture, texel=(1,1,1,1), so fragColor = uTintColor.
            // We need a 1x1 white texture. Let's create one lazily.
            EnsureWhitePixelTexture();

            gl.Uniform4(_locTintColor, 1.0f, 1.0f, 0.0f, 1.0f); // yellow

            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _crosshairVbo);
            fixed (TexturedVertex* ptr = verts)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(4 * Marshal.SizeOf<TexturedVertex>()), ptr, BufferUsageARB.DynamicDraw);
            }
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

            gl.BindTexture(TextureTarget.Texture2D, _whitePixelTex);
            gl.BindVertexArray(_crosshairVao);
            gl.LineWidth(1.5f);
            gl.DrawArrays(PrimitiveType.Lines, 0, 4);
            gl.BindVertexArray(0);
            gl.BindTexture(TextureTarget.Texture2D, 0);
        }

        private uint _whitePixelTex;

        private unsafe void EnsureWhitePixelTexture()
        {
            if (_whitePixelTex != 0) return;

            _whitePixelTex = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _whitePixelTex);

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);

            byte[] white = [255, 255, 255, 255]; // RGBA white
            fixed (byte* ptr = white)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                    1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }

            _gl.BindTexture(TextureTarget.Texture2D, 0);
        }

        public void Dispose()
        {
            ClearTexture();
            if (_whitePixelTex != 0) { _gl.DeleteTexture(_whitePixelTex); _whitePixelTex = 0; }
            if (_vbo != 0) { _gl.DeleteBuffer(_vbo); _vbo = 0; }
            if (_vao != 0) { _gl.DeleteVertexArray(_vao); _vao = 0; }
            if (_crosshairVbo != 0) { _gl.DeleteBuffer(_crosshairVbo); _crosshairVbo = 0; }
            if (_crosshairVao != 0) { _gl.DeleteVertexArray(_crosshairVao); _crosshairVao = 0; }
            if (_program != 0) { _gl.DeleteProgram(_program); _program = 0; }
        }
    }
}
