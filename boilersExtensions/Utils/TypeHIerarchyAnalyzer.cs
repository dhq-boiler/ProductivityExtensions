using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;

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
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (document == null)
                {
                    return (null, null, default, null);
                }

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null)
                {
                    return (null, null, default, null);
                }

                var syntaxRoot = await document.GetSyntaxRootAsync();
                if (syntaxRoot == null)
                {
                    return (null, null, default, null);
                }

                // カーソル位置のノードを取得
                var node = syntaxRoot.FindNode(new TextSpan(position, 0), getInnermostNodeForTie: true);
                if (node == null)
                {
                    return (null, null, default, null);
                }

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
                }
                // ジェネリック型制約
                else if (node.Parent is TypeParameterConstraintSyntax)
                {
                    typeSyntax = node.Parent.ChildNodes().OfType<TypeSyntax>().FirstOrDefault();
                    parentNode = node.Parent;
                }

                if (typeSyntax == null)
                {
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

                    // デバッグ情報出力
                    Debug.WriteLine($"Generic type: {genericName}, Base name: {genericName.Identifier.Text}, " +
                                    $"Identifier Span: ({genericName.Identifier.Span.Start}, {genericName.Identifier.Span.Length}), " +
                                    $"Full Span: ({genericName.Span.Start}, {genericName.Span.Length})");
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
                var typeSymbol = semanticModel.GetSymbolInfo(typeSyntax).Symbol as ITypeSymbol;
                return (typeSymbol, parentNode, fullTypeSpan, baseTypeSpan);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetTypeSymbolAtPositionAsync: {ex.Message}");
                return (null, null, default, null);
            }
        }

        /// <summary>
        ///     型の継承階層を取得
        /// </summary>
        public static async Task<TypeHierarchyInfo> GetTypeHierarchyAsync(ITypeSymbol typeSymbol, Document document,
            bool includeInternalTypes = true)
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
                var typeInfo = CreateTypeHierarchyInfo(typeSymbol);

                // ベースクラスを取得
                if (typeSymbol.BaseType != null)
                {
                    typeInfo.BaseClass = CreateTypeHierarchyInfo(typeSymbol.BaseType);
                }

                // 実装インターフェースを取得
                foreach (var interfaceSymbol in typeSymbol.AllInterfaces)
                {
                    // アクセス可能なインターフェースのみ（必要に応じて調整）
                    if (includeInternalTypes || interfaceSymbol.DeclaredAccessibility == Accessibility.Public)
                    {
                        typeInfo.Interfaces.Add(CreateTypeHierarchyInfo(interfaceSymbol));
                    }
                }

                // この型から派生した型を探す (ソリューション内)
                await FindDerivedTypesAsync(typeSymbol, typeInfo, solution, includeInternalTypes);

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
            Solution solution, bool includeInternalTypes)
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
                            typeInfo.DerivedClasses.Add(CreateTypeHierarchyInfo(derivedType));
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
                                typeInfo.DerivedClasses.Add(CreateTypeHierarchyInfo(implType));
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
        ///     型シンボルから型階層情報を作成
        /// </summary>
        private static TypeHierarchyInfo CreateTypeHierarchyInfo(ITypeSymbol typeSymbol)
        {
            var typeInfo = new TypeHierarchyInfo
            {
                DisplayName = typeSymbol.Name,
                FullName = typeSymbol.ToDisplayString(),
                IsInterface = typeSymbol.TypeKind == TypeKind.Interface,
                Accessibility = typeSymbol.DeclaredAccessibility.ToString(),
                IsDefinedInSolution = !typeSymbol.Locations.All(loc => loc.IsInMetadata),
                AssemblyName = typeSymbol.ContainingAssembly?.Name,
                RequiredNamespace = typeSymbol.ContainingNamespace?.ToDisplayString()
            };

            return typeInfo;
        }

        /// <summary>
        ///     継承階層を含めた型置換候補を取得
        /// </summary>
        public static async Task<List<TypeHierarchyInfo>> GetTypeReplacementCandidatesAsync(
            ITypeSymbol originalType, Document document, bool includeBaseTypes = true, bool includeDerivedTypes = true)
        {
            var candidates = new List<TypeHierarchyInfo>();

            // 元の型の階層情報を取得
            var typeHierarchy = await GetTypeHierarchyAsync(originalType, document);
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

            return candidates;
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