using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.Shell;
using Prism.Mvvm;
using Reactive.Bindings;
using Reactive.Bindings.Disposables;
using Reactive.Bindings.Extensions;

namespace boilersExtensions.Utils
{
    /// <summary>
    /// 型置換の影響範囲分析結果を表示するためのViewModel
    /// </summary>
    public class ImpactAnalysisViewModel : BindableBase, IDisposable
    {
        private readonly CompositeDisposable _compositeDisposable = new CompositeDisposable();

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

            // ブックマークトグルコマンド
            ToggleBookmarkCommand = new ReactiveCommand<TypeReferenceInfo>()
                .AddTo(_compositeDisposable);

            ToggleBookmarkCommand.Subscribe(reference =>
                {
                    if (reference != null)
                    {
                        ToggleBookmark(reference);
                    }
                })
                .AddTo(_compositeDisposable);
        }

        // コマンド
        public ReactiveCommand CloseCommand { get; }
        public ReactiveCommand<TypeReferenceInfo> NavigateToReferenceCommand { get; }
        public ReactiveCommand<TypeReferenceInfo> ToggleBookmarkCommand { get; }

        // 影響分析の基本情報
        public string OriginalTypeName { get; set; }
        public string NewTypeName { get; set; }
        public int ReferencesCount { get; set; }

        // 参照箇所のリスト
        public List<TypeReferenceInfo> References { get; set; } = new List<TypeReferenceInfo>();

        // 潜在的な問題のリスト
        public List<PotentialIssue> PotentialIssues { get; set; } = new List<PotentialIssue>();

        // ダイアログへの参照
        public Window Window { get; set; }

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
                    var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                    if (dte == null)
                        return;

                    // ファイルを開く
                    var window = dte.ItemOperations.OpenFile(reference.FilePath);
                    if (window != null)
                    {
                        // TextDocumentを取得
                        var textDoc = window.Document.Object("TextDocument") as EnvDTE.TextDocument;
                        if (textDoc != null)
                        {
                            // 指定した行にカーソルを移動
                            var point = textDoc.StartPoint.CreateEditPoint();
                            point.MoveToLineAndOffset(reference.LineNumber, reference.Column);

                            // 選択状態にする
                            var line = textDoc.StartPoint.CreateEditPoint();
                            line.MoveToLineAndOffset(reference.LineNumber, 1);
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
                    System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
                    MessageBox.Show($"参照箇所への移動中にエラーが発生しました: {ex.Message}",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        // ブックマークをトグルするメソッド
        private void ToggleBookmark(TypeReferenceInfo reference)
        {
            // UIスレッドで実行
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    // DTEサービスを取得
                    var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                    if (dte == null)
                        return;

                    // ファイルを開く (既に開いている場合は既存のウィンドウを取得)
                    var window = dte.ItemOperations.OpenFile(reference.FilePath);
                    if (window != null)
                    {
                        // TextDocumentを取得
                        var textDoc = window.Document.Object("TextDocument") as EnvDTE.TextDocument;
                        if (textDoc != null)
                        {
                            // 指定した行に移動
                            var point = textDoc.StartPoint.CreateEditPoint();
                            point.MoveToLineAndOffset(reference.LineNumber, 1); // 行の先頭に移動

                            // カーソルを指定位置に設定
                            textDoc.Selection.MoveToPoint(point);

                            // ブックマークをトグル
                            dte.ExecuteCommand("Edit.ToggleBookmark");

                            // UIの状態を更新
                            reference.IsBookmarked.Value = !reference.IsBookmarked.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Bookmark toggle error: {ex.Message}");
                }
            });
        }

        public void Dispose()
        {
            _compositeDisposable?.Dispose();
            CloseCommand?.Dispose();
        }

        // ダイアログが開かれたときの処理
        public void OnDialogOpened(Window window)
        {
            Window = window;
        }
    }

    /// <summary>
    /// 型参照情報を格納するクラス
    /// </summary>
    public class TypeReferenceInfo
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public int LineNumber { get; set; }
        public int Column { get; set; }
        public string Text { get; set; }
        public string ReferenceType { get; set; } // Method parameter, variable declaration, etc.

        // ブックマーク状態を保持するプロパティを追加
        public ReactivePropertySlim<bool> IsBookmarked { get; } = new ReactivePropertySlim<bool>(false);
    }

    /// <summary>
    /// 型置換による潜在的な問題を表すクラス
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
    }
}
