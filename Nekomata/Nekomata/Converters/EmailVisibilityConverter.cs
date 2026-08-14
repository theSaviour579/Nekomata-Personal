using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Nekomata.Models.Workspace;

namespace Nekomata.UI.Converters;

public sealed class EmailVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is WorkspaceMode.Email ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}