using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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

    private void OnOpenDialogRequested(DialogType dialogType)
    {
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow == null) return;

        // Placeholder content — replace with real UserControls later
        var content = new TextBlock
        {
            Text = $"This is the {dialogType} dialog.",
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20)
        };

        var dialogWindow = new DialogWindow(
            title: $"{dialogType}",
            content: content,
            width: 600,
            height: 400
        );

        // Track open/close state
        _viewModel?.MarkDialogOpened(dialogType);
        dialogWindow.Closed += (_, _) =>
        {
            _viewModel?.MarkDialogClosed(dialogType);
        };

        // Show non-modal, owned by parent window
        dialogWindow.Show(parentWindow);
    }
}
