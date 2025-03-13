using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Prism.Mvvm;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using boilersExtensions.Commands;
using System.Diagnostics;
using System.Threading.Tasks;

namespace boilersExtensions.ViewModels
{
    internal class GuidSelectionDialogViewModel : BindableBase, IDisposable
    {
        private CompositeDisposable _compositeDisposable = new CompositeDisposable();

        public ReactiveCommand UpdateGuidsCommand { get; }
        public ReactiveCommand CancelCommand { get; } = new ReactiveCommand();
        public ReactiveCommand SelectAllCommand { get; } = new ReactiveCommand();
        public ReactiveCommand UnselectAllCommand { get; } = new ReactiveCommand();
        public ReactiveCommand GenerateNewGuidsCommand { get; } = new ReactiveCommand();

        public List<GuidInfo> GuidList { get; set; } = new List<GuidInfo>();

        public ReactivePropertySlim<bool> PreviewChanges { get; } = new ReactivePropertySlim<bool>(true);

        // 処理中かどうかのフラグ
        public ReactivePropertySlim<bool> IsProcessing { get; } = new ReactivePropertySlim<bool>(false);

        // 進捗率（0-100）
        public ReactivePropertySlim<double> Progress { get; } = new ReactivePropertySlim<double>(0);

        // 処理状況メッセージ
        public ReactivePropertySlim<string> ProcessingStatus { get; } = new ReactivePropertySlim<string>("準備完了");

        public System.Windows.Window Window { get; set; }

        public AsyncPackage Package { get; set; }

        public TextDocument Document { get; set; }

        public string Title => "GUIDの一括更新";

        public GuidSelectionDialogViewModel()
        {
            UpdateGuidsCommand = new ReactiveCommand();
            UpdateGuidsCommand.Subscribe(async () =>
            {
                try
                {
                    // 処理開始
                    IsProcessing.Value = true;
                    Progress.Value = 0;

                    // 選択されたGUIDのみ処理
                    var selectedGuids = GuidList.FindAll(g => g.IsSelected.Value && !string.IsNullOrEmpty(g.NewGuid.Value));

                    if (selectedGuids.Count == 0)
                    {
                        MessageBox.Show("置換するGUIDが選択されていないか、新しいGUIDが生成されていません。",
                                        "GUID一括更新",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information);
                        return;
                    }

                    // UI スレッドに切り替え
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // DTEのUndoContextを開始
                    DTE dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
                    dte.UndoContext.Open("Update Multiple GUIDs");

                    try
                    {
                        // 各GUIDを置換
                        for (int i = 0; i < selectedGuids.Count; i++)
                        {
                            var guidInfo = selectedGuids[i];

                            // 進捗状況を更新（UI更新のためにDispatcher経由）
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                ProcessingStatus.Value = $"更新中: {guidInfo.OriginalGuid.Value} ({i + 1}/{selectedGuids.Count})";
                                Progress.Value = (double)(i + 1) / selectedGuids.Count * 100;
                            });

                            // 少し時間を空けてUIの更新を許可
                            await Task.Delay(10);

                            // GUIDを置換
                            ReplaceAllGuidOccurrences(Document, guidInfo.OriginalGuid.Value, guidInfo.NewGuid.Value);
                        }

                        // 成功メッセージを表示
                        MessageBox.Show($"{selectedGuids.Count}個のGUIDを更新しました。",
                                        "GUID一括更新",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information);

                        // ダイアログを閉じる
                        Window.Close();
                    }
                    finally
                    {
                        // UndoContextを閉じる
                        dte.UndoContext.Close();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"エラーが発生しました: {ex.Message}",
                                    "GUID一括更新エラー",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                }
                finally
                {
                    // 処理終了
                    ProcessingStatus.Value = "完了";
                    IsProcessing.Value = false;
                }
            })
.AddTo(_compositeDisposable);

            CancelCommand.Subscribe(() =>
            {
                Window.Close();
            })
                .AddTo(_compositeDisposable);

            // SelectAllCommand の更新
            SelectAllCommand.Subscribe(async () =>
                {
                    try
                    {
                        // 処理開始
                        IsProcessing.Value = true;

                        // UIスレッドをブロックしないために処理を非同期化
                        await System.Threading.Tasks.Task.Run(() =>
                        {
                            foreach (var guid in GuidList)
                            {
                                guid.IsSelected.Value = true;
                            }
                        });

                        // ViewModelの変更を通知するためにPropertyChangedを発火
                        RaisePropertyChanged(nameof(GuidList));
                    }
                    finally
                    {
                        // 処理終了
                        IsProcessing.Value = false;
                    }
                })
                .AddTo(_compositeDisposable);

