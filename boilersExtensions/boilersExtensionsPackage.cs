using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using boilersExtensions.Commands;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace boilersExtensions
{
    /// <summary>
    ///     This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The minimum requirement for a class to be considered a valid package for Visual Studio
    ///         is to implement the IVsPackage interface and register itself with the shell.
    ///         This package uses the helper classes defined inside the Managed Package Framework (MPF)
    ///         to do it: it derives from the Package class that provides the implementation of the
    ///         IVsPackage interface and uses the registration attributes defined in the framework to
    ///         register itself and its components with the shell. These attributes tell the pkgdef creation
    ///         utility what data to put into .pkgdef file.
    ///     </para>
    ///     <para>
    ///         To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...
    ///         &gt; in .vsixmanifest file.
    ///     </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [InstalledProductRegistration("#110", "#112", "1.0")]
    public sealed class boilersExtensionsPackage : AsyncPackage
    {
        public const string PackageGuidString = "e26b6f0b-d63a-4590-bd2f-8b201c2413dc";

        protected override async Task InitializeAsync(CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            // UI threadに切り替え
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // 他の初期化処理
            await NavigateGitHubLinesCommand.InitializeAsync(this);
            await RenameProjectCommand.InitializeAsync(this);
            await RenameSolutionCommand.InitializeAsync(this);
            await UpdateGuidCommand.InitializeAsync(this);
            await BatchUpdateGuidCommand.InitializeAsync(this);
            await TypeHierarchyCommand.InitializeAsync(this);
            await RegionNavigatorCommand.InitializeAsync(this);

            // 手動で拡張機能を初期化
            Debug.WriteLine("Initializing RegionNavigator extensions manually");
            ManualExtensionInitializer.Initialize(this);
            Debug.WriteLine("Manual initialization completed");
        }
    }
}