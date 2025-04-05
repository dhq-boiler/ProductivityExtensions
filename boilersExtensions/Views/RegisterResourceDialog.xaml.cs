using System.Windows;
using boilersExtensions.Helpers;
using boilersExtensions.ViewModels;

namespace boilersExtensions.Views
{
    /// <summary>
    /// RegisterResourceDialog.xaml の相互作用ロジック
    /// </summary>
    public partial class RegisterResourceDialog : Window
    {
        public RegisterResourceDialog()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is RegisterResourceDialogViewModel viewModel)
            {
                // Validate required fields
                if (string.IsNullOrWhiteSpace(viewModel.ResourceKey.Value))
                {
                    MessageBox.Show(ResourceService.GetString("ResourceKeyIsRequired"), ResourceService.GetString("ValidationError"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // If using custom class, validate class name
                if (viewModel.UseCustomResourceClass.Value &&
                    string.IsNullOrWhiteSpace(viewModel.ResourceClassName.Value))
                {
                    MessageBox.Show(ResourceService.GetString("NeedResourceClassName"),
                        ResourceService.GetString("ValidationError"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            DialogResult = true;
            Close();
        }
    }
}
