using System;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Threading;
using DynamicData;
using GrbLHAL_Sender.Controls;
using GrbLHAL_Sender.Convertors;
using ReactiveUI;

namespace GrbLHAL_Sender.Settings;

public partial class GrblHalSetting
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
        set => _id = value;
    }

    public int GroupId
    {
        get => _groupId;
        set => _groupId = value;
    }

    public string Name
    {
        get => _name;
        set => _name = value;
    }

    public string Unit
    {
        get => _unit;
        set => _unit = value;
    }

    public DataTypes DataType
    {
        get => _dataType;
        set => _dataType = value;
    }

    public string Format
    {
        get => _format;
        set => _format = value;
    }

    public double Min
    {
        get => _min;
        set => _min = value;
    }

    public double Max
    {
        get => _max;
        set => _max = value;
    }

    public string InternalValue { get; set; }
    public bool AllowNull
    {
        get => _allowNull;
        internal set => _allowNull = value;
    }

    public bool RebootRequired
    {
        get => _rebootRequired;
        internal set => _rebootRequired = value;
    }

    public bool NeedsSaving
    {
        get => _needsSaving;
        set => _needsSaving = value;
    }

    public string SettingValue
    {
        get => _settingValue;
        set
        {
            if (_settingValue != value)
            {
                _settingValue = value;
            }
        }
    }

    public Control Control
    {
        get => _control;
        set => _control = value;
    }
    public GrblHalSetting(int id, string value)
    {
        Id = id;
        SettingValue = value;
    }
    public GrblHalSetting(Span<string> data)
    {
        var settingName = data[0].Split(':');
        data[0] = settingName[1];

        Id = int.Parse(data[0]);
        GroupId = int.Parse(data[1]);
        Name = data[2];
        Unit = data[3];
        DataType = data[4] == string.Empty ? DataTypes.TEXT : (DataTypes)Enum.Parse(typeof(DataTypes), data[4], true);
        Format = data[5];
        Min = data[6] == string.Empty ? double.NaN : double.Parse(data[6]);
        Max = data[7] == string.Empty ? double.NaN : double.Parse(data[7]);
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
            Dispatcher.UIThread.Invoke((() =>
            {
                BuildTemplateControl(dataType);

            }));
            return;
        }

        if (dataType is DataTypes.AXISMASK or DataTypes.BITFIELD or DataTypes.XBITFIELD)
        {
            var stackP = new StackPanel();
            var labels = string.Empty;
            if (DataType == DataTypes.AXISMASK || Format == "axes")
            {
                var axisCount = GrblHalSettingsConst.AxisCount ?? 3;
                var axisLabel = GrblHalSettingsConst.Axis ?? GrblHalSettingsConst.BackUpAxis;
                for (int i = 0; i < axisCount; i++)
                {
                    labels += (labels == string.Empty ? "" : ",") + axisLabel[i] + " axis";
                }
                var format = labels.Split(",");
                for (int i = 0; i < axisCount; i++)
                {
                    InternalValue = (1 << i).ToString();
                    var checkBox = new CheckBox
                    {
                        [!CheckBox.IsCheckedProperty] = new Binding
                        {
                            Converter = new StringToBitMask(),
                            ConverterParameter = InternalValue,
                            Mode = BindingMode.TwoWay,
                            Path = SettingValue,
                        },
                        Command = ReactiveCommand.Create<bool>(CbChecked),
                        Name = $"_bitmask{i}",
                        Content = format[i].Trim(),
                        Tag = InternalValue,
                    };

                    void CbChecked(bool b)
                    {
                        int mask = 0;
                        foreach (var item in stackP.Children)
                        {
                            if (item is CheckBox cb)
                            {
                                if ((bool)cb.IsChecked)
                                {
                                    mask += Convert.ToInt32(cb.Tag);
                                    NeedsSaving = true;
                                }
                            }
                        }

                        SettingValue = mask.ToString();
                    }
                    stackP.Children.Add(checkBox);
                }

                Control = stackP;
            }
            else
            {
                var format = Format.Split(',');
                foreach (var item in format)
                {
                    if (item == "N/A") continue;
                    InternalValue = (1 << format.IndexOf(item)).ToString();
                    var checkBox = new CheckBox
                    {
                        [!CheckBox.IsCheckedProperty] = new Binding
                        {
                            Converter = new StringToBitMask(),
                            ConverterParameter = InternalValue,
                            Mode = BindingMode.TwoWay,
                            Path = SettingValue,
                        },
                        Name = $"_bitmask{format.IndexOf(item)}",
                        Content = item.Trim(),
                        IsEnabled = true,
                        Command = ReactiveCommand.Create<bool>(CbChecked),
                        Tag = InternalValue,
                    };

                    void CbChecked(bool b)
                    {
                        int mask = 0;
                        foreach (var item in stackP.Children)
                        {
                            if (item is CheckBox cb)
                            {
                                if ((bool)cb.IsChecked)
                                {
                                    mask += Convert.ToInt32(cb.Tag);
                                    NeedsSaving = true;
                                }
                            }
                        }

                        SettingValue = mask.ToString();
                    } 
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
                    SettingValue = cb.IsChecked != null && (bool)cb.IsChecked ? "1" : "0";
                    NeedsSaving = true;
                }
            }

        }
        else if (dataType == DataTypes.RADIOBUTTONS)
        {
            var stackP = new StackPanel();
            string[] label = Format.Split(',');
            for (int i = 0; i < label.Length; i++)
            {
                InternalValue = i.ToString();
                var rb = new RadioButton
                {
                    [!RadioButton.IsCheckedProperty] = new Binding
                    {
                        Converter = new StringToRadioButton(),
                        ConverterParameter = InternalValue,
                        Mode = BindingMode.TwoWay,
                        Path = SettingValue,
                        Source = SettingValue,
                    },
                    Tag = InternalValue,
                    Name = $"_radiobutton{i}",
                    Content = label[i].Trim(),
                    Command  = ReactiveCommand.Create<bool>(RbChanged),
                };
                stackP.Children.Add(rb);
            }

            void RbChanged(bool b)
            {
                var mask = 0;
                foreach (var item in stackP.Children)
                {
                    if (item is RadioButton rb)
                    {
                        if ((bool)rb.IsChecked!)
                        {

                            mask += Convert.ToInt32(rb.Tag);
                        }
                    }
                }

                SettingValue = mask.ToString();
                NeedsSaving = true;
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
            tb.KeyUp += ControlTextInput;
           
            void ControlTextInput(object? sender, KeyEventArgs e)
            {
                NeedsSaving = true;
            }
            Control = tb;
        }
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