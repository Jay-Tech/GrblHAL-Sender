using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using System;
using System.Globalization;

namespace GrbLHALSender.Views;

/// <summary>
/// Touch-friendly replacement for NumericUpDown: full-height minus/plus
/// RepeatButtons either side of a centered TextBox. Press-and-hold repeats.
/// </summary>
public partial class TouchNumericUpDown : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<TouchNumericUpDown, double>(nameof(Value),
            defaultBindingMode: BindingMode.TwoWay, coerce: CoerceValue);

    public static readonly StyledProperty<double> IncrementProperty =
        AvaloniaProperty.Register<TouchNumericUpDown, double>(nameof(Increment), 1);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<TouchNumericUpDown, double>(nameof(Minimum), double.MinValue);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<TouchNumericUpDown, double>(nameof(Maximum), double.MaxValue);

    public static readonly StyledProperty<string> FormatStringProperty =
        AvaloniaProperty.Register<TouchNumericUpDown, string>(nameof(FormatString), "0");

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Increment
    {
        get => GetValue(IncrementProperty);
        set => SetValue(IncrementProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public string FormatString
    {
        get => GetValue(FormatStringProperty);
        set => SetValue(FormatStringProperty, value);
    }

    static TouchNumericUpDown()
    {
        ValueProperty.Changed.AddClassHandler<TouchNumericUpDown>((c, _) => c.OnValueChanged());
        MinimumProperty.Changed.AddClassHandler<TouchNumericUpDown>((c, _) => c.CoerceAndRefresh());
        MaximumProperty.Changed.AddClassHandler<TouchNumericUpDown>((c, _) => c.CoerceAndRefresh());
    }

    public TouchNumericUpDown()
    {
        InitializeComponent();

        IncreaseButton.Click += (_, _) => Step(+1);
        DecreaseButton.Click += (_, _) => Step(-1);
        ValueText.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitText();
                e.Handled = true;
            }
        };
        ValueText.LostFocus += (_, _) => CommitText();

        // Numeric-only input: swallow anything that isn't a digit, decimal
        // point, or minus sign before the TextBox sees it.
        ValueText.AddHandler(TextInputEvent, (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            foreach (var ch in e.Text)
            {
                if (!char.IsDigit(ch) && ch != '.' && ch != '-')
                {
                    e.Handled = true;
                    return;
                }
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ValueText.MaxLength = 12;

        OnValueChanged();
    }

    private static double CoerceValue(AvaloniaObject o, double value)
    {
        var c = (TouchNumericUpDown)o;
        return Math.Clamp(value, c.Minimum, c.Maximum);
    }

    private void Step(int direction)
    {
        Value = Math.Clamp(Value + direction * Increment, Minimum, Maximum);
    }

    private void CommitText()
    {
        if (double.TryParse(ValueText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            double.TryParse(ValueText.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            Value = Math.Clamp(parsed, Minimum, Maximum);
        }
        // Invalid or unchanged input: rewrite the box from current state
        OnValueChanged();
    }

    private void CoerceAndRefresh()
    {
        Value = Math.Clamp(Value, Minimum, Maximum);
        OnValueChanged();
    }

    private void OnValueChanged()
    {
        ValueText.Text = Value.ToString(FormatString, CultureInfo.InvariantCulture);
        IncreaseButton.IsEnabled = Value < Maximum;
        DecreaseButton.IsEnabled = Value > Minimum;
    }
}
