using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using boilersExtensions.Generators;
using boilersExtensions.Models;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace boilersExtensions.Utils
{
    /// <summary>
    ///     シードデータを各種ファイルに挿入するユーティリティクラス
    /// </summary>
    public class SeedDataInsertExecutor
    {
        private readonly AsyncPackage _package;
        private readonly RandomDataProvider _randomDataProvider;
        private readonly SeedDataGenerator _seedGenerator;

        /// <summary>
        ///     コンストラクタ
        /// </summary>
        public SeedDataInsertExecutor(AsyncPackage package)
        {
            _package = package;
            _randomDataProvider = new RandomDataProvider();
            _seedGenerator = new SeedDataGenerator();
        }

        /// <summary>
        ///     C#クラスにシードデータメソッドを挿入します
        /// </summary>
        public async Task<bool> InsertSeedMethodToCSharpClassAsync(
            Document document,
            string className,
            List<PropertyInfo> properties,
            int recordCount)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // EntityInfoを作成
                var entityInfo = new EntityInfo { Name = className, Properties = properties };

                // SeedDataConfig作成
                var config = new SeedDataConfig();

                // EntityConfigを設定
                var entityConfig = new EntityConfigViewModel
                {
                    EntityName = className, RecordCount = { Value = recordCount }, IsSelected = { Value = true }
                };

                // プロパティ設定を追加
                foreach (var prop in properties)
                {
                    var propConfig = new PropertyConfigViewModel
                    {
                        PropertyName = prop.Name, PropertyTypeName = prop.TypeName
                    };

                    entityConfig.PropertyConfigs.Add(propConfig);
                }

                config.UpdateEntityConfig(entityConfig);

                // 改善されたSeedDataGeneratorを使用してシードデータを生成
                var generatedCode = _seedGenerator.GenerateSeedData(new List<EntityInfo> { entityInfo }, config);

                // 生成されたコードを挿入
                return await InsertGeneratedCodeToDocument(document, className, generatedCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in InsertSeedMethodToCSharpClassAsync: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        ///     生成されたコードをドキュメントに挿入します
        /// </summary>
        internal async Task<bool> InsertGeneratedCodeToDocument(Document document, string className,
            string generatedCode)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // ドキュメントオブジェクトをTextDocumentに変換
                var textDocument = document.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    Debug.WriteLine("TextDocument is null");
                    return false;
                }

                // クラスの閉じ括弧（}）を探して、その前にメソッドを挿入
                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var documentText = editPoint.GetText(textDocument.EndPoint);

                // クラス定義を正規表現で検索
                var classRegex =
                    new Regex($@"(class|record)\s+{className}(?:\s*:\s*\w+(?:<.*>)?(?:\s*,\s*\w+(?:<.*>)?)*)??\s*\{{");
                var classMatch = classRegex.Match(documentText);
                if (!classMatch.Success)
                {
                    Debug.WriteLine($"Class {className} not found");
                    return false;
                }

                // クラスの閉じ括弧を検索するため、クラス開始位置以降のテキストを取得
                var classStartPos = classMatch.Index + classMatch.Length;
                var remainingText = documentText.Substring(classStartPos);

                // 括弧の深さを追跡して正しい閉じ括弧を見つける
                var depth = 1;
                var closePos = -1;
                for (var i = 0; i < remainingText.Length; i++)
                {
                    if (remainingText[i] == '{')
                    {
                        depth++;
                    }
                    else if (remainingText[i] == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            closePos = i;
                            break;
                        }
                    }
                }

                if (closePos == -1)
                {
                    Debug.WriteLine("Could not find closing brace of class");
                    return false;
                }

                // 別の方法で挿入位置に移動 - 行と文字位置を使用
                var insertPos = classStartPos + closePos;

                // オフセットから行と列位置を計算
                var line = 1;
                var column = 1;
                for (var i = 0; i < insertPos; i++)
                {
                    if (documentText[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }
                }

                // 行と列位置を使用して移動
                var insertPoint = textDocument.CreateEditPoint();
                insertPoint.LineDown(line - 1); // 行に移動
                insertPoint.CharRight(column - 1); // 列に移動

                // GenerateSeedDataメソッドを挿入
                var methodCode = "\n\n    public static void GenerateSeedData(ModelBuilder modelBuilder)\n    {\n" +
                                 generatedCode + "\n    }\n";
                insertPoint.Insert(methodCode);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inserting code: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        ///     JSONファイルにシードデータを挿入します
        /// </summary>
        public async Task<bool> InsertSeedDataToJsonFileAsync(
            Document document,
            List<string> propertyNames,
            int recordCount)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var textDocument = document.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    return false;
                }

                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var documentText = editPoint.GetText(textDocument.EndPoint);

                // JSON配列を生成
                var jsonBuilder = new StringBuilder();
                jsonBuilder.AppendLine("[");

                for (var i = 0; i < recordCount; i++)
                {
                    jsonBuilder.AppendLine("  {");
                    for (var j = 0; j < propertyNames.Count; j++)
                    {
                        var propName = propertyNames[j];
                        var value = GetJsonValueByPropertyName(propName, i);

                        if (j < propertyNames.Count - 1)
                        {
                            jsonBuilder.AppendLine($"    \"{propName}\": {value},");
                        }
                        else
                        {
                            jsonBuilder.AppendLine($"    \"{propName}\": {value}");
                        }
                    }

                    if (i < recordCount - 1)
                    {
                        jsonBuilder.AppendLine("  },");
                    }
                    else
                    {
                        jsonBuilder.AppendLine("  }");
                    }
                }

                jsonBuilder.AppendLine("]");

                // 既存のファイル内容を上書き
                editPoint.Delete(textDocument.EndPoint);
                editPoint.Insert(jsonBuilder.ToString());

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in InsertSeedDataToJsonFileAsync: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        ///     CSVファイルにシードデータを挿入します
        /// </summary>
        public async Task<bool> InsertSeedDataToCsvFileAsync(
            Document document,
            List<string> headers,
            int recordCount)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var textDocument = document.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    return false;
                }

                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var documentText = editPoint.GetText(textDocument.EndPoint);

                // CSVを生成
                var csvBuilder = new StringBuilder();

                // ヘッダー行
                csvBuilder.AppendLine(string.Join(",", headers));

                // データ行
                for (var i = 0; i < recordCount; i++)
                {
                    var values = new List<string>();
                    foreach (var header in headers)
                    {
                        values.Add(GetCsvValueByPropertyName(header, i));
                    }

                    csvBuilder.AppendLine(string.Join(",", values));
                }

                // 既存のファイル内容を上書き
                editPoint.Delete(textDocument.EndPoint);
                editPoint.Insert(csvBuilder.ToString());

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in InsertSeedDataToCsvFileAsync: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        ///     SQLファイルにシードデータを挿入します
        /// </summary>
        public async Task<bool> InsertSeedDataToSqlFileAsync(
            Document document,
            string tableName,
            List<(string Name, string Type)> columns,
            int recordCount)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var textDocument = document.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    return false;
                }

                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var documentText = editPoint.GetText(textDocument.EndPoint);

                // SQL INSERTを生成
                var sqlBuilder = new StringBuilder();
                sqlBuilder.AppendLine("-- Generated Seed Data");
                sqlBuilder.AppendLine($"-- Generated on {DateTime.Now}");
                sqlBuilder.AppendLine();
                sqlBuilder.AppendLine("BEGIN TRANSACTION;");
                sqlBuilder.AppendLine();

                // カラム名のリスト
                var columnNames = string.Join(", ", columns.Select(c => c.Name));

                for (var i = 0; i < recordCount; i++)
                {
                    var values = new List<string>();
                    foreach (var (name, type) in columns)
                    {
                        var value = GetSqlValueByPropertyNameAndType(name, type, i);
                        values.Add(value);
                    }

                    var valueList = string.Join(", ", values);
                    sqlBuilder.AppendLine($"INSERT INTO {tableName} ({columnNames}) VALUES ({valueList});");
                }

                sqlBuilder.AppendLine();
                sqlBuilder.AppendLine("COMMIT;");

                // ドキュメントの末尾に挿入
                var endPoint = textDocument.EndPoint.CreateEditPoint();
                endPoint.Insert(sqlBuilder.ToString());

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in InsertSeedDataToSqlFileAsync: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        ///     XMLファイルにシードデータを挿入します
        /// </summary>
        public async Task<bool> InsertSeedDataToXmlFileAsync(
            Document document,
            string rootElement,
            string itemElement,
            List<string> attributes,
            int recordCount)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var textDocument = document.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    return false;
                }

                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var documentText = editPoint.GetText(textDocument.EndPoint);

                // XMLを生成
                var xmlBuilder = new StringBuilder();
                xmlBuilder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                xmlBuilder.AppendLine($"<{rootElement}>");

                for (var i = 0; i < recordCount; i++)
                {
                    xmlBuilder.Append($"  <{itemElement}");

                    foreach (var attrName in attributes)
                    {
                        var value = GetXmlValueByPropertyName(attrName, i);
                        xmlBuilder.Append($" {attrName}=\"{value}\"");
                    }

                    xmlBuilder.AppendLine("/>");
                }

                xmlBuilder.AppendLine($"</{rootElement}>");

                // 既存のファイル内容を上書き
                editPoint.Delete(textDocument.EndPoint);
                editPoint.Insert(xmlBuilder.ToString());

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in InsertSeedDataToXmlFileAsync: {ex.Message}");
                return false;
            }
        }

        #region 値生成ヘルパーメソッド

        private string GetJsonValueByPropertyName(string propName, int index)
        {
            propName = propName.ToLowerInvariant();

            if (propName == "id" || propName.EndsWith("id"))
            {
                return (index + 1).ToString();
            }

            if (propName.Contains("name"))
            {
                return $"\"{_randomDataProvider.GetRandomPersonName()} {index + 1}\"";
            }

            if (propName.Contains("email"))
            {
                return $"\"user{index + 1}@example.com\"";
            }

            if (propName.Contains("date") || propName.Contains("time"))
            {
                return $"\"{DateTime.Now.AddDays(-index).ToString("yyyy-MM-dd")}\"";
            }

            if (propName.Contains("bool") || propName.Contains("is") || propName == "active" || propName == "enabled")
            {
                return index % 2 == 0 ? "true" : "false";
            }

            if (propName.Contains("price") || propName.Contains("amount") || propName.Contains("cost"))
            {
                return Math.Round(9.99 + (index * 10.0), 2).ToString();
            }

            return $"\"Value {propName} {index + 1}\"";
        }

        private string GetCsvValueByPropertyName(string header, int index)
        {
            header = header.ToLowerInvariant();

            if (header == "id" || header.EndsWith("id"))
            {
                return (index + 1).ToString();
            }

            if (header.Contains("name"))
            {
                // CSVではカンマを含む可能性があるため、ダブルクォートで囲む
                return $"\"{_randomDataProvider.GetRandomPersonName()} {index + 1}\"";
            }

            if (header.Contains("email"))
            {
                return $"user{index + 1}@example.com";
            }

            if (header.Contains("date") || header.Contains("time"))
            {
                return DateTime.Now.AddDays(-index).ToString("yyyy-MM-dd");
            }

            if (header.Contains("bool") || header.Contains("is") || header == "active" || header == "enabled")
            {
                return index % 2 == 0 ? "true" : "false";
            }

            if (header.Contains("price") || header.Contains("amount") || header.Contains("cost"))
            {
                return Math.Round(9.99 + (index * 10.0), 2).ToString();
            }

            return $"Value {header} {index + 1}";
        }

        private string GetSqlValueByPropertyNameAndType(string columnName, string typeName, int index)
        {
            columnName = columnName.ToLowerInvariant();
            typeName = typeName.ToLowerInvariant();

            if (columnName == "id" || columnName.EndsWith("id"))
            {
                return (index + 1).ToString();
            }

            if (typeName.Contains("varchar") || typeName.Contains("text") || typeName.Contains("char"))
            {
                if (columnName.Contains("name"))
                {
                    return $"'{_randomDataProvider.GetRandomPersonName()} {index + 1}'";
                }

                if (columnName.Contains("email"))
                {
                    return $"'user{index + 1}@example.com'";
                }

                return $"'Value {columnName} {index + 1}'";
            }

            if (typeName.Contains("date") || typeName.Contains("time"))
            {
                return $"'{DateTime.Now.AddDays(-index).ToString("yyyy-MM-dd")}'";
            }

            if (typeName.Contains("bool") || typeName.Contains("bit"))
            {
                return index % 2 == 0 ? "1" : "0";
            }

            if (typeName.Contains("int"))
            {
                return (10 + (index * 5)).ToString();
            }

            if (typeName.Contains("decimal") || typeName.Contains("numeric") || typeName.Contains("float") ||
                typeName.Contains("real"))
            {
                return Math.Round(9.99 + (index * 10.0), 2).ToString();
            }

            return $"'{columnName}_{index + 1}'";
        }

        private string GetXmlValueByPropertyName(string attributeName, int index)
        {
            attributeName = attributeName.ToLowerInvariant();

            if (attributeName == "id" || attributeName.EndsWith("id"))
            {
                return (index + 1).ToString();
            }

            if (attributeName.Contains("name"))
            {
                return _randomDataProvider.GetRandomPersonName() + " " + (index + 1);
            }

            if (attributeName.Contains("email"))
            {
                return $"user{index + 1}@example.com";
            }

            if (attributeName.Contains("date") || attributeName.Contains("time"))
            {
                return DateTime.Now.AddDays(-index).ToString("yyyy-MM-dd");
            }

            if (attributeName.Contains("bool") || attributeName.Contains("is") || attributeName == "active" ||
                attributeName == "enabled")
            {
                return index % 2 == 0 ? "true" : "false";
            }

            if (attributeName.Contains("price") || attributeName.Contains("amount") || attributeName.Contains("cost"))
            {
                return Math.Round(9.99 + (index * 10.0), 2).ToString();
            }

            return $"Value {attributeName} {index + 1}";
        }

        #endregion
    }
}