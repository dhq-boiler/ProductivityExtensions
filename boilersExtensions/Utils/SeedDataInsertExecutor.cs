using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Documents;
using boilersExtensions.Models;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Threading;

namespace boilersExtensions.Utils
{
    /// <summary>
    /// シードデータの挿入を実行するユーティリティクラス
    /// </summary>
    public class SeedDataInsertExecutor
    {
        private readonly AsyncPackage _package;
        private readonly DTE _dte;
        private readonly Random _random = new Random();

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="package">Visual Studioパッケージ</param>
        public SeedDataInsertExecutor(AsyncPackage package)
        {
            _package = package;
            ThreadHelper.ThrowIfNotOnUIThread();
            _dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
        }

        /// <summary>
        /// C#クラスにシードデータ挿入メソッドを作成
        /// </summary>
        /// <param name="document">対象ドキュメント</param>
        /// <param name="className">クラス名</param>
        /// <param name="properties">プロパティのリスト(型名,プロパティ名)</param>
        /// <param name="count">生成するデータ数</param>
        /// <returns>挿入の成否</returns>
        public async Task<bool> InsertSeedMethodToCSharpClassAsync(Document document, string className,
                                                                    List<PropertyInfo> properties, int count)
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
                var classRegex = new Regex($@"(class|record)\s+{className}(?:\s*:\s*\w+(?:<.*>)?(?:\s*,\s*\w+(?:<.*>)?)*)??\s*\{{");
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
                int depth = 1;
                int closePos = -1;
                for (int i = 0; i < remainingText.Length; i++)
                {
                    if (remainingText[i] == '{') depth++;
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

                // 挿入するシードメソッドを生成
                var seedMethodContent = GenerateSeedMethodContent(className, properties, count);

                // 別の方法で挿入位置に移動 - 行と文字位置を使用
                var insertPos = classStartPos + closePos;

                // オフセットから行と列位置を計算
                int line = 1;
                int column = 1;
                for (int i = 0; i < insertPos; i++)
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
                insertPoint.LineDown(line - 1);  // 行に移動
                insertPoint.CharRight(column - 1); // 列に移動

                // メソッドを挿入
                insertPoint.Insert("\n\n" + seedMethodContent + "\n");

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inserting seed method: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// JSONファイルに配列形式のシードデータを挿入
        /// </summary>
        /// <param name="document">対象ドキュメント</param>
        /// <param name="properties">プロパティのリスト(名前のみ)</param>
        /// <param name="count">生成するデータ数</param>
        /// <returns>挿入の成否</returns>
        public async Task<bool> InsertSeedDataToJsonFileAsync(Document document, List<string> properties, int count)
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

                // JSONデータを生成
                var jsonData = GenerateJsonData(properties, count);

                // ファイルが空か確認し、空でなければ上書き確認
                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var documentText = editPoint.GetText(textDocument.EndPoint);

                if (!string.IsNullOrWhiteSpace(documentText) && documentText.Trim().Length > 2)
                {
                    // TODO: 上書き確認ダイアログを表示
                    // ここでは単純に上書きする
                }

                // 全選択して置換
                textDocument.Selection.SelectAll();
                textDocument.Selection.Delete();
                textDocument.Selection.Insert(jsonData);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inserting JSON seed data: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// CSVファイルにシードデータを挿入
        /// </summary>
        /// <param name="document">対象ドキュメント</param>
        /// <param name="headers">ヘッダー名のリスト</param>
        /// <param name="count">生成するデータ数</param>
        /// <returns>挿入の成否</returns>
        public async Task<bool> InsertSeedDataToCsvFileAsync(Document document, List<string> headers, int count)
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

                // CSVデータを生成
                var csvData = GenerateCsvData(headers, count);

                // ファイルが空か確認し、空でなければ上書き確認
                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var documentText = editPoint.GetText(textDocument.EndPoint);

                if (!string.IsNullOrWhiteSpace(documentText) && documentText.Trim().Length > 0)
                {
                    // TODO: 上書き確認ダイアログを表示
                    // ここでは単純に上書きする
                }

                // 全選択して置換
                textDocument.Selection.SelectAll();
                textDocument.Selection.Delete();
                textDocument.Selection.Insert(csvData);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inserting CSV seed data: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// SQLファイルにINSERT文を挿入
        /// </summary>
        /// <param name="document">対象ドキュメント</param>
        /// <param name="tableName">テーブル名</param>
        /// <param name="columns">カラム名のリスト</param>
        /// <param name="count">生成するデータ数</param>
        /// <returns>挿入の成否</returns>
        public async Task<bool> InsertSeedDataToSqlFileAsync(Document document, string tableName,
            List<(string columnName, string dataType)> columns, int count)
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

                // SQL INSERT文を生成
                var sqlInserts = GenerateSqlInserts(tableName, columns, count);

                // ファイルの最後に挿入
                textDocument.Selection.EndOfDocument();

                // 現在の位置がファイルの最初の行でない場合は、2行分の改行を追加
                if (textDocument.Selection.CurrentLine > 1)
                {
                    textDocument.Selection.Insert("\n\n");
                }

                // SQL文を挿入
                textDocument.Selection.Insert(sqlInserts);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inserting SQL seed data: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// XMLファイルにシードデータを挿入
        /// </summary>
        /// <param name="document">対象ドキュメント</param>
        /// <param name="rootElement">ルート要素名</param>
        /// <param name="nodeElement">ノード要素名</param>
        /// <param name="attributes">属性のリスト</param>
        /// <param name="count">生成するデータ数</param>
        /// <returns>挿入の成否</returns>
        public async Task<bool> InsertSeedDataToXmlFileAsync(Document document, string rootElement,
            string nodeElement, List<string> attributes, int count)
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

                // XMLデータを生成
                var xmlData = GenerateXmlData(rootElement, nodeElement, attributes, count);

                // ファイルが空か確認し、空でなければ上書き確認
                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var documentText = editPoint.GetText(textDocument.EndPoint);

                if (!string.IsNullOrWhiteSpace(documentText) && documentText.Trim().Length > 10)
                {
                    // TODO: 上書き確認ダイアログを表示
                    // ここでは単純に上書きする
                }

                // 全選択して置換
                textDocument.Selection.SelectAll();
                textDocument.Selection.Delete();
                textDocument.Selection.Insert(xmlData);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inserting XML seed data: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                return false;
            }
        }

