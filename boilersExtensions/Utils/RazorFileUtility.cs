using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace boilersExtensions.Utils
{
    public static class RazorFileUtility
    {
        // オリジナルのパスを取得するための補助メソッド

        public static string GetOriginalFilePath(string generatedPath)
        {
            if (string.IsNullOrEmpty(generatedPath))
            {
                return null;
            }

            // 生成されたパスから元のファイルパスを抽出
            var razorIndex = generatedPath.IndexOf(".razor", StringComparison.OrdinalIgnoreCase);
            if (razorIndex > 0)
            {
                // ".razor"の後に識別子が付いている場合、それを取り除く
                var baseFilePath = generatedPath.Substring(0, razorIndex + 6); // +6 for ".razor"
                return baseFilePath;
            }

            return generatedPath;
        }
    }
}
