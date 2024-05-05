using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices.Marshalling;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Silk.NET.OpenGL;


using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using DynamicData;
using Vector3D = Silk.NET.Maths.Vector3D;

using Color = System.Drawing.Color;

namespace GrbLHAL_Sender.Views.Render.Gl
{
    public class OpenGLRender : OpenGlControlBase, ICustomHitTest
    {
        private GL Gl;
        private BufferObject<float> Vbo;
        private BufferObject<uint> Ebo;
        private VertexArrayObject<float, uint> Vao;
        private static Texture Texture;
        private Shader Shader;

        private static Vector3 CameraPosition = new Vector3(0.0f, 0.0f, 3.0f);
        private static Vector3 CameraFront = new Vector3(0.0f, 0.0f, -1.0f);
        private static Vector3 CameraUp = Vector3.UnitY;
        private static Vector3 CameraDirection = Vector3.Zero;
        private static float CameraYaw = -90f;
        private static float CameraPitch = 0f;
        private static float CameraZoom = 45f;

        public int DiffernceX { get; set; } = -45;
        public int DiffernceY { get; set; }

        private static readonly float[] Vertices =
            [
                //X    Y      Z     R  G  B  A
                0.5f,  0.5f, 0.0f, 1, 1, 0, 1, //0
                0.5f, -0.5f, 0.0f, 1, 1, 0, 1, //1
                0.5f, -0.5f, 0.0f, 1, 0, 0, 1, //2
                -0.5f, -0.5f, 0.0f, 1, 0, 0, 1, //3
                -0.5f, -0.5f, 0.0f, 1, 0, 0, 1, //3
                -0.5f, 0.5f, 0.0f, 1, 0, 0, 1, //3
                -0.5f, 0.5f, 0.0f, 1, 0, 0, 1, //3
                0.5f,  0.5f, 0.0f, 1, 0, 0, 1, //0

                0.5f,  0.5f, 0.125f, 1, 0, 0, 1, //0
                0.5f, -0.5f, 0.125f, 1, 0, 0, 1, //1
                0.5f, -0.5f, 0.125f, 1, 0, 0, 1, //2
                -0.5f, -0.5f, 0.125f, 1, 0, 0, 1, //3
                -0.5f, -0.5f, 0.125f, 1, 0, 0, 1, //3
                -0.5f, 0.5f, 0.125f, 1, 0, 0, 1, //3
                -0.5f, 0.5f, 0.125f, 1, 0, 0, 1, //3
                0.5f,  0.5f, 0.125f, 1, 0, 0, 1, //0


                0.5f,  0.5f, 0.15f, 1, 0, 0, 1, //0
                0.5f, -0.5f, 0.15f, 1, 0, 0, 1, //1
                0.5f, -0.5f, 0.15f, 1, 0, 0, 1, //2
                -0.5f, -0.5f, 0.15f, 1, 0, 0, 1, //3
                -0.5f, -0.5f, 0.15f, 1, 0, 0, 1, //3
                -0.5f, 0.5f, 0.15f, 1, 0, 0, 1, //3
                -0.5f, 0.5f, 0.15f, 1, 0, 0, 1, //3
                0.5f,  0.5f, 0.15f, 1, 0, 0, 1, //0

                0.5f,  0.5f, 0.175f, 1, 0, 0, 1, //0
                0.5f, -0.5f, 0.175f, 1, 0, 0, 1, //1
                0.5f, -0.5f, 0.175f, 1, 0, 0, 1, //2
                -0.5f, -0.5f, 0.175f, 1, 0, 0, 1, //3
                -0.5f, -0.5f, 0.175f, 1, 0, 0, 1, //3
                -0.5f, 0.5f, 0.175f, 1, 0, 0, 1, //3
                -0.5f, 0.5f, 0.175f, 1, 0, 0, 1, //3
                0.5f,  0.5f, 0.175f, 1, 0, 0, 1, //0


            ];

        private static readonly uint[] Indices =
        [
           // 0, 1, 3, 1, 2, 3
           //0, 1, 2, 3
        ];
        public bool HitTest(Point point)
        {
            // Any point is a hit
            return true;
        }

        public OpenGLRender()
        {
            PointerWheelChanged += OpenGLRender_PointerWheelChanged;
            PointerMoved += OpenGLRender_PointerMoved;
            PointerReleased += OpenGLRender_PointerReleased;
            ClipToBounds = true;

        }

        private void OpenGLRender_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            switch (e.InitialPressMouseButton)
            {
                case MouseButton.Left when LastMousePosition == null:
                    return;
                case MouseButton.Left:
                    LastMousePosition = null;
                    break;
                case MouseButton.Right when LastMousePositionModel == null:
                    return;
                case MouseButton.Right:
                    LastMousePositionModel = null;
                    break;
            }
        }

        private void OpenGLRender_PointerMoved(object? sender, PointerEventArgs e)
        {
            var point = e.GetCurrentPoint(sender as Control);
            if (point.Properties.IsLeftButtonPressed)
            {
                CalculatePitchAndYaw(point.Position);
            }
            if (point.Properties.IsRightButtonPressed)
            {
                CalculateModelRotation(point.Position);
            }
        }

