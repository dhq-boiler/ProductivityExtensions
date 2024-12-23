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
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace boilersExtensions.TextEditor.QuickInfoSources
{
    internal class UnusedParameterQuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer _textBuffer;
        private readonly VisualStudioWorkspace _workspace;

        public UnusedParameterQuickInfoSource(ITextBuffer textBuffer, VisualStudioWorkspace workspace)
        {
            _textBuffer = textBuffer;
            _workspace = workspace;
        }

        private async Task<Document> GetDocumentFromTextBuffer(ITextBuffer buffer)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Get IVsTextBuffer
            var vsTextBuffer = buffer.Properties.GetProperty<IVsTextBuffer>(typeof(IVsTextBuffer));
            if (vsTextBuffer == null) return null;

            // Get file path
            if (!(vsTextBuffer is IPersistFileFormat persistFileFormat)) return null;
            persistFileFormat.GetCurFile(out string filePath, out _);

            // Find the document in the workspace using the file path
            var documents = _workspace.CurrentSolution.Projects
                .SelectMany(p => p.Documents)
                .Where(d => d.FilePath == filePath);

            return documents.FirstOrDefault();
        }

        public async Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            try
            {
                var triggerPoint = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);
                if (triggerPoint == null) return null;

                var document = await GetDocumentFromTextBuffer(_textBuffer);
                if (document == null) return null;

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel == null) return null;

                // Get the syntax node at the trigger position
                var position = triggerPoint.Value.Position;
                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                var node = syntaxRoot.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(position, 0));

                // Check if the node is a parameter
                if (node is ParameterSyntax parameter)
                {
                    //ローカル関数のパラメーターの場合
                    if (parameter.Parent.Parent is LocalFunctionStatementSyntax local)
                    {
                        return await GenerateQuickInfoItem(cancellationToken, semanticModel, local, parameter, document);
                    }

                    //メソッドのパラメーターの場合
                    if (parameter.Parent.Parent is MethodDeclarationSyntax method)
                    {
                        return await GenerateQuickInfoItem(cancellationToken, semanticModel, method, parameter, document);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetQuickInfoItemAsync: {ex}");
            }

            return null;
        }

        private async Task<QuickInfoItem> GenerateQuickInfoItem(CancellationToken cancellationToken, SemanticModel semanticModel,
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

        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}
