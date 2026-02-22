using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using GrbLHALSender.ViewModels;
using System;
using System.Collections.Generic;

namespace GrbLHALSender.Views;

public partial class DialogButtonView : UserControl
{
    private DialogViewModel? _viewModel;
    private readonly Dictionary<DialogType, Window> _openWindows = new();

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
            _viewModel.BringDialogFrontRequested -= BringDialogFrontRequested;
        }
        

        if (DataContext is DialogViewModel vm)
        {
            _viewModel = vm;
            _viewModel.OpenDialogRequested += OnOpenDialogRequested;
            _viewModel.BringDialogFrontRequested += BringDialogFrontRequested;
        }

        base.OnDataContextChanged(e);
    }

    private void BringDialogFrontRequested(DialogType dialog)
    {
        if (_openWindows.TryGetValue(dialog, out var window))
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Activate();
        }
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
           
            case DialogType.AppConfig:
                var appConfigView = new AppConfigView();
                var appConfigViewVm = GetMainViewModel();
                if (appConfigViewVm != null)
                {
                    appConfigView.DataContext = appConfigViewVm.AppConfigViewModel;
                    appConfigView.SetSdCardViewModel(appConfigViewVm.SdCardViewModel);
                }
                return (appConfigView, 650, 650);
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

        // Wire up CloseAction for any dialog whose ViewModel implements IDialogCloseable
        if (content.DataContext is IDialogCloseable closeable)
        {
            closeable.CloseAction = () => dialogWindow.Close();
        }

        // Wire up CloseConsoleAction for Console (DataContext is MainViewModel, not IDialogCloseable)
        if (dialogType == DialogType.Console && content.DataContext is MainViewModel consoleMainVm)
        {
            consoleMainVm.CloseConsoleAction = () => dialogWindow.Close();
        }

        // Track open/close state
        _openWindows[dialogType] = dialogWindow;
        _viewModel?.MarkDialogOpened(dialogType);
        dialogWindow.Closed += (_, _) =>
        {
            _openWindows.Remove(dialogType);
            _viewModel?.MarkDialogClosed(dialogType);

            // Clean up state when dialogs close
            if (dialogType == DialogType.Console)
            {
                var mainVm = GetMainViewModel();
                if (mainVm != null)
                    mainVm.ShowConsole = false;
            }
            else if (dialogType == DialogType.Macro)
            {
                var mainVm = GetMainViewModel();
                if (mainVm != null)
                    mainVm.MacroViewModel.DisplayMacroControl = false;
            }
            else if (dialogType == DialogType.Probe)
            {
                // Save probe settings when dialog closes
                var mainVm = GetMainViewModel();
                mainVm?.OpenProbeCommand.Execute(null);
            }
        };

        // Enable virtual keyboard on double-tap for dialog TextBoxes
        var mainView = GetMainView();
        if (mainView != null)
        {
            dialogWindow.AddHandler(
                InputElement.DoubleTappedEvent,
                mainView.OnGlobalDoubleTapped,
                RoutingStrategies.Bubble,
                handledEventsToo: true);
        }

        switch (dialogType)
        {
            // Position Console dialog in the lower-left area
            case DialogType.Console when parentWindow != null:
                {
                    dialogWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                    var parentPos = parentWindow.Position;
                    dialogWindow.Position = new PixelPoint(
                        parentPos.X + 270,
                        parentPos.Y + 400
                    );
                    break;
                }
            case DialogType.Macro when parentWindow != null:
                {
                    dialogWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                    var parentPos = parentWindow.Position;
                    dialogWindow.Position = new PixelPoint(
                        parentPos.X + 700,
                        parentPos.Y + 475);
                    break;
                }
            case DialogType.Probe when parentWindow != null:
            {
                dialogWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                var parentPos = parentWindow.Position;
                dialogWindow.Position = new PixelPoint(
                    parentPos.X + 275,
                    parentPos.Y + 405
                );
                break;
            }
            case DialogType.AppConfig when parentWindow != null:
            {
                dialogWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                var parentPos = parentWindow.Position;
                dialogWindow.Position = new PixelPoint(
                    parentPos.X + 950,
                    parentPos.Y + 350
                );
                break;
            }
            default:
                break;
                //throw new ArgumentOutOfRangeException(nameof(dialogType), dialogType, null);
        }

        // Show non-modal, owned by parent window
        dialogWindow.Show(parentWindow);
    }
}
