using System.ComponentModel;
using System.Runtime.InteropServices;
using boilersExtensions.Helpers.Attributes;
using Microsoft.VisualStudio.Shell;

namespace boilersExtensions.DialogPages
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    public class BoilersExtensionsOptionPage : DialogPage
    {
        [LocalizedCategory("BoilersExtensionsOptionPage_EnableDisableFunction")]
        [LocalizedDisplayName("BoilersExtensionsOptionPage_EnableTypeHierarchy")]
        [LocalizedDescription("BoilersExtensionsOptionPage_EnableTypeHierarchy_Description")]
        public bool EnableTypeHierarchy { get; set; } = true;

        [LocalizedCategory("BoilersExtensionsOptionPage_EnableDisableFunction")]
        [LocalizedDisplayName("BoilersExtensionsOptionPage_EnableRegionNavigator")]
        [LocalizedDescription("BoilersExtensionsOptionPage_EnableRegionNavigator_Description")]
        public bool EnableRegionNavigator { get; set; } = true;

        [LocalizedCategory("BoilersExtensionsOptionPage_EnableDisableFunction")]
        [LocalizedDisplayName("BoilersExtensionsOptionPage_EnableSolutionExplorerSynchronization")]
        [LocalizedDescription("BoilersExtensionsOptionPage_EnableSolutionExplorerSynchronization_Description")]
        public bool EnableSyncToSolutionExplorer { get; set; } = true;

        [LocalizedCategory("BoilersExtensionsOptionPage_EnableDisableFunction")]
        [LocalizedDisplayName("BoilersExtensionsOptionPage_EnableGitHubLinkNavigation")]
        [LocalizedDescription("BoilersExtensionsOptionPage_EnableGitHubLinkNavigation_Description")]
        public bool EnableNavigateGitHubLines { get; set; } = true;

        [LocalizedCategory("BoilersExtensionsOptionPage_EnableDisableFunction")]
        [LocalizedDisplayName("BoilersExtensionsOptionPage_EnableProjectRename")]
        [LocalizedDescription("BoilersExtensionsOptionPage_EnableProjectRename_Description")]
        public bool EnableRenameProject { get; set; } = true;

        [LocalizedCategory("BoilersExtensionsOptionPage_EnableDisableFunction")]
        [LocalizedDisplayName("BoilersExtensionsOptionPage_EnableSolutionRename")]
        [LocalizedDescription("BoilersExtensionsOptionPage_EnableSolutionRename_Description")]
        public bool EnableRenameSolution { get; set; } = true;

        [LocalizedCategory("BoilersExtensionsOptionPage_EnableDisableFunction")]
        [LocalizedDisplayName("BoilersExtensionsOptionPage_EnableGUIDUpdate")]
        [LocalizedDescription("BoilersExtensionsOptionPage_EnableGUIDUpdate_Description")]
        public bool EnableUpdateGuid { get; set; } = true;

        [LocalizedCategory("BoilersExtensionsOptionPage_EnableDisableFunction")]
        [LocalizedDisplayName("BoilersExtensionsOptionPage_EnableGUIDBatchUpdate")]
        [LocalizedDescription("BoilersExtensionsOptionPage_EnableGUIDBatchUpdate_Description")]
        public bool EnableBatchUpdateGuid { get; set; } = true;

        [LocalizedCategory("BoilersExtensionsOptionPage_EnableDisableFunction")]
        [LocalizedDisplayName("BoilersExtensionsOptionPage_EnableSeedGeneratorForEFCore")]
        [LocalizedDescription("BoilersExtensionsOptionPage_EnableSeedGeneratorForEFCore_Description")]
        public bool EnableSeedDataGenerator { get; set; } = true;
    }
}