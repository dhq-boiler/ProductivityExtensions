using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Shell.Interop;
using Package = Microsoft.VisualStudio.Shell.Package;

namespace boilersExtensions.Commands
{
    /// <summary>
    /// #region と #endregion 間を移動するコマンド
    /// </summary>
    internal sealed class RegionNavigatorCommand : OleMenuCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("b6894a95-f2d7-4d2e-a80f-223c722d40c1");
        private static AsyncPackage package;
        private static OleMenuCommand menuItem;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        private RegionNavigatorCommand() : base(Execute, BeforeQueryStatus, new CommandID(CommandSet, CommandId))
        {
        }

        public static RegionNavigatorCommand Instance { get; private set; }
        private static IAsyncServiceProvider ServiceProvider => package;

        /// <summary>
        /// 初期化
        /// </summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            RegionNavigatorCommand.package = package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            menuItem = Instance = new RegionNavigatorCommand();
            commandService.AddCommand(Instance);
        }

        /// <summary>
        /// コマンド実行時の処理
        /// </summary>
        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // 現在のテキストビューを取得
                var textView = GetCurrentTextView();
                if (textView == null)
                {
                    return;
                }

                // カーソル位置の行番号を取得
                var caretPosition = textView.Caret.Position.BufferPosition;
                var currentLine = textView.TextSnapshot.GetLineFromPosition(caretPosition.Position);
                var currentLineText = currentLine.GetText();

                // #region または #endregion を含む行かチェック
                if (IsRegionStart(currentLineText))
                {
                    // #region から対応する #endregion にジャンプ
                    JumpToMatchingEndRegion(textView, currentLine);
                }
                else if (IsRegionEnd(currentLineText))
                {
                    // #endregion から対応する #region にジャンプ
                    JumpToMatchingStartRegion(textView, currentLine);
                }
                else
                {
                    // カーソル行が #region/#endregion でなければ最も近い region/endregion を検索
                    FindAndJumpToNearestRegion(textView, currentLine);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Execute: {ex.Message}");
            }
        }

        /// <summary>
        /// #region 行から対応する #endregion 行にジャンプ
        /// </summary>
        private static void JumpToMatchingEndRegion(IWpfTextView textView, ITextSnapshotLine currentLine)
        {
            var snapshot = textView.TextSnapshot;
            var startLineNumber = currentLine.LineNumber;
            var nestedLevel = 0;

            // 現在の行から下に向かって検索
            for (var i = startLineNumber + 1; i < snapshot.LineCount; i++)
            {
                var line = snapshot.GetLineFromLineNumber(i);
                var lineText = line.GetText();

                if (IsRegionStart(lineText))
                {
                    // ネストしたリージョンの開始
                    nestedLevel++;
                }
                else if (IsRegionEnd(lineText))
                {
                    if (nestedLevel == 0)
                    {
                        // 対応する #endregion が見つかった
                        MoveCaretToLine(textView, i);
                        return;
                    }

                    // ネストしたリージョンの終了
                    nestedLevel--;
                }
            }

            // 対応する #endregion が見つからなかった
            ShowMessage("対応する #endregion が見つかりませんでした。");
        }

        /// <summary>
        /// #endregion 行から対応する #region 行にジャンプ
        /// </summary>
        private static void JumpToMatchingStartRegion(IWpfTextView textView, ITextSnapshotLine currentLine)
        {
            var snapshot = textView.TextSnapshot;
            var startLineNumber = currentLine.LineNumber;
            var nestedLevel = 0;

            // 現在の行から上に向かって検索
            for (var i = startLineNumber - 1; i >= 0; i--)
            {
                var line = snapshot.GetLineFromLineNumber(i);
                var lineText = line.GetText();

                if (IsRegionEnd(lineText))
                {
                    // ネストしたリージョンの終了
                    nestedLevel++;
                }
                else if (IsRegionStart(lineText))
                {
                    if (nestedLevel == 0)
                    {
                        // 対応する #region が見つかった
                        MoveCaretToLine(textView, i);
                        return;
                    }

                    // ネストしたリージョンの開始
                    nestedLevel--;
                }
            }

            // 対応する #region が見つからなかった
            ShowMessage("対応する #region が見つかりませんでした。");
        }

        /// <summary>
        /// 最も近い #region または #endregion を探して移動
        /// </summary>
        private static void FindAndJumpToNearestRegion(IWpfTextView textView, ITextSnapshotLine currentLine)
        {
            var snapshot = textView.TextSnapshot;
            var currentLineNumber = currentLine.LineNumber;
            var nearestRegionLineNumber = -1;
            var minDistance = int.MaxValue;

            // すべての行を検索して最も近い #region/#endregion を見つける
            for (var i = 0; i < snapshot.LineCount; i++)
            {
                var line = snapshot.GetLineFromLineNumber(i);
                var lineText = line.GetText();

                if (IsRegionStart(lineText) || IsRegionEnd(lineText))
                {
                    var distance = Math.Abs(i - currentLineNumber);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        nearestRegionLineNumber = i;
                    }
                }
            }

            if (nearestRegionLineNumber >= 0)
            {
                MoveCaretToLine(textView, nearestRegionLineNumber);
            }
            else
            {
                ShowMessage("ドキュメント内に #region/#endregion が見つかりませんでした。");
            }
        }

        /// <summary>
        /// 指定された行にカーソルを移動
        /// </summary>
        private static void MoveCaretToLine(IWpfTextView textView, int lineNumber)
        {
            var line = textView.TextSnapshot.GetLineFromLineNumber(lineNumber);

            // 行の先頭位置を取得
            var position = line.Start.Position;

            // 行頭の空白をスキップした位置を取得
            var lineText = line.GetText();
            var nonWhitespacePosition = position;
            for (var i = 0; i < lineText.Length; i++)
            {
                if (!char.IsWhiteSpace(lineText[i]))
                {
                    nonWhitespacePosition = position + i;
                    break;
                }
            }

            // カーソルを移動
            textView.Caret.MoveTo(new SnapshotPoint(textView.TextSnapshot, nonWhitespacePosition));

            // 選択範囲をクリア
            textView.Selection.Clear();

            // スクロールして表示
            textView.ViewScroller.EnsureSpanVisible(new SnapshotSpan(line.Start, line.End), EnsureSpanVisibleOptions.AlwaysCenter);
        }

        /// <summary>
        /// #region で始まるかチェック
        /// </summary>
        private static bool IsRegionStart(string line)
        {
            return Regex.IsMatch(line.TrimStart(), @"^#\s*region\b");
        }

        /// <summary>
        /// #endregion で始まるかチェック
        /// </summary>
        private static bool IsRegionEnd(string line)
        {
            return Regex.IsMatch(line.TrimStart(), @"^#\s*endregion\b");
        }

        /// <summary>
        /// コマンドの有効/無効状態を更新
        /// </summary>
        private static void BeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand command)
            {
                // DTEオブジェクトを取得
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));

                // アクティブなドキュメントがある場合のみ有効化
                command.Enabled = dte.ActiveDocument != null;
            }
        }

        /// <summary>
        /// 現在のテキストビューを取得
        /// </summary>
        private static IWpfTextView GetCurrentTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textManager = (IVsTextManager)ServiceProvider.GetServiceAsync(typeof(SVsTextManager)).Result;
            textManager.GetActiveView(1, null, out var textViewCurrent);

            var componentModel = (IComponentModel)ServiceProvider.GetServiceAsync(typeof(SComponentModel)).Result;
            var editor = componentModel.GetService<IVsEditorAdaptersFactoryService>();

            return editor.GetWpfTextView(textViewCurrent);
        }

        /// <summary>
        /// メッセージを表示
        /// </summary>
        private static void ShowMessage(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = (DTE)Package.GetGlobalService(typeof(DTE));
            dte.StatusBar.Text = message;
        }
    }
}