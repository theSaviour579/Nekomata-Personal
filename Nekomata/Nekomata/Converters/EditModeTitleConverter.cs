using System.Globalization;
using System.Windows.Data;

namespace Nekomata.UI.Converters;

public class EditModeTitleConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        var entityName = parameter?.ToString() ?? "Item";
        var isEdit = value is true;

        return isEdit
            ? $"Edit {entityName}"
            : $"New {entityName}";
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}