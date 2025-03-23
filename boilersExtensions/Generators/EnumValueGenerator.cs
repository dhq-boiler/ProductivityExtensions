using System;
using System.Collections.Generic;
using System.Linq;
using boilersExtensions.Models;

namespace boilersExtensions.Generators
{
    /// <summary>
    ///     Enum型プロパティの値を生成するクラス（改善版）
    /// </summary>
    public class EnumValueGenerator
    {
        private readonly Dictionary<string, Dictionary<int, object>> _enumValueCache =
            new Dictionary<string, Dictionary<int, object>>();

        private readonly Random _random = new Random();

        /// <summary>
        ///     Enum型プロパティに対する値を生成します
        /// </summary>
        /// <param name="property">プロパティ情報</param>
        /// <param name="recordIndex">レコードのインデックス（0から始まる）</param>
        /// <param name="propConfig">プロパティ設定（指定されている場合）</param>
        /// <returns>生成されたEnum値（C#のリテラル形式）</returns>
        public string GenerateEnumValue(PropertyInfo property, int recordIndex, PropertyConfigViewModel propConfig)
        {
            // プロパティがEnum型でない場合は空文字を返す
            if (!property.IsEnum || property.EnumValues.Count == 0)
            {
                return string.Empty;
            }

            // プロパティ設定が指定されていない場合のデフォルト戦略
            if (propConfig == null || !(propConfig is EnumPropertyConfigViewModel))
            {
                // デフォルトでは順番にEnum値を使用
                var valueIndex = recordIndex % property.EnumValues.Count;
                var enumValue = property.EnumValues[valueIndex];
                return $"{property.TypeName}.{enumValue.Name}";
            }

            // Enum専用の設定クラスにキャスト
            var enumPropConfig = propConfig as EnumPropertyConfigViewModel;

            // 値の選択戦略に基づいて処理
            switch (enumPropConfig.Strategy)
            {
                case EnumValueStrategy.UseAll:
                    // すべての値を順番に使用
                    return GenerateUseAllValue(property, recordIndex);

                case EnumValueStrategy.UseSpecific:
                    // 特定の値のみを使用
                    return GenerateSpecificValue(property, recordIndex, enumPropConfig);

                case EnumValueStrategy.Random:
                    // ランダムな値を使用
                    return GenerateRandomValue(property, recordIndex, enumPropConfig);

                case EnumValueStrategy.Custom:
                    // カスタム値を使用
                    return GenerateCustomValue(property, recordIndex, enumPropConfig);

                default:
                    // デフォルトでは最初の値を使用
                    return $"{property.TypeName}.{property.EnumValues[0].Name}";
            }
        }

        /// <summary>
        ///     すべてのEnum値を順番に使用する戦略
        /// </summary>
        private string GenerateUseAllValue(PropertyInfo property, int recordIndex)
        {
            var valueIndex = recordIndex % property.EnumValues.Count;
            var enumValue = property.EnumValues[valueIndex];
            return $"{property.TypeName}.{enumValue.Name}";
        }

        /// <summary>
        ///     特定のEnum値のみを使用する戦略
        /// </summary>
        private string GenerateSpecificValue(PropertyInfo property, int recordIndex, EnumPropertyConfigViewModel config)
        {
            // 選択された値がない場合は最初の値を使用
            if (config.SelectedValues == null || config.SelectedValues.Count == 0)
            {
                return $"{property.TypeName}.{property.EnumValues[0].Name}";
            }

            // 選択された値を順番に使用
            var valueIndex = recordIndex % config.SelectedValues.Count;
            var enumValueName = config.SelectedValues[valueIndex];

            return $"{property.TypeName}.{enumValueName}";
        }

        /// <summary>
        ///     ランダムなEnum値を使用する戦略
        /// </summary>
        private string GenerateRandomValue(PropertyInfo property, int recordIndex, EnumPropertyConfigViewModel config)
        {
            // レコードごとに一貫したランダム値を使用するためにキャッシュを活用
            var cacheKey = $"{property.FullTypeName}_{recordIndex}";

            if (!_enumValueCache.TryGetValue(cacheKey, out var valueCache))
            {
                valueCache = new Dictionary<int, object>();
                _enumValueCache[cacheKey] = valueCache;
            }

            if (!valueCache.TryGetValue(recordIndex, out var cachedValue))
            {
                // Flags属性がある場合の処理
                if (property.IsEnumFlags && config.CombineValues && config.ValueCount > 1)
                {
                    // 複数の値を組み合わせる
                    cachedValue = GenerateCombinedFlagsValue(property, config.ValueCount);
                }
                else
                {
                    // 単一のランダム値
                    var randomIndex = _random.Next(property.EnumValues.Count);
                    cachedValue = property.EnumValues[randomIndex].Name;
                }

                valueCache[recordIndex] = cachedValue;
            }

            // 単一の値の場合
            if (cachedValue is string valueName)
            {
                return $"{property.TypeName}.{valueName}";
            }

            // 組み合わせた値の場合
            if (cachedValue is List<string> valueNames)
            {
                return string.Join(" | ", valueNames.Select(name => $"{property.TypeName}.{name}"));
            }

            // デフォルト値
            return $"{property.TypeName}.{property.EnumValues[0].Name}";
        }

        /// <summary>
        ///     フラグEnum値を組み合わせて生成
        /// </summary>
        private List<string> GenerateCombinedFlagsValue(PropertyInfo property, int valueCount)
        {
            // 利用可能な値からランダムに選択
            var availableValues = property.EnumValues.ToList();
            var selectedValues = new List<string>();

            // valueCount個の値を選択（または最大で利用可能な値の数まで）
            var count = Math.Min(valueCount, availableValues.Count);

            for (var i = 0; i < count; i++)
            {
                var randomIndex = _random.Next(availableValues.Count);
                selectedValues.Add(availableValues[randomIndex].Name);
                availableValues.RemoveAt(randomIndex);

                if (availableValues.Count == 0)
                {
                    break;
                }
            }

            return selectedValues;
        }

        /// <summary>
        ///     カスタム割り当てに基づくEnum値を生成
        /// </summary>
        private string GenerateCustomValue(PropertyInfo property, int recordIndex, EnumPropertyConfigViewModel config)
        {
            // カスタムマッピングが指定されている場合
            if (config.CustomMapping != null && config.CustomMapping.TryGetValue(recordIndex, out var customValue))
            {
                if (customValue is string valueName)
                {
                    return $"{property.TypeName}.{valueName}";
                }

                if (customValue is List<string> valueNames)
                {
                    return string.Join(" | ", valueNames.Select(name => $"{property.TypeName}.{name}"));
                }
            }

            // デフォルト値
            return $"{property.TypeName}.{property.EnumValues[0].Name}";
        }
    }
}