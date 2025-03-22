using System.Collections.Generic;
using System.Windows;

namespace boilersExtensions.Dialogs
{
    /// <summary>
    /// 固定値編集ダイアログ
    /// </summary>
    public partial class FixedValuesDialog : Window
    {
        public string PropertyName { get; set; }
        public string FixedValuesText { get; set; }
        public List<string> FixedValues { get; private set; }

        public FixedValuesDialog()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        /// <summary>
        /// 固定値を設定
        /// </summary>
        public void SetFixedValues(string propertyName, IEnumerable<string> values)
        {
            PropertyName = propertyName;
            FixedValuesText = string.Join("\r\n", values);
        }

        /// <summary>
        /// OKボタン押下時の処理
        /// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // 空行を除去して固定値リストを作成
            FixedValues = new List<string>();
            if (!string.IsNullOrEmpty(FixedValuesText))
            {
                string[] lines = FixedValuesText.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        FixedValues.Add(trimmedLine);
                    }
                }
            }

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// キャンセルボタン押下時の処理
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}