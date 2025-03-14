using boilersExtensions.Commands;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;
using System.Windows.Input;
using System.Windows.Threading;

namespace boilersExtensions.TextEditor.Extensions
{
    /// <summary>
    /// テキストエディターの拡張機能ファクトリ
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class TextEditorExtensionsFactory : IWpfTextViewCreationListener
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
            // テキストビューにダブルクリックイベントなどの拡張機能を追加
            textView.Properties.GetOrCreateSingletonProperty(
                () => new TextEditorExtensions(textView, NavigatorService));
        }
    }

    /// <summary>
    /// テキストエディタの拡張機能
    /// </summary>
    internal sealed class TextEditorExtensions
    {
        private readonly IWpfTextView _textView;
        private readonly ITextStructureNavigatorSelectorService _navigatorService;
        private readonly DispatcherTimer _doubleClickTimer;
        private bool _isSingleClickProcessed = false;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public TextEditorExtensions(IWpfTextView textView, ITextStructureNavigatorSelectorService navigatorService)
        {
            _textView = textView;
            _navigatorService = navigatorService;

            // ダブルクリック検出用のタイマー
            _doubleClickTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime)
            };
            _doubleClickTimer.Tick += OnDoubleClickTimerElapsed;

            // マウスイベントのハンドラを登録
            _textView.VisualElement.MouseLeftButtonDown += OnMouseLeftButtonDown;
            _textView.VisualElement.MouseLeftButtonUp += OnMouseLeftButtonUp;
            _textView.Closed += OnTextViewClosed;
        }

        /// <summary>
        /// マウスの左ボタンが押されたときの処理
        /// </summary>
        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 型変更機能のみをサポートするため、CTRLキーが押されている場合のみ処理
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            // ダブルクリックかどうかをチェック
            if (e.ClickCount == 2)
            {
                // ダブルクリック - 型変更コマンドを呼び出す
                ExecuteTypeHierarchyCommand();

                // イベントを処理済みとしてマーク
                e.Handled = true;
                _isSingleClickProcessed = false;
                _doubleClickTimer.Stop();
            }
            else if (e.ClickCount == 1)
            {
                // 単一クリック - タイマーを開始
                _doubleClickTimer.Start();
                _isSingleClickProcessed = false;
            }
        }

        /// <summary>
        /// マウスの左ボタンが離されたときの処理
        /// </summary>
        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // CTRLキーが押されていない場合は何もしない
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                _doubleClickTimer.Stop();
                _isSingleClickProcessed = false;
                return;
            }

            // 単一クリックの場合、タイマーがまだ動いていて、単一クリックが未処理なら処理
            if (!_isSingleClickProcessed && _doubleClickTimer.IsEnabled)
            {
                // 単一クリック処理済みとマーク（ダブルクリック時に重複実行しないため）
                _isSingleClickProcessed = true;
            }
        }

        /// <summary>
        /// ダブルクリックタイマーが終了したときの処理
        /// </summary>
        private void OnDoubleClickTimerElapsed(object sender, EventArgs e)
        {
            _doubleClickTimer.Stop();
            _isSingleClickProcessed = false;
        }

        /// <summary>
        /// 型階層コマンドを実行
        /// </summary>
        private void ExecuteTypeHierarchyCommand()
        {
            try
            {
                // UIスレッドに切り替え
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // 型階層コマンドの実行
                    var command = TypeHierarchyCommand.Instance;
                    if (command != null)
                    {
                        command.Invoke();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error executing TypeHierarchyCommand: {ex.Message}");
            }
        }

        /// <summary>
        /// テキストビューが閉じられたときのクリーンアップ
        /// </summary>
        private void OnTextViewClosed(object sender, EventArgs e)
        {
            // イベントハンドラを解除
            _textView.VisualElement.MouseLeftButtonDown -= OnMouseLeftButtonDown;
            _textView.VisualElement.MouseLeftButtonUp -= OnMouseLeftButtonUp;
            _textView.Closed -= OnTextViewClosed;

            // タイマーを停止
            _doubleClickTimer.Stop();
            _doubleClickTimer.Tick -= OnDoubleClickTimerElapsed;
        }
    }
}