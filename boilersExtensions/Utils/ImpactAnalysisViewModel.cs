using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Prism.Mvvm;
using Reactive.Bindings;
using Reactive.Bindings.Disposables;
using Reactive.Bindings.Extensions;
using ZLinq;
using Window = System.Windows.Window;

namespace boilersExtensions.Utils
{
    /// <summary>
    ///     型置換の影響範囲分析結果を表示するためのViewModel
    /// </summary>
    public class ImpactAnalysisViewModel : BindableBase, IDisposable
    {
        private readonly CompositeDisposable _compositeDisposable = new CompositeDisposable();
        private string _razorContent;

        public ImpactAnalysisViewModel()
        {
            // ダイアログを閉じるコマンド
            CloseCommand = new ReactiveCommand()
                .AddTo(_compositeDisposable);

            CloseCommand.Subscribe(() =>
                {
                    Window?.Close();
                })
                .AddTo(_compositeDisposable);

            // 参照箇所に移動するコマンド
            NavigateToReferenceCommand = new ReactiveCommand<TypeReferenceInfo>()
                .AddTo(_compositeDisposable);

            NavigateToReferenceCommand.Subscribe(reference =>
                {
                    if (reference != null)
                    {
                        NavigateToReference(reference);
                    }
                })
                .AddTo(_compositeDisposable);

            // 特定の問題に移動するコマンド
            NavigateToIssueCommand = new ReactiveCommand<PotentialIssue>()
                .AddTo(_compositeDisposable);

            NavigateToIssueCommand.Subscribe(issue =>
                {
                    if (issue != null)
                    {
                        NavigateToIssue(issue);
                    }
                })
                .AddTo(_compositeDisposable);

            // ブックマークトグルコマンド
            ToggleBookmarkCommand = new ReactiveCommand<TypeReferenceInfo>()
                .AddTo(_compositeDisposable);

            ToggleBookmarkCommand.Subscribe(async reference =>
                {
                    if (reference != null)
                    {
                        // BookmarkManagerを使用してブックマークをトグル
                        await BookmarkManager.ToggleBookmarkAsync(reference.FilePath, reference.LineNumber);
                    }
                })
                .AddTo(_compositeDisposable);

            // Track whether we're analyzing a Razor file
            IsRazorFile.Value = !string.IsNullOrEmpty(RazorFilePath) &&
                                (RazorFilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                                 RazorFilePath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase));

            // Add disposal
            IsRazorFile.AddTo(_compositeDisposable);
        }

        // コマンド
        public ReactiveCommand CloseCommand { get; }
        public ReactiveCommand<TypeReferenceInfo> NavigateToReferenceCommand { get; }
        public ReactiveCommand<PotentialIssue> NavigateToIssueCommand { get; }
        public ReactiveCommand<TypeReferenceInfo> ToggleBookmarkCommand { get; }

        public ReactivePropertySlim<bool> IsRazorFile { get; } = new ReactivePropertySlim<bool>();

        // 影響分析の基本情報
        public string OriginalTypeName { get; set; }
        public string NewTypeName { get; set; }
        public int ReferencesCount { get; set; }

        // 参照箇所のリスト
        public List<TypeReferenceInfo> References { get; set; } = new List<TypeReferenceInfo>();

        // 潜在的な問題のリスト
        public ReactiveCollection<PotentialIssue> PotentialIssues { get; set; } =
            new ReactiveCollection<PotentialIssue>();

        // グループ化された問題のリスト
        public ReactiveCollection<IssueGroupViewModel> GroupedIssues { get; set; } =
            new ReactiveCollection<IssueGroupViewModel>();

        public ReadOnlyReactivePropertySlim<bool> HasPotentialIssues => PotentialIssues
            .CollectionChangedAsObservable()
            .Select(_ => PotentialIssues.Count > 0)
            .ToReadOnlyReactivePropertySlim(PotentialIssues.Count > 0);

        // ダイアログへの参照
        public Window Window { get; set; }
        public string RazorFilePath { get; set; }
        public Dictionary<int, int> Mapping { get; set; }
        public string ExtractedCSharpCode { get; set; }

