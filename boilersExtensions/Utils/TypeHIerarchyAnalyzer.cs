using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using VSLangProj;
using Document = Microsoft.CodeAnalysis.Document;
using Project = EnvDTE.Project;

namespace boilersExtensions.Utils
{
    /// <summary>
    ///     型の継承階層や実装インターフェースを分析するクラス
    /// </summary>
    public class TypeHierarchyAnalyzer
    {
        /// <summary>
        ///     カーソル位置の型シンボルとその親要素を取得
        /// </summary>
        public static async
    Task<(ITypeSymbol typeSymbol, SyntaxNode parentNode, TextSpan fullTypeSpan, TextSpan? baseTypeSpan)>
    GetTypeSymbolAtPositionAsync(Document document, int position)
        {
            try
            {
                SemanticModel semanticModel = null;
                SyntaxNode syntaxRoot = null;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // アクティブなドキュメントのパスを取得
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                var documentPath = dte.ActiveDocument.FullName;

                // Razorファイルの場合はRazor言語サービスを使用して直接処理
                bool isRazorFile = documentPath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                                   documentPath.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase);

                if (isRazorFile)
                {
                    Debug.WriteLine("Razorファイルが検出されました。");

                    // Razorファイルの内容を読み取る
                    string razorContent = File.ReadAllText(documentPath);

                    // ディレクティブを抽出
                    var usingDirectives = ExtractUsingDirectives(razorContent);

                    // _Imports.razorからの@using宣言を追加
                    var importsDirectives = ExtractImportsFromImportsRazor(documentPath);
                    foreach (var directive in importsDirectives)
                    {
                        if (!usingDirectives.Contains(directive))
                        {
                            usingDirectives.Add(directive);
                        }
                    }

                    var injectDirectives = ExtractInjectDirectives(razorContent);

                    // デバッグ出力
                    Debug.WriteLine($"@usingディレクティブ: {string.Join(", ", usingDirectives)}");
                    Debug.WriteLine($"@injectディレクティブ: {string.Join(", ", injectDirectives.Select(x => $"{x.type} {x.name}"))}");

                    // C#コードブロックを探す簡易パーサーとポジションマッピングを取得
                    var (csharpBlocks, mappedPosition, blockStartPosition) = ExtractCSharpBlocksWithMapping(razorContent, position);

                    if (!string.IsNullOrEmpty(csharpBlocks) && mappedPosition >= 0)
                    {
                        // using/injectディレクティブを適用
                        (string code, int adjustedPosition) = ApplyDirectivesToCSharpCodeWithPreciseMapping(csharpBlocks, usingDirectives, injectDirectives, mappedPosition);

                        // プロジェクトの参照を動的に取得
                        var references = await GetActiveProjectReferencesAsync();

                        // C#コードブロックをRoslynで解析
                        var syntaxTree = CSharpSyntaxTree.ParseText(code);
                        var compilation = CSharpCompilation.Create("RazorCompilation")
                            .AddSyntaxTrees(syntaxTree)
                            .AddReferences(references);

                        // 解析結果を使用して処理を続行...
                        semanticModel = compilation.GetSemanticModel(syntaxTree);
                        syntaxRoot = await syntaxTree.GetRootAsync();

                        // 元のpositionではなく、マッピングされたpositionを使用
                        position = adjustedPosition;
                    }
                    else
                    {
                        // コードブロックが見つからない場合は、一時的なコードを生成して解析
                        string tempCode = CreateTemporaryCode(usingDirectives, razorContent);
                        var syntaxTree = CSharpSyntaxTree.ParseText(tempCode);
                        var references = await GetActiveProjectReferencesAsync();
                        var compilation = CSharpCompilation.Create("RazorCompilation")
                            .AddSyntaxTrees(syntaxTree)
                            .AddReferences(references);

                        semanticModel = compilation.GetSemanticModel(syntaxTree);
                        syntaxRoot = await syntaxTree.GetRootAsync();

                        // 便宜上の位置を設定
                        position = tempCode.IndexOf("var placeholder") + 10;
                    }
                }
                else
                {
                    if (document == null)
                    {
                        return (null, null, default, null);
                    }

                    semanticModel = await document.GetSemanticModelAsync();
                    if (semanticModel == null)
                    {
                        return (null, null, default, null);
                    }

                    syntaxRoot = await document.GetSyntaxRootAsync();
                    if (syntaxRoot == null)
                    {
                        return (null, null, default, null);
                    }
                }

                // カーソル位置のノードを取得
                var node = syntaxRoot.FindNode(new TextSpan(position, 0), getInnermostNodeForTie: true);
                if (node == null)
                {
                    return (null, null, default, null);
                }

                Debug.WriteLine($"Position {position}のノード: {node.GetType().Name} - {node}");

                // 型名を参照するノードを特定
                TypeSyntax typeSyntax = null;
                SyntaxNode parentNode = null;

                // メソッド戻り値の型
                if (node.Parent is MethodDeclarationSyntax methodDecl &&
                    node == methodDecl.ReturnType)
                {
                    typeSyntax = methodDecl.ReturnType;
                    parentNode = methodDecl;
                }
                // パラメータの型
                else if (node.Parent is ParameterSyntax paramSyntax &&
                         node == paramSyntax.Type)
                {
                    typeSyntax = paramSyntax.Type;
                    parentNode = paramSyntax;
                }
                // プロパティの型
                else if (node.Parent is PropertyDeclarationSyntax propDecl &&
                         node == propDecl.Type)
                {
                    typeSyntax = propDecl.Type;
                    parentNode = propDecl;
                }
                // 変数宣言の型
                else if (node.Parent is VariableDeclarationSyntax varDecl &&
                         node == varDecl.Type)
                {
                    typeSyntax = varDecl.Type;
                    parentNode = varDecl;
                }
                // フィールド宣言の型
                else if (node.Parent is FieldDeclarationSyntax fieldDecl &&
                         fieldDecl.Declaration?.Type == node)
                {
                    typeSyntax = fieldDecl.Declaration.Type;
                    parentNode = fieldDecl;
                }
                // 直接TypeSyntaxの場合
                else if (node is TypeSyntax)
                {
                    typeSyntax = node as TypeSyntax;
                    parentNode = node.Parent;
                }
                // 識別子（型名の一部）の場合
                else if (node is IdentifierNameSyntax)
                {
                    var parent = node.Parent;
                    while (parent != null && !(parent is TypeSyntax))
                    {
                        parent = parent.Parent;
                    }

                    typeSyntax = parent as TypeSyntax;
                    parentNode = parent?.Parent;

                    // 親が見つからない場合は識別子自体から型を推測
                    if (typeSyntax == null)
                    {
                        var identifierName = (IdentifierNameSyntax)node;
                        string typeName = identifierName.Identifier.Text;

                        // 動的に型を解決してみる
                        var resolvedType = DynamicTypeResolver.FindTypeSymbol(semanticModel.Compilation, typeName);
                        if (resolvedType != null)
                        {
                            Debug.WriteLine($"識別子から型を動的に解決しました: {typeName} -> {resolvedType.ToDisplayString()}");
                            var syntheticTypeSpan = new TextSpan(position, typeName.Length);
                            return (resolvedType, node.Parent, syntheticTypeSpan, syntheticTypeSpan);
                        }
                    }
                }
                // ジェネリック型制約
                else if (node.Parent is TypeParameterConstraintSyntax)
                {
                    typeSyntax = node.Parent.ChildNodes().OfType<TypeSyntax>().FirstOrDefault();
                    parentNode = node.Parent;
                }

                if (typeSyntax == null)
                {
                    // 型構文が見つからない場合、Razorファイルなら特別に処理
                    if (isRazorFile)
                    {
                        // ノードのテキストから型名を抽出
                        string nodeText = node.ToString();
                        string typeName = ExtractTypeNameOnly(nodeText);

                        if (!string.IsNullOrEmpty(typeName))
                        {
                            // 動的に型を検索
                            var resolvedType = DynamicTypeResolver.FindTypeSymbol(semanticModel.Compilation, typeName);
                            if (resolvedType != null)
                            {
                                Debug.WriteLine($"テキストから型を動的に解決しました: {typeName} -> {resolvedType.ToDisplayString()}");
                                var syntheticTypeSpan = new TextSpan(position, typeName.Length);
                                return (resolvedType, node, syntheticTypeSpan, syntheticTypeSpan);
                            }
                        }
                    }

                    return (null, null, default, null);
                }

                // 型の完全なスパンを取得
                var fullTypeSpan = typeSyntax.Span;

                // ジェネリック型の場合は、基本型名部分のみのスパンを計算
                TextSpan? baseTypeSpan = null;
                if (typeSyntax is GenericNameSyntax genericName)
                {
                    // ジェネリック型の基本名だけのスパン（例: List<int>のうちのList部分）
                    baseTypeSpan = new TextSpan(
                        genericName.SpanStart,
                        genericName.Span.Length);

                    Debug.WriteLine($"ジェネリック型: {genericName}, 基本名: {genericName.Identifier.Text}");
                }
                else if (typeSyntax is QualifiedNameSyntax qualifiedName)
                {
                    // 修飾名（名前空間付き）の最後の部分だけを取得
                    baseTypeSpan = qualifiedName.Right.Span;
                }
                else
                {
                    // その他の場合は型全体のスパン
                    baseTypeSpan = typeSyntax.Span;
                }

                // 型シンボルを取得
                var symbolInfo = semanticModel.GetSymbolInfo(typeSyntax);
                var typeSymbol = symbolInfo.Symbol as ITypeSymbol;

                Debug.WriteLine($"型構文: {typeSyntax}");
                Debug.WriteLine($"シンボル情報: {symbolInfo.Symbol?.ToString() ?? "null"}");
                Debug.WriteLine($"候補理由: {symbolInfo.CandidateReason}");

                if (symbolInfo.CandidateSymbols.Length > 0)
                {
                    Debug.WriteLine("候補シンボル:");
                    foreach (var candidate in symbolInfo.CandidateSymbols)
                    {
                        Debug.WriteLine($"  - {candidate}");
                    }
                }

                // 型シンボルがnullの場合は動的に解決を試みる
                if (typeSymbol == null)
                {
                    string typeName = typeSyntax.ToString();
                    // ジェネリック型や名前空間修飾子を除去して単純な型名を取得
                    string simpleTypeName = ExtractTypeNameFromText(typeName);

                    Debug.WriteLine($"型シンボルがnullです。動的に解決を試みます: {simpleTypeName}");

                    // 動的型解決を使用
                    typeSymbol = DynamicTypeResolver.FindTypeSymbol(semanticModel.Compilation, simpleTypeName);

                    if (typeSymbol != null)
                    {
                        Debug.WriteLine($"型を動的に解決しました: {simpleTypeName} -> {typeSymbol.ToDisplayString()}");
                    }
                }

                return (typeSymbol, parentNode, fullTypeSpan, baseTypeSpan);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetTypeSymbolAtPositionAsyncでエラーが発生しました: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                return (null, null, default, null);
            }
        }

