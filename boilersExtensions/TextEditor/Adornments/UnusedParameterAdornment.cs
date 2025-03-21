using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using ZLinq;

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
        // 分析間の最小間隔（ミリ秒）
        private const int THROTTLE_INTERVAL_MS = 1000;

        // 初期分析の遅延（ミリ秒）
        private const int INITIAL_ANALYSIS_DELAY_MS = 1000;

        // 分析が一時停止中かどうかのフラグ
        private static bool _isPaused;
        private readonly List<UIElement> _adornments = new List<UIElement>();

        // 遅延初期化用のタイマー
        private readonly DispatcherTimer _initialAnalysisTimer;
        private readonly IAdornmentLayer _layer;

        // 装飾スタイル
        private readonly Brush _strikeThroughBrush = Brushes.Gray;
        private readonly double _strikeThroughThickness = 1.0;

        // スロットリング用のタイマー
        private readonly DispatcherTimer _throttleTimer;
        private readonly IWpfTextView _view;

        // 変更要求があったかどうかのフラグ
        private bool _analysisRequested;

        // 最終分析時刻を記録
        private DateTime _lastAnalysisTime = DateTime.MinValue;

        public UnusedParameterAdornment(IWpfTextView view)
        {
            _view = view;
            _layer = view.GetAdornmentLayer("UnusedParameterStrikethrough");

            // スロットリング用のタイマーを初期化
            _throttleTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(THROTTLE_INTERVAL_MS)
            };
            _throttleTimer.Tick += OnThrottleTimerTick;

            // 遅延初期化用のタイマーを設定
            _initialAnalysisTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(INITIAL_ANALYSIS_DELAY_MS)
            };
            _initialAnalysisTimer.Tick += OnInitialAnalysisTick;
            _initialAnalysisTimer.Start();

            _view.LayoutChanged += OnLayoutChanged;
            _view.TextBuffer.Changed += OnTextBufferChanged;
            _view.Closed += OnViewClosed;

            // 表示完了時に分析を実行するための追加イベント
            _view.GotAggregateFocus += OnViewGotFocus;
        }

        private void OnViewGotFocus(object sender, EventArgs e)
        {
            // ビューがフォーカスを取得したときに分析を要求
            RequestAnalysis();

            // イベントはワンショットで十分
            _view.GotAggregateFocus -= OnViewGotFocus;
        }

        private void OnInitialAnalysisTick(object sender, EventArgs e)
        {
            // 初期分析を実行
            AnalyzeAndAdornUnusedParameters();

            // タイマーを停止（一度だけ実行）
            _initialAnalysisTimer.Stop();
        }

        private void OnViewClosed(object sender, EventArgs e)
        {
            // ビューがクローズされたときにイベントをクリーンアップ
            _throttleTimer.Stop();
            _initialAnalysisTimer.Stop();
            _view.LayoutChanged -= OnLayoutChanged;
            _view.TextBuffer.Changed -= OnTextBufferChanged;
            _view.Closed -= OnViewClosed;
            _view.GotAggregateFocus -= OnViewGotFocus;
        }

        // 分析を一時停止するメソッド
        public static void PauseAnalysis() => _isPaused = true;

        // 分析を再開するメソッド
        public static void ResumeAnalysis() => _isPaused = false;

        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            // テキスト変更時には即時分析せず、要求フラグを立ててタイマーを起動
            RequestAnalysis();
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            // レイアウト変更時も同様
            if (e.NewSnapshot != e.OldSnapshot)
            {
                RequestAnalysis();
            }
            else if (e.TranslatedLines.Count > 0 || e.NewOrReformattedLines.Count > 0)
            {
                // スクロールや表示内容の変更時も分析を要求
                RequestAnalysis();
            }
        }

        private void RequestAnalysis()
        {
            _analysisRequested = true;

            // タイマーが動いていなければ起動
            if (!_throttleTimer.IsEnabled)
            {
                _throttleTimer.Start();
            }
        }

        private void OnThrottleTimerTick(object sender, EventArgs e)
        {
            // 要求がなければタイマーを停止
            if (!_analysisRequested)
            {
                _throttleTimer.Stop();
                return;
            }

            // 要求をリセット
            _analysisRequested = false;

            // 分析を実行
            AnalyzeAndAdornUnusedParameters();

            // 最終分析時刻を更新
            _lastAnalysisTime = DateTime.Now;
        }

        private async void AnalyzeAndAdornUnusedParameters()
        {
            // 分析が一時停止中なら何もしない
            if (_isPaused)
            {
                return;
            }

            // 大きなドキュメントの場合はスキップ
            if (_view.TextBuffer.CurrentSnapshot.Length > 100000)
            {
                return;
            }

            // 既存の装飾をクリア
            _layer.RemoveAllAdornments();
            _adornments.Clear();

            try
            {
                // 非同期で分析を実行
                await Task.Run(() =>
                {
                    try
                    {
                        // Get the current snapshot
                        var snapshot = _view.TextBuffer.CurrentSnapshot;
                        var text = snapshot.GetText();

                        // 明らかに長すぎるテキストはスキップ
                        if (text.Length > 100000)
                        {
                            return;
                        }

                        var tree = CSharpSyntaxTree.ParseText(text);
                        var root = tree.GetRoot();

                        var compilation = CSharpCompilation.Create("TempAssembly")
                            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                            .AddSyntaxTrees(tree);

                        var semanticModel = compilation.GetSemanticModel(tree);

                        // 通常のメソッド定義を取得
                        var methodDeclarations = root.DescendantNodes().AsValueEnumerable().OfType<MethodDeclarationSyntax>().ToList();

                        // Top-Level Statements（グローバルステートメント）を取得
                        var globalStatements = root.DescendantNodes().AsValueEnumerable().OfType<GlobalStatementSyntax>().ToList();

                        // グローバルステートメントの中のローカル関数を取得
                        var localFunctions = globalStatements.AsValueEnumerable()
                            .SelectMany(gs => gs.DescendantNodes().AsValueEnumerable().OfType<LocalFunctionStatementSyntax>()).ToList();

                        // 分析結果をキャプチャ
                        var unusedParams = new List<TextSpan>();

                        // 通常のメソッドを処理
                        foreach (var method in methodDeclarations)
                        {
                            foreach (var parameter in method.ParameterList.Parameters)
                            {
                                // パラメータの使用状況を解析
                                var parameterSymbol = semanticModel.GetDeclaredSymbol(parameter);
                                if (parameterSymbol != null)
                                {
                                    var references = method.DescendantNodes().AsValueEnumerable()
                                        .OfType<IdentifierNameSyntax>()
                                        .Where(id =>
                                            semanticModel.GetSymbolInfo(id).Symbol?.Equals(parameterSymbol) == true)
                                        .ToList();

                                    if (!references.AsValueEnumerable().Any())
                                    {
                                        // 引数全体のスパンを追加 (型名+引数名)
                                        unusedParams.Add(parameter.Span);
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
                                    var references = nodes.AsValueEnumerable()
                                        .OfType<IdentifierNameSyntax>()
                                        .Where(id =>
                                            semanticModel.GetSymbolInfo(id).Symbol?.Equals(parameterSymbol) == true)
                                        .ToList();

                                    if (!references.AsValueEnumerable().Any())
                                    {
                                        // 引数全体のスパンを追加 (型名+引数名)
                                        unusedParams.Add(parameter.Span);
                                    }
                                }
                            }
                        }

                        // UI スレッドで装飾を追加
                        _view.VisualElement.Dispatcher.Invoke(() =>
                        {
                            foreach (var span in unusedParams)
                            {
                                AddStrikethrough(span);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error analyzing parameters: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in parameter analysis: {ex.Message}");
            }
        }

        private void AddStrikethrough(TextSpan span)
        {
            try
            {
                // パラメータの実際の位置を取得
                var snapshotSpan = new SnapshotSpan(_view.TextBuffer.CurrentSnapshot,
                    new Span(span.Start, span.End - span.Start));

                // テキストスパンに含まれる各行を取得
                var startPosition = snapshotSpan.Start.Position;
                var endPosition = snapshotSpan.End.Position;

                // 開始行と終了行の取得
                var snapshot = _view.TextBuffer.CurrentSnapshot;
                var startLine = snapshot.GetLineFromPosition(startPosition);
                var endLine = snapshot.GetLineFromPosition(endPosition);

                // 単一行の場合
                if (startLine.LineNumber == endLine.LineNumber)
                {
                    AddSingleLineStrikethrough(snapshotSpan);
                    return;
                }

                // 複数行にまたがる場合、各行ごとに処理

                // 最初の行
                var firstLineEnd = startLine.End;
                if (firstLineEnd.Position > startPosition)
                {
                    var firstLineSpan =
                        new SnapshotSpan(snapshot, startPosition, firstLineEnd.Position - startPosition);
                    AddSingleLineStrikethrough(firstLineSpan);
                }

                // 中間の行
                for (var lineNumber = startLine.LineNumber + 1; lineNumber < endLine.LineNumber; lineNumber++)
                {
                    var line = snapshot.GetLineFromLineNumber(lineNumber);
                    var lineSpan = new SnapshotSpan(snapshot, line.Start, line.Length);
                    AddSingleLineStrikethrough(lineSpan);
                }

                // 最後の行
                var lastLineStart = endLine.Start;
                if (endPosition > lastLineStart.Position)
                {
                    var lastLineSpan = new SnapshotSpan(snapshot, lastLineStart, endPosition - lastLineStart.Position);
                    AddSingleLineStrikethrough(lastLineSpan);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding strikethrough: {ex.Message}");
            }
        }

        // 単一行のテキストに打ち消し線を追加
        private void AddSingleLineStrikethrough(SnapshotSpan span)
        {
            try
            {
                // スパンが実際のレイアウト内に表示されているか確認
                var line = _view.GetTextViewLineContainingBufferPosition(span.Start);
                if (line == null || !line.IsValid)
                {
                    return;
                }

                // テキストの境界を取得
                var startBounds = line.GetCharacterBounds(span.Start);
                var endBounds = span.End <= line.End
                    ? line.GetCharacterBounds(span.End)
                    : line.GetCharacterBounds(line.End);

                // グレーのテキストオーバーレイ
                var text = new TextBlock
                {
                    Text = span.GetText(),
                    Foreground = _strikeThroughBrush,
                    Background = Brushes.Transparent,
                    FontSize = _view.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize,
                    FontFamily = _view.FormattedLineSource.DefaultTextProperties.Typeface.FontFamily
                };

                Canvas.SetLeft(text, startBounds.Left);
                Canvas.SetTop(text, startBounds.TextTop);
                _adornments.Add(text);

                // 打ち消し線 - TextTop + TextHeightの半分を使用
                var strikeoutY = startBounds.TextTop + (startBounds.TextHeight / 2);

                var strikeout = new Line
                {
                    X1 = startBounds.Left,
                    Y1 = strikeoutY,
                    X2 = endBounds.Left,
                    Y2 = strikeoutY,
                    Stroke = _strikeThroughBrush,
                    StrokeThickness = _strikeThroughThickness
                };

                _adornments.Add(strikeout);

                // アドーンメントをテキストビューに追加（2つの別々のアドーンメントとして）
                _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, text, null);
                _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, strikeout, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding single line strikethrough: {ex.Message}");
            }
        }
    }
}