using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Epub3MediaOverlays.Core.MediaOverlayGeneration;

namespace Epub3MediaOverlays.Wpf
{
    /// <summary>
    /// Converts AlignmentStatus to a Color for visualization.
    /// Implements IMultiValueConverter to work with MultiBinding.
    /// </summary>
    public class StatusToBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length > 0 && values[0] is AlignmentStatus status)
            {
                return status switch
                {
                    AlignmentStatus.Success => Colors.Green,
                    AlignmentStatus.Partial => Colors.Orange,
                    AlignmentStatus.Failed => Colors.Red,
                    AlignmentStatus.Split => Colors.LightBlue,
                    _ => Colors.Gray
                };
            }
            return Colors.Gray;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts bool to Visibility.
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

