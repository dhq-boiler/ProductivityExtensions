using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace boilersExtensions.DialogPages
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    public class BoilersExtensionsOptionPage : DialogPage
    {
        private bool _enableTypeHierarchy = true;
        private bool _enableRegionNavigation = true;
        private bool _enableSolutionExplorerSync = true;

        [Category("一般設定")]
        [DisplayName("型階層選択を有効化")]
        [Description("型階層選択機能を有効または無効にします。")]
        public bool EnableTypeHierarchy
        {
            get => _enableTypeHierarchy;
            set => _enableTypeHierarchy = value;
        }

        [Category("一般設定")]
        [DisplayName("リージョンナビゲーションを有効化")]
        [Description("リージョン間の移動機能を有効または無効にします。")]
        public bool EnableRegionNavigation
        {
            get => _enableRegionNavigation;
            set => _enableRegionNavigation = value;
        }

        [Category("一般設定")]
        [DisplayName("ソリューションエクスプローラー同期を有効化")]
        [Description("ソリューションエクスプローラーとの同期機能を有効または無効にします。")]
        public bool EnableSolutionExplorerSync
        {
            get => _enableSolutionExplorerSync;
            set => _enableSolutionExplorerSync = value;
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            // 設定が変更された際の追加処理（必要に応じて）
            base.OnApply(e);
        }
    }
}
