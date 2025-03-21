using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using boilersExtensions.Models;

namespace boilersExtensions.Generators
{
    /// <summary>
    /// 標準的なプロパティ型の値を生成するクラス
    /// </summary>
    public class StandardPropertyGenerator
    {
        private readonly RandomDataProvider _randomDataProvider;
        private readonly Dictionary<string, Dictionary<int, object>> _valueCache;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="randomDataProvider">ランダムデータプロバイダ</param>
        public StandardPropertyGenerator(RandomDataProvider randomDataProvider)
        {
            _randomDataProvider = randomDataProvider;
            _valueCache = new Dictionary<string, Dictionary<int, object>>();
        }

        /// <summary>
        /// プロパティに適した値を生成します
        /// </summary>
        /// <param name="property">プロパティ情報</param>
        /// <param name="recordIndex">レコードのインデックス（0から始まる）</param>
        /// <param name="propConfig">プロパティ設定（指定されている場合）</param>
        /// <returns>生成された値（C#のリテラル形式）</returns>
        public string GenerateValue(PropertyInfo property, int recordIndex, PropertyConfigViewModel propConfig)
        {
            // プロパティ設定が指定されている場合はそれを優先
            if (propConfig != null && propConfig.UseCustomStrategy)
            {
                return propConfig.CustomValue;
            }

            // キャッシュキーを生成
            string cacheKey = $"{property.FullTypeName}_{property.Name}";

            // レコードごとに一貫した値を生成するためにキャッシュを使用
            if (!_valueCache.TryGetValue(cacheKey, out var recordValues))
            {
                recordValues = new Dictionary<int, object>();
                _valueCache[cacheKey] = recordValues;
            }

            if (!recordValues.TryGetValue(recordIndex, out var cachedValue))
            {
                // プロパティの型に応じた値を生成
                cachedValue = GenerateValueByType(property, recordIndex);
                recordValues[recordIndex] = cachedValue;
            }

            // キャッシュされた値をC#リテラル形式に変換
            return FormatValueAsLiteral(cachedValue, property.TypeName);
        }

        /// <summary>
        /// プロパティの型に応じた値を生成します
        /// </summary>
        private object GenerateValueByType(PropertyInfo property, int recordIndex)
        {
            // nullableの型の場合
            bool isNullable = property.IsNullable || !property.IsRequired;

            // 稀にnull値を生成（nullableかつ必須でない場合）
            if (isNullable && !property.IsRequired && _randomDataProvider.ShouldGenerateNull())
            {
                return null;
            }

            // プロパティの型に応じた値を生成
            string typeName = property.IsNullable ? property.UnderlyingTypeName : property.TypeName;

            switch (typeName)
            {
                case "String":
                    return GenerateStringValue(property, recordIndex);

                case "Int32":
                case "Int":
                    return _randomDataProvider.GetRandomInt32(property.MinValue, property.MaxValue);

                case "Int64":
                case "Long":
                    return _randomDataProvider.GetRandomInt64(
                        property.MinValue.HasValue ? (long)property.MinValue.Value : (long?)null,
                        property.MaxValue.HasValue ? (long)property.MaxValue.Value : (long?)null);

                case "Int16":
                case "Short":
                    return _randomDataProvider.GetRandomInt16(
                        property.MinValue.HasValue ? (short)property.MinValue.Value : (short?)null,
                        property.MaxValue.HasValue ? (short)property.MaxValue.Value : (short?)null);

                case "Byte":
                    return _randomDataProvider.GetRandomByte(
                        property.MinValue.HasValue ? (byte)property.MinValue.Value : (byte?)null,
                        property.MaxValue.HasValue ? (byte)property.MaxValue.Value : (byte?)null);

                case "Double":
                    return _randomDataProvider.GetRandomDouble(property.MinValue, property.MaxValue);

                case "Single":
                case "Float":
                    return _randomDataProvider.GetRandomSingle(
                        property.MinValue.HasValue ? (float)property.MinValue.Value : (float?)null,
                        property.MaxValue.HasValue ? (float)property.MaxValue.Value : (float?)null);

                case "Decimal":
                    return _randomDataProvider.GetRandomDecimal(
                        property.MinValue.HasValue ? (decimal)property.MinValue.Value : (decimal?)null,
                        property.MaxValue.HasValue ? (decimal)property.MaxValue.Value : (decimal?)null);

                case "Boolean":
                case "Bool":
                    return _randomDataProvider.GetRandomBoolean();

                case "DateTime":
                    return _randomDataProvider.GetRandomDateTime();

                case "DateTimeOffset":
                    return _randomDataProvider.GetRandomDateTimeOffset();

                case "TimeSpan":
                    return _randomDataProvider.GetRandomTimeSpan();

                case "Guid":
                    return _randomDataProvider.GetRandomGuid();

                case "Char":
                    return _randomDataProvider.GetRandomChar();

                case "Byte[]":
                    return _randomDataProvider.GetRandomByteArray(property.MaxLength ?? 16);

                default:
                    // 不明な型の場合はデフォルト値を使用
                    return GetDefaultValueForType(typeName);
            }
        }

