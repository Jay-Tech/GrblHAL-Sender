using GrbLHALSender.Gcode;
using GrbLHALSender.Settings;
using System;
using System.Numerics;

namespace GrbLHALSender.Views.GcodeRenderControl
{
    public class Camera3D
    {
        public float RotationX { get; private set; } = 45f;
        public float RotationY { get; private set; } = 0f;
        public float Distance { get; private set; } = 1900f;
        public float PanX { get; private set; }
        public float PanY { get; private set; }

        public float CenterX { get; private set; }
        public float CenterY { get; private set; }
        public float CenterZ { get; private set; }
        public float ModelScale { get; private set; } = 1f;

        public void FitToView(ToolpathData toolpath, float viewportWidth, float viewportHeight,
            MachineSettings? machineSettings = null, Point3D? wco = null)
        {
            bool hasMachine = machineSettings != null &&
                              machineSettings.DisplayXSize > 0 &&
                              machineSettings.DisplayYSize > 0;

            if (hasMachine)
            {
                FitToMachine(machineSettings!, viewportWidth, viewportHeight, wco);
                // Center camera vertically at the midpoint between work surface (Z=0)
                // and the floor (MinBounds.Z = deepest cut), so model fills the view.
                CenterZ = toolpath.MinBounds.Z / 2f;
            }
            else
            {
                CenterX = toolpath.Center.X;
                CenterY = toolpath.Center.Y;
                CenterZ = toolpath.MinBounds.Z / 2f;
                float dim = toolpath.MaxDimension;
                if (dim < 0.001f) dim = 1f;
                Distance = dim * 1.5f;
                ResetOrientation();
            }
        }

        public void FitToMachine(MachineSettings machineSettings, float viewportWidth, float viewportHeight,
            Point3D? wco = null)
        {
            // Center camera on the machine work area, converted to work coordinates
            // Machine grid in machine coords: X=[0,XSize], Y=[-YSize,0]
            // Work coords = Machine coords - WCO
            float wcoX = wco?.X ?? 0f;
            float wcoY = wco?.Y ?? 0f;
            float wcoZ = wco?.Z ?? 0f;

            CenterX = (float)machineSettings.DisplayXSize / 2f - wcoX;
            CenterY = -(float)machineSettings.DisplayYSize / 2f - wcoY;
            // No file loaded: center vertically at the midpoint of Z travel in work coords.
            // Machine Z goes from 0 (home/top) to -DisplayZSize (table/bottom).
            // In work coords: 0 - wcoZ (top) to -DisplayZSize - wcoZ (table).
            float tableZ = machineSettings.DisplayZSize > 0 ? -(float)machineSettings.DisplayZSize - wcoZ : -wcoZ;
            CenterZ = tableZ / 2f;
            float dim = MathF.Max((float)machineSettings.DisplayXSize, (float)machineSettings.DisplayYSize);
            // Account for viewport aspect ratio so the grid fits nicely
            float aspect = viewportWidth / viewportHeight;
            float fitDim = aspect > 1f ? dim : dim / aspect;
            Distance = fitDim * 1.8f;
            ResetOrientation();
        }

        private void ResetOrientation()
        {
            ModelScale = 1f;
            PanX = 0f;
            PanY = 0f;
            // Slight overhead angle — more top-down to see the full work surface
            RotationX = 60f;
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
            // Orbit camera using Z-up convention (CNC: Z is vertical, XY is table)
            float radX = RotationX * MathF.PI / 180f;  // Elevation/pitch
            float radY = RotationY * MathF.PI / 180f;  // Azimuth/yaw

            // Camera position on a sphere: Z-up, XY is the horizontal plane
            // RotationX=0 looks from the side, RotationX=90 looks straight down from above
            float camX = Distance * MathF.Cos(radX) * MathF.Sin(radY);
            float camY = -Distance * MathF.Cos(radX) * MathF.Cos(radY);
            float camZ = Distance * MathF.Sin(radX);

            var eye = new Vector3(camX + CenterX + PanX, camY + CenterY, camZ + CenterZ + PanY);
            var target = new Vector3(CenterX + PanX, CenterY, CenterZ + PanY);
            var up = new Vector3(0, 0, 1);

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
