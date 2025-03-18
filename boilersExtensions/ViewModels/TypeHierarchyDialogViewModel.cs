using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using boilersExtensions.Utils;
using boilersExtensions.Views;
using EnvDTE;
using LibGit2Sharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using Prism.Mvvm;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Document = Microsoft.CodeAnalysis.Document;
using Solution = Microsoft.CodeAnalysis.Solution;
using TextDocument = EnvDTE.TextDocument;
using TextSpan = Microsoft.CodeAnalysis.Text.TextSpan;
using Window = System.Windows.Window;

namespace boilersExtensions.ViewModels
{
    /// <summary>
    ///     型階層選択ダイアログのViewModel
    /// </summary>
    internal class TypeHierarchyDialogViewModel : BindableBase, IDisposable
    {
        private readonly CompositeDisposable _compositeDisposable = new CompositeDisposable();
        private IVsWindowFrame _diffWindowFrame;
        private Document _document;
        private string _extractedCSharpCode;
        private int _adjustedAddedBytes;
        private Dictionary<int, int> _mapping;

        // 完全な型スパン情報
        private TextSpan _fullTypeSpan;

        // 置換対象の情報
        private ITypeSymbol _originalTypeSymbol;
        private int _position;
        private string _razorFilePath;
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

            PreviewCommand = SelectedType
                .Select(st => st != null && st.FullName != _originalTypeSymbol.ToDisplayString())
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

            AnalyzeImpactCommand = SelectedType
                .Select(st => st != null && st.FullName != _originalTypeSymbol?.ToDisplayString())
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
            ShowBaseTypes.CombineLatest(ShowDerivedTypes, ShowRelatedTypes, (b, d, r) => true)
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

        //有効・無効
        public ReactivePropertySlim<bool> IsEnabledRelatedTypes { get; } = new ReactivePropertySlim<bool>(true);

        // 表示モード
        public ReactivePropertySlim<bool> ShowBaseTypes { get; } = new ReactivePropertySlim<bool>(true);
        public ReactivePropertySlim<bool> ShowDerivedTypes { get; } = new ReactivePropertySlim<bool>(true);
        public ReactivePropertySlim<bool> ShowRelatedTypes { get; } = new ReactivePropertySlim<bool>(true);

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
        ///     初期化
        /// </summary>
        public async Task InitializeAsync(ITypeSymbol typeSymbol, Document document, int position,
            SnapshotSpan typeSpan, ITextBuffer textBuffer, TextSpan fullTypeSpan)
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
                var actualTypeText = typeSpan.GetText();

                // デバッグ情報
                Debug.WriteLine($"InitializeAsync: Original Type Symbol={typeSymbol.ToDisplayString()}");
                Debug.WriteLine($"Actual Type Text='{actualTypeText}'");
                Debug.WriteLine(
                    $"Type with special types={typeSymbol.ToDisplayString(new SymbolDisplayFormat(miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes))}");
                Debug.WriteLine($"Type without special types={typeSymbol.ToDisplayString()}");
                Debug.WriteLine(
                    $"Type Span: '{typeSpan.GetText()}', Full Type Span: Start={fullTypeSpan.Start}, Length={fullTypeSpan.Length}");

                // 元の型名を表示
                OriginalTypeName.Value = string.IsNullOrEmpty(typeSymbol.ContainingNamespace.ToString()) &&
                                         actualTypeText.Contains(typeSymbol.ContainingNamespace.ToString())
                    ? actualTypeText
                    : $"{typeSymbol.ContainingNamespace}.{actualTypeText}";

                // 実際のコードの表記に基づいてフォーマットを判定
                var usePrimitiveTypes = DeterminePrimitiveTypeUsage(actualTypeText);

                // デバッグ出力
                Debug.WriteLine($"Using primitive types: {usePrimitiveTypes}");

                //通常のcsファイルの場合は関連型を有効にする
                IsEnabledRelatedTypes.Value = true;

                // 型候補リストを再取得（プリミティブ型の使用有無を設定）
                ShowUseSpecialTypes.Value = usePrimitiveTypes;

