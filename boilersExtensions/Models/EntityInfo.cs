using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace boilersExtensions.Models
{
    /// <summary>
    ///     Entity Frameworkのエンティティクラスに関する情報を格納するクラス
    /// </summary>
    public class EntityInfo
    {
        /// <summary>
        ///     エンティティの名前（クラス名）
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
        ///     エンティティのプロパティ一覧
        /// </summary>
        public List<PropertyInfo> Properties { get; set; } = new List<PropertyInfo>();

        /// <summary>
        ///     主キープロパティ（複合主キーの場合は最初のもの）
        /// </summary>
        public PropertyInfo KeyProperty => Properties.FirstOrDefault(p => p.IsKey);

        /// <summary>
        ///     複合主キーの場合、すべての主キープロパティ
        /// </summary>
        public IEnumerable<PropertyInfo> KeyProperties => Properties.Where(p => p.IsKey);

        /// <summary>
        ///     このエンティティからの関連（ナビゲーションプロパティ）
        /// </summary>
        public List<RelationshipInfo> Relationships { get; set; } = new List<RelationshipInfo>();

        /// <summary>
        ///     エンティティの元となるドキュメント参照
        /// </summary>
        public Document Document { get; set; }

        /// <summary>
        ///     エンティティの型シンボル（Roslyn解析時の情報）
        /// </summary>
        public INamedTypeSymbol Symbol { get; set; }

        /// <summary>
        ///     テーブル名（Table属性から取得、なければクラス名）
        /// </summary>
        public string TableName { get; set; }

        /// <summary>
        ///     スキーマ名（Table属性から取得）
        /// </summary>
        public string SchemaName { get; set; }

        /// <summary>
        ///     このエンティティが抽象クラスかどうか
        /// </summary>
        public bool IsAbstract { get; set; }

        /// <summary>
        ///     このエンティティが継承を使用しているかどうか
        /// </summary>
        public bool UsesInheritance { get; set; }

        /// <summary>
        ///     基底クラスの名前（継承を使用している場合）
        /// </summary>
        public string BaseTypeName { get; set; }

        /// <summary>
        ///     エンティティのシードデータ生成時に使用する説明文（コメントなどから抽出）
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        ///     必須プロパティの一覧
        /// </summary>
        public IEnumerable<PropertyInfo> RequiredProperties => Properties.Where(p => p.IsRequired);

        /// <summary>
        ///     外部キープロパティの一覧
        /// </summary>
        public IEnumerable<PropertyInfo> ForeignKeyProperties => Properties.Where(p => p.IsForeignKey);

        /// <summary>
        ///     DbContextでマッピングされている名前を取得（DbSet<Entity>のプロパティ名）
        /// </summary>
        public string DbSetName { get; set; }

        /// <summary>
        ///     エンティティ型からの完全なTableNameを取得します
        /// </summary>
        public string GetFullTableName()
        {
            if (string.IsNullOrEmpty(SchemaName))
            {
                return TableName;
            }

            return $"{SchemaName}.{TableName}";
        }

        /// <summary>
        ///     このエンティティが関連するテーブルの依存関係を検証します
        /// </summary>
        public IEnumerable<string> GetDependentEntityNames()
        {
            return ForeignKeyProperties
                .Select(fk => fk.ForeignKeyTargetEntity)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct();
        }

        /// <summary>
        ///     ToString()のオーバーライド
        /// </summary>
        public override string ToString() => $"{Name} ({Properties.Count} properties, {KeyProperties.Count()} keys)";
    }
}