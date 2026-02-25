using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using Avalonia.Threading;
using GrbLHALSender.Gcode;
using GrbLHALSender.Settings;
using GrbLHALSender.Views.GcodeRenderControl;
using Silk.NET.OpenGLES;
using System;
using System.Numerics;
using System.Threading;

namespace GrbLHALSender.Views.GcodeGlRenderControl
{
    /// <summary>
    /// OpenGL ES render control for GCode toolpath visualization.
    /// Implements ICustomHitTest because OpenGlControlBase has no Avalonia "background",
    /// so the default hit-testing considers it empty and all pointer events are silently dropped.
    /// Uses a DispatcherTimer for render scheduling instead of RequestNextFrameRendering to
    /// avoid known Avalonia compositor race conditions that cause partial draws or render stalls.
    /// </summary>
    public class GcodeGlRenderControl : OpenGlControlBase, ICustomHitTest
    {
        public static readonly StyledProperty<ToolpathData?> ToolpathProperty =
            AvaloniaProperty.Register<GcodeGlRenderControl, ToolpathData?>(nameof(Toolpath));

        public static readonly StyledProperty<Point3D?> SpindlePositionProperty =
            AvaloniaProperty.Register<GcodeGlRenderControl, Point3D?>(nameof(SpindlePosition));

        public static readonly StyledProperty<MachineSettings?> MachineSettingsProperty =
            AvaloniaProperty.Register<GcodeGlRenderControl, MachineSettings?>(nameof(MachineSettings));

        public static readonly StyledProperty<Point3D?> WorkCoordinateOffsetProperty =
            AvaloniaProperty.Register<GcodeGlRenderControl, Point3D?>(nameof(WorkCoordinateOffset));

        public static readonly StyledProperty<int> CompletedSegmentIndexProperty =
            AvaloniaProperty.Register<GcodeGlRenderControl, int>(nameof(CompletedSegmentIndex), defaultValue: -1);

        public static readonly StyledProperty<int> SelectedSegmentIndexProperty =
            AvaloniaProperty.Register<GcodeGlRenderControl, int>(nameof(SelectedSegmentIndex), defaultValue: -1);

        public static readonly StyledProperty<string> SelectedLineInfoProperty =
            AvaloniaProperty.Register<GcodeGlRenderControl, string>(nameof(SelectedLineInfo), defaultValue: "");

        public static readonly StyledProperty<bool> UseAntiAliasProperty =
            AvaloniaProperty.Register<GcodeGlRenderControl, bool>(nameof(UseAntiAlias), defaultValue: true);

        public static readonly StyledProperty<string?> SpindleImagePathProperty =
            AvaloniaProperty.Register<GcodeGlRenderControl, string?>(nameof(SpindleImagePath), defaultValue: "spindle.png");

        public ToolpathData? Toolpath
        {
            get => GetValue(ToolpathProperty);
            set => SetValue(ToolpathProperty, value);
        }

        public Point3D? SpindlePosition
        {
            get => GetValue(SpindlePositionProperty);
            set => SetValue(SpindlePositionProperty, value);
        }

        public MachineSettings? MachineSettings
        {
            get => GetValue(MachineSettingsProperty);
            set => SetValue(MachineSettingsProperty, value);
        }

        public Point3D? WorkCoordinateOffset
        {
            get => GetValue(WorkCoordinateOffsetProperty);
            set => SetValue(WorkCoordinateOffsetProperty, value);
        }

        public int CompletedSegmentIndex
        {
            get => GetValue(CompletedSegmentIndexProperty);
            set => SetValue(CompletedSegmentIndexProperty, value);
        }

        public int SelectedSegmentIndex
        {
            get => GetValue(SelectedSegmentIndexProperty);
            set => SetValue(SelectedSegmentIndexProperty, value);
        }

        public string SelectedLineInfo
        {
            get => GetValue(SelectedLineInfoProperty);
            set => SetValue(SelectedLineInfoProperty, value);
        }

        public bool UseAntiAlias
        {
            get => GetValue(UseAntiAliasProperty);
            set => SetValue(UseAntiAliasProperty, value);
        }

        public string? SpindleImagePath
        {
            get => GetValue(SpindleImagePathProperty);
            set => SetValue(SpindleImagePathProperty, value);
        }

