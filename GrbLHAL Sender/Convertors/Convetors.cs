using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrbLHAL_Sender.Convertors
{
    //public class BoolToVisibleConverter : IValueConverter
    //{
    //    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    //    {
    //        return value is bool && (bool)value ? Visibility.Visible : Visibility.Collapsed;
    //    }
    //    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    //    {
    //        return value is Visibility && (Visibility)value == Visibility.Visible;
    //    }
    //}

    //public class BoolToNotVisibleConverter : IValueConverter
    //{
    //    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    //    {
    //        var state = value is bool && (bool)value ? Visibility.Collapsed : Visibility.Visible;
    //        return value is bool && (bool)value ? Visibility.Collapsed : Visibility.Visible;
    //    }
    //    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    //    {
    //        return value is  Visibility && (Visibility)value == Visibility.Visible;
    //    }
    //}

}
