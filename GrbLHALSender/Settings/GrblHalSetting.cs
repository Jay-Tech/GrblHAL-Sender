using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using GrbLHALSender.Convertors;
using ReactiveUI;

namespace GrbLHALSender.Settings;

public partial class GrblHalSetting : ReactiveObject
{
    private string _settingValue;
    private int _id;
    private int _groupId;
    private string _name;
    private string _unit;
    private DataTypes _dataType;
    private string _format;
    private double _min;
    private double _max;
    private bool _allowNull;
    private bool _rebootRequired;
    private bool _needsSaving;
    private Control _control;

    public int Id
    {
        get => _id;
        set => this.RaiseAndSetIfChanged(ref _id, value);
    }

    public int GroupId
    {
        get => _groupId;
        set => this.RaiseAndSetIfChanged(ref _groupId, value);
    }

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public string Unit
    {
        get => _unit;
        set => this.RaiseAndSetIfChanged(ref _unit, value);
    }

    public DataTypes DataType
    {
        get => _dataType;
        set => this.RaiseAndSetIfChanged(ref _dataType, value);
    }

    public string Format
    {
        get => _format;
        set => this.RaiseAndSetIfChanged(ref _format, value);
    }

    public double Min
    {
        get => _min;
        set => this.RaiseAndSetIfChanged(ref _min, value);
    }

    public double Max
    {
        get => _max;
        set => this.RaiseAndSetIfChanged(ref _max, value);
    }

    public bool AllowNull
    {
        get => _allowNull;
        internal set => this.RaiseAndSetIfChanged(ref _allowNull, value);
    }

    public bool RebootRequired
    {
        get => _rebootRequired;
        internal set => this.RaiseAndSetIfChanged(ref _rebootRequired, value);
    }

    public bool NeedsSaving
    {
        get => _needsSaving;
        set => this.RaiseAndSetIfChanged(ref _needsSaving, value);
    }

    public string SettingValue
    {
        get => _settingValue;
        set => this.RaiseAndSetIfChanged(ref _settingValue, value);
    }

    public Control Control
    {
        get => _control;
        set => this.RaiseAndSetIfChanged(ref _control, value);
    }

    public GrblHalSetting(int id, string value)
    {
        Id = id;
        SettingValue = value;
    }

    public GrblHalSetting(Span<string> data)
    {
        // Parse "SETTINGTYPE:id" from data[0] without mutating the caller's span
        var settingName = data[0].Split(':');
        var idStr = settingName.Length > 1 ? settingName[1] : settingName[0];

        Id = int.Parse(idStr);
        GroupId = int.Parse(data[1]);
        Name = data[2];

        if (data.Length > 3)
            Unit = data[3];
        if (data.Length > 4)
            DataType = string.IsNullOrEmpty(data[4])
                ? DataTypes.TEXT
                : Enum.TryParse<DataTypes>(data[4], true, out var dt) ? dt : DataTypes.TEXT;
        if (data.Length > 5)
            Format = data[5];
        if (data.Length > 6)
            Min = string.IsNullOrEmpty(data[6]) ? double.NaN : double.Parse(data[6], CultureInfo.InvariantCulture);
        if (data.Length > 7)
            Max = string.IsNullOrEmpty(data[7]) ? double.NaN : double.Parse(data[7], CultureInfo.InvariantCulture);
        if (data.Length > 8)
            RebootRequired = data[8] == "1";
        if (data.Length > 9)
            AllowNull = data[9] == "1";

        BuildTemplateControl(DataType);
    }

