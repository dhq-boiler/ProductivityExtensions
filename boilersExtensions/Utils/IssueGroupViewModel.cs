using System;
using System.Collections.Generic;
using System.Linq;
using Reactive.Bindings;

namespace boilersExtensions.Utils
{
    /// <summary>
    /// グループ化された問題を表すクラス
    /// </summary>
    public class IssueGroupViewModel
    {
        /// <summary>
        /// 問題の種類（MethodMissing, PropertyMissingなど）
        /// </summary>
        public string IssueType { get; set; }

        /// <summary>
        /// 問題の説明（共通）
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 修正の提案（共通）
        /// </summary>
        public string SuggestedFix { get; set; }

        /// <summary>
        /// 元の問題リスト（内部保持用）
        /// </summary>
        private List<PotentialIssue> _allIssues = new List<PotentialIssue>();

        /// <summary>
        /// グループ内の問題数（重複を含む全体数）
        /// </summary>
        public int TotalIssueCount => _allIssues.Count;

        /// <summary>
        /// ファイルパスと行番号で重複を排除した問題リスト
        /// </summary>
        public List<PotentialIssue> Issues
        {
            get
            {
                // ファイルパスと行番号の組み合わせで重複を排除
                return _allIssues
                    .GroupBy(issue => new { issue.FilePath, issue.LineNumber })
                    .Select(group => group.First())
                    .ToList();
            }
        }

        /// <summary>
        /// 問題の詳細を展開するかどうか
        /// </summary>
        public ReactivePropertySlim<bool> IsExpanded { get; } = new ReactivePropertySlim<bool>(false);

        /// <summary>
        /// 代表的なコードスニペット（最初の問題のスニペット）
        /// </summary>
        public string RepresentativeCodeSnippet => Issues.FirstOrDefault()?.CodeSnippet;

        /// <summary>
        /// 影響を受けるファイルの一覧（重複なし）
        /// </summary>
        public List<string> AffectedFiles => Issues.Select(i => i.FileName).Distinct().ToList();

        /// <summary>
        /// 影響を受けるファイル数
        /// </summary>
        public int AffectedFileCount => AffectedFiles.Count;

        /// <summary>
        /// 固有の問題箇所の数
        /// </summary>
        public int UniqueIssueCount => Issues.Count;

        /// <summary>
        /// 問題を追加
        /// </summary>
        public void AddIssue(PotentialIssue issue)
        {
            _allIssues.Add(issue);
        }

        /// <summary>
        /// 問題をまとめて追加
        /// </summary>
        public void AddIssues(IEnumerable<PotentialIssue> issues)
        {
            _allIssues.AddRange(issues);
        }
    }
}