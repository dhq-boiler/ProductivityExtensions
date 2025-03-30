using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using boilersExtensions.DialogPages;
using boilersExtensions.Helpers;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace boilersExtensions.Utils
{
    /// <summary>
    ///     言語変更のためのユーティリティクラス
    /// </summary>
    public static class LanguageChangeManager
    {
        private static AsyncPackage _package;

        /// <summary>
        ///     言語変更マネージャーを初期化し、言語変更イベントを購読します
        /// </summary>
        public static void Initialize(AsyncPackage package)
        {
            _package = package;

            // 言語変更イベントを購読
            BoilersExtensionsOptionPage.LanguageChanged += OnLanguageChanged;
        }

        /// <summary>
        ///     言語変更イベントのハンドラー
        /// </summary>
        private static void OnLanguageChanged(object sender, LanguageChangedEventArgs args)
        {
            Debug.WriteLine($"Language changed from {args.OldLanguage} to {args.NewLanguage}");

            // 言語設定を適用
            ApplyLanguageChange();

            // 通知をユーザーに表示
            if (_package != null)
            {
                ShowLanguageChangeNotification(_package);
            }
        }

        /// <summary>
        ///     言語設定を適用し、必要なリソースをリロードします
        /// </summary>
        public static void ApplyLanguageChange()
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // 現在のカルチャと設定されたカルチャを比較
                    var currentLanguage = Thread.CurrentThread.CurrentUICulture.Name;
                    var settingLanguage = BoilersExtensionsSettings.Language;

                    // 言語設定が異なる場合のみ処理
                    if (currentLanguage != settingLanguage)
                    {
                        try
                        {
                            // 新しいカルチャを設定
                            var newCulture = new CultureInfo(settingLanguage);
                            Thread.CurrentThread.CurrentUICulture = newCulture;

                            // リソースサービスに通知して再ロード
                            ResourceService.ApplyLanguageSetting();

                            // Update all command texts
                            MenuTextUpdater.UpdateAllCommandTexts();

                            Debug.WriteLine($"Language changed from {currentLanguage} to {settingLanguage}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error setting culture: {ex.Message}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ApplyLanguageChange: {ex.Message}");
            }
        }

        /// <summary>
        ///     言語設定変更後に通知を表示
        /// </summary>
        public static void ShowLanguageChangeNotification(AsyncPackage package)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // 現在の言語に基づいてメッセージを選択
                    var settingLanguage = BoilersExtensionsSettings.Language;
                    var message = settingLanguage == "ja-JP"
                        ? "言語設定が変更されました。完全に適用するには Visual Studio を再起動してください。"
                        : "Language setting has been changed. Please restart Visual Studio for full effect.";

                    VsShellUtilities.ShowMessageBox(
                        package,
                        message,
                        string.Empty,
                        OLEMSGICON.OLEMSGICON_INFO,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing language change notification: {ex.Message}");
            }
        }
    }
}