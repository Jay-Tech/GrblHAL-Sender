using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using GrbLHALSender.ViewModels;

namespace GrbLHALSender.Views;

public partial class DialogButtonView : UserControl
{
    private DialogViewModel? _viewModel;

    public DialogButtonView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        // Unsubscribe from previous ViewModel
        if (_viewModel != null)
            _viewModel.OpenDialogRequested -= OnOpenDialogRequested;

        if (DataContext is DialogViewModel vm)
        {
            _viewModel = vm;
            _viewModel.OpenDialogRequested += OnOpenDialogRequested;
        }

        base.OnDataContextChanged(e);
    }

    /// <summary>
    /// Gets the MainViewModel from the parent MainView's DataContext.
    /// </summary>
    private MainViewModel? GetMainViewModel()
    {
        var mainView = this.FindAncestorOfType<MainView>();
        return mainView?.DataContext as MainViewModel;
    }

    private (Control content, double width, double height) CreateDialogContent(DialogType dialogType)
    {
        switch (dialogType)
        {
            case DialogType.Console:
                var consoleView = new ConsoleOutputView();
                var mainVm = GetMainViewModel();
                if (mainVm != null)
                {
                    consoleView.DataContext = mainVm;
                    mainVm.ShowConsole = true;
                }
                return (consoleView, 500, 600);

            default:
                // Placeholder for Probe, Macro, GCode dialogs
                var placeholder = new TextBlock
                {
                    Text = $"This is the {dialogType} dialog.",
                    FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(20)
                };
                return (placeholder, 600, 400);
        }
    }

    private void OnOpenDialogRequested(DialogType dialogType)
    {
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow == null) return;

        var (content, width, height) = CreateDialogContent(dialogType);

        var dialogWindow = new DialogWindow(
            title: $"{dialogType}",
            content: content,
            width: width,
            height: height
        );

        // Track open/close state
        _viewModel?.MarkDialogOpened(dialogType);
        dialogWindow.Closed += (_, _) =>
        {
            _viewModel?.MarkDialogClosed(dialogType);

            // Turn off console data flow when dialog closes
            if (dialogType == DialogType.Console)
            {
                var mainVm = GetMainViewModel();
                if (mainVm != null)
                    mainVm.ShowConsole = false;
            }
        };

        // Show non-modal, owned by parent window
        dialogWindow.Show(parentWindow);
    }
}
