using System;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Threading;

namespace boilersExtensions.Resources
{
    internal static class ResourceService
    {
        private static ResourceManager resourceMan;

        internal static void InitializeCurrentCulture()
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.CurrentUICulture;
            Debug.WriteLine($"CurrentUICulture: {Thread.CurrentThread.CurrentUICulture}");
        }

        internal static string GetString(string name)
        {
            if (resourceMan == null)
            {
                // 正しいリソース名とアセンブリを指定
                resourceMan = new ResourceManager("boilersExtensions.Resources.Resources", typeof(boilersExtensions.Resources.Resources).Assembly);
                Debug.WriteLine($"ResourceManager initialized with {resourceMan.BaseName}");
            }

            try
            {
                string result = resourceMan.GetString(name, Thread.CurrentThread.CurrentUICulture);
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