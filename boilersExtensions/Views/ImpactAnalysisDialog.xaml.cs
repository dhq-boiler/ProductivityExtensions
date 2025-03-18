using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using boilersExtensions.Utils;

namespace boilersExtensions.Views
{
    /// <summary>
    ///     ImpactAnalysisDialog.xaml の相互作用ロジック
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

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            if (dataGrid != null && dataGrid.SelectedItem is TypeReferenceInfo selectedReference)
            {
                if (DataContext is ImpactAnalysisViewModel viewModel)
                {
                    viewModel.NavigateToReferenceCommand.Execute(selectedReference);
                }
            }
        }

        private void BookmarkCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is TypeReferenceInfo reference)
            {
                if (DataContext is ImpactAnalysisViewModel viewModel)
                {
                    // イベントが2回発生するのを防ぐためにルーティングされたイベントを処理済みとしてマーク
                    e.Handled = true;

                    // ブックマークトグルコマンドを実行
                    viewModel.ToggleBookmarkCommand.Execute(reference);
                }
            }
        }
    }
}