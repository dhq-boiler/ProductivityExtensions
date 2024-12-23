using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace boilersExtensions.TextEditor.Adornments
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("CSharp")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    public class UnusedParameterAdornmentFactory : IWpfTextViewCreationListener
    {
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("UnusedParameterStrikethrough")]
        [Order(After = PredefinedAdornmentLayers.Text)]
        public AdornmentLayerDefinition EditorAdornmentLayer = null;

        public void TextViewCreated(IWpfTextView textView)
        {
            textView.Properties.GetOrCreateSingletonProperty(
                () => new UnusedParameterAdornment(textView));
        }
    }

    internal sealed class UnusedParameterAdornment
    {
        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;
        private readonly List<TextBlock> _unusedParameters = new List<TextBlock>();
        private readonly List<Line> _strikeouts = new List<Line>();

        public UnusedParameterAdornment(IWpfTextView view)
        {
            _view = view;
            _layer = view.GetAdornmentLayer("UnusedParameterStrikethrough");

            _view.LayoutChanged += OnLayoutChanged;
            _view.TextBuffer.Changed += OnTextBufferChanged;
        }

        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            AnalyzeAndAdornUnusedParameters();
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            AnalyzeAndAdornUnusedParameters();
        }

        private void AnalyzeAndAdornUnusedParameters()
        {
            _layer.RemoveAllAdornments();
            _unusedParameters.Clear();
            _strikeouts.Clear();

            // Get the current snapshot
            ITextSnapshot snapshot = _view.TextBuffer.CurrentSnapshot;
            var text = snapshot.GetText();
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = tree.GetRoot();

            var compilation = CSharpCompilation.Create("TempAssembly")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(tree);

            var semanticModel = compilation.GetSemanticModel(tree);

            // 通常のメソッド定義を取得
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            // Top-Level Statements（グローバルステートメント）を取得
            var globalStatements = root.DescendantNodes().OfType<GlobalStatementSyntax>();

            // グローバルステートメントの中のローカル関数を取得
            var localFunctions = globalStatements
                .SelectMany(gs => gs.DescendantNodes().OfType<LocalFunctionStatementSyntax>());

            // 通常のメソッドを処理
            foreach (var method in methodDeclarations)
            {
                foreach (var parameter in method.ParameterList.Parameters)
                {
                    // パラメータの使用状況を解析
                    var parameterSymbol = semanticModel.GetDeclaredSymbol(parameter);
                    if (parameterSymbol != null)
                    {
                        var references = method.DescendantNodes()
                            .OfType<IdentifierNameSyntax>()
                            .Where(id => semanticModel.GetSymbolInfo(id).Symbol?.Equals(parameterSymbol) == true)
                            .ToList();

                        if (!references.Any())
                        {
                            AddStrikethrough(parameter.Identifier.Span);
                        }
                    }
                }
            }

            // ローカル関数を処理
            foreach (var localFunction in localFunctions)
            {
                foreach (var parameter in localFunction.ParameterList.Parameters)
                {
                    // パラメータの使用状況を解析
                    var parameterSymbol = semanticModel.GetDeclaredSymbol(parameter);
                    if (parameterSymbol != null)
                    {
                        var nodes = localFunction.DescendantNodes();
                        var references = nodes
                            .OfType<IdentifierNameSyntax>()
                            .Where(id => semanticModel.GetSymbolInfo(id).Symbol?.Equals(parameterSymbol) == true)
                            .ToList();

                        if (!references.Any())
                        {
                            AddStrikethrough(parameter.Span);
                        }
                    }
                }
            }
        }

        private void AddStrikethrough(TextSpan span)
        {
            // パラメータの実際の位置を取得
            SnapshotSpan snapshotSpan = new SnapshotSpan(_view.TextBuffer.CurrentSnapshot, new Span(span.Start, span.End - span.Start));
            var line = _view.GetTextViewLineContainingBufferPosition(snapshotSpan.Start);
            var bounds = line.GetCharacterBounds(snapshotSpan.Start);
            var endBounds = line.GetCharacterBounds(snapshotSpan.End);

            // デバッグ情報の出力
            Debug.WriteLine($"Parameter bounds: Left={bounds.Left}, Right={bounds.Right}, Top={bounds.Top}, Height={bounds.Height}");

            var text = new TextBlock
            {
                Text = snapshotSpan.GetText(),
                Background = Brushes.Transparent,
                Foreground = Brushes.Gray,
                FontSize = _view.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize,
                FontFamily = _view.FormattedLineSource.DefaultTextProperties.Typeface.FontFamily,
                Padding = new System.Windows.Thickness(0, 0, 0, 0),
                //ToolTip = $"変数 {snapshotSpan.GetText()} はメソッド内のどこからも参照されていないので削除できます"
            };

            Canvas.SetLeft(text, bounds.Left);
            Canvas.SetTop(text, bounds.TextTop);
            _unusedParameters.Add(text);

            _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative,
                snapshotSpan,
                null,
                text,
                null);

            // 打ち消し線を作成
            var strikeout = new Line
            {
                X1 = bounds.Left + 0.5,
                Y1 = bounds.Top + (bounds.Height / 4 * 3) - 0.5,
                X2 = endBounds.Left + 0.5,
                Y2 = bounds.Top + (bounds.Height / 4 * 3) - 0.5,
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                //ToolTip = $"変数 {snapshotSpan.GetText()} はメソッド内のどこからも参照されていないので削除できます"
            };

            Canvas.SetLeft(strikeout, 0);
            Canvas.SetTop(strikeout, 0);
            _strikeouts.Add(strikeout);

            _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative,
                snapshotSpan,
                null,
                strikeout,
                null);
        }
    }
}