            // UnselectAllCommand の更新
            UnselectAllCommand.Subscribe(async () =>
                {
                    try
                    {
                        // 処理開始
                        IsProcessing.Value = true;

                        // UIスレッドをブロックしないために処理を非同期化
                        await System.Threading.Tasks.Task.Run(() =>
                        {
                            foreach (var guid in GuidList)
                            {
                                guid.IsSelected.Value = false;
                            }
                        });

                        // ViewModelの変更を通知するためにPropertyChangedを発火
                        RaisePropertyChanged(nameof(GuidList));
                    }
                    finally
                    {
                        // 処理終了
                        IsProcessing.Value = false;
                    }
                })
                .AddTo(_compositeDisposable);

            GenerateNewGuidsCommand.Subscribe(async () =>
                {
                    try
                    {
                        // 処理開始
                        IsProcessing.Value = true;

                        // UIスレッドをブロックしないために処理を非同期化
                        await System.Threading.Tasks.Task.Run(() =>
                        {
                            foreach (var guid in GuidList)
                            {
                                if (guid.IsSelected.Value)
                                {
                                    // 元のGUIDと同じフォーマット(中括弧付きかなしか)で新しいGUIDを生成
                                    string newGuid = Guid.NewGuid().ToString();
                                    if (guid.OriginalGuid.Value.StartsWith("{") && guid.OriginalGuid.Value.EndsWith("}"))
                                    {
                                        newGuid = "{" + newGuid + "}";
                                    }
                                    guid.NewGuid.Value = newGuid;
                                }
                            }
                        });

                        // ViewModelの変更を通知するためにPropertyChangedを発火
                        RaisePropertyChanged(nameof(GuidList));
                    }
                    finally
                    {
                        // 処理終了
                        IsProcessing.Value = false;
                    }
                })
                .AddTo(_compositeDisposable);
        }

        private void UpdateSelectedGuids()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 選択されたGUIDのみ処理
            var selectedGuids = GuidList.FindAll(g => g.IsSelected.Value && !string.IsNullOrEmpty(g.NewGuid.Value));

            if (selectedGuids.Count == 0)
            {
                MessageBox.Show("置換するGUIDが選択されていないか、新しいGUIDが生成されていません。",
                    "GUID一括更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // DTEのUndoContextを開始
            DTE dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
            dte.UndoContext.Open("Update Multiple GUIDs");

            try
            {
                // 各GUIDを置換
                foreach (var guidInfo in selectedGuids)
                {
                    ReplaceAllGuidOccurrences(Document, guidInfo.OriginalGuid.Value, guidInfo.NewGuid.Value);
                }

                // 成功メッセージを表示
                MessageBox.Show($"{selectedGuids.Count}個のGUIDを更新しました。",
                    "GUID一括更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            finally
            {
                // UndoContextを閉じる
                dte.UndoContext.Close();
            }
        }

        /// <summary>
        /// ドキュメント内のすべての一致するGUIDを新しいGUIDで置換
        /// </summary>
        private void ReplaceAllGuidOccurrences(TextDocument textDocument, string oldGuid, string newGuid)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // ドキュメントの先頭に移動
            var searchPoint = textDocument.StartPoint.CreateEditPoint();

            // 最初の出現箇所を検索
            TextRanges replacements = null;
            bool found = searchPoint.FindPattern(oldGuid, (int)EnvDTE.vsFindOptions.vsFindOptionsMatchCase, Tags: replacements);

            // 見つかる限り置換を続ける
            while (found)
            {
                // 見つかった範囲にカーソルを移動
                var editPoint = searchPoint.CreateEditPoint();

                // 古いGUIDを削除して新しいGUIDを挿入
                editPoint.Delete(oldGuid.Length);
                editPoint.Insert(newGuid);

                // 次の出現を検索
                found = searchPoint.FindPattern(oldGuid, (int)EnvDTE.vsFindOptions.vsFindOptionsMatchCase, Tags: replacements);
            }
        }

        public void OnDialogOpened(System.Windows.Window window)
        {
            this.Window = window;

            // 初期化時に新しいGUIDを生成
            GenerateNewGuidsCommand.Execute();
        }
        public void Dispose()
        {
            _compositeDisposable?.Dispose();
            UpdateGuidsCommand?.Dispose();
            CancelCommand?.Dispose();
            SelectAllCommand?.Dispose();
            UnselectAllCommand?.Dispose();
            GenerateNewGuidsCommand?.Dispose();
            PreviewChanges?.Dispose();
            IsProcessing?.Dispose();
        }
    }
}