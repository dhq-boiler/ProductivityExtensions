using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using boilersExtensions.Analyzers;
using boilersExtensions.Models;
using boilersExtensions.Utils;
using EnvDTE;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Project = Microsoft.CodeAnalysis.Project;
using Solution = Microsoft.CodeAnalysis.Solution;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using Document = EnvDTE.Document;
using TextDocument = EnvDTE.TextDocument;
using static boilersExtensions.Generators.FixedValueCombinationGenerator;
using boilersExtensions.Generators;

namespace boilersExtensions.ViewModels
{
    /// <summary>
    /// シードデータ設定ダイアログのViewModel（改修版）
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
        public ObservableCollection<PropertyViewModel> Properties { get; } =
            new ObservableCollection<PropertyViewModel>();

        public ReactivePropertySlim<PropertyViewModel> SelectedProperty { get; } =
            new ReactivePropertySlim<PropertyViewModel>();

        // エンティティ関連
        public ObservableCollection<EntityViewModel> Entities { get; } = new ObservableCollection<EntityViewModel>();

        public ReactivePropertySlim<EntityViewModel> SelectedEntity { get; } =
            new ReactivePropertySlim<EntityViewModel>();

        public ReactivePropertySlim<RelationshipViewModel> SelectedRelationship { get; } =
            new ReactivePropertySlim<RelationshipViewModel>();

        public List<string> EntityNames => Entities.Select(e => e.Name.Value).ToList();

        public List<RelationshipType> RelationshipTypes { get; } =
            Enum.GetValues(typeof(RelationshipType)).Cast<RelationshipType>().ToList();

        public ReactivePropertySlim<string> RecordCountInfoText { get; } = new ReactivePropertySlim<string>("10件");
        public ReactivePropertySlim<int> TotalRecordCount { get; } = new ReactivePropertySlim<int>(10);

        // エンティティ依存関係
        public ReactivePropertySlim<bool> ShowRelationshipVisualization { get; } = new ReactivePropertySlim<bool>(true);
        public ReactivePropertySlim<bool> AutoDetectRelationships { get; } = new ReactivePropertySlim<bool>(true);

        // UI表示用コレクション
        public ObservableCollection<EntityRelationshipInfo> EntityRelationships { get; } =
            new ObservableCollection<EntityRelationshipInfo>();

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

        public ReactivePropertySlim<DateTime?> StartDate { get; } =
            new ReactivePropertySlim<DateTime?>(DateTime.Now.AddMonths(-1));

        public ReactivePropertySlim<DateTime?> EndDate { get; } =
            new ReactivePropertySlim<DateTime?>(DateTime.Now.AddMonths(1));

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
        public ReactiveCommand LoadAdditionalEntityCommand { get; }
        public ReactiveCommand AddRelationshipCommand { get; }
        public ReactiveCommand RemoveRelationshipCommand { get; }
        public ReactiveCommand DetectRelationshipsCommand { get; }
        public ReactiveCommand UpdateRecordCountsCommand { get; }

