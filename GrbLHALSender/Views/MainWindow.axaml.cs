using Avalonia.Controls;

namespace GrbLHALSender.Views;

/// <summary>
/// The desktop shell: window chrome, and <see cref="RootCanvasHost"/> for everything else.
/// </summary>
/// <remarks>
/// This used to own the canvas selection, the scale-to-fit transform, the renderer overlay
/// switch and the touch text-handle fix. None of it was window-specific, and all of it now
/// lives in <see cref="RootCanvasHost"/> where a host without windows can reach it too.
/// </remarks>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
