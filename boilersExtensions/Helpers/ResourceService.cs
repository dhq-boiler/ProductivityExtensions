using System.Diagnostics;
using System;
using System.Globalization;
using System.Resources;
using System.Threading;
using boilersExtensions.Properties;
using Microsoft.VisualStudio.PlatformUI.Search;
using Prism.Mvvm;

namespace boilersExtensions.Helpers
{
    /// <summary>
    ///     https://qiita.com/YSRKEN/items/a96bcec8dfb0a8340a5f
    /// </summary>
    public class ResourceService : BindableBase
    {
        public static ResourceService Current { get; } = new ResourceService();

        public Resource Resource { get; } = new Resource();

        private static ResourceManager resourceMan;

        /// <summary>
        ///     リソースのカルチャーを変更
        /// </summary>
        /// <param name="name">カルチャー名</param>
        public void ChangeCulture(string name)
        {
            Resource.Culture = CultureInfo.GetCultureInfo(name);
            RaisePropertyChanged(nameof(Resource));
        }

        internal static void InitializeCurrentCulture()
        {
            Resources.Culture = CultureInfo.CurrentUICulture;
            Debug.WriteLine($"CurrentUICulture: {Resources.Culture}");
        }

        internal static string GetString(string name)
        {
            if (resourceMan == null)
            {
                // 正しいリソース名とアセンブリを指定
                resourceMan = new ResourceManager("boilersExtensions.Properties.Resource", typeof(boilersExtensions.Properties.Resource).Assembly);
                Debug.WriteLine($"ResourceManager initialized with {resourceMan.BaseName}");
            }

            try
            {
                // 再試行メカニズムを追加
                string result = null;
                int retryCount = 0;
                while (result == null && retryCount < 3)
                {
                    result = resourceMan.GetString(name, Resources.Culture);
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
    }
}