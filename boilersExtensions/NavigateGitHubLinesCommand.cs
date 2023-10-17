using EnvDTE;
using LibGit2Sharp;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TeamFoundation.Git.Extensibility;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace boilersExtensions
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class NavigateGitHubLinesCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("b19148c9-0670-418f-bce5-1845978d4302");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="NavigateGitHubLinesCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private NavigateGitHubLinesCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static NavigateGitHubLinesCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in NavigateGitHubLinesCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new NavigateGitHubLinesCommand(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            //string message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.MenuItemCallback()", this.GetType().FullName);
            //string title = "NavigateGitHubLinesCommand";

            //// Show a message box to prove we were here
            //VsShellUtilities.ShowMessageBox(
            //    this.package,
            //    message,
            //    title,
            //    OLEMSGICON.OLEMSGICON_INFO,
            //    OLEMSGBUTTON.OLEMSGBUTTON_OK,
            //    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            OpenWebBrowserAndNavigateGitHubPage();
        }

        private async Task OpenWebBrowserAndNavigateGitHubPage()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var serviceProvider = ServiceProvider;
            var textManager = await serviceProvider.GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
            IVsTextLines textLines;
            int startLine, endLine, startIndex, endIndex;
            textManager.GetActiveView(1, null, out IVsTextView textView);
            textView.GetSelection(out startLine, out startIndex, out endLine, out endIndex);
            textView.GetBuffer(out textLines);

            DTE dte = await ServiceProvider.GetServiceAsync(typeof(DTE)) as DTE;
            var document = dte.ActiveDocument;
            var projectItem = document.ProjectItem;
            string filePath = null;
            if (projectItem == null)
            {
                filePath = document.FullName;
            }
            if (projectItem != null)
            {
                filePath = projectItem.Properties.Item("FullPath").Value.ToString();
            }

            //ファイルパスを取得できた場合の処理
            var solution = await serviceProvider.GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
            if (solution != null && filePath != null)
            {
                var repoPath = await GetGitRepositoryUrl(filePath, solution, projectItem);
                var projectPath = projectItem.ContainingProject.FullName;
                string projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath);
                string projectNameWithExt = Path.GetFileName(projectPath);
                var path = projectPath.Remove(projectPath.IndexOf(projectNameWithExt), projectNameWithExt.Length);
                path = path.Remove(path.IndexOf(repoPath), repoPath.Length);
                path = path.Replace('\\', '/');
                path = path.Trim('/');

                var webBrowsingService = await serviceProvider.GetServiceAsync(typeof(SVsWebBrowsingService)) as IVsWebBrowsingService;

                var gitRepository = new Repository(repoPath);
                var repositoryUrl = gitRepository.Network.Remotes.FirstOrDefault()?.Url;
                var baseUrl = repositoryUrl?.Replace(".git", string.Empty)?.Replace("ssh://", "https://").Replace("git://", "https://").Replace("git@", "https://").Replace("github.com:", "github.com/");
                var branchName =  Uri.EscapeDataString(gitRepository.Head.FriendlyName);
                var relativeFilePath = filePath?.Substring(Path.GetDirectoryName(projectPath).Length);
                relativeFilePath = relativeFilePath?.Replace('\\', '/');
                var lineNumberBegin = startLine + 1;
                var lineNumberEnd = endLine + 1;
                var fileUrl = $"{baseUrl}/blob/{branchName}/{path}{relativeFilePath}#L{lineNumberBegin}";
                if (lineNumberBegin != lineNumberEnd)
                {
                    fileUrl += $"-L{lineNumberEnd}";
                }
                System.Diagnostics.Process.Start(fileUrl);
            }
        }

        private async Task<string> GetGitRepositoryUrl(string filePath, IVsSolution solution, ProjectItem projectItem)
        {
            // Get an instance of the IGitExt object
            IGitExt gitService = await ServiceProvider.GetServiceAsync(typeof(IGitExt)) as IGitExt;

            // Get the active repository object
            IGitRepositoryInfo repositoryInfo = gitService.ActiveRepositories.FirstOrDefault();

            return repositoryInfo.RepositoryPath;
        }
    }
}
