using System.Windows;
using boilersExtensions.ViewModels;

namespace boilersExtensions.Dialogs
{
    /// <summary>
    /// SeedDataConfigDialog.xaml の相互作用ロジック
    /// </summary>
    public partial class SeedDataConfigDialog : Window
    {
        public SeedDataConfigDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// ダイアログが閉じる時の処理
        /// </summary>
        private void SeedDataConfigDialog_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is SeedDataConfigViewModel viewModel)
            {
                viewModel.OnDialogClosing(this);
            }
        }
    }
}