using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using boilersExtensions.Models;
using System.Windows.Data;

namespace boilersExtensions.Converters
{
    public class PropertyWithNameConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // 値が不足している場合は空文字列を返す
            if (values.Length < 2 || values[0] == null || values[1] == null)
                return string.Empty;

            // 最初の値はEntityViewModel、2番目の値はプロパティ名
            if (values[0] is EntityViewModel entityViewModel && values[1] is string propertyName)
            {
                var config = entityViewModel.GetPropertyConfig(propertyName);
                if (config != null && config.HasFixedValues)
                {
                    return config.GetFixedValuesDisplayText();
                }
            }

            return string.Empty;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
