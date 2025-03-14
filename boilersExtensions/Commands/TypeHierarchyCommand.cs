using boilersExtensions.Utils;
using boilersExtensions.ViewModels;
using boilersExtensions.Views;
using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;
using Package = Microsoft.VisualStudio.Shell.Package;
using TextSpan = Microsoft.CodeAnalysis.Text.TextSpan;

namespace boilersExtensions.Commands
{
    /// <summary>
    /// 型階層選択コマンド
    /// </summary>
    internal sealed class TypeHierarchyCommand : OleMenuCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("c92b03a4-fb47-4d9d-8ab2-40d27c61788c");
        private static AsyncPackage package;
        private static OleMenuCommand menuItem;

        public static TypeHierarchyCommand Instance { get; private set; }
        private static IAsyncServiceProvider ServiceProvider => package;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        private TypeHierarchyCommand() : base(Execute, BeforeQueryStatus, new CommandID(CommandSet, CommandId))
        {
        }

        /// <summary>
        /// 初期化
        /// </summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            TypeHierarchyCommand.package = package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            menuItem = Instance = new TypeHierarchyCommand();
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
                if (textView == null) return;

                // カーソル位置を取得
                var caretPosition = textView.Caret.Position.BufferPosition;
                if (caretPosition == null) return;

                // ダブルクリックされた単語の範囲を取得
                var selectedSpan = GetSelectedWordSpan(textView);
                if (selectedSpan == null || selectedSpan.Value.IsEmpty) return;

                // ドキュメントを取得
                var document = GetDocumentFromTextView(textView);
                if (document == null) return;

                // 型解析と型階層ダイアログの表示を非同期で実行
                Task.Run(async () =>
                {
                    try
                    {
                        // カーソル位置の型シンボルを取得
                        var (typeSymbol, parentNode, fullTypeSpan, baseTypeSpan) = await TypeHierarchyAnalyzer.GetTypeSymbolAtPositionAsync(
                            document, selectedSpan.Value.Start.Position);

                        if (typeSymbol == null)
                            return;

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        // 置換用の適切なスパンを決定
                        SnapshotSpan replacementSpan;

                        // 基本型名のスパンがあればそれを使用
                        if (baseTypeSpan.HasValue)
                        {
                            // 基本型名部分のスパンを使用
                            var start = baseTypeSpan.Value.Start;
                            var length = baseTypeSpan.Value.Length;

                            // スパン情報をデバッグ出力
                            //System.Diagnostics.Debug.WriteLine($"Using base type span: Start={start}, Length={length}, Text={document.GetSyntaxRootAsync().Result.ToString().Substring(start, length)}");

                            // SnapshotSpanに変換
                            replacementSpan = new SnapshotSpan(
                                textView.TextSnapshot,
                                new Span(start, length));
                        }
                        else
                        {
                            // 選択されたスパンを使用
                            replacementSpan = selectedSpan.Value;
                            System.Diagnostics.Debug.WriteLine($"Using selected span: {selectedSpan.Value.GetText()}");
                        }

                        // ダイアログを表示（完全な型スパン情報も追加）
                        ShowTypeHierarchyDialog(typeSymbol, document, selectedSpan.Value.Start.Position, replacementSpan, textView.TextBuffer, fullTypeSpan);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in Execute: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Execute: {ex.Message}");
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
                // DTEオブジェクトを取得
                DTE dte = (DTE)Package.GetGlobalService(typeof(DTE));

                // アクティブなドキュメントがある場合のみ有効化
                command.Enabled = dte.ActiveDocument != null;
            }
        }

        /// <summary>
        /// 型階層ダイアログを表示
        /// </summary>
        private static void ShowTypeHierarchyDialog(
            ITypeSymbol typeSymbol, Microsoft.CodeAnalysis.Document document, int position, SnapshotSpan selectedSpan, ITextBuffer textBuffer, TextSpan fullTypeSpan)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // ダイアログを作成
                var window = new TypeHierarchyDialog();
                var viewModel = new TypeHierarchyDialogViewModel
                {
                    Package = package
                };
                window.DataContext = viewModel;

                // ダイアログを初期化
                viewModel.OnDialogOpened(window);

                // 非同期で型階層を初期化
                Task.Run(async () =>
                {
                    await viewModel.InitializeAsync(typeSymbol, document, position, selectedSpan, textBuffer, fullTypeSpan);
                });

                // ダイアログを表示
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ShowTypeHierarchyDialog: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のテキストビューを取得
        /// </summary>
        private static IWpfTextView GetCurrentTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textManager = (IVsTextManager)ServiceProvider.GetServiceAsync(typeof(SVsTextManager)).Result;
            textManager.GetActiveView(1, null, out IVsTextView textViewCurrent);

            IComponentModel componentModel = (IComponentModel)ServiceProvider.GetServiceAsync(typeof(SComponentModel)).Result;
            var editor = componentModel.GetService<IVsEditorAdaptersFactoryService>();

            return editor.GetWpfTextView(textViewCurrent);
        }

        /// <summary>
        /// テキストビューからドキュメントを取得
        /// </summary>
        private static Microsoft.CodeAnalysis.Document GetDocumentFromTextView(IWpfTextView textView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // ComponentModelサービスを取得
            var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
            var workspace = componentModel.GetService<VisualStudioWorkspace>();

            // アクティブなドキュメントのパスを取得
            var dte = (DTE)Package.GetGlobalService(typeof(DTE));
            var documentPath = dte.ActiveDocument.FullName;

            // 対応するドキュメントを検索
            var documents = workspace.CurrentSolution.Projects.SelectMany(p => p.Documents);
            foreach (var document in documents)
            {
                if (document.FilePath == documentPath)
                {
                    return document;
                }
            }

            return null;
        }

        /// <summary>
        /// 選択されたワードの範囲を取得
        /// </summary>
        private static SnapshotSpan? GetSelectedWordSpan(IWpfTextView textView)
        {
            if (textView.Selection.IsEmpty)
            {
                // 選択がない場合はカーソル位置のワードを取得
                var caretPosition = textView.Caret.Position.BufferPosition;

                // IComponentModelサービスを取得
                ThreadHelper.ThrowIfNotOnUIThread();
                var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
                var navigatorService = componentModel.GetService<ITextStructureNavigatorSelectorService>();

                // テキスト構造ナビゲータを取得
                var navigator = navigatorService.GetTextStructureNavigator(textView.TextBuffer);

                // カーソル位置のワード範囲を取得
                var extent = navigator.GetExtentOfWord(caretPosition);
                if (extent.IsSignificant)
                {
                    return extent.Span;
                }

                return null;
            }
            else
            {
                // 選択範囲がある場合はそれを使用
                return textView.Selection.StreamSelectionSpan.SnapshotSpan;
            }
        }


    }
}