        #region 各種シードデータ生成メソッド

        /// <summary>
        /// C#クラスのシードメソッド内容を生成
        /// </summary>
        private string GenerateSeedMethodContent(string className, List<PropertyInfo> properties, int count)
        {
            var sb = new StringBuilder();

            // メソッドシグネチャの生成
            sb.AppendLine($"/// <summary>");
            sb.AppendLine($"/// {count}件のテストデータを生成");
            sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public static List<{className}> GenerateSeedData()");
            sb.AppendLine("{");
            sb.AppendLine($"    var result = new List<{className}>();");
            sb.AppendLine("    var random = new Random();");
            sb.AppendLine($"    for (int i = 0; i < {count}; i++)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var item = new {className}");
            sb.AppendLine("        {");

            // 各プロパティに対して値を生成
            foreach (var prop in properties)
            {
                if (prop.IsNavigationProperty || prop.IsCollection)
                {
                    // ナビゲーションプロパティやコレクションはスキップ
                    continue;
                }

                // プロパティの値を生成
                string value = GeneratePropertyValue(prop, "i", "random");
                if (!string.IsNullOrEmpty(value))
                {
                    sb.AppendLine($"            {prop.Name} = {value},");
                }
            }

            // 末尾のカンマを取り除いて閉じる括弧を追加
            sb.Length = sb.Length - 3; // 最後のカンマと改行を削除
            sb.AppendLine();
            sb.AppendLine("        };");
            sb.AppendLine("        result.Add(item);");
            sb.AppendLine("    }");
            sb.AppendLine("    return result;");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// JSONデータを生成
        /// </summary>
        private string GenerateJsonData(List<string> properties, int count)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[");

            for (int i = 0; i < count; i++)
            {
                sb.AppendLine("  {");

                for (int j = 0; j < properties.Count; j++)
                {
                    var property = properties[j];
                    var value = GetRandomJsonValue(property);

                    sb.Append($"    \"{property}\": {value}");

                    // 最後のプロパティでない場合はカンマを追加
                    if (j < properties.Count - 1)
                    {
                        sb.AppendLine(",");
                    }
                    else
                    {
                        sb.AppendLine();
                    }
                }

                // 最後のアイテムでない場合はカンマを追加
                if (i < count - 1)
                {
                    sb.AppendLine("  },");
                }
                else
                {
                    sb.AppendLine("  }");
                }
            }

            sb.AppendLine("]");

            return sb.ToString();
        }

        private string GeneratePropertyValue(PropertyInfo prop, string index, string randomVarName)
        {
            // プロパティの型に基づいて値を生成
            string typeName = prop.TypeName;

            // Nullableチェック
            bool isNullable = prop.IsNullable;
            string underlyingType = prop.UnderlyingTypeName ?? typeName;

            // 特定の型に基づいた値生成
            if (typeName == "string" || underlyingType == "string")
            {
                // 名前にTitle, Name, Emailなどが含まれる場合は特殊な値を生成
                if (prop.Name.Contains("Title") || prop.Name.Contains("Name"))
                    return $"\"Name{{{index}}}\"";
                else if (prop.Name.Contains("Email"))
                    return $"$\"user{{{index}}}@example.com\"";
                else if (prop.Name.Contains("Uri") || prop.Name.Contains("Url"))
                    return $"$\"https://example.com/item{{{index}}}\"";
                else if (prop.Name.Contains("Path") || prop.Name.Contains("Directory"))
                    return $"$\"/path/to/item{{{index}}}\"";
                else if (prop.Name.Contains("Key"))
                    return $"$\"key{{{index}}}\"";
                else
                    return $"$\"Item {{{index}}}\"";
            }
            else if (typeName == "int" || underlyingType == "int" ||
                     typeName == "long" || underlyingType == "long")
            {
                return $"{randomVarName}.Next(1, 1000)" + (typeName == "long" || underlyingType == "long" ? " * 100L" : "");
            }
            else if (typeName == "double" || underlyingType == "double" ||
                     typeName == "float" || underlyingType == "float" ||
                     typeName == "decimal" || underlyingType == "decimal")
            {
                if (prop.Name.Contains("Percent"))
                    return $"Math.Round({randomVarName}.NextDouble() * 100, 2)";
                else
                    return $"Math.Round({randomVarName}.NextDouble() * 1000, 2)";
            }
            else if (typeName == "bool" || underlyingType == "bool")
            {
                return $"{randomVarName}.Next(2) == 0";
            }
            else if (typeName == "DateTime" || underlyingType == "DateTime")
            {
                return $"DateTime.Now.AddDays(-{randomVarName}.Next(365))";
            }
            else if (typeName == "Guid" || underlyingType == "Guid")
            {
                return "Guid.NewGuid()";
            }
            else if (prop.IsEnum)
            {
                string enumTypeName = typeName;

                // 内部クラス/ネストされた列挙型の場合、正しい型名を生成
                if (prop.Symbol != null && prop.Symbol.Type != null)
                {
                    enumTypeName = prop.Symbol.Type.ToDisplayString();
                }

                // クラス内に定義されたEnum型の場合、クラス名.Enum名の形式にする
                if (prop.Name == "Status" && prop.TypeName == "VideoStatus")
                {
                    enumTypeName = "VideoStatus";
                }

                return $"({enumTypeName}){randomVarName}.Next(Enum.GetValues(typeof({enumTypeName})).Length)";
            }

            // サポートされていない型や判断できない型は値を生成しない
            if (isNullable)
            {
                return "null";
            }

            return string.Empty;
        }

        /// <summary>
        /// CSVデータを生成
        /// </summary>
        private string GenerateCsvData(List<string> headers, int count)
        {
            var sb = new StringBuilder();

            // ヘッダー行を追加
            sb.AppendLine(string.Join(",", headers));

            // データ行を生成
            for (int i = 0; i < count; i++)
            {
                var values = new List<string>();

                foreach (var header in headers)
                {
                    string value = GetRandomCsvValue(header);
                    values.Add(value);
                }

                sb.AppendLine(string.Join(",", values));
            }

            return sb.ToString();
        }

        /// <summary>
        /// SQL INSERT文を生成
        /// </summary>
        private string GenerateSqlInserts(string tableName, List<(string columnName, string dataType)> columns, int count)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"-- テストデータ挿入 ({count}件)");
            sb.AppendLine($"-- 生成日時: {DateTime.Now}");
            sb.AppendLine();