        /// <summary>
        /// テキストから型名のみを抽出
        /// </summary>
        internal static string ExtractTypeNameOnly(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // 「private」「public」などの修飾子を削除
            string[] modifiers = { "private", "public", "protected", "internal", "static", "readonly", "const" };
            foreach (var modifier in modifiers)
            {
                // 先頭にある場合のみ削除
                if (text.TrimStart().StartsWith(modifier + " ", StringComparison.OrdinalIgnoreCase))
                {
                    text = text.TrimStart().Substring(modifier.Length).TrimStart();
                }
            }

            // 変数名や初期化子を削除
            int spaceIndex = text.IndexOf(' ');
            if (spaceIndex > 0)
            {
                text = text.Substring(0, spaceIndex);
            }

            // ジェネリック型パラメータを調整
            if (text.Contains('<') && !text.Contains('>'))
            {
                // 不完全なジェネリック型の場合、閉じ括弧を追加
                text += ">";
            }

            return text.Trim();
        }

        /// <summary>
        /// _Imports.razorファイルから@using宣言を抽出する
        /// </summary>
        private static List<string> ExtractImportsFromImportsRazor(string documentPath)
        {
            var usingDirectives = new List<string>();
            try
            {
                // ドキュメントのパスから_Imports.razorを探す
                string projectDir = Path.GetDirectoryName(documentPath);

                // 現在のディレクトリから上位ディレクトリに向かって_Imports.razorを探す
                while (!string.IsNullOrEmpty(projectDir))
                {
                    string importsPath = Path.Combine(projectDir, "_Imports.razor");
                    if (File.Exists(importsPath))
                    {
                        string content = File.ReadAllText(importsPath);
                        var matches = System.Text.RegularExpressions.Regex.Matches(content, @"@using\s+([^\s;]+)");

                        foreach (System.Text.RegularExpressions.Match match in matches)
                        {
                            if (match.Groups.Count > 1)
                            {
                                string ns = match.Groups[1].Value;
                                if (!usingDirectives.Contains(ns))
                                {
                                    usingDirectives.Add(ns);
                                }
                            }
                        }

                        // 見つかったら処理を終了
                        break;
                    }

                    // 一つ上のディレクトリへ
                    string parentDir = Path.GetDirectoryName(projectDir);

                    // ルートに達したら終了
                    if (parentDir == projectDir)
                        break;

                    projectDir = parentDir;
                }

                // 親ディレクトリでも見つからない場合は、プロジェクト全体を検索
                if (usingDirectives.Count == 0)
                {
                    // プロジェクトルートを推測
                    string solutionDir = GetSolutionDirectory(documentPath);
                    if (!string.IsNullOrEmpty(solutionDir))
                    {
                        foreach (var importsFile in Directory.GetFiles(solutionDir, "_Imports.razor", SearchOption.AllDirectories))
                        {
                            string content = File.ReadAllText(importsFile);
                            var matches = System.Text.RegularExpressions.Regex.Matches(content, @"@using\s+([^\s;]+)");

                            foreach (System.Text.RegularExpressions.Match match in matches)
                            {
                                if (match.Groups.Count > 1)
                                {
                                    string ns = match.Groups[1].Value;
                                    if (!usingDirectives.Contains(ns))
                                    {
                                        usingDirectives.Add(ns);
                                    }
                                }
                            }

                            // 最初に見つかったものだけ使用
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"_Imports.razorの解析エラー: {ex.Message}");
            }

            return usingDirectives;
        }

        /// <summary>
        /// ドキュメントパスからソリューションディレクトリを推測
        /// </summary>
        private static string GetSolutionDirectory(string documentPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(documentPath);
                while (!string.IsNullOrEmpty(dir))
                {
                    // .slnファイルがあるか確認
                    if (Directory.GetFiles(dir, "*.sln").Any())
                    {
                        return dir;
                    }

                    // .csprojファイルがあるか確認
                    if (Directory.GetFiles(dir, "*.csproj").Any())
                    {
                        // プロジェクトフォルダが見つかった場合はその親ディレクトリを返す
                        string _parentDir = Path.GetDirectoryName(dir);
                        return string.IsNullOrEmpty(_parentDir) ? dir : _parentDir;
                    }

                    // 一つ上のディレクトリへ
                    string parentDir = Path.GetDirectoryName(dir);

                    // ルートに達したら終了
                    if (parentDir == dir)
                        break;

                    dir = parentDir;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ソリューションディレクトリ取得エラー: {ex.Message}");
            }

            return null;
        }

        // テキストから型名を抽出するヘルパーメソッド
        private static string ExtractTypeNameFromText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
                
            // ジェネリック型パラメータを除去
            int genericIdx = text.IndexOf('<');
            if (genericIdx > 0)
            {
                text = text.Substring(0, genericIdx);
            }
            
            // 名前空間修飾子がある場合は最後の部分だけを取得
            int lastDot = text.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < text.Length - 1)
            {
                return text.Substring(lastDot + 1);
            }
            
            return text;
        }