        public void Dispose()
        {
            _compositeDisposable?.Dispose();
            CloseCommand?.Dispose();
            // その他のReactivePropertyの解放処理
            foreach (var group in GroupedIssues)
            {
                group.IsExpanded?.Dispose();
            }
        }

        // 問題をグループ化するメソッド
        public void GroupIssues()
        {
            GroupedIssues.Clear();

            // IssueTypeとDescriptionでグループ化
            var groups = PotentialIssues.AsValueEnumerable()
                .GroupBy(issue => new { issue.IssueType, issue.Description })
                .ToList();

            foreach (var group in groups)
            {
                var issueGroup = new IssueGroupViewModel
                {
                    IssueType = group.Key.IssueType,
                    Description = group.Key.Description,
                    SuggestedFix = group.AsValueEnumerable().First().SuggestedFix // グループの最初の問題から提案を取得
                };

                // 問題を追加（内部でファイルパスと行番号による重複排除が行われる）
                issueGroup.AddIssues(group.AsValueEnumerable().ToList());

                GroupedIssues.Add(issueGroup);
            }
        }

        // ImpactAnalysisViewModel内でのマッピングメソッド
        private int GetRazorLineNumber(int generatedCodeLine)
        {
            if (Mapping == null || ExtractedCSharpCode == null || string.IsNullOrEmpty(RazorFilePath))
            {
                return 0;
            }

            // マッピングから行番号を取得
            if (Mapping.TryGetValue(generatedCodeLine, out var position))
            {
                // 位置からRazorファイル内の行番号を計算
                return GetLineNumberFromPosition(ExtractedCSharpCode, position);
            }

            return 0;
        }

