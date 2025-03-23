using System;
using System.Globalization;
using System.Windows.Data;
using boilersExtensions.Models;

namespace boilersExtensions.Converters
{
    /// <summary>
    ///     EntityViewModelとプロパティ名からPropertyConfigの固定値表示テキストを取得するコンバーター
    /// </summary>
    public class PropertyConfigConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EntityViewModel entityViewModel && parameter is string propertyName)
            {
                var config = entityViewModel.GetPropertyConfig(propertyName);
                if (config != null && config.HasFixedValues)
                {
                    return config.GetFixedValuesDisplayText();
                }
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}