using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
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
        }

        // コマンド
        public ReactiveCommand CloseCommand { get; }

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
