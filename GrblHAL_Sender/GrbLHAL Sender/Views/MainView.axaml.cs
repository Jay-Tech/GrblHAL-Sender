using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace GrbLHAL_Sender.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void ToolLb_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        
        SplitB.Flyout?.Hide();
    }
}
