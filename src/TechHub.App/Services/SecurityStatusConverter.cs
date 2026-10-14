using System.Globalization;
using System.Windows.Data;

namespace TechHub.App.Services;

public sealed class SecurityStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        true => "Ativo / presente",
        false => "Inativo / ausente",
        _ => "Não verificado"
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
