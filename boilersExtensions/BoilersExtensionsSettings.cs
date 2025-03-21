using System;
using System.Diagnostics;
using System.Threading.Tasks;
using boilersExtensions.DialogPages;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace boilersExtensions
{
    /// <summary>
    ///     boilersExtensionsの設定を管理するユーティリティクラス
    /// </summary>
    public static class BoilersExtensionsSettings
    {
        /// <summary>
        ///     型階層機能が有効かどうか
        /// </summary>
        public static bool IsTypeHierarchyEnabled
        {
            get
            {
                try
                {
                    return GetOptionPageProperty("EnableTypeHierarchy", true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading TypeHierarchy setting: {ex.Message}");
                    return true; // デフォルトで有効
                }
            }
        }

        /// <summary>
        ///     リージョンナビゲータが有効かどうか
        /// </summary>
        public static bool IsRegionNavigatorEnabled
        {
            get
            {
                try
                {
                    return GetOptionPageProperty("EnableRegionNavigator", true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading RegionNavigator setting: {ex.Message}");
                    return true; // デフォルトで有効
                }
            }
        }

        /// <summary>
        ///     ソリューションエクスプローラー同期機能が有効かどうか
        /// </summary>
        public static bool IsSyncToSolutionExplorerEnabled
        {
            get
            {
                try
                {
                    return GetOptionPageProperty("EnableSyncToSolutionExplorer", true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading SyncToSolutionExplorer setting: {ex.Message}");
                    return true; // デフォルトで有効
                }
            }
        }

        /// <summary>
        ///     GitHubリンクナビゲーション機能が有効かどうか
        /// </summary>
        public static bool IsNavigateGitHubLinesEnabled
        {
            get
            {
                try
                {
                    return GetOptionPageProperty("EnableNavigateGitHubLines", true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading NavigateGitHubLines setting: {ex.Message}");
                    return true; // デフォルトで有効
                }
            }
        }

        /// <summary>
        ///     プロジェクトリネーム機能が有効かどうか
        /// </summary>
        public static bool IsRenameProjectEnabled
        {
            get
            {
                try
                {
                    return GetOptionPageProperty("EnableRenameProject", true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading RenameProject setting: {ex.Message}");
                    return true; // デフォルトで有効
                }
            }
        }

        /// <summary>
        ///     ソリューションリネーム機能が有効かどうか
        /// </summary>
        public static bool IsRenameSolutionEnabled
        {
            get
            {
                try
                {
                    return GetOptionPageProperty("EnableRenameSolution", true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading RenameSolution setting: {ex.Message}");
                    return true; // デフォルトで有効
                }
            }
        }

        /// <summary>
        ///     GUID更新機能が有効かどうか
        /// </summary>
        public static bool IsUpdateGuidEnabled
        {
            get
            {
                try
                {
                    return GetOptionPageProperty("EnableUpdateGuid", true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading UpdateGuid setting: {ex.Message}");
                    return true; // デフォルトで有効
                }
            }
        }

        /// <summary>
        ///     GUID一括更新機能が有効かどうか
        /// </summary>
        public static bool IsBatchUpdateGuidEnabled
        {
            get
            {
                try
                {
                    return GetOptionPageProperty("EnableBatchUpdateGuid", true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading BatchUpdateGuid setting: {ex.Message}");
                    return true; // デフォルトで有効
                }
            }
        }

        /// <summary>
        /// テストデータ生成機能が有効かどうか
        /// </summary>
        public static bool IsSeedDataGeneratorEnabled
        {
            get
            {
                try
                {
                    return GetOptionPageProperty<bool>("EnableSeedDataGenerator", true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading SeedDataGenerator setting: {ex.Message}");
                    return true; // デフォルトで有効
                }
            }
        }

        /// <summary>
        ///     オプションページからプロパティを取得
        /// </summary>
        private static T GetOptionPageProperty<T>(string propertyName, T defaultValue)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var package = Package.GetGlobalService(typeof(SVsShell)) as IVsShell;
                if (package == null)
                {
                    Debug.WriteLine("Failed to get SVsShell service");
                    return defaultValue;
                }

                var packageGuid = boilersExtensionsPackage.PackageGuid;
                if (packageGuid == Guid.Empty)
                {
                    Debug.WriteLine("Failed to get Package GUID");
                    return defaultValue;
                }

                var optionPage =
                    BoilersExtensionsSettingsCommand.Instance?.Package.GetDialogPage(
                        typeof(BoilersExtensionsOptionPage)) as BoilersExtensionsOptionPage;
                if (optionPage == null)
                {
                    Debug.WriteLine("Failed to get BoilersExtensionsOptionPage");
                    return defaultValue;
                }

                var property = typeof(BoilersExtensionsOptionPage).GetProperty(propertyName);
                if (property == null)
                {
                    Debug.WriteLine($"Property {propertyName} not found in BoilersExtensionsOptionPage");
                    return defaultValue;
                }

                return (T)property.GetValue(optionPage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetOptionPageProperty: {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        ///     オプションページからプロパティを取得（非同期版）
        /// </summary>
        public static async Task<T> GetOptionPagePropertyAsync<T>(string propertyName, T defaultValue)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                return GetOptionPageProperty(propertyName, defaultValue);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetOptionPagePropertyAsync: {ex.Message}");
                return defaultValue;
            }
        }
    }
}