        // GetLineNumberFromPosition メソッド（既存または新規）
        private int GetLineNumberFromPosition(string text, int position)
        {
            if (string.IsNullOrEmpty(text) || position < 0 || position >= text.Length)
            {
                return 0;
            }

            var line = 1;
            for (var i = 0; i < position; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        // 参照箇所に移動するメソッド
        private void NavigateToReference(TypeReferenceInfo reference)
        {
            // UIスレッドで実行
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    // DTEサービスを取得
                    var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                    if (dte == null)
                    {
                        return;
                    }

                    // Verify if this is a Razor file
                    var isRazorFile = IsRazorFile.Value;

                    // ファイルを開く
                    var window = dte.ItemOperations.OpenFile(reference.FilePath);
                    if (window != null)
                    {
                        // TextDocumentを取得
                        var textDoc = window.Document.Object("TextDocument") as TextDocument;
                        if (textDoc != null)
                        {
                            // 指定した行にカーソルを移動 (ファイル種類に応じて行番号を選択)
                            var targetLine = isRazorFile && reference.RazorLineNumber > 0
                                ? reference.RazorLineNumber
                                : reference.LineNumber;

                            var point = textDoc.StartPoint.CreateEditPoint();
                            point.MoveToLineAndOffset(targetLine, reference.Column);

                            // 選択状態にする
                            var line = textDoc.StartPoint.CreateEditPoint();
                            line.MoveToLineAndOffset(targetLine, 1);
                            var lineEnd = line.CreateEditPoint();
                            lineEnd.EndOfLine();

                            // 行全体を選択
                            textDoc.Selection.MoveToPoint(line);
                            textDoc.Selection.MoveToPoint(lineEnd, true);

                            // エディタにフォーカスを設定
                            window.Activate();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Navigation error: {ex.Message}");
                    MessageBox.Show($"参照箇所への移動中にエラーが発生しました: {ex.Message}",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        // 問題箇所に移動するメソッド
        private void NavigateToIssue(PotentialIssue issue)
        {
            // UIスレッドで実行
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    // DTEサービスを取得
                    var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                    if (dte == null)
                    {
                        return;
                    }

                    // Verify if this is a Razor file
                    var isRazorFile = IsRazorFile.Value;

                    // ファイルを開く
                    var window = dte.ItemOperations.OpenFile(issue.FilePath);
                    if (window != null)
                    {
                        // TextDocumentを取得
                        var textDoc = window.Document.Object("TextDocument") as TextDocument;
                        if (textDoc != null)
                        {
                            // 指定した行にカーソルを移動 (ファイル種類に応じて行番号を選択)
                            var targetLine = isRazorFile && issue.RazorLineNumber > 0
                                ? issue.RazorLineNumber
                                : issue.LineNumber;

                            var point = textDoc.StartPoint.CreateEditPoint();
                            point.MoveToLineAndOffset(targetLine, 1);

                            // 選択状態にする
                            var line = textDoc.StartPoint.CreateEditPoint();
                            line.MoveToLineAndOffset(targetLine, 1);
                            var lineEnd = line.CreateEditPoint();
                            lineEnd.EndOfLine();

                            // 行全体を選択
                            textDoc.Selection.MoveToPoint(line);
                            textDoc.Selection.MoveToPoint(lineEnd, true);

                            // エディタにフォーカスを設定
                            window.Activate();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Navigation error: {ex.Message}");
                    MessageBox.Show($"問題箇所への移動中にエラーが発生しました: {ex.Message}",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        // ImpactAnalysisViewModel 内のメソッド
        private async Task SetupReferences(List<TypeReferenceInfo> references)
        {
            foreach (var reference in references)
            {
                // 生成コードの行番号からRazorファイルの行番号を計算
                reference.RazorLineNumber = GetRazorLineNumber(reference.LineNumber);
            }
        }

        // OnDialogOpened メソッドで呼び出す
        public async void OnDialogOpened(Window window)
        {
            Window = window;

            // Set the IsRazorFile property
            IsRazorFile.Value = !string.IsNullOrEmpty(RazorFilePath) &&
                                (RazorFilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                                 RazorFilePath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase));

            // Razor行番号を設定
            SetupRazorLineNumbers();

            // 各参照のブックマーク状態を初期化
            await InitializeBookmarkStatesAsync();

            // 問題をグループ化
            GroupIssues();
        }

        // Razor行番号を設定するメソッド
        private void SetupRazorLineNumbers()
        {
            // マッピング情報がなければ何もしない
            if (Mapping == null || string.IsNullOrEmpty(ExtractedCSharpCode) || string.IsNullOrEmpty(RazorFilePath))
            {
                return;
            }

            // Razorファイルの内容を読み込む（行番号の計算に使用）
            if (string.IsNullOrEmpty(_razorContent) && File.Exists(RazorFilePath))
            {
                try
                {
                    _razorContent = File.ReadAllText(RazorFilePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Razorファイルの読み込みエラー: {ex.Message}");
                }
            }

            foreach (var reference in References)
            {
                try
                {
                    // 1. 元のファイルパスを正確に解決
                    reference.FilePath = ResolveAccurateFilePath(reference.FilePath);
                    reference.FileName = Path.GetFileName(reference.FilePath);

                    // 2. 生成コードの行番号からRazor行番号を計算（改善されたマッピングを使用）
                    var razorLine = RazorMappingHelper.MapToRazorLine(
                        Mapping, ExtractedCSharpCode, reference.LineNumber);

                    // 3. ファイル内容を使用した代替方法を試みる
                    if (razorLine <= 0 && !string.IsNullOrEmpty(_razorContent) && !string.IsNullOrEmpty(reference.Text))
                    {
                        razorLine = TryFindLineByContent(_razorContent, reference.Text.Trim());
                    }

                    // 4. 最低でも1を設定（0は表示しない）
                    reference.RazorLineNumber = Math.Max(1, razorLine);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"参照の行番号設定エラー: {ex.Message}");
                    reference.RazorLineNumber = 1; // エラー時のデフォルト値
                }
            }

            // 潜在的な問題にも同様の処理を適用
            foreach (var issue in PotentialIssues)
            {
                try
                {
                    issue.FilePath = ResolveAccurateFilePath(issue.FilePath);
                    issue.FileName = Path.GetFileName(issue.FilePath);

                    var razorLine = RazorMappingHelper.MapToRazorLine(
                        Mapping, ExtractedCSharpCode, issue.LineNumber);

                    if (razorLine <= 0 && !string.IsNullOrEmpty(_razorContent) &&
                        !string.IsNullOrEmpty(issue.CodeSnippet))
                    {
                        razorLine = TryFindLineByContent(_razorContent, issue.CodeSnippet.Trim());
                    }

                    issue.RazorLineNumber = Math.Max(1, razorLine);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"問題の行番号設定エラー: {ex.Message}");
                    issue.RazorLineNumber = 1;
                }
            }

            Debug.WriteLine($"Razorファイルパス: {RazorFilePath}");
            Debug.WriteLine($"Razorコンテンツ: {(_razorContent != null ? $"{_razorContent.Length} バイト" : "なし")}");
            Debug.WriteLine($"マッピングエントリ数: {Mapping?.Count ?? 0}");
            Debug.WriteLine($"参照数: {References.Count}");
        }


        // ファイルパスを正確に解決するヘルパーメソッド
        private string ResolveAccurateFilePath(string originalPath)
        {
            if (string.IsNullOrEmpty(originalPath))
            {
                return string.Empty;
            }

            var resolvedPath = RazorFileUtility.GetOriginalFilePath(originalPath);

            // パスが実際に存在するか確認
            if (!File.Exists(resolvedPath) && !string.IsNullOrEmpty(RazorFilePath))
            {
                // RazorFilePathを基準にして相対パスを解決してみる
                var directory = Path.GetDirectoryName(RazorFilePath);
                var fileName = Path.GetFileName(resolvedPath);

                var candidatePath = Path.Combine(directory, fileName);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return resolvedPath;
        }

        // コンテンツに基づいて行番号を見つけるヘルパーメソッド
        private int TryFindLineByContent(string fileContent, string lineContent)
        {
            if (string.IsNullOrEmpty(fileContent) || string.IsNullOrEmpty(lineContent))
            {
                return 0;
            }

            // 特徴的な内容を抽出（短すぎる場合は無視）
            if (lineContent.Length < 10)
            {
                return 0;
            }

            var contentLines = fileContent.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);

            // 各行をチェック
            for (var i = 0; i < contentLines.Length; i++)
            {
                if (contentLines[i].Contains(lineContent))
                {
                    return i + 1; // 1ベースの行番号
                }
            }

            return 0;
        }

        // 参照のブックマーク状態を初期化するメソッド
        private async Task InitializeBookmarkStatesAsync()
        {
            foreach (var reference in References)
            {
                try
                {
                    // 現在のブックマーク状態を確認（ファイルと行番号を指定）
                    var isBookmarked = BookmarkManager.IsBookmarkSet(reference.FilePath, reference.LineNumber);
                    reference.IsBookmarked.Value = isBookmarked;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error initializing bookmark state: {ex.Message}");
                    // エラー時はデフォルトでfalse
                    reference.IsBookmarked.Value = false;
                }
            }
        }
    }

    /// <summary>
    ///     型参照情報を格納するクラス
    /// </summary>
    public class TypeReferenceInfo
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public int LineNumber { get; set; }
        public int Column { get; set; }
        public string Text { get; set; }
        public string ReferenceType { get; set; } // Method parameter, variable declaration, etc.

        // 新しく追加するプロパティ
        public int RazorLineNumber { get; set; }

        // ブックマーク状態を保持するプロパティを追加
        public ReactivePropertySlim<bool> IsBookmarked { get; } = new ReactivePropertySlim<bool>();
    }

    /// <summary>
    ///     型置換による潜在的な問題を表すクラス
    /// </summary>
    public class PotentialIssue
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public int LineNumber { get; set; }
        public string IssueType { get; set; } // MethodMissing, PropertyMissing など
        public string Description { get; set; }
        public string SuggestedFix { get; set; }
        public string CodeSnippet { get; set; }
        public int RazorLineNumber { get; set; }
    }
}