using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using GrbLHALSender.Settings;
using GrbLHALSender.Theming;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GrbLHALSender.Convertors
{
    public class StringToBool : IValueConverter
    {
        public static readonly StringToBool Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is GrblHalSetting source)
            {
                switch (source.SettingValue)
                {
                    case "0":
                        return false;
                    case "1":
                        return true;
                    default:
                        // invalid option, return the exception below
                        break;
                }
            }
            return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }


    public class StringToRadioButton : IValueConverter
    {
        public static readonly StringToRadioButton Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is GrblHalSetting { SettingValue: not null } s)
            {
                var d = int.Parse(s.SettingValue);
                if (parameter?.ToString() != null)
                {
                    var v = int.Parse(parameter.ToString() ?? string.Empty);
                    return d == v;
                }
            }
            return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    public class StringToBitMask : IValueConverter
    {
        public static readonly StringToBitMask Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is GrblHalSetting s)
            {
                if (s?.SettingValue == null) return false;
                if (parameter?.ToString() != null)
                {
                    var d = int.Parse(s.SettingValue);
                    var par = System.Convert.ToInt32(parameter);
                    bool bitSet = (d & par) == par;
                    return bitSet;
                }
            }
            // converter used for the wrong type
            return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    public class AlignmentConvertor : IMultiValueConverter
    {
        public static readonly AlignmentConvertor Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is null || values.Count == 0) return VerticalAlignment.Stretch;
            else
            {
                var b = (bool)(values[0] ?? VerticalAlignment.Bottom);
                return b ? VerticalAlignment.Stretch : VerticalAlignment.Bottom;
            }

        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class HorizontalAlignmentConvertor : IMultiValueConverter
    {
        public static readonly HorizontalAlignmentConvertor Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is null || values.Count == 0) return HorizontalAlignment.Stretch;
            else
            {
                var b = (bool)(values[0] ?? HorizontalAlignment.Left);
                return b ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            }

        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

}

