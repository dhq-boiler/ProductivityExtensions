using System.Windows;
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
                    MessageBox.Show("Resource key is required.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // If using custom class, validate class name
                if (viewModel.UseCustomResourceClass.Value &&
                    string.IsNullOrWhiteSpace(viewModel.ResourceClassName.Value))
                {
                    MessageBox.Show("Resource class name is required when using a custom class.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            DialogResult = true;
            Close();
        }
    }
}
