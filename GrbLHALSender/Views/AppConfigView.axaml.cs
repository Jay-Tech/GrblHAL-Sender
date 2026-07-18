using Avalonia.Controls;
using GrbLHALSender.ViewModels;

namespace GrbLHALSender.Views;

public partial class AppConfigView : UserControl
{
    public AppConfigView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the DataContext of the embedded SdCardView to the given SdCardViewModel.
    /// Called from DialogButtonView when creating the Utility dialog.
    /// </summary>
    public void SetSdCardViewModel(SdCardViewModel sdCardViewModel)
    {
        SdCardViewControl.DataContext = sdCardViewModel;
    }

    /// <summary>
    /// Sets the DataContext of the embedded FirmwareView to the given FirmwareViewModel.
    /// Called from DialogButtonView when creating the Utility dialog.
    /// </summary>
    public void SetFirmwareViewModel(FirmwareViewModel firmwareViewModel)
    {
        FirmwareViewControl.DataContext = firmwareViewModel;
    }
}