using Avalonia.Controls;
using CNC.Core;

namespace AvaloniaApplication1.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new GrblViewModel();
    }
}
