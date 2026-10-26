using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nekomata.UI.Controls.Home;

/// <summary>Visible when a count is zero (for "nothing here" lines), otherwise collapsed.</summary>
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
