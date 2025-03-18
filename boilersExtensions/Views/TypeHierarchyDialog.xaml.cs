using System.ComponentModel;
using System.Windows;
using boilersExtensions.ViewModels;

namespace boilersExtensions.Views
{
    /// <summary>
    ///     TypeHierarchyDialog.xaml の相互作用ロジック
    /// </summary>
    public partial class TypeHierarchyDialog : Window
    {
        public TypeHierarchyDialog() => InitializeComponent();

        private void TypeHierarchyDialog_OnClosing(object sender, CancelEventArgs e)
        {
            if (DataContext is TypeHierarchyDialogViewModel viewModel)
            {
                viewModel.OnDialogClosing(this);
            }
        }
    }
}