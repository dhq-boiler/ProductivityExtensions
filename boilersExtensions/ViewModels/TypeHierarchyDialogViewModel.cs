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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows;
using Document = Microsoft.CodeAnalysis.Document;
using Window = System.Windows.Window;

namespace boilersExtensions.ViewModels
{
    /// <summary>
    /// 型階層選択ダイアログのViewModel
    /// </summary>
    internal class TypeHierarchyDialogViewModel : BindableBase, IDisposable
    {
        private CompositeDisposable _compositeDisposable = new CompositeDisposable();

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

        // 処理中フラグ
        public ReactivePropertySlim<bool> IsProcessing { get; } = new ReactivePropertySlim<bool>(false);
        public ReactivePropertySlim<string> ProcessingStatus { get; } = new ReactivePropertySlim<string>("準備完了");

        // 置換対象の情報
        private ITypeSymbol _originalTypeSymbol;
        private Document _document;
        private int _position;
        private SnapshotSpan _typeSpan;
        private ITextBuffer _textBuffer;

        // ウィンドウ参照
        public Window Window { get; set; }
        public AsyncPackage Package { get; set; }

        public string Title => "型階層選択";

        public TypeHierarchyDialogViewModel()
        {
            // 選択されている型があれば、適用ボタンを有効化
            ApplyCommand = SelectedType.Select(st => st != null).ToReactiveCommand();

            // 型変更の適用
            ApplyCommand.Subscribe(async () =>
            {
                if (SelectedType.Value == null)
                    return;

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

        // 完全な型スパン情報
        private Microsoft.CodeAnalysis.Text.TextSpan _fullTypeSpan;

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

                // 元の型名を表示
                OriginalTypeName.Value = typeSymbol.ToDisplayString();

                // デバッグ情報
                System.Diagnostics.Debug.WriteLine($"InitializeAsync: Original Type={OriginalTypeName.Value}");
                System.Diagnostics.Debug.WriteLine($"Type Span: '{typeSpan.GetText()}', Full Type Span: Start={fullTypeSpan.Start}, Length={fullTypeSpan.Length}");

                // 型の候補を取得
                await RefreshTypeCandidates();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Initialize: {ex.Message}");
            }
        }

        /// <summary>
        /// 型候補のリストを更新
        /// </summary>
        private async Task RefreshTypeCandidates()
        {
            if (_originalTypeSymbol == null || _document == null)
                return;

            try
            {
                IsProcessing.Value = true;
                ProcessingStatus.Value = "型の階層を分析中...";

                // 型の階層を取得
                var candidates = await TypeHierarchyAnalyzer.GetTypeReplacementCandidatesAsync(
                    _originalTypeSymbol,
                    _document,
                    ShowBaseTypes.Value,
                    ShowDerivedTypes.Value);

                // 候補を設定
                TypeCandidates.Value = candidates;

                // 現在の型を選択状態にする
                SelectedType.Value = candidates.FirstOrDefault(t => t.FullName == _originalTypeSymbol.ToDisplayString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RefreshTypeCandidates: {ex.Message}");
            }
            finally
            {
                IsProcessing.Value = false;
                ProcessingStatus.Value = "準備完了";
            }
        }

        /// <summary>
        /// 型の変更を適用
        /// </summary>
        private async Task ApplyTypeChange()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // 選択された型が現在の型と同じなら何もしない
            if (SelectedType.Value.FullName == _originalTypeSymbol.ToDisplayString())
                return;

            // DTEのUndoContextを開始
            DTE dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
            dte.UndoContext.Open("Type Replacement");

            try
            {
                // 元の型名のスパンを取得
                var originalTypeSpan = _typeSpan;

                // 型名を置換
                string newTypeName = GetSimplifiedTypeName(SelectedType.Value.FullName);
                System.Diagnostics.Debug.WriteLine($"Replacing type: '{originalTypeSpan.GetText()}' with '{newTypeName}'");

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
        /// 必要に応じてusing文を追加
        /// </summary>
        private async Task AddRequiredUsingDirectiveAsync()
        {
            if (SelectedType.Value == null || string.IsNullOrEmpty(SelectedType.Value.RequiredNamespace))
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // ドキュメントのルートを取得
                var syntaxRoot = await _document.GetSyntaxRootAsync();
                if (syntaxRoot == null)
                    return;

                // 必要な名前空間
                var requiredNamespace = SelectedType.Value.RequiredNamespace;

                // 既存のusing文をチェック
                var existingUsings = syntaxRoot.DescendantNodes()
                    .OfType<UsingDirectiveSyntax>()
                    .Select(u => u.Name.ToString())
                    .ToList();

                // すでに追加されている場合は何もしない
                if (existingUsings.Contains(requiredNamespace))
                    return;

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
                bool success = workspace.TryApplyChanges(newDocument.Project.Solution);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in AddRequiredUsingDirectiveAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// ダイアログが開かれた時の処理
        /// </summary>
        public void OnDialogOpened(Window window)
        {
            this.Window = window;
        }

        /// <summary>
        /// リソース解放
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
    }
}