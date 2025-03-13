using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.ComponentModel.Design;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Package = Microsoft.VisualStudio.Shell.Package;

namespace boilersExtensions.Commands
{
    internal class UpdateGuidCommand : OleMenuCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("5d92efdf-28cc-4a31-9c52-7f633ee6b0e6");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private static AsyncPackage package;

        private static OleMenuCommand menuItem;

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static UpdateGuidCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private static IAsyncServiceProvider ServiceProvider => package;

        private UpdateGuidCommand() : base(Execute, BeforeQueryStatus, new CommandID(CommandSet, CommandId))
        {
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            UpdateGuidCommand.package = package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            menuItem = Instance = new UpdateGuidCommand();
            commandService.AddCommand(Instance);
        }

        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // DTEオブジェクトを取得
            DTE dte = (DTE)Package.GetGlobalService(typeof(DTE));
            var textDocument = dte.ActiveDocument.Object("TextDocument") as TextDocument;

            if (textDocument != null)
            {
                // 選択範囲を取得
                var selection = textDocument.Selection;
                string selectedText = selection.Text;

                // GUIDの形式をチェック
                if (IsGuid(selectedText))
                {
                    // 新しいGUIDを生成
                    string newGuid = Guid.NewGuid().ToString();
                    if (selectedText.StartsWith("{") && selectedText.EndsWith("}"))
                    {
                        newGuid = "{" + newGuid + "}";
                    }

                    // UndoContextを開始
                    dte.UndoContext.Open("Update GUID");

                    try
                    {
                        // ドキュメント内の全ての一致するGUIDを検索して置換
                        ReplaceAllGuidOccurrences(textDocument, selectedText, newGuid);

                        // 成功メッセージを表示
                        VsShellUtilities.ShowMessageBox(
                            package,
                            $"GUIDを更新しました：\n{selectedText} → {newGuid}",
                            "GUID更新",
                            OLEMSGICON.OLEMSGICON_INFO,
                            OLEMSGBUTTON.OLEMSGBUTTON_OK,
                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    }
                    finally
                    {
                        // UndoContextを閉じる
                        dte.UndoContext.Close();
                    }
                }
                else
                {
                    // 選択テキストがGUIDでない場合のエラーメッセージ
                    VsShellUtilities.ShowMessageBox(
                        package,
                        "選択されたテキストはGUID形式ではありません。",
                        "GUID更新エラー",
                        OLEMSGICON.OLEMSGICON_WARNING,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
            }
        }

        /// <summary>
        /// ドキュメント内のすべての一致するGUIDを新しいGUIDで置換
        /// </summary>
        private static void ReplaceAllGuidOccurrences(TextDocument textDocument, string oldGuid, string newGuid)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // ドキュメントの先頭に移動
            var searchPoint = textDocument.StartPoint.CreateEditPoint();

            // 最初の出現箇所を検索
            TextRanges replacements = null;
            bool found = searchPoint.FindPattern(oldGuid, (int)EnvDTE.vsFindOptions.vsFindOptionsMatchCase, Tags: replacements);

            // 見つかる限り置換を続ける
            while (found)
            {
                // 見つかった範囲にカーソルを移動
                var editPoint = searchPoint.CreateEditPoint();

                // 古いGUIDを削除して新しいGUIDを挿入
                editPoint.Delete(oldGuid.Length);
                editPoint.Insert(newGuid);

                // 次の出現を検索
                found = searchPoint.FindPattern(oldGuid, (int)EnvDTE.vsFindOptions.vsFindOptionsMatchCase, Tags: replacements);
            }
        }

        private static void BeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand command)
            {
                // DTEオブジェクトを取得
                DTE dte = (DTE)Package.GetGlobalService(typeof(DTE));

                // アクティブなドキュメントがある場合のみ有効化
                if (dte.ActiveDocument != null)
                {
                    var textDocument = dte.ActiveDocument.Object("TextDocument") as TextDocument;
                    if (textDocument != null)
                    {
                        var selection = textDocument.Selection;
                        // テキストが選択されていて、それがGUIDフォーマットの場合のみ有効化
                        command.Enabled = !string.IsNullOrEmpty(selection.Text) && IsGuid(selection.Text);
                        return;
                    }
                }

                // それ以外の場合は無効化
                command.Enabled = false;
            }
        }

        /// <summary>
        /// 与えられた文字列がGUID形式かどうかをチェック
        /// </summary>
        private static bool IsGuid(string text)
        {
            // GUIDの基本パターン: 8-4-4-4-12の16進数
            string guidPattern = @"^(\{?)[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(\}?)$";
            return Regex.IsMatch(text, guidPattern);
        }
    }
}