        // Razorファイルから一時的なC#コードを生成
        private static string CreateTemporaryCode(List<string> usingDirectives, string razorContent)
        {
            var sb = new StringBuilder();
            
            // 標準名前空間
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using Microsoft.AspNetCore.Components;");
            sb.AppendLine("using Microsoft.AspNetCore.Components.Forms;");
            sb.AppendLine("using Microsoft.AspNetCore.Components.Web;");
            
            // ユーザー定義の名前空間
            foreach (var directive in usingDirectives)
            {
                sb.AppendLine($"using {directive};");
            }
            
            // コンポーネントライブラリから関連する名前空間を推測して追加
            var componentNamespaces = DynamicTypeResolver.GetNamespacesFromUsingDirectives(usingDirectives);
            foreach (var ns in componentNamespaces)
            {
                if (!usingDirectives.Contains(ns))
                {
                    sb.AppendLine($"using {ns};");
                }
            }
            
            sb.AppendLine();
            sb.AppendLine("namespace RazorNamespace {");
            sb.AppendLine("  public class TempComponent : ComponentBase {");
            
            // インジェクト型プロパティを追加
            sb.AppendLine("    [Inject] private NavigationManager NavigationManager { get; set; }");
            sb.AppendLine("    [Inject] private IJSRuntime JSRuntime { get; set; }");
            
            // Razorコンテンツから型参照を抽出（例: @bind-SelectedItem="..."）
            var componentRefs = ExtractComponentReferences(razorContent);
            foreach (var compRef in componentRefs)
            {
                sb.AppendLine($"    // 参照されたコンポーネント: {compRef}");
                sb.AppendLine($"    private {compRef} {compRef.ToLower()}Instance;");
            }
            
            // メソッドの定義
            sb.AppendLine("    protected override void OnInitialized() {");
            sb.AppendLine("      // Placeholder for position tracking");
            sb.AppendLine("      var placeholder = typeof(object);");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            
            return sb.ToString();
        }

