using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading.Tasks;
using boilersExtensions.ViewModels;
using boilersExtensions.Views;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace boilersExtensions.Commands
{
    internal class RenameSolutionCommand : OleMenuCommand
    {
        /// <summary>
        ///     Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        ///     Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("3854c682-aa0a-414a-b9ce-6dfc719d12d3");

        /// <summary>
        ///     VS Package that provides this command, not null.
        /// </summary>
        private static AsyncPackage package;

        private static OleMenuCommand menuItem;


        private RenameSolutionCommand() : base(Execute, new CommandID(CommandSet, CommandId))
        {
        }

        /// <summary>
        ///     Gets the instance of the command.
        /// </summary>
        public static RenameSolutionCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        ///     Gets the service provider from the owner package.
        /// </summary>
        private static IAsyncServiceProvider ServiceProvider => package;

        public static async Task InitializeAsync(AsyncPackage package)
        {
            RenameSolutionCommand.package = package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            menuItem = Instance = new RenameSolutionCommand();
            commandService.AddCommand(Instance);
        }

        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // DTE オブジェクトの取得
            var dte = (DTE)Package.GetGlobalService(typeof(DTE));

            var solution = dte.Solution;

            //ソリューション名の取得
            var solutionName = Path.GetFileNameWithoutExtension(solution?.FullName);

            var window = new RenameSolutionDialog
            {
                DataContext = new RenameSolutionDialogViewModel
                {
                    OldSolutionName = { Value = solutionName }, Package = package
                }
            };
            (window.DataContext as RenameSolutionDialogViewModel).OnDialogOpened(window);
            window.ShowDialog();
        }
    }
}