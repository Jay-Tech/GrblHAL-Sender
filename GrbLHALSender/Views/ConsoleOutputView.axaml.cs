using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using GrbLHALSender.ViewModels;
using System;

namespace GrbLHALSender.Views;

public partial class ConsoleOutputView : UserControl
{
    public ConsoleOutputView()
    {
        InitializeComponent();
    }

    private async void OnCopyAllClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null) return;

        try
        {
            var text = string.Join(Environment.NewLine, vm.ConsoleOutput);
            await topLevel.Clipboard.SetTextAsync(text);
        }
        catch
        {
            // Clipboard access can fail transiently (e.g. another app holding it).
            // Silent failure is fine — user can retry.
        }
    }
}
