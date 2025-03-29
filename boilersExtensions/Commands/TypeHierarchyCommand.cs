using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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
using ZLinq;
using Document = Microsoft.CodeAnalysis.Document;
using Package = Microsoft.VisualStudio.Shell.Package;
using TextSpan = Microsoft.CodeAnalysis.Text.TextSpan;

namespace boilersExtensions.Commands
{
    /// <summary>
    ///     型階層選択コマンド
    /// </summary>
    internal sealed class TypeHierarchyCommand : OleMenuCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("c92b03a4-fb47-4d9d-8ab2-40d27c61788c");
        private static AsyncPackage package;
        private static OleMenuCommand menuItem;

        /// <summary>
        ///     コンストラクタ
        /// </summary>
        private TypeHierarchyCommand() : base(Execute, new CommandID(CommandSet, CommandId)) =>
            base.BeforeQueryStatus += BeforeQueryStatus;

        public static TypeHierarchyCommand Instance { get; private set; }
        private static IAsyncServiceProvider ServiceProvider => package;

        /// <summary>
        ///     初期化
        /// </summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            TypeHierarchyCommand.package = package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            menuItem = Instance = new TypeHierarchyCommand();
            menuItem.Text = Resources.ResourceService.GetString("ChangeTypeFromTypeHierarchy");
            commandService.AddCommand(Instance);
        }

        /// <summary>
        ///     コマンド実行時の処理
        /// </summary>
        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // 設定が無効な場合は何もしない
                if (!BoilersExtensionsSettings.IsTypeHierarchyEnabled)
                {
                    Debug.WriteLine("TypeHierarchy feature is disabled in settings");
                    return;
                }

                // 現在のテキストビューを取得
                var textView = GetCurrentTextView();
                if (textView == null)
                {
                    return;
                }

                // カーソル位置を取得
                var caretPosition = textView.Caret.Position.BufferPosition;
                if (caretPosition == null)
                {
                    return;
                }

                // ダブルクリックされた単語の範囲を取得
                var selectedSpan = GetSelectedWordSpan(textView);
                if (selectedSpan == null || selectedSpan.Value.IsEmpty)
                {
                    return;
                }

                // ドキュメントを取得
                var document = GetDocumentFromTextView(textView);

                // 型解析と型階層ダイアログの表示を非同期で実行
                Task.Run(async () =>
                {
                    try
                    {
                        // カーソル位置の型シンボルを取得
                        var (typeSymbol, parentNode, fullTypeSpan, baseTypeSpan, code, codeToRazorMapping,
                                adjustedAddedBytes) =
                            await TypeHierarchyAnalyzer.GetTypeSymbolAtPositionAsync(
                                document, selectedSpan.Value.Start.Position);

                        if (typeSymbol == null)
                        {
                            return;
                        }

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        // 置換用の適切なスパンを決定
                        SnapshotSpan replacementSpan = default;

                        // 基本型名のスパンがあればそれを使用
                        if (baseTypeSpan.HasValue)
                        {
                            // 基本型名部分のスパンを使用
                            var start = baseTypeSpan.Value.Start;
                            var length = baseTypeSpan.Value.Length;

                            // スパン情報をデバッグ出力
                            //System.Diagnostics.Debug.WriteLine($"Using base type span: Start={start}, Length={length}, Text={document.GetSyntaxRootAsync().Result.ToString().Substring(start, length)}");

                            // アクティブなドキュメントのパスを取得
                            var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                            var documentPath = dte.ActiveDocument.FullName;

                            // Razorファイルの場合はRazor言語サービスを使用して直接処理
                            var isRazorFile = documentPath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                                              documentPath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase);

                            // C#コードとRazorファイル間のマッピングを使用する方法
                            if (isRazorFile && codeToRazorMapping != null) // マッピング情報が利用可能と仮定
                            {
                                // C#コード内での位置
                                var csharpPosition = fullTypeSpan.Start;

                                // C#コード内での位置からその行番号を特定する
                                var csharpLine = 0;
                                if (code != null)
                                {
                                    // コード内での行番号を計算
                                    var lines = code.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                                    var charCount = 0;
                                    for (var i = 0; i < lines.Length; i++)
                                    {
                                        var lineLength = lines[i].Length + Environment.NewLine.Length;
                                        if (charCount + lineLength > csharpPosition)
                                        {
                                            csharpLine = i;
                                            break;
                                        }

                                        charCount += lineLength;
                                    }

                                    // ファイルの内容を直接読み込む
                                    var razorContent = File.ReadAllText(documentPath);

                                    // マッピング情報を使用してRazorファイル内の対応する位置を取得
                                    if (codeToRazorMapping.TryGetValue(csharpLine, out var razorPosition))
                                    {
                                        // 行内の相対的な位置を計算（必要に応じて）
                                        var offsetInLine = csharpPosition - charCount;

                                        // 型名を取得
                                        var typeName = typeSymbol.Name;

                                        // 位置周辺のテキストをチェックして正確な型名の位置を特定
                                        // razorPosition付近で型名を探す
                                        var exactPosition =
                                            FindExactTypePosition(razorContent, razorPosition, typeName);

                                        if (exactPosition >= 0)
                                        {
                                            // テキストビューのSpanではなく、実際のファイル内容に基づいて置換対象を決定
                                            replacementSpan = new SnapshotSpan(
                                                textView.TextSnapshot,
                                                new Span(exactPosition, typeName.Length));
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // 通常のC#ファイル用の既存の処理
                                replacementSpan = new SnapshotSpan(
                                    textView.TextSnapshot,
                                    new Span(start, length));
                            }
                        }
                        else
                        {
                            // 選択されたスパンを使用
                            replacementSpan = selectedSpan.Value;
                            Debug.WriteLine($"Using selected span: {selectedSpan.Value.GetText()}");
                        }

                        // ダイアログを表示（完全な型スパン情報も追加）
                        ShowTypeHierarchyDialog(typeSymbol, document, selectedSpan.Value.Start.Position,
                            replacementSpan, textView.TextBuffer, fullTypeSpan, code, codeToRazorMapping,
                            adjustedAddedBytes);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in Execute: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Execute: {ex.Message}");
            }
        }

        // 型名の正確な位置を特定するヘルパーメソッド
        private static int FindExactTypePosition(string content, int approximatePosition, string typeName)
        {
            // 検索範囲を制限（前後100文字程度）
            var searchStart = Math.Max(0, approximatePosition - 100);
            var searchEnd = Math.Min(content.Length, approximatePosition + 100);
            var searchArea = content.Substring(searchStart, searchEnd - searchStart);

            // 型名を検索
            var relativePos = searchArea.IndexOf(typeName);
            if (relativePos >= 0)
            {
                return searchStart + relativePos;
            }

            // 見つからない場合はより広い範囲で検索
            searchStart = Math.Max(0, approximatePosition - 500);
            searchEnd = Math.Min(content.Length, approximatePosition + 500);
            searchArea = content.Substring(searchStart, searchEnd - searchStart);

            relativePos = searchArea.IndexOf(typeName);
            if (relativePos >= 0)
            {
                return searchStart + relativePos;
            }

            // それでも見つからない場合は元の位置を返す
            return approximatePosition;
        }

        /// <summary>
        ///     コマンドの有効/無効状態を更新
        /// </summary>
        private static void BeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand command)
            {
                // 設定で無効化されているかチェック
                var featureEnabled = BoilersExtensionsSettings.IsTypeHierarchyEnabled;

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
                command.Enabled = dte.ActiveDocument != null;
            }
        }

        /// <summary>
        ///     型階層ダイアログを表示
        /// </summary>
        private static void ShowTypeHierarchyDialog(ITypeSymbol typeSymbol, Document document, int position,
            SnapshotSpan selectedSpan, ITextBuffer textBuffer,
            TextSpan fullTypeSpan, string code, Dictionary<int, int> codeToRazorMapping, int adjustedAddedBytes)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // アクティブなドキュメントのパスを取得
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                var documentPath = dte.ActiveDocument.FullName;

                // Razorファイルの場合はRazor言語サービスを使用して直接処理
                var isRazorFile = documentPath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                                  documentPath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase);

                // ダイアログを作成
                var window = new TypeHierarchyDialog();
                var viewModel = new TypeHierarchyDialogViewModel { Package = package };
                window.DataContext = viewModel;

                // ダイアログを初期化
                viewModel.OnDialogOpened(window);

                // 非同期で型階層を初期化
                Task.Run(async () =>
                {
                    if (isRazorFile)
                    {
                        // Razorファイル専用の処理
                        // ここでは、解析されたC#コードとともに、元のファイルパスも渡す
                        var razorFilePath = documentPath;
                        await viewModel.InitializeRazorAsync(typeSymbol, document, position,
                            razorFilePath, code, fullTypeSpan, codeToRazorMapping, adjustedAddedBytes);
                    }
                    else
                    {
                        // 通常のC#ファイル用の既存処理
                        await viewModel.InitializeAsync(typeSymbol, document, position, selectedSpan, textBuffer,
                            fullTypeSpan);
                    }
                });

                // ダイアログを表示
                window.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ShowTypeHierarchyDialog: {ex.Message}");
            }
        }

        /// <summary>
        ///     現在のテキストビューを取得
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
        ///     テキストビューから元のRazorドキュメントを取得する
        /// </summary>
        private static Document GetDocumentFromTextView(IWpfTextView textView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // ComponentModelサービスを取得
            var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
            var workspace = componentModel.GetService<VisualStudioWorkspace>();

            // アクティブなドキュメントのパスを取得
            var dte = (DTE)Package.GetGlobalService(typeof(DTE));
            var documentPath = dte.ActiveDocument.FullName;

            // 実際のファイルパスをデバッグ出力
            Debug.WriteLine($"Active document path: {documentPath}");

            // まず、正確なパスマッチを試みる
            var documents = workspace.CurrentSolution.Projects.AsValueEnumerable()
                .SelectMany(p => p.Documents.AsValueEnumerable());
            var exactMatch = documents.FirstOrDefault(d =>
                string.Equals(d.FilePath, documentPath, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
            {
                Debug.WriteLine($"Found exact match: {exactMatch.FilePath}");
                return exactMatch;
            }

            Debug.WriteLine("Could not find matching document.");
            return null;
        }

        /// <summary>
        ///     選択されたワードの範囲を取得
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

            // 選択範囲がある場合はそれを使用
            return textView.Selection.StreamSelectionSpan.SnapshotSpan;
        }
    }
}