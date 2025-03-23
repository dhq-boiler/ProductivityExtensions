using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace boilersExtensions.DialogPages
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    public class BoilersExtensionsOptionPage : DialogPage
    {
        [Category("機能の有効/無効")]
        [DisplayName("型階層を有効にする")]
        [Description("Ctrl+クリックで型階層から型を選択できる機能を有効にします。")]
        public bool EnableTypeHierarchy { get; set; } = true;

        [Category("機能の有効/無効")]
        [DisplayName("リージョンナビゲータを有効にする")]
        [Description("Ctrl+F2でリージョン間を移動できる機能を有効にします。")]
        public bool EnableRegionNavigator { get; set; } = true;

        [Category("機能の有効/無効")]
        [DisplayName("ソリューションエクスプローラー同期を有効にする")]
        [Description("現在のファイルをソリューションエクスプローラーで表示する機能を有効にします。")]
        public bool EnableSyncToSolutionExplorer { get; set; } = true;

        [Category("機能の有効/無効")]
        [DisplayName("GitHubリンクナビゲーションを有効にする")]
        [Description("GitHubホスティングリポジトリの該当行を開く機能を有効にします。")]
        public bool EnableNavigateGitHubLines { get; set; } = true;

        [Category("機能の有効/無効")]
        [DisplayName("プロジェクトリネームを有効にする")]
        [Description("プロジェクトをリネームする機能を有効にします。")]
        public bool EnableRenameProject { get; set; } = true;

        [Category("機能の有効/無効")]
        [DisplayName("ソリューションリネームを有効にする")]
        [Description("ソリューションをリネームする機能を有効にします。")]
        public bool EnableRenameSolution { get; set; } = true;

        [Category("機能の有効/無効")]
        [DisplayName("GUID更新を有効にする")]
        [Description("選択したGUID文字列を更新する機能を有効にします。")]
        public bool EnableUpdateGuid { get; set; } = true;

        [Category("機能の有効/無効")]
        [DisplayName("GUID一括更新を有効にする")]
        [Description("GUIDを一括更新する機能を有効にします。")]
        public bool EnableBatchUpdateGuid { get; set; } = true;

        [Category("機能の有効/無効")]
        [DisplayName("Seedデータ生成を有効にする")]
        [Description("Seedデータ生成機能を有効にします。")]
        public bool EnableSeedDataGenerator { get; set; } = true;
    }
}