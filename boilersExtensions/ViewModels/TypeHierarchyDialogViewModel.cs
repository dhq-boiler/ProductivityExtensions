using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows;
using boilersExtensions.Utils;
using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
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
                    Window.Close();
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
                var newTypeName = GetSimplifiedTypeName(SelectedType.Value.DisplayName);
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
        private string GetSimplifiedTypeName(string fullName)
        {
            try
            {
                if (string.IsNullOrEmpty(fullName))
                    return string.Empty;

                // ジェネリック型かどうか確認
                if (fullName.Contains("<"))
                {
                    int genericStart = fullName.IndexOf('<');

                    // ジェネリック部分を抽出 (例: System.Collections.Generic.List<int> -> System.Collections.Generic.List と <int>)
                    string baseTypeName = fullName.Substring(0, genericStart);
                    string typeParams = fullName.Substring(genericStart); // <int> 部分

                    // 名前空間を含まない型名を取得
                    int lastDot = baseTypeName.LastIndexOf('.');
                    if (lastDot >= 0)
                    {
                        baseTypeName = baseTypeName.Substring(lastDot + 1);
                    }

                    // 名前空間なしの型名 + 元のジェネリックパラメーター
                    return baseTypeName + typeParams;
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

        /// <summary>
        ///     ダイアログが開かれた時の処理
        /// </summary>
        public void OnDialogOpened(Window window) => Window = window;
    }
}