            // カラム名部分
            string columnList = string.Join(", ", columns.Select(c => c.columnName));

            for (int i = 0; i < count; i++)
            {
                // 値のリストを構築
                var values = new List<string>();

                foreach (var (columnName, dataType) in columns)
                {
                    string value = GetRandomSqlValue(dataType, columnName);
                    values.Add(value);
                }

                string valueList = string.Join(", ", values);

                sb.AppendLine($"INSERT INTO {tableName} ({columnList}) VALUES ({valueList});");
            }

            return sb.ToString();
        }

        /// <summary>
        /// XMLデータを生成
        /// </summary>
        private string GenerateXmlData(string rootElement, string nodeElement, List<string> attributes, int count)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<{rootElement}>");

            for (int i = 0; i < count; i++)
            {
                sb.Append($"  <{nodeElement}");

                foreach (var attr in attributes)
                {
                    string value = GetRandomXmlValue(attr);
                    sb.Append($" {attr}=\"{value}\"");
                }

                sb.AppendLine("/>");
            }

            sb.AppendLine($"</{rootElement}>");

            return sb.ToString();
        }

        #endregion

        #region 型に応じたランダム値生成メソッド

        /// <summary>
        /// C#の型に応じたランダム値を取得
        /// </summary>
        private string GetRandomValueForType(string typeName, string propertyName)
        {
            // プロパティ名に基づいたより適切な値の生成を試みる
            var propertyNameLower = propertyName.ToLowerInvariant();

            // ID系のプロパティは連番
            if (propertyNameLower == "id" || propertyNameLower.EndsWith("id"))
            {
                return "i + 1";
            }

            // 日付系のプロパティ
            if (propertyNameLower.Contains("date") || propertyNameLower.Contains("time"))
            {
                if (typeName == "DateTime" || typeName == "System.DateTime")
                {
                    return "DateTime.Now.AddDays(random.Next(-30, 30))";
                }
                if (typeName == "DateTimeOffset" || typeName == "System.DateTimeOffset")
                {
                    return "DateTimeOffset.Now.AddDays(random.Next(-30, 30))";
                }
            }

            // 名前系のプロパティ
            if (propertyNameLower.Contains("name") || propertyNameLower.Contains("title"))
            {
                if (typeName == "string" || typeName == "System.String")
                {
                    return propertyNameLower.Contains("first") ? "\"First\" + i" :
                           propertyNameLower.Contains("last") ? "\"Last\" + i" :
                           "\"Name\" + i";
                }
            }

            // 一般的な型に基づいてデフォルト値を生成
            switch (typeName)
            {
                case "int":
                case "Int32":
                case "System.Int32":
                    return "random.Next(1, 100)";

                case "long":
                case "Int64":
                case "System.Int64":
                    return "random.Next(1, 1000) * 100L";

                case "decimal":
                case "System.Decimal":
                    return "Math.Round(random.Next(1, 10000) / 100.0m, 2)";

                case "double":
                case "System.Double":
                    return "Math.Round(random.NextDouble() * 100, 2)";

                case "float":
                case "System.Single":
                    return "(float)Math.Round(random.NextDouble() * 100, 2)";

                case "bool":
                case "Boolean":
                case "System.Boolean":
                    return "random.Next(2) == 0";

                case "string":
                case "String":
                case "System.String":
                    if (propertyNameLower.Contains("email"))
                    {
                        return "$\"user{i}@example.com\"";
                    }
                    if (propertyNameLower.Contains("phone"))
                    {
                        return "$\"555-{random.Next(1000, 9999)}\"";
                    }
                    if (propertyNameLower.Contains("address"))
                    {
                        return "$\"Address {i}, Street {random.Next(1, 100)}\"";
                    }
                    // その他の一般的な文字列
                    return "$\"Item {i}\"";

                case "DateTime":
                case "System.DateTime":
                    return "DateTime.Now.AddDays(random.Next(-30, 30))";

                case "DateTimeOffset":
                case "System.DateTimeOffset":
                    return "DateTimeOffset.Now.AddDays(random.Next(-30, 30))";

                case "Guid":
                case "System.Guid":
                    return "Guid.NewGuid()";

                default:
                    // サポートされていない型や複雑な型はnullを返す
                    return "null";
            }
        }

        /// <summary>
        /// JSONのプロパティに対応するランダム値を取得
        /// </summary>
        private string GetRandomJsonValue(string property)
        {
            var propertyLower = property.ToLowerInvariant();

            // ID系のプロパティ
            if (propertyLower == "id" || propertyLower.EndsWith("id"))
            {
                return _random.Next(1, 1000).ToString();
            }

            // 日付系のプロパティ
            if (propertyLower.Contains("date") || propertyLower.Contains("time"))
            {
                var date = DateTime.Now.AddDays(_random.Next(-30, 30));
                return $"\"{date:yyyy-MM-dd}\"";
            }

            // 名前系のプロパティ
            if (propertyLower.Contains("name") || propertyLower.Contains("title"))
            {
                return propertyLower.Contains("first") ? $"\"First{_random.Next(1, 100)}\"" :
                       propertyLower.Contains("last") ? $"\"Last{_random.Next(1, 100)}\"" :
                       $"\"Name{_random.Next(1, 100)}\"";
            }

            // その他の一般的なプロパティタイプを推測
            if (propertyLower.Contains("count") || propertyLower.Contains("number"))
            {
                return _random.Next(1, 100).ToString();
            }

            if (propertyLower.Contains("price") || propertyLower.Contains("amount"))
            {
                return Math.Round(_random.NextDouble() * 100, 2).ToString();
            }

            if (propertyLower.Contains("active") || propertyLower.Contains("enabled"))
            {
                return _random.Next(2) == 0 ? "true" : "false";
            }

            if (propertyLower.Contains("email"))
            {
                return $"\"user{_random.Next(1, 100)}@example.com\"";
            }

            if (propertyLower.Contains("phone"))
            {
                return $"\"555-{_random.Next(1000, 9999)}\"";
            }

            if (propertyLower.Contains("address"))
            {
                return $"\"Address {_random.Next(1, 100)}, Street {_random.Next(1, 100)}\"";
            }

            // デフォルトは文字列として扱う
            return $"\"Value {_random.Next(1, 100)}\"";
        }

        /// <summary>
        /// CSVのヘッダーに対応するランダム値を取得
        /// </summary>
        private string GetRandomCsvValue(string header)
        {
            var headerLower = header.ToLowerInvariant();

            // ID系のプロパティ
            if (headerLower == "id" || headerLower.EndsWith("id"))
            {
                return _random.Next(1, 1000).ToString();
            }

            // 日付系のプロパティ
            if (headerLower.Contains("date") || headerLower.Contains("time"))
            {
                var date = DateTime.Now.AddDays(_random.Next(-30, 30));
                return $"{date:yyyy-MM-dd}";
            }

            // 名前系のプロパティ
            if (headerLower.Contains("name") || headerLower.Contains("title"))
            {
                string value = headerLower.Contains("first") ? $"First{_random.Next(1, 100)}" :
                              headerLower.Contains("last") ? $"Last{_random.Next(1, 100)}" :
                              $"Name{_random.Next(1, 100)}";

                // CSVでは値にカンマが含まれる可能性があるためダブルクォートで囲む
                return $"\"{value}\"";
            }

            // その他の一般的なプロパティタイプを推測
            if (headerLower.Contains("count") || headerLower.Contains("number"))
            {
                return _random.Next(1, 100).ToString();
            }

            if (headerLower.Contains("price") || headerLower.Contains("amount"))
            {
                return Math.Round(_random.NextDouble() * 100, 2).ToString();
            }

            if (headerLower.Contains("active") || headerLower.Contains("enabled"))
            {
                return _random.Next(2) == 0 ? "true" : "false";
            }

            if (headerLower.Contains("email"))
            {
                return $"user{_random.Next(1, 100)}@example.com";
            }

            if (headerLower.Contains("phone"))
            {
                return $"555-{_random.Next(1000, 9999)}";
            }

            if (headerLower.Contains("address"))
            {
                string value = $"Address {_random.Next(1, 100)}, Street {_random.Next(1, 100)}";
                return $"\"{value}\"";
            }

            // デフォルトは文字列として扱う
            return $"Value {_random.Next(1, 100)}";
        }

        /// <summary>
        /// SQLデータ型に対応するランダム値を取得
        /// </summary>
        private string GetRandomSqlValue(string dataType, string columnName)
        {
            var columnLower = columnName.ToLowerInvariant();
            var dataTypeLower = dataType.ToLowerInvariant();

            // ID系のカラム
            if (columnLower == "id" || columnLower.EndsWith("id"))
            {
                return _random.Next(1, 1000).ToString();
            }

            // 値を生成するためにデータ型を使用
            if (dataTypeLower.Contains("int") || dataTypeLower.Contains("decimal") ||
                dataTypeLower.Contains("float") || dataTypeLower.Contains("double") ||
                dataTypeLower.Contains("number"))
            {
                // 小数系の型
                if (dataTypeLower.Contains("decimal") || dataTypeLower.Contains("float") ||
                    dataTypeLower.Contains("double") || dataTypeLower.Contains("real"))
                {
                    return Math.Round(_random.NextDouble() * 100, 2).ToString();
                }

                // 整数系の型
                return _random.Next(1, 1000).ToString();
            }

            // 日付系のカラム
            if (dataTypeLower.Contains("date") || dataTypeLower.Contains("time"))
            {
                var date = DateTime.Now.AddDays(_random.Next(-30, 30));
                return $"'{date:yyyy-MM-dd}'";
            }

            // 文字列系の型
            if (dataTypeLower.Contains("char") || dataTypeLower.Contains("text") ||
                dataTypeLower.Contains("varchar") || dataTypeLower.Contains("nvarchar"))
            {
                // カラム名に基づいて適切な値を生成
                if (columnLower.Contains("name") || columnLower.Contains("title"))
                {
                    return columnLower.Contains("first") ? $"'First{_random.Next(1, 100)}'" :
                           columnLower.Contains("last") ? $"'Last{_random.Next(1, 100)}'" :
                           $"'Name{_random.Next(1, 100)}'";
                }

                if (columnLower.Contains("email"))
                {
                    return $"'user{_random.Next(1, 100)}@example.com'";
                }

                if (columnLower.Contains("phone"))
                {
                    return $"'555-{_random.Next(1000, 9999)}'";
                }

                if (columnLower.Contains("address"))
                {
                    return $"'Address {_random.Next(1, 100)}, Street {_random.Next(1, 100)}'";
                }

                // ブール値
                if (columnLower.Contains("active") || columnLower.Contains("enabled") ||
                    columnLower.Contains("flag") || dataTypeLower.Contains("bool"))
                {
                    return _random.Next(2) == 0 ? "1" : "0";
                }

                // デフォルトは文字列として扱う
                return $"'Value {_random.Next(1, 100)}'";
            }

            // ブール値系の型
            if (dataTypeLower.Contains("bool") || dataTypeLower.Contains("bit"))
            {
                return _random.Next(2) == 0 ? "1" : "0";
            }

            // GUID/UUID
            if (dataTypeLower.Contains("uniqueidentifier") || dataTypeLower.Contains("uuid"))
            {
                return $"'{Guid.NewGuid()}'";
            }

            // サポートされていない型は NULL を返す
            return "NULL";
        }

        /// <summary>
        /// XML属性に対応するランダム値を取得
        /// </summary>
        private string GetRandomXmlValue(string attribute)
        {
            var attributeLower = attribute.ToLowerInvariant();

            // ID系の属性
            if (attributeLower == "id" || attributeLower.EndsWith("id"))
            {
                return _random.Next(1, 1000).ToString();
            }

            // 日付系の属性
            if (attributeLower.Contains("date") || attributeLower.Contains("time"))
            {
                var date = DateTime.Now.AddDays(_random.Next(-30, 30));
                return date.ToString("yyyy-MM-dd");
            }

            // 名前系の属性
            if (attributeLower.Contains("name") || attributeLower.Contains("title"))
            {
                return attributeLower.Contains("first") ? $"First{_random.Next(1, 100)}" :
                       attributeLower.Contains("last") ? $"Last{_random.Next(1, 100)}" :
                       $"Name{_random.Next(1, 100)}";
            }

            // その他の一般的な属性タイプを推測
            if (attributeLower.Contains("count") || attributeLower.Contains("number"))
            {
                return _random.Next(1, 100).ToString();
            }

            if (attributeLower.Contains("price") || attributeLower.Contains("amount"))
            {
                return Math.Round(_random.NextDouble() * 100, 2).ToString();
            }

            if (attributeLower.Contains("active") || attributeLower.Contains("enabled"))
            {
                return _random.Next(2) == 0 ? "true" : "false";
            }

            if (attributeLower.Contains("email"))
            {
                return $"user{_random.Next(1, 100)}@example.com";
            }

            if (attributeLower.Contains("phone"))
            {
                return $"555-{_random.Next(1000, 9999)}";
            }

            if (attributeLower.Contains("address"))
            {
                return $"Address {_random.Next(1, 100)}, Street {_random.Next(1, 100)}";
            }

            // デフォルトは文字列として扱う
            return $"Value {_random.Next(1, 100)}";
        }

        #endregion
    }
}