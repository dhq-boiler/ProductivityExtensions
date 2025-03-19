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
            _textView.Closed += OnTextViewClosed;

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
            }

            if (_textView != null)
            {
                _textView.Closed -= OnTextViewClosed;
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

                // Ctrl+Alt+クリックの組み合わせを処理
                if (Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Alt))
                {
                    return;
                }

                Debug.WriteLine("Ctrl+Alt+Click detected");
                _isHandlingClick = true;

                try
                {
                    // クリック位置のテキスト位置を取得
                    var point = e.GetPosition(_textView.VisualElement);
                    Debug.WriteLine($"Click position: X={point.X}, Y={point.Y}");

                    ITextSnapshotLine clickedLine = null;
                    int? position = null;

                    // まず通常の方法でクリック位置から行を取得
                    position = GetPositionFromPoint(point);
                    if (position.HasValue)
                    {
                        clickedLine = _textView.TextSnapshot.GetLineFromPosition(position.Value);
                        Debug.WriteLine($"Position from point: {position.Value}, Line: {clickedLine.LineNumber + 1}");
                    }

                    // 位置が取得できなかった場合、代替手段として現在のカーソル位置を使用
                    if (clickedLine == null)
                    {
                        clickedLine = _textView.Caret.Position.BufferPosition.GetContainingLine();
                        Debug.WriteLine($"Using caret line instead: {clickedLine.LineNumber + 1}");
                    }

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
        /// クリック位置からテキスト位置を取得（改善版）
        /// </summary>
        private int? GetPositionFromPoint(System.Windows.Point point)
        {
            Debug.WriteLine($"GetPositionFromPoint: Point X={point.X}, Y={point.Y}");

            try
            {
                // クリック位置をテキストビューの座標系に変換してテキスト位置を取得
                var line = _textView.TextViewLines.GetTextViewLineContainingYCoordinate(point.Y);
                if (line != null)
                {
                    // 1. 水平位置からバッファ位置を計算
                    var bufferPosition = line.GetBufferPositionFromXCoordinate(point.X, false);
                    if (bufferPosition.HasValue)
                    {
                        Debug.WriteLine($"Found buffer position from line: {bufferPosition.Value.Position}");
                        return bufferPosition.Value.Position;
                    }

                    // 2. クリック位置から最も近いインサーション位置を取得
                    var insPos = line.GetInsertionBufferPositionFromXCoordinate(point.X);
                    Debug.WriteLine($"Found insertion position: {insPos.Position}");
                    return insPos.Position;

                    // 3. X座標が範囲外の場合は、行の開始または終了を使用
                    // (この部分は実際には実行されませんが、念のためコードとして残しておきます)
                    var position = point.X < line.TextLeft ? line.Start.Position : line.End.Position;
                    Debug.WriteLine($"Using line {(point.X < line.TextLeft ? "start" : "end")} position: {position}");
                    return position;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetPositionFromPoint error: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }

            // 最終的なフォールバック: カーソル位置を使用
            try
            {
                if (_textView.Caret != null && _textView.Caret.Position.BufferPosition != null)
                {
                    var caretPosition = _textView.Caret.Position.BufferPosition.Position;
                    Debug.WriteLine($"Falling back to caret position: {caretPosition}");
                    return caretPosition;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Caret position fallback error: {ex.Message}");
            }

            return null;
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
        /// 対応するリージョンディレクティブに移動
        /// </summary>
        private void ExecuteRegionNavigation(ITextSnapshotLine line)
        {
            try
            {
                var lineText = line.GetText().TrimStart();
                var isStartRegion = Regex.IsMatch(lineText, @"^#\s*region\b");
                var snapshot = _textView.TextSnapshot;
                var startLineNumber = line.LineNumber;
                var nestedLevel = 0;

                Debug.WriteLine($"ExecuteRegionNavigation: Line {startLineNumber + 1}, IsStartRegion: {isStartRegion}, Text: {lineText.Trim()}");

                ITextSnapshotLine matchingLine = null;

                if (isStartRegion)
                {
                    // #region から対応する #endregion を検索
                    for (var i = startLineNumber + 1; i < snapshot.LineCount; i++)
                    {
                        var currentLine = snapshot.GetLineFromLineNumber(i);
                        var currentLineText = currentLine.GetText().TrimStart();

                        if (Regex.IsMatch(currentLineText, @"^#\s*region\b"))
                        {
                            nestedLevel++;
                            Debug.WriteLine($"Found nested #region at line {i + 1}, nestedLevel: {nestedLevel}");
                        }
                        else if (Regex.IsMatch(currentLineText, @"^#\s*endregion\b"))
                        {
                            if (nestedLevel == 0)
                            {
                                // 対応する #endregion が見つかった
                                Debug.WriteLine($"Found matching #endregion at line {i + 1}");
                                matchingLine = currentLine;
                                break;
                            }
                            nestedLevel--;
                            Debug.WriteLine($"Found nested #endregion at line {i + 1}, nestedLevel: {nestedLevel}");
                        }
                    }
                }
                else
                {
                    // #endregion から対応する #region を検索
                    for (var i = startLineNumber - 1; i >= 0; i--)
                    {
                        var currentLine = snapshot.GetLineFromLineNumber(i);
                        var currentLineText = currentLine.GetText().TrimStart();

                        if (Regex.IsMatch(currentLineText, @"^#\s*endregion\b"))
                        {
                            nestedLevel++;
                            Debug.WriteLine($"Found nested #endregion at line {i + 1}, nestedLevel: {nestedLevel}");
                        }
                        else if (Regex.IsMatch(currentLineText, @"^#\s*region\b"))
                        {
                            if (nestedLevel == 0)
                            {
                                // 対応する #region が見つかった
                                Debug.WriteLine($"Found matching #region at line {i + 1}");
                                matchingLine = currentLine;
                                break;
                            }
                            nestedLevel--;
                            Debug.WriteLine($"Found nested #region at line {i + 1}, nestedLevel: {nestedLevel}");
                        }
                    }
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

                    // 行をハイライト（一時的に）
                    HighlightLine(line);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MoveCaretToLine error: {ex.Message}");
            }
        }

        /// <summary>
        /// 行を一時的にハイライト
        /// </summary>
        private void HighlightLine(ITextSnapshotLine line)
        {
            try
            {
                // 行全体を選択
                _textView.Selection.Select(
                    new SnapshotSpan(line.Start, line.End),
                    false);

                // 500ミリ秒後に選択を解除
                var dispatcherTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };

                dispatcherTimer.Tick += (s, e) =>
                {
                    dispatcherTimer.Stop();
                    _textView.Selection.Clear();
                };

                dispatcherTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HighlightLine error: {ex.Message}");
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