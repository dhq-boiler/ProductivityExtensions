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
            sb.AppendLine("        // ランダム生成用のインスタンスを定義");
            sb.AppendLine("        var random = new Random();");
            sb.AppendLine();

            // 生成済みの主キー値を管理するディクショナリ
            var generatedPrimaryKeys = new Dictionary<string, List<object>>();

            // 外部キー関係のマッピング（親エンティティ名→子エンティティ名→プロパティ名のマップ）
            var foreignKeyMappings = BuildForeignKeyMappings(entities);

            // 各エンティティのレコード数を取得
            var entityRecordCounts = new Dictionary<string, int>();
            foreach (var entity in orderedEntities)
            {
                var entityConfig = config.GetEntityConfig(entity.Name);
                if (entityConfig != null && entityConfig.IsSelected)
                {
                    entityRecordCounts[entity.Name] = entityConfig.RecordCount;
                }
            }

            foreach (var entity in orderedEntities)
            {
                // 設定で指定された数のシードデータを生成
                var entityConfig = config.GetEntityConfig(entity.Name);
                if (entityConfig == null || !entityConfig.IsSelected || entityConfig.RecordCount <= 0)
                {
                    continue;
                }

                // このエンティティへの外部キー参照を持つエンティティがあるかどうかを確認
                var hasChildEntities = foreignKeyMappings.ContainsKey(entity.Name);

                sb.AppendLine($"        // {entity.Name} エンティティのシードデータ");
                sb.AppendLine($"        modelBuilder.Entity<{entity.Name}>().HasData(");

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

                // 生成するレコード数を決定
                int recordsToGenerate = entityConfig.RecordCount;

                // 主キー値のリストを初期化
                generatedPrimaryKeys[entity.Name] = new List<object>();

                // 各レコードを生成
                for (int i = 0; i < recordsToGenerate; i++)
                {
                    sb.AppendLine($"            new {entity.Name}");
                    sb.AppendLine("            {");

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

                        // 外部キープロパティの場合
                        if (prop.IsForeignKey)
                        {
                            string propValue = GenerateForeignKeyValue(prop, i, entityConfig, entity.Name, generatedPrimaryKeys);
                            if (propValue != null)
                            {
                                propStrings.Add($"                {prop.Name} = {propValue}");
                            }
                            continue;
                        }

                        // 主キープロパティの場合、キー値を保存
                        if (prop.IsKey)
                        {
                            // プロパティの値を生成
                            string propValue = GeneratePropertyValue(prop, i, entityConfig);
                            if (propValue != null)
                            {
                                propStrings.Add($"                {prop.Name} = {propValue}");

                                // Guidや数値型（正規表現で判定）で値を抽出
                                if (propValue.Contains("Guid"))
                                {
                                    // "new Guid("xxxxx")" 形式からGUID文字列を抽出
                                    var guidMatch = System.Text.RegularExpressions.Regex.Match(propValue, "\"(.+?)\"");
                                    if (guidMatch.Success)
                                    {
                                        generatedPrimaryKeys[entity.Name].Add(guidMatch.Groups[1].Value);
                                    }
                                }
                                else if (int.TryParse(propValue, out int intValue))
                                {
                                    generatedPrimaryKeys[entity.Name].Add(intValue);
                                }
                                else
                                {
                                    // その他の型は文字列として保存
                                    generatedPrimaryKeys[entity.Name].Add(propValue);
                                }
                            }
                            continue;
                        }

                        // 通常のプロパティの値を生成
                        string value = GeneratePropertyValue(prop, i, entityConfig);
                        if (value != null)
                        {
                            propStrings.Add($"                {prop.Name} = {value}");
                        }
                    }

                    sb.AppendLine(string.Join(",\r\n", propStrings));
                    sb.AppendLine("            }" + (i < recordsToGenerate - 1 ? "," : ""));
                }

                sb.AppendLine("        );");
                sb.AppendLine();
            }

            // 次に子エンティティを処理
            foreach (var entity in orderedEntities)
            {
                // 設定で指定された数のシードデータを生成
                var entityConfig = config.GetEntityConfig(entity.Name);
                if (entityConfig == null || !entityConfig.IsSelected || entityConfig.RecordCount <= 0)
                {
                    continue;
                }

                // このエンティティが外部キー参照を持っているかどうかを確認
                bool hasForeignKeys = entity.Properties.Any(p => p.IsForeignKey);

                // 外部キー参照を持たないまたは既に処理済みの場合はスキップ
                if (!hasForeignKeys || generatedPrimaryKeys.ContainsKey(entity.Name))
                {
                    continue;
                }

                sb.AppendLine($"        // {entity.Name} エンティティのシードデータ (外部キー参照あり)");
                sb.AppendLine($"        modelBuilder.Entity<{entity.Name}>().HasData(");

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

                // 生成するレコード数
                int recordsToGenerate = entityConfig.RecordCount;

                // 主キー値のリストを初期化
                generatedPrimaryKeys[entity.Name] = new List<object>();

                // 外部キーを持つプロパティを抽出
                var foreignKeyProps = entity.Properties.Where(p => p.IsForeignKey).ToList();

                // 親エンティティごとのレコード数を計算
                var recordsPerParent = new Dictionary<string, int>();
                foreach (var fkProp in foreignKeyProps)
                {
                    if (!string.IsNullOrEmpty(fkProp.ForeignKeyTargetEntity) &&
                        generatedPrimaryKeys.ContainsKey(fkProp.ForeignKeyTargetEntity))
                    {
                        int parentCount = generatedPrimaryKeys[fkProp.ForeignKeyTargetEntity].Count;
                        if (parentCount > 0)
                        {
                            // 均等に分配するための計算
                            recordsPerParent[fkProp.ForeignKeyTargetEntity] = (int)Math.Ceiling((double)recordsToGenerate / parentCount);
                        }
                    }
                }

                // 各レコードを生成
                for (int i = 0; i < recordsToGenerate; i++)
                {
                    sb.AppendLine($"            new {entity.Name}");
                    sb.AppendLine("            {");

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

                        // 外部キープロパティの場合、親エンティティの主キー値を均等に分配
                        if (prop.IsForeignKey)
                        {
                            string targetEntity = prop.ForeignKeyTargetEntity;
                            if (!string.IsNullOrEmpty(targetEntity) && generatedPrimaryKeys.ContainsKey(targetEntity))
                            {
                                var parentKeys = generatedPrimaryKeys[targetEntity];
                                if (parentKeys.Count > 0)
                                {
                                    // 親エンティティに均等に分配するための計算
                                    int parentIndex = i / (recordsPerParent.ContainsKey(targetEntity) ? recordsPerParent[targetEntity] : 1);
                                    parentIndex = Math.Min(parentIndex, parentKeys.Count - 1); // 配列の範囲を超えないように

                                    var parentKey = parentKeys[parentIndex];

                                    // 親キーの型に応じたフォーマット
                                    string fkValue;
                                    if (parentKey is string strKey && System.Text.RegularExpressions.Regex.IsMatch(strKey, @"^[\da-fA-F]{8}(-[\da-fA-F]{4}){3}-[\da-fA-F]{12}$"))
                                    {
                                        // GUIDの場合
                                        fkValue = $"new Guid(\"{strKey}\")";
                                    }
                                    else if (parentKey is int intKey)
                                    {
                                        // 整数の場合
                                        fkValue = intKey.ToString();
                                    }
                                    else
                                    {
                                        // その他の型は文字列として扱う
                                        fkValue = parentKey.ToString();
                                    }

                                    propStrings.Add($"                {prop.Name} = {fkValue}");
                                }
                            }
                            else
                            {
                                // 親エンティティが見つからない場合はデフォルト値を使用
                                string defaultValue = GeneratePropertyValue(prop, i, entityConfig);
                                if (defaultValue != null)
                                {
                                    propStrings.Add($"                {prop.Name} = {defaultValue}");
                                }
                            }
                            continue;
                        }

                        // 主キープロパティの場合、キー値を保存
                        if (prop.IsKey)
                        {
                            string propValue = GeneratePropertyValue(prop, i, entityConfig);
                            if (propValue != null)
                            {
                                propStrings.Add($"                {prop.Name} = {propValue}");

                                // Guidや数値型で値を抽出
                                if (propValue.Contains("Guid"))
                                {
                                    // "new Guid("xxxxx")" 形式からGUID文字列を抽出
                                    var guidMatch = System.Text.RegularExpressions.Regex.Match(propValue, "\"(.+?)\"");
                                    if (guidMatch.Success)
                                    {
                                        generatedPrimaryKeys[entity.Name].Add(guidMatch.Groups[1].Value);
                                    }
                                }
                                else if (int.TryParse(propValue, out int intValue))
                                {
                                    generatedPrimaryKeys[entity.Name].Add(intValue);
                                }
                                else
                                {
                                    // その他の型は文字列として保存
                                    generatedPrimaryKeys[entity.Name].Add(propValue);
                                }
                            }
                            continue;
                        }

                        // 通常のプロパティの値を生成
                        string value = GeneratePropertyValue(prop, i, entityConfig);
                        if (value != null)
                        {
                            propStrings.Add($"                {prop.Name} = {value}");
                        }
                    }

                    sb.AppendLine(string.Join(",\r\n", propStrings));
                    sb.AppendLine("            }" + (i < recordsToGenerate - 1 ? "," : ""));
                }

                sb.AppendLine("        );");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// 外部キー値を生成します
        /// </summary>
        private string GenerateForeignKeyValue(
            PropertyInfo property,
            int recordIndex,
            EntityConfigViewModel entityConfig,
            string entityName,
            Dictionary<string, List<object>> generatedPrimaryKeys)
        {
            // プロパティ設定が指定されている場合はそれを優先
            var propConfig = entityConfig.GetPropertyConfig(property.Name);
            if (propConfig != null)
            {
                // カスタム戦略の処理
                if (propConfig.UseCustomStrategy)
                {
                    return propConfig.CustomValue;
                }

                // 固定値の処理
                if (propConfig.HasFixedValues)
                {
                    int fixedValueIndex = recordIndex % propConfig.FixedValues.Count;
                    string fixedValue = propConfig.FixedValues[fixedValueIndex];

                    // 型に応じてフォーマット
                    if (property.TypeName == "String" || property.TypeName.Contains("string"))
                    {
                        return $"\"{fixedValue}\"";
                    }

                    return fixedValue;
                }
            }

            // 参照先エンティティが指定されている場合
            string targetEntity = property.ForeignKeyTargetEntity;
            if (!string.IsNullOrEmpty(targetEntity) && generatedPrimaryKeys.ContainsKey(targetEntity))
            {
                var parentKeys = generatedPrimaryKeys[targetEntity];
                if (parentKeys.Count > 0)
                {
                    // 親エンティティのレコード数に基づいて、均等に分配
                    int parentEntityCount = parentKeys.Count;
                    int parentRecordIndex = recordIndex % parentEntityCount;

                    var parentKey = parentKeys[parentRecordIndex];

                    // 親キーの型に応じたフォーマット
                    if (parentKey is string strKey && System.Text.RegularExpressions.Regex.IsMatch(strKey, @"^[\da-fA-F]{8}(-[\da-fA-F]{4}){3}-[\da-fA-F]{12}$"))
                    {
                        // GUIDの場合
                        return $"new Guid(\"{strKey}\")";
                    }
                    else if (parentKey is int intKey)
                    {
                        // 整数の場合
                        return intKey.ToString();
                    }
                    else
                    {
                        // その他の型は文字列として扱う
                        return parentKey.ToString();
                    }
                }
            }

            // 標準の外部キー生成に戻る
            return base.GenerateForeignKeyValue(property, recordIndex, entityConfig, propConfig);
        }

        /// <summary>
        /// エンティティ間の外部キー関係をマッピングします
        /// </summary>
        private Dictionary<string, Dictionary<string, List<string>>> BuildForeignKeyMappings(List<EntityInfo> entities)
        {
            var mappings = new Dictionary<string, Dictionary<string, List<string>>>();

            foreach (var entity in entities)
            {
                foreach (var prop in entity.Properties.Where(p => p.IsForeignKey))
                {
                    string targetEntity = prop.ForeignKeyTargetEntity;
                    if (!string.IsNullOrEmpty(targetEntity))
                    {
                        // parent -> child -> foreignKeyProperties マッピング
                        if (!mappings.ContainsKey(targetEntity))
                        {
                            mappings[targetEntity] = new Dictionary<string, List<string>>();
                        }

                        if (!mappings[targetEntity].ContainsKey(entity.Name))
                        {
                            mappings[targetEntity][entity.Name] = new List<string>();
                        }

                        mappings[targetEntity][entity.Name].Add(prop.Name);
                    }
                }
            }

            return mappings;
        }
    }
}