        #endregion

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public SeedDataConfigViewModel()
        {
            // プロパティの初期化
            Properties.Clear();

            // エンティティリストを最初に空にする
            Entities.Clear();

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

            // エンティティ選択変更時の処理
            SelectedEntity.Subscribe(entity =>
            {
                if (entity != null)
                {
                    // エンティティの親子関係を表示するために情報を更新
                    UpdateEntityRelationshipInfo();
                }

                // プレビュー更新
                UpdatePreview();
            }).AddTo(Disposables);

            // プロパティ変更時のプレビュー更新
            DataCount.Subscribe(_ =>
            {
                UpdateEntityRecordCounts();
                UpdatePreview();
            }).AddTo(Disposables);

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

            LoadAdditionalEntityCommand = new ReactiveCommand()
                .WithSubscribe(LoadAdditionalEntity)
                .AddTo(Disposables);

            AddRelationshipCommand = new ReactiveCommand()
                .WithSubscribe(AddRelationship)
                .AddTo(Disposables);

            RemoveRelationshipCommand = new ReactiveCommand()
                .WithSubscribe(RemoveRelationship)
                .AddTo(Disposables);

            DetectRelationshipsCommand = new ReactiveCommand()
                .WithSubscribe(DetectAndSetupRelationships)
                .AddTo(Disposables);

            UpdateRecordCountsCommand = new ReactiveCommand()
                .WithSubscribe(UpdateEntityRecordCounts)
                .AddTo(Disposables);

            CancelCommand = new ReactiveCommand()
                .WithSubscribe(() =>
                {
                    var window = Application.Current.Windows.OfType<System.Windows.Window>()
                        .FirstOrDefault(w => w.DataContext == this);
                    window?.Close();
                })
                .AddTo(Disposables);
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

            // EntityViewModelが利用可能な場合は、そこにも設定を追加
            if (SelectedEntity.Value != null)
            {
                SelectedEntity.Value.Properties.Clear();
                SelectedEntity.Value.PropertyConfigs.Clear();
            }

            foreach (System.Text.RegularExpressions.Match match in propertyMatches)
            {
                var type = match.Groups[1].Value;
                var name = match.Groups[2].Value;

                // PropertyViewModelを作成
                var property = new PropertyViewModel
                {
                    Name = { Value = name },
                    Type = { Value = type },
                    DataType = { Value = DetermineDataType(name, type) }
                };

                // 古いスタイルのUIの場合はPropertiesコレクションに追加
                Properties.Add(property);

                // EntityViewModelが利用可能な場合は、そこにもプロパティを追加
                if (SelectedEntity.Value != null)
                {
                    SelectedEntity.Value.Properties.Add(property);

                    // PropertyConfigViewModelも作成して追加
                    var propConfig = new PropertyConfigViewModel
                    {
                        PropertyName = name,
                        PropertyTypeName = type
                    };

                    SelectedEntity.Value.PropertyConfigs.Add(propConfig);
                }
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

        private void AddRelationship()
        {
            if (SelectedEntity.Value == null || Entities.Count < 2) return;

            var relationship = new RelationshipViewModel
            {
                SourceEntityName = { Value = SelectedEntity.Value.Name.Value },
                TargetEntityName = { Value = Entities.First(e => e != SelectedEntity.Value).Name.Value },
                SourceProperty = { Value = "Id" },
                TargetProperty = { Value = "Id" },
                RelationType = { Value = RelationshipType.OneToMany }
            };

            SelectedEntity.Value.Relationships.Add(relationship);
            SelectedRelationship.Value = relationship;
        }

        /// <summary>
        /// リレーションシップ削除メソッド
        /// </summary>
        private void RemoveRelationship()
        {
            if (SelectedEntity.Value == null || SelectedRelationship.Value == null) return;

            SelectedEntity.Value.Relationships.Remove(SelectedRelationship.Value);
            SelectedRelationship.Value = null;
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
        /// 全プロパティの固定値の組み合わせ数を計算して表示を更新するメソッド
        /// </summary>
        internal void CalculateAndUpdateTotalRecordCount()
        {
            int baseCount = DataCount.Value;
            int totalCombinations = CalculateTotalCombinations();

            // 基本のレコード数と組み合わせ数を掛けて総レコード数を計算
            int totalRecords = baseCount * Math.Max(1, totalCombinations);

            // UIの表示を更新
            if (totalCombinations > 1)
            {
                // 組み合わせ数がある場合は、その情報を表示
                RecordCountInfoText.Value = $"{baseCount} × {totalCombinations} = {totalRecords}件";
                TotalRecordCount.Value = totalRecords;
            }
            else
            {
                // 組み合わせがない場合は単純に件数を表示
                RecordCountInfoText.Value = $"{baseCount}件";
                TotalRecordCount.Value = baseCount;
            }

            // プレビュー更新
            UpdatePreview();
        }

        /// <summary>
        /// 全プロパティの固定値の組み合わせ総数を計算
        /// </summary>
        private int CalculateTotalCombinations()
        {
            int combinations = 1;

            // アクティブなエンティティの固定値を持つプロパティを検索
            var entity = SelectedEntity.Value;
            if (entity != null)
            {
                foreach (var prop in entity.Properties)
                {
                    int fixedValueCount = prop.FixedValues.Count;
                    if (fixedValueCount > 0)
                    {
                        combinations *= fixedValueCount;
                    }
                }
            }
            else
            {
                // 古いUI（単一エンティティ）のための処理
                foreach (var prop in Properties)
                {
                    int fixedValueCount = prop.FixedValues.Count;
                    if (fixedValueCount > 0)
                    {
                        combinations *= fixedValueCount;
                    }
                }
            }

            return combinations;
        }

        /// <summary>
        /// エンティティ間のリレーションシップを自動検出して設定
        /// </summary>
        private void DetectAndSetupRelationships()
        {
            if (Entities.Count <= 1) return;

            // エンティティの親子関係を検出
            var entityConfigs = new List<EntityConfigViewModel>();

            // EntityViewModelからEntityConfigViewModelを作成
            foreach (var entity in Entities)
            {
                var config = new EntityConfigViewModel
                {
                    EntityName = entity.Name.Value, IsSelected = { Value = entity.IsSelected.Value }
                };

                // RecordCountをコピー
                config.RecordCount.Value = entity.RecordCount.Value;

                // プロパティ設定をコピー
                foreach (var prop in entity.Properties)
                {
                    config.PropertyConfigs.Add(new PropertyConfigViewModel
                    {
                        PropertyName = prop.Name.Value, PropertyTypeName = prop.Type.Value
                    });
                }

                entityConfigs.Add(config);
            }

            // リレーションシップを解決
            foreach (var config in entityConfigs)
            {
                config.ResolveEntityRelationships(entityConfigs);
            }

            // EntityViewModelの関係を更新
            foreach (var entity in Entities)
            {
                var config = entityConfigs.FirstOrDefault(c => c.EntityName == entity.Name.Value);
                if (config != null)
                {
                    // 親がある場合は設定を反映
                    if (config.HasParent.Value && config.ParentEntity.Value != null)
                    {
                        var parentName = config.ParentEntity.Value.EntityName;
                        var parentEntity = Entities.FirstOrDefault(e => e.Name.Value == parentName);

                        if (parentEntity != null)
                        {
                            // 親1件あたりの子レコード数を設定（デフォルト値）
                            entity.RecordsPerParent.Value = 2;
                            entity.ParentEntity.Value = parentEntity;

                            // リレーションシップを追加
                            var relationship = entity.Relationships.FirstOrDefault(r =>
                                r.TargetEntityName.Value == parentName);

                            if (relationship == null)
                            {
                                // 外部キーフィールドを探す
                                string fkField = entity.Properties
                                    .FirstOrDefault(p => p.Name.Value == parentName + "Id")?.Name.Value;

                                if (!string.IsNullOrEmpty(fkField))
                                {
                                    relationship = new RelationshipViewModel
                                    {
                                        SourceEntityName = { Value = entity.Name.Value },
                                        SourceProperty = { Value = fkField },
                                        TargetEntityName = { Value = parentName },
                                        TargetProperty = { Value = "Id" },
                                        RelationType = { Value = RelationshipType.ManyToOne }
                                    };

                                    entity.Relationships.Add(relationship);
                                }
                            }
                        }
                    }
                }
            }

            // レコード数を更新
            UpdateEntityRecordCounts();

            // リレーションシップ表示を更新
            UpdateEntityRelationshipInfo();
        }

        /// <summary>
        /// エンティティの親子関係に基づいてレコード数を更新
        /// </summary>
        private void UpdateEntityRecordCounts()
        {
            // 親エンティティから順に処理
            var processedEntities = new HashSet<string>();
            var entitiesToProcess = new Queue<EntityViewModel>(
                Entities.Where(e => e.ParentEntity.Value == null)); // 親を持たないエンティティから

            while (entitiesToProcess.Count > 0)
            {
                var entity = entitiesToProcess.Dequeue();

                if (processedEntities.Contains(entity.Name.Value))
                    continue;

                processedEntities.Add(entity.Name.Value);

                // 親を持たない場合は指定のレコード数
                if (entity.ParentEntity.Value == null)
                {
                    entity.TotalRecordCount.Value = entity.RecordCount.Value;
                }
                // 親を持つ場合は「親のレコード数 × 親1件あたりの件数」
                else
                {
                    var parent = entity.ParentEntity.Value;
                    entity.TotalRecordCount.Value = parent.TotalRecordCount.Value * entity.RecordsPerParent.Value;
                }

                // 子エンティティを処理キューに追加
                foreach (var otherEntity in Entities)
                {
                    if (otherEntity.ParentEntity.Value == entity)
                    {
                        entitiesToProcess.Enqueue(otherEntity);
                    }
                }
            }

            // エンティティ関係図を更新
            UpdateEntityRelationshipInfo();
        }

        /// <summary>
        /// エンティティの親子関係情報を更新
        /// </summary>
        private void UpdateEntityRelationshipInfo()
        {
            EntityRelationships.Clear();

            // まず親エンティティとなるエンティティを追加
            foreach (var entity in Entities.Where(e => e.IsSelected.Value))
            {
                if (entity.ParentEntity.Value == null)
                {
                    // 親を持たないエンティティ
                    EntityRelationships.Add(new EntityRelationshipInfo
                    {
                        ParentEntityName = "",
                        ThisEntityName = entity.Name.Value,
                        RelationshipType = "単体",
                        RecordsPerParent = 0,
                        TotalRecords = entity.TotalRecordCount.Value
                    });
                }
            }

            // 次に子エンティティを追加
            foreach (var entity in Entities.Where(e => e.IsSelected.Value))
            {
                if (entity.ParentEntity.Value != null)
                {
                    var parent = entity.ParentEntity.Value;

                    // 親が選択されているエンティティの場合のみ追加
                    if (parent.IsSelected.Value)
                    {
                        EntityRelationships.Add(new EntityRelationshipInfo
                        {
                            ParentEntityName = parent.Name.Value,
                            ThisEntityName = entity.Name.Value,
                            RelationshipType = "1対多",
                            RecordsPerParent = entity.RecordsPerParent.Value,
                            TotalRecords = entity.TotalRecordCount.Value
                        });
                    }
                }
            }
        }

        // 指定されたエンティティのプロパティリストを取得するメソッド
        public List<string> GetEntityProperties(string entityName)
        {
            var entity = Entities.FirstOrDefault(e => e.Name.Value == entityName);
            if (entity != null)
            {
                return entity.Properties.Select(p => p.Name.Value).ToList();
            }

            return new List<string>();
        }

        private void LoadAdditionalEntity()
        {
            // ファイル選択ダイアログを表示
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "C# Files (*.cs)|*.cs", Title = "関連エンティティファイルを選択"
            };

            if (dialog.ShowDialog() == true)
            {
                // ファイルを開いて解析
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    try
                    {
                        SetProcessing(true, "関連エンティティを解析中...");
                        // DTEからファイルを開く
                        var dte = (EnvDTE.DTE)AsyncPackage.GetGlobalService(typeof(EnvDTE.DTE));
                        var documentWindow = dte.OpenFile(EnvDTE.Constants.vsViewKindCode, dialog.FileName);

                        var document = documentWindow?.Document;

                        // Roslynを使用してエンティティを解析
                        var entityInfo = await LoadEntityFromDocument(document);
                        if (entityInfo != null)
                        {
                            // EntityViewModelへ変換
                            var entityVm = new EntityViewModel
                            {
                                Name = { Value = entityInfo.Name },
                                FullName = { Value = entityInfo.FullName },
                                RecordCount = { Value = 5 }, // デフォルト値
                                RecordsPerParent = { Value = 2 }, // デフォルト値：親1件あたり2件
                                IsSelected = { Value = true },
                                FilePath = { Value = dialog.FileName }
                            };

                            // プロパティを読み込み
                            foreach (var prop in entityInfo.Properties)
                            {
                                entityVm.Properties.Add(new PropertyViewModel
                                {
                                    Name = { Value = prop.Name },
                                    Type = { Value = prop.TypeName },
                                    DataType = { Value = DetermineDataType(prop.Name, prop.TypeName) }
                                });
                            }

                            Entities.Add(entityVm);
                            SelectedEntity.Value = entityVm;

                            // 自動的にリレーションシップを検出
                            if (AutoDetectRelationships.Value)
                            {
                                DetectAndSetupRelationships();
                            }
                        }
                    }
                    finally
                    {
                        SetProcessing(false);
                    }
                });
            }
        }

