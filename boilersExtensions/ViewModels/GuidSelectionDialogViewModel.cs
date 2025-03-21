using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using boilersExtensions.Commands;
using boilersExtensions.TextEditor.Adornments;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Prism.Mvvm;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using ZLinq;
using Window = System.Windows.Window;

namespace boilersExtensions.ViewModels
{
    internal class GuidSelectionDialogViewModel : BindableBase, IDisposable
    {
        private readonly CompositeDisposable _compositeDisposable = new CompositeDisposable();

        private Dictionary<string, GuidPositionInfo> _guidPositions = new Dictionary<string, GuidPositionInfo>();

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

                        // アナライザーを一時停止
                        UnusedParameterAdornment.PauseAnalysis();

                        // 選択されたGUIDのみ処理
                        var selectedGuids = GuidList.FindAll(g =>
                            g.IsSelected.Value && !string.IsNullOrEmpty(g.NewGuid.Value));

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
                        var dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));
                        dte.UndoContext.Open("Update Multiple GUIDs");

                        try
                        {
                            // 各GUIDを置換
                            for (var i = 0; i < selectedGuids.Count; i++)
                            {
                                var guidInfo = selectedGuids[i];
                                var originalGuid = guidInfo.OriginalGuid.Value;

                                // 進捗状況を更新
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    ProcessingStatus.Value = $"更新中: {originalGuid} ({i + 1}/{selectedGuids.Count})";
                                    Progress.Value = (double)(i + 1) / selectedGuids.Count * 100;
                                });

                                // 少し時間を空けてUIの更新を許可
                                await Task.Delay(10);

                                // キャッシュした位置情報を使ってGUIDを置換
                                if (_guidPositions.TryGetValue(originalGuid, out var posInfo))
                                {
                                    ReplaceGuidAtPositions(Document, posInfo, guidInfo.NewGuid.Value);
                                }
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
                        // アナライザーを再開
                        UnusedParameterAdornment.ResumeAnalysis();

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
                        await Task.Run(() =>
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
                        await Task.Run(() =>
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
                        await Task.Run(() =>
                        {
                            foreach (var guid in GuidList)
                            {
                                if (guid.IsSelected.Value)
                                {
                                    // 元のGUIDと同じフォーマット(中括弧付きかなしか)で新しいGUIDを生成
                                    var newGuid = Guid.NewGuid().ToString();
                                    if (guid.OriginalGuid.Value.StartsWith("{") &&
                                        guid.OriginalGuid.Value.EndsWith("}"))
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

        public ReactiveCommand UpdateGuidsCommand { get; }
        public ReactiveCommand CancelCommand { get; } = new ReactiveCommand();
        public ReactiveCommand SelectAllCommand { get; } = new ReactiveCommand();
        public ReactiveCommand UnselectAllCommand { get; } = new ReactiveCommand();
        public ReactiveCommand GenerateNewGuidsCommand { get; } = new ReactiveCommand();

        public List<GuidInfo> GuidList { get; set; } = new List<GuidInfo>();

        public ReactivePropertySlim<bool> PreviewChanges { get; } = new ReactivePropertySlim<bool>(true);

        // 処理中かどうかのフラグ
        public ReactivePropertySlim<bool> IsProcessing { get; } = new ReactivePropertySlim<bool>();

        // 進捗率（0-100）
        public ReactivePropertySlim<double> Progress { get; } = new ReactivePropertySlim<double>();

        // 処理状況メッセージ
        public ReactivePropertySlim<string> ProcessingStatus { get; } = new ReactivePropertySlim<string>("準備完了");

        public Window Window { get; set; }

        public AsyncPackage Package { get; set; }

        public TextDocument Document { get; set; }

        public string Title => "GUIDの一括更新";


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

        /// <summary>
        ///     位置情報を使って効率的に置換
        /// </summary>
        private void ReplaceGuidAtPositions(TextDocument textDocument, GuidPositionInfo guidInfo, string newGuid)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 編集操作の順序を逆順にすることで、置換による位置のずれを回避
            foreach (var position in guidInfo.Positions
                         .AsValueEnumerable().OrderByDescending(p => p.Line).ThenByDescending(p => p.Column).ToList())
            {
                var editPoint = textDocument.StartPoint.CreateEditPoint();
                editPoint.MoveToLineAndOffset(position.Line, position.Column);

                // 古いGUIDを削除して新しいGUIDを挿入
                editPoint.Delete(guidInfo.Guid.Length);
                editPoint.Insert(newGuid);
            }
        }

        /// <summary>
        ///     ドキュメント内のすべてのGUIDを位置情報とともに検出
        /// </summary>
        private Dictionary<string, GuidPositionInfo> FindAllGuidsWithPositions(TextDocument textDocument)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var result = new Dictionary<string, GuidPositionInfo>();
            var guidPattern = @"(\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?)";

            // 各行ごとに処理
            for (var lineNum = 1; lineNum <= textDocument.EndPoint.Line; lineNum++)
            {
                var line = textDocument.StartPoint.CreateEditPoint();
                line.MoveToLineAndOffset(lineNum, 1);
                var lineText = line.GetLines(lineNum, lineNum + 1);

                // 行内のGUIDを検出
                var matches = Regex.Matches(lineText, guidPattern);
                foreach (Match match in matches)
                {
                    var guidText = match.Groups[1].Value;

                    if (!result.TryGetValue(guidText, out var guidInfo))
                    {
                        guidInfo = new GuidPositionInfo { Guid = guidText };
                        result[guidText] = guidInfo;
                    }

                    // 位置情報を記録（列位置はマッチの開始位置 + 1）
                    guidInfo.Positions.Add(new TextPosition { Line = lineNum, Column = match.Index + 1 });
                }
            }

            return result;
        }

        public async void OnDialogOpened(Window window)
        {
            Window = window;

            // 処理開始
            IsProcessing.Value = true;
            ProcessingStatus.Value = "GUIDを検索中...";

            try
            {
                // UIスレッドをブロックしないためにバックグラウンドで実行
                await Task.Run(() =>
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        // UIスレッドに切り替え
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        // 位置情報付きでGUIDを検出
                        _guidPositions = FindAllGuidsWithPositions(Document);

                        // GuidInfoリストを作成
                        GuidList = _guidPositions.Values
                            .AsValueEnumerable().Select(pos =>
                                new GuidInfo(
                                    pos.Guid,
                                    null,
                                    true,
                                    pos.Occurrences
                                )).ToList();
                    });
                });

                // 新しいGUIDを生成
                GenerateNewGuidsCommand.Execute();
            }
            finally
            {
                ProcessingStatus.Value = "準備完了";
                IsProcessing.Value = false;
            }
        }
    }
}