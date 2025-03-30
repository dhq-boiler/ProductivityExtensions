using System;

namespace boilersExtensions.DialogPages
{
    /// <summary>
    /// 言語変更イベントの引数クラス
    /// </summary>
    public class LanguageChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 変更前の言語コード
        /// </summary>
        public string OldLanguage { get; }

        /// <summary>
        /// 変更後の言語コード
        /// </summary>
        public string NewLanguage { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="oldLanguage">変更前の言語コード</param>
        /// <param name="newLanguage">変更後の言語コード</param>
        public LanguageChangedEventArgs(string oldLanguage, string newLanguage)
        {
            OldLanguage = oldLanguage;
            NewLanguage = newLanguage;
        }
    }
}