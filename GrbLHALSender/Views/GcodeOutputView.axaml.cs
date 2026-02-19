using Avalonia.Controls;
using Avalonia.Threading;
using GrbLHALSender.ViewModels;
using System;

namespace GrbLHALSender.Views;

public partial class GcodeOutputView : UserControl
{
    private DispatcherTimer? _scrollTimer;
    private int _lastScrolledIndex = -1;

    public GcodeOutputView()
    {
        InitializeComponent();

        // Throttled scroll timer — only scrolls the ListBox when visible, at ~4 Hz.
        // This replaces the SelectedIndex binding which caused expensive layout passes
        // on every update (even when the panel was hidden).
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _scrollTimer.Tick += ScrollTimerTick;
        _scrollTimer.Start();
    }

    private void ScrollTimerTick(object? sender, EventArgs e)
    {
        // Only scroll if we're actually visible on screen
        if (!IsVisible || !IsEffectivelyVisible) return;

        if (DataContext is not JobViewModel vm) return;

        var index = vm.GcodeFileIndex;
        if (index == _lastScrolledIndex) return;
        if (index < 0 || index >= vm.GCodeOutPut.Count) return;

        _lastScrolledIndex = index;
        ListBoxGCode.SelectedIndex = index;
        ListBoxGCode.ScrollIntoView(vm.GCodeOutPut[index]);
    }
}
