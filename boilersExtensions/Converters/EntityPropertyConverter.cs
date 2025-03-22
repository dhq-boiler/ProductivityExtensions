using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using boilersExtensions.ViewModels;
using System.Windows.Data;

namespace boilersExtensions.Converters
{
    public class EntityPropertyConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is SeedDataConfigViewModel viewModel && values[1] is string entityName)
            {
                return viewModel.GetEntityProperties(entityName);
            }
            return new List<string>();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