        /// <summary>
        /// 文字列値を生成します
        /// </summary>
        private string GenerateStringValue(PropertyInfo property, int recordIndex)
        {
            // 文字列の生成パターンを決定
            string pattern = DetermineStringPattern(property);

            // パターンに基づいて文字列を生成
            switch (pattern)
            {
                case "Email":
                    return _randomDataProvider.GetRandomEmail();

                case "Name":
                    return _randomDataProvider.GetRandomPersonName();

                case "FirstName":
                    return _randomDataProvider.GetRandomFirstName();

                case "LastName":
                    return _randomDataProvider.GetRandomLastName();

                case "FullName":
                    return _randomDataProvider.GetRandomFullName();

                case "Address":
                    return _randomDataProvider.GetRandomAddress();

                case "PhoneNumber":
                    return _randomDataProvider.GetRandomPhoneNumber();

                case "Url":
                    return _randomDataProvider.GetRandomUrl();

                case "Username":
                    return _randomDataProvider.GetRandomUsername();

                case "Password":
                    return _randomDataProvider.GetRandomPassword();

                case "CompanyName":
                    return _randomDataProvider.GetRandomCompanyName();

                case "JobTitle":
                    return _randomDataProvider.GetRandomJobTitle();

                case "Lorem":
                    return _randomDataProvider.GetRandomLoremIpsum(property.MaxLength ?? 100);

                default:
                    // デフォルトはプロパティ名とレコードインデックスを組み合わせた文字列
                    int maxLength = property.MaxLength ?? 50;
                    return _randomDataProvider.GetRandomString(maxLength, property.Name, recordIndex);
            }
        }

        /// <summary>
        /// 文字列プロパティの生成パターンを決定します
        /// </summary>
        private string DetermineStringPattern(PropertyInfo property)
        {
            string propName = property.Name.ToLowerInvariant();

            // プロパティ名に基づいて生成パターンを推測
            if (propName.Contains("email"))
            {
                return "Email";
            }

            if (propName.EndsWith("name"))
            {
                if (propName.Contains("first"))
                {
                    return "FirstName";
                }

                if (propName.Contains("last"))
                {
                    return "LastName";
                }

                if (propName.Contains("full"))
                {
                    return "FullName";
                }

                if (propName.Contains("company") || propName.Contains("business"))
                {
                    return "CompanyName";
                }

                return "Name";
            }

            if (propName.Contains("address"))
            {
                return "Address";
            }

            if (propName.Contains("phone"))
            {
                return "PhoneNumber";
            }

            if (propName.Contains("url") || propName.Contains("website") || propName.Contains("site"))
            {
                return "Url";
            }

            if (propName.Contains("username") || propName.Contains("login"))
            {
                return "Username";
            }

            if (propName.Contains("password") || propName.Contains("pwd"))
            {
                return "Password";
            }

            if (propName.Contains("job") || propName.Contains("title") || propName.Contains("position"))
            {
                return "JobTitle";
            }

            if (propName.Contains("description") || propName.Contains("content") || propName.Contains("text"))
            {
                return "Lorem";
            }

            return "Default";
        }

