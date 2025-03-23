using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using boilersExtensions.ViewModels;

namespace boilersExtensions.Dialogs
{
    /// <summary>
    ///     SeedDataConfigDialog.xaml の相互作用ロジック
    /// </summary>
    public partial class SeedDataConfigDialog : Window
    {
        public SeedDataConfigDialog() => InitializeComponent();

        /// <summary>
        ///     ダイアログが閉じる時の処理
        /// </summary>
        private void SeedDataConfigDialog_Closing(object sender, CancelEventArgs e)
        {
            if (DataContext is SeedDataConfigViewModel viewModel)
            {
                viewModel.OnDialogClosing(this);
            }
        }

        /// <summary>
        ///     固定値編集ボタンが押された時の処理
        /// </summary>
        private void EditFixedValuesButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is SeedDataConfigViewModel viewModel && viewModel.SelectedEntity.Value != null)
            {
                // クリックされたボタンからプロパティ名を取得
                var button = sender as Button;
                if (button == null)
                {
                    return;
                }

                // DataContextからプロパティ情報を取得
                var property = button.DataContext as PropertyViewModel;
                if (property == null)
                {
                    return;
                }

                var propertyName = property.Name.Value;

                // プロパティ設定を取得
                var entityConfig = viewModel.SelectedEntity.Value;
                var propConfig = entityConfig.GetPropertyConfig(propertyName);

                if (propConfig == null)
                {
                    MessageBox.Show("プロパティ設定の取得に失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 固定値編集ダイアログを表示
                var dialog = new FixedValuesDialog();
                dialog.SetFixedValues(propertyName, propConfig.FixedValues);
                dialog.Owner = this;

                if (dialog.ShowDialog() == true)
                {
                    // OKが押された場合、固定値を更新
                    propConfig.FixedValues.Clear();
                    foreach (var value in dialog.FixedValues)
                    {
                        propConfig.FixedValues.Add(value);
                    }

                    // データ件数を更新
                    viewModel.CalculateAndUpdateTotalRecordCount();
                }
            }
        }
    }
}