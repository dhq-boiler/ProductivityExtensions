using System.Windows;
using boilersExtensions.Utils;

namespace boilersExtensions.Views
{
    /// <summary>
    /// ImpactAnalysisDialog.xaml の相互作用ロジック
    /// </summary>
    public partial class ImpactAnalysisDialog : Window
    {
        public ImpactAnalysisDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ImpactAnalysisViewModel viewModel)
            {
                viewModel.OnDialogOpened(this);
            }
        }
    }
}