    public void BuildTemplateControl(DataTypes dataType)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => BuildTemplateControl(dataType));
            return;
        }

        if (dataType is DataTypes.AXISMASK or DataTypes.BITFIELD or DataTypes.XBITFIELD)
        {
            var stackP = new StackPanel();

            if (DataType == DataTypes.AXISMASK || Format == "axes")
            {
                var axisCount = GrblHalSettingsConst.AxisCount ?? 3;
                var axisLabel = GrblHalSettingsConst.Axis ?? GrblHalSettingsConst.BackUpAxis[1..axisCount];

                for (int i = 0; i < axisCount; i++)
                {
                    var bitValue = (1 << i).ToString();
                    var checkBox = new CheckBox
                    {
                        [!CheckBox.IsCheckedProperty] = new Binding
                        {
                            Converter = new StringToBitMask(),
                            ConverterParameter = bitValue,
                            Mode = BindingMode.TwoWay,
                            Path = SettingValue,
                        },
                        Command = ReactiveCommand.Create<bool>(_ => UpdateBitmaskFromPanel(stackP)),
                        Name = $"_bitmask{i}",
                        Content = $"{axisLabel[i]} axis",
                        Tag = bitValue,
                    };
                    stackP.Children.Add(checkBox);
                }

                Control = stackP;
            }
            else
            {
                var format = Format.Split(',');
                for (int i = 0; i < format.Length; i++)
                {
                    if (format[i] == "N/A") continue;

                    var bitValue = (1 << i).ToString();
                    var checkBox = new CheckBox
                    {
                        [!CheckBox.IsCheckedProperty] = new Binding
                        {
                            Converter = new StringToBitMask(),
                            ConverterParameter = bitValue,
                            Mode = BindingMode.TwoWay,
                            Path = SettingValue,
                        },
                        Name = $"_bitmask{i}",
                        Content = format[i].Trim(),
                        IsEnabled = true,
                        Command = ReactiveCommand.Create<bool>(_ => UpdateBitmaskFromPanel(stackP)),
                        Tag = bitValue,
                    };
                    stackP.Children.Add(checkBox);
                }

                Control = stackP;
            }
        }
        else if (dataType == DataTypes.BOOL)
        {
            Control = new CheckBox()
            {
                [!ToggleButton.IsCheckedProperty] = new Binding
                {
                    Converter = new StringToBool(),
                    ConverterParameter = SettingValue,
                    Mode = BindingMode.TwoWay,
                    Path = SettingValue,
                },
                Width = 400,
                Content = Name,
                Command = ReactiveCommand.Create<bool>(CbChecked),
            };

            void CbChecked(bool b)
            {
                if (Control is CheckBox cb)
                {
                    SettingValue = cb.IsChecked == true ? "1" : "0";
                    NeedsSaving = true;
                }
            }
        }
        else if (dataType == DataTypes.RADIOBUTTONS)
        {
            var stackP = new StackPanel();
            string[] labels = Format.Split(',');
            for (int i = 0; i < labels.Length; i++)
            {
                var tagValue = i.ToString();
                var rb = new RadioButton
                {
                    [!ToggleButton.IsCheckedProperty] = new Binding
                    {
                        Converter = new StringToRadioButton(),
                        ConverterParameter = tagValue,
                        Mode = BindingMode.TwoWay,
                        Path = SettingValue ?? string.Empty,
                    },
                    Tag = tagValue,
                    Name = $"_radiobutton{i}",
                    Content = labels[i].Trim(),
                    Command = ReactiveCommand.Create<bool>(_ => UpdateRadioFromPanel(stackP)),
                };
                stackP.Children.Add(rb);
            }

            Control = stackP;
        }
        else
        {
            var tb = new TextBox
            {
                [!TextBlock.TextProperty] = new Binding("SettingValue", BindingMode.TwoWay),
                Width = 200,
            };
            tb.KeyUp += (_, _) => NeedsSaving = true;
            Control = tb;
        }
    }

    /// <summary>
    /// Reads all CheckBoxes in a StackPanel to compute the combined bitmask value.
    /// Shared by both axis-mask and bitfield checkbox panels.
    /// </summary>
    private void UpdateBitmaskFromPanel(StackPanel panel)
    {
        int mask = 0;
        foreach (var child in panel.Children)
        {
            if (child is CheckBox cb && cb.IsChecked == true)
            {
                mask += Convert.ToInt32(cb.Tag);
            }
        }

        SettingValue = mask.ToString();
        NeedsSaving = true;
    }

    /// <summary>
    /// Reads all RadioButtons in a StackPanel to find the selected value.
    /// </summary>
    private void UpdateRadioFromPanel(StackPanel panel)
    {
        int value = 0;
        foreach (var child in panel.Children)
        {
            if (child is RadioButton rb && rb.IsChecked == true)
            {
                value += Convert.ToInt32(rb.Tag);
            }
        }

        SettingValue = value.ToString();
        NeedsSaving = true;
    }

    public enum PendingMessageSet
    {
        NotPending = 0,
        Options = 1,
        Setting = 2
    }

    public enum DataTypes
    {
        BOOL = 0,
        BITFIELD,
        XBITFIELD,
        RADIOBUTTONS,
        AXISMASK,
        INTEGER,
        FLOAT,
        TEXT,
        PASSWORD,
        IP4
    };
}
