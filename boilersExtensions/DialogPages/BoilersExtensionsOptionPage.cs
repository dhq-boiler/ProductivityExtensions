using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace boilersExtensions.DialogPages
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    public class BoilersExtensionsOptionPage : DialogPage
    {
        private bool _enableTypeHierarchy = true;
        private bool _enableRegionNavigator = true;
        private bool _enableSyncToSolutionExplorer = true;
        private bool _enableNavigateGitHubLines = true;
        private bool _enableRenameProject = true;
        private bool _enableRenameSolution = true;
        private bool _enableUpdateGuid = true;
        private bool _enableBatchUpdateGuid = true;

        [Category("機能の有効/無効")]
        [DisplayName("型階層を有効にする")]
        [Description("Ctrl+クリックで型階層から型を選択できる機能を有効にします。")]
        public bool EnableTypeHierarchy
        {
            get { return _enableTypeHierarchy; }
            set { _enableTypeHierarchy = value; }
        }

        [Category("機能の有効/無効")]
        [DisplayName("リージョンナビゲータを有効にする")]
        [Description("Ctrl+F2でリージョン間を移動できる機能を有効にします。")]
        public bool EnableRegionNavigator
        {
            get { return _enableRegionNavigator; }
            set { _enableRegionNavigator = value; }
        }

        [Category("機能の有効/無効")]
        [DisplayName("ソリューションエクスプローラー同期を有効にする")]
        [Description("現在のファイルをソリューションエクスプローラーで表示する機能を有効にします。")]
        public bool EnableSyncToSolutionExplorer
        {
            get { return _enableSyncToSolutionExplorer; }
            set { _enableSyncToSolutionExplorer = value; }
        }

        [Category("機能の有効/無効")]
        [DisplayName("GitHubリンクナビゲーションを有効にする")]
        [Description("GitHubホスティングリポジトリの該当行を開く機能を有効にします。")]
        public bool EnableNavigateGitHubLines
        {
            get { return _enableNavigateGitHubLines; }
            set { _enableNavigateGitHubLines = value; }
        }

        [Category("機能の有効/無効")]
        [DisplayName("プロジェクトリネームを有効にする")]
        [Description("プロジェクトをリネームする機能を有効にします。")]
        public bool EnableRenameProject
        {
            get { return _enableRenameProject; }
            set { _enableRenameProject = value; }
        }

        [Category("機能の有効/無効")]
        [DisplayName("ソリューションリネームを有効にする")]
        [Description("ソリューションをリネームする機能を有効にします。")]
        public bool EnableRenameSolution
        {
            get { return _enableRenameSolution; }
            set { _enableRenameSolution = value; }
        }

        [Category("機能の有効/無効")]
        [DisplayName("GUID更新を有効にする")]
        [Description("選択したGUID文字列を更新する機能を有効にします。")]
        public bool EnableUpdateGuid
        {
            get { return _enableUpdateGuid; }
            set { _enableUpdateGuid = value; }
        }

        [Category("機能の有効/無効")]
        [DisplayName("GUID一括更新を有効にする")]
        [Description("GUIDを一括更新する機能を有効にします。")]
        public bool EnableBatchUpdateGuid
        {
            get { return _enableBatchUpdateGuid; }
            set { _enableBatchUpdateGuid = value; }
        }
    }
}
