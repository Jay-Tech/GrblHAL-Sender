using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaApplication1.Converters
{

    public class LogicalNotConverter : IValueConverter
    {
        public IValueConverter FinalConverter { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool result =
                (value is bool
                    ? !(bool)value
                    : ((value is bool?) ? (bool?)value != true : ((value is int) ? (int)value == 0 : false))) ||
                value == null;

            return FinalConverter == null ? result : FinalConverter.Convert(result, targetType, parameter, culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(bool)value;
        }
    }

    public class StringAddToConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            return values.Length == 2
                ? values[0].ToString() + string.Format((string)parameter, values[1].ToString())
                : string.Empty;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter,
            System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        object? IMultiValueConverter.Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

