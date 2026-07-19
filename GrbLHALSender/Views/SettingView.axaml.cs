using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GrbLHALSender.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GrbLHALSender.Views
{
    public partial class SettingView : UserControl
    {
        private IDisposable? _importInteractionDisposable;
        private IDisposable? _exportInteractionDisposable;

        // Drag-to-scroll (touch panning) state
        private bool _panPending;
        private bool _panning;
        private Point _panStartPoint;
        private Vector _panStartOffset;
        private const double PanThreshold = 12;

        public SettingView()
        {
            InitializeComponent();
            SetupDragToScroll();
        }

        /// <summary>
        /// Lets the user scroll the settings list by dragging anywhere on it,
        /// instead of having to hit the scrollbar. A movement threshold keeps
        /// taps and clicks working normally on the row editors: only once the
        /// pointer has moved vertically past the threshold does the gesture
        /// become a pan, after which events are swallowed so the drag doesn't
        /// also select text or press buttons.
        /// </summary>
        private void SetupDragToScroll()
        {
            SettingScroll.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(SettingScroll).Properties.IsLeftButtonPressed) return;
                _panPending = true;
                _panning = false;
                _panStartPoint = e.GetPosition(SettingScroll);
                _panStartOffset = SettingScroll.Offset;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

            SettingScroll.AddHandler(PointerMovedEvent, (_, e) =>
            {
                if (!_panPending) return;

                var pos = e.GetPosition(SettingScroll);
                var dy = pos.Y - _panStartPoint.Y;

                if (!_panning && Math.Abs(dy) > PanThreshold)
                {
                    _panning = true;
                    e.Pointer.Capture(SettingScroll);
                }

                if (_panning)
                {
                    SettingScroll.Offset = new Vector(
                        _panStartOffset.X,
                        Math.Max(0, _panStartOffset.Y - dy));
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

            SettingScroll.AddHandler(PointerReleasedEvent, (_, e) =>
            {
                if (_panning)
                {
                    e.Pointer.Capture(null);
                    e.Handled = true; // drag was a scroll — not a click on a row control
                }
                _panPending = false;
                _panning = false;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

            SettingScroll.AddHandler(PointerCaptureLostEvent, (_, _) =>
            {
                _panPending = false;
                _panning = false;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            _importInteractionDisposable?.Dispose();
            _exportInteractionDisposable?.Dispose();

            if (DataContext is SettingsViewModel vm)
            {
                _importInteractionDisposable =
                    vm.ImportFileInteraction.RegisterHandler(ImportFileHandler);
                _exportInteractionDisposable =
                    vm.ExportFileInteraction.RegisterHandler(ExportFileHandler);
            }

            base.OnDataContextChanged(e);
        }

        private async Task<IStorageFile?> ImportFileHandler(string title)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return null;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    Title = title,
                    FileTypeFilter = new List<FilePickerFileType>
                    {
                        new("Text Files") { Patterns = new[] { "*.txt" } },
                        new("All Files") { Patterns = new[] { "*.*" } }
                    }
                });

            return files.Count > 0 ? files[0] : null;
        }

        private async Task<IStorageFile?> ExportFileHandler(string title)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return null;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = title,
                    SuggestedFileName = "GrblHALSettings.txt",
                    DefaultExtension = "txt",
                    FileTypeChoices = new List<FilePickerFileType>
                    {
                        new("Text Files") { Patterns = new[] { "*.txt" } },
                        new("All Files") { Patterns = new[] { "*.*" } }
                    }
                });

            return file;
        }
    }
}
