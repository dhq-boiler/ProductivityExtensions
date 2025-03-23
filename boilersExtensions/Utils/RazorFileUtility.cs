using System;
using System.IO;

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

            // 1. URIデコードして正確なパスを取得
            generatedPath = Uri.UnescapeDataString(generatedPath);

            // 2. 複数のパターンに対応
            var razorPatterns = new[] { ".razor", ".cshtml" };

            foreach (var pattern in razorPatterns)
            {
                var patternIndex = generatedPath.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (patternIndex > 0)
                {
                    // パターンの後に追加の識別子がある場合それを取り除く
                    var baseFilePath = generatedPath.Substring(0, patternIndex + pattern.Length);

                    // 追加の検証: これが実際のファイルパスかどうか
                    if (File.Exists(baseFilePath))
                    {
                        return baseFilePath;
                    }
                }
            }

            // 3. 末尾から検索する代替アプローチ
            var lastRazorIndex = generatedPath.LastIndexOf(".razor", StringComparison.OrdinalIgnoreCase);
            if (lastRazorIndex > 0)
            {
                var candidatePath = generatedPath.Substring(0, lastRazorIndex + 6);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            // 4. ファイル名のみを使用した最終的な試み
            var fileName = Path.GetFileName(generatedPath);
            var directoryName = Path.GetDirectoryName(generatedPath);

            if (!string.IsNullOrEmpty(directoryName))
            {
                foreach (var pattern in razorPatterns)
                {
                    var nameIndex = fileName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                    if (nameIndex > 0)
                    {
                        var baseName = fileName.Substring(0, nameIndex + pattern.Length);
                        var candidatePath = Path.Combine(directoryName, baseName);

                        if (File.Exists(candidatePath))
                        {
                            return candidatePath;
                        }
                    }
                }
            }

            // どの方法でも元のパスを見つけられなかった場合は生成されたパスを返す
            return generatedPath;
        }
    }
}