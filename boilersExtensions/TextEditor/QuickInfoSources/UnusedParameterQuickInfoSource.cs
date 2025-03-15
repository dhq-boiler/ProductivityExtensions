using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.TextManager.Interop;
using TextSpan = Microsoft.CodeAnalysis.Text.TextSpan;

namespace boilersExtensions.TextEditor.QuickInfoSources
{
    internal class UnusedParameterQuickInfoSource : IAsyncQuickInfoSource
    {
        // ファイルサイズの閾値 (バイト)
        private const int FILE_SIZE_THRESHOLD = 100000;

        // キャッシュのタイムアウト (ミリ秒)
        private const int CACHE_TIMEOUT_MS = 10000;
        private readonly ITextBuffer _textBuffer;
        private readonly VisualStudioWorkspace _workspace;
        private QuickInfoItem _cachedQuickInfoItem;

        // 最後の分析時刻
        private DateTime _lastAnalysisTime = DateTime.MinValue;

        // 分析結果キャッシュ
        private Document _lastDocument;
        private int _lastPosition = -1;

        public UnusedParameterQuickInfoSource(ITextBuffer textBuffer, VisualStudioWorkspace workspace)
        {
            _textBuffer = textBuffer;
            _workspace = workspace;
        }

        public async Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session,
            CancellationToken cancellationToken)
        {
            try
            {
                var triggerPoint = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);
                if (triggerPoint == null)
                {
                    return null;
                }

                // ファイルサイズをチェック
                if (_textBuffer.CurrentSnapshot.Length > FILE_SIZE_THRESHOLD)
                {
                    return null;
                }

                // 現在の位置
                var position = triggerPoint.Value.Position;

                // キャッシュの確認
                var now = DateTime.Now;
                if (_lastPosition == position &&
                    _cachedQuickInfoItem != null &&
                    (now - _lastAnalysisTime).TotalMilliseconds < CACHE_TIMEOUT_MS)
                {
                    return _cachedQuickInfoItem;
                }

                _lastPosition = position;

                var document = await GetDocumentFromTextBuffer(_textBuffer);
                if (document == null)
                {
                    return null;
                }

                // 同じドキュメントでない場合はキャッシュをクリア
                if (_lastDocument != document)
                {
                    _lastDocument = document;
                    _cachedQuickInfoItem = null;
                }

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel == null)
                {
                    return null;
                }

                // Get the syntax node at the trigger position
                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                var node = syntaxRoot.FindNode(new TextSpan(position, 0));

                // Check if the node is a parameter
                if (node is ParameterSyntax parameter)
                {
                    //ローカル関数のパラメーターの場合
                    if (parameter.Parent.Parent is LocalFunctionStatementSyntax local)
                    {
                        var result = await GenerateQuickInfoItem(cancellationToken, semanticModel, local, parameter,
                            document);
                        _cachedQuickInfoItem = result;
                        _lastAnalysisTime = now;
                        return result;
                    }

                    //メソッドのパラメーターの場合
                    if (parameter.Parent.Parent is MethodDeclarationSyntax method)
                    {
                        var result = await GenerateQuickInfoItem(cancellationToken, semanticModel, method, parameter,
                            document);
                        _cachedQuickInfoItem = result;
                        _lastAnalysisTime = now;
                        return result;
                    }
                }

                // パラメータではない場合はキャッシュをクリア
                _cachedQuickInfoItem = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetQuickInfoItemAsync: {ex}");
            }

            return null;
        }

        public void Dispose()
        {
            // Cleanup if needed
            _cachedQuickInfoItem = null;
            _lastDocument = null;
        }

        private async Task<Document> GetDocumentFromTextBuffer(ITextBuffer buffer)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Get IVsTextBuffer
            var vsTextBuffer = buffer.Properties.GetProperty<IVsTextBuffer>(typeof(IVsTextBuffer));
            if (vsTextBuffer == null)
            {
                return null;
            }

            // Get file path
            if (!(vsTextBuffer is IPersistFileFormat persistFileFormat))
            {
                return null;
            }

            persistFileFormat.GetCurFile(out var filePath, out _);

            // Find the document in the workspace using the file path
            var documents = _workspace.CurrentSolution.Projects
                .SelectMany(p => p.Documents)
                .Where(d => d.FilePath == filePath);

            return documents.FirstOrDefault();
        }

        private async Task<QuickInfoItem> GenerateQuickInfoItem(CancellationToken cancellationToken,
            SemanticModel semanticModel,
            SyntaxNode syntaxNode, ParameterSyntax parameter, Document document)
        {
            var methodSymbol =
                semanticModel.GetDeclaredSymbol(syntaxNode);
            var parameterSymbol = semanticModel.GetDeclaredSymbol(parameter);

            if (methodSymbol != null && parameterSymbol != null)
            {
                // Find all references to the parameter within the method
                var references = await SymbolFinder.FindReferencesAsync(parameterSymbol,
                    document.Project.Solution, cancellationToken);
                var referenceCount = references.SelectMany(r => r.Locations).Count();

                if (referenceCount == 0)
                {
                    // Create tracking span for the parameter
                    var parameterSpan = _textBuffer.CurrentSnapshot.CreateTrackingSpan(
                        parameter.Span.Start, parameter.Span.Length,
                        SpanTrackingMode.EdgeInclusive);

                    // Create QuickInfo content
                    var message = $"引数 '{parameter.Identifier.Text}' はメソッド内のどこからも参照されていないので削除できます";
                    var dataElm = new ContainerElement(
                        ContainerElementStyle.Stacked,
                        new ClassifiedTextElement(
                            new ClassifiedTextRun(PredefinedClassificationTypeNames.NaturalLanguage,
                                message)
                        ));

                    return new QuickInfoItem(parameterSpan, dataElm);
                }
            }

            return null;
        }
    }
}