using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using boilersExtensions.Models;
using static boilersExtensions.Generators.FixedValueCombinationGenerator;

namespace boilersExtensions.Generators
{
    /// <summary>
    /// 親子関係を考慮した改良版シードデータ生成クラス
    /// </summary>
    public class EnhancedRelationalSeedDataGenerator : EnhancedSeedDataGenerator
    {
        /// <summary>
        /// 親子関係を考慮してシードデータを生成します
        /// </summary>
        public string GenerateSeedDataWithRelationships(List<EntityInfo> entities, SeedDataConfig config)
        {
            // 依存関係を解決して適切な順序でエンティティを処理
            var orderedEntities = ResolveDependencyOrder(entities);

            var sb = new StringBuilder();

            // ランダム変数の宣言を追加
            sb.AppendLine("        // ランダム生成用のインスタンスを定義");
            sb.AppendLine("        var random = new Random();");
            sb.AppendLine();

            // 生成した主キー値を保存するディクショナリ
            var generatedKeys = new Dictionary<string, List<object>>();

            // まず親エンティティを処理
            for (int i = 0; i < orderedEntities.Count; i++)
            {
                var entity = orderedEntities[i];
                var entityConfig = config.GetEntityConfig(entity.Name);

                if (entityConfig == null || !entityConfig.IsSelected.Value)
                    continue;

                // 親エンティティを持つかどうか
                bool hasParent = entityConfig.HasParent.Value && entityConfig.ParentEntity.Value != null;

                sb.AppendLine($"        // {entity.Name} エンティティのシードデータ");
                sb.AppendLine($"        modelBuilder.Entity<{entity.Name}>().HasData(");

                int recordsToGenerate;

                if (hasParent)
                {
                    // 親エンティティを持つ場合
                    var parentEntityName = entityConfig.ParentEntity.Value.EntityName;

                    // 親エンティティの主キー値がまだ生成されていない場合はスキップ
                    if (!generatedKeys.ContainsKey(parentEntityName) || generatedKeys[parentEntityName].Count == 0)
                    {
                        sb.AppendLine("            // 親エンティティのシードデータが必要です");
                        sb.AppendLine("        );");
                        sb.AppendLine();
                        continue;
                    }

                    // 「親の件数 × 親1件あたりの件数」を計算
                    int parentRecordCount = generatedKeys[parentEntityName].Count;
                    int recordsPerParent = entityConfig.RecordsPerParent.Value;
                    recordsToGenerate = parentRecordCount * recordsPerParent;
                }
                else
                {
                    // 親がない場合は直接指定された件数
                    recordsToGenerate = entityConfig.RecordCount.Value;
                }

                // このエンティティのキー値を追跡するリストを初期化
                generatedKeys[entity.Name] = new List<object>();

                // 各レコードを生成
                for (int recordIndex = 0; recordIndex < recordsToGenerate; recordIndex++)
                {
                    sb.AppendLine($"            new {entity.Name}");
                    sb.AppendLine("            {");

                    // プロパティごとに値を生成
                    var propStrings = new List<string>();
                    foreach (var prop in entity.Properties)
                    {
                        // シードデータから除外するプロパティはスキップ
                        if (prop.ExcludeFromSeed || prop.IsNavigationProperty || prop.IsCollection)
                            continue;

                        // EqualityContract プロパティはスキップ (record型で自動生成される)
                        if (prop.Name == "EqualityContract")
                            continue;

                        // 読み取り専用プロパティはスキップ
                        if (prop.Symbol != null && prop.Symbol.SetMethod == null)
                            continue;

                        // 外部キープロパティの場合、親エンティティの主キー値を参照
                        if (prop.IsForeignKey && hasParent &&
                            !string.IsNullOrEmpty(prop.ForeignKeyTargetEntity) &&
                            prop.ForeignKeyTargetEntity == entityConfig.ParentEntity.Value.EntityName)
                        {
                            var parentKeys = generatedKeys[prop.ForeignKeyTargetEntity];

                            // 親レコードを特定（均等に分配）
                            int recordsPerParent = entityConfig.RecordsPerParent.Value;
                            int parentIndex = recordIndex / recordsPerParent;

                            // インデックスの範囲チェック
                            if (parentIndex < parentKeys.Count)
                            {
                                var parentKey = parentKeys[parentIndex];

                                // 主キーのフォーマット
                                string fkValue;
                                if (parentKey is string strKey && Guid.TryParse(strKey, out _))
                                {
                                    // GUIDの場合
                                    fkValue = $"new Guid(\"{strKey}\")";
                                }
                                else
                                {
                                    // その他の型はそのまま
                                    fkValue = parentKey.ToString();
                                }

                                propStrings.Add($"                {prop.Name} = {fkValue}");
                                continue;
                            }
                        }

                        // 主キープロパティの場合、値をキャッシュに保存
                        if (prop.IsKey)
                        {
                            string keyValue;

                            // カスタム戦略でIDを生成
                            if (prop.TypeName == "Guid" || prop.TypeName.Contains("Guid"))
                            {
                                // Guid型の場合は決定論的なGUIDを生成
                                string guidString = GenerateDeterministicGuid(entity.Name, recordIndex);
                                keyValue = $"new Guid(\"{guidString}\")";
                                generatedKeys[entity.Name].Add(guidString);
                            }
                            else if (prop.TypeName == "Int32" || prop.TypeName == "Int" ||
                                     prop.TypeName == "Int64" || prop.TypeName == "Long")
                            {
                                // 整数型の場合はインデックス+1を使用
                                int intKey = recordIndex + 1;
                                keyValue = intKey.ToString();
                                generatedKeys[entity.Name].Add(intKey);
                            }
                            else
                            {
                                // その他の型はプロパティ値生成を使用
                                keyValue = GeneratePropertyValue(prop, recordIndex, entityConfig);
                                generatedKeys[entity.Name].Add(keyValue);
                            }

                            propStrings.Add($"                {prop.Name} = {keyValue}");
                            continue;
                        }

                        // その他のプロパティは通常生成
                        string propValue = GeneratePropertyValue(prop, recordIndex, entityConfig);
                        if (propValue != null)
                        {
                            propStrings.Add($"                {prop.Name} = {propValue}");
                        }
                    }

                    sb.AppendLine(string.Join(",\r\n", propStrings));
                    sb.AppendLine("            }" + (recordIndex < recordsToGenerate - 1 ? "," : ""));
                }

                sb.AppendLine("        );");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// 決定論的なGUIDを生成します
        /// </summary>
        private string GenerateDeterministicGuid(string prefix, int index)
        {
            // シード文字列を作成
            string seedString = $"{prefix}_{index}_{DateTime.Now.Ticks}";

            // MD5ハッシュを使用（SHA256なども使用可能）
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(seedString);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                // GUID形式で返す
                return new Guid(hashBytes).ToString();
            }
        }
    }
}