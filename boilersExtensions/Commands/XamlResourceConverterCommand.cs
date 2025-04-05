using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using boilersExtensions.Helpers;
using boilersExtensions.ViewModels;
using boilersExtensions.Views;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace boilersExtensions.Commands
{
    internal sealed class XamlRegisterResourceStringCommand : OleMenuCommand
    {
        public const int CommandId = 0x0200; // 新しいコマンドID
        public static readonly Guid CommandSet = new Guid("70264969-bdcf-4cd4-a9a3-ac8ba3e90466"); // 既存のCommandSetを再利用

        private static AsyncPackage package;

        private XamlRegisterResourceStringCommand() : base(Execute, new CommandID(CommandSet, CommandId))
        {
            base.BeforeQueryStatus += BeforeQueryStatus;
        }

        public static XamlRegisterResourceStringCommand Instance { get; private set; }
        private static IAsyncServiceProvider ServiceProvider => package;

        public static async Task InitializeAsync(AsyncPackage package)
        {
            XamlRegisterResourceStringCommand.package = package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new XamlRegisterResourceStringCommand();
            Instance.Text = ResourceService.GetString("RegisterResourceString");
            commandService.AddCommand(Instance);

            Debug.WriteLine("RegisterResourceStringCommand initialized successfully");
        }

        private static void BeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand command)
            {
                // デフォルトでは非表示に設定
                command.Visible = false;
                command.Enabled = false;

                // DTEサービスを取得
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));

                // アクティブなドキュメントがXAMLファイルかどうかを確認
                if (dte?.ActiveDocument != null)
                {
                    string extension = System.IO.Path.GetExtension(dte.ActiveDocument.FullName);
                    bool isXamlFile = extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase);

                    // XAMLファイルが開かれている場合のみコマンドを表示・有効化
                    if (isXamlFile)
                    {
                        command.Visible = true;
                        command.Enabled = true;
                    }
                }
            }
        }

        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // 現在のXAMLドキュメントを取得
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                if (dte?.ActiveDocument == null || dte.ActiveDocument.Language != "XAML")
                {
                    ShowDialogMessage("Please open a XAML file first.");
                    return;
                }

                // XAMLファイルかどうかを確認
                string extension = System.IO.Path.GetExtension(dte.ActiveDocument.FullName);
                if (!extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    ShowDialogMessage("This command can only be used with XAML files.");
                    return;
                }

                // XAMLコードを取得
                var textSelection = dte.ActiveDocument.Selection as TextSelection;
                string xamlContent = textSelection != null && !textSelection.IsEmpty
                    ? textSelection.Text
                    : GetDocumentText(dte.ActiveDocument);

                // ダイアログを表示
                var viewModel = new XamlRegisterResourceStringDialogViewModel
                {
                    OriginalXaml = xamlContent,
                    Package = package
                };

                var dialog = new XamlRegisterResourceStringDialog { DataContext = viewModel };
                bool? result = dialog.ShowDialog();

                if (result == true)
                {
                    // 変換したXAMLを既存のドキュメントに適用
                    if (textSelection != null && !textSelection.IsEmpty)
                    {
                        // 行番号を取得
                        int lineNumber = textSelection.BottomPoint.Line;

                        // 置換
                        textSelection.Text = viewModel.ConvertedXaml.Value;

                        // 該当行に移動
                        textSelection.GotoLine(lineNumber);

                        // 行末に移動
                        textSelection.EndOfLine();

                        // 末尾3文字を削除するために3文字分左に移動してから選択
                        textSelection.CharLeft(true, 3);

                        // 選択した3文字を削除
                        textSelection.Delete();
                    }
                    else
                    {
                        SetDocumentText(dte.ActiveDocument, viewModel.ConvertedXaml.Value);
                    }

                    ShowDialogMessage("XAML strings converted to resource references successfully.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Execute: {ex.Message}");
                ShowDialogMessage($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// ドキュメント全体のテキストを取得する
        /// </summary>
        /// <param name="document">対象ドキュメント</param>
        /// <returns>ドキュメントのテキスト全体</returns>
        private static string GetDocumentText(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // TextDocumentオブジェクトを取得
                TextDocument textDocument = document.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    Debug.WriteLine("Failed to get TextDocument from Document");
                    return string.Empty;
                }

                // 編集ポイントを作成
                EditPoint startPoint = textDocument.StartPoint.CreateEditPoint();
                EditPoint endPoint = textDocument.EndPoint.CreateEditPoint();

                // 全テキストを取得
                return startPoint.GetText(endPoint);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetDocumentText: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// ドキュメント全体のテキストを置き換える
        /// </summary>
        /// <param name="document">対象ドキュメント</param>
        /// <param name="newText">新しいテキスト</param>
        /// <returns>成功した場合はtrue</returns>
        private static bool SetDocumentText(Document document, string newText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // TextDocumentオブジェクトを取得
                TextDocument textDocument = document.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                {
                    Debug.WriteLine("Failed to get TextDocument from Document");
                    return false;
                }

                // テキスト選択を取得
                TextSelection selection = textDocument.Selection;

                // 全テキストを選択
                selection.SelectAll();

                // 新しいテキストで置き換える
                selection.Text = newText;

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SetDocumentText: {ex.Message}");
                return false;
            }
        }

        private static void ShowMessage(string message)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var dte = (DTE)Package.GetGlobalService(typeof(DTE));
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
        /// メッセージダイアログを表示します
        /// </summary>
        /// <param name="message">表示するメッセージ</param>
        /// <param name="title">ダイアログのタイトル（省略可）</param>
        /// <param name="icon">表示するアイコン（省略可）</param>
        private static void ShowDialogMessage(string message, string title = "boilersExtensions", OLEMSGICON icon = OLEMSGICON.OLEMSGICON_INFO)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // ダイアログを表示
                    VsShellUtilities.ShowMessageBox(
                        package,
                        message,
                        title,
                        icon,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                    // ステータスバーにも表示（オプション）
                    var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                    if (dte != null)
                    {
                        dte.StatusBar.Text = message;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dialog display error: {ex.Message}");

                // 最後の砦としてのフォールバック：通常のメッセージだけでも表示を試みる
                try
                {
                    ShowMessage(message);
                }
                catch { }
            }
        }
    }
}
