using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows;
using boilersExtensions.Utils;
using boilersExtensions.Views;
using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Prism.Mvvm;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Document = Microsoft.CodeAnalysis.Document;
using Window = System.Windows.Window;

namespace boilersExtensions.ViewModels
{
    /// <summary>
    ///     型階層選択ダイアログのViewModel
    /// </summary>
    internal class TypeHierarchyDialogViewModel : BindableBase, IDisposable
    {
        private readonly CompositeDisposable _compositeDisposable = new CompositeDisposable();
        private Document _document; 
        private IVsWindowFrame _diffWindowFrame;

        // 完全な型スパン情報
        private TextSpan _fullTypeSpan;

        // 置換対象の情報
        private ITypeSymbol _originalTypeSymbol;
        private int _position;
        private ITextBuffer _textBuffer;
        private SnapshotSpan _typeSpan;

        public TypeHierarchyDialogViewModel()
        {
            // 選択されている型があれば、適用ボタンを有効化
            ApplyCommand = SelectedType.Select(st => st != null).ToReactiveCommand();

            // 型変更の適用
            ApplyCommand.Subscribe(async () =>
                {
                    if (SelectedType.Value == null)
                    {
                        return;
                    }

                    try
                    {
                        // 処理開始
                        IsProcessing.Value = true;
                        ProcessingStatus.Value = "型を更新中...";

                        await ApplyTypeChange();

                        // ダイアログを閉じる
                        Window.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"型の置換中にエラーが発生しました: {ex.Message}",
                            "型階層選択エラー",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    finally
                    {
                        // 処理終了
                        IsProcessing.Value = false;
                    }
                })
                .AddTo(_compositeDisposable);

            // キャンセル処理
            CancelCommand.Subscribe(() =>
                {
                    // Diffウィンドウが開いていれば閉じる
                    if (_diffWindowFrame != null)
                    {
                        _diffWindowFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
                        _diffWindowFrame = null;
                    }

                    Window.Close();
                })
                .AddTo(_compositeDisposable);

            PreviewCommand = SelectedType.Select(st => st != null && st.FullName != _originalTypeSymbol.ToDisplayString())
                .ToReactiveCommand();

            PreviewCommand.Subscribe(async () =>
                {
                    // Diffウィンドウが開いていれば閉じる
                    if (_diffWindowFrame != null)
                    {
                        _diffWindowFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
                        _diffWindowFrame = null;
                    }

                    await ShowTypeChangePreview();
                })
                .AddTo(_compositeDisposable);

            AnalyzeImpactCommand = SelectedType.Select(st => st != null && st.FullName != _originalTypeSymbol?.ToDisplayString())
                .ToReactiveCommand();

            AnalyzeImpactCommand.Subscribe(async () =>
                {
                    await ShowImpactAnalysis();
                })
                .AddTo(_compositeDisposable);

            // 選択された型が変更されたらDiffプレビューを更新
            SelectedType.Subscribe(async selectedType =>
                {
                    // 選択されている型があり、元の型と異なる場合のみプレビュー表示
                    if (selectedType != null && _originalTypeSymbol != null &&
                        selectedType.FullName != _originalTypeSymbol.ToDisplayString())
                    {
                        // Diffウィンドウが開いていれば閉じる
                        if (_diffWindowFrame != null)
                        {
                            _diffWindowFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
                            _diffWindowFrame = null;
                        }

                        await ShowTypeChangePreview();
                    }
                    else if (_diffWindowFrame != null)
                    {
                        // 選択が元の型に戻った場合やnullになった場合はDiffウィンドウを閉じるだけ
                        _diffWindowFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
                        _diffWindowFrame = null;
                    }
                })
                .AddTo(_compositeDisposable);

            // 表示モード変更時に候補を再取得
            ShowBaseTypes.CombineLatest(ShowDerivedTypes, (b, d) => true)
                .Subscribe(async _ => await RefreshTypeCandidates())
                .AddTo(_compositeDisposable);
        }

        // コマンド
        public ReactiveCommand ApplyCommand { get; }
        public ReactiveCommand CancelCommand { get; } = new ReactiveCommand();
        public ReactiveCommand PreviewCommand { get; }
        public ReactiveCommand AnalyzeImpactCommand { get; }

        // プロパティ
        public ReactivePropertySlim<string> OriginalTypeName { get; } = new ReactivePropertySlim<string>();

        public ReactivePropertySlim<List<TypeHierarchyAnalyzer.TypeHierarchyInfo>> TypeCandidates { get; }
            = new ReactivePropertySlim<List<TypeHierarchyAnalyzer.TypeHierarchyInfo>>();

        public ReactivePropertySlim<TypeHierarchyAnalyzer.TypeHierarchyInfo> SelectedType { get; }
            = new ReactivePropertySlim<TypeHierarchyAnalyzer.TypeHierarchyInfo>();

        // 表示モード
        public ReactivePropertySlim<bool> ShowBaseTypes { get; } = new ReactivePropertySlim<bool>(true);
        public ReactivePropertySlim<bool> ShowDerivedTypes { get; } = new ReactivePropertySlim<bool>(true);
        public ReactivePropertySlim<bool> ShowUseSpecialTypes { get; } = new ReactivePropertySlim<bool>(true);


        // 処理中フラグ
        public ReactivePropertySlim<bool> IsProcessing { get; } = new ReactivePropertySlim<bool>();
        public ReactivePropertySlim<string> ProcessingStatus { get; } = new ReactivePropertySlim<string>("準備完了");

        // ウィンドウ参照
        public Window Window { get; set; }
        public AsyncPackage Package { get; set; }

        public string Title => "型階層選択";

        /// <summary>
        ///     リソース解放
        /// </summary>
        public void Dispose()
        {
            _compositeDisposable?.Dispose();
            ApplyCommand?.Dispose();
            CancelCommand?.Dispose();
            OriginalTypeName?.Dispose();
            TypeCandidates?.Dispose();
            SelectedType?.Dispose();
            ShowBaseTypes?.Dispose();
            ShowDerivedTypes?.Dispose();
            IsProcessing?.Dispose();
            ProcessingStatus?.Dispose();
        }

        /// <summary>
        /// 初期化
        /// </summary>
        public async Task InitializeAsync(ITypeSymbol typeSymbol, Document document, int position,
            SnapshotSpan typeSpan, ITextBuffer textBuffer, Microsoft.CodeAnalysis.Text.TextSpan fullTypeSpan)
        {
            try
            {
                _originalTypeSymbol = typeSymbol;
                _document = document;
                _position = position;
                _typeSpan = typeSpan;
                _textBuffer = textBuffer;
                _fullTypeSpan = fullTypeSpan;

                // 実際のコードの文字列を取得（これが元のコードでの型表記を正確に反映している）
                string actualTypeText = typeSpan.GetText();

                // デバッグ情報
                System.Diagnostics.Debug.WriteLine($"InitializeAsync: Original Type Symbol={typeSymbol.ToDisplayString()}");
                System.Diagnostics.Debug.WriteLine($"Actual Type Text='{actualTypeText}'");
                System.Diagnostics.Debug.WriteLine($"Type with special types={typeSymbol.ToDisplayString(new SymbolDisplayFormat(miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes))}");
                System.Diagnostics.Debug.WriteLine($"Type without special types={typeSymbol.ToDisplayString()}");
                System.Diagnostics.Debug.WriteLine($"Type Span: '{typeSpan.GetText()}', Full Type Span: Start={fullTypeSpan.Start}, Length={fullTypeSpan.Length}");

                // 元の型名を表示
                OriginalTypeName.Value = string.IsNullOrEmpty(typeSymbol.ContainingNamespace.ToString()) && actualTypeText.Contains(typeSymbol.ContainingNamespace.ToString())
                    ? actualTypeText
                    : $"{typeSymbol.ContainingNamespace}.{actualTypeText}";

                // 実際のコードの表記に基づいてフォーマットを判定
                bool usePrimitiveTypes = DeterminePrimitiveTypeUsage(actualTypeText);

                // デバッグ出力
                System.Diagnostics.Debug.WriteLine($"Using primitive types: {usePrimitiveTypes}");

                // 型候補リストを再取得（プリミティブ型の使用有無を設定）
                ShowUseSpecialTypes.Value = usePrimitiveTypes;

                // 型の候補を取得
                await RefreshTypeCandidates();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Initialize: {ex.Message}");
            }
        }

        /// <summary>
        /// 実際のコードの表記からプリミティブ型が使用されているかを判定
        /// </summary>
        private bool DeterminePrimitiveTypeUsage(string actualTypeText)
        {
            // プリミティブ型の対応表
            var primitiveTypes = new Dictionary<string, string>
            {
                { "System.Int32", "int" },
                { "System.Int64", "long" },
                { "System.Single", "float" },
                { "System.Double", "double" },
                { "System.Boolean", "bool" },
                { "System.String", "string" },
                { "System.Char", "char" },
                { "System.Byte", "byte" },
                { "System.SByte", "sbyte" },
                { "System.Int16", "short" },
                { "System.UInt16", "ushort" },
                { "System.UInt32", "uint" },
                { "System.UInt64", "ulong" },
                { "System.Decimal", "decimal" },
                { "System.Object", "object" }
            };

            // まず、プリミティブ型（int など）が含まれているかチェック
            foreach (var primitiveType in primitiveTypes.Values)
            {
                // ジェネリック型パラメータとして現れる可能性のあるパターン
                if (actualTypeText.Contains($"<{primitiveType}>") ||
                    actualTypeText.Contains($"<{primitiveType},") ||
                    actualTypeText.Contains($", {primitiveType}>") ||
                    actualTypeText.Contains($", {primitiveType},"))
                {
                    return true; // プリミティブ型表記を使用
                }
            }

            // 次に、.NET型（System.Int32 など）が含まれているかチェック
            foreach (var netType in primitiveTypes.Keys)
            {
                string shortNetType = netType.Substring(netType.LastIndexOf('.') + 1); // "Int32" など
                if (actualTypeText.Contains($"<{shortNetType}>") ||
                    actualTypeText.Contains($"<{shortNetType},") ||
                    actualTypeText.Contains($", {shortNetType}>") ||
                    actualTypeText.Contains($", {shortNetType},"))
                {
                    return false; // .NET型表記を使用
                }
            }

            // デフォルトではプリミティブ型表記を使用
            return true;
        }

        /// <summary>
        ///     型候補のリストを更新
        /// </summary>
        private async Task RefreshTypeCandidates()
        {
            if (_originalTypeSymbol == null || _document == null)
            {
                return;
            }

            try
            {
                IsProcessing.Value = true;
                ProcessingStatus.Value = "型の階層を分析中...";

                // 型の階層を取得
                var candidates = await TypeHierarchyAnalyzer.GetTypeReplacementCandidatesAsync(
                    _originalTypeSymbol,
                    _document,
                    ShowBaseTypes.Value,
                    ShowDerivedTypes.Value,
                    ShowUseSpecialTypes.Value);

                // 候補を設定
                TypeCandidates.Value = candidates;

                // 現在の型を選択状態にする
                SelectedType.Value =
                    candidates.FirstOrDefault(t => t.FullName == _originalTypeSymbol.ToDisplayString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in RefreshTypeCandidates: {ex.Message}");
            }
            finally
            {
                IsProcessing.Value = false;
                ProcessingStatus.Value = "準備完了";
            }
        }

        /// <summary>
        ///     型の変更を適用
        /// </summary>
        private async Task ApplyTypeChange()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Diffウィンドウが開いていれば閉じる
            if (_diffWindowFrame != null)
            {
                _diffWindowFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
                _diffWindowFrame = null;
            }

            // 選択された型が現在の型と同じなら何もしない
            if (SelectedType.Value.FullName == _originalTypeSymbol.ToDisplayString())
            {
                return;
            }

            // DTEのUndoContextを開始
            var dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
            dte.UndoContext.Open("Type Replacement");

            try
            {
                // 元の型名のスパンを取得
                var originalTypeSpan = _typeSpan;

                // 型名を置換
                var newTypeName = GetSimplifiedTypeName(SelectedType.Value.DisplayName, originalTypeSpan.GetText());
                Debug.WriteLine($"Replacing type: '{originalTypeSpan.GetText()}' with '{newTypeName}'");

                // テキストを置換
                _textBuffer.Replace(originalTypeSpan.Span, newTypeName);

                // 必要に応じてusing文を追加
                await AddRequiredUsingDirectiveAsync();
            }
            finally
            {
                // UndoContextを閉じる
                dte.UndoContext.Close();
            }
        }

        /// <summary>
        /// 表示用に型名を簡略化
        /// </summary>
        private string GetSimplifiedTypeName(string fullName, string originalTypeText)
        {
            try
            {
                if (string.IsNullOrEmpty(fullName))
                    return string.Empty;

                // ジェネリック型かどうか確認
                if (fullName.Contains("<"))
                {
                    int genericStart = fullName.IndexOf('<');
                    int originalGenericStart = originalTypeText.IndexOf('<');

                    // ジェネリック部分を抽出 (例: System.Collections.Generic.List<int> -> System.Collections.Generic.List と <int>)
                    string baseTypeName = fullName.Substring(0, genericStart);
                    string typeParams = fullName.Substring(genericStart); // <int> 部分
                    string originalTypeParams = originalTypeText.Substring(originalGenericStart); // <int> 部分

                    // 型パラメーターの数が異なる場合は元の型名をそのまま返す
                    if (typeParams.Count(x => x == ',') != originalTypeParams.Count(x => x == ','))
                    {
                        MessageBox.Show("型パラメーターの数に互換がないため、型パラメーターにプレースホルダーを設定します。",
                            "警告",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return fullName;
                    }

                    // 名前空間を含まない型名を取得
                    int lastDot = baseTypeName.LastIndexOf('.');
                    if (lastDot >= 0)
                    {
                        baseTypeName = baseTypeName.Substring(lastDot + 1);
                    }

                    // 名前空間なしの型名 + 元のジェネリックパラメーター
                    return baseTypeName + originalTypeParams;
                }
                else
                {
                    // 非ジェネリック型
                    int lastDot = fullName.LastIndexOf('.');
                    if (lastDot >= 0)
                    {
                        return fullName.Substring(lastDot + 1);
                    }
                    return fullName;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetSimplifiedTypeName: {ex.Message}");
                return fullName; // エラー時は元の型名をそのまま返す
            }
        }

        /// <summary>
        ///     必要に応じてusing文を追加
        /// </summary>
        private async Task AddRequiredUsingDirectiveAsync()
        {
            if (SelectedType.Value == null || string.IsNullOrEmpty(SelectedType.Value.RequiredNamespace))
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // ドキュメントのルートを取得
                var syntaxRoot = await _document.GetSyntaxRootAsync();
                if (syntaxRoot == null)
                {
                    return;
                }

                // 必要な名前空間
                var requiredNamespace = SelectedType.Value.RequiredNamespace;

                // 既存のusing文をチェック
                var existingUsings = syntaxRoot.DescendantNodes()
                    .OfType<UsingDirectiveSyntax>()
                    .Select(u => u.Name.ToString())
                    .ToList();

                // すでに追加されている場合は何もしない
                if (existingUsings.Contains(requiredNamespace))
                {
                    return;
                }

                // 必要なusing文を作成
                var newUsing = SyntaxFactory.UsingDirective(
                        SyntaxFactory.ParseName(requiredNamespace))
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                // 既存のusing文の後に追加
                var firstUsing = syntaxRoot.DescendantNodes().OfType<UsingDirectiveSyntax>().FirstOrDefault();
                if (firstUsing != null)
                {
                    syntaxRoot = syntaxRoot.InsertNodesAfter(
                        firstUsing,
                        new[] { newUsing });
                }
                else
                {
                    // usingがない場合は先頭に追加
                    syntaxRoot = syntaxRoot.InsertNodesBefore(
                        syntaxRoot.DescendantNodes().First(),
                        new[] { newUsing });
                }

                // ドキュメントを更新
                var newDocument = _document.WithSyntaxRoot(syntaxRoot);
                var workspace = _document.Project.Solution.Workspace;
                var success = workspace.TryApplyChanges(newDocument.Project.Solution);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in AddRequiredUsingDirectiveAsync: {ex.Message}");
            }
        }

        public async Task ShowTypeChangePreview()
        {
            try
            {
                if (SelectedType.Value == null || _document == null)
                {
                    return;
                }

                IsProcessing.Value = true;
                ProcessingStatus.Value = "コード変更をプレビュー中...";

                // 現在のドキュメントのテキストを取得
                var sourceText = await _document.GetTextAsync();
                var originalCode = sourceText.ToString();

                // 型を置換した新しいコードを生成
                var newTypeName = GetSimplifiedTypeName(SelectedType.Value.DisplayName, _typeSpan.GetText());

                // 置換後のテキストを作成
                var start = _typeSpan.Span.Start;
                var end = _typeSpan.Span.End;
                var newCode = originalCode.Substring(0, start) +
                              newTypeName +
                              originalCode.Substring(end);

                // DiffViewerを使って差分を表示
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var diffViewer = new DiffViewer();
                _diffWindowFrame = diffViewer.ShowDiff(originalCode, newCode, true, caption: "型変更のプレビュー", tooltip: "型変更を適用するか検討してください");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ShowTypeChangePreview: {ex.Message}");
                MessageBox.Show($"プレビューの表示中にエラーが発生しました: {ex.Message}",
                    "プレビューエラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing.Value = false;
                ProcessingStatus.Value = "準備完了";
            }
        }

        /// <summary>
        ///     ダイアログが開かれた時の処理
        /// </summary>
        public void OnDialogOpened(Window window) => Window = window;

        public void OnDialogClosing(TypeHierarchyDialog typeHierarchyDialog)
        {
            // Diffウィンドウが開いていれば閉じる
            if (_diffWindowFrame != null)
            {
                _diffWindowFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
                _diffWindowFrame = null;
            }
        }

        #region 影響範囲分析

        public async Task ShowImpactAnalysis()
        {
            try
            {
                IsProcessing.Value = true;
                ProcessingStatus.Value = "影響範囲を分析中...";

                // 選択された型のシンボルを取得
                if (_originalTypeSymbol == null || _document == null)
                    return;

                // 変更対象の変数/パラメータを特定
                // シンタックスツリーから変更対象のノードを探す
                var syntaxRoot = await _document.GetSyntaxRootAsync();
                var semanticModel = await _document.GetSemanticModelAsync();

                // カーソル位置のパラメータシンボルを特定
                var nodeAtPosition = syntaxRoot.FindNode(new TextSpan(_position, 0), getInnermostNodeForTie: true);

                // 変数/パラメータ宣言ノードを探す
                var parameterNode = nodeAtPosition.AncestorsAndSelf()
                    .OfType<ParameterSyntax>()
                    .FirstOrDefault();

                // パラメータが見つからない場合は変数宣言を探す
                if (parameterNode == null)
                {
                    var variableNode = nodeAtPosition.AncestorsAndSelf()
                        .OfType<VariableDeclaratorSyntax>()
                        .FirstOrDefault();

                    if (variableNode != null)
                    {
                        // 変数のシンボルを取得
                        var variableSymbol = semanticModel.GetDeclaredSymbol(variableNode) as ILocalSymbol;
                        if (variableSymbol != null)
                        {
                            await ShowImpactForSymbol(variableSymbol);
                            return;
                        }
                    }
                }
                else
                {
                    // パラメータのシンボルを取得
                    var parameterSymbol = semanticModel.GetDeclaredSymbol(parameterNode) as IParameterSymbol;
                    if (parameterSymbol != null)
                    {
                        await ShowImpactForSymbol(parameterSymbol);
                        return;
                    }
                }

                // 特定の変数/パラメータが見つからない場合、
                // 型全体に対する参照検索を行う（こちらは過剰検出につながるため避けるべき）
                MessageBox.Show("特定のパラメータや変数が見つかりませんでした。型全体に対する参照を検索します。",
                               "警告",
                               MessageBoxButton.OK,
                               MessageBoxImage.Warning);

                await ShowImpactForSymbol(_originalTypeSymbol);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ShowImpactAnalysis: {ex.Message}");
                MessageBox.Show($"影響範囲の分析中にエラーが発生しました: {ex.Message}",
                               "エラー",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing.Value = false;
                ProcessingStatus.Value = "準備完了";
            }
        }

        private async Task ShowImpactForSymbol(ISymbol symbol)
        {
            var solution = _document.Project.Solution;
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution);

            // 参照の一覧を構築
            var impactList = new List<TypeReferenceInfo>();

            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    var sourceTree = await location.Document.GetSyntaxTreeAsync();
                    var lineSpan = sourceTree.GetLineSpan(location.Location.SourceSpan);
                    var line = lineSpan.StartLinePosition.Line + 1;

                    // メソッド内での参照かどうかを判定
                    var semanticModel = await location.Document.GetSemanticModelAsync();
                    var node = (await sourceTree.GetRootAsync()).FindNode(location.Location.SourceSpan);
                    var containingMethod = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                    string methodContext = containingMethod != null ? containingMethod.Identifier.Text : "不明";

                    var referenceInfo = new TypeReferenceInfo
                    {
                        FilePath = location.Document.FilePath,
                        FileName = Path.GetFileName(location.Document.FilePath),
                        LineNumber = line,
                        Column = lineSpan.StartLinePosition.Character + 1,
                        Text = await GetLineTextAsync(location.Document, lineSpan.StartLinePosition.Line),
                        ReferenceType = $"{(symbol is IParameterSymbol ? "パラメータ" : "変数")}の使用 ({methodContext}内)"
                    };

                    // ブックマークの初期状態を確認
                    await CheckBookmarkStatusAsync(referenceInfo);

                    impactList.Add(referenceInfo);
                }


            }

            // 影響範囲ダイアログを表示
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dialog = new ImpactAnalysisDialog
            {
                DataContext = new ImpactAnalysisViewModel
                {
                    OriginalTypeName = _originalTypeSymbol.ToDisplayString(),
                    NewTypeName = SelectedType.Value.DisplayName,
                    ReferencesCount = impactList.Count,
                    References = impactList
                }
            };

            (dialog.DataContext as ImpactAnalysisViewModel).OnDialogOpened(dialog);
            dialog.Show();
        }

        // ブックマークの状態をチェックするメソッド
        private async Task CheckBookmarkStatusAsync(TypeReferenceInfo reference)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null)
                    return;

