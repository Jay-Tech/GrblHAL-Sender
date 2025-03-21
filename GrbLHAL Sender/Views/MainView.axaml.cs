using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using DynamicData;
using Eremex.AvaloniaUI.Controls3D;
using GrbLHAL_Sender.ViewModels;
using ReactiveUI;


namespace GrbLHAL_Sender.Views;

public partial class MainView : UserControl
{
    private MainViewModel _viewModel;
    public MainView()
    {
        InitializeComponent();

        //this.WhenActivated(d =>
        //{
        //    d(ViewModel.SelectFilesInteraction.RegisterHandler(this.InteractionHandler));
        //});

     

    }
   
    IDisposable? _selectFilesInteractionDisposable;

    private GeometryModel3D model;
  
    //Transform the model
   

    protected override void OnDataContextChanged(EventArgs e)
    {
        _selectFilesInteractionDisposable?.Dispose();

        if (DataContext is MainViewModel vm)
        {
            _viewModel = vm;
            _selectFilesInteractionDisposable =
                vm.JobViewModel.SelectFilesInteraction.RegisterHandler(InteractionHandler);
        }
        base.OnDataContextChanged(e);
    }

    private void ToolLb_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
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

    //private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    //{
    //    e.Handled = true;
    //    SplitB.Flyout?.Hide();
    //    Debug.WriteLine("flyout");
    //}

   
}
