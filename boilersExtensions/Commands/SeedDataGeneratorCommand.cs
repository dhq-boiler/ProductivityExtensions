using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using boilersExtensions.Dialogs;
using boilersExtensions.Models;
using boilersExtensions.ViewModels;
using boilersExtensions.Views;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using static System.Net.Mime.MediaTypeNames;
using Task = System.Threading.Tasks.Task;

namespace boilersExtensions.Commands
{
    /// <summary>
    /// テストデータ生成コマンド
    /// </summary>
    internal sealed class SeedDataGeneratorCommand : OleMenuCommand
    {
        public const int CommandId = 0x0200;
        public static readonly Guid CommandSet = new Guid("0A3B7D5F-6D61-4B5E-9A4F-6D0E6F8B3F1C"); // boilersExtensionsExtensionsCmdSet と同じGUID
        private static AsyncPackage package;
        private static OleMenuCommand menuItem;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        private SeedDataGeneratorCommand() : base(Execute, new CommandID(CommandSet, CommandId))
        {
            base.BeforeQueryStatus += BeforeQueryStatus;
        }

        public static SeedDataGeneratorCommand Instance { get; private set; }
        private static IAsyncServiceProvider ServiceProvider => package;

        /// <summary>
        /// 初期化
        /// </summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            SeedDataGeneratorCommand.package = package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            menuItem = Instance = new SeedDataGeneratorCommand();
            commandService.AddCommand(Instance);

            Debug.WriteLine("SeedDataGeneratorCommand initialized successfully");
        }

        /// <summary>
        /// コマンド実行時の処理
        /// </summary>
        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // 設定が無効な場合は何もしない
                if (!BoilersExtensionsSettings.IsSeedDataGeneratorEnabled)
                {
                    Debug.WriteLine("SeedDataGenerator feature is disabled in settings");
                    return;
                }

                Debug.WriteLine("SeedDataGeneratorCommand Execute called");

                // アクティブなドキュメントの情報を取得
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                if (dte?.ActiveDocument == null)
                {
                    ShowMessage("アクティブなドキュメントがありません。");
                    return;
                }

                // ドキュメントの種類に応じた処理
                var documentPath = dte.ActiveDocument.FullName;
                var extension = Path.GetExtension(documentPath).ToLowerInvariant();

