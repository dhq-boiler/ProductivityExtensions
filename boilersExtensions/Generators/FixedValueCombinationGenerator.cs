using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace boilersExtensions.Generators
{
    /// <summary>
    /// 固定値を持つプロパティの組み合わせをすべて生成するヘルパークラス
    /// </summary>
    public class FixedValueCombinationGenerator
    {
        /// <summary>
        /// 固定値を持つプロパティの情報
        /// </summary>
        public class PropertyWithFixedValues
        {
            public string PropertyName { get; set; }
            public List<string> FixedValues { get; set; }
            public int CurrentIndex { get; set; } = 0;
        }

        /// <summary>
        /// 組み合わせの一つ
        /// </summary>
        public class Combination
        {
            public Dictionary<string, string> PropertyValues { get; } = new Dictionary<string, string>();
        }

        /// <summary>
        /// すべての組み合わせを生成
        /// </summary>
        public static List<Combination> GenerateAllCombinations(List<PropertyWithFixedValues> properties)
        {
            var result = new List<Combination>();
            GenerateCombinationsRecursive(properties, 0, new Combination(), result);
            return result;
        }

        /// <summary>
        /// 再帰的に組み合わせを生成
        /// </summary>
        private static void GenerateCombinationsRecursive(
            List<PropertyWithFixedValues> properties,
            int propertyIndex,
            Combination currentCombination,
            List<Combination> result)
        {
            // 終了条件：すべてのプロパティを処理した
            if (propertyIndex >= properties.Count)
            {
                result.Add(CloneCombination(currentCombination));
                return;
            }

            // 現在のプロパティの固定値ごとに再帰
            var property = properties[propertyIndex];
            for (int i = 0; i < property.FixedValues.Count; i++)
            {
                // 現在の組み合わせに値を追加
                currentCombination.PropertyValues[property.PropertyName] = property.FixedValues[i];

                // 次のプロパティへ
                GenerateCombinationsRecursive(properties, propertyIndex + 1, currentCombination, result);

                // バックトラック（クリーンアップは不要、上書きされるため）
            }
        }

        /// <summary>
        /// 組み合わせのディープコピーを作成
        /// </summary>
        private static Combination CloneCombination(Combination original)
        {
            var clone = new Combination();
            foreach (var kvp in original.PropertyValues)
            {
                clone.PropertyValues[kvp.Key] = kvp.Value;
            }
            return clone;
        }
    }
}
