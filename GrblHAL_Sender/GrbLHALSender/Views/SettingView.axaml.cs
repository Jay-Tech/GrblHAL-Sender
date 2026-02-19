using Avalonia.Controls;
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

        public SettingView()
        {
            InitializeComponent();
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
