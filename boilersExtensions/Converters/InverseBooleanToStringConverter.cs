using System;
using System.Globalization;
using System.Windows.Data;

namespace boilersExtensions.Converters
{
    /// <summary>
    ///     Booleanの値を反転し、パラメータとして受け取った文字列を使い分けるコンバーター
    /// </summary>
    public class InverseBooleanToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                var invertedValue = !boolValue;

                // パラメーターがある場合はそれを使用
                if (parameter != null)
                {
                    var parts = parameter.ToString().Split(',');
                    if (parts.Length >= 2)
                    {
                        return invertedValue ? parts[0].Trim() : parts[1].Trim();
                    }

                    return invertedValue ? parameter.ToString() : "行";
                }

                // パラメーターがない場合はデフォルト値
                return invertedValue ? "行 (Razor)" : "行";
            }

            return "行";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 双方向バインディングは使用しないのでnull
            return null;
        }
    }
}