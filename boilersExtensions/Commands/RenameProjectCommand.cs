using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading.Tasks;
using boilersExtensions.Helpers;
using boilersExtensions.ViewModels;
using boilersExtensions.Views;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Package = Microsoft.VisualStudio.Shell.Package;

namespace boilersExtensions
{
    internal class RenameProjectCommand : OleMenuCommand
    {
        /// <summary>
        ///     Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        ///     Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("7d2cd062-6ec4-42dc-8c6d-019b9b5d57cf");

        /// <summary>
        ///     VS Package that provides this command, not null.
        /// </summary>
        private static AsyncPackage package;

        private static OleMenuCommand menuItem;


        private RenameProjectCommand() : base(Execute, new CommandID(CommandSet, CommandId)) =>
            base.BeforeQueryStatus += BeforeQueryStatus;

        /// <summary>
        ///     Gets the instance of the command.
        /// </summary>
        public static RenameProjectCommand Instance
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
            RenameProjectCommand.package = package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            menuItem = Instance = new RenameProjectCommand();
            menuItem.Text = ResourceService.GetString("RenameProject");
            commandService.AddCommand(Instance);
        }

        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 設定が無効な場合は何もしない
            if (!BoilersExtensionsSettings.IsRenameProjectEnabled)
            {
                Debug.WriteLine("RenameProject feature is disabled in settings");
                return;
            }

            // DTE オブジェクトの取得
            var dte = (DTE)Package.GetGlobalService(typeof(DTE));

            // アクティブなプロジェクトの配列を取得
            var activeSolutionProjects = dte.ActiveSolutionProjects as Array;

            // 配列から最初のプロジェクトを取得（通常、アクティブなプロジェクト）
            var activeProject = activeSolutionProjects?.GetValue(0) as Project;

            // プロジェクト名の取得
            var projectName = activeProject?.Name;

            var window = new RenameProjectDialog
            {
                DataContext = new RenameProjectDialogViewModel
                {
                    OldProjectName = { Value = projectName }, Package = package
                }
            };
            (window.DataContext as RenameProjectDialogViewModel).OnDialogOpened(window);
            window.ShowDialog();
        }

        /// <summary>
        ///     コマンドの有効/無効状態を更新
        /// </summary>
        private static void BeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand command)
            {
                // 設定で無効化されているかチェック
                var featureEnabled = BoilersExtensionsSettings.IsRenameProjectEnabled;

                if (!featureEnabled)
                {
                    // 機能が無効の場合はメニュー項目を非表示にする
                    command.Visible = false;
                    command.Enabled = false;
                    return;
                }

                // 機能が有効な場合は通常の条件で表示/非表示を決定
                command.Visible = true;
            }
        }
    }
}