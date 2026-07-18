using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GrbLHALSender.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GrbLHALSender.Views;

public partial class FirmwareView : UserControl
{
    private IDisposable? _selectHexDisposable;

    public FirmwareView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        DisposeHandlers();

        if (DataContext is FirmwareViewModel vm)
        {
            _selectHexDisposable =
                vm.SelectHexFileInteraction.RegisterHandler(OpenHexFileHandler);
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
        _selectHexDisposable?.Dispose();
        _selectHexDisposable = null;
    }

    private async Task<IStorageFile?> OpenHexFileHandler(string title)
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
                    new("Firmware Files") { Patterns = new[] { "*.hex" } },
                    new("All Files") { Patterns = new[] { "*.*" } }
                }
            });

        return files.Count > 0 ? files[0] : null;
    }
}
