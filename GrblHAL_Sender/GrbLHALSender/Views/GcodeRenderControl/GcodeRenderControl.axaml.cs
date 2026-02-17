using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GrbLHALSender.Gcode;
using GrbLHALSender.Settings;

namespace GrbLHALSender.Views.GcodeRenderControl
{
    public partial class GcodeRenderControl : UserControl
    {
        public static readonly StyledProperty<ToolpathData?> ToolpathProperty =
            AvaloniaProperty.Register<GcodeRenderControl, ToolpathData?>(nameof(Toolpath));

        public static readonly StyledProperty<Point3D?> SpindlePositionProperty =
            AvaloniaProperty.Register<GcodeRenderControl, Point3D?>(nameof(SpindlePosition));

        public static readonly StyledProperty<MachineSettings?> MachineSettingsProperty =
            AvaloniaProperty.Register<GcodeRenderControl, MachineSettings?>(nameof(MachineSettings));

        public static readonly StyledProperty<Point3D?> WorkCoordinateOffsetProperty =
            AvaloniaProperty.Register<GcodeRenderControl, Point3D?>(nameof(WorkCoordinateOffset));

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

        private readonly Camera3D _camera = new();
        private readonly ToolpathSceneCache _sceneCache = new();
        private Point? _lastPointerPos;
        private bool _isLeftDragging;
        private bool _isRightDragging;
        private bool _isMiddleDragging;
        private bool _fitted;

        public GcodeRenderControl()
        {
            InitializeComponent();
            Background = Brushes.Transparent; // Required for hit testing
            ClipToBounds = true;
        }

        static GcodeRenderControl()
        {
            AffectsRender<GcodeRenderControl>(ToolpathProperty, SpindlePositionProperty, MachineSettingsProperty, WorkCoordinateOffsetProperty);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ToolpathProperty || change.Property == MachineSettingsProperty ||
                change.Property == WorkCoordinateOffsetProperty)
            {
                _fitted = false;
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

            var toolpath = Toolpath;
            var machine = MachineSettings;

            var wco = WorkCoordinateOffset;

            if (!_fitted)
            {
                if (toolpath != null && toolpath.Segments.Count > 0)
                {
                    // Toolpath loaded — fit to toolpath (uses machine bounds if available)
                    _camera.FitToView(toolpath, (float)Bounds.Width, (float)Bounds.Height, machine, wco);
                    _fitted = true;
                }
                else if (machine != null && machine.XSize > 0 && machine.YSize > 0)
                {
                    // No toolpath yet — fit to machine grid at startup
                    _camera.FitToMachine(machine, (float)Bounds.Width, (float)Bounds.Height, wco);
                    _fitted = true;
                }
            }

            var op = new GcodeRenderOperation(
                new Rect(0, 0, Bounds.Width, Bounds.Height),
                toolpath,
                _camera,
                _sceneCache,
                SpindlePosition,
                machine,
                WorkCoordinateOffset);

            context.Custom(op);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
            _lastPointerPos = point.Position;

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
                InvalidateVisual();
            }
            else if (_isRightDragging || _isMiddleDragging)
            {
                _camera.Pan(deltaX, deltaY);
                InvalidateVisual();
            }

            _lastPointerPos = currentPos;
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _isLeftDragging = false;
            _isRightDragging = false;
            _isMiddleDragging = false;
            _lastPointerPos = null;
            e.Handled = true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            _camera.Zoom((float)e.Delta.Y);
            InvalidateVisual();
            e.Handled = true;
        }
    }
}
