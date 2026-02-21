using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GrbLHALSender.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GrbLHALSender.Views;

public partial class SdCardView : UserControl
{
    private IDisposable? _selectFilesDisposable;
    private IDisposable? _saveFileDisposable;

    public SdCardView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        DisposeHandlers();

        if (DataContext is SdCardViewModel vm)
        {
            _selectFilesDisposable =
                vm.SelectFilesInteraction.RegisterHandler(OpenFileHandler);
            _saveFileDisposable =
                vm.SaveFileInteraction.RegisterHandler(SaveFileHandler);
        }

        base.OnDataContextChanged(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Clean up interaction handlers when the dialog (and this view) closes,
        // so the next dialog open can re-register fresh handlers.
        DisposeHandlers();
        base.OnDetachedFromVisualTree(e);
    }

    private void DisposeHandlers()
    {
        _selectFilesDisposable?.Dispose();
        _selectFilesDisposable = null;
        _saveFileDisposable?.Dispose();
        _saveFileDisposable = null;
    }

    private async Task<IReadOnlyList<IStorageFile>?> OpenFileHandler(string title)
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
                    new("G-Code Files") { Patterns = new[] { "*.nc", "*.ngc", "*.gcode", "*.tap", "*.cnc", "*.macro" } },
                    new("All Files") { Patterns = new[] { "*.*" } }
                }
            });

        return files.Count > 0 ? files : null;
    }

    private async Task<IStorageFile?> SaveFileHandler(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = title,
                DefaultExtension = "nc",
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new("G-Code Files") { Patterns = new[] { "*.nc", "*.ngc", "*.gcode", "*.tap", "*.cnc" } },
                    new("All Files") { Patterns = new[] { "*.*" } }
                }
            });

        return file;
    }
}