                // 型の候補を取得
                await RefreshTypeCandidates();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Initialize: {ex.Message}");
            }
        }

        public async Task InitializeRazorAsync(ITypeSymbol typeSymbol, Document document, int position,
            string razorFilePath, string csharpCode, TextSpan fullTypeSpan, Dictionary<int, int> mapping, int adjustedAddedBytes)
        {
            try
            {
                _originalTypeSymbol = typeSymbol;
                _document = document;
                _position = position;
                _fullTypeSpan = fullTypeSpan;

                // Razorファイルのパスを保存
                _razorFilePath = razorFilePath;

                // C#コードを保存
                _extractedCSharpCode = csharpCode;

                // 追加バイト数を保存
                _adjustedAddedBytes = adjustedAddedBytes;

                // マッピング情報を保存
                _mapping = mapping;

                // 実際の型名を取得
                var actualTypeText = typeSymbol.Name;

                // デバッグ情報
                Debug.WriteLine($"InitializeRazorAsync: Original Type Symbol={typeSymbol.ToDisplayString()}");
                Debug.WriteLine($"Actual Type Text='{actualTypeText}'");

                //razorスクリプトファイルの場合は関連型を無効にする
                IsEnabledRelatedTypes.Value = false;

                // 元の型名を表示
                OriginalTypeName.Value = typeSymbol.ToDisplayString();

                // 型候補リストを取得
                await RefreshTypeCandidates();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in InitializeRazorAsync: {ex.Message}");
            }
        }

        /// <summary>
        ///     実際のコードの表記からプリミティブ型が使用されているかを判定
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
                var shortNetType = netType.Substring(netType.LastIndexOf('.') + 1); // "Int32" など
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
            if (_originalTypeSymbol == null)
            {
                return;
            }

            try
            {
                IsProcessing.Value = true;
                ProcessingStatus.Value = "型の階層を分析中...";

                // Razorファイルの場合
                var isRazorFile = !string.IsNullOrEmpty(_razorFilePath) &&
                                  (_razorFilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                                   _razorFilePath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase));

                if (isRazorFile && _document == null)
                {
                    // Razorファイル用の型候補取得処理
                    // コンパイレーションから直接型候補を取得する
                    var candidates = await GetRazorTypeReplacementCandidatesAsync(
                        _originalTypeSymbol,
                        ShowBaseTypes.Value,
                        ShowDerivedTypes.Value,
                        ShowRelatedTypes.Value,
                        ShowUseSpecialTypes.Value);

                    // 候補を設定
                    TypeCandidates.Value = candidates;

                    // 現在の型を選択状態にする
                    SelectedType.Value =
                        candidates.FirstOrDefault(t => t.FullName == _originalTypeSymbol.ToDisplayString());
                }
                else if (_document != null)
                {
                    // 通常のC#ファイル用の既存処理
                    var candidates = await TypeHierarchyAnalyzer.GetTypeReplacementCandidatesAsync(
                        _originalTypeSymbol,
                        _document,
                        ShowBaseTypes.Value,
                        ShowDerivedTypes.Value,
                        ShowRelatedTypes.Value,
                        ShowUseSpecialTypes.Value);

                    // 候補を設定
                    TypeCandidates.Value = candidates;

                    // 現在の型を選択状態にする
                    SelectedType.Value =
                        candidates.FirstOrDefault(t => t.FullName == _originalTypeSymbol.ToDisplayString());
                }
                else
                {
                    // どちらの条件も満たさない場合（エラー状態）
                    Debug.WriteLine("Error: Both _document and _razorFilePath are invalid");
                }
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
        ///     Razor用の型候補を取得する補助メソッド
        /// </summary>
        private async Task<List<TypeHierarchyAnalyzer.TypeHierarchyInfo>> GetRazorTypeReplacementCandidatesAsync(
            ITypeSymbol originalType,
            bool includeBaseTypes,
            bool includeDerivedTypes,
            bool includeRelatedTypes,
            bool showUseSpecialTypes)
        {
            var candidates = new List<TypeHierarchyAnalyzer.TypeHierarchyInfo>();

            try
            {
                // Roslynワークスペースからコンパイレーションを取得
                var componentModel = await Package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
                var workspace = componentModel?.GetService<VisualStudioWorkspace>();

                if (workspace != null)
                {
                    // ワークスペースからファイルに関連するプロジェクトを検索
                    var project = workspace.CurrentSolution.Projects
                        .FirstOrDefault(p => p.Documents.Any(d =>
                            string.Equals(d.FilePath, _razorFilePath, StringComparison.OrdinalIgnoreCase) ||
                            d.FilePath.Contains(Path.GetFileName(_razorFilePath))));

                    if (project != null)
                    {
                        var compilation = await project.GetCompilationAsync();

                        // 元の型のTypeHierarchyInfoを作成
                        var typeInfo = TypeHierarchyAnalyzer.CreateTypeHierarchyInfo(originalType, showUseSpecialTypes);
                        candidates.Add(typeInfo);

                        // ベース型とインターフェースを追加
                        if (includeBaseTypes)
                        {
                            // ベースクラスを追加
                            var baseType = originalType.BaseType;
                            while (baseType != null && !baseType.ToDisplayString().Equals("object"))
                            {
                                candidates.Add(
                                    TypeHierarchyAnalyzer.CreateTypeHierarchyInfo(baseType, showUseSpecialTypes));
                                baseType = baseType.BaseType;
                            }

                            // インターフェースを追加
                            foreach (var iface in originalType.Interfaces)
                            {
                                candidates.Add(
                                    TypeHierarchyAnalyzer.CreateTypeHierarchyInfo(iface, showUseSpecialTypes));
                            }
                        }

                        // 派生型や関連型の検索は既存の機能を使用
                        // （完全な実装はTypeHierarchyAnalyzerを参照）
                        // 派生型を追加（具象化）
                        if (includeDerivedTypes)
                        {
                            foreach (var derived in typeInfo.DerivedClasses)
                            {
                                candidates.Add(derived);
                            }
                        }

                        // アセンブリ内の類似インターフェースを検索（新機能）
                        if (originalType.TypeKind == TypeKind.Interface)
                        {
                            // 元のインターフェース名からパターンを作成 (例: ICollection -> I*Collection*)
                            var namePattern = originalType.Name;
                            if (namePattern.StartsWith("I"))
                            {
                                namePattern = namePattern.Substring(1); // "I" を削除
                            }

                            // アセンブリ内のすべての型をチェック
                            foreach (var assembly in compilation.References.Select(r =>
                                         compilation.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol))
                            {
                                if (assembly == null)
                                {
                                    continue;
                                }

                                // 名前空間を再帰的に探索
                                TypeHierarchyAnalyzer.SearchForSimilarInterfaces(assembly.GlobalNamespace, namePattern,
                                    candidates, originalType, showUseSpecialTypes);
                            }
                        }

                        // 名前の類似性に基づいて追加の関連型を検索
                        var typeName = originalType.Name;
                        if (originalType.TypeKind == TypeKind.Interface && typeName.StartsWith("I"))
                        {
                            // 名前のパターンを作成
                            var baseName = typeName.Substring(1); // "I" を削除
                            var patternPairs = new Dictionary<string, string>
                            {
                                { "Data", "ReadOnlyData" },
                                { "Mutable", "Immutable" },
                                { "", "ReadOnly" },
                                { "Collection", "ReadOnlyCollection" },
                                { "List", "ReadOnlyList" },
                                { "Dictionary", "ReadOnlyDictionary" }
                                // 他のパターンも追加できます
                            };

                            // コンパイルしているプロジェクト内のすべての型シンボルを取得
                            var allTypes = new List<INamedTypeSymbol>();
                            TypeHierarchyAnalyzer.CollectAllTypes(compilation.GlobalNamespace, allTypes);

                            // パターンに基づいて候補を検索
                            foreach (var pair in patternPairs)
                            {
                                if (baseName.Contains(pair.Key))
                                {
                                    string targetName;
                                    if (string.IsNullOrEmpty(pair.Key))
                                    {
                                        // 空キーの場合は、頭に接頭辞を追加
                                        targetName = "I" + pair.Value + baseName;
                                    }
                                    else
                                    {
                                        // 通常の置換
                                        targetName = "I" + baseName.Replace(pair.Key, pair.Value);
                                    }

                                    var matchingTypes = allTypes.Where(t =>
                                        t.Name == targetName &&
                                        t.TypeKind == TypeKind.Interface &&
                                        !candidates.Any(c => c.FullName == t.ToDisplayString()));

                                    foreach (var type in matchingTypes)
                                    {
                                        candidates.Add(TypeHierarchyAnalyzer.CreateTypeHierarchyInfo(type,
                                            showUseSpecialTypes, originalType));
                                        Debug.WriteLine($"Added pattern-matched type: {type.ToDisplayString()}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetRazorTypeReplacementCandidatesAsync: {ex.Message}");
            }

            return candidates;
        }

        /// <summary>
        ///     型の変更を適用
        /// </summary>
        private async Task ApplyTypeChange()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Razorファイルかどうか確認
            var isRazorFile = !string.IsNullOrEmpty(_razorFilePath) &&
                              (_razorFilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                               _razorFilePath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase));

            // DTEオブジェクトを取得
            var dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));

            if (isRazorFile)
            {
                try
                {
                    // ファイルを開いて編集するか、既に開いている場合は直接編集する
                    EnvDTE.Document openedDoc = null;

                    try
                    {
                        // 既に開いているか確認
                        EnvDTE.Document activeDoc = dte.ActiveDocument;
                        if (activeDoc != null && string.Equals(activeDoc.FullName, _razorFilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            openedDoc = activeDoc;
                        }
                        else
                        {
                            // ファイルを開く
                            openedDoc = dte.ItemOperations.OpenFile(_razorFilePath).Document;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error opening file: {ex.Message}");

                        // 代替方法：直接ファイルを読み書きする
                        ProcessRazorFileDirectly();
                        return;
                    }

                    if (openedDoc != null)
                    {
                        // テキスト編集用のオブジェクトを取得
                        var textDoc = openedDoc.Object("TextDocument") as TextDocument;
                        if (textDoc != null)
                        {
                            // 新しい型名を取得
                            var newTypeName = GetSimplifiedTypeName(SelectedType.Value.DisplayName, _originalTypeSymbol.Name);
                            var originalTypeName = _originalTypeSymbol.Name;

                            // カーソル位置またはスパン情報が存在する場合
                            if (_fullTypeSpan.Start >= 0 || _position > 0)
                            {
                                // 目標の行番号を取得
                                int targetLine = 0;

                                if (_position > 0)
                                {
                                    // 位置情報から行番号を取得
                                    var editPoint = textDoc.StartPoint.CreateEditPoint();
                                    var text = editPoint.GetText(textDoc.EndPoint);
                                    targetLine = GetLineNumberFromPosition(text, _position);
                                }
                                else if (_extractedCSharpCode != null)
                                {
                                    // C#コード内での位置から行番号を推定
                                    int offset = _fullTypeSpan.Start;
                                    targetLine = GetLineNumberFromPosition(_extractedCSharpCode, offset);
                                }

                                if (targetLine > 0)
                                {
                                    // 対象の行までスキップ
                                    var editPoint = textDoc.StartPoint.CreateEditPoint();
                                    editPoint.LineDown(targetLine - 1);

                                    // 行内で型名を検索
                                    var lineText = editPoint.GetLines(editPoint.Line, editPoint.Line + 1);
                                    int inLinePos = lineText.IndexOf(originalTypeName);

                                    if (inLinePos >= 0)
                                    {
                                        // UndoContextを開始
                                        dte.UndoContext.Open("Type Replacement");

                                        try
                                        {
                                            // 編集ポイントを型名の開始位置に移動
                                            editPoint.CharRight(inLinePos);

                                            // 元の型名を削除して新しい型名を挿入
                                            editPoint.Delete(originalTypeName.Length);
                                            editPoint.Insert(newTypeName);

                                            // 変更を適用
                                            openedDoc.Save();
                                        }
                                        finally
                                        {
                                            // UndoContextを閉じる
                                            dte.UndoContext.Close();
                                        }

                                        return; // 正常に完了
                                    }
                                }
                            }

                            // 行番号での検索に失敗した場合、カーソル位置を使用するか確認
                            var selection = textDoc.Selection;
                            if (selection != null)
                            {
                                // 現在のカーソル行のテキストを取得
                                var currentLine = selection.CurrentLine;
                                var lineText = selection.Text;

                                // カーソル行内で型名を検索
                                int inLinePos = lineText.IndexOf(originalTypeName);
                                if (inLinePos >= 0)
                                {
                                    // UndoContextを開始
                                    dte.UndoContext.Open("Type Replacement");

                                    try
                                    {
                                        // 編集ポイントを型名の開始位置に移動
                                        selection.MoveToLineAndOffset(currentLine, inLinePos + 1);

                                        // 元の型名を削除して新しい型名を挿入
                                        selection.Delete(originalTypeName.Length);
                                        selection.Insert(newTypeName);

                                        // 変更を適用
                                        openedDoc.Save();
                                    }
                                    finally
                                    {
                                        // UndoContextを閉じる
                                        dte.UndoContext.Close();
                                    }

                                    return; // 正常に完了
                                }
                            }

                            // 上記の方法で見つからない場合は、ユーザーに確認して通常の方法で置換
                            var result = MessageBox.Show(
                                "特定の行での型の置換ができませんでした。\n最初に見つかった型名を置換しますか？",
                                "型置換",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (result == MessageBoxResult.Yes)
                            {
                                // 通常の検索置換を実行
                                ProcessRazorFileStandard(textDoc, originalTypeName, newTypeName);
                            }
                        }
                        else
                        {
                            MessageBox.Show("テキストドキュメントとして編集できませんでした。",
                                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error modifying Razor file: {ex.Message}");
                    MessageBox.Show($"Razorファイルの編集中にエラーが発生しました: {ex.Message}",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
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
        }

        // 通常の検索置換
        private void ProcessRazorFileStandard(TextDocument textDoc, string originalTypeName, string newTypeName)
        {
            var dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));

            // UndoContextを開始
            dte.UndoContext.Open("Type Replacement");

            try
            {
                // 編集ポイントを作成
                var editPoint = textDoc.StartPoint.CreateEditPoint();

                // ドキュメント全体のテキストを取得
                var text = editPoint.GetText(textDoc.EndPoint);

                // 元の型名を探す
                var position = text.IndexOf(originalTypeName);

                if (position >= 0)
                {
                    // 編集ポイントを型名の開始位置に移動
                    editPoint.MoveToAbsoluteOffset(position + 1);

                    // 元の型名を削除して新しい型名を挿入
                    editPoint.Delete(originalTypeName.Length);
                    editPoint.Insert(newTypeName);

                    // 変更を適用
                    (textDoc as EnvDTE.Document).Save();
                }
                else
                {
                    MessageBox.Show("型を置換する位置を特定できませんでした。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                // UndoContextを閉じる
                dte.UndoContext.Close();
            }
        }

        // ファイルを直接読み書きする方法
        private void ProcessRazorFileDirectly()
        {
            try
            {
                // ファイルを直接読み書きする
                var razorContent = File.ReadAllText(_razorFilePath);

                // 新しい型名を取得
                var newTypeName = GetSimplifiedTypeName(SelectedType.Value.DisplayName, _originalTypeSymbol.Name);

                // 元の型名を探す
                var originalTypeName = _originalTypeSymbol.Name;

                // ソーステキストを行に分割して特定の行を検索
                var lines = razorContent.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);
                int targetLine = -1;

                // 位置情報から行番号を推定
                if (_position > 0)
                {
                    targetLine = GetLineNumberFromPosition(razorContent, _position);
                }

                if (targetLine > 0 && targetLine <= lines.Length)
                {
                    // 対象の行のテキスト
                    string lineText = lines[targetLine - 1];

                    // 行内で型名を検索
                    int inLinePos = lineText.IndexOf(originalTypeName);
                    if (inLinePos >= 0)
                    {
                        // 行内の型名を置換
                        lines[targetLine - 1] = lineText.Substring(0, inLinePos) +
                                               newTypeName +
                                               lineText.Substring(inLinePos + originalTypeName.Length);

                        // ファイルに書き戻す
                        File.WriteAllLines(_razorFilePath, lines);
                        return;
                    }
                }

                // 行番号での検索に失敗した場合、ユーザーに確認
                var result = MessageBox.Show(
                    "特定の行での型の置換ができませんでした。\n最初に見つかった型名を置換しますか？",
                    "型置換",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // 通常の検索置換
                    var position = razorContent.IndexOf(originalTypeName);
                    if (position >= 0)
                    {
                        var newContent = razorContent.Substring(0, position) +
                                         newTypeName +
                                         razorContent.Substring(position + originalTypeName.Length);

                        // ファイルに書き戻す
                        File.WriteAllText(_razorFilePath, newContent);
                    }
                    else
                    {
                        MessageBox.Show("型を置換する位置を特定できませんでした。",
                            "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing Razor file directly: {ex.Message}");
                MessageBox.Show($"ファイルの直接編集中にエラーが発生しました: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        ///     表示用に型名を簡略化
        /// </summary>
        private string GetSimplifiedTypeName(string fullName, string originalTypeText)
        {
            try
            {
                if (string.IsNullOrEmpty(fullName))
                {
                    return string.Empty;
                }

                // ジェネリック型かどうか確認
                if (fullName.Contains("<"))
                {
                    var genericStart = fullName.IndexOf('<');
                    var originalGenericStart = originalTypeText.IndexOf('<');

                    // ジェネリック部分を抽出 (例: System.Collections.Generic.List<int> -> System.Collections.Generic.List と <int>)
                    var baseTypeName = fullName.Substring(0, genericStart);
                    var typeParams = fullName.Substring(genericStart); // <int> 部分
                    var originalTypeParams = originalGenericStart != -1 ? originalTypeText.Substring(originalGenericStart) // <int> 部分
                            : $"<{OriginalTypeName}>"; //元の型自身が型パラメーターになる 

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
                    var lastDot = baseTypeName.LastIndexOf('.');
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
                    var lastDot = fullName.LastIndexOf('.');
                    if (lastDot >= 0)
                    {
                        return fullName.Substring(lastDot + 1);
                    }

                    return fullName;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetSimplifiedTypeName: {ex.Message}");
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
                if (SelectedType.Value == null)
                {
                    return;
                }

                IsProcessing.Value = true;
                ProcessingStatus.Value = "コード変更をプレビュー中...";

                string originalCode;
                string newCode;

                // Razorファイルの場合
                bool isRazorFile = !string.IsNullOrEmpty(_razorFilePath) &&
                                   (_razorFilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                                    _razorFilePath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase));

                if (isRazorFile && _document == null)
                {
                    // Razorファイルの内容を直接読み込む
                    originalCode = File.ReadAllText(_razorFilePath);
                    string originalTypeName = _originalTypeSymbol.Name;
                    string newTypeName = GetSimplifiedTypeName(SelectedType.Value.DisplayName, originalTypeName);

                    // ソーステキストを行に分割
                    var lines = originalCode.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);
                    int targetLine = -1;

                    // 位置情報から行番号を推定
                    if (_position > 0)
                    {
                        targetLine = GetLineNumberFromPosition(originalCode, _position);
                    }
                    else if (_fullTypeSpan.Start >= 0 && _extractedCSharpCode != null)
                    {
                        // C#コード内での位置から行番号を推定
                        targetLine = GetLineNumberFromPosition(_extractedCSharpCode, _fullTypeSpan.Start);
                    }

                    // 行が特定できた場合
                    if (targetLine > 0 && targetLine <= lines.Length)
                    {
                        Debug.WriteLine($"Targeting line {targetLine} for preview");

                        // 対象の行のテキスト
                        string lineText = lines[targetLine - 1];

                        // 行内で型名を検索
                        int inLinePos = lineText.IndexOf(originalTypeName);
                        if (inLinePos >= 0)
                        {
                            // 変更後のテキストを作成（対象行のみ変更）
                            var modifiedLines = new string[lines.Length];
                            Array.Copy(lines, modifiedLines, lines.Length);

                            // 行の中の型名を置換
                            modifiedLines[targetLine - 1] = lineText.Substring(0, inLinePos) +
                                                          newTypeName +
                                                          lineText.Substring(inLinePos + originalTypeName.Length);

                            // 変更後のコードを作成
                            newCode = string.Join(Environment.NewLine, modifiedLines);

                            // DiffViewerを使って差分を表示
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            var diffViewer = new DiffViewer();
                            _diffWindowFrame = diffViewer.ShowDiff(originalCode, newCode, true,
                                                                "型変更のプレビュー",
                                                                $"行 {targetLine} の型名を置換します");

                            IsProcessing.Value = false;
                            ProcessingStatus.Value = "準備完了";
                            return;
                        }
                    }

                    // 行番号での検索に失敗した場合、ユーザーに確認
                    var result = MessageBox.Show(
                        "特定の行での型名が見つかりませんでした。\n最初に見つかった型名でプレビューしますか？",
                        "プレビュー確認",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // 通常の検索置換でプレビュー
                        int position = originalCode.IndexOf(originalTypeName);
                        if (position >= 0)
                        {
                            newCode = originalCode.Substring(0, position) +
                                      newTypeName +
                                      originalCode.Substring(position + originalTypeName.Length);

                            // DiffViewerを使って差分を表示
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            var diffViewer = new DiffViewer();
                            _diffWindowFrame = diffViewer.ShowDiff(originalCode, newCode, true,
                                                                "型変更のプレビュー",
                                                                "最初に見つかった型名を置換します");
                        }
                        else
                        {
                            MessageBox.Show("型を置換する位置を特定できませんでした。",
                                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);

                            IsProcessing.Value = false;
                            ProcessingStatus.Value = "準備完了";
                            return;
                        }
                    }
                    else
                    {
                        // ユーザーがキャンセルした場合
                        IsProcessing.Value = false;
                        ProcessingStatus.Value = "準備完了";
                        return;
                    }
                }
                else if (_document != null)
                {
                    // 通常のC#ファイル用の既存処理
                    var sourceText = await _document.GetTextAsync();
                    originalCode = sourceText.ToString();

                    // 型を置換した新しいコードを生成
                    var newTypeName = GetSimplifiedTypeName(SelectedType.Value.DisplayName, _typeSpan.GetText());

                    // 置換後のテキストを作成
                    var start = _typeSpan.Span.Start;
                    var end = _typeSpan.Span.End;
                    newCode = originalCode.Substring(0, start) +
                              newTypeName +
                              originalCode.Substring(end);
                }
                else
                {
                    // どちらの条件も満たさない場合（エラー状態）
                    Debug.WriteLine("Error: Both _document and _razorFilePath are invalid");
                    IsProcessing.Value = false;
                    ProcessingStatus.Value = "準備完了";
                    return;
                }

                // DiffViewerを使って差分を表示
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var commonDiffViewer = new DiffViewer();
                _diffWindowFrame = commonDiffViewer.ShowDiff(originalCode, newCode, true,
                                                          "型変更のプレビュー",
                                                          "型変更を適用するか検討してください");
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

        // 位置情報から行番号を取得するヘルパーメソッド (既に定義されていなければ追加)
        private int GetLineNumberFromPosition(string text, int position)
        {
            if (string.IsNullOrEmpty(text) || position < 0)
                return -1;

            // 安全のため、位置が文字列の長さを超えないように調整
            position = Math.Min(position, text.Length - 1);

            int line = 1;
            for (int i = 0; i < position; i++)
            {
                if (i < text.Length && text[i] == '\n')
                    line++;
            }

            return line;
        }

        /// <summary>
        ///     ダイアログが開かれた時の処理
        /// </summary>
        public void OnDialogOpened(Window window)
        {
            Window = window;

            // 元の型名がある場合は正規化する
            if (!string.IsNullOrEmpty(OriginalTypeName.Value))
            {
                // 型名のみを抽出
                var cleanTypeName = TypeHierarchyAnalyzer.ExtractTypeNameOnly(OriginalTypeName.Value);
                OriginalTypeName.Value = cleanTypeName;
            }
        }

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

                // 選択された型のシンボルがない場合は中止
                if (_originalTypeSymbol == null)
                {
                    return;
                }

                // Razorファイルの場合
                bool isRazorFile = !string.IsNullOrEmpty(_razorFilePath) &&
                                   (_razorFilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                                    _razorFilePath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase));

                if (isRazorFile && _document == null)
                {
                    // Razorファイル用の影響分析
                    await AnalyzeRazorFileImpact();
                }
                else if (_document != null)
                {
                    // 通常のC#ファイル用の既存処理
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

                    // 特定の変数/パラメータが見つからない場合
                    MessageBox.Show("特定のパラメータや変数が見つかりませんでした。型全体に対する参照を検索します。",
                        "警告",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    await ShowImpactForSymbol(_originalTypeSymbol);
                }
                else
                {
                    MessageBox.Show("ドキュメントの取得に失敗しました。分析を続行できません。",
                        "エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
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

        /// <summary>
        /// Razorファイルの影響分析を行います
        /// </summary>
        private async Task AnalyzeRazorFileImpact()
        {
            try
            {
                // マッピング情報のデバッグ出力
                if (_mapping != null)
                {
                    Debug.WriteLine($"マッピング情報のエントリ数: {_mapping.Count}");
                    foreach (var entry in _mapping.Take(10))
                    {
                        Debug.WriteLine($"生成コード行 {entry.Key} -> Razor行 {entry.Value}");
                    }
                }

                // 型名を正確に取得
                string originalTypeName = _originalTypeSymbol.Name;
                string newTypeName = GetSimplifiedTypeName(SelectedType.Value.DisplayName, originalTypeName);

                // 影響分析のための参照リストを準備
                var impactList = new List<TypeReferenceInfo>();
                var potentialIssues = new List<PotentialIssue>();

                // 参照収集をより包括的に行う
                await CollectAllTypeReferences(_originalTypeSymbol, impactList, potentialIssues);

                // Razorファイルからの直接的な参照も検索
                if (File.Exists(_razorFilePath))
                {
                    await CollectDirectRazorReferences(_razorFilePath, originalTypeName, impactList);
                }

                // 参照が見つかった場合、影響範囲分析ダイアログを表示
                if (impactList.Count > 0)
                {
                    // マッピング情報を検証
                    RazorMappingHelper.ValidateMapping(_mapping, _extractedCSharpCode);

                    // ダイアログ表示
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    var rcPotentialIssues = new ReactiveCollection<PotentialIssue>();
                    rcPotentialIssues.AddRange(potentialIssues);

                    var dialog = new ImpactAnalysisDialog
                    {
                        DataContext = new ImpactAnalysisViewModel
                        {
                            OriginalTypeName = _originalTypeSymbol.ToDisplayString(),
                            NewTypeName = SelectedType.Value.DisplayName,
                            ReferencesCount = impactList.Count,
                            References = impactList,
                            PotentialIssues = rcPotentialIssues,

                            // マッピング情報を明示的に渡す
                            Mapping = _mapping,
                            ExtractedCSharpCode = _extractedCSharpCode,
                            RazorFilePath = _razorFilePath
                        }
                    };

                    (dialog.DataContext as ImpactAnalysisViewModel).OnDialogOpened(dialog);
                    dialog.Show();
                }
                else
                {
                    MessageBox.Show($"型 '{originalTypeName}' の参照が見つかりませんでした。",
                        "分析結果", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Razorファイル影響分析エラー: {ex.Message}");
                MessageBox.Show($"Razorファイルの影響分析中にエラーが発生しました: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // Razorファイルから直接参照を収集するヘルパーメソッド
        private async Task CollectDirectRazorReferences(string razorFilePath, string typeName, List<TypeReferenceInfo> references)
        {
            try
            {
                // Razorファイルの内容を読み込む
                string razorContent = File.ReadAllText(razorFilePath);
                string[] lines = razorContent.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);

                // 1. 標準的なC#タグでの型参照
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    if (line.Contains($"@{typeName}") ||
                        line.Contains($"<{typeName}") ||
                        line.Contains($" {typeName}"))
                    {
                        references.Add(new TypeReferenceInfo
                        {
                            FilePath = razorFilePath,
                            FileName = Path.GetFileName(razorFilePath),
                            LineNumber = i + 1,  // 行番号は1ベース
                            RazorLineNumber = i + 1,  // Razorファイルなので同じ
                            Column = line.IndexOf(typeName) + 1,
                            Text = line.Trim(),
                            ReferenceType = "Razorファイルでの直接参照"
                        });
                    }
                }

                // 2. コンポーネント属性でのバインド
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    // コンポーネント属性でのバインド (@bind, @bind-Value など)
                    if (line.Contains("@bind") && line.Contains(typeName))
                    {
                        references.Add(new TypeReferenceInfo
                        {
                            FilePath = razorFilePath,
                            FileName = Path.GetFileName(razorFilePath),
                            LineNumber = i + 1,
                            RazorLineNumber = i + 1,
                            Column = line.IndexOf("@bind") + 1,
                            Text = line.Trim(),
                            ReferenceType = "データバインド参照"
                        });
                    }
                }

                // 3. Injectディレクティブでの型参照
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    if (line.Contains("@inject") && line.Contains(typeName))
                    {
                        references.Add(new TypeReferenceInfo
                        {
                            FilePath = razorFilePath,
                            FileName = Path.GetFileName(razorFilePath),
                            LineNumber = i + 1,
                            RazorLineNumber = i + 1,
                            Column = line.IndexOf(typeName) + 1,
                            Text = line.Trim(),
                            ReferenceType = "依存性注入参照"
                        });
                    }
                }

                // 4. @code ブロック内での型参照
                bool inCodeBlock = false;
                int codeBlockStartLine = 0;
                int braceCount = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    // @code ブロックの開始を検出
                    if (line.Contains("@code") && line.Contains("{"))
                    {
                        inCodeBlock = true;
                        codeBlockStartLine = i;
                        braceCount = CountOccurrences(line, '{') - CountOccurrences(line, '}');
                        continue;
                    }

                    // @code ブロック内部
                    if (inCodeBlock)
                    {
                        braceCount += CountOccurrences(line, '{') - CountOccurrences(line, '}');

                        // ブロック終了
                        if (braceCount <= 0)
                        {
                            inCodeBlock = false;
                            continue;
                        }

                        // ブロック内でtypeNameを検索
                        if (line.Contains(typeName))
                        {
                            references.Add(new TypeReferenceInfo
                            {
                                FilePath = razorFilePath,
                                FileName = Path.GetFileName(razorFilePath),
                                LineNumber = i + 1,
                                RazorLineNumber = i + 1,
                                Column = line.IndexOf(typeName) + 1,
                                Text = line.Trim(),
                                ReferenceType = "@code ブロック内での参照"
                            });
                        }
                    }
                }

                // 5. @functions ブロック内での型参照 (レガシーRazor構文)
                inCodeBlock = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    // @functions ブロックの開始を検出
                    if (line.Contains("@functions") && line.Contains("{"))
                    {
                        inCodeBlock = true;
                        braceCount = CountOccurrences(line, '{') - CountOccurrences(line, '}');
                        continue;
                    }

                    // @functions ブロック内部
                    if (inCodeBlock)
                    {
                        braceCount += CountOccurrences(line, '{') - CountOccurrences(line, '}');

                        // ブロック終了
                        if (braceCount <= 0)
                        {
                            inCodeBlock = false;
                            continue;
                        }

                        // ブロック内でtypeNameを検索
                        if (line.Contains(typeName))
                        {
                            references.Add(new TypeReferenceInfo
                            {
                                FilePath = razorFilePath,
                                FileName = Path.GetFileName(razorFilePath),
                                LineNumber = i + 1,
                                RazorLineNumber = i + 1,
                                Column = line.IndexOf(typeName) + 1,
                                Text = line.Trim(),
                                ReferenceType = "@functions ブロック内での参照"
                            });
                        }
                    }
                }

                // 6. インラインC#式での参照 (@(expression))
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    int expressionStart = line.IndexOf("@(");
                    while (expressionStart >= 0)
                    {
                        int expressionEnd = FindMatchingParenthesis(line, expressionStart + 1);
                        if (expressionEnd > expressionStart)
                        {
                            string expression = line.Substring(expressionStart + 2, expressionEnd - expressionStart - 2);
                            if (expression.Contains(typeName))
                            {
                                references.Add(new TypeReferenceInfo
                                {
                                    FilePath = razorFilePath,
                                    FileName = Path.GetFileName(razorFilePath),
                                    LineNumber = i + 1,
                                    RazorLineNumber = i + 1,
                                    Column = expressionStart + expression.IndexOf(typeName) + 3, // @( の後ろの位置
                                    Text = line.Trim(),
                                    ReferenceType = "インラインC#式での参照"
                                });
                            }

                            expressionStart = line.IndexOf("@(", expressionEnd);
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                // 7. コンポーネント参照でのタイプパラメータ
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    // <Component TItem="TypeName"> パターン
                    var typeParamPattern = new System.Text.RegularExpressions.Regex($@"T\w+=\""{typeName}\""");
                    var matches = typeParamPattern.Matches(line);

                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        references.Add(new TypeReferenceInfo
                        {
                            FilePath = razorFilePath,
                            FileName = Path.GetFileName(razorFilePath),
                            LineNumber = i + 1,
                            RazorLineNumber = i + 1,
                            Column = match.Index + 1,
                            Text = line.Trim(),
                            ReferenceType = "ジェネリック型パラメータ参照"
                        });
                    }
                }

                // 8. @inherits ディレクティブでの型参照
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    if (line.Contains("@inherits") && line.Contains(typeName))
                    {
                        references.Add(new TypeReferenceInfo
                        {
                            FilePath = razorFilePath,
                            FileName = Path.GetFileName(razorFilePath),
                            LineNumber = i + 1,
                            RazorLineNumber = i + 1,
                            Column = line.IndexOf(typeName) + 1,
                            Text = line.Trim(),
                            ReferenceType = "継承参照"
                        });
                    }
                }

                // 9. @implements ディレクティブでの型参照
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    if (line.Contains("@implements") && line.Contains(typeName))
                    {
                        references.Add(new TypeReferenceInfo
                        {
                            FilePath = razorFilePath,
                            FileName = Path.GetFileName(razorFilePath),
                            LineNumber = i + 1,
                            RazorLineNumber = i + 1,
                            Column = line.IndexOf(typeName) + 1,
                            Text = line.Trim(),
                            ReferenceType = "インターフェース実装参照"
                        });
                    }
                }

                // 10. @typeparam ディレクティブでの型制約
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];

                    if (line.Contains("@typeparam") && line.Contains("where") && line.Contains(typeName))
                    {
                        references.Add(new TypeReferenceInfo
                        {
                            FilePath = razorFilePath,
                            FileName = Path.GetFileName(razorFilePath),
                            LineNumber = i + 1,
                            RazorLineNumber = i + 1,
                            Column = line.IndexOf(typeName) + 1,
                            Text = line.Trim(),
                            ReferenceType = "型パラメータ制約参照"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Razorファイルからの直接参照収集エラー: {ex.Message}");
            }
        }

        private static int CountOccurrences(string text, char character)
        {
            return text.Count(c => c == character);
        }

        private static int FindMatchingParenthesis(string text, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                if (text[i] == '(')
                {
                    depth++;
                }
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }
            return -1; // 対応する閉じ括弧がない
        }

        /// <summary>
        /// Razorファイルが属するプロジェクトを特定するための改良メソッド
        /// </summary>
        private Microsoft.CodeAnalysis.Project FindProjectForRazorFile(string razorFilePath, VisualStudioWorkspace workspace)
        {
            try
            {
                // 1. 完全なパスマッチで検索
                var exactMatchProject = workspace.CurrentSolution.Projects
                    .FirstOrDefault(p => p.Documents.Any(d =>
                        string.Equals(d.FilePath, razorFilePath, StringComparison.OrdinalIgnoreCase)));

                if (exactMatchProject != null)
                    return exactMatchProject;

                // 2. ファイル名でマッチを試行
                string fileName = Path.GetFileName(razorFilePath);
                var fileNameMatchProject = workspace.CurrentSolution.Projects
                    .FirstOrDefault(p => p.Documents.Any(d =>
                        d.FilePath != null &&
                        string.Equals(Path.GetFileName(d.FilePath), fileName, StringComparison.OrdinalIgnoreCase)));

                if (fileNameMatchProject != null)
                    return fileNameMatchProject;

                // 3. DTEを使用してプロジェクト情報を取得
                var dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
                if (dte != null && dte.Solution != null)
                {
                    // アクティブなプロジェクトを取得
                    Array activeSolutionProjects = dte.ActiveSolutionProjects as Array;
                    if (activeSolutionProjects != null && activeSolutionProjects.Length > 0)
                    {
                        var activeProject = activeSolutionProjects.GetValue(0) as Microsoft.CodeAnalysis.Project;
                        if (activeProject != null)
                        {
                            string projectName = activeProject.Name;

                            // ワークスペースから同じ名前のプロジェクトを探す
                            var activeRoslynProject = workspace.CurrentSolution.Projects
                                .FirstOrDefault(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));

                            if (activeRoslynProject != null)
                                return activeRoslynProject;
                        }
                    }

                    // すべてのプロジェクトをチェック
                    foreach (EnvDTE.Project dteProject in dte.Solution.Projects)
                    {
                        try
                        {
                            // プロジェクト内のアイテムを再帰的にチェック
                            if (IsFileInDTEProject(dteProject, razorFilePath))
                            {
                                // 該当するプロジェクトが見つかったらワークスペースから対応するプロジェクトを探す
                                var matchingRoslynProject = workspace.CurrentSolution.Projects
                                    .FirstOrDefault(p => string.Equals(p.Name, dteProject.Name, StringComparison.OrdinalIgnoreCase));

                                if (matchingRoslynProject != null)
                                    return matchingRoslynProject;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"プロジェクトチェック中のエラー: {ex.Message}");
                        }
                    }
                }

                // 4. ファイルパスから最も可能性の高いプロジェクトを推測
                string razorDir = Path.GetDirectoryName(razorFilePath);

                // ディレクトリ名と一致するプロジェクトを検索
                string directoryName = new DirectoryInfo(razorDir).Name;
                var dirNameMatchProject = workspace.CurrentSolution.Projects
                    .FirstOrDefault(p => p.Name.Contains(directoryName) || directoryName.Contains(p.Name));

                if (dirNameMatchProject != null)
                    return dirNameMatchProject;

                // 5. 最後の手段: すべてのBlazorプロジェクトから選択
                var blazorProjects = workspace.CurrentSolution.Projects
                    .Where(p => p.Documents.Any(d =>
                        d.FilePath != null &&
                        (d.FilePath.EndsWith(".razor") || d.FilePath.EndsWith(".cshtml"))))
                    .ToList();

                // Blazorプロジェクトが1つだけなら、それを返す
                if (blazorProjects.Count == 1)
                    return blazorProjects[0];

                // 複数ある場合は名前でソートして最初のものを返す
                if (blazorProjects.Count > 1)
                    return blazorProjects.OrderBy(p => p.Name).First();

                // プロジェクトが見つからなかった場合はnullを返す
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プロジェクト検索中のエラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// DTEプロジェクト内でファイルを再帰的に検索する
        /// </summary>
        private bool IsFileInDTEProject(EnvDTE.Project project, string filePath)
        {
            try
            {
                if (project.ProjectItems == null)
                    return false;

                foreach (ProjectItem item in project.ProjectItems)
                {
                    // このアイテムが検索中のファイルかチェック
                    if (item.FileCount > 0)
                    {
                        try
                        {
                            string itemPath = item.FileNames[0];
                            if (string.Equals(itemPath, filePath, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    // サブフォルダ内も再帰的に検索
                    if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                    {
                        if (IsFileInProjectItems(item.ProjectItems, filePath))
                        {
                            return true;
                        }
                    }

                    // プロジェクト内プロジェクトの場合
                    if (item.SubProject != null)
                    {
                        if (IsFileInDTEProject(item.SubProject, filePath))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プロジェクト内ファイル検索エラー: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ProjectItems内でファイルを再帰的に検索する
        /// </summary>
        private bool IsFileInProjectItems(ProjectItems items, string filePath)
        {
            foreach (ProjectItem item in items)
            {
                // このアイテムが検索中のファイルかチェック
                if (item.FileCount > 0)
                {
                    try
                    {
                        string itemPath = item.FileNames[0];
                        if (string.Equals(itemPath, filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                // サブフォルダ内も再帰的に検索
                if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                {
                    if (IsFileInProjectItems(item.ProjectItems, filePath))
                    {
                        return true;
                    }
                }

                // プロジェクト内プロジェクトの場合
                if (item.SubProject != null)
                {
                    if (IsFileInDTEProject(item.SubProject, filePath))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// すべてのプロジェクトを対象に型参照を検索
        /// </summary>
        private async Task AnalyzeTypeReferencesInAllProjects(ITypeSymbol typeSymbol, List<Microsoft.CodeAnalysis.Project> projects)
        {
            try
            {
                var impactList = new List<TypeReferenceInfo>();
                var potentialIssues = new List<PotentialIssue>();

                foreach (var project in projects)
                {
                    // Razorファイルに関連するコードファイルを検索
                    var generatedDocs = project.Documents
                        .Where(d => d.FilePath != null && (
                            d.FilePath.Contains(".razor.g.cs") ||
                            d.FilePath.Contains(".cshtml.g.cs") ||
                            (d.FilePath.Contains(".razor.") && d.FilePath.EndsWith(".cs")) ||
                            (d.FilePath.Contains(".cshtml.") && d.FilePath.EndsWith(".cs"))
                        )).ToList();

                    // 通常のコードファイルも検索対象に含める
                    var csharpDocs = project.Documents
                        .Where(d => d.FilePath != null && d.FilePath.EndsWith(".cs") &&
                               !generatedDocs.Any(gd => gd.FilePath == d.FilePath))
                        .ToList();

                    var allDocs = generatedDocs.Concat(csharpDocs).ToList();

                    foreach (var doc in allDocs)
                    {
                        try
                        {
                            var text = await doc.GetTextAsync();
                            var root = await doc.GetSyntaxRootAsync();
                            var semanticModel = await doc.GetSemanticModelAsync();

                            if (root == null || semanticModel == null)
                                continue;

                            // 型名の文字列検索（簡易的なアプローチ）
                            var typeNameNodes = root.DescendantNodes()
                                .OfType<IdentifierNameSyntax>()
                                .Where(n => n.Identifier.Text == typeSymbol.Name)
                                .ToList();

                            foreach (var node in typeNameNodes)
                            {
                                var linePosition = text.Lines.GetLinePosition(node.Span.Start);
                                var refLine = linePosition.Line + 1;

                                var lineSpan = text.Lines[linePosition.Line].Span;
                                var referenceText = text.ToString(lineSpan).Trim();

                                impactList.Add(new TypeReferenceInfo
                                {
                                    FilePath = doc.FilePath,
                                    FileName = Path.GetFileName(doc.FilePath),
                                    LineNumber = refLine,
                                    Column = linePosition.Character + 1,
                                    Text = referenceText,
                                    ReferenceType = "型名の出現"
                                });
                            }

                            // より厳密な型参照の検索
                            var typeNodes = root.DescendantNodes()
                                .OfType<TypeSyntax>()
                                .ToList();

                            foreach (var typeNode in typeNodes)
                            {
                                var typeInfo = semanticModel.GetTypeInfo(typeNode);

                                if (typeInfo.Type != null &&
                                    (typeInfo.Type.Name == typeSymbol.Name ||
                                     typeInfo.Type.ToDisplayString() == typeSymbol.ToDisplayString()))
                                {
                                    var linePosition = text.Lines.GetLinePosition(typeNode.Span.Start);
                                    var refLine = linePosition.Line + 1;

                                    var lineSpan = text.Lines[linePosition.Line].Span;
                                    var referenceText = text.ToString(lineSpan).Trim();

                                    string referenceType = "型の参照";

                                    // 参照種別の詳細を取得
                                    var parent = typeNode.Parent;
                                    if (parent is ParameterSyntax)
                                    {
                                        referenceType = "パラメータの型";
                                    }
                                    else if (parent is VariableDeclarationSyntax)
                                    {
                                        referenceType = "変数の型";
                                    }
                                    else if (parent is PropertyDeclarationSyntax)
                                    {
                                        referenceType = "プロパティの型";
                                    }
                                    else if (parent is MethodDeclarationSyntax)
                                    {
                                        referenceType = "メソッドの戻り値型";
                                    }

                                    impactList.Add(new TypeReferenceInfo
                                    {
                                        FilePath = doc.FilePath,
                                        FileName = Path.GetFileName(doc.FilePath),
                                        LineNumber = refLine,
                                        Column = linePosition.Character + 1,
                                        Text = referenceText,
                                        ReferenceType = referenceType
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ドキュメント分析エラー ({doc.FilePath}): {ex.Message}");
                        }
                    }
                }

                // 重複を削除
                impactList = impactList
                    .GroupBy(i => new { i.FilePath, i.LineNumber, i.Text })
                    .Select(g => g.First())
                    .ToList();

                // ソート：生成コードではないファイル優先、そして行番号順
                impactList = impactList
                    .OrderBy(r => r.FilePath.Contains(".g.cs")) // 生成コードではないファイルを優先
                    .ThenBy(r => r.FileName)
                    .ThenBy(r => r.LineNumber)
                    .ToList();

                // 影響範囲ダイアログを表示
                if (impactList.Count > 0)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    var rcPotentialIssues = new ReactiveCollection<PotentialIssue>();
                    rcPotentialIssues.AddRange(potentialIssues);

                    var dialog = new ImpactAnalysisDialog
                    {
                        DataContext = new ImpactAnalysisViewModel
                        {
                            OriginalTypeName = _originalTypeSymbol.ToDisplayString(),
                            NewTypeName = SelectedType.Value.DisplayName,
                            ReferencesCount = impactList.Count,
                            References = impactList,
                            PotentialIssues = rcPotentialIssues
                        }
                    };

                    (dialog.DataContext as ImpactAnalysisViewModel).OnDialogOpened(dialog);
                    dialog.Show();
                }
                else
                {
                    MessageBox.Show(
                        $"型 '{typeSymbol.Name}' の参照が見つかりませんでした。",
                        "分析結果",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"全プロジェクト分析エラー: {ex.Message}");
                MessageBox.Show($"プロジェクト分析中にエラーが発生しました: {ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task CollectAllTypeReferences(
    ITypeSymbol typeSymbol,
    List<TypeReferenceInfo> impactList,
    List<PotentialIssue> potentialIssues)
        {
            try
            {
                // ComponentModel サービスを取得
                var componentModel = await Package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
                var workspace = componentModel?.GetService<VisualStudioWorkspace>();

                if (workspace == null)
                {
                    Debug.WriteLine("ワークスペースの取得に失敗しました。");
                    return;
                }

                // ワークスペースから現在のソリューションを取得
                var solution = workspace.CurrentSolution;

                // Razor ファイルを含むプロジェクトを特定
                var razorProject = FindProjectForRazorFile(_razorFilePath, workspace);
                if (razorProject == null)
                {
                    Debug.WriteLine("Razor ファイルを含むプロジェクトが見つかりませんでした。");
                    return;
                }

                // プロジェクト内の C# コードを含む生成ファイルを特定
                var generatedDocuments = razorProject.Documents
                    .Where(d => d.FilePath != null && (
                        d.FilePath.Contains(".razor.g.cs") ||
                        d.FilePath.Contains(".cshtml.g.cs") ||
                        (d.FilePath.Contains(".razor.") && d.FilePath.EndsWith(".cs")) ||
                        (d.FilePath.Contains(".cshtml.") && d.FilePath.EndsWith(".cs"))
                    )).ToList();

                // 型名の参照を収集
                foreach (var doc in generatedDocuments)
                {
                    try
                    {
                        var text = await doc.GetTextAsync();
                        var root = await doc.GetSyntaxRootAsync();
                        var semanticModel = await doc.GetSemanticModelAsync();

                        if (root == null || semanticModel == null)
                            continue;

                        // 型参照（TypeSyntax）を検索
                        var typeReferences = root.DescendantNodes()
                            .OfType<TypeSyntax>()
                            .Where(ts => {
                                var symbolInfo = semanticModel.GetSymbolInfo(ts);
                                return symbolInfo.Symbol != null &&
                                       SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, typeSymbol);
                            });

                        foreach (var typeRef in typeReferences)
                        {
                            var linePosition = text.Lines.GetLinePosition(typeRef.Span.Start);
                            var refLine = linePosition.Line + 1;

                            var lineSpan = text.Lines[linePosition.Line].Span;
                            var referenceText = text.ToString(lineSpan).Trim();

                            var referenceInfo = new TypeReferenceInfo
                            {
                                FilePath = RazorFileUtility.GetOriginalFilePath(doc.FilePath),
                                FileName = Path.GetFileName(RazorFileUtility.GetOriginalFilePath(doc.FilePath)),
                                LineNumber = refLine,
                                Column = linePosition.Character + 1,
                                Text = referenceText,
                                ReferenceType = "型の参照"
                            };

                            // 参照の種類を判断
                            if (typeRef.Parent is ParameterSyntax)
                            {
                                referenceInfo.ReferenceType = "パラメータの型";
                            }
                            else if (typeRef.Parent is VariableDeclarationSyntax)
                            {
                                referenceInfo.ReferenceType = "変数の型";
                            }
                            else if (typeRef.Parent is PropertyDeclarationSyntax)
                            {
                                referenceInfo.ReferenceType = "プロパティの型";
                            }
                            else if (typeRef.Parent is MethodDeclarationSyntax)
                            {
                                referenceInfo.ReferenceType = "メソッドの戻り値型";
                            }

                            // 重複を避けるために追加
                            if (!impactList.Any(r =>
                                r.FilePath == referenceInfo.FilePath &&
                                r.LineNumber == referenceInfo.LineNumber))
                            {
                                impactList.Add(referenceInfo);
                            }
                        }

                        // メンバーアクセス（プロパティやメソッド呼び出し）の参照も検索
                        var memberAccesses = root.DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Where(ma => {
                                var expressionType = semanticModel.GetTypeInfo(ma.Expression).Type;
                                return expressionType != null &&
                                       SymbolEqualityComparer.Default.Equals(expressionType, typeSymbol);
                            });

                        foreach (var memberAccess in memberAccesses)
                        {
                            var linePosition = text.Lines.GetLinePosition(memberAccess.Span.Start);
                            var refLine = linePosition.Line + 1;

                            var lineSpan = text.Lines[linePosition.Line].Span;
                            var referenceText = text.ToString(lineSpan).Trim();

                            var referenceInfo = new TypeReferenceInfo
                            {
                                FilePath = RazorFileUtility.GetOriginalFilePath(doc.FilePath),
                                FileName = Path.GetFileName(RazorFileUtility.GetOriginalFilePath(doc.FilePath)),
                                LineNumber = refLine,
                                Column = linePosition.Character + 1,
                                Text = referenceText,
                                ReferenceType = $"メンバー {memberAccess.Name.Identifier.Text} の使用"
                            };

                            // 重複を避けるために追加
                            if (!impactList.Any(r =>
                                r.FilePath == referenceInfo.FilePath &&
                                r.LineNumber == referenceInfo.LineNumber))
                            {
                                impactList.Add(referenceInfo);
                            }
                        }

                        // 選択された新しい型との互換性の問題を分析
                        if (SelectedType.Value != null)
                        {
                            var compilation = await razorProject.GetCompilationAsync();
                            var newTypeSymbol = FindTypeSymbolInCompilation(SelectedType.Value.FullName, compilation);

                            if (newTypeSymbol != null)
                            {
                                // 型の互換性をチェック
                                var compatibilityIssues = CheckTypeCompatibility(typeSymbol, newTypeSymbol);

                                foreach (var issue in compatibilityIssues)
                                {
                                    // 問題に関連するコードを検索
                                    var relatedNodes = FindNodesRelatedToCompatibilityIssue(
                                        root, semanticModel, typeSymbol, issue);

                                    foreach (var node in relatedNodes)
                                    {
                                        var linePosition = text.Lines.GetLinePosition(node.Span.Start);
                                        var refLine = linePosition.Line + 1;

                                        var lineSpan = text.Lines[linePosition.Line].Span;
                                        var codeSnippet = text.ToString(lineSpan).Trim();

                                        // 潜在的な問題として追加
                                        potentialIssues.Add(new PotentialIssue
                                        {
                                            FilePath = RazorFileUtility.GetOriginalFilePath(doc.FilePath),
                                            FileName = Path.GetFileName(RazorFileUtility.GetOriginalFilePath(doc.FilePath)),
                                            LineNumber = refLine,
                                            RazorLineNumber = _mapping != null ?
                                                RazorMappingHelper.MapToRazorLine(_mapping, _extractedCSharpCode, refLine) : 0,
                                            IssueType = issue.IssueType,
                                            Description = issue.Description,
                                            SuggestedFix = issue.SuggestedFix,
                                            CodeSnippet = codeSnippet
                                        });
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ドキュメント {doc.FilePath} の解析中にエラーが発生しました: {ex.Message}");
                    }
                }

                // 結果をソート
                impactList.Sort((a, b) =>
                    string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase) != 0 ?
                    string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase) :
                    a.LineNumber.CompareTo(b.LineNumber));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"参照収集中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// コンパイレーションから型シンボルを検索します
        /// </summary>
        private INamedTypeSymbol FindTypeSymbolInCompilation(string fullTypeName, Compilation compilation)
        {
            // ジェネリック型の場合は型引数を取り除く
            string nonGenericName = fullTypeName;
            if (fullTypeName.Contains("<"))
            {
                nonGenericName = fullTypeName.Substring(0, fullTypeName.IndexOf("<"));
            }

            // メタデータ名でシンボルを検索
            var symbol = compilation.GetTypeByMetadataName(nonGenericName);
            if (symbol != null)
            {
                return symbol;
            }

            // 名前で検索（名前空間を考慮）
            var candidateSymbols = compilation.GetSymbolsWithName(
                name => name.EndsWith(Path.GetFileName(nonGenericName)),
                SymbolFilter.Type
            ).OfType<INamedTypeSymbol>();

            // 最適な候補を返す（完全一致優先）
            foreach (var candidate in candidateSymbols)
            {
                if (candidate.ToDisplayString() == fullTypeName)
                    return candidate;
            }

            // 完全一致がなければ、部分一致の最初の候補を返す
            return candidateSymbols.FirstOrDefault();
        }

        /// <summary>
        /// 2つの型の互換性をチェックし、潜在的な問題を返します
        /// </summary>
        private List<CompatibilityIssue> CheckTypeCompatibility(ITypeSymbol originalType, ITypeSymbol newType)
        {
            var issues = new List<CompatibilityIssue>();

            // 基本的な型の互換性をチェック
            if (originalType.TypeKind != newType.TypeKind)
            {
                issues.Add(new CompatibilityIssue
                {
                    IssueType = "型の種類の不一致",
                    Description = $"元の型 '{originalType.Name}' は {originalType.TypeKind} ですが、新しい型 '{newType.Name}' は {newType.TypeKind} です。",
                    SuggestedFix = "型の種類が一致するように設計を見直してください。"
                });
            }

            // クラスの場合は継承関係をチェック
            if (originalType.TypeKind == TypeKind.Class && newType.TypeKind == TypeKind.Class)
            {
                bool isCompatible = false;
                var baseType = newType;

                while (baseType != null)
                {
                    if (SymbolEqualityComparer.Default.Equals(baseType, originalType))
                    {
                        isCompatible = true;
                        break;
                    }
                    baseType = baseType.BaseType;
                }

                if (!isCompatible && !SymbolEqualityComparer.Default.Equals(originalType, newType))
                {
                    issues.Add(new CompatibilityIssue
                    {
                        IssueType = "継承関係の不一致",
                        Description = $"新しい型 '{newType.Name}' は元の型 '{originalType.Name}' を継承していません。",
                        SuggestedFix = "元の型と継承関係がある型を使用するか、アダプターパターンを検討してください。"
                    });
                }
            }

            // インターフェースの実装をチェック
            if (originalType.TypeKind == TypeKind.Interface)
            {
                bool implementsInterface = newType.AllInterfaces.Any(i =>
                    SymbolEqualityComparer.Default.Equals(i, originalType));

                if (!implementsInterface && !SymbolEqualityComparer.Default.Equals(originalType, newType))
                {
                    issues.Add(new CompatibilityIssue
                    {
                        IssueType = "インターフェース実装の不一致",
                        Description = $"新しい型 '{newType.Name}' はインターフェース '{originalType.Name}' を実装していません。",
                        SuggestedFix = "インターフェースを実装する型を選択するか、アダプターパターンを検討してください。"
                    });
                }
            }

            // メンバーの比較（メソッド、プロパティなど）
            var originalMembers = GetTypeMembers(originalType);
            var newMembers = GetTypeMembers(newType);

            // メソッドの互換性チェック
            var originalMethods = originalMembers.OfType<IMethodSymbol>()
                .Where(m => !m.IsImplicitlyDeclared && m.MethodKind != MethodKind.Constructor)
                .ToList();

            foreach (var originalMethod in originalMethods)
            {
                // オーバーロードを含めて一致するメソッドを検索
                var matchingMethods = newMembers.OfType<IMethodSymbol>()
                    .Where(m => m.Name == originalMethod.Name && m.MethodKind != MethodKind.Constructor)
                    .ToList();

                if (matchingMethods.Count == 0)
                {
                    issues.Add(new CompatibilityIssue
                    {
                        IssueType = "メソッド欠落",
                        Description = $"元の型の '{originalMethod.Name}' メソッドが新しい型にありません。",
                        SuggestedFix = $"新しい型に '{originalMethod.Name}' メソッドを実装するか、拡張メソッドを検討してください。",
                        Member = originalMethod.Name
                    });
                }
                else
                {
                    // シグネチャの互換性をチェック
                    bool hasCompatibleOverload = matchingMethods.Any(m => AreMethodSignaturesCompatible(originalMethod, m));

                    if (!hasCompatibleOverload)
                    {
                        var paramList = string.Join(", ", originalMethod.Parameters.Select(p => $"{p.Type.Name} {p.Name}"));
                        issues.Add(new CompatibilityIssue
                        {
                            IssueType = "メソッドシグネチャの不一致",
                            Description = $"メソッド '{originalMethod.Name}({paramList})' のシグネチャが一致するものが新しい型にありません。",
                            SuggestedFix = "メソッドのシグネチャを一致させるか、アダプターを実装してください。",
                            Member = originalMethod.Name
                        });
                    }
                }
            }

            // プロパティの互換性チェック
            var originalProperties = originalMembers.OfType<IPropertySymbol>()
                .Where(p => !p.IsImplicitlyDeclared)
                .ToList();

            foreach (var originalProperty in originalProperties)
            {
                var matchingProperty = newMembers.OfType<IPropertySymbol>()
                    .FirstOrDefault(p => p.Name == originalProperty.Name);

                if (matchingProperty == null)
                {
                    issues.Add(new CompatibilityIssue
                    {
                        IssueType = "プロパティ欠落",
                        Description = $"元の型の '{originalProperty.Name}' プロパティが新しい型にありません。",
                        SuggestedFix = $"新しい型に '{originalProperty.Name}' プロパティを実装するか、拡張メソッドを検討してください。",
                        Member = originalProperty.Name
                    });
                }
                else if (!SymbolEqualityComparer.Default.Equals(originalProperty.Type, matchingProperty.Type))
                {
                    issues.Add(new CompatibilityIssue
                    {
                        IssueType = "プロパティ型の不一致",
                        Description = $"プロパティ '{originalProperty.Name}' の型が元の型では '{originalProperty.Type.Name}' ですが、新しい型では '{matchingProperty.Type.Name}' です。",
                        SuggestedFix = "型変換ロジックを追加するか、代替のプロパティを検討してください。",
                        Member = originalProperty.Name
                    });
                }
            }

            // イベントの互換性チェック
            var originalEvents = originalMembers.OfType<IEventSymbol>()
                .Where(e => !e.IsImplicitlyDeclared)
                .ToList();

            foreach (var originalEvent in originalEvents)
            {
                var matchingEvent = newMembers.OfType<IEventSymbol>()
                    .FirstOrDefault(e => e.Name == originalEvent.Name);

                if (matchingEvent == null)
                {
                    issues.Add(new CompatibilityIssue
                    {
                        IssueType = "イベント欠落",
                        Description = $"元の型の '{originalEvent.Name}' イベントが新しい型にありません。",
                        SuggestedFix = $"新しい型に '{originalEvent.Name}' イベントを実装するか、独自のイベント処理を実装してください。",
                        Member = originalEvent.Name
                    });
                }
                else if (!SymbolEqualityComparer.Default.Equals(originalEvent.Type, matchingEvent.Type))
                {
                    issues.Add(new CompatibilityIssue
                    {
                        IssueType = "イベント型の不一致",
                        Description = $"イベント '{originalEvent.Name}' の型が元の型では '{originalEvent.Type.Name}' ですが、新しい型では '{matchingEvent.Type.Name}' です。",
                        SuggestedFix = "アダプターまたはラッパーを実装して、イベントハンドラーの互換性を確保してください。",
                        Member = originalEvent.Name
                    });
                }
            }

            // より一般的な固有メンバーのチェック
            var originalSpecificMembers = originalMembers
                .Where(m => !m.IsImplicitlyDeclared &&
                            !newMembers.Any(nm => nm.Name == m.Name))
                .ToList();

            // 解析対象のコードを取得
            string codeToAnalyze = _extractedCSharpCode ?? string.Empty;

            foreach (var specificMember in originalSpecificMembers)
            {
                // 重要なメンバー（プロパティやパブリックメソッド）のみを対象にする
                if (specificMember.DeclaredAccessibility == Accessibility.Public ||
                    specificMember.DeclaredAccessibility == Accessibility.Internal)
                {
                    // 実際にそのメンバーが使用されているかどうかをチェック
                    bool isUsed = codeToAnalyze.Contains($".{specificMember.Name}") ||
                                  codeToAnalyze.Contains($"{originalType.Name}.{specificMember.Name}");

                    if (isUsed)
                    {
                        issues.Add(new CompatibilityIssue
                        {
                            IssueType = "メンバーアクセスの不一致",
                            Description = $"元の型の '{specificMember.Name}' メンバーは新しい型には存在しません。",
                            SuggestedFix = $"'{specificMember.Name}' にアクセスするコードを修正するか、代替手段を実装してください。",
                            Member = specificMember.Name
                        });
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// 互換性の問題に関連するノードを検索
        /// </summary>
        private List<SyntaxNode> FindNodesRelatedToCompatibilityIssue(
            SyntaxNode root,
            SemanticModel semanticModel,
            ITypeSymbol originalType,
            CompatibilityIssue issue)
        {
            var result = new List<SyntaxNode>();

            // メンバーの参照が指定されている場合
            if (!string.IsNullOrEmpty(issue.Member))
            {
                // メソッド呼び出しを検索
                var methodCalls = root.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Where(i => {
                        if (i.Expression is MemberAccessExpressionSyntax memberAccess)
                        {
                            // 型.メソッド() の形式の呼び出し
                            if (memberAccess.Name.Identifier.Text == issue.Member)
                            {
                                var expressionType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
                                return expressionType != null &&
                                       expressionType.ToString() == originalType.ToString();
                            }
                        }
                        else if (i.Expression is IdentifierNameSyntax identifier &&
                                 identifier.Identifier.Text == issue.Member)
                        {
                            // 単独のメソッド呼び出し（例：this内やusing staticなど）
                            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                            if (symbol is IMethodSymbol methodSymbol)
                            {
                                return methodSymbol.ContainingType != null &&
                                       methodSymbol.ContainingType.ToString() == originalType.ToString();
                            }
                        }
                        return false;
                    })
                    .ToList();

                result.AddRange(methodCalls);

                // プロパティアクセスを検索
                var propertyAccesses = root.DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Where(m =>
                    {
                        var issueMember = issue.Member;
                        if (issueMember.StartsWith("get_"))
                            issueMember = issueMember.Remove(0, 4);
                        else if (issueMember.StartsWith("set_"))
                            issueMember = issueMember.Remove(0, 4);
                        if (m.Name.Identifier.Text == issueMember)
                        {
                            var expressionType = semanticModel.GetTypeInfo(m.Expression).Type;
                            return expressionType != null &&
                                   expressionType.ToString() == originalType.ToString();
                        }
                        return false;
                    })
                    .ToList();

                result.AddRange(propertyAccesses);

                // イベント参照を検索
                var eventReferences = root.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Where(a => {
                        if (a.Left is MemberAccessExpressionSyntax memberAccess &&
                            memberAccess.Name.Identifier.Text == issue.Member)
                        {
                            var expressionType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
                            return expressionType != null &&
                                   expressionType.ToString() == originalType.ToString();
                        }
                        return false;
                    })
                    .ToList();

                result.AddRange(eventReferences);
            }
            else
            {
                // 特定のメンバーが指定されていない場合、型の参照全般を検索
                var typeReferences = root.DescendantNodes()
                    .OfType<TypeSyntax>()
                    .Where(t => {
                        var symbolInfo = semanticModel.GetSymbolInfo(t);
                        return symbolInfo.Symbol != null &&
                               symbolInfo.Symbol.ToString() == originalType.ToString();
                    })
                    .ToList();

                result.AddRange(typeReferences);
            }

            return result;
        }

        /// <summary>
        /// 互換性の問題を表すクラス
        /// </summary>
        private class CompatibilityIssue
        {
            public string IssueType { get; set; }
            public string Description { get; set; }
            public string SuggestedFix { get; set; }
            public string Member { get; set; } // 問題のあるメンバー名（オプション）
        }

        /// <summary>
        /// 型シンボルに対する影響分析（プロジェクト指定版）
        /// </summary>
        private async Task ShowImpactForSymbolInProject(ISymbol symbol, Microsoft.CodeAnalysis.Project project)
        {
            try
            {
                // 参照の一覧を構築
                var impactList = new List<TypeReferenceInfo>();

                // Razorファイルの場合は生成コードも検索
                var generatedDocs = project.Solution.Projects
                    .SelectMany(p => p.Documents)
                    .Where(d => d.FilePath != null && (
                        d.FilePath.Contains(".razor.g.cs") ||
                        d.FilePath.Contains(".cshtml.g.cs") ||
                        (d.FilePath.Contains(".razor.") && d.FilePath.EndsWith(".cs")) ||
                        (d.FilePath.Contains(".cshtml.") && d.FilePath.EndsWith(".cs"))
                    )).ToList();

                // 通常のコードファイルも検索対象に含める
                var csharpDocs = project.Documents
                    .Where(d => d.FilePath != null && d.FilePath.EndsWith(".cs") &&
                           !generatedDocs.Any(gd => gd.FilePath == d.FilePath))
                    .ToList();

                var allDocs = generatedDocs.Concat(csharpDocs).ToList();

                foreach (var doc in allDocs)
                {
                    var text = await doc.GetTextAsync();
                    var root = await doc.GetSyntaxRootAsync();
                    var semanticModel = await doc.GetSemanticModelAsync();

                    // 型名の出現を検索
                    var typeNodes = root.DescendantNodes()
                        .OfType<TypeSyntax>()
                        .Where(t => {
                            var symbolInfo = semanticModel.GetSymbolInfo(t);
                            return symbolInfo.Symbol != null &&
                                   SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, symbol);
                        })
                        .ToList();

                    foreach (var typeNode in typeNodes)
                    {
                        var linePosition = text.Lines.GetLinePosition(typeNode.Span.Start);
                        var refLine = linePosition.Line + 1;

                        var lineSpan = text.Lines[linePosition.Line].Span;
                        var referenceText = text.ToString(lineSpan).Trim();

                        string referenceType = "型の参照";

                        // 参照種別の詳細を取得
                        var parent = typeNode.Parent;
                        if (parent is ParameterSyntax)
                        {
                            referenceType = "パラメータの型";
                        }
                        else if (parent is VariableDeclarationSyntax)
                        {
                            referenceType = "変数の型";
                        }
                        else if (parent is PropertyDeclarationSyntax)
                        {
                            referenceType = "プロパティの型";
                        }
                        else if (parent is MethodDeclarationSyntax)
                        {
                            referenceType = "メソッドの戻り値型";
                        }

                        impactList.Add(new TypeReferenceInfo
                        {
                            FilePath = doc.FilePath,
                            FileName = Path.GetFileName(doc.FilePath),
                            LineNumber = refLine,
                            Column = linePosition.Character + 1,
                            Text = referenceText,
                            ReferenceType = referenceType
                        });
                    }
                }

                // 潜在的な問題の分析
                var potentialIssues = await AnalyzePotentialIssues(symbol, _originalTypeSymbol, SelectedType.Value);
                var rcPotentialIssues = new ReactiveCollection<PotentialIssue>();
                rcPotentialIssues.AddRange(potentialIssues);

                // ソート：生成コードではないファイル優先、そして行番号順
                impactList = impactList
                    .OrderBy(r => r.FilePath.Contains(".g.cs")) // 生成コードではないファイルを優先
                    .ThenBy(r => r.FileName)
                    .ThenBy(r => r.LineNumber)
                    .ToList();

                // 影響範囲ダイアログを表示
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var dialog = new ImpactAnalysisDialog
                {
                    DataContext = new ImpactAnalysisViewModel
                    {
                        OriginalTypeName = _originalTypeSymbol.ToDisplayString(),
                        NewTypeName = SelectedType.Value.DisplayName,
                        ReferencesCount = impactList.Count,
                        References = impactList,
                        PotentialIssues = rcPotentialIssues
                    }
                };

                (dialog.DataContext as ImpactAnalysisViewModel).OnDialogOpened(dialog);
                dialog.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ShowImpactForSymbolInProject: {ex.Message}");
                MessageBox.Show($"影響範囲の分析中にエラーが発生しました: {ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
                    var methodContext = containingMethod != null ? containingMethod.Identifier.Text : "不明";

                    // 行のコードテキストを取得
                    var lineText = await GetLineTextAsync(location.Document, lineSpan.StartLinePosition.Line);

                    // Razorファイルの行番号をマッピング（コード内容も使用）
                    int razorLine = await RazorMappingHelper.FindRazorLineByCode(_mapping, _extractedCSharpCode, line, lineText, location.Document);

                    var referenceInfo = new TypeReferenceInfo
                    {
                        FilePath = RazorFileUtility.GetOriginalFilePath(location.Document.FilePath),
                        FileName = RazorFileUtility.GetOriginalFilePath(Path.GetFileName(location.Document.FilePath)),
                        LineNumber = line,
                        RazorLineNumber = razorLine,
                        Column = lineSpan.StartLinePosition.Character + 1,
                        Text = lineText,
                        ReferenceType = $"{(symbol is IParameterSymbol ? "パラメータ" : "変数")}の使用 ({methodContext}内)"
                    };

                    impactList.Add(referenceInfo);
                }
            }

            // 潜在的な問題の分析を追加
            var potentialIssues = await AnalyzePotentialIssues(symbol, _originalTypeSymbol, SelectedType.Value);
            var rcPotentialIssues = new ReactiveCollection<PotentialIssue>();
            rcPotentialIssues.AddRange(potentialIssues);

            // 影響範囲ダイアログを表示
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dialog = new ImpactAnalysisDialog
            {
                DataContext = new ImpactAnalysisViewModel
                {
                    OriginalTypeName = _originalTypeSymbol.ToDisplayString(),
                    NewTypeName = SelectedType.Value.DisplayName,
                    ReferencesCount = impactList.Count,
                    References = impactList,
                    PotentialIssues = rcPotentialIssues,
                    // マッピング情報を渡す
                    RazorFilePath = _razorFilePath,
                    Mapping = _mapping,
                    ExtractedCSharpCode = _extractedCSharpCode
                }
            };

            (dialog.DataContext as ImpactAnalysisViewModel).OnDialogOpened(dialog);
            dialog.Show();
        }

        private async Task<string> GetLineTextAsync(Document document, int lineNumber)
        {
            var text = await document.GetTextAsync();
            var line = text.Lines[lineNumber];
            return line.ToString();
        }

        private async Task<List<PotentialIssue>> AnalyzePotentialIssues(ISymbol symbol, ITypeSymbol originalType,
            TypeHierarchyAnalyzer.TypeHierarchyInfo newTypeInfo)
        {
            var issues = new List<PotentialIssue>();

            // Roslyn APIを使用してシンボルから型情報を取得
            var solution = _document.Project.Solution;
            var compilation = await _document.Project.GetCompilationAsync();

            // 元の型と新しい型のシンボルを取得
            var newTypeSymbol = GetTypeSymbolFromTypeInfo(newTypeInfo, compilation);

            if (newTypeSymbol == null)
            {
                return issues;
            }

            // 元の型のメンバーを取得
            var originalMembers = GetTypeMembers(originalType);
            var newMembers = GetTypeMembers(newTypeSymbol);

            // 欠落しているメソッドのチェック
            foreach (var member in originalMembers.Where(m => m.Kind == SymbolKind.Method))
            {
                var method = (IMethodSymbol)member;

                // 同名の新しいメソッドを検索
                var correspondingMethods = newMembers
                    .Where(m => m.Kind == SymbolKind.Method && m.Name == method.Name)
                    .Cast<IMethodSymbol>()
                    .ToList();

                if (correspondingMethods.Count == 0)
                {
                    // メソッドが見つからない - 最も深刻な問題
                    var references = await SymbolFinder.FindReferencesAsync(method, solution);

                    foreach (var reference in references)
                    {
                        foreach (var location in reference.Locations)
                        {
                            // 参照ごとに問題を追加
                            issues.Add(await CreateMethodMissingIssue(method, location));
                        }
                    }
                }
                else
                {
                    // 互換性のあるオーバーロードを検索
                    var foundCompatibleOverload = false;
                    foreach (var overload in correspondingMethods)
                    {
                        if (AreMethodSignaturesCompatible(method, overload))
                        {
                            foundCompatibleOverload = true;
                            break;
                        }
                    }

                    if (!foundCompatibleOverload)
                    {
                        // 互換性のあるオーバーロードが見つからなかった
                        var references = await SymbolFinder.FindReferencesAsync(method, solution);

                        foreach (var reference in references)
                        {
                            foreach (var location in reference.Locations)
                            {
                                // 一番似ているオーバーロードを選んで問題を表示
                                var bestMatch = FindBestMatchingOverload(method, correspondingMethods);
                                issues.Add(await CreateMethodSignatureIssue(method, bestMatch, location));
                            }
                        }
                    }
                }
            }

            // プロパティの不一致をチェック
            foreach (var member in originalMembers.Where(m => m.Kind == SymbolKind.Property))
            {
                var property = (IPropertySymbol)member;

                // 同名の新しいプロパティを検索
                var correspondingProperty = newMembers
                    .Where(m => m.Kind == SymbolKind.Property && m.Name == property.Name)
                    .Cast<IPropertySymbol>()
                    .FirstOrDefault();

                if (correspondingProperty == null)
                {
                    // プロパティが見つからない
                    var references = await SymbolFinder.FindReferencesAsync(property, solution);

                    foreach (var reference in references)
                    {
                        foreach (var location in reference.Locations)
                        {
                            issues.Add(await CreatePropertyMissingIssue(property, location));
                        }
                    }
                }
                else if (!IsTypeCompatible(property.Type, correspondingProperty.Type))
                {
                    // プロパティの型が互換性ない
                    var references = await SymbolFinder.FindReferencesAsync(property, solution);

                    foreach (var reference in references)
                    {
                        foreach (var location in reference.Locations)
                        {
                            issues.Add(await CreatePropertyTypeIssue(property, correspondingProperty, location));
                        }
                    }
                }
            }

            // イベントの不一致をチェック
            foreach (var member in originalMembers.Where(m => m.Kind == SymbolKind.Event))
            {
                var eventSymbol = (IEventSymbol)member;

                // 同名の新しいイベントを検索
                var correspondingEvent = newMembers
                    .Where(m => m.Kind == SymbolKind.Event && m.Name == eventSymbol.Name)
                    .Cast<IEventSymbol>()
                    .FirstOrDefault();

                if (correspondingEvent == null)
                {
                    // イベントが見つからない場合
                    var references = await SymbolFinder.FindReferencesAsync(eventSymbol, solution);

                    foreach (var reference in references)
                    {
                        foreach (var location in reference.Locations)
                        {
                            issues.Add(await CreateEventMissingIssue(eventSymbol, location));
                        }
                    }
                }
                else if (!AreEventTypesCompatible(eventSymbol, correspondingEvent))
                {
                    // イベントの型が互換性ない場合
                    var references = await SymbolFinder.FindReferencesAsync(eventSymbol, solution);

                    foreach (var reference in references)
                    {
                        foreach (var location in reference.Locations)
                        {
                            issues.Add(await CreateEventTypeIssue(eventSymbol, correspondingEvent, location));
                        }
                    }
                }
                else if (eventSymbol.DeclaredAccessibility != correspondingEvent.DeclaredAccessibility)
                {
                    // アクセシビリティが異なる場合
                    var references = await SymbolFinder.FindReferencesAsync(eventSymbol, solution);

                    foreach (var reference in references)
                    {
                        foreach (var location in reference.Locations)
                        {
                            issues.Add(await CreateEventAccessibilityIssue(eventSymbol, correspondingEvent, location));
                        }
                    }
                }
            }

            return issues;
        }

        /// <summary>
        ///     元のメソッドに最も近いオーバーロードを見つける
        /// </summary>
        private IMethodSymbol FindBestMatchingOverload(IMethodSymbol originalMethod, List<IMethodSymbol> overloads)
        {
            // 戻り値の型が一致するものを優先
            var sameReturnType = overloads.Where(o =>
                SymbolEqualityComparer.Default.Equals(o.ReturnType, originalMethod.ReturnType)).ToList();

            if (sameReturnType.Count > 0)
            {
                overloads = sameReturnType;
            }

            // パラメータ数が同じものを優先
            var sameParamCount = overloads.Where(o => o.Parameters.Length == originalMethod.Parameters.Length).ToList();

            if (sameParamCount.Count > 0)
            {
                return sameParamCount[0];
            }

            // パラメータ数が最も近いものを選択
            return overloads.OrderBy(o => Math.Abs(o.Parameters.Length - originalMethod.Parameters.Length)).First();
        }

        /// <summary>
        ///     イベントのアクセシビリティ不一致に関する問題を作成
        /// </summary>
        private async Task<PotentialIssue> CreateEventAccessibilityIssue(IEventSymbol originalEvent,
            IEventSymbol newEvent, ReferenceLocation location)
        {
            var lineSpan = location.Location.GetLineSpan();
            var filePath = location.Document.FilePath;

            // 生成コードの行番号
            int generatedCodeLine = lineSpan.StartLinePosition.Line + 1;

            // Razorファイルの行番号計算
            int razorLine = generatedCodeLine;

            if (_mapping != null && _extractedCSharpCode != null)
            {
                // マッピング情報があれば使用
                if (_mapping.TryGetValue(generatedCodeLine, out var position))
                {
                    // マッピング位置からRazorファイルの行番号を計算
                    razorLine = GetLineNumberFromPosition(_extractedCSharpCode, position);
                }
            }

            return new PotentialIssue
            {
                FilePath = RazorFileUtility.GetOriginalFilePath(filePath),
                FileName = RazorFileUtility.GetOriginalFilePath(Path.GetFileName(filePath)),
                LineNumber = razorLine, // Razorファイルの行番号を使用
                IssueType = "イベントアクセシビリティの不一致",
                Description =
                    $"イベント '{originalEvent.Name}' のアクセシビリティが異なります: '{originalEvent.DeclaredAccessibility}' → '{newEvent.DeclaredAccessibility}'",
                SuggestedFix = "アクセシビリティの変更により、一部のコードで参照できなくなる可能性があります。コードの構造を見直すか、アクセサメソッドの実装を検討してください。",
                CodeSnippet = await GetCodeSnippet(location.Document, lineSpan.StartLinePosition.Line)
            };
        }

        // イベントの互換性を検査するサンプルコード
        public async Task<List<string>> CheckEventCompatibility(INamedTypeSymbol originalType, INamedTypeSymbol newType,
            Solution solution)
        {
            var issues = new List<string>();

            // 元の型のすべてのイベントを取得
            var originalEvents = originalType.GetMembers()
                .Where(m => m.Kind == SymbolKind.Event)
                .Cast<IEventSymbol>()
                .ToList();

            // 新しい型のすべてのイベントを取得
            var newEvents = newType.GetMembers()
                .Where(m => m.Kind == SymbolKind.Event)
                .Cast<IEventSymbol>()
                .ToList();

            // 各イベントをチェック
            foreach (var originalEvent in originalEvents)
            {
                // 新しい型に同名のイベントがあるか確認
                var correspondingEvent = newEvents.FirstOrDefault(e => e.Name == originalEvent.Name);

                if (correspondingEvent == null)
                {
                    // イベントが見つからない場合
                    issues.Add($"イベント '{originalEvent.Name}' は新しい型に存在しません");

                    // このイベントへの参照を探す
                    var references = await SymbolFinder.FindReferencesAsync(originalEvent, solution);
                    foreach (var reference in references)
                    {
                        issues.Add($"  - 参照箇所: {reference.Definition.Name}, {reference.Locations.Count()}箇所");
                    }
                }
                else
                {
                    // イベントデリゲート型の互換性を確認
                    if (!AreEventTypesCompatible(originalEvent, correspondingEvent))
                    {
                        issues.Add($"イベント '{originalEvent.Name}' のデリゲート型が不一致: " +
                                   $"'{originalEvent.Type.ToDisplayString()}' → '{correspondingEvent.Type.ToDisplayString()}'");
                    }

                    // アクセシビリティの違いをチェック
                    if (originalEvent.DeclaredAccessibility != correspondingEvent.DeclaredAccessibility)
                    {
                        issues.Add($"イベント '{originalEvent.Name}' のアクセシビリティが異なります: " +
                                   $"'{originalEvent.DeclaredAccessibility}' → '{correspondingEvent.DeclaredAccessibility}'");
                    }
                }
            }

            return issues;
        }

        // イベントデリゲート型の互換性チェック
        private bool AreEventTypesCompatible(IEventSymbol originalEvent, IEventSymbol newEvent)
        {
            // 同じ型は互換性あり
            if (SymbolEqualityComparer.Default.Equals(originalEvent.Type, newEvent.Type))
            {
                return true;
            }

            // デリゲート型を取得
            if (originalEvent.Type is INamedTypeSymbol originalDelegateType &&
                newEvent.Type is INamedTypeSymbol newDelegateType)
            {
                // デリゲートメソッド（Invoke）のシグネチャを比較
                var originalInvokeMethod = originalDelegateType.DelegateInvokeMethod;
                var newInvokeMethod = newDelegateType.DelegateInvokeMethod;

                if (originalInvokeMethod == null || newInvokeMethod == null)
                {
                    return false;
                }

                // 戻り値の型をチェック
                if (!SymbolEqualityComparer.Default.Equals(originalInvokeMethod.ReturnType, newInvokeMethod.ReturnType))
                {
                    return false;
                }

                // パラメータの数が違う場合は互換性なし
                if (originalInvokeMethod.Parameters.Length != newInvokeMethod.Parameters.Length)
                {
                    return false;
                }

                // 各パラメータの型をチェック
                for (var i = 0; i < originalInvokeMethod.Parameters.Length; i++)
                {
                    var originalParam = originalInvokeMethod.Parameters[i];
                    var newParam = newInvokeMethod.Parameters[i];

                    // パラメータの型が互換性ない場合
                    if (!SymbolEqualityComparer.Default.Equals(originalParam.Type, newParam.Type))
                    {
                        return false;
                    }
                }

                // すべてのチェックをパスしたら互換性あり
                return true;
            }

            return false;
        }

        private async Task<PotentialIssue> CreateEventMissingIssue(IEventSymbol eventSymbol, ReferenceLocation location)
        {
            var lineSpan = location.Location.GetLineSpan();
            var filePath = location.Document.FilePath;

            // 生成コードの行番号
            int generatedCodeLine = lineSpan.StartLinePosition.Line + 1;

            // Razorファイルの行番号計算
            int razorLine = generatedCodeLine;

            if (_mapping != null && _extractedCSharpCode != null)
            {
                // マッピング情報があれば使用
                if (_mapping.TryGetValue(generatedCodeLine, out var position))
                {
                    // マッピング位置からRazorファイルの行番号を計算
                    razorLine = GetLineNumberFromPosition(_extractedCSharpCode, position);
                }
            }

            return new PotentialIssue
            {
                FilePath = RazorFileUtility.GetOriginalFilePath(filePath),
                FileName = RazorFileUtility.GetOriginalFilePath(Path.GetFileName(filePath)),
                LineNumber = razorLine, // Razorファイルの行番号を使用
                IssueType = "イベント欠落",
                Description = $"イベント '{eventSymbol.Name}' は新しい型に存在しません。",
                SuggestedFix = "新しい型に対応するイベントを実装するか、カスタムイベントハンドラーを使用してイベントをエミュレートすることを検討してください。",
                CodeSnippet = await GetCodeSnippet(location.Document, lineSpan.StartLinePosition.Line)
            };
        }

        private async Task<PotentialIssue> CreateEventTypeIssue(IEventSymbol originalEvent, IEventSymbol newEvent,
            ReferenceLocation location)
        {
            var lineSpan = location.Location.GetLineSpan();
            var filePath = location.Document.FilePath;

            // 生成コードの行番号
            int generatedCodeLine = lineSpan.StartLinePosition.Line + 1;

            // Razorファイルの行番号計算
            int razorLine = generatedCodeLine;

            if (_mapping != null && _extractedCSharpCode != null)
            {
                // マッピング情報があれば使用
                if (_mapping.TryGetValue(generatedCodeLine, out var position))
                {
                    // マッピング位置からRazorファイルの行番号を計算
                    razorLine = GetLineNumberFromPosition(_extractedCSharpCode, position);
                }
            }

            return new PotentialIssue
            {
                FilePath = RazorFileUtility.GetOriginalFilePath(filePath),
                FileName = RazorFileUtility.GetOriginalFilePath(Path.GetFileName(filePath)),
                LineNumber = razorLine, // Razorファイルの行番号を使用
                IssueType = "イベント型の不一致",
                Description =
                    $"イベント '{originalEvent.Name}' のデリゲート型が異なります: '{originalEvent.Type}' → '{newEvent.Type}'",
                SuggestedFix = "イベントハンドラーに適応するためのアダプターメソッドの実装を検討してください。",
                CodeSnippet = await GetCodeSnippet(location.Document, lineSpan.StartLinePosition.Line)
            };
        }

        private ITypeSymbol GetTypeSymbolFromTypeInfo(TypeHierarchyAnalyzer.TypeHierarchyInfo typeInfo,
            Compilation compilation)
        {
            // 名前空間が指定されている場合はそれを使用
            if (!string.IsNullOrEmpty(typeInfo.RequiredNamespace))
            {
                var fullName = $"{typeInfo.RequiredNamespace}.{typeInfo.DisplayName}";

                // ジェネリック型の場合は`1などのメタデータ表記に変換
                var metadataName = ConvertToMetadataName(fullName);
                var typeSymbol = compilation.GetTypeByMetadataName(metadataName);
                if (typeSymbol != null)
                {
                    return typeSymbol;
                }
            }

            // フルネームをそのまま使用
            var metadataFullName = ConvertToMetadataName(typeInfo.FullName);
            return compilation.GetTypeByMetadataName(metadataFullName);
        }

        // ジェネリック型をメタデータ名に変換するヘルパー
        private string ConvertToMetadataName(string typeName)
        {
            // 例: "List<T>" -> "List`1"
            if (typeName.Contains("<"))
            {
                var startIdx = typeName.IndexOf('<');
                var endIdx = typeName.LastIndexOf('>');

                if (startIdx > 0 && endIdx > startIdx)
                {
                    var baseName = typeName.Substring(0, startIdx);
                    var typeParams = typeName.Substring(startIdx + 1, endIdx - startIdx - 1);

                    // カンマの数をカウントして型パラメータの数を計算
                    var paramCount = typeParams.Count(c => c == ',') + 1;

                    return $"{baseName}`{paramCount}";
                }
            }

            return typeName;
        }

        private async Task<INamedTypeSymbol> GetTypeSymbolFromFullName(string fullTypeName, Compilation compilation)
        {
            // ジェネリック型かどうかを判断
            var isGenericType = fullTypeName.Contains("<");

            if (!isGenericType)
            {
                // 非ジェネリック型の場合は直接取得
                return compilation.GetTypeByMetadataName(fullTypeName);
            }

            // ジェネリック型の場合、型引数を抽出
            var genericStart = fullTypeName.IndexOf('<');
            var genericEnd = fullTypeName.LastIndexOf('>');

            if (genericStart < 0 || genericEnd < 0 || genericEnd <= genericStart)
            {
                return null;
            }

            // 基本型名（例：System.Collections.Generic.ICollection）
            var baseTypeName = fullTypeName.Substring(0, genericStart);

            // 型引数部分（例：<int>の中身）
            var typeArgsString = fullTypeName.Substring(genericStart + 1, genericEnd - genericStart - 1);

            // 型引数を分割（複数の場合はカンマで区切られている）
            var typeArgNames = typeArgsString.Split(',').Select(arg => arg.Trim()).ToArray();

            // 正しいメタデータ名を取得
            var metadataTypeName = baseTypeName;
            if (isGenericType)
            {
                // 型パラメータの数を数える
                var typeParamCount = typeArgNames.Length;
                metadataTypeName = $"{baseTypeName}`{typeParamCount}";
            }

            // 型シンボルを取得
            var baseType = compilation.GetTypeByMetadataName(metadataTypeName);
            if (baseType == null)
            {
                return null;
            }

            // 型引数のシンボルを取得
            var typeArgs = new List<ITypeSymbol>();
            foreach (var argName in typeArgNames)
            {
                // プリミティブ型の場合の特殊処理
                ITypeSymbol argType = null;
                switch (argName.ToLowerInvariant())
                {
                    case "int":
                        argType = compilation.GetSpecialType(SpecialType.System_Int32);
                        break;
                    case "string":
                        argType = compilation.GetSpecialType(SpecialType.System_String);
                        break;
                    case "bool":
                        argType = compilation.GetSpecialType(SpecialType.System_Boolean);
                        break;
                    case "double":
                        argType = compilation.GetSpecialType(SpecialType.System_Double);
                        break;
                    case "decimal":
                        argType = compilation.GetSpecialType(SpecialType.System_Decimal);
                        break;
                    // 他のプリミティブ型もここに追加
                    default:
                        // 非プリミティブ型の場合は再帰的に取得
                        // 注意：これは入れ子のジェネリック型には対応していません
                        argType = await GetTypeSymbolFromFullName(argName, compilation);
                        break;
                }

                if (argType == null)
                {
                    return null;
                }

                typeArgs.Add(argType);
            }

            // ジェネリック型インスタンスを構築
            return baseType.Construct(typeArgs.ToArray());
        }

        private async Task<PotentialIssue> CreateMethodMissingIssue(IMethodSymbol method, ReferenceLocation location)
        {
            var lineSpan = location.Location.GetLineSpan();
            var filePath = location.Document.FilePath;

            // 生成コードの行番号
            int generatedCodeLine = lineSpan.StartLinePosition.Line + 1;

            // Razorファイルの行番号計算
            int razorLine = generatedCodeLine;

            if (_mapping != null && _extractedCSharpCode != null)
            {
                // マッピング情報があれば使用
                if (_mapping.TryGetValue(generatedCodeLine, out var position))
                {
                    // マッピング位置からRazorファイルの行番号を計算
                    razorLine = GetLineNumberFromPosition(_extractedCSharpCode, position);
                }
            }

            return new PotentialIssue
            {
                FilePath = RazorFileUtility.GetOriginalFilePath(filePath),
                FileName = RazorFileUtility.GetOriginalFilePath(Path.GetFileName(filePath)),
                LineNumber = razorLine, // Razorファイルの行番号を使用
                IssueType = "メソッド欠落",
                Description = $"メソッド '{method.Name}' は新しい型に存在しません。",
                SuggestedFix = "新しい型に対応するメソッドを実装するか、アダプターパターンを使用してください。",
                CodeSnippet = await GetCodeSnippet(location.Document, lineSpan.StartLinePosition.Line)
            };
        }

        /// <summary>
        ///     型のすべてのメンバー（メソッド、プロパティなど）を取得
        /// </summary>
        private IEnumerable<ISymbol> GetTypeMembers(ITypeSymbol typeSymbol)
        {
            var members = new List<ISymbol>();

            // 直接のメンバーを取得
            members.AddRange(typeSymbol.GetMembers());

            // インターフェース実装のメンバーも含める
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                members.AddRange(iface.GetMembers());
            }

            // 基底クラスのメンバーも含める（オプション）
            var baseType = typeSymbol.BaseType;
            while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
            {
                members.AddRange(baseType.GetMembers());
                baseType = baseType.BaseType;
            }

            // メンバーをフィルタリング（特殊なメンバーを除外）
            return members.Where(m =>
                !m.IsImplicitlyDeclared &&
                m.DeclaredAccessibility != Accessibility.Private &&
                !m.IsStatic &&
                m.Name != ".ctor" && // コンストラクタを除外
                !m.Name.StartsWith("op_")); // 演算子オーバーロードを除外
        }

        /// <summary>
        ///     2つのメソッドのシグネチャが互換性があるかチェック
        /// </summary>
        private bool AreMethodSignaturesCompatible(IMethodSymbol original, IMethodSymbol newMethod)
        {
            // パラメータ数が異なる場合は互換性なし
            if (original.Parameters.Length != newMethod.Parameters.Length)
            {
                return false;
            }

            // 戻り値の型をチェック
            if (!IsTypeCompatible(original.ReturnType, newMethod.ReturnType))
            {
                return false;
            }

            // 各パラメータの型をチェック
            for (var i = 0; i < original.Parameters.Length; i++)
            {
                var origParam = original.Parameters[i];
                var newParam = newMethod.Parameters[i];

                if (!IsTypeCompatible(origParam.Type, newParam.Type))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     型の互換性チェック
        /// </summary>
        private bool IsTypeCompatible(ITypeSymbol originalType, ITypeSymbol newType)
        {
            // 同じ型は互換性あり
            if (originalType.Equals(newType, SymbolEqualityComparer.Default))
            {
                return true;
            }

            // 数値型の場合、拡大変換は許容（int → longなど）
            if (IsNumericType(originalType) && IsNumericType(newType))
            {
                return GetNumericTypeRank(newType) >= GetNumericTypeRank(originalType);
            }

            // 継承関係のチェック
            if (originalType.TypeKind == TypeKind.Class && newType.TypeKind == TypeKind.Class)
            {
                var baseType = newType;
                while (baseType != null)
                {
                    if (baseType.Equals(originalType, SymbolEqualityComparer.Default))
                    {
                        return true;
                    }

                    baseType = baseType.BaseType;
                }
            }

            // インターフェース実装のチェック
            if (originalType.TypeKind == TypeKind.Interface)
            {
                return newType.AllInterfaces.Any(i =>
                    i.Equals(originalType, SymbolEqualityComparer.Default));
            }

            return false;
        }

        /// <summary>
        ///     数値型かどうかをチェック
        /// </summary>
        private bool IsNumericType(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        ///     数値型のランク（サイズ）を取得
        /// </summary>
        private int GetNumericTypeRank(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                    return 1;
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                    return 2;
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                    return 3;
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                    return 4;
                case SpecialType.System_Single:
                    return 5;
                case SpecialType.System_Double:
                    return 6;
                case SpecialType.System_Decimal:
                    return 7;
                default:
                    return 0;
            }
        }

        /// <summary>
        ///     メソッドシグネチャの不一致に関する問題を作成
        /// </summary>
        private async Task<PotentialIssue> CreateMethodSignatureIssue(IMethodSymbol original, IMethodSymbol newMethod,
            ReferenceLocation location)
        {
            var lineSpan = location.Location.GetLineSpan();
            var filePath = location.Document.FilePath;

            // シグネチャの違いを特定
            var incompatibilityDetails = "";

            // 戻り値の型が異なる場合
            if (!original.ReturnType.Equals(newMethod.ReturnType, SymbolEqualityComparer.Default))
            {
                incompatibilityDetails += $"戻り値の型が異なります: '{original.ReturnType}' → '{newMethod.ReturnType}' ";
            }

            // パラメータの違いを確認
            var minParamCount = Math.Min(original.Parameters.Length, newMethod.Parameters.Length);
            for (var i = 0; i < minParamCount; i++)
            {
                var origParam = original.Parameters[i];
                var newParam = newMethod.Parameters[i];

                if (!origParam.Type.Equals(newParam.Type, SymbolEqualityComparer.Default))
                {
                    incompatibilityDetails +=
                        $"パラメータ #{i + 1} ({origParam.Name}) の型が異なります: '{origParam.Type}' → '{newParam.Type}' ";
                }
            }

            // 元のメソッドにあって新しいメソッドにないパラメータ
            for (var i = minParamCount; i < original.Parameters.Length; i++)
            {
                incompatibilityDetails += $"パラメータ #{i + 1} ({original.Parameters[i].Name}) が新しいメソッドにありません。 ";
            }

            // 新しいメソッドにあって元のメソッドにないパラメータ
            for (var i = minParamCount; i < newMethod.Parameters.Length; i++)
            {
                incompatibilityDetails += $"新しいメソッドには追加のパラメータ #{i + 1} ({newMethod.Parameters[i].Name}) があります。 ";
            }

            // 生成コードの行番号
            int generatedCodeLine = lineSpan.StartLinePosition.Line + 1;

            // Razorファイルの行番号計算
            int razorLine = generatedCodeLine;

            if (_mapping != null && _extractedCSharpCode != null)
            {
                // マッピング情報があれば使用
                if (_mapping.TryGetValue(generatedCodeLine, out var position))
                {
                    // マッピング位置からRazorファイルの行番号を計算
                    razorLine = GetLineNumberFromPosition(_extractedCSharpCode, position);
                }
            }

            return new PotentialIssue
            {
                FilePath = RazorFileUtility.GetOriginalFilePath(filePath),
                FileName = RazorFileUtility.GetOriginalFilePath(Path.GetFileName(filePath)),
                LineNumber = razorLine, // Razorファイルの行番号を使用
                IssueType = "メソッドシグネチャの不一致",
                Description = $"メソッド '{original.Name}' のシグネチャが新しい型では異なります。{incompatibilityDetails}",
                SuggestedFix = "メソッド呼び出しを修正するか、アダプターを実装して互換性を確保してください。",
                CodeSnippet = await GetCodeSnippet(location.Document, lineSpan.StartLinePosition.Line)
            };
        }

        /// <summary>
        ///     指定された行のコードスニペットを取得
        /// </summary>
        private async Task<string> GetCodeSnippet(Document document, int lineNumber, int contextLines = 1)
        {
            var sourceText = await document.GetTextAsync();
            var lines = sourceText.Lines;

            // 範囲内に収める
            var startLine = Math.Max(0, lineNumber - contextLines + 1);
            var endLine = Math.Min(lines.Count - 1, lineNumber + contextLines + 1);

            var snippetBuilder = new StringBuilder();

            // 前後の行を含めてスニペットを構築
            for (var i = startLine; i <= endLine; i++)
            {
                var line = lines[i];
                var lineText = sourceText.GetSubText(line.Span).ToString();

                // 現在の行を強調表示
                if (i == lineNumber)
                {
                    snippetBuilder.AppendLine($"→ {lineText}");
                }
                else
                {
                    snippetBuilder.AppendLine($"  {lineText}");
                }
            }

            return snippetBuilder.ToString();
        }

        private async Task<PotentialIssue> CreatePropertyMissingIssue(IPropertySymbol property,
            ReferenceLocation location)
        {
            var lineSpan = location.Location.GetLineSpan();
            var filePath = location.Document.FilePath;

            // 生成コードの行番号
            int generatedCodeLine = lineSpan.StartLinePosition.Line + 1;

            // Razorファイルの行番号計算
            int razorLine = generatedCodeLine;

            if (_mapping != null && _extractedCSharpCode != null)
            {
                // マッピング情報があれば使用
                if (_mapping.TryGetValue(generatedCodeLine, out var position))
                {
                    // マッピング位置からRazorファイルの行番号を計算
                    razorLine = GetLineNumberFromPosition(_extractedCSharpCode, position);
                }
            }

            return new PotentialIssue
            {
                FilePath = RazorFileUtility.GetOriginalFilePath(filePath),
                FileName = RazorFileUtility.GetOriginalFilePath(Path.GetFileName(filePath)),
                LineNumber = razorLine, // Razorファイルの行番号を使用
                IssueType = "プロパティ欠落",
                Description = $"プロパティ '{property.Name}' は新しい型に存在しません。",
                SuggestedFix = "新しい型に対応するプロパティを実装するか、拡張メソッドを使用してプロパティ機能を再現することを検討してください。",
                CodeSnippet = await GetCodeSnippet(location.Document, lineSpan.StartLinePosition.Line)
            };
        }

        private async Task<PotentialIssue> CreatePropertyTypeIssue(IPropertySymbol original,
            IPropertySymbol newProperty, ReferenceLocation location)
        {
            var lineSpan = location.Location.GetLineSpan();
            var filePath = location.Document.FilePath;

            // 生成コードの行番号
            int generatedCodeLine = lineSpan.StartLinePosition.Line + 1;

            // Razorファイルの行番号計算
            int razorLine = generatedCodeLine;

            if (_mapping != null && _extractedCSharpCode != null)
            {
                // マッピング情報があれば使用
                if (_mapping.TryGetValue(generatedCodeLine, out var position))
                {
                    // マッピング位置からRazorファイルの行番号を計算
                    razorLine = GetLineNumberFromPosition(_extractedCSharpCode, position);
                }
            }

            return new PotentialIssue
            {
                FilePath = RazorFileUtility.GetOriginalFilePath(filePath),
                FileName = RazorFileUtility.GetOriginalFilePath(Path.GetFileName(filePath)),
                LineNumber = razorLine, // Razorファイルの行番号を使用
                IssueType = "プロパティ型の不一致",
                Description = $"プロパティ '{original.Name}' の型が異なります: '{original.Type}' → '{newProperty.Type}'",
                SuggestedFix = "型変換または拡張メソッドを使用して互換性を確保することを検討してください。",
                CodeSnippet = await GetCodeSnippet(location.Document, lineSpan.StartLinePosition.Line)
            };
        }

        public class ImpactAnalysisResult
        {
            public string OriginalType { get; set; }
            public string NewType { get; set; }
            public int TotalReferences { get; set; }
            public List<PotentialIssue> PotentialIssues { get; set; }
        }

        #endregion
    }
}