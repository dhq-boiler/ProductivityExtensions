using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using ZLinq;
using Document = Microsoft.CodeAnalysis.Document;

namespace boilersExtensions.Utils
{
    public static class RazorMappingHelper
    {
        public static int MapToRazorLine(Dictionary<int, int> mapping, string csharpCode, int generatedCodeLine)
        {
            if (mapping == null || string.IsNullOrEmpty(csharpCode))
            {
                return 0;
            }

            var razorLine = 0;

            try
            {
                // 1. 直接マッピングを試みる
                if (mapping.TryGetValue(generatedCodeLine, out razorLine))
                {
                    return razorLine; // マッピングが行番号を直接格納するように修正
                }

                // 2. 最も近い行番号を見つける
                var closestLine = FindClosestMappedLine(mapping, generatedCodeLine);
                if (closestLine > 0)
                {
                    // 生成コードでの行の差を計算
                    var lineDifference = generatedCodeLine - closestLine;

                    // Razorでの対応する行を計算
                    if (mapping.TryGetValue(closestLine, out var closestRazorLine))
                    {
                        return closestRazorLine + lineDifference;
                    }
                }

                // 3. 行コンテンツに基づく検索を試みる
                return FindRazorLineByContent(csharpCode, generatedCodeLine);
            }
            finally
            {
                Debug.WriteLine($"生成コード行 {generatedCodeLine} -> Razor行 {razorLine} へのマッピング");
            }
        }

        private static int FindClosestMappedLine(Dictionary<int, int> mapping, int targetLine)
        {
            // マッピングに存在する行で、targetLineに最も近いものを見つける
            var closestLine = 0;
            var smallestDifference = int.MaxValue;

            foreach (var line in mapping.Keys)
            {
                var difference = Math.Abs(line - targetLine);
                if (difference < smallestDifference)
                {
                    smallestDifference = difference;
                    closestLine = line;
                }
            }

            // 差が大きすぎる場合は信頼性が低いと見なす
            return smallestDifference <= 5 ? closestLine : 0;
        }

        private static int FindRazorLineByContent(string csharpCode, int generatedCodeLine)
        {
            try
            {
                if (string.IsNullOrEmpty(csharpCode) || generatedCodeLine <= 0)
                {
                    return 0;
                }

                // 生成コードを行に分割
                var codeLines = csharpCode.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);

                // 行番号が範囲外
                if (generatedCodeLine > codeLines.Length)
                {
                    return 0;
                }

                // 対象行のコンテンツを取得
                var targetLineContent = codeLines[generatedCodeLine - 1].Trim();

                // 行が空またはコメントのみの場合はスキップ
                if (string.IsNullOrWhiteSpace(targetLineContent) ||
                    targetLineContent.StartsWith("//") ||
                    targetLineContent.StartsWith("/*"))
                {
                    return 0;
                }

                // 特徴的なコード部分を抽出
                var significantCode = ExtractSignificantCode(targetLineContent);
                if (string.IsNullOrEmpty(significantCode))
                {
                    return 0;
                }

                // 周辺の行も含めて解析
                var contextStartLine = Math.Max(0, generatedCodeLine - 3);
                var contextEndLine = Math.Min(codeLines.Length - 1, generatedCodeLine + 3);

                var contextCode = new StringBuilder();
                for (var i = contextStartLine; i <= contextEndLine; i++)
                {
                    if (i == generatedCodeLine - 1)
                    {
                        contextCode.AppendLine("/** TARGET LINE **/ " + codeLines[i]);
                    }
                    else
                    {
                        contextCode.AppendLine(codeLines[i]);
                    }
                }

                Debug.WriteLine($"解析対象コード（行 {generatedCodeLine}）:\n{contextCode}");
                Debug.WriteLine($"特徴的コード部分: {significantCode}");

                // Razorファイル内で検索すべき行を推定
                // 実際のアプリケーションでは、ここでRazorファイルの内容を解析して対応する行を見つける
                // この例ではデモ目的で、生成コードの行番号をそのまま返しています
                return generatedCodeLine;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FindRazorLineByContent エラー: {ex.Message}");
                return 0;
            }
        }

