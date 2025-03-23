using System;
using System.Collections.Generic;
using System.Windows;

namespace boilersExtensions.Dialogs
{
    /// <summary>
    ///     固定値編集ダイアログ
    /// </summary>
    public partial class FixedValuesDialog : Window
    {
        public FixedValuesDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        public string PropertyName { get; set; }
        public string FixedValuesText { get; set; }
        public List<string> FixedValues { get; private set; }

        /// <summary>
        ///     固定値を設定
        /// </summary>
        public void SetFixedValues(string propertyName, IEnumerable<string> values)
        {
            PropertyName = propertyName;
            FixedValuesText = string.Join("\r\n", values);
        }

        /// <summary>
        ///     OKボタン押下時の処理
        /// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // 空行を除去して固定値リストを作成
            FixedValues = new List<string>();
            if (!string.IsNullOrEmpty(FixedValuesText))
            {
                var lines = FixedValuesText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
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
        ///     キャンセルボタン押下時の処理
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}