        private readonly Camera3D _camera = new();
        private Point? _lastPointerPos;
        private Point? _pressStartPos;
        private bool _isLeftDragging;
        private bool _isRightDragging;
        private bool _isMiddleDragging;
        private bool _fitted;

        // Spindle image: loaded on UI thread, uploaded to GL texture on compositor thread.
        private readonly SpindleImageProvider _spindleImageProvider = new();
        private volatile bool _needsSpindleTextureUpload;

        // GL state — only accessed on the compositor thread
        private GL? _gl;
        private GlToolpathRenderer? _toolpathRenderer;
        private GlSpindleRenderer? _spindleRenderer;
        private GlSpindleImageRenderer? _spindleImageRenderer;
        private GlAxisLabelRenderer? _axisLabelRenderer;
        private uint _program;
        private int _locMVP;
        private int _locUseOverride;
        private int _locColorOverride;
        private bool _glInitialized;

        // MSAA framebuffer for anti-aliasing.
        // Avalonia's OpenGlControlBase provides a single-sampled FBO. We create our own
        // multisampled FBO, render everything to it, then blit/resolve to Avalonia's FBO.
        private const int MsaaSamples = 4;
        private uint _msaaFbo;
        private uint _msaaColorRbo;
        private uint _msaaDepthRbo;
        private int _msaaWidth;
        private int _msaaHeight;

        // Lock-free state passing: UI thread writes a new RenderState reference,
        // compositor thread reads it atomically. No lock contention.
        private volatile RenderState _pendingState = new();

        // Rebuild flags — set on UI thread via Interlocked, consumed on compositor thread.
        private int _needsToolpathRebuild; // 0 = false, 1 = true
        private int _needsGridRebuild;     // 0 = false, 1 = true

        // Pre-built vertex arrays: computed on UI thread, uploaded to GPU on compositor thread.
        // This avoids heavy iteration of ToolpathData.Segments on the compositor thread.
        private volatile LineVertex[]? _pendingToolpathVertices;
        private volatile LineVertex[]? _pendingGridVertices;
        private volatile int _pendingGridVertexCount;
        private volatile LineVertex[]? _pendingAxesVertices;
        private volatile int _pendingAxesVertexCount;
        // Not volatile (struct can't be volatile), but torn reads are visually imperceptible
        // since axis endpoints only change when grid/axes are rebuilt.
        private AxisEndpoints _pendingAxisEndpoints;

        // Dirty flag + timer: avoids calling RequestNextFrameRendering directly from
        // rapid property changes. The timer coalesces multiple updates into a single
        // render request per tick, preventing compositor race conditions that cause
        // partial draws or render stalls (Avalonia issues #14725, #17865, #18560).
        private volatile bool _renderDirty;
        private DispatcherTimer? _renderTimer;

        private sealed class RenderState
        {
            public ToolpathData? Toolpath;
            public Point3D? SpindlePosition;
            public MachineSettings? MachineSettings;
            public Point3D? WorkCoordinateOffset;
            public int CompletedSegmentIndex = -1;
            public int SelectedSegmentIndex = -1;
            public bool UseAntiAlias = true;
        }

        public GcodeGlRenderControl()
        {
            ClipToBounds = true;

            // Load the spindle image from Config folder on startup
            _spindleImageProvider.TryLoad(SpindleImagePath);
            _needsSpindleTextureUpload = true;
        }

        // =====================================================================
        // ICustomHitTest — required for OpenGlControlBase to receive pointer events.
        // Without this, Avalonia's hit-testing considers the control empty because
        // it has no Avalonia-rendered visual content (it renders via OpenGL offscreen).
        // =====================================================================

        /// <summary>
        /// Returns true for all points within bounds, enabling pointer events.
        /// </summary>
        public bool HitTest(Point point) => true;

        // =====================================================================
        // Render scheduling via DispatcherTimer
        // =====================================================================

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            // Build default grid + axes so the control shows the grid immediately on startup,
            // even before any machine connects or toolpath is loaded. OnPropertyChanged won't
            // fire because all properties start at their default values (null).
            PreBuildGridAndAxesVertices(Toolpath, MachineSettings, WorkCoordinateOffset);
            Interlocked.Exchange(ref _needsGridRebuild, 1);
            PublishRenderState();

