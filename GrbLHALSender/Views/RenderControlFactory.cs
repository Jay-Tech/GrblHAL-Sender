using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using GrbLHALSender.Views.GcodeGlRenderControl;
using GrbLHALSender.Views.GcodeRenderControl;
using System;

namespace GrbLHALSender.Views
{
    /// <summary>
    /// Creates the appropriate GCode render control based on renderer selection
    /// and wires up all property bindings. Only the selected control is instantiated,
    /// avoiding unnecessary resource usage (e.g. OpenGL context allocation when using Software).
    /// </summary>
    public static class RenderControlFactory
    {
        public static Control CreateSoftwareRenderer()
        {
            var control = new GcodeRenderControl.GcodeRenderControl
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            };

            BindCommonProperties(control,
                GcodeRenderControl.GcodeRenderControl.ToolpathProperty,
                GcodeRenderControl.GcodeRenderControl.SpindlePositionProperty,
                GcodeRenderControl.GcodeRenderControl.MachineSettingsProperty,
                GcodeRenderControl.GcodeRenderControl.WorkCoordinateOffsetProperty,
                GcodeRenderControl.GcodeRenderControl.CompletedSegmentIndexProperty,
                GcodeRenderControl.GcodeRenderControl.SelectedLineInfoProperty,
                GcodeRenderControl.GcodeRenderControl.SelectedSegmentIndexProperty,
                GcodeRenderControl.GcodeRenderControl.SpindleImagePathProperty,
                GcodeRenderControl.GcodeRenderControl.UseAntiAliasProperty);

            return control;
        }

        /// <param name="onOpenGlUnavailable">
        /// Invoked on the UI thread if OpenGL cannot be brought up on this machine, so the
        /// caller can swap in the software renderer. Without it a machine with a broken GLES
        /// stack shows a silently blank 3D pane.
        /// </param>
        public static Control CreateHardwareRenderer(Action? onOpenGlUnavailable = null)
        {
            var control = new GcodeGlRenderControl.GcodeGlRenderControl
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            };

            if (onOpenGlUnavailable != null)
                control.OpenGlUnavailable += (_, _) => onOpenGlUnavailable();

            BindCommonProperties(control,
                GcodeGlRenderControl.GcodeGlRenderControl.ToolpathProperty,
                GcodeGlRenderControl.GcodeGlRenderControl.SpindlePositionProperty,
                GcodeGlRenderControl.GcodeGlRenderControl.MachineSettingsProperty,
                GcodeGlRenderControl.GcodeGlRenderControl.WorkCoordinateOffsetProperty,
                GcodeGlRenderControl.GcodeGlRenderControl.CompletedSegmentIndexProperty,
                GcodeGlRenderControl.GcodeGlRenderControl.SelectedLineInfoProperty,
                GcodeGlRenderControl.GcodeGlRenderControl.SelectedSegmentIndexProperty,
                GcodeGlRenderControl.GcodeGlRenderControl.SpindleImagePathProperty,
                GcodeGlRenderControl.GcodeGlRenderControl.UseAntiAliasProperty);

            return control;
        }

        /// <summary>
        /// Binds all shared render control properties to the MainViewModel paths.
        /// Both controls use identical property names, so the binding paths are the same.
        /// The DataContext is inherited from the parent ContentControl in the visual tree.
        /// </summary>
        private static void BindCommonProperties(
            Control control,
            AvaloniaProperty toolpathProp,
            AvaloniaProperty spindlePosProp,
            AvaloniaProperty machineSettingsProp,
            AvaloniaProperty wcoProp,
            AvaloniaProperty completedSegProp,
            AvaloniaProperty selectedLineInfoProp,
            AvaloniaProperty selectedSegmentProp,
            AvaloniaProperty spindleImageProp,
            AvaloniaProperty useAntiAliasProp)
        {
            control.Bind(toolpathProp, new Binding("JobViewModel.ToolpathData"));
            control.Bind(spindlePosProp, new Binding("JobViewModel.CurrentSpindlePosition"));
            control.Bind(machineSettingsProp, new Binding("MachineSettings"));
            control.Bind(wcoProp, new Binding("JobViewModel.WorkCoordinateOffset"));
            control.Bind(completedSegProp, new Binding("JobViewModel.CompletedSegmentIndex"));
            control.Bind(selectedLineInfoProp,
                new Binding("JobViewModel.SelectedLineInfo") { Mode = BindingMode.TwoWay });
            control.Bind(selectedSegmentProp,
                new Binding("JobViewModel.SelectedSegmentIndex") { Mode = BindingMode.TwoWay });
            control.Bind(spindleImageProp, new Binding("SpindleImagePath"));
            control.Bind(useAntiAliasProp, new Binding("UseAntiAlias"));
        }
    }
}
