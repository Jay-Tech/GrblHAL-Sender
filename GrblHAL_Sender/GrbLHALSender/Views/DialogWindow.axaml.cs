using Avalonia.Controls;

namespace GrbLHALSender.Views;

public partial class DialogWindow : Window
{
    public DialogWindow()
    {
        InitializeComponent();
    }

    public DialogWindow(string title, Control content, double width = 600, double height = 400) : this()
    {
        Title = title;
        Width = width;
        Height = height;
        DialogContent.Content = content;
        MinWidth = width;
        MinHeight = height;
    }
}