        private void CalculatePitchAndYaw(Point point)
        {
            var lookSensitivity = 0.05f;
            if (LastMousePosition == null)
            {
                LastMousePosition = new LastMousePosition
                {
                    X = point.X,
                    Y = point.Y
                };
            }
            else
            {
                var xOffset = (point.X - LastMousePosition.X) * lookSensitivity;
                var yOffset = (point.Y - LastMousePosition.Y) * lookSensitivity;
                LastMousePosition.X = point.X;
                LastMousePosition.Y = point.Y;

                CameraYaw -= (float)xOffset;
                CameraPitch += (float)yOffset;

                //We don't want to be able to look behind us by going over our head or under our feet so make sure it stays within these bounds
                CameraPitch = Math.Clamp(CameraPitch, -89.0f, 89.0f);

                CameraDirection.X = MathF.Cos(MathHelper.DegreesToRadians(CameraYaw)) * MathF.Cos(MathHelper.DegreesToRadians(CameraPitch));
                CameraDirection.Y = MathF.Sin(MathHelper.DegreesToRadians(CameraPitch));
                CameraDirection.Z = MathF.Sin(MathHelper.DegreesToRadians(CameraYaw)) * MathF.Cos(MathHelper.DegreesToRadians(CameraPitch));
                CameraFront = Vector3.Normalize(CameraDirection);
            }
        }

        private void CalculateModelRotation(Point point)
        {

            var lookSensitivity = .2f;
            if (LastMousePositionModel == null)
            {
                LastMousePositionModel = new LastMousePosition
                {
                    X = point.X,
                    Y = point.Y
                };
            }
            else
            {
                var xOffset = (point.X - LastMousePositionModel.X) * lookSensitivity;
                var yOffset = (point.Y - LastMousePositionModel.Y) * lookSensitivity;
                DiffernceX += (int)yOffset;
                DiffernceY += (int)xOffset;
                if (Math.Abs(DiffernceX) >= 360)
                {
                    DiffernceX = 0;
                    LastMousePositionModel.X = xOffset = 0;
                }
                if (Math.Abs(DiffernceY) >= 360)
                {
                    DiffernceY = 0;
                    LastMousePositionModel.Y = yOffset = 0;
                }
                LastMousePositionModel.X = point.X;
                LastMousePositionModel.Y = point.Y;
            }
        }

        public LastMousePosition? LastMousePosition { get; set; }
        public LastMousePosition? LastMousePositionModel { get; set; }


        protected override void OnOpenGlInit(GlInterface gl)
        {
            base.OnOpenGlInit(gl);
            Gl = GL.GetApi(gl.GetProcAddress);

            //Instantiating our new abstractions
            Ebo = new BufferObject<uint>(Gl, Indices, BufferTargetARB.ElementArrayBuffer);
            Vbo = new BufferObject<float>(Gl, Vertices, BufferTargetARB.ArrayBuffer);
            Vao = new VertexArrayObject<float, uint>(Gl, Vbo);
            //Telling the VAO object how to lay out the attribute pointers
            Vao.VertexAttributePointer(0, 3, VertexAttribPointerType.Float, 7, 0);
            Vao.VertexAttributePointer(1, 4, VertexAttribPointerType.Float, 7, 3);

            Shader = new Shader(Gl, "shader.vert", "shader.frag");

        }

        private void OpenGLRender_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
        {
            CameraZoom = Math.Clamp(CameraZoom - (float)e.Delta.Y, 1.0f, 65);
        }

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            Vbo.Dispose();
            Ebo.Dispose();
            Vao.Dispose();
            Shader.Dispose();
            base.OnOpenGlDeinit(gl);
        }

        protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
        {
            Gl.ClearColor(Color.WhiteSmoke);
            Gl.Enable(EnableCap.DepthTest);
            // Gl.Enable(EnableCap.StencilTest);
            Gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
            //Ebo.Bind();
            // Vbo.Bind();
            Vao.Bind();
            Shader.Use();
            Shader.SetUniform("uTexture0", 1);
            Gl.Viewport(0, 0, (uint)Bounds.Width, (uint)Bounds.Height);

            var model = Matrix4x4.CreateRotationY(MathHelper.DegreesToRadians(DiffernceY)) * Matrix4x4.CreateRotationX(MathHelper.DegreesToRadians(DiffernceX));
            var view = Matrix4x4.CreateLookAt(CameraPosition, CameraPosition + CameraFront, CameraUp);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(CameraZoom), (float)Bounds.Height / (float)Bounds.Width, 0.1f, 100.0f);



            // var sV = (float)Math.Sin(DateTime.Now.Millisecond / 1000f * Math.PI);
            //Shader.SetUniform("uBlue", 0);
            Shader.SetUniform("uModel", model);
            Shader.SetUniform("uView", view);
            Shader.SetUniform("uProjection", projection);
            //Gl.DrawElements(PrimitiveType.Lines, (uint)Indices.Length, DrawElementsType.UnsignedInt, null)
            Gl.DrawArrays(PrimitiveType.Lines, 0, (uint)Vertices.Length);

            //Gl.DrawElements(PrimitiveType.LineLoop, (uint)Indices.Length, DrawElementsType.UnsignedInt, null);
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
        }
    }
}
public class LastMousePosition
{
    public double X { get; set; }
    public double Y { get; set; }
}