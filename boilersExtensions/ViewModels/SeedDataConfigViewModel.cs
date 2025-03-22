using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using boilersExtensions.Utils;
using EnvDTE;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Project = Microsoft.CodeAnalysis.Project;
using Solution = Microsoft.CodeAnalysis.Solution;

namespace boilersExtensions.ViewModels
{
    /// <summary>
    /// シードデータ設定ダイアログのViewModel
    /// </summary>
    public class SeedDataConfigViewModel : ViewModelBase
    {
        #region プロパティ

        // 対象ファイル情報
        public ReactivePropertySlim<string> TargetFileName { get; } = new ReactivePropertySlim<string>();
        public ReactivePropertySlim<string> TargetType { get; } = new ReactivePropertySlim<string>();

        // 生成データ設定
        public ReactivePropertySlim<int> DataCount { get; } = new ReactivePropertySlim<int>(10);

        // プロパティ一覧
        public ObservableCollection<PropertyViewModel> Properties { get; } = new ObservableCollection<PropertyViewModel>();
        public ReactivePropertySlim<PropertyViewModel> SelectedProperty { get; } = new ReactivePropertySlim<PropertyViewModel>();

        // データ形式関連
        public List<string> DataFormats { get; } = new List<string>
        {
            "C#クラス",
            "JSON配列",
            "CSV形式",
            "SQL INSERT文",
            "XML形式"
        };
        public ReactivePropertySlim<string> SelectedDataFormat { get; } = new ReactivePropertySlim<string>("C#クラス");

        // 各形式の設定
        public ReactivePropertySlim<bool> IsCSharpFormat { get; } = new ReactivePropertySlim<bool>(true);
        public ReactivePropertySlim<bool> IsJsonFormat { get; } = new ReactivePropertySlim<bool>(false);
        public ReactivePropertySlim<bool> IsCsvFormat { get; } = new ReactivePropertySlim<bool>(false);
        public ReactivePropertySlim<bool> IsSqlFormat { get; } = new ReactivePropertySlim<bool>(false);
        public ReactivePropertySlim<bool> IsXmlFormat { get; } = new ReactivePropertySlim<bool>(false);

        // C#形式設定
        public ReactivePropertySlim<string> ClassName { get; } = new ReactivePropertySlim<string>("TestData");
        public ReactivePropertySlim<bool> UsePropertyInitializer { get; } = new ReactivePropertySlim<bool>(true);
        public ReactivePropertySlim<bool> IsStaticMethod { get; } = new ReactivePropertySlim<bool>(true);

        // SQL形式設定
        public ReactivePropertySlim<string> TableName { get; } = new ReactivePropertySlim<string>("TestTable");
        public ReactivePropertySlim<bool> IncludeTransaction { get; } = new ReactivePropertySlim<bool>(true);

        // XML形式設定
        public ReactivePropertySlim<string> RootElementName { get; } = new ReactivePropertySlim<string>("TestData");
        public ReactivePropertySlim<string> ItemElementName { get; } = new ReactivePropertySlim<string>("Item");

        // 詳細設定
        public ReactivePropertySlim<bool> UseRandomValues { get; } = new ReactivePropertySlim<bool>(true);
        public ReactivePropertySlim<bool> AutoGenerateIds { get; } = new ReactivePropertySlim<bool>(true);
        public ReactivePropertySlim<bool> IncludeNullValues { get; } = new ReactivePropertySlim<bool>(false);
        public ReactivePropertySlim<DateTime?> StartDate { get; } = new ReactivePropertySlim<DateTime?>(DateTime.Now.AddMonths(-1));
        public ReactivePropertySlim<DateTime?> EndDate { get; } = new ReactivePropertySlim<DateTime?>(DateTime.Now.AddMonths(1));
        public ReactivePropertySlim<string> MinNumericValue { get; } = new ReactivePropertySlim<string>("1");
        public ReactivePropertySlim<string> MaxNumericValue { get; } = new ReactivePropertySlim<string>("100");

        // プレビュー
        public ReactivePropertySlim<string> PreviewText { get; } = new ReactivePropertySlim<string>();

        // 使用可能なデータタイプ
        public List<string> DataTypes { get; } = new List<string>
        {
            "標準",
            "ID/連番",
            "名前",
            "Email",
            "電話番号",
            "住所",
            "日付",
            "ブール値",
            "GUID",
            "価格/金額",
            "カスタム"
        };

        // 対象ドキュメント
        private EnvDTE.Document _targetDocument;

        #endregion

        #region コマンド

        // コマンド
        public ReactiveCommand AddPropertyCommand { get; }
        public ReactiveCommand RemovePropertyCommand { get; }
        public ReactiveCommand MoveUpPropertyCommand { get; }
        public ReactiveCommand MoveDownPropertyCommand { get; }
        public ReactiveCommand LoadSchemaCommand { get; }
        public ReactiveCommand GenerateSeedDataCommand { get; }
        public ReactiveCommand CancelCommand { get; }

