using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using GrbLHAL_Sender.ViewModels;
using ReactiveUI;


namespace GrbLHAL_Sender.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        
        //this.WhenActivated(d =>
        //{
        //    d(ViewModel.SelectFilesInteraction.RegisterHandler(this.InteractionHandler));
        //});
    }
   
    IDisposable? _selectFilesInteractionDisposable;

    protected override void OnDataContextChanged(EventArgs e)
    {
        _selectFilesInteractionDisposable?.Dispose();

        if (DataContext is MainViewModel vm)
        {
            _selectFilesInteractionDisposable =
                vm.JobViewModel.SelectFilesInteraction.RegisterHandler(InteractionHandler);
        }
        base.OnDataContextChanged(e);
    }

    private void ToolLb_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        SplitB.Flyout?.Hide();
    }
    private async Task<IReadOnlyList<IStorageFile>?> InteractionHandler(string input)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var storageFiles = await topLevel!.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions()
            {
                AllowMultiple = true,
                Title = input
            });
        return storageFiles;
    }

    //private async Task InteractionHandler(InteractionContext<string?, string[]?> context)
    //{
    //    // Get our parent top level control in order to get the needed service (in our sample the storage provider. Can also be the clipboard etc.)
    //    var topLevel = TopLevel.GetTopLevel(this);

    //    var storageFiles = await topLevel!.StorageProvider
    //        .OpenFilePickerAsync(
    //            new FilePickerOpenOptions()
    //            {
    //                AllowMultiple = true,
    //                Title = context.Input
    //            });

    //    context.SetOutput(storageFiles?.Select(x => x.Name).ToArray());
    //}
    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        //throw new NotImplementedException();
    }
}
