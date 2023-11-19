using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO.Packaging;
using System.Linq;
using System.Management.Instrumentation;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using EnvDTE;
using Package = Microsoft.VisualStudio.Shell.Package;

namespace boilersExtensions
{
    internal class RenameProjectCommand : OleMenuCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("7d2cd062-6ec4-42dc-8c6d-019b9b5d57cf");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private static AsyncPackage package;

        private static OleMenuCommand menuItem;

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static RenameProjectCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private static IAsyncServiceProvider ServiceProvider => package;


        private RenameProjectCommand() : base(Execute, new CommandID(CommandSet, CommandId))
        {
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            RenameProjectCommand.package = package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            menuItem = Instance = new RenameProjectCommand();
            commandService.AddCommand(Instance);
        }

        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // DTE オブジェクトの取得
            DTE dte = (DTE)Package.GetGlobalService(typeof(DTE));

            // アクティブなプロジェクトの配列を取得
            Array activeSolutionProjects = dte.ActiveSolutionProjects as Array;

            // 配列から最初のプロジェクトを取得（通常、アクティブなプロジェクト）
            Project activeProject = activeSolutionProjects?.GetValue(0) as Project;

            // プロジェクト名の取得
            string projectName = activeProject?.Name;

            var window = new RenameProjectDialog()
            {
                DataContext = new RenameProjectDialogViewModel()
                {
                    OldProjectName =
                    {
                        Value = projectName
                    }
                }
            };
            (window.DataContext as RenameProjectDialogViewModel).OnDialogOpened(window);
            window.ShowDialog();
        }
    }
}
