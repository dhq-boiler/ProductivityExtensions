using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace boilersExtensions.Converters
{
    /// <summary>
    ///     TypeConverter for language selection in settings
    /// </summary>
    public class LanguageSelectionConverter : TypeConverter
    {
        // Dictionary to map language codes to display names
        private static readonly Dictionary<string, string> _languageOptions = new Dictionary<string, string>
        {
            { "en-US", "English (US)" }, { "ja-JP", "日本語 (Japanese)" }
        };

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context) =>
            new StandardValuesCollection(_languageOptions.Keys.ToList());

        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string stringValue)
            {
                // Handle display name to value conversion
                if (_languageOptions.ContainsValue(stringValue))
                {
                    return _languageOptions.FirstOrDefault(x => x.Value == stringValue).Key;
                }

                // Handle direct language code
                if (_languageOptions.ContainsKey(stringValue))
                {
                    return stringValue;
                }
            }

            return base.ConvertFrom(context, culture, value);
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value,
            Type destinationType)
        {
            if (destinationType == typeof(string) && value is string languageCode)
            {
                // Display the friendly name in the UI
                if (_languageOptions.TryGetValue(languageCode, out var displayName))
                {
                    return displayName;
                }

                return languageCode;
            }

            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}