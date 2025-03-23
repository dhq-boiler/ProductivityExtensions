using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using ZLinq;

namespace boilersExtensions.TextEditor.SuggestedActions
{
    internal class ExtractMethodSuggestedAction : ISuggestedAction
    {
        private readonly ITextSnapshot m_snapshot;
        private readonly ITrackingSpan m_span;

        public ExtractMethodSuggestedAction(ITrackingSpan span)
        {
            m_span = span;
            m_snapshot = span.TextBuffer.CurrentSnapshot;
        }

        public string DefaultMethodName => "NewMethod";

        public void Dispose()
        {
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            // This is a sample action and doesn't participate in LightBulb telemetry
            telemetryId = Guid.Empty;
            return false;
        }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<SuggestedActionSet>>(null);

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken) => Task.FromResult<object>(null);

        public void Invoke(CancellationToken cancellationToken)
        {
            var document = m_snapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (document == null)
            {
                return;
            }

            // 選択範囲のテキストを取得
            var selectedSpan = m_span.GetSpan(m_snapshot);
            var selectedText = selectedSpan.GetText();

            // シンタックスツリーを取得
            var root = document.GetSyntaxRootAsync(cancellationToken).Result;
            var semanticModel = document.GetSemanticModelAsync(cancellationToken).Result;

            // 選択範囲のノードを取得
            var selectedNode = root.FindNode(new TextSpan(selectedSpan.Span.Start, selectedSpan.Span.End));

            // メソッドの作成に必要な情報を収集
            var (parameters, localVariables) = AnalyzeVariables(selectedNode, semanticModel);
            var returnType = DetermineReturnType(selectedNode, semanticModel, localVariables);

            // 新しいメソッドの構築
            var methodDeclaration = CreateMethodDeclaration(
                DefaultMethodName,
                selectedText,
                parameters,
                returnType);

            // 元のコードをメソッド呼び出しに置き換え
            var methodInvocation = CreateMethodInvocation(
                DefaultMethodName,
                parameters,
                returnType != "void");

            // 変更を適用
            using (var edit = m_snapshot.TextBuffer.CreateEdit())
            {
                // 新しいメソッドを追加
                var insertPosition = FindMethodInsertionPoint(root, selectedNode);
                edit.Insert(insertPosition, Environment.NewLine + methodDeclaration + Environment.NewLine);

                // 選択範囲をメソッド呼び出しに置き換え
                edit.Replace(selectedSpan.Span, methodInvocation);

                edit.Apply();
            }
        }

        public bool HasActionSets => false;
        public string DisplayText => "メソッドの抽出";
        public ImageMoniker IconMoniker => default;
        public string IconAutomationText => null;
        public string InputGestureText => null;
        public bool HasPreview => false;

        private (List<(string type, string name)> parameters, List<string> localVars) AnalyzeVariables(
            SyntaxNode node,
            SemanticModel semanticModel)
        {
            var parameters = new List<(string type, string name)>();
            var localVariables = new List<string>();

            // 選択範囲内で使用される変数を収集
            var dataFlowAnalysis = semanticModel.AnalyzeDataFlow(node);
            if (dataFlowAnalysis.Succeeded)
            {
                // 読み取られる変数をパラメータとして追加
                foreach (var variable in dataFlowAnalysis.ReadInside)
                {
                    if (dataFlowAnalysis.WrittenInside.Contains(variable))
                    {
                        continue;
                    }

                    var type = variable.GetType().ToString();
                    var name = variable.Name;
                    parameters.Add((type, name));
                }

                // ローカル変数を収集
                foreach (var variable in dataFlowAnalysis.WrittenInside)
                {
                    localVariables.Add(variable.Name);
                }
            }

            return (parameters, localVariables);
        }

        private string DetermineReturnType(
            SyntaxNode node,
            SemanticModel semanticModel,
            List<string> localVariables)
        {
            var dataFlowAnalysis = semanticModel.AnalyzeDataFlow(node);
            if (!dataFlowAnalysis.Succeeded)
            {
                return "void";
            }

            // 変更された変数が1つだけの場合、その型を返り値として使用
            var writtenOutside = dataFlowAnalysis.WrittenInside.AsValueEnumerable()
                .Intersect(dataFlowAnalysis.WrittenOutside).ToList();
            if (writtenOutside.Count == 1)
            {
                return writtenOutside[0].GetType().ToString();
            }

            return "void";
        }

        private string CreateMethodDeclaration(
            string methodName,
            string body,
            List<(string type, string name)> parameters,
            string returnType)
        {
            var parameterList = string.Join(", ", parameters.AsValueEnumerable().Select(p => $"{p.type} {p.name}"));
            var indentedBody = body.Replace(Environment.NewLine, Environment.NewLine + "        ");

            return $@"    private {returnType} {methodName}({parameterList})
    {{
        {indentedBody}
    }}";
        }

        private string CreateMethodInvocation(
            string methodName,
            List<(string type, string name)> parameters,
            bool hasReturnValue)
        {
            var argumentList = string.Join(", ", parameters.AsValueEnumerable().Select(p => p.name));
            var invocation = $"{methodName}({argumentList})";

            // 返り値がある場合は変数に代入
            if (hasReturnValue)
            {
                invocation = $"var result = {invocation}";
            }

            return invocation + ";";
        }

        private int FindMethodInsertionPoint(SyntaxNode root, SyntaxNode selectedNode)
        {
            // 現在のメソッドの終了位置を見つける
            var containingMethod = selectedNode.Ancestors().AsValueEnumerable().OfType<MethodDeclarationSyntax>()
                .FirstOrDefault();
            if (containingMethod != null)
            {
                return containingMethod.Span.End;
            }

            // メソッドが見つからない場合はクラスの終了位置の手前に挿入
            var containingClass = selectedNode.Ancestors().AsValueEnumerable().OfType<ClassDeclarationSyntax>()
                .FirstOrDefault();
            if (containingClass != null)
            {
                return containingClass.Span.End - 1;
            }

            return selectedNode.Span.End;
        }
    }
}