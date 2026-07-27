using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using GrbLHALSender.Convertors;
using GrbLHALSender.Settings;
using ReactiveUI;
using System;

namespace GrbLHALSender.Views;

/// <summary>
/// Picks an editor control for a <see cref="GrblHalSetting"/> based on its <see cref="GrblHalSetting.DataTypes"/>.
/// All UI construction lives here so the model stays Avalonia-free.
/// </summary>
public class SettingEditorTemplate : IDataTemplate
{
    private const string AxesFormat = "axes";

    public bool Match(object? data) => data is GrblHalSetting;

    public Control? Build(object? data)
    {
        if (data is not GrblHalSetting setting) return null;

        return setting.DataType switch
        {
            GrblHalSetting.DataTypes.AXISMASK => BuildBitmask(setting, axisMode: true),
            GrblHalSetting.DataTypes.BITFIELD or GrblHalSetting.DataTypes.XBITFIELD =>
                setting.Format == AxesFormat
                    ? BuildBitmask(setting, axisMode: true)
                    : BuildBitmask(setting, axisMode: false),
            GrblHalSetting.DataTypes.BOOL => BuildBool(setting),
            GrblHalSetting.DataTypes.RADIOBUTTONS => BuildRadio(setting),
            _ => BuildText(setting),
        };
    }

    private static Control BuildBitmask(GrblHalSetting setting, bool axisMode)
    {
        var panel = new StackPanel();

        string[] labels;
        if (axisMode)
        {
            var axisCount = GrblHalSettingsConst.AxisCount ?? 3;
            var axisLabel = GrblHalSettingsConst.Axis ?? GrblHalSettingsConst.BackUpAxis[1..axisCount];
            labels = new string[axisCount];
            for (int i = 0; i < axisCount; i++) labels[i] = $"{axisLabel[i]} axis";
        }
        else
        {
            labels = setting.Format?.Split(',') ?? Array.Empty<string>();
        }

        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] == "N/A") continue;

            var bitValue = (1 << i).ToString();
            var checkBox = new CheckBox
            {
                [!CheckBox.IsCheckedProperty] = new Binding
                {
                    Converter = new StringToBitMask(),
                    ConverterParameter = bitValue,
                    Mode = BindingMode.OneWay,
                },
                Name = $"_bitmask{i}",
                Content = labels[i].Trim(),
                Tag = bitValue,
                Command = ReactiveCommand.Create<bool>(_ => WriteBitmask(setting, panel)),
            };
            panel.Children.Add(checkBox);
        }

        return panel;
    }

    private static Control BuildBool(GrblHalSetting setting)
    {
        CheckBox? cb = null;
        cb = new CheckBox
        {
            [!ToggleButton.IsCheckedProperty] = new Binding
            {
                Converter = new StringToBool(),
                Mode = BindingMode.OneWay,
            },
            // No Content/Width: the row already shows the name, and a fixed 400px
            // label here would push the control off the right edge.
            Command = ReactiveCommand.Create<bool>(_ =>
            {
                setting.SettingValue = cb!.IsChecked == true ? "1" : "0";
                setting.NeedsSaving = true;
            }),
        };
        return cb;
    }

    private static Control BuildRadio(GrblHalSetting setting)
    {
        var panel = new StackPanel();
        var labels = setting.Format?.Split(',') ?? Array.Empty<string>();
        for (int i = 0; i < labels.Length; i++)
        {
            var tagValue = i.ToString();
            var rb = new RadioButton
            {
                [!ToggleButton.IsCheckedProperty] = new Binding
                {
                    Converter = new StringToRadioButton(),
                    ConverterParameter = tagValue,
                    Mode = BindingMode.OneWay,
                },
                Tag = tagValue,
                Name = $"_radiobutton{i}",
                Content = labels[i].Trim(),
                Command = ReactiveCommand.Create<bool>(_ => WriteRadio(setting, panel)),
            };
            panel.Children.Add(rb);
        }
        return panel;
    }

    private static Control BuildText(GrblHalSetting setting)
    {
        var tb = new TextBox
        {
            [!TextBox.TextProperty] = new Binding(nameof(GrblHalSetting.SettingValue)),
            Width = 200,
        };
        return tb;
    }

    private static void WriteBitmask(GrblHalSetting setting, StackPanel panel)
    {
        int mask = 0;
        foreach (var child in panel.Children)
        {
            if (child is CheckBox cb && cb.IsChecked == true)
                mask |= Convert.ToInt32(cb.Tag);
        }
        setting.SettingValue = mask.ToString();
        setting.NeedsSaving = true;
    }

    private static void WriteRadio(GrblHalSetting setting, StackPanel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is RadioButton rb && rb.IsChecked == true)
            {
                setting.SettingValue = rb.Tag?.ToString() ?? "0";
                setting.NeedsSaving = true;
                return;
            }
        }
    }
}
