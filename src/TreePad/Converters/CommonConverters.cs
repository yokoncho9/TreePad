using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TreePad.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                if (Invert) b = !b;
                return b ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
            {
                bool b = v == Visibility.Visible;
                return Invert ? !b : b;
            }
            return false;
        }
    }

    public class BoolToTextWrappingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
            {
                return TextWrapping.Wrap;
            }
            return TextWrapping.NoWrap;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TextWrapping tw)
            {
                return tw == TextWrapping.Wrap;
            }
            return false;
        }
    }

    public class DoubleToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return new GridLength(Math.Max(100, d));
            }
            return new GridLength(280);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is GridLength gl)
            {
                return gl.Value;
            }
            return 280.0;
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = value == null || (value is string s && string.IsNullOrEmpty(s));
            if (Invert) isNull = !isNull;
            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
