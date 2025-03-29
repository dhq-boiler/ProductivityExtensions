using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using EnvDTE;
using LibGit2Sharp;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TeamFoundation.Git.Extensibility;
using Microsoft.VisualStudio.TextManager.Interop;
using ZLinq;
using Process = System.Diagnostics.Process;
using Task = System.Threading.Tasks.Task;

namespace boilersExtensions
{
    /// <summary>
    ///     Command handler
    /// </summary>
    internal sealed class NavigateGitHubLinesCommand : OleMenuCommand
    {
        /// <summary>
        ///     Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        ///     Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("b19148c9-0670-418f-bce5-1845978d4302");

        /// <summary>
        ///     VS Package that provides this command, not null.
        /// </summary>
        private static AsyncPackage package;

        private static OleMenuCommand menuItem;

        /// <summary>
        ///     Initializes a new instance of the <see cref="NavigateGitHubLinesCommand" /> class.
        ///     Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner Package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private NavigateGitHubLinesCommand() : base(Execute, new CommandID(CommandSet, CommandId)) =>
            base.BeforeQueryStatus += BeforeQueryStatus;

        /// <summary>
        ///     Gets the instance of the command.
        /// </summary>
        public static NavigateGitHubLinesCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        ///     Gets the service provider from the owner Package.
        /// </summary>
        private static IAsyncServiceProvider ServiceProvider => package;

        private static async void BeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand command)
            {
                // 設定で無効化されているかチェック
                var featureEnabled = BoilersExtensionsSettings.IsNavigateGitHubLinesEnabled;

                if (!featureEnabled)
                {
                    // 機能が無効の場合はメニュー項目を非表示にする
                    command.Visible = false;
                    command.Enabled = false;
                    return;
                }

                // 機能が有効な場合は通常の条件で表示/非表示を決定
                command.Visible = command.Enabled = !string.IsNullOrEmpty(await GetGitRepositoryUrl());
            }
        }

        /// <summary>
        ///     Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner Package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            NavigateGitHubLinesCommand.package = package;

            // Switch to the main thread - the call to AddCommand in NavigateGitHubLinesCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            menuItem = Instance = new NavigateGitHubLinesCommand();
            menuItem.Text = Resources.ResourceService.GetString("OpenGitHubLine");
            commandService.AddCommand(Instance);
        }

        /// <summary>
        ///     This function is the callback used to execute the command when the menu item is clicked.
        ///     See the constructor to see how the menu item is associated with this function using
        ///     OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 設定が無効な場合は何もしない
            if (!BoilersExtensionsSettings.IsNavigateGitHubLinesEnabled)
            {
                Debug.WriteLine("NavigateGitHubLines feature is disabled in settings");
                return;
            }

            //string message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.MenuItemCallback()", this.GetType().FullName);
            //string title = "NavigateGitHubLinesCommand";

            //// Show a message box to prove we were here
            //VsShellUtilities.ShowMessageBox(
            //    this.Package,
            //    message,
            //    title,
            //    OLEMSGICON.OLEMSGICON_INFO,
            //    OLEMSGBUTTON.OLEMSGBUTTON_OK,
            //    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            OpenWebBrowserAndNavigateGitHubPage();
        }

        private static async Task OpenWebBrowserAndNavigateGitHubPage()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var serviceProvider = ServiceProvider;
            var textManager = await serviceProvider.GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
            textManager.GetActiveView(1, null, out var textView);
            textView.GetSelection(out var startLine, out _, out var endLine, out _);
            textView.GetBuffer(out _);

            var dte = await ServiceProvider.GetServiceAsync(typeof(DTE)) as DTE;
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
            if (await serviceProvider.GetServiceAsync(typeof(SVsSolution)) is IVsSolution solution && filePath != null)
            {
                var repoPath = await GetGitRepositoryUrl();

                if (repoPath == null)
                {
                    menuItem.Enabled = false;
                    return;
                }

                var projectPath = projectItem.ContainingProject.FullName;
                var projectNameWithExt = Path.GetFileName(projectPath);
                var path = projectPath.Remove(projectPath.IndexOf(projectNameWithExt), projectNameWithExt.Length);
                path = path.Remove(path.IndexOf(repoPath), repoPath.Length);
                path = path.Replace('\\', '/');
                path = path.Trim('/');

                var gitRepository = new Repository(repoPath);
                var repositoryUrl = gitRepository.Network.Remotes.AsValueEnumerable().FirstOrDefault()?.Url;
                var baseUrl = repositoryUrl?.Replace(".git", string.Empty)?.Replace("ssh://", "https://")
                    .Replace("git://", "https://")
                    .Replace("git@", "https://")
                    .Replace("github.com:", "github.com/");
                var branchName = Uri.EscapeDataString(gitRepository.Head.FriendlyName);
                var relativeFilePath = filePath?.Substring(Path.GetDirectoryName(projectPath).Length);
                relativeFilePath = relativeFilePath?.Replace('\\', '/');
                var lineNumberBegin = startLine + 1;
                var lineNumberEnd = endLine + 1;
                var fileUrl = $"{baseUrl}/blob/{branchName}/{path}{relativeFilePath}#L{lineNumberBegin}";
                if (lineNumberBegin != lineNumberEnd)
                {
                    fileUrl += $"-L{lineNumberEnd}";
                }

                Process.Start(fileUrl);
            }
        }

        private static async Task<string> GetGitRepositoryUrl()
        {
            var repositoryInfo = await GetGitRepositoryInfo();

            return repositoryInfo?.RepositoryPath;
        }

        private static async Task<IGitRepositoryInfo> GetGitRepositoryInfo()
        {
            // Get an instance of the IGitExt object
            var gitService = await ServiceProvider.GetServiceAsync(typeof(IGitExt)) as IGitExt;

            // Get the active repository object
            return gitService.ActiveRepositories.AsValueEnumerable().FirstOrDefault();
        }
    }
}