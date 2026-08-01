using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GrbLHALSender.ViewModels;
using System;

namespace GrbLHALSender.Views;

/// <summary>
/// Feed rate, jog pad, step size, tool and R-ATC, spindle and overrides. Binds to
/// MainViewModel, inherited from whichever canvas hosts it.
/// </summary>
public partial class JogPanelView : UserControl
{
    /// <summary>
    /// Whether the spindle block is shown. The portrait canvas turns it off and hosts
    /// <see cref="SpindlePanelView"/> in its control block instead, which fills the space
    /// between the two side panels and shortens this one by roughly 300px.
    /// </summary>
    public static readonly StyledProperty<bool> ShowSpindleProperty =
        AvaloniaProperty.Register<JogPanelView, bool>(nameof(ShowSpindle), defaultValue: true);

    public bool ShowSpindle
    {
        get => GetValue(ShowSpindleProperty);
        set => SetValue(ShowSpindleProperty, value);
    }

    private MainViewModel? _viewModel;

    // Jog press-and-hold state
    private DispatcherTimer? _jogHoldTimer;
    private bool _jogHoldActive;
    private string? _jogHoldAxis;
    private bool _jogHoldPositive;

    public JogPanelView()
    {
        InitializeComponent();

        // Set up press-and-hold for continuous jog on all jog buttons
        SetupJogButton(XDown, "X", false);
        SetupJogButton(Xup, "X", true);
        SetupJogButton(YUp, "Y", true);
        SetupJogButton(YDown, "Y", false);
        SetupJogButton(ZUp, "Z", true);
        SetupJogButton(ZDown, "Z", false);

        SpindleSection.IsVisible = ShowSpindle;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        _viewModel = DataContext as MainViewModel;
        base.OnDataContextChanged(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Applied here rather than by binding: the section is a named child of this control,
        // so a compiled binding would have to reach back out through the MainViewModel context.
        if (change.Property == ShowSpindleProperty)
            SpindleSection.IsVisible = ShowSpindle;
    }

    private void SetupJogButton(Button button, string axis, bool positive)
    {
        button.AddHandler(PointerPressedEvent, (_, e) => JogButton_PointerPressed(axis, positive), RoutingStrategies.Tunnel);
        button.AddHandler(PointerReleasedEvent, (_, e) => JogButton_PointerReleased(e), RoutingStrategies.Tunnel);
        button.AddHandler(PointerCaptureLostEvent, (_, _) => JogButton_CaptureLost(), RoutingStrategies.Tunnel);
    }

    private void JogButton_PointerPressed(string axis, bool positive)
    {
        _jogHoldActive = false;
        _jogHoldAxis = axis;
        _jogHoldPositive = positive;

        _jogHoldTimer?.Stop();
        _jogHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _jogHoldTimer.Tick += (_, _) =>
        {
            _jogHoldTimer!.Stop();
            _jogHoldActive = true;

            if (_jogHoldPositive)
                _viewModel?.JogContinuousPos(_jogHoldAxis!);
            else
                _viewModel?.JogContinuousNeg(_jogHoldAxis!);
        };
        _jogHoldTimer.Start();
    }

    private void JogButton_PointerReleased(PointerReleasedEventArgs e)
    {
        _jogHoldTimer?.Stop();

        if (_jogHoldActive)
        {
            _viewModel?.JogCancel();
            _jogHoldActive = false;
            e.Handled = true;
        }
    }

    private void JogButton_CaptureLost()
    {
        _jogHoldTimer?.Stop();

        if (_jogHoldActive)
        {
            _viewModel?.JogCancel();
            _jogHoldActive = false;
        }
    }

    private void ToolLb_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        SplitB.Flyout?.Hide();
    }
}
