using Avalonia.Controls;
using GrbLHAL_Sender.ViewModels;

namespace GrbLHAL_Sender.Views.render3d;

public partial class Render3dView : UserControl
{
    public Render3dView()
    {
        DataContext = new Render3dViewModel();
        InitializeComponent();
    }
}