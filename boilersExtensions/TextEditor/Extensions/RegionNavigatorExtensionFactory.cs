using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
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
        private ITextSnapshotLine _mouseHoverLine; // マウスカーソルが指す行を保持

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
                // 設定が無効な場合は何もしない
                if (!IsFeatureEnabled())
                {
                    return;
                }

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
                // 設定が無効な場合は何もしない
                if (!IsFeatureEnabled())
                {
                    return;
                }

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
                // 設定が無効な場合は何もしない
                if (!IsFeatureEnabled())
                {
                    return;
                }

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

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // 設定が無効な場合は何もしない
                if (!IsFeatureEnabled())
                {
                    return;
                }

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
                    Debug.WriteLine($"Click position: X={point.X:F1}, Y={point.Y:F1}");

                    // マウスカーソルが指す行を取得して保持
                    _mouseHoverLine = GetTextLineFromMousePosition(point);

                    // Windowsクリックイベントの座標ではなく、テキストエディタの座標系に変換（重要）
                    Point textViewPoint;
                    try
                    {
                        // WPF座標からテキストビュー座標に変換（スクロール位置も考慮）
                        textViewPoint = new Point(
                            point.X + _textView.ViewportLeft,
                            point.Y + _textView.ViewportTop
                        );
                        Debug.WriteLine($"Adjusted text view point: X={textViewPoint.X:F1}, Y={textViewPoint.Y:F1} " +
                                        $"(ViewportLeft={_textView.ViewportLeft:F1}, ViewportTop={_textView.ViewportTop:F1})");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error converting coordinates: {ex.Message}");
                        textViewPoint = point; // 変換失敗時は元の座標を使用
                    }

                    // 行が取得できた場合の処理
                    if (_mouseHoverLine != null)
                    {
                        var lineText = _mouseHoverLine.GetText();
                        Debug.WriteLine($"Processing line {_mouseHoverLine.LineNumber + 1}: \"{lineText.Trim()}\"");

                        //MessageBox.Show($"あなたは {_mouseHoverLine.LineNumber + 1} 行目をクリックしました。", "DEBUG");

                        // 必要に応じてマウスカーソルの行を優先的に使用
                        var lineToProcess = _mouseHoverLine;

                        // まずクリックした位置にカーソルを確実に移動させる
                        MoveCaretToExactLine(lineToProcess);

                        // #region または #endregion 行かチェック
                        if (IsRegionDirective(lineText))
                        {
                            Debug.WriteLine($"Region directive detected at line {lineToProcess.LineNumber + 1}: {lineText.Trim()}");

                            // イベントを処理済みとしてマーク - 最重要
                            e.Handled = true;

                            // region/endregion間の移動を実行
                            ExecuteRegionNavigation(lineToProcess);
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
                    // クリック処理後は、一時的に保持していたマウスホバー行をクリア
                    _mouseHoverLine = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnMouseLeftButtonDown: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                _isHandlingClick = false;
                _mouseHoverLine = null;
            }
        }

        /// <summary>
        /// 機能が有効かどうかを確認
        /// </summary>
        private bool IsFeatureEnabled()
        {
            try
            {
                return BoilersExtensionsSettings.IsRegionNavigatorEnabled;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking if region navigator is enabled: {ex.Message}");
                return true; // エラーの場合はデフォルトで有効
            }
        }

        /// <summary>
        /// 指定された行に確実にカーソルを移動する強化版メソッド
        /// </summary>
        private void MoveCaretToExactLine(ITextSnapshotLine line)
        {
            try
            {
                // UIスレッドで実行
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    Debug.WriteLine($"Moving caret to exact line {line.LineNumber + 1}");

                    // まず強制的に既存の選択をクリア
                    _textView.Selection.Clear();

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

                    // カーソル移動（バッファ座標を使用）
                    _textView.Caret.MoveTo(new SnapshotPoint(_textView.TextSnapshot, nonWhitespacePosition));

                    // すぐにDTEも更新（Visual Studioの内部状態との同期）
                    try
                    {
                        var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                        if (dte?.ActiveDocument?.Selection is EnvDTE.TextSelection selection)
                        {
                            // ライン番号とキャラクタ位置を1ベースの値に変換
                            int lineNumber = line.LineNumber + 1;
                            // 行頭からの文字数を計算
                            int column = nonWhitespacePosition - line.Start.Position + 1;

                            // DTEの選択を更新
                            selection.MoveToLineAndOffset(lineNumber, column);

                            // 選択範囲をクリア
                            selection.StartOfLine();

                            // 行内の非空白文字への移動を試行
                            try
                            {
                                // 空白でない文字を探す
                                var currentLine = selection.CurrentLine;
                                var text = selection.Text;

                                // 空白をスキップして最初の実質的な文字に移動（DTEのネイティブ機能を使用）
                                selection.StartOfLine(EnvDTE.vsStartOfLineOptions.vsStartOfLineOptionsFirstText);

                                Debug.WriteLine($"DTE caret moved to line {selection.CurrentLine}, column {selection.CurrentColumn}");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error moving to first non-whitespace: {ex.Message}");
                            }

                            // 選択状態を確認・調整
                            if (selection.IsActiveEndGreater)
                            {
                                // 選択領域がある場合はクリア
                                selection.Collapse();
                            }
                        }

                        // VsTextViewのカーソル位置も更新（二重保険）
                        var textManager = Package.GetGlobalService(typeof(SVsTextManager)) as IVsTextManager;
                        if (textManager != null)
                        {
                            // アクティブなテキストビューを取得
                            textManager.GetActiveView(1, null, out var vsTextView);
                            if (vsTextView != null)
                            {
                                // 指定行・列にカーソルを移動
                                vsTextView.SetCaretPos(line.LineNumber, nonWhitespacePosition - line.Start.Position);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating DTE caret position: {ex.Message}");
                    }

                    // 行のスクロールを確実に行い、その行を画面に表示
                    _textView.ViewScroller.EnsureSpanVisible(
                        new SnapshotSpan(line.Start, line.End),
                        EnsureSpanVisibleOptions.AlwaysCenter); // 常に中央に表示

                    Debug.WriteLine($"Caret moved to line {line.LineNumber + 1}, position {nonWhitespacePosition}, text: \"{lineText.Trim()}\"");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Critical error in MoveCaretToExactLine: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
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
                // 方法1: 最も正確な方法 - TextViewLines.GetTextViewLineContainingYCoordinate を使用
                var textViewLines = _textView.TextViewLines;
                if (textViewLines != null && textViewLines.Count > 0)
                {
                    try
                    {
                        // TextViewLinesが表示されている場合
                        IWpfTextViewLine viewLine = null;

                        // 表示範囲内にあるかどうかをチェック
                        bool isYInView = (mousePosition.Y >= 0 && mousePosition.Y <= _textView.ViewportHeight);

                        if (isYInView)
                        {
                            // スクロール情報を考慮した正規化
                            var normalizedMousePosition = mousePosition.Y + _textView.ViewportTop;
                            var textViewLine = textViewLines.GetTextViewLineContainingYCoordinate(normalizedMousePosition);
                            viewLine = textViewLine as IWpfTextViewLine;

                            if (viewLine != null)
                            {
                                var snapshotLine = viewLine.Start.GetContainingLine();
                                Debug.WriteLine($"Method 1: Found line {snapshotLine.LineNumber + 1} using Y coordinate {mousePosition.Y:F1}");
                                Debug.WriteLine($"Scroll Top: {_textView.ViewportTop:F1}, Normalized Y: {normalizedMousePosition:F1}");
                                Debug.WriteLine($"Line bounds: Top={viewLine.Top:F1}, Bottom={viewLine.Bottom:F1}, TextTop={viewLine.TextTop:F1}, TextBottom={viewLine.TextBottom:F1}");

                                // ハイライト表示して確認
                                HighlightLine(snapshotLine, "Found by mouse Y coordinate");

                                return snapshotLine;
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"Mouse Y position outside viewport: Y={mousePosition.Y:F1}, Viewport height={_textView.ViewportHeight:F1}");

                            // 表示範囲外の場合は最も近い行を特定
                            if (mousePosition.Y < 0)
                            {
                                // 画面上部の表示されている最初の行
                                var firstLine = textViewLines.FirstVisibleLine;
                                if (firstLine != null)
                                {
                                    var snapshotLine = firstLine.Start.GetContainingLine();
                                    Debug.WriteLine($"Using first visible line {snapshotLine.LineNumber + 1} since mouse is above viewport");
                                    return snapshotLine;
                                }
                            }
                            else if (mousePosition.Y > _textView.ViewportHeight)
                            {
                                // 画面下部の表示されている最後の行
                                var lastLine = textViewLines.LastVisibleLine;
                                if (lastLine != null)
                                {
                                    var snapshotLine = lastLine.Start.GetContainingLine();
                                    Debug.WriteLine($"Using last visible line {snapshotLine.LineNumber + 1} since mouse is below viewport");
                                    return snapshotLine;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error getting line at Y coordinate: {ex.Message}");
                    }
                }
                else
                {
                    Debug.WriteLine("TextViewLines is null or empty");
                }

                // 方法2: エディタの現在の選択範囲を使用
                if (_textView.Selection != null && !_textView.Selection.IsEmpty)
                {
                    var selectedSpan = _textView.Selection.StreamSelectionSpan;
                    var startLine = selectedSpan.Start.Position.GetContainingLine();
                    Debug.WriteLine($"Method 2: Using currently selected line {startLine.LineNumber + 1}");

                    // 選択行をハイライト表示
                    HighlightLine(startLine, "Found by current selection");

                    return startLine;
                }

                // 方法3: 直接ヒットテストを実行
                try
                {
                    // テキストビューのバッファポジションをマウス位置で直接検索
                    var bufferPosition = _textView.BufferGraph.MapDownToFirstMatch(
                        new SnapshotPoint(_textView.TextSnapshot, 0),
                        PointTrackingMode.Positive,
                        snapshot => true,
                        PositionAffinity.Successor);

                    if (bufferPosition.HasValue)
                    {
                        var line = bufferPosition.Value.GetContainingLine();
                        Debug.WriteLine($"Method 3: Found line {line.LineNumber + 1} using direct buffer position test");

                        // 見つかった行をハイライト
                        HighlightLine(line, "Found by direct hit test");

                        return line;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in direct hit test: {ex.Message}");
                }

                // 方法4: カーソル位置の行を使用
                try
                {
                    var caretLine = _textView.Caret.Position.BufferPosition.GetContainingLine();
                    Debug.WriteLine($"Method 4: Using caret line {caretLine.LineNumber + 1}");

                    // カーソル行をハイライト
                    HighlightLine(caretLine, "Found by caret position");

                    return caretLine;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting caret line: {ex.Message}");
                }

                // 方法5: テキストビューの最上部や一番近い表示行を使用
                try
                {
                    if (textViewLines != null && textViewLines.Count > 0)
                    {
                        ITextViewLine targetLine = null;

                        if (mousePosition.Y <= 0)
                        {
                            // 画面上部
                            targetLine = textViewLines.FirstVisibleLine;
                        }
                        else if (mousePosition.Y >= _textView.ViewportHeight)
                        {
                            // 画面下部
                            targetLine = textViewLines.LastVisibleLine;
                        }
                        else
                        {
                            // 画面中央付近
                            int middleIndex = textViewLines.Count / 2;
                            targetLine = textViewLines[middleIndex];
                        }

                        if (targetLine != null)
                        {
                            var line = targetLine.Extent.Start.GetContainingLine();
                            Debug.WriteLine($"Method 6: Using line {line.LineNumber + 1} based on view position");
                            return line;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting viewport-based line: {ex.Message}");
                }

                // すべての方法が失敗した場合、最終的にドキュメントの中央の行を返す
                var middleLine = _textView.TextSnapshot.GetLineFromLineNumber(
                    Math.Min(_textView.TextSnapshot.LineCount - 1, _textView.TextSnapshot.LineCount / 2));

                Debug.WriteLine($"Last resort: Using middle line {middleLine.LineNumber + 1}");
                return middleLine;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Critical error in GetTextLineFromMousePosition: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);

                // 例外時はカーソル行を返す
                try
                {
                    return _textView.Caret.Position.BufferPosition.GetContainingLine();
                }
                catch
                {
                    // 本当に何も見つからなかった場合は最初の行を返す
                    return _textView.TextSnapshot.GetLineFromLineNumber(0);
                }
            }
        }

        /// <summary>
        /// 行を一時的にハイライト表示して、どの行が選択されたかを視覚的に確認
        /// </summary>
        private void HighlightLine(ITextSnapshotLine line, string source)
        {
            try
            {
                // デバッグが有効な場合のみ行う特別なハイライト
#if DEBUG
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // 現在の選択状態を保存
                    var oldSelection = _textView.Selection.StreamSelectionSpan;
                    bool wasEmpty = _textView.Selection.IsEmpty;

                    // ハイライト用の色を設定（通常選択とは異なる色）
                    var originalBackgroundBrush = _textView.Background;

                    try
                    {
                        // 行全体を選択
                        _textView.Selection.Select(
                            new SnapshotSpan(line.Start, line.End),
                            false);

                        // 特定の色で選択行を表示（デバッグ時のみ）
                        // _textView.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightYellow);

                        // デバッグ出力
                        Debug.WriteLine($"Highlighted line {line.LineNumber + 1} ({source}): \"{line.GetText().Trim()}\"");

                        // 200ミリ秒待機
                        await Task.Delay(200);
                    }
                    finally
                    {
                        // 元の選択状態に戻す
                        if (wasEmpty)
                        {
                            _textView.Selection.Clear();
                        }
                        else
                        {
                            _textView.Selection.Select(new SnapshotSpan(oldSelection.Snapshot, oldSelection.SnapshotSpan), false);
                        }

                        // 背景色を戻す（使用する場合）
                        // _textView.Background = originalBackgroundBrush;
                    }
                });
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error highlighting line: {ex.Message}");
            }
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
        /// 対応するリージョンディレクティブに移動（改善版）
        /// </summary>
        private void ExecuteRegionNavigation(ITextSnapshotLine line)
        {
            try
            {
                // カーソルが確実にこの行にあることを確認
                Debug.WriteLine($"ExecuteRegionNavigation: Ensuring caret is at line {line.LineNumber + 1}");

                // 現在のカーソル行をデバッグ出力
                var currentCaretPosition = _textView.Caret.Position.BufferPosition;
                var currentCaretLine = currentCaretPosition.GetContainingLine();
                Debug.WriteLine($"Current caret line before navigation: {currentCaretLine.LineNumber + 1}");

                // カーソル位置と指定行が異なる場合は強制的に移動（重要）
                if (currentCaretLine.LineNumber != line.LineNumber)
                {
                    Debug.WriteLine($"Caret position mismatch! Moving from line {currentCaretLine.LineNumber + 1} to {line.LineNumber + 1}");
                    MoveCaretToExactLine(line);

                    // 少し待機して確実にカーソル位置が更新されるようにする
                    System.Threading.Thread.Sleep(50);
                }

                var lineText = line.GetText().TrimStart();
                bool isStartRegion = IsStartRegionDirective(lineText);
                var snapshot = _textView.TextSnapshot;
                var startLineNumber = line.LineNumber;

                Debug.WriteLine($"ExecuteRegionNavigation: Processing line {startLineNumber + 1}, IsStartRegion: {isStartRegion}, Text: \"{lineText.Trim()}\"");

                ITextSnapshotLine matchingLine = null;

                if (isStartRegion)
                {
                    // #region から対応する #endregion を検索
                    matchingLine = FindMatchingEndRegion(startLineNumber + 1);
                    Debug.WriteLine(matchingLine != null
                        ? $"Found matching #endregion at line {matchingLine.LineNumber + 1}"
                        : "No matching #endregion found");
                }
                else if (IsEndRegionDirective(lineText))
                {
                    // #endregion から対応する #region を検索
                    matchingLine = FindMatchingStartRegion(startLineNumber + 1);
                    Debug.WriteLine(matchingLine != null
                        ? $"Found matching #region at line {matchingLine.LineNumber + 1}"
                        : "No matching #region found");
                }

                if (matchingLine != null)
                {
                    // 見つかった行にカーソルを移動（対応する#region/#endregionに）
                    Debug.WriteLine($"Moving to matching region directive at line {matchingLine.LineNumber + 1}");

                    // 一時的なハイライト
                    HighlightLineForNavigation(matchingLine);

                    // カーソル移動とスクロール
                    MoveCaretToExactLine(matchingLine);
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
        /// ナビゲーション用に行を一時的にハイライト
        /// </summary>
        private void HighlightLineForNavigation(ITextSnapshotLine line)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // 行全体を選択
                    _textView.Selection.Select(
                        new SnapshotSpan(line.Start, line.End),
                        false);

                    Debug.WriteLine($"Highlighting line {line.LineNumber + 1} for navigation: \"{line.GetText().Trim()}\"");

                    // 選択を短時間維持（ユーザーに見せるため）
                    await Task.Delay(250);

                    // 選択はExecuteRegionNavigationの後続処理で解除されるので
                    // ここでは解除しない
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error highlighting line for navigation: {ex.Message}");
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

            endLineNumber--;

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