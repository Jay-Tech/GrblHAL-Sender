using Avalonia;
using Avalonia.Controls;
using GrbLHAL_Sender.ViewModels;
using System;
using Avalonia.Media;

namespace GrbLHAL_Sender.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
#if DEBUG

        this.AttachDevTools();

#endif
    }

    private void Control_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        double xScale = e.NewSize.Height / 1080;
        double yScale = e.NewSize.Width / 1920;
        var diff = Math.Abs( xScale - yScale) /2;
        double value = Math.Min(xScale, yScale);
        var s = (double)OnCoerceScaleValue(value);
        //LayoutTransformC.LayoutTransform = new ScaleTransform(xScale,value);
    }



    private double OnCoerceScaleValue(double value)
    {
        if (double.IsNaN(value))
            return 1.0f;

        value = Math.Max(0.1, value);
        return value;
    }
}