        // Razorファイルからコンポーネント参照を抽出
        private static List<string> ExtractComponentReferences(string razorContent)
        {
            var result = new List<string>();
            
            try
            {
                // よく使われるコンポーネント属性を検索
                var patterns = new[] {
                    @"@bind-(\w+)=",                 // @bind-Value, @bind-SelectedItem等
                    @"<(\w+)[\s>]",                  // <Button>, <Select>等
                    @"<(\w+)@",                      // <Table@ref>等
                    @"[^.]\.(\w+)\s*\(",             // 型メソッド呼び出し
                    @"typeof\((\w+(?:\.\w+)*)\)"     // typeof(SelectedItem)等
                };
                
                foreach (var pattern in patterns)
                {
                    var matches = System.Text.RegularExpressions.Regex.Matches(razorContent, pattern);
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        if (match.Groups.Count > 1)
                        {
                            string componentName = match.Groups[1].Value;
                            
                            // HTMLタグと区別するための簡易チェック（先頭が大文字かどうか）
                            if (componentName.Length > 0 && char.IsUpper(componentName[0]))
                            {
                                if (!result.Contains(componentName))
                                {
                                    result.Add(componentName);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"コンポーネント参照抽出エラー: {ex.Message}");
            }
            
            return result;
        }

        // プロジェクトの参照を動的に解決する
        // アクティブプロジェクトから参照を動的に解決する
        private static async Task<List<MetadataReference>> GetActiveProjectReferencesAsync()
        {
            var references = new List<MetadataReference>();

            try
            {
                // UIスレッドに切り替え
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // 基本的な.NET参照を追加
                references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
                references.Add(MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location));
                references.Add(MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location));
                references.Add(MetadataReference.CreateFromFile(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute).Assembly.Location));

                // ASP.NET Core関連アセンブリの追加を試行
                TryAddAssemblyReference(references, "Microsoft.AspNetCore.Components");
                TryAddAssemblyReference(references, "Microsoft.AspNetCore.Components.Web");
                TryAddAssemblyReference(references, "Microsoft.AspNetCore.Components.Forms");
                TryAddAssemblyReference(references, "Microsoft.JSInterop");

                // DTEからアクティブプロジェクトを取得
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                var activeProject = GetProjectHaveOpenedFileInTextEditor();
                if (activeProject != null)
                {
                    Debug.WriteLine($"アクティブプロジェクト: {activeProject.Name}");

                    // VSS References コレクションを取得
                    var vssReferences = activeProject.Object as VSLangProj.VSProject;
                    if (vssReferences != null)
                    {
                        // プロジェクト内の全参照を追加
                        foreach (VSLangProj.Reference reference in vssReferences.References)
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(reference.Path) && File.Exists(reference.Path))
                                {
                                    references.Add(MetadataReference.CreateFromFile(reference.Path));
                                    Debug.WriteLine($"参照追加: {reference.Name} - {reference.Path}");
                                }
                                else if (!string.IsNullOrEmpty(reference.Name))
                                {
                                    // パスが取得できない場合はNuGetキャッシュから探す
                                    var resolvedPath = TryResolveNuGetReference(reference.Name);
                                    if (!string.IsNullOrEmpty(resolvedPath))
                                    {
                                        references.Add(MetadataReference.CreateFromFile(resolvedPath));
                                        Debug.WriteLine($"NuGetから解決した参照: {reference.Name} - {resolvedPath}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"参照追加エラー ({reference.Name}): {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        Debug.WriteLine("VSProject参照を取得できませんでした。代替方法を使用します。");

                        // 代替方法：プロジェクトファイルを解析
                        SearchForProjectReferences(activeProject, references);
                    }

                    // プロジェクト出力フォルダからDLLを検索
                    SearchForAssembliesInProjectOutput(activeProject, references);
                }

                // Roslynワークスペースからも参照を取得
                await AddReferencesFromRoslynWorkspace(references);

                // 実行中のアセンブリから一般的なUI/Blazorライブラリを探す
                SearchForUIAssembliesInCurrentDomain(references);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"参照解決エラー: {ex.Message}");
            }

            return references;
        }

        // 名前からアセンブリ参照を追加
        private static void TryAddAssemblyReference(List<MetadataReference> references, string assemblyName)
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == assemblyName);

                if (assembly != null)
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                    Debug.WriteLine($"アセンブリ参照追加: {assemblyName}");
                }
                else
                {
                    Debug.WriteLine($"アセンブリ読み込み不可: {assemblyName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{assemblyName}の追加エラー: {ex.Message}");
            }
        }

        // NuGetパッケージを解決
        private static string TryResolveNuGetReference(string referenceName)
        {
            try
            {
                // NuGetパッケージのあり得る名前を生成
                string packageName = referenceName;

                // 一般的なパッケージ名変換を適用
                if (referenceName.Contains("."))
                {
                    // "Some.Library.Component" -> "Some.Library"のようなパターン
                    var parts = referenceName.Split('.');
                    if (parts.Length > 2)
                    {
                        packageName = string.Join(".", parts.Take(2));
                    }
                }

                // NuGetパッケージ検索パス
                string nugetDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nuget", "packages");

                if (Directory.Exists(nugetDir))
                {
                    // パッケージ名でディレクトリ検索
                    var possiblePackageDirs = Directory.GetDirectories(nugetDir, packageName, SearchOption.TopDirectoryOnly);

                    // パターンマッチでも検索
                    if (possiblePackageDirs.Length == 0)
                    {
                        possiblePackageDirs = Directory.GetDirectories(nugetDir)
                            .Where(d => Path.GetFileName(d).IndexOf(packageName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        packageName.IndexOf(Path.GetFileName(d), StringComparison.OrdinalIgnoreCase) >= 0)
                            .ToArray();
                    }

                    foreach (var packageDir in possiblePackageDirs)
                    {
                        // 最新バージョンを取得
                        var versions = Directory.GetDirectories(packageDir);
                        if (versions.Length > 0)
                        {
                            var latestVersion = versions.OrderByDescending(v => v).First();

                            // ライブラリフォルダを検索
                            string libDir = Path.Combine(latestVersion, "lib");
                            if (Directory.Exists(libDir))
                            {
                                // 最新のフレームワークバージョンを検索
                                var frameworks = Directory.GetDirectories(libDir)
                                    .Where(d => Path.GetFileName(d).StartsWith("net"))
                                    .OrderByDescending(d => d);

                                foreach (var framework in frameworks)
                                {
                                    // DLLを検索
                                    var dllPath = Path.Combine(framework, $"{referenceName}.dll");
                                    if (File.Exists(dllPath))
                                    {
                                        return dllPath;
                                    }

                                    // 名前の一部一致でも検索
                                    var matchingFiles = Directory.GetFiles(framework, "*.dll")
                                        .Where(f => Path.GetFileNameWithoutExtension(f).IndexOf(referenceName, StringComparison.OrdinalIgnoreCase) >= 0)
                                        .ToArray();

                                    if (matchingFiles.Length > 0)
                                    {
                                        return matchingFiles[0];
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NuGet参照解決エラー: {ex.Message}");
            }

            return null;
        }

        // プロジェクトファイルから参照を検索
        private static void SearchForProjectReferences(Project project, List<MetadataReference> references)
        {
            try
            {
                string projectPath = project.FullName;
                if (File.Exists(projectPath))
                {
                    string projectContent = File.ReadAllText(projectPath);

                    // パッケージ参照を検索
                    var packageRefRegex = new System.Text.RegularExpressions.Regex(
                        @"<PackageReference\s+Include=""([^""]+)""\s+Version=""([^""]+)""");

                    var matches = packageRefRegex.Matches(projectContent);
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        if (match.Groups.Count >= 3)
                        {
                            string packageName = match.Groups[1].Value;
                            string packageVersion = match.Groups[2].Value;

                            // NuGetキャッシュからDLLを検索
                            string nugetDir = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                ".nuget", "packages", packageName, packageVersion);

                            if (Directory.Exists(nugetDir))
                            {
                                // libフォルダを検索
                                string libDir = Path.Combine(nugetDir, "lib");
                                if (Directory.Exists(libDir))
                                {
                                    // フレームワークバージョンを検索
                                    var frameworkDirs = Directory.GetDirectories(libDir);
                                    if (frameworkDirs.Length > 0)
                                    {
                                        // 最新のフレームワークディレクトリを使用
                                        string latestFrameworkDir = frameworkDirs
                                            .OrderByDescending(d => d)
                                            .First();

                                        // 全てのDLLを参照に追加
                                        foreach (var dll in Directory.GetFiles(latestFrameworkDir, "*.dll"))
                                        {
                                            Debug.WriteLine($"プロジェクトファイルから見つけたDLL: {dll}");
                                            references.Add(MetadataReference.CreateFromFile(dll));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プロジェクト参照検索エラー: {ex.Message}");
            }
        }

        // プロジェクト出力フォルダからDLLを検索
        private static void SearchForAssembliesInProjectOutput(Project project, List<MetadataReference> references)
        {
            try
            {
                // 出力パスの取得を試みる
                string outputPath = null;
                if (project.ConfigurationManager != null &&
                    project.ConfigurationManager.ActiveConfiguration != null &&
                    project.ConfigurationManager.ActiveConfiguration.Properties != null)
                {
                    try
                    {
                        outputPath = project.ConfigurationManager.ActiveConfiguration.Properties.Item("OutputPath")?.Value?.ToString();
                    }
                    catch
                    {
                        // プロパティが取得できない場合は無視
                    }
                }

                // 出力パスが取得できない場合はデフォルトを使用
                if (string.IsNullOrEmpty(outputPath))
                {
                    outputPath = "bin\\Debug";
                }

                string projectDir = Path.GetDirectoryName(project.FullName);
                string fullOutputPath = Path.Combine(projectDir, outputPath);

                if (Directory.Exists(fullOutputPath))
                {
                    // ネットフレームワークフォルダを探す
                    var netDirs = Directory.GetDirectories(fullOutputPath, "net*");
                    if (netDirs.Length > 0)
                    {
                        // 各フレームワークディレクトリからDLLを追加
                        foreach (var netDir in netDirs)
                        {
                            foreach (var dll in Directory.GetFiles(netDir, "*.dll"))
                            {
                                try
                                {
                                    Debug.WriteLine($"プロジェクト出力からDLLを追加: {dll}");
                                    references.Add(MetadataReference.CreateFromFile(dll));
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"DLL追加エラー: {dll} - {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        // ネットフレームワークフォルダがない場合は直接DLLを探す
                        foreach (var dll in Directory.GetFiles(fullOutputPath, "*.dll"))
                        {
                            try
                            {
                                Debug.WriteLine($"プロジェクト出力からDLLを追加: {dll}");
                                references.Add(MetadataReference.CreateFromFile(dll));
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"DLL追加エラー: {dll} - {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プロジェクト出力参照検索エラー: {ex.Message}");
            }
        }

        // Roslynワークスペースから参照を追加
        private static async Task AddReferencesFromRoslynWorkspace(List<MetadataReference> references)
        {
            try
            {
                // コンポーネントモデルサービスを取得
                var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
                if (componentModel != null)
                {
                    var workspace = componentModel.GetService<VisualStudioWorkspace>();
                    if (workspace != null)
                    {
                        // アクティブなドキュメントのプロジェクトを検索
                        var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                        var activeDocumentPath = dte.ActiveDocument?.FullName;

                        if (!string.IsNullOrEmpty(activeDocumentPath))
                        {
                            var project = workspace.CurrentSolution.Projects
                                .FirstOrDefault(p => p.Documents.Any(d => string.Equals(d.FilePath, activeDocumentPath, StringComparison.OrdinalIgnoreCase)));

                            if (project != null)
                            {
                                // プロジェクトの参照を追加
                                foreach (var metadataRef in project.MetadataReferences)
                                {
                                    if (!references.Contains(metadataRef))
                                    {
                                        references.Add(metadataRef);
                                    }
                                }

                                // プロジェクト参照を追加
                                foreach (var projectRef in project.ProjectReferences)
                                {
                                    var referencedProject = workspace.CurrentSolution.GetProject(projectRef.ProjectId);
                                    if (referencedProject != null)
                                    {
                                        var compilation = await referencedProject.GetCompilationAsync();
                                        if (compilation != null)
                                        {
                                            references.Add(compilation.ToMetadataReference());
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Roslyn参照追加エラー: {ex.Message}");
            }
        }

        // 実行中のアセンブリからUI/Blazor関連アセンブリを検索
        private static void SearchForUIAssembliesInCurrentDomain(List<MetadataReference> references)
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();

                foreach (var assembly in assemblies)
                {
                    try
                    {
                        // アセンブリ名がUI/Blazor関連かどうかをチェック
                        string name = assembly.GetName().Name;
                        if (name.Contains("Blazor") ||
                            name.Contains("Component") ||
                            name.Contains("UI") ||
                            name.Contains("Controls") ||
                            name.Contains("Web"))
                        {
                            // アセンブリの場所を取得
                            string location = assembly.Location;
                            if (!string.IsNullOrEmpty(location) && File.Exists(location))
                            {
                                // すでに追加されていなければ追加
                                if (!references.Any(r => r.Display == location))
                                {
                                    references.Add(MetadataReference.CreateFromFile(location));
                                    Debug.WriteLine($"実行中アセンブリから参照追加: {name} - {location}");
                                }
                            }
                        }
                    }
                    catch
                    {
                        // 一部のアセンブリは取得できない場合がある
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"実行中アセンブリ参照検索エラー: {ex.Message}");
            }
        }

        private static Project GetProjectHaveOpenedFileInTextEditor()
        {
            // テキストエディタで現在開いているファイルの属するプロジェクトを取得
            var dte = (DTE)Package.GetGlobalService(typeof(DTE));

            if (dte?.ActiveDocument != null)
            {
                // 現在アクティブなドキュメントの完全パスを取得
                string activeFilePath = dte.ActiveDocument.FullName;

                // ソリューション内の全プロジェクトを検索
                foreach (Project project in dte.Solution.Projects)
                {
                    // プロジェクト内のすべての項目をチェック
                    ProjectItems projectItems = project.ProjectItems;
                    if (IsFileInProject(projectItems, activeFilePath))
                    {
                        return project;
                    }
                }
            }

            return null;
        }

        // プロジェクトアイテム内でファイルを再帰的に検索するヘルパーメソッド
        private static bool IsFileInProject(ProjectItems items, string filePath)
        {
            if (items == null) return false;

            foreach (ProjectItem item in items)
            {
                // このアイテムが検索中のファイルかチェック
                if (item.FileCount > 0)
                {
                    try
                    {
                        string itemPath = item.FileNames[0];
                        if (string.Equals(itemPath, filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ファイルパス取得エラー: {ex.Message}");
                        continue;
                    }
                }

                // サブフォルダ内も再帰的に検索
                if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                {
                    if (IsFileInProject(item.ProjectItems, filePath))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Helper method to determine if we're in Debug configuration
        private static bool IsDebugConfiguration()
        {
            try
            {
                // Get the DTE service
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte != null)
                {
                    var configuration = dte.Solution.SolutionBuild.ActiveConfiguration.Name;
                    return configuration.IndexOf("debug", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"構成の判定中にエラーが発生しました: {ex.Message}");
            }

            // Default to Debug if we can't determine
            return true;
        }

        // @usingディレクティブを抽出する
        private static List<string> ExtractUsingDirectives(string razorContent)
        {
            var usingDirectives = new List<string>();
            var regex = new Regex(@"@using\s+([^\s;]+)");
            var matches = regex.Matches(razorContent);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    usingDirectives.Add(match.Groups[1].Value);
                }
            }

            return usingDirectives;
        }

        // @injectディレクティブを抽出する
        private static List<(string type, string name)> ExtractInjectDirectives(string razorContent)
        {
            var injectDirectives = new List<(string type, string name)>();
            var regex = new Regex(@"@inject\s+([^\s]+)\s+([^\s;]+)");
            var matches = regex.Matches(razorContent);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 2)
                {
                    injectDirectives.Add((match.Groups[1].Value, match.Groups[2].Value));
                }
            }

            return injectDirectives;
        }

        // 抽出したusing/injectディレクティブをC#コードに適用
        private static (string code, int adjustedPosition) ApplyDirectivesToCSharpCodeWithPreciseMapping(
    string csharpCode,
    List<string> usingDirectives,
    List<(string type, string name)> injectDirectives,
    int originalPosition)
        {
            var codeBuilder = new StringBuilder();

            // 標準名前空間の追加
            var standardNamespaces = new List<string>
            {
                "System",
                "System.Collections.Generic",
                "System.Linq",
                "System.Threading.Tasks",
                "Microsoft.AspNetCore.Components",
                "Microsoft.AspNetCore.Components.Web"
            };

            foreach (var ns in standardNamespaces)
            {
                codeBuilder.AppendLine($"using {ns};");
            }

            // Blazorウェブ関連の標準名前空間
            codeBuilder.AppendLine("using Microsoft.AspNetCore.Components.Forms;");
            codeBuilder.AppendLine("using Microsoft.AspNetCore.Components.Routing;");
            codeBuilder.AppendLine("using Microsoft.JSInterop;");

            // ユーザー定義の名前空間を追加（_Imports.razorからの名前空間を含む）
            foreach (var usingDirective in usingDirectives)
            {
                codeBuilder.AppendLine($"using {usingDirective};");
            }

            // コンポーネントライブラリの名前空間を推測して追加
            var componentNamespaces = DynamicTypeResolver.GetNamespacesFromUsingDirectives(usingDirectives);
            foreach (var ns in componentNamespaces)
            {
                if (!usingDirectives.Contains(ns))
                {
                    codeBuilder.AppendLine($"using {ns};");
                }
            }

            codeBuilder.AppendLine();

            // 一時的な名前空間とクラスを作成
            codeBuilder.AppendLine("namespace RazorNamespace {");
            codeBuilder.AppendLine("  public class RazorComponent : Microsoft.AspNetCore.Components.ComponentBase {");

            // インジェクトされたサービスをプロパティとして追加
            foreach (var (type, name) in injectDirectives)
            {
                codeBuilder.AppendLine($"    [Inject] private {type} {name} {{ get; set; }}");
            }

            // よく使われるBlazorのサービスも追加
            codeBuilder.AppendLine("    [Inject] private NavigationManager NavigationManager { get; set; }");
            codeBuilder.AppendLine("    [Inject] private IJSRuntime JSRuntime { get; set; }");

            // 可能性のあるコンポーネント参照を追加
            var componentTypes = InferComponentTypes(csharpCode, usingDirectives);
            foreach (var (type, varName) in componentTypes)
            {
                codeBuilder.AppendLine($"    private {type} {varName};");
            }

            // コンポーネントのライフサイクルメソッドのスタブを追加
            codeBuilder.AppendLine("    protected override void OnInitialized() { }");
            codeBuilder.AppendLine("    protected override void OnParametersSet() { }");
            codeBuilder.AppendLine("    protected override bool ShouldRender() => true;");

            // Renderフラグメントのスタブ
            codeBuilder.AppendLine("    private RenderFragment ChildContent => (builder) => { };");

            // ユーザーコードをクラス本体に直接配置
            // インデントの調整（ユーザーコードに既にインデントがある場合は調整）
            string indentedCode = csharpCode;
            //if (!csharpCode.StartsWith("    ")) // すでに適切なインデントがあるかチェック
            //{
            //    indentedCode = csharpCode.Replace(Environment.NewLine, Environment.NewLine + "    ");
            //}
            codeBuilder.AppendLine(indentedCode);

            // クラスと名前空間を閉じる
            codeBuilder.AppendLine("  }");
            codeBuilder.AppendLine("}");

            // 前置コードの長さを計算
            int preambleLength = codeBuilder.ToString().IndexOf(indentedCode);

            // 調整されたポジション = 前置コードの長さ + オリジナルポジション
            // インデントを追加した場合のみ調整
            int indentAdjustment = !csharpCode.StartsWith("    ") ? 4 : 0;
            int adjustedPosition = preambleLength + originalPosition + indentAdjustment;

            // デバッグ情報
            Debug.WriteLine($"生成されたコード（一部）: {codeBuilder.ToString().Substring(0, Math.Min(500, codeBuilder.Length))}");
            Debug.WriteLine($"オリジナルポジション: {originalPosition}, 調整後ポジション: {adjustedPosition}");

            return (codeBuilder.ToString(), adjustedPosition);
        }

        /// <summary>
        /// コード内で参照されている可能性のあるコンポーネント型を推測
        /// </summary>
        private static List<(string type, string varName)> InferComponentTypes(string code, List<string> usingDirectives)
        {
            var result = new List<(string, string)>();

            try
            {
                // よく使われるBlazorコンポーネント名のパターン
                var componentPatterns = new[]
                {
                    @"\b(Select|Table|Grid|Tree|Button|Card|Modal|Dialog|Form|Input|Checkbox|Radio|Dropdown|Menu|Tab|Panel|Layout|ItemsControl|DatePicker|TimePicker|ColorPicker|Chart)\b",
                    @"\b(\w+Component)\b",
                    @"\b(\w+Control)\b"
                };

                // コンポーネント型の抽出
                var foundTypes = new HashSet<string>();
                foreach (var pattern in componentPatterns)
                {
                    var matches = System.Text.RegularExpressions.Regex.Matches(code, pattern);
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        if (match.Groups.Count > 1)
                        {
                            string typeName = match.Groups[1].Value;
                            if (!foundTypes.Contains(typeName))
                            {
                                foundTypes.Add(typeName);
                                // 変数名は先頭を小文字にした型名
                                string varName = char.ToLowerInvariant(typeName[0]) + typeName.Substring(1);
                                result.Add((typeName, varName));
                            }
                        }
                    }
                }

                // ユーザー定義のusing名前空間からも推測
                foreach (var ns in usingDirectives)
                {
                    if (ns.Contains("Blazor") || ns.Contains("Component") || ns.Contains("UI"))
                    {
                        // コンポーネントを含む可能性の高い名前空間から一般的な型を推測
                        var commonNames = new[] { "Select", "Table", "Grid", "Button" };
                        foreach (var name in commonNames)
                        {
                            if (!foundTypes.Contains(name))
                            {
                                foundTypes.Add(name);
                                string varName = char.ToLowerInvariant(name[0]) + name.Substring(1);
                                result.Add((name, varName));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"コンポーネント型推測エラー: {ex.Message}");
            }

            return result;
        }

        // C#コードブロックを抽出し、位置マッピングも行う
        private static (string code, int mappedPosition, int blockStartPosition) ExtractCSharpBlocksWithMapping(
            string razorContent, int originalPosition)
        {
            StringBuilder csharpCode = new StringBuilder();
            int mappedPosition = -1;
            int blockStartPosition = -1;

            // コードブロックの位置をトラッキングするための変数
            int currentOutputPosition = 0;

            // @{ ... } のパターンを検出
            int index = 0;
            while ((index = razorContent.IndexOf("@{", index)) >= 0)
            {
                int braceStart = index + 2;
                int braceCount = 1;
                int braceEnd = braceStart;

                while (braceCount > 0 && braceEnd < razorContent.Length)
                {
                    if (razorContent[braceEnd] == '{') braceCount++;
                    else if (razorContent[braceEnd] == '}') braceCount--;
                    braceEnd++;
                }

                if (braceCount == 0)
                {
                    string blockContent = razorContent.Substring(braceStart, braceEnd - braceStart - 1);

                    // 元の位置がこのブロック内にあるかチェック
                    if (originalPosition > braceStart && originalPosition < braceEnd - 1)
                    {
                        // ブロック内での相対位置を計算
                        int relativePosition = originalPosition - braceStart;
                        mappedPosition = currentOutputPosition + relativePosition;
                        blockStartPosition = braceStart;
                    }

                    csharpCode.AppendLine(blockContent);
                    currentOutputPosition += blockContent.Length + Environment.NewLine.Length;
                }

                index = braceEnd;
            }

            // @code { ... } のパターンを検出（改行を含む可能性を考慮）
            index = 0;
            while ((index = razorContent.IndexOf("@code", index)) >= 0)
            {
                // @code の後の位置を特定
                int codeEnd = index + 5;

                // @code の後にある空白や改行をスキップして { を探す
                int braceIndex = codeEnd;
                while (braceIndex < razorContent.Length &&
                       (char.IsWhiteSpace(razorContent[braceIndex]) ||
                        razorContent[braceIndex] == '\r' ||
                        razorContent[braceIndex] == '\n'))
                {
                    braceIndex++;
                }

                // 次の文字が { であるかチェック
                if (braceIndex < razorContent.Length && razorContent[braceIndex] == '{')
                {
                    int braceStart = braceIndex + 1;
                    int braceCount = 1;
                    int braceEnd = braceStart;

                    while (braceCount > 0 && braceEnd < razorContent.Length)
                    {
                        if (razorContent[braceEnd] == '{') braceCount++;
                        else if (razorContent[braceEnd] == '}') braceCount--;
                        braceEnd++;
                    }

                    if (braceCount == 0)
                    {
                        string blockContent = razorContent.Substring(braceStart, braceEnd - braceStart - 1);

                        // 元の位置がこのブロック内にあるかチェック
                        if (originalPosition > braceStart && originalPosition < braceEnd - 1)
                        {
                            // ブロック内での相対位置を計算
                            int relativePosition = originalPosition - braceStart;
                            mappedPosition = currentOutputPosition + relativePosition;
                            blockStartPosition = braceStart;
                        }

                        csharpCode.AppendLine(blockContent);
                        currentOutputPosition += blockContent.Length + Environment.NewLine.Length;
                    }

                    index = braceEnd;
                }
                else
                {
                    // { が見つからない場合は次の @code を探す
                    index = codeEnd;
                }
            }

            return (csharpCode.ToString(), mappedPosition, blockStartPosition);
        }

        /// <summary>
        ///     型の継承階層を取得
        /// </summary>
        public static async Task<TypeHierarchyInfo> GetTypeHierarchyAsync(ITypeSymbol typeSymbol, Document document,
            bool includeInternalTypes = true, bool showUseSpecialTypes = false)
        {
            try
            {
                if (typeSymbol == null || document == null)
                {
                    return null;
                }

                var solution = document.Project.Solution;
                var compilation = await document.Project.GetCompilationAsync();

                // 型情報の作成
                var typeInfo = CreateTypeHierarchyInfo(typeSymbol, showUseSpecialTypes);

                // ベースクラスを取得
                if (typeSymbol.BaseType != null)
                {
                    typeInfo.BaseClass = CreateTypeHierarchyInfo(typeSymbol.BaseType, showUseSpecialTypes);
                }

                // 実装インターフェースを取得
                foreach (var interfaceSymbol in typeSymbol.AllInterfaces)
                {
                    // アクセス可能なインターフェースのみ（必要に応じて調整）
                    if (includeInternalTypes || interfaceSymbol.DeclaredAccessibility == Accessibility.Public)
                    {
                        typeInfo.Interfaces.Add(CreateTypeHierarchyInfo(interfaceSymbol, showUseSpecialTypes));
                    }
                }

                // この型から派生した型を探す (ソリューション内)
                await FindDerivedTypesAsync(typeSymbol, typeInfo, solution, includeInternalTypes, showUseSpecialTypes);

                return typeInfo;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetTypeHierarchyAsync: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        ///     派生型を検索
        /// </summary>
        private static async Task FindDerivedTypesAsync(ITypeSymbol baseType, TypeHierarchyInfo typeInfo,
            Microsoft.CodeAnalysis.Solution solution, bool includeInternalTypes, bool showUseSpecialTypes = false)
        {
            try
            {
                // クラス型の場合のみFindDerivedClassesAsyncを使用
                if (baseType is INamedTypeSymbol namedTypeSymbol && baseType.TypeKind == TypeKind.Class)
                {
                    // DerivedClassFinder APIを使用して派生型を検索
                    var derivedTypes = await SymbolFinder.FindDerivedClassesAsync(namedTypeSymbol, solution);
                    foreach (var derivedType in derivedTypes)
                    {
                        // アクセス可能な派生型のみ
                        if (includeInternalTypes || derivedType.DeclaredAccessibility == Accessibility.Public)
                        {
                            typeInfo.DerivedClasses.Add(CreateTypeHierarchyInfo(derivedType, showUseSpecialTypes));
                        }
                    }
                }

                // インターフェースの場合は実装クラスを検索
                if (baseType.TypeKind == TypeKind.Interface && baseType is INamedTypeSymbol interfaceSymbol)
                {
                    var implementingTypes = await SymbolFinder.FindImplementationsAsync(interfaceSymbol, solution);
                    foreach (var implementingType in implementingTypes)
                    {
                        if (implementingType is ITypeSymbol implType)
                        {
                            // すでに追加されている派生型と重複しないようにする
                            if (!typeInfo.DerivedClasses.Any(t => t.FullName == implType.ToDisplayString()) &&
                                (includeInternalTypes || implType.DeclaredAccessibility == Accessibility.Public))
                            {
                                typeInfo.DerivedClasses.Add(CreateTypeHierarchyInfo(implType, showUseSpecialTypes));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in FindDerivedTypesAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// 型シンボルから型階層情報を作成
        /// </summary>
        private static TypeHierarchyInfo CreateTypeHierarchyInfo(ITypeSymbol typeSymbol, bool showUseSpecialTypes, ITypeSymbol originalTypeSymbol = null)
        {
            string displayName;

            if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                // 元の型がジェネリック型であるかを確認
                INamedTypeSymbol originalNamedType = originalTypeSymbol as INamedTypeSymbol;
                bool canReplaceTypeParams = originalNamedType != null &&
                                         originalNamedType.IsGenericType &&
                                         namedType.TypeParameters.Length == originalNamedType.TypeArguments.Length;

                if (canReplaceTypeParams)
                {
                    try
                    {
                        // 基本型名（ジェネリックパラメータなし）を取得
                        string baseName = namedType.Name;

                        // 型パラメータを元の型から取得
                        var typeArgs = string.Join(", ", originalNamedType.TypeArguments.Select(arg =>
                            showUseSpecialTypes
                                ? arg.ToDisplayString(new SymbolDisplayFormat(miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes))
                                : arg.ToDisplayString()
                        ));

                        // 最終的な表示名を作成 (例: IReadOnlyCollection<string>)
                        displayName = $"{baseName}<{typeArgs}>";
                    }
                    catch (Exception ex)
                    {
                        // エラーが発生した場合は標準のフォーマットを使用
                        Debug.WriteLine($"Error replacing type parameters: {ex.Message}");
                        displayName = GetDefaultDisplayName(namedType, showUseSpecialTypes);
                    }
                }
                else
                {
                    // 型パラメータの数が一致しない場合は標準のフォーマットを使用
                    displayName = GetDefaultDisplayName(namedType, showUseSpecialTypes);
                }
            }
            else
            {
                displayName = typeSymbol.Name;
            }

            // FullNameも同じ表記規則を適用
            string fullName;
            if (showUseSpecialTypes)
            {
                fullName = typeSymbol.ToDisplayString(
                    new SymbolDisplayFormat(miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes));
            }
            else
            {
                fullName = typeSymbol.ToDisplayString();
            }

            var typeInfo = new TypeHierarchyInfo
            {
                DisplayName = displayName,
                FullName = fullName,
                IsInterface = typeSymbol.TypeKind == TypeKind.Interface,
                Accessibility = typeSymbol.DeclaredAccessibility.ToString(),
                IsDefinedInSolution = !typeSymbol.Locations.All(loc => loc.IsInMetadata),
                AssemblyName = typeSymbol.ContainingAssembly?.Name,
                RequiredNamespace = typeSymbol.ContainingNamespace?.ToDisplayString()
            };

            return typeInfo;
        }

        // 標準のフォーマットで型名を取得するヘルパーメソッド
        private static string GetDefaultDisplayName(INamedTypeSymbol namedType, bool showUseSpecialTypes)
        {
            var format = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

            if (showUseSpecialTypes)
            {
                format = format.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
            }

            return namedType.ToDisplayString(format);
        }

        /// <summary>
        /// 型パラメータがプリミティブ型表記かどうかを判断
        /// </summary>
        private static bool DeterminePrimitiveTypeUsage(string actualTypeText)
        {
            // プリミティブ型の対応表（C#プリミティブ型と.NET型）
            var primitiveTypes = new Dictionary<string, string>
            {
                { "int", "Int32" },
                { "long", "Int64" },
                { "float", "Single" },
                { "double", "Double" },
                { "bool", "Boolean" },
                { "string", "String" },
                { "char", "Char" },
                { "byte", "Byte" },
                { "sbyte", "SByte" },
                { "short", "Int16" },
                { "ushort", "UInt16" },
                { "uint", "UInt32" },
                { "ulong", "UInt64" },
                { "decimal", "Decimal" },
                { "object", "Object" }
            };

            // まず、プリミティブ型（int など）が含まれているかチェック
            foreach (var primitiveType in primitiveTypes.Keys)
            {
                // ジェネリック型パラメータとして現れる可能性のあるパターン
                if (actualTypeText.Contains($"<{primitiveType}>") ||
                    actualTypeText.Contains($"<{primitiveType},") ||
                    actualTypeText.Contains($", {primitiveType}>") ||
                    actualTypeText.Contains($", {primitiveType},"))
                {
                    return true; // プリミティブ型表記を使用
                }
            }

            // 次に、.NET型（Int32 など）が含まれているかチェック（名前空間を省略した短い名前）
            foreach (var netType in primitiveTypes.Values)
            {
                if (actualTypeText.Contains($"<{netType}>") ||
                    actualTypeText.Contains($"<{netType},") ||
                    actualTypeText.Contains($", {netType}>") ||
                    actualTypeText.Contains($", {netType},"))
                {
                    return false; // .NET型表記を使用
                }
            }

            // デフォルトでは一般的なC#コードの慣習に従い、プリミティブ型表記を使用
            return true;
        }

        /// <summary>
        /// 型パラメータが文字列内に存在するかチェック
        /// </summary>
        private static bool IsTypeParamInString(string typeString, string paramType)
        {
            // ジェネリック型パラメータとして現れる可能性のあるパターン
            return typeString.Contains($"<{paramType}>") ||
                   typeString.Contains($"<{paramType},") ||
                   typeString.Contains($", {paramType}>") ||
                   typeString.Contains($", {paramType},");
        }

        /// <summary>
        ///     継承階層を含めた型置換候補を取得
        /// </summary>
        public static async Task<List<TypeHierarchyInfo>> GetTypeReplacementCandidatesAsync(
    ITypeSymbol originalType, Document document, bool includeBaseTypes = true, bool includeDerivedTypes = true, bool includeRelatedTypes = true, bool showUseSpecialTypes = false)
        {
            var candidates = new List<TypeHierarchyInfo>();
            var compilation = await document.Project.GetCompilationAsync();

            // 元の型の階層情報を取得
            var typeHierarchy = await GetTypeHierarchyAsync(originalType, document, showUseSpecialTypes: showUseSpecialTypes);
            if (typeHierarchy == null)
            {
                return candidates;
            }

            // 現在の型自体も候補に含める
            candidates.Add(typeHierarchy);

            // ベース型とインターフェースを追加（抽象化）
            if (includeBaseTypes)
            {
                // ベースクラスを追加
                var baseClass = typeHierarchy.BaseClass;
                while (baseClass != null && baseClass.FullName != "object")
                {
                    candidates.Add(baseClass);
                    baseClass = baseClass.BaseClass;
                }

                // 実装インターフェースを追加
                foreach (var iface in typeHierarchy.Interfaces)
                {
                    candidates.Add(iface);
                }
            }

            // 派生型を追加（具象化）
            if (includeDerivedTypes)
            {
                foreach (var derived in typeHierarchy.DerivedClasses)
                {
                    candidates.Add(derived);
                }
            }

            // アセンブリ内の類似インターフェースを検索（新機能）
            if (originalType.TypeKind == TypeKind.Interface)
            {
                // 元のインターフェース名からパターンを作成 (例: ICollection -> I*Collection*)
                string namePattern = originalType.Name;
                if (namePattern.StartsWith("I"))
                {
                    namePattern = namePattern.Substring(1); // "I" を削除
                }

                // アセンブリ内のすべての型をチェック
                foreach (var assembly in compilation.References.Select(r => compilation.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol))
                {
                    if (assembly == null) continue;

                    // 名前空間を再帰的に探索
                    SearchForSimilarInterfaces(assembly.GlobalNamespace, namePattern, candidates, originalType, showUseSpecialTypes);
                }
            }

            // 名前の類似性に基づいて追加の関連型を検索
            var typeName = originalType.Name;
            if (originalType.TypeKind == TypeKind.Interface && typeName.StartsWith("I"))
            {
                // 名前のパターンを作成
                var baseName = typeName.Substring(1); // "I" を削除
                var patternPairs = new Dictionary<string, string>
                {
                    { "Data", "ReadOnlyData" },
                    { "Mutable", "Immutable" },
                    { "", "ReadOnly" },
                    { "Collection", "ReadOnlyCollection" },
                    { "List", "ReadOnlyList" },
                    { "Dictionary", "ReadOnlyDictionary" },
                    // 他のパターンも追加できます
                };

                // コンパイルしているプロジェクト内のすべての型シンボルを取得
                var allTypes = new List<INamedTypeSymbol>();
                CollectAllTypes(compilation.GlobalNamespace, allTypes);

                // パターンに基づいて候補を検索
                foreach (var pair in patternPairs)
                {
                    if (baseName.Contains(pair.Key))
                    {
                        string targetName;
                        if (string.IsNullOrEmpty(pair.Key))
                        {
                            // 空キーの場合は、頭に接頭辞を追加
                            targetName = "I" + pair.Value + baseName;
                        }
                        else
                        {
                            // 通常の置換
                            targetName = "I" + baseName.Replace(pair.Key, pair.Value);
                        }
                        var matchingTypes = allTypes.Where(t =>
                            t.Name == targetName &&
                            t.TypeKind == TypeKind.Interface &&
                            !candidates.Any(c => c.FullName == t.ToDisplayString()));

                        foreach (var type in matchingTypes)
                        {
                            candidates.Add(CreateTypeHierarchyInfo(type, showUseSpecialTypes, originalType));
                            Debug.WriteLine($"Added pattern-matched type: {type.ToDisplayString()}");
                        }
                    }
                }
            }

            if (includeRelatedTypes)
            {
                // Get related types by naming pattern, common usages, etc.
                var relatedTypes = await FindRelatedTypesAsync(originalType, document, compilation);
                foreach (var relatedType in relatedTypes)
                {
                    // Only add if not already in the list
                    if (!candidates.Any(c => c.FullName == relatedType.ToDisplayString()))
                    {
                        candidates.Add(CreateTypeHierarchyInfo(relatedType, showUseSpecialTypes));
                    }
                }
            }

            return candidates;
        }

        private static async Task<List<ITypeSymbol>> FindRelatedTypesAsync(ITypeSymbol originalType, Document document, Compilation compilation)
        {
            var relatedTypes = new List<ITypeSymbol>();

            // Find types with similar names
            string typeName = originalType.Name;
            string searchPattern = typeName;

            // For interface types, try finding implementation classes
            if (originalType.TypeKind == TypeKind.Interface)
            {
                // Look for implementation classes
                var implementingTypes = await SymbolFinder.FindImplementationsAsync(originalType as INamedTypeSymbol, document.Project.Solution);
                foreach (var implementingType in implementingTypes)
                {
                    if (implementingType is ITypeSymbol typeSymbol)
                    {
                        relatedTypes.Add(typeSymbol);
                    }
                }

                // For interfaces with "I" prefix, look for similar names without the "I"
                if (typeName.StartsWith("I") && typeName.Length > 1)
                {
                    searchPattern = typeName.Substring(1);
                }
            }

            // Search for types with similar names in all referenced assemblies
            foreach (var assembly in compilation.References.Select(r => compilation.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol))
            {
                if (assembly != null)
                {
                    SearchForSimilarTypes(assembly.GlobalNamespace, searchPattern, relatedTypes, originalType);
                }
            }

            return relatedTypes;
        }

        private static void SearchForSimilarTypes(INamespaceSymbol ns, string pattern, List<ITypeSymbol> relatedTypes, ITypeSymbol originalType)
        {
            foreach (var member in ns.GetMembers())
            {
                if (member is ITypeSymbol typeSymbol &&
                    !SymbolEqualityComparer.Default.Equals(typeSymbol, originalType) &&
                    typeSymbol.Name.Contains(pattern))
                {
                    relatedTypes.Add(typeSymbol);
                }
                else if (member is INamespaceSymbol subNamespace)
                {
                    SearchForSimilarTypes(subNamespace, pattern, relatedTypes, originalType);
                }
            }
        }

        // すべての型を収集するヘルパーメソッド
        private static void CollectAllTypes(INamespaceSymbol ns, List<INamedTypeSymbol> types)
        {
            foreach (var member in ns.GetMembers())
            {
                if (member is INamedTypeSymbol type)
                {
                    types.Add(type);
                }
                else if (member is INamespaceSymbol childNs)
                {
                    CollectAllTypes(childNs, types);
                }
            }
        }

        private static void SearchForSimilarInterfaces(INamespaceSymbol ns, string pattern, List<TypeHierarchyInfo> candidates,
            ITypeSymbol originalType, bool showUseSpecialTypes)
        {
            // 現在の名前空間内のすべてのメンバーを検索
            foreach (var member in ns.GetMembers())
            {
                // インターフェースの場合
                if (member is INamedTypeSymbol typeSymbol && typeSymbol.TypeKind == TypeKind.Interface)
                {
                    string name = typeSymbol.Name;

                    // 元のインターフェースと同じでない、かつ名前が似ている場合
                    if (!SymbolEqualityComparer.Default.Equals(typeSymbol, originalType) &&
                        name.Contains(pattern))
                    {
                        // 既に追加済みでない場合は追加
                        if (!candidates.Any(c => c.FullName == typeSymbol.ToDisplayString()))
                        {
                            candidates.Add(CreateTypeHierarchyInfo(typeSymbol, showUseSpecialTypes));
                        }
                    }
                }
                // サブ名前空間を再帰的に探索
                else if (member is INamespaceSymbol subNamespace)
                {
                    SearchForSimilarInterfaces(subNamespace, pattern, candidates, originalType, showUseSpecialTypes);
                }
            }
        }

        /// <summary>
        ///     型階層情報を格納するクラス
        /// </summary>
        public class TypeHierarchyInfo
        {
            /// <summary>
            ///     型名（表示用）
            /// </summary>
            public string DisplayName { get; set; }

            /// <summary>
            ///     型の完全修飾名
            /// </summary>
            public string FullName { get; set; }

            /// <summary>
            ///     継承ベースクラス
            /// </summary>
            public TypeHierarchyInfo BaseClass { get; set; }

            /// <summary>
            ///     実装インターフェースのリスト
            /// </summary>
            public List<TypeHierarchyInfo> Interfaces { get; set; } = new List<TypeHierarchyInfo>();

            /// <summary>
            ///     この型を継承するサブクラスのリスト
            /// </summary>
            public List<TypeHierarchyInfo> DerivedClasses { get; set; } = new List<TypeHierarchyInfo>();

            /// <summary>
            ///     この型はインターフェースか
            /// </summary>
            public bool IsInterface { get; set; }

            /// <summary>
            ///     アクセシビリティ（public, internal, private等）
            /// </summary>
            public string Accessibility { get; set; }

            /// <summary>
            ///     ソリューション内で定義されている型か
            /// </summary>
            public bool IsDefinedInSolution { get; set; }

            /// <summary>
            ///     アセンブリ名
            /// </summary>
            public string AssemblyName { get; set; }

            /// <summary>
            ///     この型を使用する場合に必要な using ステートメント
            /// </summary>
            public string RequiredNamespace { get; set; }
        }
    }
}