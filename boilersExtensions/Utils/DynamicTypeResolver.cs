using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace boilersExtensions.Utils
{
    /// <summary>
    /// 型シンボルを動的に解決するためのヘルパークラス
    /// </summary>
    public static class DynamicTypeResolver
    {
        // 検索結果をキャッシュするための辞書
        private static readonly Dictionary<string, string> TypeNameToFullNameCache = new Dictionary<string, string>();

        /// <summary>
        /// コンパイレーションから型名に最も近い型シンボルを検索
        /// </summary>
        /// <param name="compilation">コンパイレーション</param>
        /// <param name="typeName">検索する型名</param>
        /// <returns>見つかった型シンボル、または null</returns>
        public static INamedTypeSymbol FindTypeSymbol(Compilation compilation, string typeName)
        {
            try
            {
                // 型名が完全修飾名の場合はそのまま検索
                if (typeName.Contains("."))
                {
                    var directSymbol = compilation.GetTypeByMetadataName(typeName);
                    if (directSymbol != null)
                    {
                        Debug.WriteLine($"完全修飾名で型を直接見つけました: {typeName}");
                        return directSymbol;
                    }
                }

                // キャッシュをチェック
                if (TypeNameToFullNameCache.TryGetValue(typeName, out var cachedFullName))
                {
                    var cachedSymbol = compilation.GetTypeByMetadataName(cachedFullName);
                    if (cachedSymbol != null)
                    {
                        Debug.WriteLine($"キャッシュから型を見つけました: {typeName} -> {cachedFullName}");
                        return cachedSymbol;
                    }
                }

                // 名前で検索
                var symbols = compilation.GetSymbolsWithName(typeName, SymbolFilter.Type)
                    .OfType<INamedTypeSymbol>()
                    .ToList();

                if (symbols.Count > 0)
                {
                    // 最も関連性の高い型を選択
                    var bestMatch = SelectBestTypeMatch(symbols, typeName);
                    if (bestMatch != null)
                    {
                        // キャッシュに追加
                        TypeNameToFullNameCache[typeName] = bestMatch.ToDisplayString();
                        Debug.WriteLine($"名前検索から型を見つけました: {typeName} -> {bestMatch.ToDisplayString()}");
                        return bestMatch;
                    }
                }

                // 部分一致で検索
                symbols = compilation.GetSymbolsWithName(
                    name => name.EndsWith(typeName, StringComparison.OrdinalIgnoreCase),
                    SymbolFilter.Type)
                    .OfType<INamedTypeSymbol>()
                    .ToList();

                if (symbols.Count > 0)
                {
                    var bestMatch = SelectBestTypeMatch(symbols, typeName);
                    if (bestMatch != null)
                    {
                        TypeNameToFullNameCache[typeName] = bestMatch.ToDisplayString();
                        Debug.WriteLine($"部分一致検索から型を見つけました: {typeName} -> {bestMatch.ToDisplayString()}");
                        return bestMatch;
                    }
                }

                // 推測による検索
                var candidates = GenerateNamespaceCandidates(typeName);
                foreach (var candidate in candidates)
                {
                    var symbol = compilation.GetTypeByMetadataName(candidate);
                    if (symbol != null)
                    {
                        TypeNameToFullNameCache[typeName] = candidate;
                        Debug.WriteLine($"名前空間推測から型を見つけました: {typeName} -> {candidate}");
                        return symbol;
                    }
                }

                Debug.WriteLine($"型が見つかりませんでした: {typeName}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"型検索中にエラーが発生しました: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 複数の型シンボルから最適なものを選択
        /// </summary>
        private static INamedTypeSymbol SelectBestTypeMatch(List<INamedTypeSymbol> symbols, string typeName)
        {
            // アクセス可能性の高い順（public > internal > private）
            symbols = symbols.OrderByDescending(s => s.DeclaredAccessibility).ToList();

            // UI系のライブラリを優先（Blazor、WPF、Windowsなど）
            var uiRelated = symbols.FirstOrDefault(s =>
                IsUIRelatedNamespace(s.ContainingNamespace.ToDisplayString()));
            if (uiRelated != null)
            {
                return uiRelated;
            }

            // 名前がそのままマッチするものを優先
            var exactNameMatch = symbols.FirstOrDefault(s => s.Name == typeName);
            if (exactNameMatch != null)
            {
                return exactNameMatch;
            }

            // それ以外の場合は最初の要素を返す
            return symbols.FirstOrDefault();
        }

        /// <summary>
        /// 与えられた名前空間がUI関連かどうかをチェック
        /// </summary>
        private static bool IsUIRelatedNamespace(string ns)
        {
            return ns.Contains("Blazor") ||
                   ns.Contains("Components") ||
                   ns.Contains("UI") ||
                   ns.Contains("Control") ||
                   ns.Contains("Window") ||
                   ns.Contains("Wpf") ||
                   ns.Contains("Xamarin") ||
                   ns.Contains("MAUI");
        }

        /// <summary>
        /// 型名から可能性のある完全修飾名の候補を生成
        /// </summary>
        private static IEnumerable<string> GenerateNamespaceCandidates(string typeName)
        {
            // 一般的な名前空間のパターン
            var commonNamespaces = new[]
            {
                // UI・Webフレームワーク関連
                $"Microsoft.AspNetCore.Components.{typeName}",
                $"Microsoft.AspNetCore.Components.Web.{typeName}",
                $"Microsoft.AspNetCore.Components.Forms.{typeName}",
                $"Microsoft.AspNetCore.Mvc.{typeName}",
                
                // よく使われるコンポーネントライブラリ（特定のライブラリをハードコードせず一般的なパターン）
                $"Components.{typeName}",
                $"{GetLibraryNameFromComponentName(typeName)}.Components.{typeName}",
                $"{GetLibraryNameFromComponentName(typeName)}.{typeName}",
                
                // .NET標準
                $"System.{typeName}",
                $"System.Collections.Generic.{typeName}",
                $"System.Linq.{typeName}",
                $"System.ComponentModel.{typeName}"
            };

            return commonNamespaces;
        }

        /// <summary>
        /// コンポーネント名からライブラリ名を推測する
        /// </summary>
        private static string GetLibraryNameFromComponentName(string componentName)
        {
            // 一般的なUI関連の型の場合
            if (componentName == "SelectedItem" ||
                componentName == "Select" ||
                componentName == "Table" ||
                componentName == "Tree" ||
                componentName == "Button" ||
                componentName == "Grid" ||
                componentName.Contains("Panel"))
            {
                // ここで特定のライブラリ名をハードコードするのではなく、
                // Razorファイル内の @using ディレクティブなどから動的に判断するといいでしょう
                return "UI";  // 汎用的な名前を使用
            }

            // 一般的なパターンから推測（例：ButtonBaseならButtonが含まれるライブラリ）
            if (componentName.EndsWith("Base"))
            {
                return componentName.Substring(0, componentName.Length - 4);
            }

            // I接頭辞のインターフェースの場合は接頭辞を除去
            if (componentName.StartsWith("I") && componentName.Length > 1 && char.IsUpper(componentName[1]))
            {
                return componentName.Substring(1);
            }

            return componentName;
        }

        /// <summary>
        /// Razorファイルの@usingディレクティブから動的に名前空間を検出
        /// </summary>
        public static List<string> GetNamespacesFromUsingDirectives(List<string> usingDirectives)
        {
            var result = new List<string>();

            foreach (var usingDir in usingDirectives)
            {
                // コンポーネントライブラリの名前空間を特定
                if (usingDir.Contains("Components") ||
                    usingDir.Contains("Blazor") ||
                    usingDir.Contains("UI") ||
                    usingDir.Contains("Control"))
                {
                    // コンポーネントを含む可能性の高い名前空間を記録
                    result.Add(usingDir);

                    // サブ名前空間も考慮（例: BootstrapBlazor -> BootstrapBlazor.Components）
                    if (!usingDir.EndsWith(".Components"))
                    {
                        result.Add($"{usingDir}.Components");
                    }
                }
            }

            return result;
        }
    }
}