                // C#ファイルの場合はクラス構造を分析してダイアログを表示
                if (extension == ".cs")
                {
                    HandleCSharpFile(dte.ActiveDocument);
                }
                // JSONファイルの場合はJSON構造を分析してダイアログを表示
                else if (extension == ".json")
                {
                    HandleJsonFile(dte.ActiveDocument);
                }
                // その他のサポートされる形式
                else if (extension == ".xml" || extension == ".csv" || extension == ".sql")
                {
                    HandleOtherFile(dte.ActiveDocument, extension);
                }
                else
                {
                    ShowMessage($"サポートされていないファイル形式です: {extension}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Execute: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                ShowMessage($"エラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// C#ファイルの処理
        /// </summary>
        private static void HandleCSharpFile(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // TextDocumentのテキストを取得
                var textDocument = document.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    ShowMessage("テキストとして開けませんでした。");
                    return;
                }

                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var documentText = editPoint.GetText(textDocument.EndPoint);

                // クラス構造を分析（簡易的な実装）
                var classNamePattern = @"(class|record)\s+(\w+)";
                var propertyPattern = @"(public|private|protected|internal)(?:\s+virtual|\s+override|\s+new)?\s+([^\s{]+)\s+(\w+)\s*\{\s*get\s*;";

                var classNames = System.Text.RegularExpressions.Regex.Matches(documentText, classNamePattern)
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToList();

                var properties = System.Text.RegularExpressions.Regex.Matches(documentText, propertyPattern)
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => new PropertyInfo
                    {
                        Name = m.Groups[3].Value,
                        TypeName = m.Groups[2].Value,
                        FullTypeName = m.Groups[2].Value, // 完全修飾名は後で必要に応じて解析
                        ColumnName = m.Groups[3].Value // デフォルトでプロパティ名と同じ
                    })
                    .ToList();

                // ここでダイアログを表示してシード値の生成オプションを選択させる
                // シンプルな実装としてメッセージボックスを表示
                var message = $"検出されたクラス: {string.Join(", ", classNames)}\n";
                message += $"プロパティ数: {properties.Count}";

                VsShellUtilities.ShowMessageBox(
                    package,
                    message,
                    "クラス構造分析",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                // SeedDataConfigDialogを表示
                var viewModel = new SeedDataConfigViewModel
                {
                    TargetFileName = { Value = document.FullName },
                    TargetType = { Value = classNames.First() },
                    Package = package
                };

                var window = new SeedDataConfigDialog
                {
                    DataContext = viewModel
                };

                // ViewModelの初期化
                viewModel.OnDialogOpened(window);

                // SetTargetDocumentメソッドを呼び出す - documentTypeには"csharp"を指定
                viewModel.SetTargetDocument(document, "csharp");

                // ダイアログを表示
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in HandleCSharpFile: {ex.Message}");
                ShowMessage($"C#ファイルの分析中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// JSONファイルの処理
        /// </summary>
        private static void HandleJsonFile(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // JSONファイルの内容を取得
                var textDocument = document.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    ShowMessage("JSONファイルをテキストとして開けませんでした。");
                    return;
                }

                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var jsonText = editPoint.GetText(textDocument.EndPoint);

                // JSONファイルの構造を簡易的に分析（実際にはもっと高度な解析が必要）
                var keysCount = jsonText.Split('"').Length / 2;
                var arrayCount = jsonText.Split('[').Length - 1;
                var objectCount = jsonText.Split('{').Length - 1;

                var message = $"JSONのキー数（推定）: {keysCount}\n";
                message += $"配列の数: {arrayCount}\n";
                message += $"オブジェクトの数: {objectCount}";

                VsShellUtilities.ShowMessageBox(
                    package,
                    message,
                    "JSON構造分析",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                // 本来はここでJSONシード生成ダイアログを表示
                // 将来的な拡張ポイント
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in HandleJsonFile: {ex.Message}");
                ShowMessage($"JSONファイルの分析中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// その他のファイル形式の処理
        /// </summary>
        private static void HandleOtherFile(Document document, string extension)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // ファイルの内容を取得
                var textDocument = document.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    ShowMessage("ファイルをテキストとして開けませんでした。");
                    return;
                }

                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var fileText = editPoint.GetText(textDocument.EndPoint);

                // ファイル形式に応じた簡易的な分析
                string message;

                switch (extension)
                {
                    case ".xml":
                        var tagCount = fileText.Split('<').Length - 1;
                        message = $"XMLタグの数（推定）: {tagCount}";
                        break;

                    case ".csv":
                        var lineCount = fileText.Split('\n').Length;
                        var columnCount = fileText.Split('\n')[0].Split(',').Length;
                        message = $"行数: {lineCount}\n列数: {columnCount}";
                        break;

                    case ".sql":
                        var insertCount = fileText.Split(new[] { "INSERT" }, StringSplitOptions.None).Length - 1;
                        var selectCount = fileText.Split(new[] { "SELECT" }, StringSplitOptions.None).Length - 1;
                        message = $"INSERT文の数: {insertCount}\nSELECT文の数: {selectCount}";
                        break;

                    default:
                        message = "このファイル形式の詳細分析はまだ実装されていません。";
                        break;
                }

                VsShellUtilities.ShowMessageBox(
                    package,
                    message,
                    $"{extension}ファイル分析",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                // 本来はここで適切なシード生成ダイアログを表示
                // 将来的な拡張ポイント
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in HandleOtherFile: {ex.Message}");
                ShowMessage($"ファイルの分析中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// メッセージを表示
        /// </summary>
        private static void ShowMessage(string message)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () => {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                    if (dte != null)
                    {
                        dte.StatusBar.Text = message;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Status bar update error: {ex.Message}");
            }
        }

        /// <summary>
        /// コマンドの有効/無効状態を更新
        /// </summary>
        private static void BeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand command)
            {
                // 設定で無効化されているかチェック
                bool featureEnabled = BoilersExtensionsSettings.IsSeedDataGeneratorEnabled;

                if (!featureEnabled)
                {
                    // 機能が無効の場合はメニュー項目を非表示にする
                    command.Visible = false;
                    command.Enabled = false;
                    return;
                }

                // 機能が有効な場合は通常の条件で表示/非表示を決定
                command.Visible = true;

                // DTEオブジェクトを取得
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));

                // アクティブなドキュメントがある場合のみ有効化
                if (dte?.ActiveDocument != null)
                {
                    var extension = Path.GetExtension(dte.ActiveDocument.FullName).ToLowerInvariant();

                    // サポートされているファイル形式かチェック
                    command.Enabled = extension == ".cs" || extension == ".json" ||
                                   extension == ".xml" || extension == ".csv" ||
                                   extension == ".sql";
                    return;
                }

                // それ以外の場合は無効化
                command.Enabled = false;
            }
        }
    }
}