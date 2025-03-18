using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace boilersExtensions.Utils
{
    public static class RazorMappingHelper
    {
        public static int MapToRazorLine(Dictionary<int, int> mapping, string csharpCode, int generatedCodeLine)
        {
            if (mapping == null || string.IsNullOrEmpty(csharpCode))
                return 0;

            if (mapping.TryGetValue(generatedCodeLine, out var position))
            {
                return GetLineNumberFromPosition(csharpCode, position);
            }

            return 0;
        }

        public static int GetLineNumberFromPosition(string text, int position)
        {
            if (string.IsNullOrEmpty(text) || position < 0 || position >= text.Length)
                return 0;

            int line = 1;
            for (int i = 0; i < position; i++)
            {
                if (text[i] == '\n')
                    line++;
            }

            return line;
        }

        public static async Task<int> FindRazorLineByCode(Dictionary<int, int> mapping, string csharpCode, int generatedCodeLine, string codeSnippet, Microsoft.CodeAnalysis.Document document)
        {
            // マッピング情報がある場合はそれを使用
            int mappedLine = MapToRazorLine(mapping, csharpCode, generatedCodeLine);
            if (mappedLine > 0)
                return mappedLine;

            // マッピングがなければ、コード内容に基づいて検索
            if (string.IsNullOrEmpty(codeSnippet))
                return 0;

            try
            {
                // DTE経由でRazorファイルの内容を取得
                var dte = (DTE)(AsyncPackage.GetGlobalService(typeof(DTE)));
                var filePath = document.FilePath;

                // ファイルパスから元のRazorファイルパスを取得
                var razorFilePath = RazorFileUtility.GetOriginalFilePath(filePath);
                if (!File.Exists(razorFilePath))
                    return 0;

                // Razorファイルの内容を読み込む
                var razorContent = File.ReadAllText(razorFilePath);
                var lines = razorContent.Split(new[] { '\n' });

                // 特徴的なコード部分を抽出（余分な空白を削除）
                string searchPattern = codeSnippet.Trim();
                if (searchPattern.Length > 20)
                    searchPattern = searchPattern.Substring(0, 20);

                // 行番号で検索
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(searchPattern))
                    {
                        Debug.WriteLine($"コード内容に基づいて行を検出: {i + 1}");
                        return i + 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"行検索中のエラー: {ex.Message}");
            }

            return 0;
        }

        /// <summary>
        /// マッピング情報を検証し、デバッグ出力します
        /// </summary>
        public static void ValidateMapping(Dictionary<int, int> mapping, string extractedCSharpCode)
        {
            if (mapping == null || string.IsNullOrEmpty(extractedCSharpCode))
                return;

            Debug.WriteLine($"マッピング情報のエントリ数: {mapping.Count}");

            // サンプルとしていくつかのエントリを出力
            int count = 0;
            foreach (var entry in mapping)
            {
                if (count++ < 10)
                {
                    var razorLine = GetLineNumberFromPosition(extractedCSharpCode, entry.Value);
                    Debug.WriteLine($"生成コード行 {entry.Key} -> Razor位置 {entry.Value} (行 {razorLine})");
                }
            }
        }

        private static string[] ExtractKeywords(string line)
        {
            // 意味のある特徴的なキーワードを抽出
            return line.Split(new[] { ' ', '.', '(', ')', ',', ';', '=', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length > 3 && !IsCommonWord(word))
                .ToArray();
        }

        private static bool IsCommonWord(string word)
        {
            // 一般的な単語やキーワードを除外
            string[] commonWords = { "this", "null", "void", "true", "false", "async", "await", "return", "using", "class", "public", "private" };
            return commonWords.Contains(word.ToLower());
        }
    }
}
