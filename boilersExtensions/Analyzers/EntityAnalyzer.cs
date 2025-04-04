﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using boilersExtensions.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace boilersExtensions.Analyzers
{
    /// <summary>
    ///     Entity Frameworkのエンティティクラスを解析するクラス
    /// </summary>
    public class EntityAnalyzer
    {
        // DbContextクラス名の一般的なサフィックス
        private static readonly string[] DbContextSuffixes = { "DbContext", "Context", "DataContext" };

        // 解析済みエンティティのキャッシュ
        private readonly Dictionary<string, EntityInfo> _entityCache = new Dictionary<string, EntityInfo>();

        /// <summary>
        ///     ドキュメント内のすべてのエンティティクラスを解析します
        /// </summary>
        /// <param name="document">解析対象のドキュメント</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>検出されたエンティティ情報のリスト</returns>
        public async Task<List<EntityInfo>> AnalyzeEntitiesAsync(Document document,
            CancellationToken cancellationToken = default)
        {
            var result = new List<EntityInfo>();
            // ファイルパスをキャッシュキーとして使用
            var filePath = document.FilePath;
            if (filePath != null && _entityCache.ContainsKey(filePath))
            {
                return new List<EntityInfo> { _entityCache[filePath] };
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null)
            {
                return result;
            }

            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            if (syntaxRoot == null)
            {
                return result;
            }

            // TypeDeclarationSyntax を使用して class と record の両方を取得
            var typeDeclarations = syntaxRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Where(t => t is ClassDeclarationSyntax || t is RecordDeclarationSyntax);

            foreach (var typeDecl in typeDeclarations)
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
                if (typeSymbol == null)
                {
                    continue;
                }

                // エンティティクラスかどうかを判定
                if (await IsEntityClass(document, typeSymbol))
                {
                    // エンティティ情報を抽出
                    var entityInfo = await ExtractEntityInfoAsync(typeSymbol, typeDecl, document, semanticModel,
                        cancellationToken);
                    if (entityInfo != null)
                    {
                        result.Add(entityInfo);
                        // キャッシュに追加
                        if (filePath != null)
                        {
                            _entityCache[filePath] = entityInfo;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        ///     解析対象のソリューション内でDbContextクラスを検索します
        /// </summary>
        /// <param name="solution">解析対象のソリューション</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>DbContextクラスの情報</returns>
        public async Task<List<DbContextInfo>> FindDbContextsInSolutionAsync(Solution solution,
            CancellationToken cancellationToken = default)
        {
            var result = new List<DbContextInfo>();

            // ソリューション内のすべてのプロジェクトを処理
            foreach (var project in solution.Projects)
            {
                // プロジェクトがEF Coreを参照しているかチェック
                if (!await ProjectUsesEntityFrameworkAsync(project, cancellationToken))
                {
                    continue;
                }

                // プロジェクト内のすべてのドキュメントを処理
                foreach (var document in project.Documents)
                {
                    var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                    if (semanticModel == null)
                    {
                        continue;
                    }

                    var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                    if (syntaxRoot == null)
                    {
                        continue;
                    }

                    // クラス宣言を検索
                    var classDeclarations = syntaxRoot.DescendantNodes().OfType<ClassDeclarationSyntax>();

                    foreach (var classDecl in classDeclarations)
                    {
                        // クラスシンボルを取得
                        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
                        if (classSymbol == null)
                        {
                            continue;
                        }

                        // DbContextクラスかどうかを判定
                        if (IsDbContextClass(classSymbol))
                        {
                            // DbContext情報を抽出
                            var contextInfo = ExtractDbContextInfo(classSymbol, classDecl, document, semanticModel);
                            if (contextInfo != null)
                            {
                                result.Add(contextInfo);
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        ///     クラスシンボルからエンティティ情報を抽出します（基底クラスのプロパティも含む）
        /// </summary>
        private async Task<EntityInfo> ExtractEntityInfoAsync(
            INamedTypeSymbol typeSymbol,
            TypeDeclarationSyntax typeDecl,
            Document document,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var entityInfo = new EntityInfo
            {
                Name = typeSymbol.Name,
                FullName = typeSymbol.ToDisplayString(),
                Namespace = typeSymbol.ContainingNamespace.ToDisplayString(),
                Document = document,
                Symbol = typeSymbol,
                IsAbstract = typeSymbol.IsAbstract,
                TableName = GetTableName(typeSymbol)
            };

            // 継承情報を抽出
            if (typeSymbol.BaseType != null && typeSymbol.BaseType.Name != "Object")
            {
                entityInfo.UsesInheritance = true;
                entityInfo.BaseTypeName = typeSymbol.BaseType.Name;
            }

            // プロパティを抽出（基底クラスも含めて再帰的に）
            await ExtractPropertiesRecursivelyAsync(entityInfo, typeSymbol, semanticModel, new HashSet<string>(),
                cancellationToken);

            // リレーションシップを抽出
            await ExtractRelationshipsAsync(entityInfo, typeSymbol, semanticModel, cancellationToken);

            // DbSetプロパティ名を検索（親DbContextから）
            entityInfo.DbSetName =
                await FindDbSetPropertyNameAsync(typeSymbol, document.Project.Solution, cancellationToken);

            return entityInfo;
        }

        /// <summary>
        ///     再帰的にクラス階層からプロパティを抽出します
        /// </summary>
        private async Task ExtractPropertiesRecursivelyAsync(
            EntityInfo entityInfo,
            INamedTypeSymbol typeSymbol,
            SemanticModel semanticModel,
            HashSet<string> processedTypes,
            CancellationToken cancellationToken)
        {
            // 型が既に処理済みの場合はスキップ（循環参照防止）
            if (processedTypes.Contains(typeSymbol.ToDisplayString()))
            {
                return;
            }

            // この型を処理済みとしてマーク
            processedTypes.Add(typeSymbol.ToDisplayString());

            // 現在のクラスのプロパティを抽出
            foreach (var member in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                // 静的プロパティや自動実装でないプロパティ, publicでないプロパティ, setterがpublicでないプロパティはスキップ
                if (member.IsStatic || !member.IsAutoProperty()
                                    || member.DeclaredAccessibility != Accessibility.Public
                                    || member.SetMethod is null
                                    || member.SetMethod.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                // 既に同名のプロパティが追加されていないか確認（派生クラスが優先）
                if (entityInfo.Properties.All(p => p.Name != member.Name))
                {
                    var propertyInfo = ExtractPropertyInfo(member, semanticModel);

                    // 基底クラスから継承したプロパティであることを記録
                    if (typeSymbol != entityInfo.Symbol)
                    {
                        propertyInfo.Description = string.IsNullOrEmpty(propertyInfo.Description)
                            ? $"Inherited from {typeSymbol.Name}"
                            : $"{propertyInfo.Description} (Inherited from {typeSymbol.Name})";
                    }

                    entityInfo.Properties.Add(propertyInfo);
                }
            }

            // 基底クラスが存在し、Objectでない場合は再帰的に処理
            if (typeSymbol.BaseType != null && typeSymbol.BaseType.Name != "Object")
            {
                await ExtractPropertiesRecursivelyAsync(
                    entityInfo,
                    typeSymbol.BaseType,
                    semanticModel,
                    processedTypes,
                    cancellationToken);
            }

            //// インターフェースからのプロパティも抽出（オプション）
            //if (entityInfo.IncludeInterfaceProperties)
            //{
            //    foreach (var implementedInterface in typeSymbol.AllInterfaces)
            //    {
            //        await ExtractPropertiesRecursivelyAsync(
            //            entityInfo,
            //            implementedInterface,
            //            semanticModel,
            //            processedTypes,
            //            cancellationToken);
            //    }
            //}
        }

        /// <summary>
        ///     プロパティシンボルからプロパティ情報を抽出します
        /// </summary>
        private PropertyInfo ExtractPropertyInfo(IPropertySymbol propertySymbol, SemanticModel semanticModel)
        {
            var propertyInfo = new PropertyInfo
            {
                Name = propertySymbol.Name,
                TypeName = propertySymbol.Type.Name,
                FullTypeName = propertySymbol.Type.ToDisplayString(),
                TypeSymbol = propertySymbol.Type,
                Symbol = propertySymbol,
                ColumnName = propertySymbol.Name, // デフォルトはプロパティ名と同じ
                IsNavigationProperty = IsNavigationProperty(propertySymbol),
                IsRequired = IsRequired(propertySymbol)
            };

            // 型に関する追加情報を設定
            SetPropertyTypeInfo(propertyInfo, propertySymbol);

            // 属性情報を抽出
            ExtractAttributeInfo(propertyInfo, propertySymbol);

            // キー情報を設定
            SetKeyInfo(propertyInfo, propertySymbol);

            // 外部キー情報を設定
            SetForeignKeyInfo(propertyInfo, propertySymbol);

            return propertyInfo;
        }

        /// <summary>
        ///     プロパティが主キーかどうかを判定します
        /// </summary>
        private void SetKeyInfo(PropertyInfo propertyInfo, IPropertySymbol propertySymbol)
        {
            // Key属性があるか確認
            propertyInfo.IsKey = HasAttribute(propertySymbol, "Key") ||
                                 propertySymbol.Name == "Id" ||
                                 propertySymbol.Name == $"{propertySymbol.ContainingType.Name}Id";

            // 主キーのAutoGeneratedの設定
            if (propertyInfo.IsKey)
            {
                // DatabaseGeneratedAttribute を確認
                foreach (var attribute in propertySymbol.GetAttributes())
                {
                    if (attribute.AttributeClass.Name == "DatabaseGenerated" ||
                        attribute.AttributeClass.Name == "DatabaseGeneratedAttribute")
                    {
                        if (attribute.ConstructorArguments.Length > 0 &&
                            attribute.ConstructorArguments[0].Value is int genOption)
                        {
                            // Identity(1)またはComputed(2)の場合は自動生成
                            propertyInfo.IsAutoGenerated = genOption == 1 || genOption == 2;
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     プロパティの型情報を設定します
        /// </summary>
        private void SetPropertyTypeInfo(PropertyInfo propertyInfo, IPropertySymbol propertySymbol)
        {
            // Enum型チェック
            if (propertySymbol.Type.TypeKind == TypeKind.Enum)
            {
                propertyInfo.IsEnum = true;
                // Enum値の抽出
                ExtractEnumValues(propertyInfo, propertySymbol.Type as INamedTypeSymbol);
            }

            // Nullable型チェック
            if (propertySymbol.Type.Name == "Nullable" && propertySymbol.Type is INamedTypeSymbol namedType)
            {
                propertyInfo.IsNullable = true;
                if (namedType.TypeArguments.Length > 0)
                {
                    propertyInfo.UnderlyingTypeName = namedType.TypeArguments[0].Name;
                    // FullTypeNameをNullable<T>の形式で保存
                    propertyInfo.FullTypeName = $"System.Nullable<{namedType.TypeArguments[0].ToDisplayString()}>";
                }
            }

            // コレクション型チェック
            if (IsCollectionType(propertySymbol.Type) && propertySymbol.Type is INamedTypeSymbol collType)
            {
                propertyInfo.IsCollection = true;
                if (collType.TypeArguments.Length > 0)
                {
                    propertyInfo.CollectionElementType = collType.TypeArguments[0].Name;
                }
            }

            // システム型かどうか
            propertyInfo.IsSystemType = propertySymbol.Type.ContainingNamespace?.ToDisplayString()
                .StartsWith("System") == true;
        }

        /// <summary>
        ///     プロパティが外部キーかどうかを判定し、情報を設定します
        /// </summary>
        private void SetForeignKeyInfo(PropertyInfo propertyInfo, IPropertySymbol propertySymbol)
        {
            // ForeignKey属性があるか確認
            foreach (var attribute in propertySymbol.GetAttributes())
            {
                if (attribute.AttributeClass.Name == "ForeignKey" ||
                    attribute.AttributeClass.Name == "ForeignKeyAttribute")
                {
                    propertyInfo.IsForeignKey = true;

                    if (attribute.ConstructorArguments.Length > 0)
                    {
                        var navPropName = attribute.ConstructorArguments[0].Value?.ToString();
                        // 関連するタイプを探す
                        foreach (var member in propertySymbol.ContainingType.GetMembers().OfType<IPropertySymbol>())
                        {
                            if (member.Name == navPropName && IsNavigationProperty(member))
                            {
                                propertyInfo.ForeignKeyTargetEntity = member.Type.Name;
                                propertyInfo.ForeignKeyTargetProperty = "Id"; // 通常はId
                                break;
                            }
                        }
                    }
                }
            }

            // 名前に基づく外部キーの推測
            if (!propertyInfo.IsForeignKey && propertyInfo.Name.EndsWith("Id") &&
                propertyInfo.Name != "Id" && !propertyInfo.IsKey)
            {
                propertyInfo.IsForeignKey = true;
                var targetEntityName = propertyInfo.Name.Substring(0, propertyInfo.Name.Length - 2);
                propertyInfo.ForeignKeyTargetEntity = targetEntityName;
                propertyInfo.ForeignKeyTargetProperty = "Id";
            }
        }

        /// <summary>
        ///     プロパティが必須かどうかを判定します
        /// </summary>
        private bool IsRequired(IPropertySymbol propertySymbol)
        {
            // Required属性があるか確認
            if (HasAttribute(propertySymbol, "Required"))
            {
                return true;
            }

            // 非Nullable値型は必須
            if (propertySymbol.Type.IsValueType && !IsNullableType(propertySymbol.Type))
            {
                return true;
            }

            // 非Nullable参照型は必須（C# 8.0以降の機能）
            if (!propertySymbol.Type.IsValueType &&
                propertySymbol.NullableAnnotation == NullableAnnotation.NotAnnotated)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///     型がNullable型かどうかを判定します
        /// </summary>
        private bool IsNullableType(ITypeSymbol type) => type.Name == "Nullable" &&
                                                         type is INamedTypeSymbol namedType &&
                                                         namedType.TypeArguments.Length > 0;

        /// <summary>
        ///     プロパティの属性情報を抽出します
        /// </summary>
        private void ExtractAttributeInfo(PropertyInfo propertyInfo, IPropertySymbol propertySymbol)
        {
            foreach (var attribute in propertySymbol.GetAttributes())
            {
                var attrName = attribute.AttributeClass.Name;

                // MaxLength/StringLength属性
                if (attrName == "MaxLength" || attrName == "StringLength")
                {
                    if (attribute.ConstructorArguments.Length > 0 &&
                        attribute.ConstructorArguments[0].Value is int length)
                    {
                        propertyInfo.MaxLength = length;
                    }
                }
                // MinLength属性
                else if (attrName == "MinLength")
                {
                    if (attribute.ConstructorArguments.Length > 0 &&
                        attribute.ConstructorArguments[0].Value is int length)
                    {
                        propertyInfo.MinLength = length;
                    }
                }
                // Range属性
                else if (attrName == "Range")
                {
                    if (attribute.ConstructorArguments.Length >= 2)
                    {
                        // min値の取得
                        if (attribute.ConstructorArguments[0].Value is int minInt)
                        {
                            propertyInfo.MinValue = minInt;
                        }
                        else if (attribute.ConstructorArguments[0].Value is double minDouble)
                        {
                            propertyInfo.MinValue = minDouble;
                        }

                        // max値の取得
                        if (attribute.ConstructorArguments[1].Value is int maxInt)
                        {
                            propertyInfo.MaxValue = maxInt;
                        }
                        else if (attribute.ConstructorArguments[1].Value is double maxDouble)
                        {
                            propertyInfo.MaxValue = maxDouble;
                        }
                    }
                }
                // RegularExpression属性
                else if (attrName == "RegularExpression")
                {
                    if (attribute.ConstructorArguments.Length > 0)
                    {
                        propertyInfo.RegexPattern = attribute.ConstructorArguments[0].Value?.ToString();
                    }
                }
                // Column属性
                else if (attrName == "Column" || attrName == "ColumnAttribute")
                {
                    if (attribute.ConstructorArguments.Length > 0)
                    {
                        propertyInfo.ColumnName = attribute.ConstructorArguments[0].Value?.ToString();
                    }
                }
                // DefaultValue属性
                else if (attrName == "DefaultValue" || attrName == "DefaultValueAttribute")
                {
                    if (attribute.ConstructorArguments.Length > 0)
                    {
                        propertyInfo.HasDefaultValue = true;
                        propertyInfo.DefaultValue = attribute.ConstructorArguments[0].Value?.ToString();
                    }
                }
            }
        }

        /// <summary>
        ///     シンボルが特定の属性を持つかどうかを確認します
        /// </summary>
        private bool HasAttribute(ISymbol symbol, string attributeName)
        {
            return symbol.GetAttributes().Any(attr =>
                attr.AttributeClass.Name == attributeName ||
                attr.AttributeClass.Name == attributeName + "Attribute");
        }

        /// <summary>
        ///     Enum型の値を抽出します
        /// </summary>
        private void ExtractEnumValues(PropertyInfo propertyInfo, INamedTypeSymbol enumType)
        {
            // Flags属性のチェック
            propertyInfo.IsEnumFlags = HasAttribute(enumType, "Flags");

            // Enum値を取得
            foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.ConstantValue != null)
                {
                    var enumValueInfo = new EnumValueInfo
                    {
                        Name = member.Name, Value = Convert.ToInt32(member.ConstantValue)
                    };

                    // Description属性があれば取得
                    foreach (var attr in member.GetAttributes())
                    {
                        if (attr.AttributeClass.Name == "Description" ||
                            attr.AttributeClass.Name == "DescriptionAttribute")
                        {
                            if (attr.ConstructorArguments.Length > 0)
                            {
                                enumValueInfo.Description = attr.ConstructorArguments[0].Value?.ToString();
                            }
                        }
                    }

                    propertyInfo.EnumValues.Add(enumValueInfo);
                }
            }
        }

        /// <summary>
        ///     エンティティのテーブル名を取得します
        /// </summary>
        private string GetTableName(INamedTypeSymbol typeSymbol)
        {
            var tableName = typeSymbol.Name;

            // Table属性を検索
            foreach (var attribute in typeSymbol.GetAttributes())
            {
                if (attribute.AttributeClass.Name == "Table" ||
                    attribute.AttributeClass.Name == "TableAttribute")
                {
                    // 属性の引数からテーブル名を取得
                    if (attribute.ConstructorArguments.Length > 0)
                    {
                        tableName = attribute.ConstructorArguments[0].Value?.ToString() ?? tableName;
                    }

                    // スキーマ名も取得（存在すれば）
                    if (attribute.NamedArguments.Any(na => na.Key == "Schema"))
                    {
                        var schemaArg = attribute.NamedArguments.First(na => na.Key == "Schema");
                        var schema = schemaArg.Value.Value?.ToString();

                        if (!string.IsNullOrEmpty(schema))
                        {
                            return $"{schema}.{tableName}";
                        }
                    }

                    break;
                }
            }

            return tableName;
        }

        /// <summary>
        ///     エンティティ間のリレーションシップを抽出します
        /// </summary>
        private async Task ExtractRelationshipsAsync(
            EntityInfo entityInfo,
            INamedTypeSymbol classSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            // ナビゲーションプロパティを検索
            var navigationProperties = classSymbol.GetMembers().OfType<IPropertySymbol>()
                .Where(p => IsNavigationProperty(p.Type));

            foreach (var navProp in navigationProperties)
            {
                var relationship = new RelationshipInfo
                {
                    SourceEntityName = entityInfo.Name, SourceNavigationPropertyName = navProp.Name
                };

                // 1対多または多対多のリレーションシップ
                if (IsCollectionType(navProp.Type))
                {
                    var elementType = GetCollectionElementType(navProp.Type);
                    relationship.TargetEntityName = elementType;
                    relationship.RelationType = RelationshipType.OneToMany;

                    // 対応する多対一のリレーションシップを検索
                    var inverseProperty = await FindInverseNavigationPropertyAsync(
                        classSymbol, navProp, elementType, semanticModel.Compilation, cancellationToken);

                    if (inverseProperty != null)
                    {
                        relationship.TargetNavigationPropertyName = inverseProperty.Name;
                    }
                }
                // 1対1または多対1のリレーションシップ
                else
                {
                    relationship.TargetEntityName = navProp.Type.Name;
                    relationship.RelationType = RelationshipType.ManyToOne;

                    // 外部キープロパティを検索
                    var foreignKeyProperty = FindForeignKeyProperty(classSymbol, navProp);
                    if (foreignKeyProperty != null)
                    {
                        relationship.ForeignKeyPropertyName = foreignKeyProperty.Name;
                    }

                    // 対応する1対多のリレーションシップを検索
                    var inverseProperty = await FindInverseNavigationPropertyAsync(
                        classSymbol, navProp, navProp.Type.Name, semanticModel.Compilation, cancellationToken);

                    if (inverseProperty != null)
                    {
                        relationship.TargetNavigationPropertyName = inverseProperty.Name;

                        // 対応するナビゲーションプロパティがコレクション型の場合は多対一
                        if (IsCollectionType(inverseProperty.Type))
                        {
                            relationship.RelationType = RelationshipType.ManyToOne;
                        }
                        else
                        {
                            relationship.RelationType = RelationshipType.OneToOne;
                        }
                    }
                }

                entityInfo.Relationships.Add(relationship);
            }
        }

        /// <summary>
        ///     対応する逆方向のナビゲーションプロパティを検索します
        /// </summary>
        private async Task<IPropertySymbol> FindInverseNavigationPropertyAsync(
            INamedTypeSymbol sourceClass,
            IPropertySymbol navigationProperty,
            string targetTypeName,
            Compilation compilation,
            CancellationToken cancellationToken)
        {
            // 対象となるクラスシンボルを検索
            var targetClassSymbol = compilation.GetTypeByMetadataName(targetTypeName);
            if (targetClassSymbol == null)
            {
                return null;
            }

            // InverseProperty属性が設定されている場合
            var inversePropertyAttribute = navigationProperty.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass.Name == "InversePropertyAttribute");

            if (inversePropertyAttribute != null &&
                inversePropertyAttribute.ConstructorArguments.Length > 0 &&
                inversePropertyAttribute.ConstructorArguments[0].Value is string inversePropName)
            {
                // 指定された名前のプロパティを検索
                return targetClassSymbol.GetMembers(inversePropName).OfType<IPropertySymbol>().FirstOrDefault();
            }

            // ForeignKey属性が設定されている場合
            var foreignKeyAttribute = navigationProperty.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass.Name == "ForeignKeyAttribute");

            if (foreignKeyAttribute != null &&
                foreignKeyAttribute.ConstructorArguments.Length > 0 &&
                foreignKeyAttribute.ConstructorArguments[0].Value is string foreignKeyName)
            {
                // 関連する外部キーを持つナビゲーションプロパティを検索
                return targetClassSymbol.GetMembers().OfType<IPropertySymbol>()
                    .FirstOrDefault(p => HasForeignKeyAttribute(p, sourceClass.Name));
            }

            // 規約に基づく検索
            // 対象クラスの中から、ソースクラスを参照するナビゲーションプロパティを探す
            foreach (var prop in targetClassSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (IsNavigationProperty(prop.Type))
                {
                    if (IsCollectionType(prop.Type))
                    {
                        var elementType = GetCollectionElementType(prop.Type);
                        if (elementType == sourceClass.Name)
                        {
                            return prop;
                        }
                    }
                    else if (prop.Type.Name == sourceClass.Name)
                    {
                        return prop;
                    }
                }
            }

            return null;
        }

        /// <summary>
        ///     ナビゲーションプロパティに関連する外部キープロパティを検索します
        /// </summary>
        private IPropertySymbol FindForeignKeyProperty(INamedTypeSymbol classSymbol, IPropertySymbol navigationProperty)
        {
            // ForeignKey属性が設定されている場合
            var foreignKeyAttribute = navigationProperty.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass.Name == "ForeignKeyAttribute");

            if (foreignKeyAttribute != null &&
                foreignKeyAttribute.ConstructorArguments.Length > 0 &&
                foreignKeyAttribute.ConstructorArguments[0].Value is string foreignKeyName)
            {
                // 指定された名前のプロパティを検索
                return classSymbol.GetMembers(foreignKeyName).OfType<IPropertySymbol>().FirstOrDefault();
            }

            // 規約に基づく検索 (NavPropertyName + "Id")
            var conventionForeignKeyName = navigationProperty.Name + "Id";
            var conventionForeignKey = classSymbol.GetMembers(conventionForeignKeyName).OfType<IPropertySymbol>()
                .FirstOrDefault();
            if (conventionForeignKey != null)
            {
                return conventionForeignKey;
            }

            return null;
        }

        /// <summary>
        ///     エンティティクラスに対応するDbSetプロパティ名を検索します
        /// </summary>
        private async Task<string> FindDbSetPropertyNameAsync(
            INamedTypeSymbol entityClass,
            Solution solution,
            CancellationToken cancellationToken)
        {
            // すべてのDbContextクラスを検索
            var dbContexts = await FindDbContextsInSolutionAsync(solution, cancellationToken);

            foreach (var dbContext in dbContexts)
            {
                // DbSetプロパティを検索
                foreach (var dbSetProperty in dbContext.DbSetProperties)
                {
                    if (dbSetProperty.EntityTypeName == entityClass.Name)
                    {
                        return dbSetProperty.PropertyName;
                    }
                }
            }

            // 見つからなければ、複数形の名前を予測
            return entityClass.Name + "s";
        }

        /// <summary>
        ///     クラスがEntity Frameworkのエンティティクラスかどうかを判定します
        /// </summary>
        private async Task<bool> IsEntityClass(Document document, INamedTypeSymbol classSymbol)
        {
            // 抽象クラスは通常エンティティとして扱わない（ただし、継承階層の親として重要）
            if (classSymbol.IsAbstract)
            {
                return false;
            }

            // EF Core関連の属性を持っているか
            if (HasEntityFrameworkAttributes(classSymbol))
            {
                return true;
            }

            // DbContextから参照されているか
            if (await IsReferencedByDbContext(document, classSymbol))
            {
                return true;
            }

            // 主キープロパティを持っているか
            if (HasPrimaryKey(classSymbol))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///     クラスがDbContextを継承しているかどうかを判定します
        /// </summary>
        private bool IsDbContextClass(INamedTypeSymbol classSymbol)
        {
            // DbContextクラスを継承している場合
            if (classSymbol.BaseType != null)
            {
                var baseTypeName = classSymbol.BaseType.Name;
                if (baseTypeName == "DbContext")
                {
                    return true;
                }

                // 間接的な継承の場合も考慮
                var currentType = classSymbol.BaseType;
                while (currentType != null)
                {
                    if (currentType.Name == "DbContext")
                    {
                        return true;
                    }

                    currentType = currentType.BaseType;
                }

                // 名前で判断（Convention）
                foreach (var suffix in DbContextSuffixes)
                {
                    if (classSymbol.Name.EndsWith(suffix))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        ///     プロジェクトがEntity Frameworkを使用しているかどうかを判定します
        /// </summary>
        private async Task<bool> ProjectUsesEntityFrameworkAsync(Project project, CancellationToken cancellationToken)
        {
            // プロジェクト参照を確認
            foreach (var reference in project.MetadataReferences)
            {
                if (reference.Display?.Contains("EntityFrameworkCore") == true)
                {
                    return true;
                }
            }

            // ソースコード内にEF Core名前空間の使用があるか確認
            foreach (var document in project.Documents)
            {
                var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                if (syntaxRoot == null)
                {
                    continue;
                }

                // using宣言を検索
                var usingDirectives = syntaxRoot.DescendantNodes().OfType<UsingDirectiveSyntax>();
                foreach (var usingDirective in usingDirectives)
                {
                    if (usingDirective.Name.ToString().Contains("EntityFrameworkCore"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        ///     クラスがEF Core関連の属性を持っているかどうかを判定します
        /// </summary>
        private bool HasEntityFrameworkAttributes(INamedTypeSymbol classSymbol)
        {
            var attributeNames = new[] { "Table", "Entity", "Keyless", "Owned" };

            return classSymbol.GetAttributes().Any(attr =>
                attributeNames.Contains(attr.AttributeClass.Name) ||
                attributeNames.Any(name => attr.AttributeClass.Name == name + "Attribute"));
        }

        /// <summary>
        ///     クラスがDbContextから参照されているかどうかを判定します
        /// </summary>
        private async Task<bool> IsReferencedByDbContext(Document document, INamedTypeSymbol typeSymbol,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // ソリューション全体を検索するために現在のソリューションを取得
                var solution = document.Project.Solution;

                // DbContext派生クラスを含むドキュメントを探す
                foreach (var project in solution.Projects)
                {
                    foreach (var projectDocument in project.Documents)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var syntaxRoot = await projectDocument.GetSyntaxRootAsync(cancellationToken);
                        if (syntaxRoot == null)
                        {
                            continue;
                        }

                        var semanticModel = await projectDocument.GetSemanticModelAsync(cancellationToken);
                        if (semanticModel == null)
                        {
                            continue;
                        }

                        // クラスとレコードの宣言を検索
                        var typeDeclarations = syntaxRoot.DescendantNodes().OfType<TypeDeclarationSyntax>();

                        foreach (var typeDecl in typeDeclarations)
                        {
                            var classSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
                            if (classSymbol == null)
                            {
                                continue;
                            }

                            if (IsDbContextClass(classSymbol))
                            {
                                // DbSetプロパティを検索
                                foreach (var member in classSymbol.GetMembers())
                                {
                                    if (member is IPropertySymbol propertySymbol)
                                    {
                                        if (IsDbSetProperty(propertySymbol, out var entityType))
                                        {
                                            if (SymbolEqualityComparer.Default.Equals(entityType, typeSymbol))
                                            {
                                                // DbSetプロパティ名も保存できる
                                                var dbSetName = propertySymbol.Name;
                                                return true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in IsReferencedByDbContextAsync: {ex.Message}");
                return false;
            }
        }

        ///// <summary>
        ///// 指定されたシンボルがDbContextを継承しているかどうかを判定します
        ///// </summary>
        //private bool IsDbContextClass(INamedTypeSymbol classSymbol)
        //{
        //    if (classSymbol == null)
        //        return false;

        //    // クラス自体がDbContextかどうかをチェック
        //    if (classSymbol.Name == "DbContext" || classSymbol.Name.EndsWith("Context"))
        //        return true;

        //    // 基底クラスがDbContextかどうかを再帰的にチェック
        //    if (classSymbol.BaseType != null)
        //        return IsDbContextClass(classSymbol.BaseType);

        //    return false;
        //}

        /// <summary>
        ///     プロパティがDbSet<T>型かどうかを判定し、Tの型を取得します
        /// </summary>
        private bool IsDbSetProperty(IPropertySymbol propertySymbol, out ITypeSymbol entityType)
        {
            entityType = null;

            if (propertySymbol.Type is INamedTypeSymbol namedType)
            {
                // DbSet<T>かどうかを確認
                if (namedType.Name == "DbSet" && namedType.TypeArguments.Length == 1)
                {
                    entityType = namedType.TypeArguments[0];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     クラスが主キープロパティを持っているかどうかを判定します
        /// </summary>
        private bool HasPrimaryKey(INamedTypeSymbol classSymbol)
        {
            // Key属性を持つプロパティを検索
            foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (HasKeyAttribute(member))
                {
                    return true;
                }
            }

            // 規約に基づくプロパティを検索 (Id または ClassNameId)
            var conventionKeyNames = new[] { "Id", classSymbol.Name + "Id" };
            return classSymbol.GetMembers().OfType<IPropertySymbol>()
                .Any(p => conventionKeyNames.Contains(p.Name));
        }

        /// <summary>
        ///     プロパティがKey属性を持っているかどうかを判定します
        /// </summary>
        private bool HasKeyAttribute(IPropertySymbol propertySymbol)
        {
            return propertySymbol.GetAttributes().Any(attr =>
                attr.AttributeClass.Name == "Key" ||
                attr.AttributeClass.Name == "KeyAttribute");
        }

        /// <summary>
        ///     プロパティがRequired属性を持っているか、非Nullableかどうかを判定します
        /// </summary>
        private bool IsRequiredProperty(IPropertySymbol propertySymbol)
        {
            // Required属性が設定されている場合
            var hasRequiredAttribute = propertySymbol.GetAttributes().Any(attr =>
                attr.AttributeClass.Name == "Required" ||
                attr.AttributeClass.Name == "RequiredAttribute");

            if (hasRequiredAttribute)
            {
                return true;
            }

            // プリミティブ型で非Nullableの場合
            if (propertySymbol.Type.IsValueType &&
                !(propertySymbol.Type is INamedTypeSymbol namedType && namedType.IsNullableType()))
            {
                return true;
            }

            // C# 8.0以降の参照型の非Nullableの場合
            if (propertySymbol.NullableAnnotation == NullableAnnotation.NotAnnotated)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///     プロパティがDatabaseGenerated属性を持っているかどうかを判定します
        /// </summary>
        private bool HasDatabaseGeneratedAttribute(IPropertySymbol propertySymbol)
        {
            return propertySymbol.GetAttributes().Any(attr =>
                attr.AttributeClass.Name == "DatabaseGenerated" ||
                attr.AttributeClass.Name == "DatabaseGeneratedAttribute");
        }

        /// <summary>
        ///     プロパティの最大長・最小長の制約を抽出します
        /// </summary>
        private void ExtractLengthConstraints(IPropertySymbol propertySymbol, PropertyInfo propertyInfo)
        {
            // StringLength属性
            var stringLengthAttr = propertySymbol.GetAttributes().FirstOrDefault(attr =>
                attr.AttributeClass.Name == "StringLength" ||
                attr.AttributeClass.Name == "StringLengthAttribute");

            if (stringLengthAttr != null && stringLengthAttr.ConstructorArguments.Length > 0)
            {
                propertyInfo.MaxLength = Convert.ToInt32(stringLengthAttr.ConstructorArguments[0].Value);

                // 最小長が指定されている場合
                var namedArgs = stringLengthAttr.NamedArguments;
                var minLengthArg = namedArgs.FirstOrDefault(arg => arg.Key == "MinimumLength");
                if (!minLengthArg.Equals(default) && minLengthArg.Value.Value != null)
                {
                    propertyInfo.MinLength = Convert.ToInt32(minLengthArg.Value.Value);
                }
            }

            // MaxLength属性
            var maxLengthAttr = propertySymbol.GetAttributes().FirstOrDefault(attr =>
                attr.AttributeClass.Name == "MaxLength" ||
                attr.AttributeClass.Name == "MaxLengthAttribute");

            if (maxLengthAttr != null && maxLengthAttr.ConstructorArguments.Length > 0 &&
                propertyInfo.MaxLength == null)
            {
                propertyInfo.MaxLength = Convert.ToInt32(maxLengthAttr.ConstructorArguments[0].Value);
            }

            // MinLength属性
            var minLengthAttr = propertySymbol.GetAttributes().FirstOrDefault(attr =>
                attr.AttributeClass.Name == "MinLength" ||
                attr.AttributeClass.Name == "MinLengthAttribute");

            if (minLengthAttr != null && minLengthAttr.ConstructorArguments.Length > 0 &&
                propertyInfo.MinLength == null)
            {
                propertyInfo.MinLength = Convert.ToInt32(minLengthAttr.ConstructorArguments[0].Value);
            }
        }

        /// <summary>
        ///     外部キー情報を抽出します
        /// </summary>
        private void ExtractForeignKeyInfo(IPropertySymbol propertySymbol, PropertyInfo propertyInfo)
        {
            // ForeignKey属性
            var foreignKeyAttr = propertySymbol.GetAttributes().FirstOrDefault(attr =>
                attr.AttributeClass.Name == "ForeignKey" ||
                attr.AttributeClass.Name == "ForeignKeyAttribute");

            if (foreignKeyAttr != null && foreignKeyAttr.ConstructorArguments.Length > 0)
            {
                propertyInfo.IsForeignKey = true;
                propertyInfo.ForeignKeyTargetProperty = foreignKeyAttr.ConstructorArguments[0].Value as string;

                // ターゲットエンティティ名を推測（ForeignKeyの対象となるナビゲーションプロパティの型）
                var navigationPropertyName = propertyInfo.ForeignKeyTargetProperty;
                if (!string.IsNullOrEmpty(navigationPropertyName))
                {
                    var containingType = propertySymbol.ContainingType;
                    var navigationProperty = containingType.GetMembers(navigationPropertyName).OfType<IPropertySymbol>()
                        .FirstOrDefault();
                    if (navigationProperty != null)
                    {
                        propertyInfo.ForeignKeyTargetEntity = navigationProperty.Type.Name;
                    }
                }
            }
            // 規約に基づく判定（プロパティ名がIdで終わる場合）
            else if (propertySymbol.Name.EndsWith("Id") && !propertySymbol.Name.Equals("Id"))
            {
                propertyInfo.IsForeignKey = true;

                // ターゲットエンティティ名を推測（プロパティ名からIdを除いた部分）
                var targetEntity = propertySymbol.Name.Substring(0, propertySymbol.Name.Length - 2);
                propertyInfo.ForeignKeyTargetEntity = targetEntity;

                // ターゲットプロパティ名を推測（通常は「Id」）
                propertyInfo.ForeignKeyTargetProperty = "Id";
            }
        }

        /// <summary>
        ///     Enum型の値情報を抽出します
        /// </summary>
        private List<EnumValueInfo> ExtractEnumValues(INamedTypeSymbol enumType)
        {
            var result = new List<EnumValueInfo>();

            if (enumType == null || enumType.TypeKind != TypeKind.Enum)
            {
                return result;
            }

            foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.HasConstantValue)
                {
                    var enumValue = new EnumValueInfo
                    {
                        Name = member.Name, Value = Convert.ToInt32(member.ConstantValue)
                    };

                    result.Add(enumValue);
                }
            }

            return result;
        }

        ////// <summary>
        /// 型がFlags属性を持っているかどうかを判定します
        /// </summary>
        private bool HasFlagsAttribute(ITypeSymbol typeSymbol)
        {
            return typeSymbol.GetAttributes().Any(attr =>
                attr.AttributeClass.Name == "Flags" ||
                attr.AttributeClass.Name == "FlagsAttribute" ||
                attr.AttributeClass.ToString() == "System.FlagsAttribute");
        }

        /// <summary>
        ///     プロパティがForeignKey属性を持っているかどうかを判定します
        /// </summary>
        private bool HasForeignKeyAttribute(IPropertySymbol propertySymbol, string targetEntityName = null)
        {
            var foreignKeyAttrs = propertySymbol.GetAttributes().Where(attr =>
                attr.AttributeClass.Name == "ForeignKey" ||
                attr.AttributeClass.Name == "ForeignKeyAttribute");

            if (!foreignKeyAttrs.Any())
            {
                return false;
            }

            // ターゲットエンティティが指定されている場合は絞り込む
            if (!string.IsNullOrEmpty(targetEntityName))
            {
                foreach (var attr in foreignKeyAttrs)
                {
                    if (attr.ConstructorArguments.Length > 0 &&
                        attr.ConstructorArguments[0].Value is string navigationPropName)
                    {
                        var containingType = propertySymbol.ContainingType;
                        var navigationProperty = containingType.GetMembers(navigationPropName)
                            .OfType<IPropertySymbol>().FirstOrDefault();

                        if (navigationProperty != null && navigationProperty.Type.Name == targetEntityName)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            return true;
        }

        /// <summary>
        ///     型がナビゲーションプロパティかどうかを判定します
        /// </summary>
        public static bool IsNavigationProperty(ITypeSymbol propertySymbol)
        {
            // 仮想プロパティのみをナビゲーションプロパティとして扱う
            if (propertySymbol.IsVirtual)
            {
                // コレクション型の場合
                if (IsCollectionType(propertySymbol))
                {
                    return true;
                }

                // クラス/インターフェース型で、システム型でない場合
                if ((propertySymbol.TypeKind == TypeKind.Class ||
                     propertySymbol.TypeKind == TypeKind.Interface) &&
                    !IsSystemType(propertySymbol))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsNavigationProperty(IPropertySymbol propertySymbol)
        {
            // 仮想プロパティのみをナビゲーションプロパティとして扱う
            if (propertySymbol.IsVirtual)
            {
                // コレクション型の場合
                if (IsCollectionType(propertySymbol))
                {
                    return true;
                }

                // クラス/インターフェース型で、システム型でない場合
                if ((propertySymbol.Type.TypeKind == TypeKind.Class ||
                     propertySymbol.Type.TypeKind == TypeKind.Interface) &&
                    !IsSystemType(propertySymbol))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     型がコレクション型かどうかを判定します
        /// </summary>
        private static bool IsCollectionType(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol namedType)
            {
                // ICollection<T>、IList<T>、List<T>などの実装または継承をチェック
                if (namedType.TypeArguments.Length > 0)
                {
                    var originalDefinition = namedType.OriginalDefinition?.ToString();
                    if (originalDefinition != null)
                    {
                        if (originalDefinition.StartsWith("System.Collections.Generic.ICollection<") ||
                            originalDefinition.StartsWith("System.Collections.Generic.IList<") ||
                            originalDefinition.StartsWith("System.Collections.Generic.List<") ||
                            originalDefinition.StartsWith("System.Collections.Generic.IEnumerable<") ||
                            originalDefinition.StartsWith("System.Collections.Generic.HashSet<") ||
                            originalDefinition.StartsWith("System.Collections.ObjectModel.Collection<") ||
                            originalDefinition.StartsWith("System.Collections.ObjectModel.ObservableCollection<"))
                        {
                            return true;
                        }
                    }
                }

                // インターフェイスの実装をチェック
                foreach (var intf in namedType.AllInterfaces)
                {
                    var interfaceName = intf.OriginalDefinition?.ToString();
                    if (interfaceName != null)
                    {
                        if (interfaceName.StartsWith("System.Collections.Generic.ICollection<") ||
                            interfaceName.StartsWith("System.Collections.Generic.IList<"))
                        {
                            return true;
                        }

                        if (interfaceName.StartsWith($"System.Collections.Generic.IEnumerable<{namedType.Name}>")
                            || interfaceName.StartsWith(
                                $"System.Collections.Generic.IEnumerable<{namedType.ContainingNamespace}.{namedType.Name}>"))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsCollectionType(IPropertySymbol type)
        {
            if (type is INamedTypeSymbol namedType)
            {
                // ICollection<T>、IList<T>、List<T>などの実装または継承をチェック
                if (namedType.TypeArguments.Length > 0)
                {
                    var originalDefinition = namedType.OriginalDefinition?.ToString();
                    if (originalDefinition != null)
                    {
                        if (originalDefinition.StartsWith("System.Collections.Generic.ICollection<") ||
                            originalDefinition.StartsWith("System.Collections.Generic.IList<") ||
                            originalDefinition.StartsWith("System.Collections.Generic.List<") ||
                            originalDefinition.StartsWith("System.Collections.Generic.IEnumerable<") ||
                            originalDefinition.StartsWith("System.Collections.Generic.HashSet<") ||
                            originalDefinition.StartsWith("System.Collections.ObjectModel.Collection<") ||
                            originalDefinition.StartsWith("System.Collections.ObjectModel.ObservableCollection<"))
                        {
                            return true;
                        }
                    }
                }

                // インターフェイスの実装をチェック
                foreach (var intf in namedType.AllInterfaces)
                {
                    var interfaceName = intf.OriginalDefinition?.ToString();
                    if (interfaceName != null)
                    {
                        if (interfaceName.StartsWith("System.Collections.Generic.ICollection<") ||
                            interfaceName.StartsWith("System.Collections.Generic.IList<"))
                        {
                            return true;
                        }

                        if (interfaceName.StartsWith($"System.Collections.Generic.IEnumerable<{namedType.Name}>")
                            || interfaceName.StartsWith(
                                $"System.Collections.Generic.IEnumerable<{namedType.ContainingNamespace}.{namedType.Name}>"))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        ///     コレクション型の要素の型名を取得します
        /// </summary>
        private string GetCollectionElementType(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                return namedType.TypeArguments[0].Name;
            }

            return string.Empty;
        }

        /// <summary>
        ///     型がシステム型かどうかを判定します
        /// </summary>
        private static bool IsSystemType(ITypeSymbol type)
        {
            if (type.ContainingNamespace?.ToString().StartsWith("System") == true)
            {
                return true;
            }

            // プリミティブ型
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_Char:
                case SpecialType.System_DateTime:
                case SpecialType.System_Decimal:
                case SpecialType.System_Double:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_Object:
                case SpecialType.System_SByte:
                case SpecialType.System_Single:
                case SpecialType.System_String:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                    return true;
            }

            // よく使われるSystem型
            var typeName = type.ToString();
            if (typeName == "System.Guid" ||
                typeName == "System.DateTimeOffset" ||
                typeName == "System.TimeSpan" ||
                typeName == "System.Uri" ||
                typeName == "System.Drawing.Color" ||
                typeName == "System.Net.IPAddress")
            {
                return true;
            }

            return false;
        }

        private static bool IsSystemType(IPropertySymbol type)
        {
            if (type.ContainingNamespace?.ToString().StartsWith("System") == true)
            {
                return true;
            }

            // プリミティブ型
            switch (type.Type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_Char:
                case SpecialType.System_DateTime:
                case SpecialType.System_Decimal:
                case SpecialType.System_Double:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_Object:
                case SpecialType.System_SByte:
                case SpecialType.System_Single:
                case SpecialType.System_String:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                    return true;
            }

            // よく使われるSystem型
            var typeName = type.ToString();
            if (typeName == "System.Guid" ||
                typeName == "System.DateTimeOffset" ||
                typeName == "System.TimeSpan" ||
                typeName == "System.Uri" ||
                typeName == "System.Drawing.Color" ||
                typeName == "System.Net.IPAddress")
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///     カラム名を取得します（Column属性から、なければプロパティ名）
        /// </summary>
        private string GetColumnName(IPropertySymbol propertySymbol)
        {
            var columnAttribute = propertySymbol.GetAttributes().FirstOrDefault(attr =>
                attr.AttributeClass.Name == "Column" ||
                attr.AttributeClass.Name == "ColumnAttribute");

            if (columnAttribute != null && columnAttribute.ConstructorArguments.Length > 0)
            {
                return columnAttribute.ConstructorArguments[0].Value as string;
            }

            // デフォルトはプロパティ名
            return propertySymbol.Name;
        }

        /// <summary>
        ///     DbContext情報を抽出します
        /// </summary>
        private DbContextInfo ExtractDbContextInfo(
            INamedTypeSymbol classSymbol,
            ClassDeclarationSyntax classDecl,
            Document document,
            SemanticModel semanticModel)
        {
            var contextInfo = new DbContextInfo
            {
                Name = classSymbol.Name,
                FullName = classSymbol.ToDisplayString(),
                Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
                Document = document
            };

            // DbSetプロパティを抽出
            foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (IsDbSetProperty(member))
                {
                    var dbSetInfo = new DbSetInfo
                    {
                        PropertyName = member.Name, EntityTypeName = GetDbSetEntityTypeName(member.Type)
                    };

                    contextInfo.DbSetProperties.Add(dbSetInfo);
                }
            }

            return contextInfo;
        }

        /// <summary>
        ///     プロパティがDbSet<T>かどうかを判定します
        /// </summary>
        private bool IsDbSetProperty(IPropertySymbol propertySymbol)
        {
            if (propertySymbol.Type is INamedTypeSymbol namedType)
            {
                var typeName = namedType.OriginalDefinition?.ToString();
                return typeName != null &&
                       (typeName.StartsWith("Microsoft.EntityFrameworkCore.DbSet<") ||
                        typeName.Contains(".DbSet<"));
            }

            return false;
        }

        /// <summary>
        ///     DbSet<T>からエンティティ型名を取得します
        /// </summary>
        private string GetDbSetEntityTypeName(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                return namedType.TypeArguments[0].Name;
            }

            return string.Empty;
        }
    }

    /// <summary>
    ///     DbContextに関する情報を格納するクラス
    /// </summary>
    public class DbContextInfo
    {
        /// <summary>
        ///     DbContextの名前
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        ///     完全修飾名（名前空間を含む）
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        ///     名前空間
        /// </summary>
        public string Namespace { get; set; }

        /// <summary>
        ///     DbContextの元となるドキュメント参照
        /// </summary>
        public Document Document { get; set; }

        /// <summary>
        ///     DbSetプロパティのリスト
        /// </summary>
        public List<DbSetInfo> DbSetProperties { get; set; } = new List<DbSetInfo>();

        /// <summary>
        ///     ToString()のオーバーライド
        /// </summary>
        public override string ToString() => $"{Name} ({DbSetProperties.Count} entities)";
    }

    /// <summary>
    ///     DbSetプロパティに関する情報を格納するクラス
    /// </summary>
    public class DbSetInfo
    {
        /// <summary>
        ///     DbSetプロパティの名前
        /// </summary>
        public string PropertyName { get; set; }

        /// <summary>
        ///     エンティティの型名
        /// </summary>
        public string EntityTypeName { get; set; }

        /// <summary>
        ///     ToString()のオーバーライド
        /// </summary>
        public override string ToString() => $"{PropertyName} ({EntityTypeName})";
    }

    /// <summary>
    ///     拡張メソッド
    /// </summary>
    public static class EntityAnalyzerExtensions
    {
        /// <summary>
        ///     プロパティが自動実装されたプロパティかどうかを判定します
        /// </summary>
        public static bool IsAutoProperty(this IPropertySymbol property) =>
            property.GetMethod != null && property.SetMethod != null;

        /// <summary>
        ///     型がNullable<T>かどうかを判定します
        /// </summary>
        public static bool IsNullableType(this INamedTypeSymbol type) =>
            type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }
}