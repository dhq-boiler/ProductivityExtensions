using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace boilersExtensions.Commands
{
    /// <summary>
    ///     ソリューションエクスプローラーで現在のファイルを選択するコマンド
    /// </summary>
    internal sealed class SyncToSolutionExplorerCommand : OleMenuCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("b9af64c5-3f2d-4a53-a5c3-924a11e8d439");
        private static AsyncPackage package;
        private static OleMenuCommand menuItem;

        /// <summary>
        ///     コンストラクタ
        /// </summary>
        private SyncToSolutionExplorerCommand() : base(Execute, new CommandID(CommandSet, CommandId)) =>
            base.BeforeQueryStatus += BeforeQueryStatus;

        public static SyncToSolutionExplorerCommand Instance { get; private set; }
        private static IAsyncServiceProvider ServiceProvider => package;

        /// <summary>
        ///     初期化
        /// </summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            SyncToSolutionExplorerCommand.package = package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            menuItem = Instance = new SyncToSolutionExplorerCommand();
            menuItem.Text = Resources.ResourceService.GetString("ViewInSolutionExplorer");
            commandService.AddCommand(Instance);

            Debug.WriteLine("SyncToSolutionExplorerCommand initialized successfully");
        }

        /// <summary>
        ///     コマンド実行時の処理
        /// </summary>
        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // 設定が無効な場合は何もしない
                if (!BoilersExtensionsSettings.IsSyncToSolutionExplorerEnabled)
                {
                    Debug.WriteLine("SyncToSolutionExplorer feature is disabled in settings");
                    return;
                }

                Debug.WriteLine("SyncToSolutionExplorerCommand Execute called");

                // DTEオブジェクトを取得
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                if (dte == null)
                {
                    Debug.WriteLine("DTE service not available");
                    return;
                }

                // アクティブなドキュメントがない場合は何もしない
                if (dte.ActiveDocument == null)
                {
                    Debug.WriteLine("No active document found");
                    return;
                }

                // アクティブなドキュメントのパスを取得
                var filePath = dte.ActiveDocument.FullName;
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    Debug.WriteLine($"Invalid file path or file doesn't exist: {filePath}");
                    return;
                }

                Debug.WriteLine($"Synchronizing with file: {filePath}");

                // ソリューションエクスプローラーで対象ファイルを選択・表示
                SelectFileInSolutionExplorer(dte, filePath);

                // 成功メッセージをステータスバーに表示
                dte.StatusBar.Text = "ソリューションエクスプローラーで選択しました";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Execute: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        ///     ソリューションエクスプローラーでファイルを選択・表示する
        /// </summary>
        private static void SelectFileInSolutionExplorer(DTE dte, string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

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
                        // ソリューションエクスプローラーをアクティブにする
                        try
                        {
                            solutionExplorer.Activate();
                            Debug.WriteLine("Solution Explorer window activated");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error activating Solution Explorer: {ex.Message}");
                        }
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
                                    dte.ExecuteCommand("SolutionExplorer.SyncWithActiveDocument");
                                    Debug.WriteLine("Executed SolutionExplorer.SyncWithActiveDocument command");
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
        ///     コマンドの有効/無効状態を更新
        /// </summary>
        private static new void BeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand command)
            {
                // 設定で無効化されているかチェック
                var featureEnabled = BoilersExtensionsSettings.IsSyncToSolutionExplorerEnabled;

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
                command.Enabled = dte?.ActiveDocument != null;
            }
        }
    }
}