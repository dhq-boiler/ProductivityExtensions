using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using boilersExtensions.ViewModels;
using boilersExtensions.Views;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Reactive.Bindings;
using Reactive.Bindings.Disposables;
using Reactive.Bindings.Extensions;
using Package = Microsoft.VisualStudio.Shell.Package;

namespace boilersExtensions.Commands
{
    internal class BatchUpdateGuidCommand : OleMenuCommand
    {
        /// <summary>
        ///     Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        ///     Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("6f89e4ab-2b85-49b6-a2d9-3f9b78e02acf");

        /// <summary>
        ///     VS Package that provides this command, not null.
        /// </summary>
        private static AsyncPackage package;

        private static OleMenuCommand menuItem;

        private BatchUpdateGuidCommand() : base(Execute, new CommandID(CommandSet, CommandId))
        {
            base.BeforeQueryStatus += BeforeQueryStatus;
        }

        /// <summary>
        ///     Gets the instance of the command.
        /// </summary>
        public static BatchUpdateGuidCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        ///     Gets the service provider from the owner Package.
        /// </summary>
        private static IAsyncServiceProvider ServiceProvider => package;

        public static async Task InitializeAsync(AsyncPackage package)
        {
            BatchUpdateGuidCommand.package = package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            menuItem = Instance = new BatchUpdateGuidCommand();
            commandService.AddCommand(Instance);
        }

        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 設定が無効な場合は何もしない
            if (!BoilersExtensionsSettings.IsBatchUpdateGuidEnabled)
            {
                Debug.WriteLine("BatchUpdateGuid feature is disabled in settings");
                return;
            }

            // DTEオブジェクトを取得
            var dte = (DTE)Package.GetGlobalService(typeof(DTE));
            var textDocument = dte.ActiveDocument.Object("TextDocument") as TextDocument;

            if (textDocument != null)
            {
                // ドキュメント内のすべてのGUIDを検出
                var guids = FindAllGuidsInDocument(textDocument);

                if (guids.Count == 0)
                {
                    // GUIDが見つからない場合
                    VsShellUtilities.ShowMessageBox(
                        package,
                        "ドキュメント内にGUIDが見つかりませんでした。",
                        "GUID一括更新",
                        OLEMSGICON.OLEMSGICON_INFO,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return;
                }

                // ダイアログを表示してGUIDの選択を行う
                var window = new GuidSelectionDialog
                {
                    DataContext = new GuidSelectionDialogViewModel
                    {
                        GuidList = guids, Package = package, Document = textDocument
                    }
                };
                (window.DataContext as GuidSelectionDialogViewModel).OnDialogOpened(window);
                window.ShowDialog();
            }
        }

        private static void BeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand command)
            {
                // 設定で無効化されているかチェック
                bool featureEnabled = BoilersExtensionsSettings.IsBatchUpdateGuidEnabled;

                if (!featureEnabled)
                {
                    // 機能が無効の場合はメニュー項目を非表示にする
                    command.Visible = false;
                    command.Enabled = false;
                    return;
                }

                // 機能が有効な場合は通常の条件で表示/非表示を決定
                command.Visible = true;

                // DTEオブジェクトを取得
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));

                // アクティブなドキュメントがある場合のみ有効化
                command.Enabled = dte.ActiveDocument != null;
            }
        }

        /// <summary>
        ///     ドキュメント内のすべてのGUIDを検出する
        /// </summary>
        private static List<GuidInfo> FindAllGuidsInDocument(TextDocument textDocument)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var result = new List<GuidInfo>();

            // ドキュメント全体のテキストを取得
            var editPoint = textDocument.StartPoint.CreateEditPoint();
            var documentText = editPoint.GetText(textDocument.EndPoint);

            // GUIDパターンの正規表現
            var guidPattern = @"(\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?)";

            // すべての一致を検索
            var matches = Regex.Matches(documentText, guidPattern);
            var uniqueGuids = new HashSet<string>();

            foreach (Match match in matches)
            {
                var guidText = match.Groups[1].Value;
                if (uniqueGuids.Add(guidText))
                {
                    result.Add(new GuidInfo(
                        guidText,
                        null,
                        true,
                        CountOccurrences(documentText, guidText)
                    ));
                }
            }

            return result;
        }

        /// <summary>
        ///     テキスト内での特定の文字列の出現回数をカウント
        /// </summary>
        private static int CountOccurrences(string text, string pattern)
        {
            var count = 0;
            var i = 0;
            while ((i = text.IndexOf(pattern, i)) != -1)
            {
                i += pattern.Length;
                count++;
            }

            return count;
        }
    }

    /// <summary>
    ///     GUIDの位置情報を保持するクラス
    /// </summary>
    public class GuidPositionInfo
    {
        public string Guid { get; set; }
        public List<TextPosition> Positions { get; set; } = new List<TextPosition>();
        public int Occurrences => Positions.Count;
    }

    /// <summary>
    ///     テキスト内の位置を表すクラス
    /// </summary>
    public class TextPosition
    {
        public int Line { get; set; }
        public int Column { get; set; }
    }

    /// <summary>
    ///     Reactive版GUID情報を保持するクラス
    /// </summary>
    public class GuidInfo : IDisposable
    {
        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        public GuidInfo()
        {
            OriginalGuid = new ReactivePropertySlim<string>().AddTo(_disposables);
            NewGuid = new ReactivePropertySlim<string>().AddTo(_disposables);
            IsSelected = new ReactivePropertySlim<bool>(true).AddTo(_disposables);
            Occurrences = new ReactivePropertySlim<int>().AddTo(_disposables);
        }

        public GuidInfo(string originalGuid, string newGuid, bool isSelected, int occurrences)
        {
            OriginalGuid = new ReactivePropertySlim<string>(originalGuid).AddTo(_disposables);
            NewGuid = new ReactivePropertySlim<string>(newGuid).AddTo(_disposables);
            IsSelected = new ReactivePropertySlim<bool>(isSelected).AddTo(_disposables);
            Occurrences = new ReactivePropertySlim<int>(occurrences).AddTo(_disposables);
        }

        public ReactivePropertySlim<string> OriginalGuid { get; }
        public ReactivePropertySlim<string> NewGuid { get; }
        public ReactivePropertySlim<bool> IsSelected { get; }
        public ReactivePropertySlim<int> Occurrences { get; }

        public void Dispose() => _disposables.Dispose();
    }
}