        /// <summary>
        /// 型のデフォルト値を取得します
        /// </summary>
        private object GetDefaultValueForType(string typeName)
        {
            switch (typeName)
            {
                case "String":
                    return string.Empty;

                case "Int32":
                case "Int":
                    return 0;

                case "Int64":
                case "Long":
                    return 0L;

                case "Int16":
                case "Short":
                    return (short)0;

                case "Byte":
                    return (byte)0;

                case "Double":
                    return 0.0;

                case "Single":
                case "Float":
                    return 0.0f;

                case "Decimal":
                    return 0.0m;

                case "Boolean":
                case "Bool":
                    return false;

                case "DateTime":
                    return DateTime.MinValue;

                case "DateTimeOffset":
                    return DateTimeOffset.MinValue;

                case "TimeSpan":
                    return TimeSpan.Zero;

                case "Guid":
                    return Guid.Empty;

                case "Char":
                    return '\0';

                default:
                    return null;
            }
        }

        /// <summary>
        /// 値をC#リテラル形式に変換します
        /// </summary>
        private string FormatValueAsLiteral(object value, string typeName)
        {
            if (value == null)
            {
                return "null";
            }

            switch (typeName)
            {
                case "String":
                    // 特殊文字をエスケープ
                    return $"\"{EscapeString(value.ToString())}\"";

                case "DateTime":
                    var dateTime = (DateTime)value;
                    return $"new DateTime({dateTime.Year}, {dateTime.Month}, {dateTime.Day}, {dateTime.Hour}, {dateTime.Minute}, {dateTime.Second})";

                case "DateTimeOffset":
                    var dto = (DateTimeOffset)value;
                    return $"new DateTimeOffset({dto.Year}, {dto.Month}, {dto.Day}, {dto.Hour}, {dto.Minute}, {dto.Second}, {dto.Offset.TotalHours}h)";

                case "TimeSpan":
                    var ts = (TimeSpan)value;
                    return $"new TimeSpan({ts.Days}, {ts.Hours}, {ts.Minutes}, {ts.Seconds})";

                case "Guid":
                    return $"new Guid(\"{value}\")";

                case "Char":
                    return $"'{value}'";

                case "Decimal":
                    return $"{value}m";

                case "Single":
                case "Float":
                    return $"{value}f";

                case "Double":
                    return $"{value}d";

                case "Byte[]":
                    // バイト配列を16進文字列に変換
                    return FormatByteArray((byte[])value);

                default:
                    return value.ToString();
            }
        }

        /// <summary>
        /// 文字列内の特殊文字をエスケープします
        /// </summary>
        private string EscapeString(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                switch (c)
                {
                    case '\"':
                        sb.Append("\\\"");
                        break;

                    case '\\':
                        sb.Append("\\\\");
                        break;

                    case '\b':
                        sb.Append("\\b");
                        break;

                    case '\f':
                        sb.Append("\\f");
                        break;

                    case '\n':
                        sb.Append("\\n");
                        break;

                    case '\r':
                        sb.Append("\\r");
                        break;

                    case '\t':
                        sb.Append("\\t");
                        break;

                    default:
                        // 制御文字もエスケープ
                        if (char.IsControl(c))
                        {
                            sb.Append($"\\u{(int)c:X4}");
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// バイト配列をC#配列リテラル形式にフォーマットします
        /// </summary>
        private string FormatByteArray(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return "new byte[0]";
            }

            var sb = new StringBuilder("new byte[] { ");
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append("0x").Append(bytes[i].ToString("X2"));

                if (i < bytes.Length - 1)
                {
                    sb.Append(", ");
                }

                // 長い配列の場合は途中で改行
                if (i > 0 && i % 8 == 0)
                {
                    sb.Append(Environment.NewLine).Append("    ");
                }
            }

            sb.Append(" }");
            return sb.ToString();
        }
    }
}