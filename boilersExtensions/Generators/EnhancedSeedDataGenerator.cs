using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using boilersExtensions.Models;
using boilersExtensions.ViewModels;
using static boilersExtensions.Generators.FixedValueCombinationGenerator;

namespace boilersExtensions.Generators
{
    /// <summary>
    /// 固定値に対応したシードデータ生成クラスの拡張
    /// </summary>
    public class EnhancedSeedDataGenerator : SeedDataGenerator
    {
        /// <summary>
        /// 固定値を考慮したシードデータを生成します
        /// </summary>
        public string GenerateSeedDataWithFixedValues(List<EntityInfo> entities, SeedDataConfig config)
        {
            // 依存関係を解決して適切な順序でエンティティを処理
            var orderedEntities = ResolveDependencyOrder(entities);

            var sb = new StringBuilder();

            // ランダム変数の宣言を追加
            sb.AppendLine("    // ランダム生成用のインスタンスを定義");
            sb.AppendLine("    var random = new Random();");
            sb.AppendLine();

            foreach (var entity in orderedEntities)
            {
                // 設定で指定された数のシードデータを生成
                var entityConfig = config.GetEntityConfig(entity.Name);
                if (entityConfig == null || !entityConfig.IsSelected || entityConfig.RecordCount <= 0)
                {
                    continue;
                }

                sb.AppendLine($"    // {entity.Name} エンティティのシードデータ");
                sb.AppendLine($"    modelBuilder.Entity<{entity.Name}>().HasData(");

                // 固定値を持つプロパティを収集
                var propertiesWithFixedValues = new List<PropertyWithFixedValues>();
                foreach (var prop in entity.Properties)
                {
                    if (prop.ExcludeFromSeed || prop.IsNavigationProperty || prop.IsCollection)
                        continue;

                    var propConfig = entityConfig.GetPropertyConfig(prop.Name);
                    if (propConfig is PropertyConfigViewModel vm && vm.FixedValues.Count > 0)
                    {
                        propertiesWithFixedValues.Add(new PropertyWithFixedValues
                        {
                            PropertyName = prop.Name,
                            FixedValues = vm.FixedValues.ToList()
                        });
                    }
                }

                // 固定値の組み合わせがある場合
                if (propertiesWithFixedValues.Count > 0)
                {
                    // すべての組み合わせを生成
                    var combinations = GenerateAllCombinations(propertiesWithFixedValues);
                    int baseRecordCount = entityConfig.RecordCount / Math.Max(1, combinations.Count);
                    baseRecordCount = Math.Max(1, baseRecordCount); // 最低1件は生成

                    int totalRecords = baseRecordCount * combinations.Count;
                    int currentRecord = 0;

                    // 各組み合わせに対して指定数のレコードを生成
                    foreach (var combination in combinations)
                    {
                        for (int i = 0; i < baseRecordCount; i++)
                        {
                            int recordIndex = currentRecord++;
                            sb.AppendLine($"        new {entity.Name}");
                            sb.AppendLine("        {");

                            // プロパティごとに値を生成
                            var propStrings = new List<string>();
                            foreach (var prop in entity.Properties)
                            {
                                // シードデータから除外するプロパティはスキップ
                                if (prop.ExcludeFromSeed || prop.IsNavigationProperty || prop.IsCollection)
                                {
                                    continue;
                                }

                                string propValue;
                                // 固定値が設定されているかチェック
                                if (combination.PropertyValues.TryGetValue(prop.Name, out string fixedValue))
                                {
                                    // 型に応じて固定値をフォーマット
                                    if (prop.TypeName == "String" || prop.TypeName.Contains("string"))
                                    {
                                        propValue = $"\"{fixedValue}\"";
                                    }
                                    else
                                    {
                                        propValue = fixedValue;
                                    }
                                }
                                else
                                {
                                    // 通常の値生成
                                    propValue = GeneratePropertyValue(prop, recordIndex, entityConfig);
                                }

                                if (propValue != null)
                                {
                                    propStrings.Add($"            {prop.Name} = {propValue}");
                                }
                            }

                            sb.AppendLine(string.Join(",\r\n", propStrings));
                            sb.AppendLine("        }" + (currentRecord < totalRecords ? "," : ""));
                        }
                    }
                }
                else
                {
                    // 固定値の設定がない場合は通常のレコード生成
                    for (int i = 0; i < entityConfig.RecordCount; i++)
                    {
                        sb.AppendLine($"        new {entity.Name}");
                        sb.AppendLine("        {");

                        // プロパティごとに値を生成
                        var propStrings = new List<string>();
                        foreach (var prop in entity.Properties)
                        {
                            // シードデータから除外するプロパティはスキップ
                            if (prop.ExcludeFromSeed || prop.IsNavigationProperty || prop.IsCollection)
                            {
                                continue;
                            }

                            // EqualityContract プロパティはスキップ (record型で自動生成される)
                            if (prop.Name == "EqualityContract")
                            {
                                continue;
                            }

                            // 読み取り専用プロパティはスキップ
                            if (prop.Symbol != null && prop.Symbol.SetMethod == null)
                            {
                                continue;
                            }

                            // プロパティの値を生成
                            string propValue = GeneratePropertyValue(prop, i, entityConfig);
                            if (propValue != null)
                            {
                                propStrings.Add($"            {prop.Name} = {propValue}");
                            }
                        }

                        sb.AppendLine(string.Join(",\r\n", propStrings));
                        sb.AppendLine("        }" + (i < entityConfig.RecordCount - 1 ? "," : ""));
                    }
                }

                sb.AppendLine("    );");
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}