                // この方法では既存のブックマーク状態を直接確認できないため、
                // 初期状態は全てfalseとし、UIの操作で変更していく方針としています
                reference.IsBookmarked.Value = false;

                // 注：既存のブックマーク状態を取得するためには、
                // Visual Studio拡張APIの中でBookmark関連のサービスを
                // 利用する必要があります。より高度なソリューションが必要な場合は
                // IBookmarkServiceやIVsBookmarkServiceなどを検討してください。
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking bookmark status: {ex.Message}");
            }
        }

        private async Task<string> GetLineTextAsync(Document document, int lineNumber)
        {
            var text = await document.GetTextAsync();
            var line = text.Lines[lineNumber];
            return line.ToString();
        }

        public async Task<ImpactAnalysisResult> AnalyzeCompatibility()
        {
            var result = new ImpactAnalysisResult
            {
                OriginalType = _originalTypeSymbol.ToDisplayString(),
                NewType = SelectedType.Value.DisplayName,
                TotalReferences = 0,
                PotentialIssues = new List<PotentialIssue>()
            };

            var solution = _document.Project.Solution;
            var references = await SymbolFinder.FindReferencesAsync(_originalTypeSymbol, solution);

            foreach (var reference in references)
            {
                result.TotalReferences += reference.Locations.Count();

                foreach (var location in reference.Locations)
                {
                    // 参照箇所のシンタックスノードを取得
                    var syntaxTree = await location.Document.GetSyntaxTreeAsync();
                    var semanticModel = await location.Document.GetSemanticModelAsync();
                    var node = await syntaxTree.GetRootAsync();
                    var referenceNode = node.FindNode(location.Location.SourceSpan);

                    // 互換性チェック
                    var issues = CheckCompatibility(referenceNode, semanticModel, _originalTypeSymbol, SelectedType.Value);

                    if (issues.Any())
                    {
                        result.PotentialIssues.AddRange(issues);
                    }
                }
            }

            return result;
        }

        private List<PotentialIssue> CheckCompatibility(SyntaxNode node, SemanticModel semanticModel,
            ITypeSymbol originalType, TypeHierarchyAnalyzer.TypeHierarchyInfo newType)
        {
            var issues = new List<PotentialIssue>();

            // メソッドコールの場合
            if (node.Parent is InvocationExpressionSyntax invocation)
            {
                // 呼び出しメソッドの解析
                // 新しい型に同名のメソッドが存在するか、引数の互換性など
            }

            // プロパティアクセスの場合
            if (node.Parent is MemberAccessExpressionSyntax memberAccess)
            {
                // 新しい型に同名のプロパティが存在するかなど
            }

            // その他、型の使用状況に応じたチェック
            // ...

            return issues;
        }

        public class ImpactAnalysisResult
        {
            public string OriginalType { get; set; }
            public string NewType { get; set; }
            public int TotalReferences { get; set; }
            public List<PotentialIssue> PotentialIssues { get; set; }
        }

        public class PotentialIssue
        {
            public string FilePath { get; set; }
            public int LineNumber { get; set; }
            public string IssueType { get; set; } // MethodMissing, PropertyMissing など
            public string Description { get; set; }
            public string SuggestedFix { get; set; }
        }

        #endregion
    }
}