        /// <summary>
        /// 名前と型からデータタイプを推測
        /// </summary>
        private string DetermineDataType(string name, string type)
        {
            var nameLower = name.ToLowerInvariant();

            // GUID
            if (nameLower.Contains("guid") || nameLower.Contains("uuid") ||
                type == "Guid" || type.Contains("Unique"))
            {
                return "GUID";
            }

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
        /// ドキュメントからエンティティ情報を解析して読み込む
        /// </summary>
        private async Task<EntityInfo> LoadEntityFromDocument(EnvDTE.Document document)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Roslynドキュメントを取得
                var roslynDoc = await GetRoslynDocumentFromActiveDocumentAsync(document);
                if (roslynDoc == null) return null;

                // 以下はコードの一部を省略（既存のコードを引き継ぎ）
                // EntityAnalyzerを使用してエンティティ情報を取得する処理
                var analyzer = new EntityAnalyzer();
                var entities = await analyzer.AnalyzeEntitiesAsync(roslynDoc);

                if (entities.Count > 0)
                {
                    return entities[0]; // 最初のエンティティを返す
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading entity from document: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }

            return null;
        }

        // 以下は既存のメソッドの省略
        // ...

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
                    if (Entities.Count == 0 || Entities.All(e => e.Properties.Count == 0))
                    {
                        MessageBox.Show(
                            "プロパティ/エンティティが設定されていません。スキーマ読込または手動でプロパティ/エンティティを追加してください。",
                            "エラー",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    // EntityInfo リストを作成
                    var entityInfos = new List<EntityInfo>();

                    foreach (var entity in Entities.Where(e => e.IsSelected.Value))
                    {
                        // DTEからファイルを開く
                        var dte = (EnvDTE.DTE)AsyncPackage.GetGlobalService(typeof(EnvDTE.DTE));
                        var documentWindow = dte.OpenFile(EnvDTE.Constants.vsViewKindCode, entity.FilePath.Value);

                        var envDTEDocument = documentWindow?.Document;

                        // EntityAnalyzerを使用してC#クラスを解析
                        var analyzer = new Analyzers.EntityAnalyzer();

                        // アクティブなドキュメントをRoslynのDocumentに変換する
                        var document = await GetRoslynDocumentFromActiveDocumentAsync(envDTEDocument);

                        if (document == null)
                        {
                            MessageBox.Show(
                                $"{entity.Name.Value}の解析に失敗しました。",
                                "エラー",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            continue;
                        }

                        try
                        {
                            // 現在選択されているエンティティを解析
                            var entities = await analyzer.AnalyzeEntitiesAsync(document);
                            entityInfos.AddRange(entities);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error analyzing entities: {ex.Message}");
                        }
                    }

                    if (entityInfos.Count == 0)
                    {
                        MessageBox.Show(
                            $"出力可能なエンティティクラスが見つかりませんでした。",
                            "エラー",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    // EntityInfoからSeedDataConfigを作成
                    var config = new SeedDataConfig();

                    // 選択されているエンティティの設定を取得
                    foreach (var entity in entityInfos)
                    {
                        // 対応するEntityViewModelを探す
                        var entityVm = Entities.FirstOrDefault(e => e.Name.Value == entity.Name);
                        if (entityVm == null || !entityVm.IsSelected.Value) continue;

                        var entityConfig = new EntityConfigViewModel { EntityName = entity.Name, IsSelected = { Value = true } };

                        // RecordCount値とRecordsPerParent値を設定
                        entityConfig.RecordCount.Value = entityVm.RecordCount.Value;
                        entityConfig.RecordsPerParent.Value = entityVm.RecordsPerParent.Value;
                        entityConfig.TotalRecordCount.Value = entityVm.TotalRecordCount.Value;

                        // 親エンティティが設定されている場合
                        if (entityVm.ParentEntity.Value != null)
                        {
                            entityConfig.ParentEntityName.Value = entityVm.ParentEntity.Value.Name.Value;
                            entityConfig.HasParent.Value = true;
                        }

                        // プロパティ設定をコピー
                        foreach (var prop in entity.Properties)
                        {
                            if (prop.ExcludeFromSeed || prop.IsNavigationProperty || prop.IsCollection)
                                continue;

                            // UIから該当するプロパティ設定を検索
                            var uiPropConfig = entityVm.GetPropertyConfig(prop.Name);
                            if (uiPropConfig != null)
                            {
                                // 設定をコピー
                                entityConfig.PropertyConfigs.Add(uiPropConfig);
                            }
                            else
                            {
                                // 新しい設定を作成
                                entityConfig.PropertyConfigs.Add(new PropertyConfigViewModel
                                {
                                    PropertyName = prop.Name, PropertyTypeName = prop.TypeName
                                });
                            }
                        }

                        // リレーションシップ設定をコピー
                        foreach (var relationship in entityVm.Relationships)
                        {
                            var relConfig = new RelationshipConfigViewModel
                            {
                                RelatedEntityName = relationship.TargetEntityName.Value,
                                Strategy = ConvertRelationshipType(relationship.RelationType.Value)
                            };

                            // 親1件あたりの子レコード数を設定
                            if (relationship.RelationType.Value == RelationshipType.ManyToOne ||
                                relationship.RelationType.Value == RelationshipType.OneToMany)
                            {
                                relConfig.ChildrenPerParent = entityVm.RecordsPerParent.Value;
                            }

                            entityConfig.RelationshipConfigs.Add(relConfig);
                        }

                        // 設定を保存
                        config.UpdateEntityConfig(entityConfig);
                    }

                    // 親子関係を解決
                    foreach (var entityConfig in config.EntityConfigs)
                    {
                        if (!string.IsNullOrEmpty(entityConfig.ParentEntityName.Value))
                        {
                            var parentConfig = config.GetEntityConfig(entityConfig.ParentEntityName.Value);
                            if (parentConfig != null)
                            {
                                entityConfig.ParentEntity.Value = parentConfig;
                            }
                        }
                    }

                    UpdateProgress(50, "コード生成中...");

                    // 拡張シードデータジェネレーターを使用
                    var seedGenerator = new EnhancedRelationalSeedDataGenerator();
                    var generatedCode = seedGenerator.GenerateSeedDataWithRelationships(entityInfos, config);

                    UpdateProgress(80, "コードを挿入中...");

                    // 生成したコードを挿入
                    var executor = new SeedDataInsertExecutor(Package);
                    bool result =
                        await executor.InsertGeneratedCodeToDocument(_targetDocument, ClassName.Value, generatedCode);

                    UpdateProgress(100, "完了");

                    // 結果に応じたメッセージを表示
                    if (result)
                    {
                        int totalRecords = Entities.Where(e => e.IsSelected.Value)
                            .Sum(e => e.TotalRecordCount.Value);

                        MessageBox.Show(
                            $"合計{totalRecords}件のテストデータを生成しました。",
                            "完了",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        // ダイアログを閉じる
                        var window = Application.Current.Windows.OfType<System.Windows.Window>()
                            .FirstOrDefault(w => w.DataContext == this);
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

        // RelationshipType から RelationshipStrategy への変換メソッド
        private RelationshipStrategy ConvertRelationshipType(RelationshipType type)
        {
            switch (type)
            {
                case RelationshipType.OneToOne:
                    return RelationshipStrategy.OneToOne;
                case RelationshipType.OneToMany:
                    return RelationshipStrategy.OneToMany;
                case RelationshipType.ManyToOne:
                    return RelationshipStrategy.ManyToOne;
                case RelationshipType.ManyToMany:
                    // ManyToManyは直接マッピングできないため、CustomまたはOneToManyを返す
                    return RelationshipStrategy.Custom;
                default:
                    return RelationshipStrategy.OneToOne; // デフォルト
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
    }

    /// <summary>
    /// エンティティ関係情報を表示するためのクラス
    /// </summary>
    public class EntityRelationshipInfo
    {
        /// <summary>
        /// 親エンティティ名
        /// </summary>
        public string ParentEntityName { get; set; }

        /// <summary>
        /// エンティティ名
        /// </summary>
        public string ThisEntityName { get; set; }

        /// <summary>
        /// リレーションシップの種類
        /// </summary>
        public string RelationshipType { get; set; }

        /// <summary>
        /// 親1件あたりの子レコード数
        /// </summary>
        public int RecordsPerParent { get; set; }

        /// <summary>
        /// 合計レコード数
        /// </summary>
        public int TotalRecords { get; set; }

        /// <summary>
        /// 表示用文字列
        /// </summary>
        public override string ToString()
        {
            if (string.IsNullOrEmpty(ThisEntityName))
            {
                return $"{ParentEntityName}: {TotalRecords}件";
            }

            return $"{ParentEntityName} → {ThisEntityName}: 親1件あたり{RecordsPerParent}件 (合計{TotalRecords}件)";
        }
    }

    /// <summary>
    /// プロパティ情報を保持するViewModel
    /// </summary>
    public class PropertyViewModel : ViewModelBase
    {
        public ReactivePropertySlim<string> Name { get; } = new ReactivePropertySlim<string>();
        public ReactivePropertySlim<string> Type { get; } = new ReactivePropertySlim<string>();
        public ReactivePropertySlim<string> DataType { get; } = new ReactivePropertySlim<string>();

        // 固定値リストを保持するプロパティを追加
        public ReactiveCollection<string> FixedValues { get; } = new ReactiveCollection<string>();

        // 固定値があるかどうかを示すプロパティ
        public ReactivePropertySlim<bool> HasFixedValues { get; } = new ReactivePropertySlim<bool>(false);

        public PropertyViewModel()
        {
            // ReactivePropertyをDisposablesコレクションに追加
            Name.AddTo(Disposables);
            Type.AddTo(Disposables);
            DataType.AddTo(Disposables);
            HasFixedValues.AddTo(Disposables);

            // 固定値が追加/削除されたときにHasFixedValuesを更新
            FixedValues.CollectionChanged += (sender, args) =>
            {
                HasFixedValues.Value = FixedValues.Count > 0;
            };
        }

        // 固定値の文字列表現（表示用）
        public string GetFixedValuesDisplayText()
        {
            if (FixedValues.Count == 0)
                return string.Empty;

            return FixedValues.Count == 1
                ? FixedValues[0]
                : $"{FixedValues.Count}個の値...";
        }
    }
}