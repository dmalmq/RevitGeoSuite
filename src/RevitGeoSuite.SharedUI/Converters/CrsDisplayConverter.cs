using System;
using System.Globalization;
using System.Windows.Data;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.SharedUI.Converters;

public sealed class CrsDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CrsDefinition definition)
        {
            return $"EPSG:{definition.EpsgCode}  {definition.Name}";
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
