using System;
using System.Numerics;
using GrbLHAL_Sender.Gcode;

namespace GrbLHAL_Sender.Views.GcodeRenderControl
{
    public class Camera3D
    {
        public float RotationX { get; private set; } = -45f;
        public float RotationY { get; private set; } = 0f;
        public float Distance { get; private set; } = 1900f;
        public float PanX { get; private set; }
        public float PanY { get; private set; }

        public float CenterX { get; private set; }
        public float CenterY { get; private set; }
        public float CenterZ { get; private set; }
        public float ModelScale { get; private set; } = 1f;

        public void FitToView(ToolpathData toolpath, float viewportWidth, float viewportHeight)
        {
            CenterX = toolpath.Center.X;
            CenterY = toolpath.Center.Y;
            CenterZ = toolpath.Center.Z;

            float dim = toolpath.MaxDimension;
            if (dim < 0.001f) dim = 1f;

            // Set distance so the model fills ~60% of the viewport
            float viewSize = MathF.Min(viewportWidth, viewportHeight);
            Distance = dim * 1.5f;
            ModelScale = 1f;
            PanX = 0f;
            PanY = 0f;
            RotationX = -45f;
            RotationY = 0f;
        }

        public void Rotate(float deltaX, float deltaY)
        {
            RotationY += deltaX * 0.5f;
            RotationX -= deltaY * 0.5f;
            RotationX = Math.Clamp(RotationX, -89f, 89f);
        }

        public void Pan(float deltaX, float deltaY)
        {
            float panSpeed = Distance * 0.002f;
            PanX += deltaX * panSpeed;
            PanY -= deltaY * panSpeed;
        }

        public void Zoom(float delta)
        {
            float factor = delta > 0 ? 0.9f : 1.1f;
            Distance *= factor;
            Distance = Math.Clamp(Distance, 1f, 100000f);
        }

        public Matrix4x4 GetViewMatrix()
        {
            // Orbit camera: rotate around the model center, then pull back by Distance
            float radX = RotationX * MathF.PI / 180f;
            float radY = RotationY * MathF.PI / 180f;

            // Camera position on a sphere around origin
            float camX = Distance * MathF.Cos(radX) * MathF.Sin(radY);
            float camY = Distance * MathF.Sin(radX);
            float camZ = Distance * MathF.Cos(radX) * MathF.Cos(radY);

            var eye = new Vector3(camX + CenterX + PanX, camY + CenterY + PanY, camZ + CenterZ);
            var target = new Vector3(CenterX + PanX, CenterY + PanY, CenterZ);
            var up = new Vector3(0, 1, 0);

            return Matrix4x4.CreateLookAt(eye, target, up);
        }

        public Matrix4x4 GetProjectionMatrix(float viewportWidth, float viewportHeight)
        {
            float aspect = viewportWidth / viewportHeight;
            float fov = 45f * MathF.PI / 180f;
            return Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, 0.1f, Distance * 10f);
        }
    }
}
