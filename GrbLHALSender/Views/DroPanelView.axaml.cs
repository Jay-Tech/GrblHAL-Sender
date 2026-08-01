using Avalonia;
using Avalonia.Controls;

namespace GrbLHALSender.Views;

/// <summary>
/// Home, DRO readouts, WCS selection and MDI. Binds to MainViewModel, inherited from
/// whichever canvas hosts it.
/// </summary>
public partial class DroPanelView : UserControl
{
    /// <summary>
    /// Whether the WCS block is shown. The portrait canvas turns it off and hosts
    /// <see cref="WcsPanelView"/> in its control block instead, which fills the space between
    /// the two side panels and shortens this one by roughly 250px.
    /// </summary>
    public static readonly StyledProperty<bool> ShowWcsProperty =
        AvaloniaProperty.Register<DroPanelView, bool>(nameof(ShowWcs), defaultValue: true);

    public bool ShowWcs
    {
        get => GetValue(ShowWcsProperty);
        set => SetValue(ShowWcsProperty, value);
    }

    public DroPanelView()
    {
        InitializeComponent();
        WcsSection.IsVisible = ShowWcs;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Applied here rather than by binding: the section is a named child of this control,
        // so a compiled binding would have to reach back out through the MainViewModel context.
        if (change.Property == ShowWcsProperty)
            WcsSection.IsVisible = ShowWcs;
    }
}
