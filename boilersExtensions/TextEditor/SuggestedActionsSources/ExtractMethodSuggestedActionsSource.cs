using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using boilersExtensions.TextEditor.SuggestedActions;

namespace boilersExtensions.TextEditor.SuggestedActionsSources
{
    internal class ExtractMethodSuggestedActionsSource : ISuggestedActionsSource
    {
        private readonly ITextView _textView;
        private readonly ITextBuffer _textBuffer;

        public ExtractMethodSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer)
        {
            _textView = textView;
            _textBuffer = textBuffer;
        }

        private ITrackingSpan TryGetWordSelected(ITextSnapshot snapshot, SnapshotSpan selectedSpan)
        {
            // 選択範囲が空の場合は抽出不可
            if (selectedSpan.IsEmpty)
                return null;

            // 基本的なバリデーション
            if (!IsValidSelectionForExtraction(selectedSpan, snapshot))
                return null;

            // シンタックスツリーを取得
            var document = snapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (document == null)
                return null;

            // 選択範囲のシンタックスノードを取得
            var root = document.GetSyntaxRootAsync().Result;
            var selectedNode = root.FindNode(new TextSpan(selectedSpan.Span.Start, selectedSpan.Span.End));

            // コンパイルエラーがある場合は抽出不可
            var semanticModel = document.GetSemanticModelAsync().Result;
            var diagnostics = semanticModel.GetDiagnostics(new TextSpan(selectedSpan.Span.Start, selectedSpan.Span.End));
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return null;

            // 抽出可能な構文かチェック
            if (!IsExtractableNode(selectedNode))
                return null;

            // TrackingSpanを作成して返す
            return snapshot.CreateTrackingSpan(selectedSpan.Span, SpanTrackingMode.EdgeExclusive);
        }

        private bool IsValidSelectionForExtraction(SnapshotSpan span, ITextSnapshot snapshot)
        {
            // 選択範囲が複数行にまたがっているか確認
            var startLine = snapshot.GetLineFromPosition(span.Start);
            var endLine = snapshot.GetLineFromPosition(span.End);

            // 1行内の選択は通常メソッド抽出には適さない
            if (startLine.LineNumber == endLine.LineNumber)
                return false;

            // 選択範囲が意味のある最小単位を満たしているか
            if (span.Length < 10) // 最小文字数のしきい値
                return false;

            return true;
        }

        private bool IsExtractableNode(SyntaxNode node)
        {
            if (node == null)
                return false;

            // メソッド抽出に適さないノードをチェック
            if (node is MethodDeclarationSyntax ||
                node is PropertyDeclarationSyntax ||
                node is ClassDeclarationSyntax ||
                node is NamespaceDeclarationSyntax)
                return false;

            // 制御文の一部分だけが選択されていないかチェック
            if (IsPartialControlFlow(node))
                return false;

            // return文を含む場合の特別な処理
            if (ContainsReturnStatement(node))
            {
                // メソッドの最後のreturn文のみ許可
                if (!IsLastReturnStatement(node))
                    return false;
            }

            return true;
        }

        private bool IsPartialControlFlow(SyntaxNode node)
        {
            // if文、for文、while文などの制御構造の一部だけが選択されていないかチェック
            var controlFlowParent = node.Ancestors().FirstOrDefault(n =>
                n is IfStatementSyntax ||
                n is ForStatementSyntax ||
                n is WhileStatementSyntax ||
                n is DoStatementSyntax ||
                n is ForEachStatementSyntax);

            if (controlFlowParent != null)
            {
                // 制御構造全体が選択範囲に含まれているかチェック
                return !node.Span.Contains(controlFlowParent.Span);
            }

            return false;
        }

        private bool ContainsReturnStatement(SyntaxNode node)
        {
            return node.DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Any();
        }

        private bool IsLastReturnStatement(SyntaxNode node)
        {
            var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (method == null)
                return false;

            var lastReturn = method.DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .LastOrDefault();

            return lastReturn != null && node.Span.Contains(lastReturn.Span);
        }

        public void Dispose()
        {
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            // This is a sample provider and doesn't participate in LightBulb telemetry
            telemetryId = Guid.Empty;
            return false;
        }

        public IEnumerable<SuggestedActionSet> GetSuggestedActions(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            var trackingSpan = TryGetWordSelected(range.Snapshot, range);
            if (trackingSpan == null)
                return null;

            var action = new ExtractMethodSuggestedAction(trackingSpan);
            return new[] { new SuggestedActionSet(new[] { action }) };
        }

        public Task<bool> HasSuggestedActionsAsync(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            var trackingSpan = TryGetWordSelected(range.Snapshot, range);
            return Task.FromResult(trackingSpan != null);
        }

        public event EventHandler<EventArgs> SuggestedActionsChanged;
    }
}
