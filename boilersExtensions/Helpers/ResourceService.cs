using System;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Threading;
using boilersExtensions.Properties;
using Microsoft.VisualStudio.Shell;
using Prism.Mvvm;

namespace boilersExtensions.Helpers
{
    /// <summary>
    ///     https://qiita.com/YSRKEN/items/a96bcec8dfb0a8340a5f
    /// </summary>
    public class ResourceService : BindableBase
    {
        private static ResourceManager resourceMan;
        public static ResourceService Current { get; } = new ResourceService();

        public Resource Resource { get; } = new Resource();

        /// <summary>
        ///     リソースのカルチャーを変更
        /// </summary>
        /// <param name="name">カルチャー名</param>
        public void ChangeCulture(string name)
        {
            Resource.Culture = CultureInfo.GetCultureInfo(name);
            RaisePropertyChanged(nameof(Resource));
        }

        /// <summary>
        ///     現在のカルチャを初期化
        /// </summary>
        public static void InitializeCurrentCulture()
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // 設定から言語を取得
                    var languageSetting = "en-US";

                    // パッケージがロードされた後に設定を取得
                    if (BoilersExtensionsSettingsCommand.Instance?.Package != null)
                    {
                        languageSetting = BoilersExtensionsSettings.Language;
                    }

                    // 対応する CultureInfo を作成
                    CultureInfo culture;
                    try
                    {
                        culture = new CultureInfo(languageSetting);
                    }
                    catch (Exception)
                    {
                        // 無効な言語コードの場合はデフォルトに戻す
                        culture = new CultureInfo("en-US");
                    }

                    // カルチャを設定
                    Thread.CurrentThread.CurrentUICulture = culture;
                    Resource.Culture = culture;
                    Debug.WriteLine($"Culture set to {culture.Name}");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing culture: {ex.Message}");
                // デフォルトカルチャーをフォールバックとして使用
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            }
        }

        public static void ReloadResources()
        {
            try
            {
                // Re-initialize the ResourceManager to pick up new resources
                resourceMan = new ResourceManager(typeof(Resource));

                // Apply current culture
                InitializeCurrentCulture();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reloading resources: {ex.Message}");
            }
        }

        internal static string GetString(string name)
        {
            if (resourceMan == null)
            {
                // 正しいリソース名とアセンブリを指定
                resourceMan = new ResourceManager("boilersExtensions.Properties.Resource", typeof(Resource).Assembly);
                Debug.WriteLine($"ResourceManager initialized with {resourceMan.BaseName}");
            }

            try
            {
                // 再試行メカニズムを追加
                string result = null;
                var retryCount = 0;
                while (result == null && retryCount < 3)
                {
                    result = resourceMan.GetString(name, Resource.Culture);
                    if (result == null)
                    {
                        retryCount++;
                        // 短い待機を入れる
                        Thread.Sleep(50);
                    }
                }

                Debug.WriteLine($"GetString({name}) -> {result}");
                return result ?? name;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting resource: {ex.Message}");
                return name;
            }
        }

        /// <summary>
        ///     現在の言語設定を再適用します
        /// </summary>
        public static void ApplyLanguageSetting()
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // 設定から言語を取得
                    var languageSetting = BoilersExtensionsSettings.Language;

                    // 対応する CultureInfo を作成
                    CultureInfo culture;
                    try
                    {
                        culture = new CultureInfo(languageSetting);
                    }
                    catch (Exception)
                    {
                        // 無効な言語コードの場合はデフォルトに戻す
                        culture = new CultureInfo("en-US");
                    }

                    // カルチャを設定
                    Thread.CurrentThread.CurrentUICulture = culture;
                    Resource.Culture = culture;

                    Debug.WriteLine($"Culture updated to {culture.Name}");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying language setting: {ex.Message}");
            }
        }
    }
}