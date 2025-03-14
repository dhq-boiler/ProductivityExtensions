using boilersExtensions.TextEditor.Providers;
using boilersExtensions.TextEditor.SuggestedActions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace boilersExtensions.TextEditor.SuggestedActionsSources
{
    internal class UnusedParameterSuggestedActionsSource : IAsyncSuggestedActionsSource
    {
        private readonly UnusedParameterSuggestedActionsSourceProvider m_factory;
        private readonly ITextBuffer m_textBuffer;
        private readonly ITextView m_textView;

        // ファイルサイズの閾値 (バイト)
        private const int FILE_SIZE_THRESHOLD = 100000;

        // キャッシュのタイムアウト (ミリ秒)
        private const int CACHE_TIMEOUT_MS = 5000;

        // 最後の分析時刻
        private DateTime _lastAnalysisTime = DateTime.MinValue;

        // 最後の分析位置
        private int _lastAnalysisPosition = -1;

        // 分析結果キャッシュ
        private (bool isValid, TextExtent wordExtent) _cachedResult;

        public event EventHandler<EventArgs> SuggestedActionsChanged;

        public Task<ISuggestedActionCategorySet> GetSuggestedActionCategoriesAsync(
            ISuggestedActionCategorySet requestedActionCategories,
            SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            // 通常はnullを返すか、特定のカテゴリセットを返します
            return Task.FromResult<ISuggestedActionCategorySet>(null);
        }

        public async Task GetSuggestedActionsAsync(
            ISuggestedActionCategorySet requestedActionCategories,
            SnapshotSpan range,
            ImmutableArray<ISuggestedActionSetCollector> suggestedActionSetCollectors,
            CancellationToken cancellationToken)
        {
            // ファイルサイズをチェック
            if (m_textBuffer.CurrentSnapshot.Length > FILE_SIZE_THRESHOLD)
                return;

            var result = await TryGetWordUnderCaret(cancellationToken);
            if (result.isValid && result.wordExtent.IsSignificant)
            {
                var trackingSpan = range.Snapshot.CreateTrackingSpan(
                    result.wordExtent.Span,
                    SpanTrackingMode.EdgeInclusive);

                var removeAction = new RemoveUnusedParameterSuggestedAction(trackingSpan);

                // コレクターに追加
                foreach (var collector in suggestedActionSetCollectors)
                {
                    collector.Add(new SuggestedActionSet(PredefinedSuggestedActionCategoryNames.Refactoring, new[] { removeAction }));
                }
            }
        }

        public UnusedParameterSuggestedActionsSource(UnusedParameterSuggestedActionsSourceProvider unusedParameterSuggestedActionsSourceProvider, ITextView textView, ITextBuffer textBuffer)
        {
            m_factory = unusedParameterSuggestedActionsSourceProvider;
            m_textBuffer = textBuffer;
            m_textView = textView;
        }

        private async Task<(bool isValid, TextExtent wordExtent)> TryGetWordUnderCaret(CancellationToken cancellationToken = default)
        {
            // ファイルサイズをチェック
            if (m_textBuffer.CurrentSnapshot.Length > FILE_SIZE_THRESHOLD)
                return (false, default(TextExtent));

            // カレット位置の取得
            ITextCaret caret = m_textView.Caret;
            if (caret.Position.BufferPosition <= 0)
            {
                return (false, default(TextExtent));
            }

            var currentPosition = caret.Position.BufferPosition;

            // 前回と同じ位置でキャッシュが有効期限内ならキャッシュを使用
            var now = DateTime.Now;
            if (currentPosition.Position == _lastAnalysisPosition &&
                (now - _lastAnalysisTime).TotalMilliseconds < CACHE_TIMEOUT_MS)
            {
                return _cachedResult;
            }

            _lastAnalysisPosition = currentPosition.Position;

            try
            {
                // Get the document using the new method
                var document = await GetDocument(m_textBuffer, cancellationToken);
                if (document == null)
                {
                    _cachedResult = (false, default(TextExtent));
                    return _cachedResult;
                }

                // Get semantic model
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel == null)
                {
                    _cachedResult = (false, default(TextExtent));
                    return _cachedResult;
                }

                // Get syntax node at position
                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                var node = syntaxRoot.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(currentPosition, 0));

                // Check if the node is a parameter
                if (!(node is ParameterSyntax parameter))
                {
                    _cachedResult = (false, default(TextExtent));
                    return _cachedResult;
                }

                // Get parameter symbol
                var parameterSymbol = semanticModel.GetDeclaredSymbol(parameter);
                if (parameterSymbol == null)
                {
                    _cachedResult = (false, default(TextExtent));
                    return _cachedResult;
                }

                // Find references
                var references = await SymbolFinder.FindReferencesAsync(parameterSymbol, document.Project.Solution, cancellationToken);
                var referenceCount = references.SelectMany(r => r.Locations).Count();

                // If parameter has references, don't create an extent
                if (referenceCount > 0)
                {
                    _cachedResult = (false, default(TextExtent));
                    return _cachedResult;
                }

                var currentLine = currentPosition.GetContainingLine();
                var lineText = currentLine.GetText();

                // パラメーターリストの開始と終了位置を見つける
                int paramListStart = lineText.LastIndexOf('(', currentPosition.Position - currentLine.Start);
                int paramListEnd = lineText.IndexOf(')', currentPosition.Position - currentLine.Start);

                if (paramListStart == -1 || paramListEnd == -1)
                {
                    _cachedResult = (false, default(TextExtent));
                    return _cachedResult;
                }

                // パラメーターリスト内のテキストを取得
                string parameters = lineText.Substring(paramListStart + 1, paramListEnd - paramListStart - 1);
                var paramArray = parameters.Split(',').Select(p => p.Trim()).ToList();

                // カレット位置がどのパラメーターを指しているか特定
                int currentPos = currentPosition.Position - currentLine.Start - paramListStart - 1;
                int currentParam = -1;
                int accumulatedLength = 0;

                for (int i = 0; i < paramArray.Count; i++)
                {
                    int paramLength = paramArray[i].Length;
                    if (i > 0)
                    {
                        accumulatedLength += 2; // カンマとスペース分
                    }

                    if (currentPos >= accumulatedLength && currentPos <= accumulatedLength + paramLength)
                    {
                        currentParam = i;
                        break;
                    }

                    accumulatedLength += paramLength;
                }

                if (currentParam == -1)
                {
                    _cachedResult = (false, default(TextExtent));
                    return _cachedResult;
                }

                // パラメーターの範囲を計算
                int paramStart = paramListStart + 1;
                for (int i = 0; i < currentParam; i++)
                {
                    paramStart += paramArray[i].Length + 2; // +2 for ", "
                }

                int paramEnd = paramStart + paramArray[currentParam].Length;

                // カンマの処理
                if (currentParam < paramArray.Count - 1)
                {
                    // 後続のパラメーターがある場合、カンマとスペースも含める
                    paramEnd += 2;
                }
                else if (currentParam > 0)
                {
                    // 前のパラメーターがある場合、前のカンマとスペースも含める
                    paramStart -= 2;
                }

                // スナップショット内の正しい位置に変換
                int absoluteStart = currentLine.Start + paramStart;
                int length = paramEnd - paramStart;

                var span = new SnapshotSpan(currentPosition.Snapshot, absoluteStart, length);
                var wordExtent = new TextExtent(span, true);

                // 結果をキャッシュして返す
                _lastAnalysisTime = now;
                _cachedResult = (true, wordExtent);
                return _cachedResult;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in TryGetWordUnderCaret: {ex.Message}");
                _cachedResult = (false, default(TextExtent));
                return _cachedResult;
            }
        }

        private async Task<Document> GetDocument(ITextBuffer textBuffer, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Get the workspace from the provider
            var workspace = m_factory.Workspace;
            if (workspace == null) return null;

            // Get the text container service
            if (!textBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument textDocument))
                return null;

            var filePath = textDocument.FilePath;
            if (string.IsNullOrEmpty(filePath))
                return null;

            // Find the document in the workspace using the file path
            var document = workspace.CurrentSolution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

            return document;
        }

        IEnumerable<SuggestedActionSet> ISuggestedActionsSource.GetSuggestedActions(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> HasSuggestedActionsAsync(
            ISuggestedActionCategorySet requestedActionCategories,
            SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            // ファイルサイズをチェック
            if (m_textBuffer.CurrentSnapshot.Length > FILE_SIZE_THRESHOLD)
                return false;

            var result = await TryGetWordUnderCaret(cancellationToken);
            return result.isValid && result.wordExtent.IsSignificant;
        }

        public void Dispose()
        {
            // クリーンアップが必要な場合はここに実装
            _cachedResult = (false, default);
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            // This is a sample provider and doesn't participate in LightBulb telemetry
            telemetryId = Guid.Empty;
            return false;
        }
    }
}