        #endregion

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public SeedDataConfigViewModel()
        {
            // プロパティの初期化
            Properties.Clear();

            // データ形式切り替え時の処理
            SelectedDataFormat.Subscribe(format =>
            {
                IsCSharpFormat.Value = format == "C#クラス";
                IsJsonFormat.Value = format == "JSON配列";
                IsCsvFormat.Value = format == "CSV形式";
                IsSqlFormat.Value = format == "SQL INSERT文";
                IsXmlFormat.Value = format == "XML形式";

                // プレビュー更新
                UpdatePreview();
            }).AddTo(Disposables);

            // プロパティ変更時のプレビュー更新
            DataCount.Subscribe(_ => UpdatePreview()).AddTo(Disposables);
            ClassName.Subscribe(_ => UpdatePreview()).AddTo(Disposables);
            TableName.Subscribe(_ => UpdatePreview()).AddTo(Disposables);
            RootElementName.Subscribe(_ => UpdatePreview()).AddTo(Disposables);
            ItemElementName.Subscribe(_ => UpdatePreview()).AddTo(Disposables);

            // コマンドの実装
            AddPropertyCommand = new ReactiveCommand()
                .WithSubscribe(AddProperty)
                .AddTo(Disposables);

            RemovePropertyCommand = new ReactiveCommand()
                .WithSubscribe(RemoveProperty)
                .AddTo(Disposables);

            MoveUpPropertyCommand = new ReactiveCommand()
                .WithSubscribe(MoveUpProperty)
                .AddTo(Disposables);

            MoveDownPropertyCommand = new ReactiveCommand()
                .WithSubscribe(MoveDownProperty)
                .AddTo(Disposables);

            LoadSchemaCommand = new ReactiveCommand()
                .WithSubscribe(LoadSchema)
                .AddTo(Disposables);

            GenerateSeedDataCommand = new ReactiveCommand()
                .WithSubscribe(GenerateSeedData)
                .AddTo(Disposables);

            CancelCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    var window = Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.DataContext == this);
                    window?.Close();
                })
                .AddTo(Disposables);
        }

        /// <summary>
        /// 対象ドキュメントを設定
        /// </summary>
        public void SetTargetDocument(EnvDTE.Document document, string documentType)
        {
            _targetDocument = document;
            TargetFileName.Value = Path.GetFileName(document.FullName);
            TargetType.Value = documentType;

            // 文書の種類に応じてデータ形式のデフォルト値を設定
            switch (documentType.ToLowerInvariant())
            {
                case "csharp":
                    SelectedDataFormat.Value = "C#クラス";
                    break;
                case "json":
                    SelectedDataFormat.Value = "JSON配列";
                    break;
                case "csv":
                    SelectedDataFormat.Value = "CSV形式";
                    break;
                case "sql":
                    SelectedDataFormat.Value = "SQL INSERT文";
                    break;
                case "xml":
                    SelectedDataFormat.Value = "XML形式";
                    break;
                default:
                    SelectedDataFormat.Value = "C#クラス";
                    break;
            }

            // ファイルの内容からスキーマ情報を自動読み込み
            LoadSchema();
        }

        /// <summary>
        /// ダイアログが開かれた時の処理
        /// </summary>
        public override void OnDialogOpened(object dialog)
        {
            base.OnDialogOpened(dialog);

            // 初期プレビュー更新
            UpdatePreview();
        }

        /// <summary>
        /// プロパティを追加
        /// </summary>
        private void AddProperty()
        {
            var property = new PropertyViewModel
            {
                Name = { Value = $"Property{Properties.Count + 1}" },
                Type = { Value = "string" },
                DataType = { Value = "標準" }
            };

            Properties.Add(property);
            SelectedProperty.Value = property;

            // プレビュー更新
            UpdatePreview();
        }

        /// <summary>
        /// 選択されたプロパティを削除
        /// </summary>
        private void RemoveProperty()
        {
            if (SelectedProperty.Value != null)
            {
                Properties.Remove(SelectedProperty.Value);
                SelectedProperty.Value = Properties.FirstOrDefault();

                // プレビュー更新
                UpdatePreview();
            }
        }

        /// <summary>
        /// 選択されたプロパティを上に移動
        /// </summary>
        private void MoveUpProperty()
        {
            if (SelectedProperty.Value != null)
            {
                int index = Properties.IndexOf(SelectedProperty.Value);
                if (index > 0)
                {
                    Properties.Move(index, index - 1);

                    // プレビュー更新
                    UpdatePreview();
                }
            }
        }

        /// <summary>
        /// 選択されたプロパティを下に移動
        /// </summary>
        private void MoveDownProperty()
        {
            if (SelectedProperty.Value != null)
            {
                int index = Properties.IndexOf(SelectedProperty.Value);
                if (index < Properties.Count - 1)
                {
                    Properties.Move(index, index + 1);

                    // プレビュー更新
                    UpdatePreview();
                }
            }
        }

        /// <summary>
        /// ファイルからスキーマ情報を読み込み
        /// </summary>
        private void LoadSchema()
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    SetProcessing(true, "スキーマを解析中...");

                    // ファイルの種類に応じたスキーマ解析
                    switch (TargetType.Value.ToLowerInvariant())
                    {
                        case "csharp":
                            await LoadCSharpSchema();
                            break;
                        case "json":
                            await LoadJsonSchema();
                            break;
                        case "csv":
                            await LoadCsvSchema();
                            break;
                        case "sql":
                            await LoadSqlSchema();
                            break;
                        case "xml":
                            await LoadXmlSchema();
                            break;
                    }

                    // プレビュー更新
                    UpdatePreview();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading schema: {ex.Message}");
                    Debug.WriteLine(ex.StackTrace);

                    // エラーメッセージを表示
                    MessageBox.Show(
                        $"スキーマ読み込み中にエラーが発生しました: {ex.Message}",
                        "エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    SetProcessing(false);
                }
            });
        }

        /// <summary>
        /// C#ファイルのスキーマを読み込み
        /// </summary>
        private async Task LoadCSharpSchema()
        {
            // C#コードの解析は複雑なため、簡易実装
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // TextDocumentからテキストを取得
            var textDocument = _targetDocument.Object("TextDocument") as EnvDTE.TextDocument;
            if (textDocument == null) return;

            var editPoint = textDocument.StartPoint.CreateEditPoint();
            var documentText = editPoint.GetText(textDocument.EndPoint);

            // 簡易的なクラス名の取得（実際にはRoslynなどでより正確に解析する）
            var classNameMatch = System.Text.RegularExpressions.Regex.Match(documentText, @"class\s+(\w+)");
            if (classNameMatch.Success)
            {
                ClassName.Value = classNameMatch.Groups[1].Value;
            }

            // 簡易的なプロパティ取得（単純な実装なのでリアルな環境では不十分）
            var propertyMatches = System.Text.RegularExpressions.Regex.Matches(
                documentText,
                @"public\s+(\w+(?:<\w+>)?)\s+(\w+)\s*\{");

            // プロパティリストをクリアして新しいプロパティを追加
            Properties.Clear();

            foreach (System.Text.RegularExpressions.Match match in propertyMatches)
            {
                var type = match.Groups[1].Value;
                var name = match.Groups[2].Value;

                var property = new PropertyViewModel
                {
                    Name = { Value = name },
                    Type = { Value = type },
                    DataType = { Value = DetermineDataType(name, type) }
                };

                Properties.Add(property);
            }
        }

        /// <summary>
        /// JSONファイルのスキーマを読み込み
        /// </summary>
        private async Task LoadJsonSchema()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // TextDocumentからテキストを取得
            var textDocument = _targetDocument.Object("TextDocument") as EnvDTE.TextDocument;
            if (textDocument == null) return;

            var editPoint = textDocument.StartPoint.CreateEditPoint();
            var jsonText = editPoint.GetText(textDocument.EndPoint);

            try
            {
                // 簡易的なJSON解析（実際にはより堅牢な実装が必要）
                if (jsonText.Trim().StartsWith("[") && jsonText.Trim().Contains("{"))
                {
                    // JSONの配列構造からプロパティを抽出
                    var firstObject = jsonText.Substring(jsonText.IndexOf('{') + 1);
                    if (firstObject.Contains("}"))
                    {
                        firstObject = firstObject.Substring(0, firstObject.IndexOf('}'));
                    }

                    // プロパティを分割（簡易実装）
                    var properties = firstObject.Split(',')
                        .Select(p => p.Trim())
                        .Where(p => p.Contains(":"))
                        .ToList();

                    // プロパティリストをクリア
                    Properties.Clear();

                    foreach (var prop in properties)
                    {
                        var parts = prop.Split(new[] { ':' }, 2);
                        if (parts.Length == 2)
                        {
                            // プロパティ名の取得（引用符を除去）
                            var name = parts[0].Trim();
                            if (name.StartsWith("\"") && name.EndsWith("\""))
                            {
                                name = name.Substring(1, name.Length - 2);
                            }

                            // プロパティの値から型を推測
                            var value = parts[1].Trim();
                            string type = "string";

                            if (value.StartsWith("\"") && value.EndsWith("\""))
                            {
                                type = "string";
                            }
                            else if (value == "true" || value == "false")
                            {
                                type = "bool";
                            }
                            else if (int.TryParse(value, out _))
                            {
                                type = "int";
                            }
                            else if (double.TryParse(value, out _))
                            {
                                type = "double";
                            }

                            var property = new PropertyViewModel
                            {
                                Name = { Value = name },
                                Type = { Value = type },
                                DataType = { Value = DetermineDataType(name, type) }
                            };

                            Properties.Add(property);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing JSON: {ex.Message}");
            }
        }

        /// <summary>
        /// CSVファイルのスキーマを読み込み
        /// </summary>
        private async Task LoadCsvSchema()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // TextDocumentからテキストを取得
            var textDocument = _targetDocument.Object("TextDocument") as EnvDTE.TextDocument;
            if (textDocument == null) return;

            var editPoint = textDocument.StartPoint.CreateEditPoint();
            var csvText = editPoint.GetText(textDocument.EndPoint);

            try
            {
                // CSVの1行目をヘッダーとして解析
                var lines = csvText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                {
                    var headers = lines[0].Split(',');

                    // プロパティリストをクリア
                    Properties.Clear();

                    foreach (var header in headers)
                    {
                        var name = header.Trim();
                        if (name.StartsWith("\"") && name.EndsWith("\""))
                        {
                            name = name.Substring(1, name.Length - 2);
                        }

                        // 2行目があればデータ型を推測、なければstring
                        string type = "string";
                        if (lines.Length > 1)
                        {
                            var values = lines[1].Split(',');
                            int index = Array.IndexOf(headers, header);
                            if (index >= 0 && index < values.Length)
                            {
                                var value = values[index].Trim();
                                if (value.StartsWith("\"") && value.EndsWith("\""))
                                {
                                    type = "string";
                                }
                                else if (int.TryParse(value, out _))
                                {
                                    type = "int";
                                }
                                else if (double.TryParse(value, out _))
                                {
                                    type = "double";
                                }
                            }
                        }

                        var property = new PropertyViewModel
                        {
                            Name = { Value = name },
                            Type = { Value = type },
                            DataType = { Value = DetermineDataType(name, type) }
                        };

                        Properties.Add(property);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing CSV: {ex.Message}");
            }
        }

        /// <summary>
        /// SQLファイルのスキーマを読み込み
        /// </summary>
        private async Task LoadSqlSchema()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // TextDocumentからテキストを取得
            var textDocument = _targetDocument.Object("TextDocument") as EnvDTE.TextDocument;
            if (textDocument == null) return;

            var editPoint = textDocument.StartPoint.CreateEditPoint();
            var sqlText = editPoint.GetText(textDocument.EndPoint);

            try
            {
                // CREATE TABLE文からスキーマを解析（簡易実装）
                var createTableMatch = System.Text.RegularExpressions.Regex.Match(
                    sqlText,
                    @"CREATE\s+TABLE\s+(\w+)\s*\((.*?)\)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

                if (createTableMatch.Success)
                {
                    // テーブル名を取得
                    TableName.Value = createTableMatch.Groups[1].Value;

                    // カラム定義を解析
                    string columnDefs = createTableMatch.Groups[2].Value;
                    var columns = columnDefs.Split(',')
                        .Select(c => c.Trim())
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .ToList();

                    // プロパティリストをクリア
                    Properties.Clear();

                    foreach (var column in columns)
                    {
                        // プライマリーキーや制約行はスキップ
                        if (column.StartsWith("PRIMARY KEY") ||
                            column.StartsWith("FOREIGN KEY") ||
                            column.StartsWith("CONSTRAINT"))
                        {
                            continue;
                        }

                        // カラム名と型を抽出
                        var parts = column.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            string name = parts[0];
                            string sqlType = parts[1];

                            // SQL型をC#型に変換
                            string csharpType;
                            switch (sqlType.ToUpperInvariant())
                            {
                                case "INT":
                                case "INTEGER":
                                case "SMALLINT":
                                case "TINYINT":
                                    csharpType = "int";
                                    break;
                                case "BIGINT":
                                    csharpType = "long";
                                    break;
                                case "FLOAT":
                                case "REAL":
                                    csharpType = "float";
                                    break;
                                case "DECIMAL":
                                case "NUMERIC":
                                case "MONEY":
                                    csharpType = "decimal";
                                    break;
                                case "BIT":
                                    csharpType = "bool";
                                    break;
                                case "DATE":
                                case "DATETIME":
                                case "TIMESTAMP":
                                    csharpType = "DateTime";
                                    break;
                                case "UNIQUEIDENTIFIER":
                                    csharpType = "Guid";
                                    break;
                                default:
                                    csharpType = "string";
                                    break;
                            }

                            var property = new PropertyViewModel
                            {
                                Name = { Value = name },
                                Type = { Value = csharpType },
                                DataType = { Value = DetermineDataType(name, csharpType) }
                            };

                            Properties.Add(property);
                        }
                    }
                }
                else
                {
                    // INSERT文からスキーマを推測（簡易実装）
                    var insertMatch = System.Text.RegularExpressions.Regex.Match(
                        sqlText,
                        @"INSERT\s+INTO\s+(\w+)\s*\((.*?)\)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    if (insertMatch.Success)
                    {
                        // テーブル名を取得
                        TableName.Value = insertMatch.Groups[1].Value;

                        // カラム名を抽出
                        string columnNames = insertMatch.Groups[2].Value;
                        var columns = columnNames.Split(',')
                            .Select(c => c.Trim())
                            .Where(c => !string.IsNullOrWhiteSpace(c))
                            .ToList();

                        // プロパティリストをクリア
                        Properties.Clear();

                        foreach (var column in columns)
                        {
                            var property = new PropertyViewModel
                            {
                                Name = { Value = column },
                                Type = { Value = "string" }, // 型が不明なのでstringをデフォルトに
                                DataType = { Value = DetermineDataType(column, "string") }
                            };

                            Properties.Add(property);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing SQL: {ex.Message}");
            }
        }

        /// <summary>
        /// XMLファイルのスキーマを読み込み
        /// </summary>
        private async Task LoadXmlSchema()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // TextDocumentからテキストを取得
            var textDocument = _targetDocument.Object("TextDocument") as EnvDTE.TextDocument;
            if (textDocument == null) return;

            var editPoint = textDocument.StartPoint.CreateEditPoint();
            var xmlText = editPoint.GetText(textDocument.EndPoint);

            try
            {
                // XMLの構造を簡易的に解析（実際にはXDocument等を使用すべき）

                // ルート要素名を取得
                var rootMatch = System.Text.RegularExpressions.Regex.Match(xmlText, @"<(\w+)[^>]*>");
                if (rootMatch.Success)
                {
                    RootElementName.Value = rootMatch.Groups[1].Value;

                    // 最初の子要素名を取得
                    var childMatch = System.Text.RegularExpressions.Regex.Match(
                        xmlText.Substring(rootMatch.Index + rootMatch.Length),
                        @"<(\w+)[^>]*>");

                    if (childMatch.Success)
                    {
                        ItemElementName.Value = childMatch.Groups[1].Value;

                        // 子要素の属性を取得
                        var attributesMatch = System.Text.RegularExpressions.Regex.Match(
                            childMatch.Value,
                            @"<\w+\s+(.*?)(?:>|/>)");

                        if (attributesMatch.Success)
                        {
                            string attributesText = attributesMatch.Groups[1].Value;
                            var attributes = System.Text.RegularExpressions.Regex.Matches(
                                attributesText,
                                @"(\w+)\s*=\s*[""']([^""']*)[""']");

                            // プロパティリストをクリア
                            Properties.Clear();

                            foreach (System.Text.RegularExpressions.Match attrMatch in attributes)
                            {
                                string name = attrMatch.Groups[1].Value;
                                string value = attrMatch.Groups[2].Value;

                                // 値から型を推測
                                string type;
                                if (int.TryParse(value, out _))
                                {
                                    type = "int";
                                }
                                else if (double.TryParse(value, out _))
                                {
                                    type = "double";
                                }
                                else if (value == "true" || value == "false")
                                {
                                    type = "bool";
                                }
                                else if (DateTime.TryParse(value, out _))
                                {
                                    type = "DateTime";
                                }
                                else
                                {
                                    type = "string";
                                }

                                var property = new PropertyViewModel
                                {
                                    Name = { Value = name },
                                    Type = { Value = type },
                                    DataType = { Value = DetermineDataType(name, type) }
                                };

                                Properties.Add(property);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing XML: {ex.Message}");
            }
        }

        /// <summary>
        /// 名前と型からデータタイプを推測
        /// </summary>
        private string DetermineDataType(string name, string type)
        {
            var nameLower = name.ToLowerInvariant();

            // ID系
            if (nameLower == "id" || nameLower.EndsWith("id"))
            {
                return "ID/連番";
            }

            // 名前系
            if (nameLower.Contains("name") || nameLower.Contains("title"))
            {
                return "名前";
            }

            // メール
            if (nameLower.Contains("email"))
            {
                return "Email";
            }

            // 電話番号
            if (nameLower.Contains("phone") || nameLower.Contains("tel"))
            {
                return "電話番号";
            }

            // 住所
            if (nameLower.Contains("address"))
            {
                return "住所";
            }

            // 日付
            if (nameLower.Contains("date") || nameLower.Contains("time") ||
                type.Contains("Date") || type.Contains("Time"))
            {
                return "日付";
            }

            // ブール値
            if (nameLower.Contains("enabled") || nameLower.Contains("active") ||
                nameLower.Contains("flag") || nameLower.Contains("is") ||
                type == "bool" || type == "Boolean")
            {
                return "ブール値";
            }

            // GUID
            if (nameLower.Contains("guid") || nameLower.Contains("uuid") ||
                type == "Guid" || type.Contains("Unique"))
            {
                return "GUID";
            }

            // 価格/金額
            if (nameLower.Contains("price") || nameLower.Contains("cost") ||
                nameLower.Contains("amount") || nameLower.Contains("fee") ||
                type == "decimal" || type == "money")
            {
                return "価格/金額";
            }

            // デフォルト
            return "標準";
        }

        /// <summary>
        /// シードデータを生成して挿入
        /// </summary>
        private void GenerateSeedData()
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    SetProcessing(true, "テストデータを生成中...");
                    UpdateProgress(0);

                    // プロパティリストが空の場合はエラー
                    if (Properties.Count == 0)
                    {
                        MessageBox.Show(
                            "プロパティが設定されていません。スキーマ読込または手動でプロパティを追加してください。",
                            "エラー",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    // データ件数が不正な場合は修正
                    if (DataCount.Value <= 0)
                    {
                        DataCount.Value = 10;
                    }

                    // SeedDataInsertExecutorを作成
                    var executor = new SeedDataInsertExecutor(Package);
                    bool result = false;

                    // 選択されたデータ形式に応じてデータ生成
                    UpdateProgress(20, "データを整形中...");

                    switch (SelectedDataFormat.Value)
                    {
                        case "C#クラス":
                            // アクティブなドキュメントをRoslynのDocumentに変換する
                            var document = await GetRoslynDocumentFromActiveDocumentAsync(_targetDocument);

                            // EntityAnalyzerを使用してC#クラスを解析
                            var analyzer = new Analyzers.EntityAnalyzer();
                            var entity = await analyzer.AnalyzeEntitiesAsync(document);

                            if (entity.All(x => x.Name != ClassName.Value))
                            {
                                MessageBox.Show(
                                    $"アクティブドキュメントから {ClassName.Value} が検出できませんでした。",
                                    "エラー",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                                break;
                            }

                            // 解析されたPropertyInfoを使用する
                            result = await executor.InsertSeedMethodToCSharpClassAsync(
                                _targetDocument,
                                ClassName.Value,
                                entity.First(x => x.Name == ClassName.Value).Properties,  // 完全なPropertyInfoリスト
                                DataCount.Value);

                            break;

                        case "JSON配列":
                            // プロパティ名のリストを作成
                            var jsonProperties = Properties.Select(p => p.Name.Value).ToList();

                            // JSONファイルにデータを挿入
                            result = await executor.InsertSeedDataToJsonFileAsync(
                                _targetDocument,
                                jsonProperties,
                                DataCount.Value);

                            break;

                        case "CSV形式":
                            // ヘッダー名のリストを作成
                            var headers = Properties.Select(p => p.Name.Value).ToList();

                            // CSVファイルにデータを挿入
                            result = await executor.InsertSeedDataToCsvFileAsync(
                                _targetDocument,
                                headers,
                                DataCount.Value);

                            break;

                        case "SQL INSERT文":
                            // カラム名と型のタプルリストを作成
                            var columns = Properties.Select(p =>
                                (p.Name.Value, p.Type.Value)
                            ).ToList();

                            // SQLファイルにINSERT文を挿入
                            result = await executor.InsertSeedDataToSqlFileAsync(
                                _targetDocument,
                                TableName.Value,
                                columns,
                                DataCount.Value);

                            break;

                        case "XML形式":
                            // 属性名のリストを作成
                            var attributes = Properties.Select(p => p.Name.Value).ToList();

                            // XMLファイルにデータを挿入
                            result = await executor.InsertSeedDataToXmlFileAsync(
                                _targetDocument,
                                RootElementName.Value,
                                ItemElementName.Value,
                                attributes,
                                DataCount.Value);

                            break;
                    }

                    UpdateProgress(100, "完了");

                    // 結果に応じたメッセージを表示
                    if (result)
                    {
                        MessageBox.Show(
                            $"{DataCount.Value}件のテストデータを生成しました。",
                            "完了",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        // ダイアログを閉じる
                        var window = Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.DataContext == this);
                        window?.Close();
                    }
                    else
                    {
                        MessageBox.Show(
                            "データ生成中にエラーが発生しました。",
                            "エラー",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error generating seed data: {ex.Message}");
                    Debug.WriteLine(ex.StackTrace);

                    // エラーメッセージを表示
                    MessageBox.Show(
                        $"テストデータ生成中にエラーが発生しました: {ex.Message}",
                        "エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    SetProcessing(false);
                }
            });
        }

        private async Task<Microsoft.CodeAnalysis.Document> GetRoslynDocumentFromActiveDocumentAsync(EnvDTE.Document activeDocument)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // ComponentModelサービスを取得
                var componentModel = (await Package.GetServiceAsync(typeof(SComponentModel))) as IComponentModel;
                var workspace = componentModel.GetService<VisualStudioWorkspace>();

                // アクティブなドキュメントのパスを取得
                string documentPath = activeDocument.FullName;

                // ソリューション内のすべてのプロジェクトからドキュメントを検索
                foreach (var _project in workspace.CurrentSolution.Projects)
                {
                    foreach (var document in _project.Documents)
                    {
                        if (string.Equals(document.FilePath, documentPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return document;
                        }
                    }
                }

                // ドキュメントが見つからない場合の処理
                // 可能であればAdHocWorkspaceを使用して新しいドキュメントを作成
                var textDocument = activeDocument.Object("TextDocument") as EnvDTE.TextDocument;
                var text = textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);

                var adhocWorkspace = new AdhocWorkspace();
                var projectInfo = ProjectInfo.Create(
                    ProjectId.CreateNewId(),
                    VersionStamp.Create(),
                    "TempProject",
                    "TempAssembly",
                    LanguageNames.CSharp);

                var project = adhocWorkspace.AddProject(projectInfo);

                var documentInfo = DocumentInfo.Create(
                    DocumentId.CreateNewId(project.Id),
                    Path.GetFileName(documentPath),
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create())),
                    filePath: documentPath);

                return adhocWorkspace.AddDocument(documentInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting Roslyn Document: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// プレビューテキストを更新
        /// </summary>
        private void UpdatePreview()
        {
            try
            {
                // プロパティリストが空の場合は何もしない
                if (Properties.Count == 0)
                {
                    PreviewText.Value = "プロパティが設定されていません。\nスキーマ読込または手動でプロパティを追加してください。";
                    return;
                }

                var sb = new StringBuilder();

                // データ形式に応じたプレビューを生成
                switch (SelectedDataFormat.Value)
                {
                    case "C#クラス":
                        GenerateCSharpPreview(sb);
                        break;

                    case "JSON配列":
                        GenerateJsonPreview(sb);
                        break;

                    case "CSV形式":
                        GenerateCsvPreview(sb);
                        break;

                    case "SQL INSERT文":
                        GenerateSqlPreview(sb);
                        break;

                    case "XML形式":
                        GenerateXmlPreview(sb);
                        break;
                }

                PreviewText.Value = sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating preview: {ex.Message}");
                PreviewText.Value = $"プレビュー生成中にエラーが発生しました: {ex.Message}";
            }
        }

        /// <summary>
        /// C#クラスのプレビューを生成
        /// </summary>
        private void GenerateCSharpPreview(StringBuilder sb)
        {
            sb.AppendLine($"// {DataCount.Value}件のテストデータを生成するメソッド");
            sb.AppendLine($"public {(IsStaticMethod.Value ? "static " : "")}List<{ClassName.Value}> GenerateSeedData()");
            sb.AppendLine("{");
            sb.AppendLine($"    var result = new List<{ClassName.Value}>();");
            sb.AppendLine("    var random = new Random();");
            sb.AppendLine();
            sb.AppendLine($"    for (int i = 0; i < {DataCount.Value}; i++)");
            sb.AppendLine("    {");

            if (UsePropertyInitializer.Value)
            {
                sb.AppendLine($"        var item = new {ClassName.Value}");
                sb.AppendLine("        {");

                for (int i = 0; i < Math.Min(5, Properties.Count); i++)
                {
                    var property = Properties[i];
                    string value = GetSampleValueForType(property.Type.Value, property.Name.Value, property.DataType.Value);

                    if (i < Properties.Count - 1)
                    {
                        sb.AppendLine($"            {property.Name.Value} = {value},");
                    }
                    else
                    {
                        sb.AppendLine($"            {property.Name.Value} = {value}");
                    }
                }

                if (Properties.Count > 5)
                {
                    sb.AppendLine("            // ... 他のプロパティ");
                }

                sb.AppendLine("        };");
            }
            else
            {
                sb.AppendLine($"        var item = new {ClassName.Value}();");

                for (int i = 0; i < Math.Min(5, Properties.Count); i++)
                {
                    var property = Properties[i];
                    string value = GetSampleValueForType(property.Type.Value, property.Name.Value, property.DataType.Value);
                    sb.AppendLine($"        item.{property.Name.Value} = {value};");
                }

                if (Properties.Count > 5)
                {
                    sb.AppendLine("        // ... 他のプロパティ");
                }
            }

            sb.AppendLine("        result.Add(item);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    return result;");
            sb.AppendLine("}");
        }

        /// <summary>
        /// JSON配列のプレビューを生成
        /// </summary>
        private void GenerateJsonPreview(StringBuilder sb)
        {
            sb.AppendLine("[");

            // 先頭の2件を表示
            for (int i = 0; i < Math.Min(2, DataCount.Value); i++)
            {
                sb.AppendLine("  {");

                for (int j = 0; j < Properties.Count; j++)
                {
                    var property = Properties[j];
                    string value = GetSampleJsonValue(property.Name.Value, property.Type.Value, property.DataType.Value);

                    if (j < Properties.Count - 1)
                    {
                        sb.AppendLine($"    \"{property.Name.Value}\": {value},");
                    }
                    else
                    {
                        sb.AppendLine($"    \"{property.Name.Value}\": {value}");
                    }
                }

                if (i < Math.Min(2, DataCount.Value) - 1)
                {
                    sb.AppendLine("  },");
                }
                else if (DataCount.Value > 2)
                {
                    sb.AppendLine("  },");
                }
                else
                {
                    sb.AppendLine("  }");
                }
            }

            // データ件数が2件を超える場合は省略記号を表示
            if (DataCount.Value > 2)
            {
                sb.AppendLine("  // ... 他のデータ");

                sb.AppendLine("  {");

                for (int j = 0; j < Properties.Count; j++)
                {
                    var property = Properties[j];
                    string value = GetSampleJsonValue(property.Name.Value, property.Type.Value, property.DataType.Value);

                    if (j < Properties.Count - 1)
                    {
                        sb.AppendLine($"    \"{property.Name.Value}\": {value},");
                    }
                    else
                    {
                        sb.AppendLine($"    \"{property.Name.Value}\": {value}");
                    }
                }

                sb.AppendLine("  }");
            }

            sb.AppendLine("]");
        }

        /// <summary>
        /// CSV形式のプレビューを生成
        /// </summary>
        private void GenerateCsvPreview(StringBuilder sb)
        {
            // ヘッダー行
            sb.AppendLine(string.Join(",", Properties.Select(p => p.Name.Value)));

            // データ行（最初の3件だけ表示）
            for (int i = 0; i < Math.Min(3, DataCount.Value); i++)
            {
                var values = new List<string>();

                foreach (var property in Properties)
                {
                    string value = GetSampleCsvValue(property.Name.Value, property.Type.Value, property.DataType.Value);
                    values.Add(value);
                }

                sb.AppendLine(string.Join(",", values));
            }

            // データ件数が3件を超える場合は省略記号を表示
            if (DataCount.Value > 3)
            {
                sb.AppendLine("// ... 他のデータ");
            }
        }

        /// <summary>
        /// SQL INSERT文のプレビューを生成
        /// </summary>
        private void GenerateSqlPreview(StringBuilder sb)
        {
            sb.AppendLine($"-- {DataCount.Value}件のテストデータ");
            sb.AppendLine($"-- 生成日時: {DateTime.Now}");
            sb.AppendLine();

            if (IncludeTransaction.Value)
            {
                sb.AppendLine("BEGIN TRANSACTION;");
                sb.AppendLine();
            }

            // カラム名の一覧
            string columns = string.Join(", ", Properties.Select(p => p.Name.Value));

            // 先頭の2件のみ表示
            for (int i = 0; i < Math.Min(2, DataCount.Value); i++)
            {
                var values = new List<string>();

                foreach (var property in Properties)
                {
                    string value = GetSampleSqlValue(property.Name.Value, property.Type.Value, property.DataType.Value);
                    values.Add(value);
                }

                string valueList = string.Join(", ", values);

                sb.AppendLine($"INSERT INTO {TableName.Value} ({columns}) VALUES ({valueList});");
            }

            // データ件数が2件を超える場合は省略記号を表示
            if (DataCount.Value > 2)
            {
                sb.AppendLine("-- ... 他のINSERT文");
            }

            if (IncludeTransaction.Value)
            {
                sb.AppendLine();
                sb.AppendLine("COMMIT;");
            }
        }

        /// <summary>
        /// XML形式のプレビューを生成
        /// </summary>
        private void GenerateXmlPreview(StringBuilder sb)
        {
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<{RootElementName.Value}>");

            // 先頭の3件のみ表示
            for (int i = 0; i < Math.Min(3, DataCount.Value); i++)
            {
                sb.Append($"  <{ItemElementName.Value}");

                foreach (var property in Properties)
                {
                    string value = GetSampleXmlValue(property.Name.Value, property.Type.Value, property.DataType.Value);
                    sb.Append($" {property.Name.Value}=\"{value}\"");
                }

                sb.AppendLine("/>");
            }

            // データ件数が3件を超える場合は省略記号を表示
            if (DataCount.Value > 3)
            {
                sb.AppendLine("  <!-- ... 他のデータ -->");
            }

            sb.AppendLine($"</{RootElementName.Value}>");
        }

        #region サンプル値生成メソッド

        /// <summary>
        /// C#のプロパティ型に応じたサンプル値を取得
        /// </summary>
        private string GetSampleValueForType(string typeName, string propertyName, string dataType)
        {
            // データタイプに基づいた値の生成
            switch (dataType)
            {
                case "ID/連番":
                    return "i + 1";

                case "名前":
                    if (propertyName.ToLowerInvariant().Contains("first"))
                        return "\"FirstName\" + i";
                    if (propertyName.ToLowerInvariant().Contains("last"))
                        return "\"LastName\" + i";
                    return "\"Name\" + i";

                case "Email":
                    return "$\"user{i}@example.com\"";

                case "電話番号":
                    return "$\"555-{random.Next(1000, 9999)}\"";

                case "住所":
                    return "$\"Address {i}, Street {random.Next(1, 100)}\"";

                case "日付":
                    return "DateTime.Now.AddDays(random.Next(-30, 30))";

                case "ブール値":
                    return "random.Next(2) == 0";

                case "GUID":
                    return "Guid.NewGuid()";

                case "価格/金額":
                    return "Math.Round(random.NextDouble() * 100, 2)";
            }

            // 型名に基づいたデフォルト値
            switch (typeName.ToLowerInvariant())
            {
                case "int":
                case "int32":
                case "long":
                case "int64":
                    return "random.Next(1, 100)";

                case "double":
                case "float":
                case "decimal":
                    return "Math.Round(random.NextDouble() * 100, 2)";

                case "bool":
                case "boolean":
                    return "random.Next(2) == 0";

                case "datetime":
                    return "DateTime.Now.AddDays(random.Next(-30, 30))";

                case "guid":
                    return "Guid.NewGuid()";

                case "string":
                default:
                    return $"\"Item {propertyName} \" + i";
            }
        }

        /// <summary>
        /// JSONプロパティ用のサンプル値を取得
        /// </summary>
        private string GetSampleJsonValue(string propertyName, string typeName, string dataType)
        {
            // データタイプに基づいた値の生成
            switch (dataType)
            {
                case "ID/連番":
                    return "1";

                case "名前":
                    if (propertyName.ToLowerInvariant().Contains("first"))
                        return "\"FirstName1\"";
                    if (propertyName.ToLowerInvariant().Contains("last"))
                        return "\"LastName1\"";
                    return "\"Name1\"";

                case "Email":
                    return "\"user1@example.com\"";

                case "電話番号":
                    return "\"555-1234\"";

                case "住所":
                    return "\"Address 1, Street 10\"";

                case "日付":
                    return $"\"{DateTime.Now:yyyy-MM-dd}\"";

                case "ブール値":
                    return "true";

                case "GUID":
                    return $"\"{Guid.NewGuid()}\"";

                case "価格/金額":
                    return "99.95";
            }

            // 型名に基づいたデフォルト値
            switch (typeName.ToLowerInvariant())
            {
                case "int":
                case "int32":
                case "long":
                case "int64":
                    return "42";

                case "double":
                case "float":
                case "decimal":
                    return "42.5";

                case "bool":
                case "boolean":
                    return "true";

                case "datetime":
                    return $"\"{DateTime.Now:yyyy-MM-dd}\"";

                case "guid":
                    return $"\"{Guid.NewGuid()}\"";

                case "string":
                default:
                    return $"\"Sample {propertyName}\"";
            }
        }

        /// <summary>
        /// CSV値用のサンプル値を取得
        /// </summary>
        private string GetSampleCsvValue(string headerName, string typeName, string dataType)
        {
            // CSV形式では文字列にカンマが含まれる可能性があるためダブルクォートで囲む

            // データタイプに基づいた値の生成
            switch (dataType)
            {
                case "ID/連番":
                    return "1";

                case "名前":
                    if (headerName.ToLowerInvariant().Contains("first"))
                        return "\"FirstName1\"";
                    if (headerName.ToLowerInvariant().Contains("last"))
                        return "\"LastName1\"";
                    return "\"Name1\"";

                case "Email":
                    return "user1@example.com";

                case "電話番号":
                    return "555-1234";

                case "住所":
                    return "\"Address 1, Street 10\"";

                case "日付":
                    return $"{DateTime.Now:yyyy-MM-dd}";

                case "ブール値":
                    return "true";

                case "GUID":
                    return $"{Guid.NewGuid()}";

                case "価格/金額":
                    return "99.95";
            }

            // 型名に基づいたデフォルト値
            switch (typeName.ToLowerInvariant())
            {
                case "int":
                case "int32":
                case "long":
                case "int64":
                    return "42";

                case "double":
                case "float":
                case "decimal":
                    return "42.5";

                case "bool":
                case "boolean":
                    return "true";

                case "datetime":
                    return $"{DateTime.Now:yyyy-MM-dd}";

                case "guid":
                    return $"{Guid.NewGuid()}";

                case "string":
                default:
                    return $"\"Sample {headerName}\"";
            }
        }

        /// <summary>
        /// SQL値用のサンプル値を取得
        /// </summary>
        private string GetSampleSqlValue(string columnName, string typeName, string dataType)
        {
            // データタイプに基づいた値の生成
            switch (dataType)
            {
                case "ID/連番":
                    return "1";

                case "名前":
                    if (columnName.ToLowerInvariant().Contains("first"))
                        return "'FirstName1'";
                    if (columnName.ToLowerInvariant().Contains("last"))
                        return "'LastName1'";
                    return "'Name1'";

                case "Email":
                    return "'user1@example.com'";

                case "電話番号":
                    return "'555-1234'";

                case "住所":
                    return "'Address 1, Street 10'";

                case "日付":
                    return $"'{DateTime.Now:yyyy-MM-dd}'";

                case "ブール値":
                    return "1";

                case "GUID":
                    return $"'{Guid.NewGuid()}'";

                case "価格/金額":
                    return "99.95";
            }

            // 型名に基づいたデフォルト値
            switch (typeName.ToLowerInvariant())
            {
                case "int":
                case "int32":
                case "long":
                case "int64":
                    return "42";

                case "double":
                case "float":
                case "decimal":
                    return "42.5";

                case "bool":
                case "boolean":
                    return "1";

                case "datetime":
                    return $"'{DateTime.Now:yyyy-MM-dd}'";

                case "guid":
                    return $"'{Guid.NewGuid()}'";

                case "string":
                default:
                    return $"'Sample {columnName}'";
            }
        }

        /// <summary>
        /// XML属性用のサンプル値を取得
        /// </summary>
        private string GetSampleXmlValue(string attributeName, string typeName, string dataType)
        {
            // XMLでは属性値に引用符が含まれると問題があるため、エスケープ処理が必要

            // データタイプに基づいた値の生成
            switch (dataType)
            {
                case "ID/連番":
                    return "1";

                case "名前":
                    if (attributeName.ToLowerInvariant().Contains("first"))
                        return "FirstName1";
                    if (attributeName.ToLowerInvariant().Contains("last"))
                        return "LastName1";
                    return "Name1";

                case "Email":
                    return "user1@example.com";

                case "電話番号":
                    return "555-1234";

                case "住所":
                    return "Address 1, Street 10";

                case "日付":
                    return $"{DateTime.Now:yyyy-MM-dd}";

                case "ブール値":
                    return "true";

                case "GUID":
                    return $"{Guid.NewGuid()}";

                case "価格/金額":
                    return "99.95";
            }

            // 型名に基づいたデフォルト値
            switch (typeName.ToLowerInvariant())
            {
                case "int":
                case "int32":
                case "long":
                case "int64":
                    return "42";

                case "double":
                case "float":
                case "decimal":
                    return "42.5";

                case "bool":
                case "boolean":
                    return "true";

                case "datetime":
                    return $"{DateTime.Now:yyyy-MM-dd}";

                case "guid":
                    return $"{Guid.NewGuid()}";

                case "string":
                default:
                    return $"Sample {attributeName}";
            }
        }

        #endregion
    }

    /// <summary>
    /// プロパティ情報を保持するViewModel
    /// </summary>
    public class PropertyViewModel : ViewModelBase
    {
        public ReactivePropertySlim<string> Name { get; } = new ReactivePropertySlim<string>();
        public ReactivePropertySlim<string> Type { get; } = new ReactivePropertySlim<string>();
        public ReactivePropertySlim<string> DataType { get; } = new ReactivePropertySlim<string>();

        public PropertyViewModel()
        {
            // ReactivePropertyをDisposablesコレクションに追加
            Name.AddTo(Disposables);
            Type.AddTo(Disposables);
            DataType.AddTo(Disposables);
        }
    }
}