        // 特徴的なコード部分を抽出するヘルパーメソッド
        private static string ExtractSignificantCode(string lineContent)
        {
            // 長すぎる行は短くする
            if (lineContent.Length > 100)
            {
                lineContent = lineContent.Substring(0, 100);
            }

            // 括弧内の重要な部分を抽出
            var openBrace = lineContent.IndexOf('(');
            if (openBrace > 0 && lineContent.IndexOf(')', openBrace) > openBrace)
            {
                // メソッド呼び出しっぽい部分を抽出
                var methodNameCandidate = lineContent.Substring(0, openBrace).Trim();
                var lastSpace = methodNameCandidate.LastIndexOf(' ');
                if (lastSpace > 0)
                {
                    methodNameCandidate = methodNameCandidate.Substring(lastSpace).Trim();
                }

                return methodNameCandidate;
            }

            // 変数宣言の抽出
            var variablePattern = new Regex(@"\b[A-Za-z_][A-Za-z0-9_<>]*\s+[A-Za-z_][A-Za-z0-9_]*\s*[=;]");
            var varMatch = variablePattern.Match(lineContent);
            if (varMatch.Success)
            {
                return varMatch.Value.Trim();
            }

            // プロパティアクセスの抽出
            var propertyPattern = new Regex(@"[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*");
            var propMatch = propertyPattern.Match(lineContent);
            if (propMatch.Success)
            {
                return propMatch.Value;
            }

            // デフォルトとして、行内の最初の意味のある部分を返す
            var parts = lineContent.Split(
                new[] { ' ', '\t', '.', ',', ';', '(', ')', '{', '}', '=', '+', '-', '*', '/' },
                StringSplitOptions.RemoveEmptyEntries);

            return parts.Length > 0 ? parts[0] : string.Empty;
        }

        public static int GetLineNumberFromPosition(string text, int position)
        {
            if (string.IsNullOrEmpty(text) || position < 0 || position >= text.Length)
            {
                return 0;
            }

            var line = 1;
            for (var i = 0; i < position; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        public static async Task<int> FindRazorLineByCode(Dictionary<int, int> mapping, string csharpCode,
            int generatedCodeLine, string codeSnippet, Document document)
        {
            // マッピング情報がある場合はそれを使用
            var mappedLine = MapToRazorLine(mapping, csharpCode, generatedCodeLine);
            if (mappedLine > 0)
            {
                return mappedLine;
            }

            // マッピングがなければ、コード内容に基づいて検索
            if (string.IsNullOrEmpty(codeSnippet))
            {
                return 0;
            }

            try
            {
                // DTE経由でRazorファイルの内容を取得
                var dte = (DTE)AsyncPackage.GetGlobalService(typeof(DTE));
                var filePath = document.FilePath;

                // ファイルパスから元のRazorファイルパスを取得
                var razorFilePath = RazorFileUtility.GetOriginalFilePath(filePath);
                if (!File.Exists(razorFilePath))
                {
                    return 0;
                }

                // Razorファイルの内容を読み込む
                var razorContent = File.ReadAllText(razorFilePath);
                var lines = razorContent.Split('\n');

                // 特徴的なコード部分を抽出（余分な空白を削除）
                var searchPattern = codeSnippet.Trim();
                if (searchPattern.Length > 20)
                {
                    searchPattern = searchPattern.Substring(0, 20);
                }

                // 行番号で検索
                for (var i = 0; i < lines.Length; i++)
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
        ///     マッピング情報を検証し、デバッグ出力します
        /// </summary>
        public static void ValidateMapping(Dictionary<int, int> mapping, string extractedCSharpCode)
        {
            if (mapping == null || string.IsNullOrEmpty(extractedCSharpCode))
            {
                return;
            }

            Debug.WriteLine($"マッピング情報のエントリ数: {mapping.Count}");

            // サンプルとしていくつかのエントリを出力
            var count = 0;
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
            return line.Split(new[] { ' ', '.', '(', ')', ',', ';', '=', '"', '\'' },
                    StringSplitOptions.RemoveEmptyEntries)
                .AsValueEnumerable()
                .Where(word => word.Length > 3 && !IsCommonWord(word))
                .ToArray();
        }

        private static bool IsCommonWord(string word)
        {
            // 一般的な単語やキーワードを除外
            string[] commonWords =
            {
                "this", "null", "void", "true", "false", "async", "await", "return", "using", "class", "public",
                "private"
            };
            return commonWords
                .AsValueEnumerable().Contains(word.ToLower());
        }
    }
}