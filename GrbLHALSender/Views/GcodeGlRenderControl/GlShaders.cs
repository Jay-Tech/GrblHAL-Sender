using Silk.NET.OpenGLES;
using System;

namespace GrbLHALSender.Views.GcodeGlRenderControl
{
    internal static class GlShaders
    {
        internal const string VertexSource = @"#version 300 es
precision highp float;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec4 aColor;

uniform mat4 uMVP;

out vec4 vColor;

void main()
{
    gl_Position = uMVP * vec4(aPosition, 1.0);
    vColor = aColor;
}
";

        internal const string FragmentSource = @"#version 300 es
precision mediump float;

in vec4 vColor;

uniform bool uUseOverride;
uniform vec4 uColorOverride;

out vec4 fragColor;

void main()
{
    fragColor = uUseOverride ? uColorOverride : vColor;
}
";

        // =====================================================================
        // Text/Label shader: draws screen-aligned textured quads for axis labels.
        // Takes 2D screen-space position + UV, applies an orthographic projection.
        // =====================================================================

        internal const string TextVertexSource = @"#version 300 es
precision highp float;

layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;

uniform mat4 uOrtho;

out vec2 vTexCoord;

void main()
{
    gl_Position = uOrtho * vec4(aPosition, 0.0, 1.0);
    vTexCoord = aTexCoord;
}
";

        internal const string TextFragmentSource = @"#version 300 es
precision mediump float;

in vec2 vTexCoord;

uniform sampler2D uTexture;
uniform vec4 uTintColor;

out vec4 fragColor;

void main()
{
    vec4 texel = texture(uTexture, vTexCoord);
    fragColor = texel * uTintColor;
}
";

        internal static uint CompileShader(GL gl, ShaderType type, string source)
        {
            uint shader = gl.CreateShader(type);
            gl.ShaderSource(shader, source);
            gl.CompileShader(shader);

            gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
            if (status == 0)
            {
                string log = gl.GetShaderInfoLog(shader);
                gl.DeleteShader(shader);
                throw new Exception($"Shader compile error ({type}): {log}");
            }

            return shader;
        }

        internal static uint LinkProgram(GL gl, uint vertexShader, uint fragmentShader)
        {
            uint program = gl.CreateProgram();
            gl.AttachShader(program, vertexShader);
            gl.AttachShader(program, fragmentShader);
            gl.LinkProgram(program);

            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
            if (status == 0)
            {
                string log = gl.GetProgramInfoLog(program);
                gl.DeleteProgram(program);
                throw new Exception($"Program link error: {log}");
            }

            // Shaders can be detached after linking
            gl.DetachShader(program, vertexShader);
            gl.DetachShader(program, fragmentShader);

            return program;
        }
    }
}
