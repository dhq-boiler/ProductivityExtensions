using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using boilersExtensions.Commands;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;

namespace boilersExtensions.TextEditor.Extensions
{
    /// <summary>
    /// テキストエディターの拡張機能ファクトリ - リージョンナビゲーション
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]  // すべてのテキストファイルに対して適用
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class RegionNavigatorExtensionFactory : IWpfTextViewCreationListener
    {
        /// <summary>
        /// テキスト構造ナビゲーションサービス
        /// </summary>
        [Import]
        internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }

        /// <summary>
        /// テキストビュー作成時の処理
        /// </summary>
        public void TextViewCreated(IWpfTextView textView)
        {
            try
            {
                Debug.WriteLine("RegionNavigatorExtensionFactory.TextViewCreated called!");

                // テキストビューにダブルクリックイベントなどの拡張機能を追加
                textView.Properties.GetOrCreateSingletonProperty(
                    () => new RegionNavigatorExtension(textView, NavigatorService));

                Debug.WriteLine("RegionNavigatorExtension successfully attached to TextView");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in TextViewCreated: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// リージョンナビゲーションの拡張機能
    /// </summary>
    internal sealed class RegionNavigatorExtension : IDisposable
    {
        private readonly ITextStructureNavigatorSelectorService _navigatorService;
        private readonly IWpfTextView _textView;
        private bool _isDisposed = false;
        private bool _isHandlingClick = false;  // クリック処理中かどうかのフラグ
        private DispatcherTimer _hoverTimer; // マウスホバー用タイマー
        private Point _lastMousePosition; // 最後のマウス位置
        private ITextSnapshotLine _lastHighlightedLine; // 最後にハイライトした行
        private bool _isSelectingRegion = false; // リージョン選択中フラグ

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public RegionNavigatorExtension(IWpfTextView textView, ITextStructureNavigatorSelectorService navigatorService)
        {
            Debug.WriteLine("RegionNavigatorExtension constructor called!");

            _textView = textView;
            _navigatorService = navigatorService;

            // まず既存のイベントハンドラを解除（念のため）
            CleanupEventHandlers();

            // プレビューマウスイベントのハンドラを登録（通常のイベントより先に発生）
            _textView.VisualElement.PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
            _textView.VisualElement.MouseMove += OnMouseMove; // マウス移動イベントを追加
            _textView.VisualElement.MouseLeave += OnMouseLeave; // マウスがエディタから出たときのイベントを追加
            _textView.Closed += OnTextViewClosed;

            // ホバータイマーを初期化（マウスがある位置に一定時間とどまったときに処理を実行）
            _hoverTimer = new DispatcherTimer();
            _hoverTimer.Interval = TimeSpan.FromMilliseconds(300); // 300msのホバー時間
            _hoverTimer.Tick += OnHoverTimerTick;

            Debug.WriteLine("RegionNavigatorExtension event handlers registered");
        }

        /// <summary>
        /// イベントハンドラを解除
        /// </summary>
        private void CleanupEventHandlers()
        {
            if (_textView?.VisualElement != null)
            {
                _textView.VisualElement.PreviewMouseLeftButtonDown -= OnMouseLeftButtonDown;
                _textView.VisualElement.MouseMove -= OnMouseMove;
                _textView.VisualElement.MouseLeave -= OnMouseLeave;
            }

            if (_textView != null)
            {
                _textView.Closed -= OnTextViewClosed;
            }

            // タイマーのクリーンアップ
            if (_hoverTimer != null)
            {
                _hoverTimer.Stop();
                _hoverTimer.Tick -= OnHoverTimerTick;
            }
        }

        /// <summary>
        /// マウスの移動イベント - ホバー検出に使用
        /// </summary>
        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                // 現在位置を保存
                _lastMousePosition = e.GetPosition(_textView.VisualElement);

                // タイマーをリセット
                _hoverTimer.Stop();
                _hoverTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnMouseMove: {ex.Message}");
            }
        }

        /// <summary>
        /// マウスがテキストビューから離れたとき
        /// </summary>
        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            try
            {
                // タイマーを停止
                _hoverTimer.Stop();

                // 選択状態をクリア（リージョン自動選択をキャンセル）
                if (_isSelectingRegion)
                {
                    _textView.Selection.Clear();
                    _isSelectingRegion = false;
                    _lastHighlightedLine = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnMouseLeave: {ex.Message}");
            }
        }

        /// <summary>
        /// ホバータイマーのTickイベント - マウスが一定時間同じ場所にとどまったとき
        /// </summary>
        private void OnHoverTimerTick(object sender, EventArgs e)
        {
            try
            {
                // タイマーを停止
                _hoverTimer.Stop();

                // マウス位置から行を取得
                var snapshotLine = GetTextLineFromMousePosition(_lastMousePosition);
                if (snapshotLine == null)
                {
                    // 行が見つからない場合は選択をクリア
                    if (_isSelectingRegion)
                    {
                        _textView.Selection.Clear();
                        _isSelectingRegion = false;
                        _lastHighlightedLine = null;
                    }
                    return;
                }

                var lineText = snapshotLine.GetText();

                // すでに同じ行が選択されていれば何もしない
                if (_lastHighlightedLine != null && _lastHighlightedLine.LineNumber == snapshotLine.LineNumber)
                {
                    return;
                }

                // region/endregion行かチェック
                if (IsRegionDirective(lineText))
                {
                    // 行全体を選択
                    _textView.Selection.Select(new SnapshotSpan(snapshotLine.Start, snapshotLine.End), false);
                    _lastHighlightedLine = snapshotLine;
                    _isSelectingRegion = true;

                    Debug.WriteLine($"Hovering over region directive at line {snapshotLine.LineNumber + 1}: {lineText.Trim()}");
                }
                else if (_isSelectingRegion)
                {
                    // リージョン行でなければ選択をクリア
                    _textView.Selection.Clear();
                    _isSelectingRegion = false;
                    _lastHighlightedLine = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnHoverTimerTick: {ex.Message}");
            }
        }

        /// <summary>
        /// マウスの左ボタンが押されたときの処理
        /// </summary>
        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // 処理済みのイベントは無視
                if (e.Handled || _isHandlingClick)
                {
                    Debug.WriteLine("OnMouseLeftButtonDown: Event is already handled or processing another click, ignoring");
                    return;
                }

                // Ctrl+クリックの組み合わせを処理
                if (Keyboard.Modifiers != ModifierKeys.Control)
                {
                    return;
                }

                Debug.WriteLine("Ctrl+Click detected");
                _isHandlingClick = true;

                try
                {
                    var point = e.GetPosition(_textView.VisualElement);
                    Debug.WriteLine($"Click position: X={point.X}, Y={point.Y}");

                    // マウス位置から行を取得（改良版）
                    var clickedLine = GetTextLineFromMousePosition(point);
                    if (clickedLine == null)
                    {
                        // マウス座標で行が見つからない場合、カーソル位置の行を使用
                        clickedLine = _textView.Caret.Position.BufferPosition.GetContainingLine();
                        Debug.WriteLine($"Using caret line instead: {clickedLine.LineNumber + 1}");
                    }
                    else
                    {
                        Debug.WriteLine($"Found line at mouse position: {clickedLine.LineNumber + 1}");
                    }

                    // 行が取得できた場合の処理
                    if (clickedLine != null)
                    {
                        var lineText = clickedLine.GetText();
                        Debug.WriteLine($"Clicked on line {clickedLine.LineNumber + 1}: {lineText.Trim()}");

                        // #region または #endregion 行かチェック
                        if (IsRegionDirective(lineText))
                        {
                            Debug.WriteLine($"Region directive detected at line {clickedLine.LineNumber + 1}: {lineText.Trim()}");

                            // イベントを処理済みとしてマーク - 最重要
                            e.Handled = true;

                            // region/endregion間の移動を実行
                            ExecuteRegionNavigation(clickedLine);
                        }
                        else
                        {
                            Debug.WriteLine("Not a region directive line, searching for closest region directive");

                            // 近くのリージョンディレクティブを検索
                            var closestRegionLine = FindClosestRegionDirective(clickedLine);
                            if (closestRegionLine != null)
                            {
                                Debug.WriteLine($"Found closest region directive at line {closestRegionLine.LineNumber + 1}: {closestRegionLine.GetText().Trim()}");
                                e.Handled = true;
                                ExecuteRegionNavigation(closestRegionLine);
                            }
                            else
                            {
                                Debug.WriteLine("No region directive found in the document");
                                ShowMessage("ドキュメント内にリージョンディレクティブが見つかりませんでした。");
                            }
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Could not determine clicked line");
                    }
                }
                finally
                {
                    _isHandlingClick = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnMouseLeftButtonDown: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                _isHandlingClick = false;
            }
        }

        /// <summary>
        /// マウス位置からテキスト行を取得する改良版メソッド
        /// </summary>
        private ITextSnapshotLine GetTextLineFromMousePosition(Point mousePosition)
        {
            // 保守的な対応として、いずれかが利用できない場合はnullを返す
            if (_textView == null || _textView.IsClosed)
            {
                Debug.WriteLine("TextView is null or closed");
                return null;
            }

            try
            {
                // 方法1: TextViewLines.GetTextViewLineContainingYCoordinate を使用
                var textViewLines = _textView.TextViewLines;
                if (textViewLines != null)
                {
                    IWpfTextViewLine viewLine = null;

                    try
                    {
                        // 表示範囲内にあるかどうかをチェック
                        bool isInView = (mousePosition.Y >= 0 &&
                                       mousePosition.Y <= _textView.ViewportHeight &&
                                       mousePosition.X >= 0 &&
                                       mousePosition.X <= _textView.ViewportWidth);

                        if (isInView)
                        {
                            viewLine = textViewLines.GetTextViewLineContainingYCoordinate(mousePosition.Y) as IWpfTextViewLine;
                        }
                        else
                        {
                            Debug.WriteLine($"Mouse position outside viewport: X={mousePosition.X}, Y={mousePosition.Y}, " +
                                          $"Viewport: {_textView.ViewportWidth}x{_textView.ViewportHeight}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error getting line at Y coordinate: {ex.Message}");
                    }

                    if (viewLine != null)
                    {
                        var snapshotLine = viewLine.Start.GetContainingLine();
                        Debug.WriteLine($"Method 1: Found line {snapshotLine.LineNumber + 1}");
                        return snapshotLine;
                    }
                    Debug.WriteLine("Method 1: No line found at Y coordinate");
                }
                else
                {
                    Debug.WriteLine("TextViewLines is null or empty");
                }

                // 方法2: マウス入力時に選択されている行があればそれを使用
                if (_textView.Selection != null && !_textView.Selection.IsEmpty)
                {
                    var selectedSpan = _textView.Selection.StreamSelectionSpan;
                    var startLine = selectedSpan.Start.Position.GetContainingLine();
                    Debug.WriteLine($"Method 2: Using currently selected line {startLine.LineNumber + 1}");
                    return startLine;
                }

                // 方法3: 表示されている行範囲から近似計算
                // 表示中の最初と最後の行を取得
                try
                {
                    if (textViewLines != null)
                    {
                        var firstVisibleLine = textViewLines.FirstVisibleLine;
                        var lastVisibleLine = textViewLines.LastVisibleLine;

                        if (firstVisibleLine != null && lastVisibleLine != null)
                        {
                            var firstLine = firstVisibleLine.Start.GetContainingLine();
                            var lastLine = lastVisibleLine.End.GetContainingLine();
                            var firstLineNumber = firstLine.LineNumber;
                            var lastLineNumber = lastLine.LineNumber;

                            // 表示範囲内の行数
                            var visibleLineCount = lastLineNumber - firstLineNumber + 1;

                            // マウスの相対Y位置から行を推定
                            double viewportHeight = _textView.ViewportHeight;
                            if (viewportHeight > 0)
                            {
                                double relativeY = mousePosition.Y / viewportHeight;
                                relativeY = Math.Max(0, Math.Min(1, relativeY)); // 0～1の範囲に制限

                                int estimatedLineOffset = (int)(relativeY * visibleLineCount);
                                int estimatedLineNumber = firstLineNumber + estimatedLineOffset;

                                // 範囲チェック
                                estimatedLineNumber = Math.Max(0, Math.Min(_textView.TextSnapshot.LineCount - 1, estimatedLineNumber));

                                var estimatedLine = _textView.TextSnapshot.GetLineFromLineNumber(estimatedLineNumber);
                                Debug.WriteLine($"Method 3: Estimated line {estimatedLine.LineNumber + 1} " +
                                              $"(from range {firstLineNumber + 1}-{lastLineNumber + 1}, relativeY={relativeY:F2})");
                                return estimatedLine;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error estimating line from viewport: {ex.Message}");
                }

                // 方法4: キャレット位置の行を使用
                try
                {
                    var caretLine = _textView.Caret.Position.BufferPosition.GetContainingLine();
                    Debug.WriteLine($"Method 4: Using caret line {caretLine.LineNumber + 1}");
                    return caretLine;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting caret line: {ex.Message}");
                }

                // 方法5: 最後の手段 - 全くダメな場合はドキュメント内の適当な場所（中央あたり）
                try
                {
                    int middleLineNumber = _textView.TextSnapshot.LineCount / 2;
                    var middleLine = _textView.TextSnapshot.GetLineFromLineNumber(middleLineNumber);
                    Debug.WriteLine($"Method 5: Using middle line {middleLine.LineNumber + 1} as last resort");
                    return middleLine;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting middle line: {ex.Message}");
                }

                // もし本当に何も見つからない場合はnullを返す
                Debug.WriteLine("All line detection methods failed");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unhandled error in GetTextLineFromMousePosition: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 最も近いリージョンディレクティブを探す
        /// </summary>
        private ITextSnapshotLine FindClosestRegionDirective(ITextSnapshotLine currentLine)
        {
            var snapshot = _textView.TextSnapshot;
            var currentLineNumber = currentLine.LineNumber;
            ITextSnapshotLine closestLine = null;
            var minDistance = int.MaxValue;

            // 現在の行から上下に検索
            for (var i = 0; i < snapshot.LineCount; i++)
            {
                var line = snapshot.GetLineFromLineNumber(i);
                var lineText = line.GetText();

                if (IsRegionDirective(lineText))
                {
                    var distance = Math.Abs(i - currentLineNumber);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestLine = line;
                    }
                }
            }

            return closestLine;
        }

        /// <summary>
        /// テキストビューが閉じられたときのクリーンアップ
        /// </summary>
        private void OnTextViewClosed(object sender, EventArgs e)
        {
            Debug.WriteLine("OnTextViewClosed called - cleaning up event handlers");
            Dispose();
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                CleanupEventHandlers();
                Debug.WriteLine("RegionNavigatorExtension disposed");
            }
        }

        /// <summary>
        /// リージョンディレクティブかどうかチェック (#region または #endregion)
        /// </summary>
        private bool IsRegionDirective(string lineText)
        {
            var trimmedLine = lineText.TrimStart();
            // より正確なマッチングのために正規表現を使用
            return Regex.IsMatch(trimmedLine, @"^#\s*region\b") ||
                   Regex.IsMatch(trimmedLine, @"^#\s*endregion\b");
        }

        /// <summary>
        /// 指定された行のテキストが#regionか調べる
        /// </summary>
        private bool IsStartRegionDirective(string lineText)
        {
            var trimmedLine = lineText.TrimStart();
            return Regex.IsMatch(trimmedLine, @"^#\s*region\b");
        }

        /// <summary>
        /// 指定された行のテキストが#endregionか調べる
        /// </summary>
        private bool IsEndRegionDirective(string lineText)
        {
            var trimmedLine = lineText.TrimStart();
            return Regex.IsMatch(trimmedLine, @"^#\s*endregion\b");
        }

        /// <summary>
        /// 対応するリージョンディレクティブに移動
        /// </summary>
        private void ExecuteRegionNavigation(ITextSnapshotLine line)
        {
            try
            {
                var lineText = line.GetText().TrimStart();
                bool isStartRegion = IsStartRegionDirective(lineText);
                var snapshot = _textView.TextSnapshot;
                var startLineNumber = line.LineNumber;

                Debug.WriteLine($"ExecuteRegionNavigation: Line {startLineNumber + 1}, IsStartRegion: {isStartRegion}, Text: {lineText.Trim()}");

                ITextSnapshotLine matchingLine = null;

                if (isStartRegion)
                {
                    // #region から対応する #endregion を検索
                    matchingLine = FindMatchingEndRegion(startLineNumber);
                }
                else if (IsEndRegionDirective(lineText))
                {
                    // #endregion から対応する #region を検索
                    matchingLine = FindMatchingStartRegion(startLineNumber);
                }

                if (matchingLine != null)
                {
                    // 見つかった行にカーソルを移動
                    MoveCaretToLine(matchingLine.LineNumber);
                }
                else
                {
                    // 対応するディレクティブが見つからなかった場合はステータスバーにメッセージを表示
                    ShowMessage(isStartRegion
                        ? "対応する #endregion が見つかりませんでした。"
                        : "対応する #region が見つかりませんでした。");

                    Debug.WriteLine("No matching region directive found.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ExecuteRegionNavigation error: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// 指定された行の #region に対応する #endregion を検索する改良版
        /// スタックベースの実装でネストされたリージョンを正確に処理
        /// </summary>
        private ITextSnapshotLine FindMatchingEndRegion(int startLineNumber)
        {
            var snapshot = _textView.TextSnapshot;
            var stack = new System.Collections.Generic.Stack<int>();

            // 開始行を基準として検索を始める
            stack.Push(startLineNumber);

            for (int i = startLineNumber + 1; i < snapshot.LineCount; i++)
            {
                var currentLine = snapshot.GetLineFromLineNumber(i);
                var lineText = currentLine.GetText().TrimStart();

                if (IsStartRegionDirective(lineText))
                {
                    // ネストされた #region を見つけたらスタックに追加
                    stack.Push(i);
                    Debug.WriteLine($"Found nested #region at line {i + 1}, stack depth: {stack.Count}");
                }
                else if (IsEndRegionDirective(lineText))
                {
                    // #endregion を見つけたらスタックから1つ取り出す
                    stack.Pop();
                    Debug.WriteLine($"Found #endregion at line {i + 1}, stack depth: {stack.Count}");

                    // スタックが空になったら、これが対応する #endregion
                    if (stack.Count == 0)
                    {
                        Debug.WriteLine($"Found matching #endregion at line {i + 1}");
                        return currentLine;
                    }
                }
            }

            Debug.WriteLine("No matching #endregion found");
            return null;
        }

        /// <summary>
        /// 指定された行の #endregion に対応する #region を検索する改良版
        /// スタックベースの実装でネストされたリージョンを正確に処理
        /// </summary>
        private ITextSnapshotLine FindMatchingStartRegion(int endLineNumber)
        {
            var snapshot = _textView.TextSnapshot;
            var stack = new System.Collections.Generic.Stack<int>();

            // 終了行を基準として検索を始める
            stack.Push(endLineNumber);

            for (int i = endLineNumber - 1; i >= 0; i--)
            {
                var currentLine = snapshot.GetLineFromLineNumber(i);
                var lineText = currentLine.GetText().TrimStart();

                if (IsEndRegionDirective(lineText))
                {
                    // ネストされた #endregion を見つけたらスタックに追加
                    stack.Push(i);
                    Debug.WriteLine($"Found nested #endregion at line {i + 1}, stack depth: {stack.Count}");
                }
                else if (IsStartRegionDirective(lineText))
                {
                    // #region を見つけたらスタックから1つ取り出す
                    stack.Pop();
                    Debug.WriteLine($"Found #region at line {i + 1}, stack depth: {stack.Count}");

                    // スタックが空になったら、これが対応する #region
                    if (stack.Count == 0)
                    {
                        Debug.WriteLine($"Found matching #region at line {i + 1}");
                        return currentLine;
                    }
                }
            }

            Debug.WriteLine("No matching #region found");
            return null;
        }

        /// <summary>
        /// 指定された行にカーソルを移動
        /// </summary>
        private void MoveCaretToLine(int lineNumber)
        {
            try
            {
                // UIスレッドで実行
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    var line = _textView.TextSnapshot.GetLineFromLineNumber(lineNumber);

                    // 行の先頭位置を取得
                    var position = line.Start.Position;

                    // 行頭の空白をスキップした位置を取得
                    var lineText = line.GetText();
                    var nonWhitespacePosition = position;
                    for (var i = 0; i < lineText.Length; i++)
                    {
                        if (!char.IsWhiteSpace(lineText[i]))
                        {
                            nonWhitespacePosition = position + i;
                            break;
                        }
                    }

                    // カーソルを移動
                    _textView.Caret.MoveTo(new SnapshotPoint(_textView.TextSnapshot, nonWhitespacePosition));

                    // 選択範囲をクリア
                    _textView.Selection.Clear();

                    // スクロールして表示
                    _textView.ViewScroller.EnsureSpanVisible(
                        new SnapshotSpan(line.Start, line.End),
                        EnsureSpanVisibleOptions.AlwaysCenter);

                    // 行を自動選択
                    _textView.Selection.Select(new SnapshotSpan(line.Start, line.End), false);
                    _lastHighlightedLine = line;
                    _isSelectingRegion = true;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MoveCaretToLine error: {ex.Message}");
            }
        }

        /// <summary>
        /// メッセージを表示
        /// </summary>
        private void ShowMessage(string message)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () => {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                    if (dte != null)
                    {
                        dte.StatusBar.Text = message;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Status bar update error: {ex.Message}");
            }
        }
    }
}