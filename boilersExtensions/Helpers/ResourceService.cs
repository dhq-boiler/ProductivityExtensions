using boilersExtensions.Properties;
using Prism.Mvvm;
using System.Globalization;

namespace boilersExtensions.Helpers
{

    /// <summary>
    /// https://qiita.com/YSRKEN/items/a96bcec8dfb0a8340a5f
    /// </summary>
    public class ResourceService : BindableBase
    {
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
    }
}