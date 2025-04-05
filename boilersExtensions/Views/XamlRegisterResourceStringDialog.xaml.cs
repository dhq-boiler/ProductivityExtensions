using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using boilersExtensions.Helpers;
using boilersExtensions.ViewModels;

namespace boilersExtensions.Views
{
    /// <summary>
    /// XamlRegisterResourceStringDialog.xaml の相互作用ロジック
    /// </summary>
    public partial class XamlRegisterResourceStringDialog : Window
    {
        private XamlRegisterResourceStringDialogViewModel ViewModel => DataContext as XamlRegisterResourceStringDialogViewModel;

        public XamlRegisterResourceStringDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// ダイアログがロードされた時のイベントハンドラ
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // ViewModelを初期化
            if (ViewModel != null)
            {
                ViewModel.Initialize();
            }
        }

        /// <summary>
        /// 変換ボタンがクリックされた時のイベントハンドラ
        /// </summary>
        private void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                // バリデーション処理
                if (string.IsNullOrWhiteSpace(ViewModel.ResourceNamespace.Value))
                {
                    MessageBox.Show(ResourceService.GetString("ResourceNamespaceRequired"),
                                   ResourceService.GetString("ValidationError"),
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(ViewModel.ResourceClass.Value))
                {
                    MessageBox.Show(ResourceService.GetString("ResourceClassRequired"),
                                   ResourceService.GetString("ValidationError"),
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 選択されたアイテムがない場合の確認
                if (!ViewModel.DetectedTextItems.Any(item => item.IsSelected))
                {
                    var result = MessageBox.Show(ResourceService.GetString("NoItemsSelectedForConversion"),
                                              ResourceService.GetString("Confirmation"),
                                              MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result != MessageBoxResult.Yes)
                        return;
                }

                // リソース登録と変換処理を実行
                bool success = ViewModel.ConvertAndRegisterResources();

                if (success)
                {
                    // 成功した場合はダイアログを閉じる
                    DialogResult = true;
                    Close();
                }
                else
                {
                    // エラーがあった場合はメッセージを表示
                    MessageBox.Show(ResourceService.GetString("ErrorConvertingResources"),
                                   ResourceService.GetString("Error"),
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// すべて選択ボタンがクリックされた時のイベントハンドラ
        /// </summary>
        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                foreach (var item in ViewModel.DetectedTextItems)
                {
                    item.IsSelected = true;
                }

                // DataGridを更新
                itemsDataGrid.Items.Refresh();

                // プレビューを更新
                ViewModel.UpdatePreview();
            }
        }

        /// <summary>
        /// すべて選択解除ボタンがクリックされた時のイベントハンドラ
        /// </summary>
        private void UnselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                foreach (var item in ViewModel.DetectedTextItems)
                {
                    item.IsSelected = false;
                }

                // DataGridを更新
                itemsDataGrid.Items.Refresh();

                // プレビューを更新
                ViewModel.UpdatePreview();
            }
        }

        /// <summary>
        /// リソースキーが変更された時のイベントハンドラ
        /// </summary>
        private void ResourceKeyTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.UpdatePreview();
            }
        }

        /// <summary>
        /// チェックボックスの状態が変更された時のイベントハンドラ
        /// </summary>
        private void CheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.UpdatePreview();
            }
        }

        /// <summary>
        /// Enterキーを押した時のイベントハンドラ
        /// </summary>
        private void ResourceKeyTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (ViewModel != null)
                {
                    ViewModel.UpdatePreview();
                }
            }
        }
    }
}