            // ~60 fps timer drives rendering. Only actually calls RequestNextFrameRendering
            // when _renderDirty is set, so it's effectively idle when nothing changes.
            _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _renderTimer.Tick += OnRenderTimerTick;
            _renderTimer.Start();

            MarkDirty();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _renderTimer?.Stop();
            if (_renderTimer != null)
                _renderTimer.Tick -= OnRenderTimerTick;
            _renderTimer = null;

            base.OnDetachedFromVisualTree(e);
        }

        private void OnRenderTimerTick(object? sender, EventArgs e)
        {
            if (_renderDirty)
            {
                _renderDirty = false;
                RequestNextFrameRendering();
            }
        }

        /// <summary>
        /// Marks the control as needing a repaint. The DispatcherTimer will coalesce
        /// multiple dirty flags into a single RequestNextFrameRendering per tick.
        /// </summary>
        private void MarkDirty()
        {
            _renderDirty = true;
        }

        // =====================================================================
        // Property change handling
        // =====================================================================

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ToolpathProperty || change.Property == MachineSettingsProperty ||
                change.Property == WorkCoordinateOffsetProperty)
            {
                _fitted = false;
                if (change.Property == ToolpathProperty)
                {
                    SelectedSegmentIndex = -1;
                    SelectedLineInfo = "";
                    // Pre-build toolpath vertices on the UI thread
                    PreBuildToolpathVertices(Toolpath);
                    Interlocked.Exchange(ref _needsToolpathRebuild, 1);
                }

                // Pre-build grid + axes vertices on the UI thread
                PreBuildGridAndAxesVertices(Toolpath, MachineSettings, WorkCoordinateOffset);
                Interlocked.Exchange(ref _needsGridRebuild, 1);

                PublishRenderState();
                MarkDirty();
            }
            else if (change.Property == SpindleImagePathProperty)
            {
                _spindleImageProvider.TryLoad(SpindleImagePath);
                _needsSpindleTextureUpload = true;
                MarkDirty();
            }
            else if (change.Property == UseAntiAliasProperty)
            {
                PublishRenderState();
                MarkDirty();
            }
            else if (change.Property == SpindlePositionProperty ||
                     change.Property == CompletedSegmentIndexProperty ||
                     change.Property == SelectedSegmentIndexProperty)
            {
                PublishRenderState();
                MarkDirty();
            }
        }

        /// <summary>
        /// Publishes a new immutable snapshot of all UI properties.
        /// Atomic reference swap — no lock needed.
        /// </summary>
        private void PublishRenderState()
        {
            _pendingState = new RenderState
            {
                Toolpath = Toolpath,
                SpindlePosition = SpindlePosition,
                MachineSettings = MachineSettings,
                WorkCoordinateOffset = WorkCoordinateOffset,
                CompletedSegmentIndex = CompletedSegmentIndex,
                SelectedSegmentIndex = SelectedSegmentIndex,
                UseAntiAlias = UseAntiAlias
            };
        }

        /// <summary>
        /// Pre-builds toolpath vertex array on the UI thread so the compositor
        /// only needs to upload to GPU (fast memcpy) instead of iterating segments.
        /// </summary>
        private void PreBuildToolpathVertices(ToolpathData? toolpath)
        {
            if (toolpath == null || toolpath.Segments.Count == 0)
            {
                _pendingToolpathVertices = null;
                return;
            }

            _pendingToolpathVertices = GlToolpathRenderer.BuildToolpathVertices(toolpath);
        }

        /// <summary>
        /// Pre-builds grid and axes vertex arrays on the UI thread.
        /// Also captures axis endpoint world positions for label projection.
        /// </summary>
        private void PreBuildGridAndAxesVertices(ToolpathData? toolpath, MachineSettings? machineSettings, Point3D? wco)
        {
            var (gridVerts, gridCount) = GlToolpathRenderer.BuildGridVertices(toolpath, machineSettings, wco);
            _pendingGridVertices = gridVerts;
            _pendingGridVertexCount = gridCount;

            var (axesVerts, axesCount, endpoints) = GlToolpathRenderer.BuildAxesVertices(toolpath, machineSettings, wco);
            _pendingAxesVertices = axesVerts;
            _pendingAxesVertexCount = axesCount;
            _pendingAxisEndpoints = endpoints;
        }

        // =====================================================================
        // OpenGL lifecycle
        // =====================================================================

        protected override void OnOpenGlInit(GlInterface gl)
        {
            try
            {
                _gl = GL.GetApi(gl.GetProcAddress);

                // Compile shaders and link program
                uint vs = GlShaders.CompileShader(_gl, ShaderType.VertexShader, GlShaders.VertexSource);
                uint fs = GlShaders.CompileShader(_gl, ShaderType.FragmentShader, GlShaders.FragmentSource);
                _program = GlShaders.LinkProgram(_gl, vs, fs);
                _gl.DeleteShader(vs);
                _gl.DeleteShader(fs);

                _locMVP = _gl.GetUniformLocation(_program, "uMVP");
                _locUseOverride = _gl.GetUniformLocation(_program, "uUseOverride");
                _locColorOverride = _gl.GetUniformLocation(_program, "uColorOverride");

                _toolpathRenderer = new GlToolpathRenderer(_gl);
                _spindleRenderer = new GlSpindleRenderer(_gl);
                _spindleImageRenderer = new GlSpindleImageRenderer(_gl);
                _axisLabelRenderer = new GlAxisLabelRenderer(_gl);

                _gl.Enable(EnableCap.DepthTest);
                _gl.DepthFunc(DepthFunction.Lequal);

                // Create MSAA FBO resources (sized lazily on first render)
                _msaaFbo = _gl.GenFramebuffer();
                _msaaColorRbo = _gl.GenRenderbuffer();
                _msaaDepthRbo = _gl.GenRenderbuffer();

                _glInitialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GcodeGlRenderControl] OpenGL init failed: {ex.Message}");
                _glInitialized = false;
            }
        }

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            _glInitialized = false;
            _toolpathRenderer?.Dispose();
            _spindleRenderer?.Dispose();
            _spindleImageRenderer?.Dispose();
            _axisLabelRenderer?.Dispose();
            _spindleImageProvider.Dispose();

            if (_gl != null)
            {
                // Clean up MSAA resources
                if (_msaaDepthRbo != 0) { _gl.DeleteRenderbuffer(_msaaDepthRbo); _msaaDepthRbo = 0; }
                if (_msaaColorRbo != 0) { _gl.DeleteRenderbuffer(_msaaColorRbo); _msaaColorRbo = 0; }
                if (_msaaFbo != 0) { _gl.DeleteFramebuffer(_msaaFbo); _msaaFbo = 0; }

                if (_program != 0)
                {
                    _gl.DeleteProgram(_program);
                    _program = 0;
                }
            }

            _gl?.Dispose();
            _gl = null;
        }

        /// <summary>
        /// Ensures the MSAA renderbuffers match the current pixel dimensions.
        /// Called each frame — only reallocates when the size actually changes.
        /// </summary>
        private void EnsureMsaaBuffers(int pixelW, int pixelH)
        {
            if (_gl == null) return;
            if (_msaaWidth == pixelW && _msaaHeight == pixelH) return;

            _msaaWidth = pixelW;
            _msaaHeight = pixelH;

            // Resize color renderbuffer (RGBA8, multisampled)
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaColorRbo);
            _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer,
                (uint)MsaaSamples, InternalFormat.Rgba8, (uint)pixelW, (uint)pixelH);

            // Resize depth renderbuffer (Depth24, multisampled)
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaDepthRbo);
            _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer,
                (uint)MsaaSamples, InternalFormat.DepthComponent24, (uint)pixelW, (uint)pixelH);

            // Attach to MSAA FBO
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _msaaColorRbo);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _msaaDepthRbo);

            var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete)
            {
                System.Diagnostics.Debug.WriteLine($"[GcodeGlRenderControl] MSAA FBO incomplete: {status}");
            }

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
        }

        // =====================================================================
        // Render pass (runs on compositor thread)
        // =====================================================================

        protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
        {
            if (_gl == null || !_glInitialized) return;

            try
            {
                var scaling = VisualRoot?.RenderScaling ?? 1.0;
                int pixelW = (int)(Bounds.Width * scaling);
                int pixelH = (int)(Bounds.Height * scaling);
                if (pixelW <= 0 || pixelH <= 0) return;

                float width = (float)Bounds.Width;
                float height = (float)Bounds.Height;

                // Read render state — atomic reference read, no lock
                var state = _pendingState;
                bool useMsaa = state.UseAntiAlias;

                // Consume rebuild flags (Interlocked.Exchange is atomic)
                bool rebuildToolpath = Interlocked.Exchange(ref _needsToolpathRebuild, 0) != 0;
                bool rebuildGrid = Interlocked.Exchange(ref _needsGridRebuild, 0) != 0;

                // Snapshot axis endpoints (struct copy, safe to read on compositor thread)
                var axisEndpoints = _pendingAxisEndpoints;

                // Upload pre-built vertex arrays to GPU (fast — just a memcpy, no segment iteration)
                if (rebuildToolpath)
                {
                    var verts = _pendingToolpathVertices;
                    _toolpathRenderer?.UploadToolpath(verts);
                }

                if (rebuildGrid)
                {
                    var gridVerts = _pendingGridVertices;
                    var gridCount = _pendingGridVertexCount;
                    _toolpathRenderer?.UploadGrid(gridVerts, gridCount);

                    var axesVerts = _pendingAxesVertices;
                    var axesCount = _pendingAxesVertexCount;
                    _toolpathRenderer?.UploadAxes(axesVerts, axesCount);
                }

                // Auto-fit camera
                if (!_fitted)
                {
                    if (state.Toolpath != null && state.Toolpath.Segments.Count > 0)
                    {
                        _camera.FitToView(state.Toolpath, width, height, state.MachineSettings, state.WorkCoordinateOffset);
                        _fitted = true;
                    }
                    else if (state.MachineSettings != null && state.MachineSettings.DisplayXSize > 0 && state.MachineSettings.DisplayYSize > 0)
                    {
                        _camera.FitToMachine(state.MachineSettings, width, height, state.WorkCoordinateOffset);
                        _fitted = true;
                    }
                }

                // Decide which FBO to render into: our MSAA FBO or Avalonia's directly
                uint renderFbo;
                if (useMsaa)
                {
                    EnsureMsaaBuffers(pixelW, pixelH);
                    renderFbo = _msaaFbo;
                }
                else
                {
                    renderFbo = (uint)fb;
                }

                // Bind render target, set viewport, and clear
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, renderFbo);
                _gl.Viewport(0, 0, (uint)pixelW, (uint)pixelH);
                _gl.ClearColor(25f / 255f, 25f / 255f, 30f / 255f, 1.0f);
                _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                // Build MVP
                var view = _camera.GetViewMatrix();
                var proj = _camera.GetProjectionMatrix(width, height);
                var mvp = view * proj;

                _gl.UseProgram(_program);

                // Upload MVP matrix
                float* mvpPtr = (float*)&mvp;
                _gl.UniformMatrix4(_locMVP, 1, false, mvpPtr);

                // Draw grid + axes (depth test on)
                _gl.Enable(EnableCap.DepthTest);
                _gl.Uniform1(_locUseOverride, 0); // false
                _toolpathRenderer?.DrawGrid(_gl);
                _toolpathRenderer?.DrawAxes(_gl);

                // Draw full toolpath
                _toolpathRenderer?.DrawToolpath(_gl);

                // Draw completed overlay (depth test off — overdraw on top)
                if (state.CompletedSegmentIndex > 0)
                {
                    _gl.Disable(EnableCap.DepthTest);
                    _gl.Uniform1(_locUseOverride, 1); // true
                    _gl.Uniform4(_locColorOverride, 203f / 255f, 203f / 255f, 212f / 255f, 1.0f);
                    _toolpathRenderer?.DrawToolpathRange(_gl, 0, state.CompletedSegmentIndex);

                    // Draw selected segment
                    if (state.SelectedSegmentIndex >= 0)
                    {
                        _gl.Uniform4(_locColorOverride, 1.0f, 1.0f, 0.0f, 1.0f);
                        _toolpathRenderer?.DrawToolpathRange(_gl, state.SelectedSegmentIndex, 1);
                    }

                    _gl.Enable(EnableCap.DepthTest);
                }
                else if (state.SelectedSegmentIndex >= 0)
                {
                    _gl.Disable(EnableCap.DepthTest);
                    _gl.Uniform1(_locUseOverride, 1);
                    _gl.Uniform4(_locColorOverride, 1.0f, 1.0f, 0.0f, 1.0f);
                    _toolpathRenderer?.DrawToolpathRange(_gl, state.SelectedSegmentIndex, 1);
                    _gl.Enable(EnableCap.DepthTest);
                }

                // Upload spindle image texture if changed (UI thread loaded bitmap, we upload to GPU here)
                if (_needsSpindleTextureUpload)
                {
                    _needsSpindleTextureUpload = false;
                    var bitmap = _spindleImageProvider.Bitmap;
                    if (bitmap != null)
                        _spindleImageRenderer?.UploadTexture(bitmap);
                    else
                        _spindleImageRenderer?.ClearTexture();
                }

                // Draw spindle
                if (state.SpindlePosition.HasValue)
                {
                    var scaleRef = GetScaleRef(state.Toolpath, state.MachineSettings);
                    bool drewImage = false;

                    // Try image-based spindle first
                    if (_spindleImageRenderer != null && _spindleImageRenderer.HasImage)
                    {
                        float bitLength = scaleRef * 0.03f;
                        float shaftLength = scaleRef * 0.07f;
                        var pos = state.SpindlePosition.Value;
                        var shaftTop = new Point3D(pos.X, pos.Y, pos.Z + bitLength + shaftLength);

                        var tipScreen = ProjectToScreen(pos, mvp, width, height);
                        var shaftTopScreen = ProjectToScreen(shaftTop, mvp, width, height);

                        if (tipScreen.HasValue && shaftTopScreen.HasValue)
                        {
                            // Restore main 3D shader after image draws (image renderer switches to text shader)
                            drewImage = _spindleImageRenderer.Draw(_gl, width, height,
                                tipScreen.Value, shaftTopScreen.Value);
                        }
                    }

                    // Fall back to procedural 3D spindle if no image or image too small
                    if (!drewImage)
                    {
                        _gl.UseProgram(_program);
                        float* mvpPtr2 = (float*)&mvp;
                        _gl.UniformMatrix4(_locMVP, 1, false, mvpPtr2);
                        _gl.Uniform1(_locUseOverride, 0);

                        _spindleRenderer?.Update(state.SpindlePosition.Value, scaleRef);
                        _spindleRenderer?.Draw(_gl);

                        // Crosshair (depth test off)
                        _gl.Disable(EnableCap.DepthTest);
                        _spindleRenderer?.DrawCrosshair(_gl);
                        _gl.Enable(EnableCap.DepthTest);
                    }
                }

                // Draw axis labels (screen-aligned textured quads)
                // Project axis endpoints from world space to screen space using the current MVP.
                var xScreen = ProjectToScreen(axisEndpoints.XEnd, mvp, width, height);
                var yScreen = ProjectToScreen(axisEndpoints.YEnd, mvp, width, height);
                var zScreen = ProjectToScreen(axisEndpoints.ZEnd, mvp, width, height);
                _axisLabelRenderer?.Draw(_gl, width, height, xScreen, yScreen, zScreen);

                // If using MSAA, blit (resolve) our multisampled FBO to Avalonia's single-sample FBO
                if (useMsaa)
                {
                    _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
                    _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)fb);
                    _gl.BlitFramebuffer(
                        0, 0, pixelW, pixelH,
                        0, 0, pixelW, pixelH,
                        (uint)ClearBufferMask.ColorBufferBit,
                        BlitFramebufferFilter.Linear);

                    // Restore Avalonia's FBO as current
                    _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
                }
            }
            catch (Exception ex)
            {
                // Don't let GL errors crash the compositor thread
                System.Diagnostics.Debug.WriteLine($"[GcodeGlRenderControl] Render error: {ex.Message}");
            }
        }

        private static float GetScaleRef(ToolpathData? toolpath, MachineSettings? machineSettings)
        {
            if (toolpath != null && toolpath.MaxDimension > 0)
                return toolpath.MaxDimension;
            if (machineSettings != null && machineSettings.DisplayXSize > 0)
                return (float)Math.Max(machineSettings.DisplayXSize, machineSettings.DisplayYSize);
            return 600f;
        }

        // =====================================================================
        // Mouse handling
        // =====================================================================

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
            _lastPointerPos = point.Position;
            _pressStartPos = point.Position;

            if (point.Properties.IsLeftButtonPressed)
                _isLeftDragging = true;
            if (point.Properties.IsRightButtonPressed)
                _isRightDragging = true;
            if (point.Properties.IsMiddleButtonPressed)
                _isMiddleDragging = true;

            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_lastPointerPos == null) return;

            var currentPos = e.GetCurrentPoint(this).Position;
            var deltaX = (float)(currentPos.X - _lastPointerPos.Value.X);
            var deltaY = (float)(currentPos.Y - _lastPointerPos.Value.Y);

            if (_isLeftDragging)
            {
                _camera.Rotate(deltaX, deltaY);
                MarkDirty();
            }
            else if (_isRightDragging || _isMiddleDragging)
            {
                _camera.Pan(deltaX, deltaY);
                MarkDirty();
            }

            _lastPointerPos = currentPos;
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (_pressStartPos.HasValue)
            {
                var releasePos = e.GetCurrentPoint(this).Position;
                var dx = releasePos.X - _pressStartPos.Value.X;
                var dy = releasePos.Y - _pressStartPos.Value.Y;
                if (dx * dx + dy * dy < 25)
                {
                    OnSegmentClicked((float)releasePos.X, (float)releasePos.Y);
                }
            }

            _isLeftDragging = false;
            _isRightDragging = false;
            _isMiddleDragging = false;
            _lastPointerPos = null;
            _pressStartPos = null;
            e.Handled = true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            _camera.Zoom((float)e.Delta.Y);
            MarkDirty();
            e.Handled = true;
        }

        private void OnSegmentClicked(float screenX, float screenY)
        {
            var toolpath = Toolpath;
            if (toolpath == null || toolpath.Segments.Count == 0) return;

            var view = _camera.GetViewMatrix();
            var proj = _camera.GetProjectionMatrix((float)Bounds.Width, (float)Bounds.Height);
            var viewProj = view * proj;
            float width = (float)Bounds.Width;
            float height = (float)Bounds.Height;

            float bestDistSq = float.MaxValue;
            int bestIndex = -1;

            for (int i = 0; i < toolpath.Segments.Count; i++)
            {
                var seg = toolpath.Segments[i];
                var p1 = ProjectToScreen(seg.Start, viewProj, width, height);
                var p2 = ProjectToScreen(seg.End, viewProj, width, height);

                if (!p1.HasValue || !p2.HasValue) continue;

                float distSq = DistanceToLineSegmentSq(screenX, screenY, p1.Value, p2.Value);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0 && bestDistSq <= 100f)
            {
                SelectedSegmentIndex = bestIndex;
                var segment = toolpath.Segments[bestIndex];
                SelectedLineInfo = $"Line: {segment.SourceLineIndex + 1}";
            }
            else
            {
                SelectedSegmentIndex = -1;
                SelectedLineInfo = "";
            }
        }

        internal static Vector2? ProjectToScreen(Point3D point, Matrix4x4 viewProj, float width, float height)
        {
            var v = new Vector4(point.X, point.Y, point.Z, 1f);
            var clip = Vector4.Transform(v, viewProj);
            if (clip.W <= 0.001f) return null;
            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;
            float screenX = (ndcX + 1f) * 0.5f * width;
            float screenY = (1f - ndcY) * 0.5f * height;
            return new Vector2(screenX, screenY);
        }

        private static float DistanceToLineSegmentSq(float px, float py, Vector2 p1, Vector2 p2)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float lenSq = dx * dx + dy * dy;

            if (lenSq < 0.001f)
            {
                float ex = px - p1.X;
                float ey = py - p1.Y;
                return ex * ex + ey * ey;
            }

            float t = ((px - p1.X) * dx + (py - p1.Y) * dy) / lenSq;
            t = Math.Clamp(t, 0f, 1f);
            float closestX = p1.X + t * dx;
            float closestY = p1.Y + t * dy;
            float distX = px - closestX;
            float distY = py - closestY;
            return distX * distX + distY * distY;
        }
    }
}
