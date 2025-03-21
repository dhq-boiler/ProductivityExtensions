using Microsoft.VisualStudio.Shell;
using Prism.Mvvm;
using Reactive.Bindings;
using Reactive.Bindings.Disposables;
using Reactive.Bindings.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using System.Windows.Input;
using CompositeDisposable = System.Reactive.Disposables.CompositeDisposable;

namespace boilersExtensions.ViewModels
{
    /// <summary>
    /// すべてのViewModelの基底クラス
    /// </summary>
    public abstract class ViewModelBase : BindableBase, IDisposable, INotifyPropertyChanged
    {
        // ReactivePropertyなどのリソース管理用
        protected CompositeDisposable Disposables { get; } = new CompositeDisposable();

        // 処理中かどうかを表すフラグ
        public ReactivePropertySlim<bool> IsProcessing { get; }
            = new ReactivePropertySlim<bool>(false);

        // 処理状況の説明文
        public ReactivePropertySlim<string> ProcessingStatus { get; }
            = new ReactivePropertySlim<string>("");

        // プログレスバー用の値（0-100）
        public ReactivePropertySlim<double> Progress { get; }
            = new ReactivePropertySlim<double>(0);

        // 非同期タスクのキャンセル用トークンソース
        protected System.Threading.CancellationTokenSource CancellationTokenSource { get; private set; }
            = new System.Threading.CancellationTokenSource();

        // 現在のPackage
        public AsyncPackage Package { get; set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        protected ViewModelBase()
        {
            // プロパティの変更通知をコンポジットDisposableに登録
            IsProcessing.AddTo(Disposables);
            ProcessingStatus.AddTo(Disposables);
            Progress.AddTo(Disposables);
        }

        /// <summary>
        /// 処理中状態の設定
        /// </summary>
        protected void SetProcessing(bool isProcessing, string statusMessage = "")
        {
            IsProcessing.Value = isProcessing;
            ProcessingStatus.Value = statusMessage;
            Progress.Value = 0;
        }

        /// <summary>
        /// プログレス値の更新
        /// </summary>
        protected void UpdateProgress(double progressValue, string statusMessage = null)
        {
            // 0-100の範囲に制限
            Progress.Value = Math.Max(0, Math.Min(100, progressValue));

            // ステータスメッセージが指定されていれば更新
            if (!string.IsNullOrEmpty(statusMessage))
            {
                ProcessingStatus.Value = statusMessage;
            }
        }

        /// <summary>
        /// 非同期処理の実行
        /// </summary>
        protected async Task RunAsync(Func<Task> asyncAction, string processingMessage, string errorMessage = null)
        {
            try
            {
                // 処理中状態に設定
                SetProcessing(true, processingMessage);

                // 非同期タスクを実行
                await asyncAction();
            }
            catch (Exception ex)
            {
                // エラーをログに出力
                Debug.WriteLine($"Error in {GetType().Name}.RunAsync: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);

                // エラーメッセージが指定されていればステータスに表示
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    ProcessingStatus.Value = errorMessage;
                }
            }
            finally
            {
                // 処理完了
                SetProcessing(false);
            }
        }

        /// <summary>
        /// UIスレッドの取得
        /// </summary>
        protected Microsoft.VisualStudio.Threading.JoinableTaskFactory GetJoinableTaskFactory()
        {
            return ThreadHelper.JoinableTaskFactory;
        }

        /// <summary>
        /// ダイアログが開かれた際に呼び出されるメソッド
        /// </summary>
        public virtual void OnDialogOpened(object dialog)
        {
            // 派生クラスでオーバーライドして実装
        }

        /// <summary>
        /// ダイアログが閉じられる際に呼び出されるメソッド
        /// </summary>
        public virtual void OnDialogClosing(object dialog)
        {
            // 派生クラスでオーバーライドして実装
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        public virtual void Dispose()
        {
            try
            {
                // キャンセルトークンソースをキャンセル
                if (CancellationTokenSource != null && !CancellationTokenSource.IsCancellationRequested)
                {
                    CancellationTokenSource.Cancel();
                    CancellationTokenSource.Dispose();
                    CancellationTokenSource = null;
                }

                // Reactive系リソースを解放
                Disposables.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in {GetType().Name}.Dispose: {ex.Message}");
            }
        }

        /// <summary>
        /// キャンセルトークンのリセット
        /// </summary>
        protected void ResetCancellationToken()
        {
            if (CancellationTokenSource != null)
            {
                CancellationTokenSource.Dispose();
            }
            CancellationTokenSource = new System.Threading.CancellationTokenSource();
        }

        /// <summary>
        /// UI上でメッセージを表示（ステータスバーなど）
        /// </summary>
        protected void ShowMessage(string message)
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