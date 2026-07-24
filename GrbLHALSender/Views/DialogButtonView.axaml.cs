using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using GrbLHALSender.ViewModels;
using System;

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
        {
            _viewModel.OpenDialogRequested -= OnOpenDialogRequested;
            _viewModel.CloseDialogRequested -= OnCloseDialogRequested;
        }

        if (DataContext is DialogViewModel vm)
        {
            _viewModel = vm;
            _viewModel.OpenDialogRequested += OnOpenDialogRequested;
            _viewModel.CloseDialogRequested += OnCloseDialogRequested;
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

    /// <summary>
    /// Gets the parent MainView instance.
    /// </summary>
    private MainView? GetMainView() => this.FindAncestorOfType<MainView>();

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

            case DialogType.Macro:
                var macroView = new MacroView();
                var macroMainVm = GetMainViewModel();
                if (macroMainVm != null)
                {
                    macroView.DataContext = macroMainVm.MacroViewModel;
                    macroMainVm.MacroViewModel.DisplayMacroControl = true;
                }
                return (macroView, 450, 500);

            case DialogType.Probe:
                var probeView = new ProbeView
                {
                    MinHeight = 595,
                    MinWidth = 480
                };
                var probeMainVm = GetMainViewModel();
                if (probeMainVm != null)
                    probeView.DataContext = probeMainVm.ProbeViewModel;
                return (probeView, 480, 595);

            case DialogType.Surfacing:
                var surfacingView = new SurfacingView
                {
                    MinHeight = 640,
                    MinWidth = 560
                };
                var surfacingMainVm = GetMainViewModel();
                if (surfacingMainVm != null)
                    surfacingView.DataContext = surfacingMainVm.SurfacingViewModel;
                return (surfacingView, 560, 640);

            case DialogType.AppConfig:
                var appConfigView = new AppConfigView();
                var appConfigViewVm = GetMainViewModel();
                if (appConfigViewVm != null)
                {
                    appConfigView.DataContext = appConfigViewVm.AppConfigViewModel;
                    appConfigView.SetSdCardViewModel(appConfigViewVm.SdCardViewModel);
                }
                return (appConfigView, 675, 725);
            default:
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

    private void OnCloseDialogRequested(DialogType dialogType)
    {
        var mainView = GetMainView();
        if (mainView == null) return;

        // CloseHost runs the dialog's onClosed callback, which handles
        // MarkDialogClosed and any per-dialog cleanup/save side effects.
        var host = dialogType == DialogType.Console ? mainView.ConsoleHost : mainView.DialogHost;
        host.CloseHost();
    }

    private void OnOpenDialogRequested(DialogType dialogType)
    {
        var mainView = GetMainView();
        if (mainView == null) return;

        var (content, width, height) = CreateDialogContent(dialogType);

        // Console gets its own host so it can stay open for monitoring while
        // a tool dialog (probe, macro, ...) is up. Everything else shares the
        // single-slot host: opening one closes whichever was open.
        var host = dialogType == DialogType.Console ? mainView.ConsoleHost : mainView.DialogHost;

        // Wire up CloseAction for any dialog whose ViewModel implements IDialogCloseable
        if (content.DataContext is IDialogCloseable closeable)
            closeable.CloseAction = () => host.CloseHost();

        // Wire up CloseConsoleAction for Console (DataContext is MainViewModel, not IDialogCloseable)
        if (dialogType == DialogType.Console && content.DataContext is MainViewModel consoleMainVm)
            consoleMainVm.CloseConsoleAction = () => host.CloseHost();

        _viewModel?.MarkDialogOpened(dialogType);

        host.ShowDialogContent($"{dialogType}", content, width, height, onClosed: () =>
        {
            _viewModel?.MarkDialogClosed(dialogType);

            // Clean up state when dialogs close
            if (dialogType == DialogType.Console)
            {
                var vm = GetMainViewModel();
                if (vm != null)
                    vm.ShowConsole = false;
            }
            else if (dialogType == DialogType.Macro)
            {
                var vm = GetMainViewModel();
                if (vm != null)
                    vm.MacroViewModel.DisplayMacroControl = false;
            }
            else if (dialogType == DialogType.Probe)
            {
                // Save probe settings when dialog closes
                var vm = GetMainViewModel();
                vm?.OpenProbeCommand.Execute(null);
            }
        });
    }
}
