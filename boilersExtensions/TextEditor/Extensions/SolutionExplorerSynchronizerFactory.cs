using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace boilersExtensions.TextEditor.Extensions
{
    /// <summary>
    ///     ファイルを開いた時にソリューションエクスプローラーで対応するファイルを選択・ハイライトする拡張機能
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class SolutionExplorerSynchronizerFactory : IWpfTextViewCreationListener
    {
        /// <summary>
        ///     テキストビュー作成時の処理
        /// </summary>
        public void TextViewCreated(IWpfTextView textView)
        {
            try
            {
                Debug.WriteLine("SolutionExplorerSynchronizerFactory.TextViewCreated called!");

                // テキストビューにイベント拡張機能を追加
                textView.Properties.GetOrCreateSingletonProperty(
                    () => new SolutionExplorerSynchronizer(textView));

                Debug.WriteLine("SolutionExplorerSynchronizer successfully attached to TextView");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in TextViewCreated: {ex.Message}");
            }
        }
    }

    /// <summary>
    ///     ソリューションエクスプローラー同期機能
    /// </summary>
    internal sealed class SolutionExplorerSynchronizer : IDisposable
    {
        // 同期間隔の最小値（ミリ秒）- ここを調整して頻度を下げる
        private const int MIN_SYNC_INTERVAL_MS = 2000;
        private readonly IWpfTextView _textView;
        private bool _isDisposed;
        private bool _isSynchronizing;
        private DateTime _lastSync = DateTime.MinValue;
        private string _lastSyncedFile = string.Empty;

        /// <summary>
        ///     コンストラクタ
        /// </summary>
        public SolutionExplorerSynchronizer(IWpfTextView textView)
        {
            Debug.WriteLine("SolutionExplorerSynchronizer constructor called!");

            _textView = textView;

            // イベントハンドラを解除（念のため）
            CleanupEventHandlers();

            var dte = (DTE)Package.GetGlobalService(typeof(DTE));
            if (dte == null)
            {
                Debug.WriteLine("DTE service not available");
                return;
            }

            // ActiveDocument の変更を監視（これは残す - ファイルが切り替わったときのみ）
            dte.Events.DocumentEvents.DocumentOpened += doc =>
            {
                Debug.WriteLine($"Document opened: {doc.FullName}");
                SynchronizeWithSolutionExplorer();
            };

            // 各種イベントのハンドラを登録
            // _textView.GotAggregateFocus += OnTextViewGotFocus; // この行を削除または無効化
            _textView.Closed += OnTextViewClosed;

            // テキストバッファの変更イベントは削除 - 編集中に同期させない
            // _textView.TextBuffer.Changed += OnTextBufferChanged;

            // 初回のファイル同期を実行
            SynchronizeWithSolutionExplorer();

            Debug.WriteLine("SolutionExplorerSynchronizer event handlers registered");
        }

        // サービスプロバイダー
        private static IAsyncServiceProvider ServiceProvider =>
            AsyncPackage.GetGlobalService(typeof(IAsyncServiceProvider)) as IAsyncServiceProvider;

        /// <summary>
        ///     リソースの解放
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                CleanupEventHandlers();
                Debug.WriteLine("SolutionExplorerSynchronizer disposed");
            }
        }

        /// <summary>
        ///     イベントハンドラを解除
        /// </summary>
        private void CleanupEventHandlers()
        {
            if (_textView != null)
            {
                _textView.GotAggregateFocus -= OnTextViewGotFocus;
                _textView.Closed -= OnTextViewClosed;

                if (_textView.TextBuffer != null)
                {
                    _textView.TextBuffer.Changed -= OnTextBufferChanged;
                }
            }
        }

        /// <summary>
        ///     テキストビューがフォーカスを得たとき - 無効化
        /// </summary>
        private void OnTextViewGotFocus(object sender, EventArgs e)
        {
            // フォーカス時の同期は無効化するか、コメントアウトする
            /*
            try
            {
                Debug.WriteLine("TextViewGotFocus event triggered");
                SynchronizeWithSolutionExplorer();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnTextViewGotFocus: {ex.Message}");
            }
            */
        }

        /// <summary>
        ///     テキストバッファが変更されたとき - 無効化
        /// </summary>
        private void OnTextBufferChanged(object sender, EventArgs e)
        {
            // バッファ変更時の同期は無効化するか、コメントアウトする
            /*
            try
            {
                Debug.WriteLine("TextBuffer changed event triggered");
                SynchronizeWithSolutionExplorer();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnTextBufferChanged: {ex.Message}");
            }
            */
        }

        /// <summary>
        ///     ソリューションエクスプローラーとの同期処理
        /// </summary>
        private void SynchronizeWithSolutionExplorer()
        {
            try
            {
                if (!BoilersExtensionsSettings.IsSyncToSolutionExplorerEnabled)
                {
                    Debug.WriteLine("SyncToSolutionExplorer feature is disabled in settings");
                    return;
                }

                // 実行中の場合は処理しない（再入防止）
                if (_isSynchronizing)
                {
                    Debug.WriteLine("Already synchronizing, skipping");
                    return;
                }

                // 過度な同期を防ぐためのスロットリング
                var now = DateTime.Now;
                var timeSinceLastSync = (now - _lastSync).TotalMilliseconds;
                if (timeSinceLastSync < MIN_SYNC_INTERVAL_MS)
                {
                    Debug.WriteLine(
                        $"Skipping sync - last sync was {timeSinceLastSync}ms ago (minimum: {MIN_SYNC_INTERVAL_MS}ms)");
                    return;
                }

                _isSynchronizing = true;

                // UIスレッドで実行
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    try
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        // DTEサービスを取得
                        var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                        if (dte == null)
                        {
                            Debug.WriteLine("DTE service not available");
                            return;
                        }

                        if (dte.ActiveDocument == null)
                        {
                            Debug.WriteLine("No active document, skipping synchronization");
                            return;
                        }

                        var filePath = dte.ActiveDocument.FullName;

                        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                        {
                            Debug.WriteLine($"Invalid file path or file doesn't exist: {filePath}");
                            return;
                        }

                        // 前回同期したファイルと同じなら処理しない
                        if (filePath == _lastSyncedFile)
                        {
                            Debug.WriteLine($"File already synchronized: {filePath}");
                            return;
                        }

                        Debug.WriteLine($"Synchronizing with file: {filePath}");

                        // ソリューションエクスプローラーで対象ファイルを選択
                        await SelectFileInSolutionExplorerAsync(dte, filePath);

                        // 同期情報を更新
                        _lastSync = DateTime.Now;
                        _lastSyncedFile = filePath;
                    }
                    finally
                    {
                        _isSynchronizing = false;
                    }
                });
            }
            catch (Exception ex)
            {
                _isSynchronizing = false;
                Debug.WriteLine($"Error in SynchronizeWithSolutionExplorer: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        ///     ソリューションエクスプローラーでファイルを選択・表示する
        /// </summary>
        private async Task SelectFileInSolutionExplorerAsync(DTE dte, string filePath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // 方法1: もっともシンプルで信頼性の高い方法 - SyncronizeコマンドおよびTrackコマンドを実行
                try
                {
                    // 複数のコマンドを試して、どれかが動作するようにする
                    var commandsToTry = new[]
                    {
                        "SolutionExplorer.SyncWithActiveDocument", // VS2019/2022での標準コマンド
                        "View.TrackDocumentInSolutionExplorer", // よく使われるコマンド
                        "View.SynchronizeClassView", // 別の関連コマンド
                        "SolutionExplorer.SynchronizeWithActiveDocument" // 別の表記
                    };

                    foreach (var commandName in commandsToTry)
                    {
                        try
                        {
                            Debug.WriteLine($"Executing {commandName} command");
                            dte.ExecuteCommand(commandName);
                            Debug.WriteLine($"Successfully executed {commandName}");

                            // いずれかのコマンドが成功したらループを抜ける
                            return;
                        }
                        catch (Exception specificCmdEx)
                        {
                            Debug.WriteLine($"Error executing {commandName}: {specificCmdEx.Message}");
                            // 次のコマンドを試す
                        }
                    }

                    // すべてのコマンドが失敗した場合
                    Debug.WriteLine("All synchronization commands failed, trying alternative methods");
                }
                catch (Exception cmdEx)
                {
                    Debug.WriteLine($"Error in command execution block: {cmdEx.Message}");
                    // 失敗した場合は次の方法を試す
                }

                // 方法2: ソリューションエクスプローラーウィンドウを直接操作
                try
                {
                    // ソリューションエクスプローラーのウィンドウを取得
                    Window solutionExplorer = null;
                    try
                    {
                        solutionExplorer = dte.Windows.Item(Constants.vsWindowKindSolutionExplorer);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error getting Solution Explorer window: {ex.Message}");
                    }

                    if (solutionExplorer != null)
                    {
                        // ソリューションエクスプローラーをアクティブにしない - ここを変更
                        // この部分をコメントアウトするか削除して、フォーカスを取らないようにする
                        /*
                        try
                        {
                            solutionExplorer.Activate();
                            Debug.WriteLine("Solution Explorer window activated");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error activating Solution Explorer: {ex.Message}");
                        }
                        */
                    }
                    else
                    {
                        Debug.WriteLine("Solution Explorer window not found");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error manipulating Solution Explorer window: {ex.Message}");
                }

                // 方法3: アクティブなドキュメントからProjectItemを直接取得
                try
                {
                    Debug.WriteLine("Trying to select project item directly from active document");
                    if (dte.ActiveDocument != null && dte.ActiveDocument.ProjectItem != null)
                    {
                        try
                        {
                            var projectItem = dte.ActiveDocument.ProjectItem;

                            // ProjectItem.Select メソッドがないため、代替手段を使用
                            // 代わりに ExpandView() を呼び出すか、ActiveDocument のまま操作
                            if (projectItem != null)
                            {
                                try
                                {
                                    // プロジェクトアイテムの親フォルダを展開（存在する場合）
                                    projectItem.ExpandView();
                                    Debug.WriteLine("Expanded project item view");

                                    // DTE コマンドを使用して選択（SolutionExplorer.SyncWithActiveDocument）
                                    // ソリューションエクスプローラーに切り替えない方法に変更
                                    if (dte.Commands.Item("SolutionExplorer.SyncWithActiveDocument").IsAvailable)
                                    {
                                        dte.ExecuteCommand("SolutionExplorer.SyncWithActiveDocument");
                                        Debug.WriteLine("Executed SolutionExplorer.SyncWithActiveDocument command");
                                    }
                                }
                                catch (Exception cmdEx)
                                {
                                    Debug.WriteLine($"Error expanding or syncing: {cmdEx.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error accessing ActiveDocument.ProjectItem: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error accessing ActiveDocument.ProjectItem: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SelectFileInSolutionExplorer: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        ///     テキストビューが閉じられたときのクリーンアップ
        /// </summary>
        private void OnTextViewClosed(object sender, EventArgs e)
        {
            Debug.WriteLine("TextViewClosed event triggered - cleaning up");
            Dispose();
        }
    }
}