using System.Collections.Generic;

namespace boilersExtensions.Models
{
    /// <summary>
    ///     Entity Frameworkエンティティ間のリレーションシップ情報を格納するクラス
    /// </summary>
    public class RelationshipInfo
    {
        /// <summary>
        ///     リレーションシップの種類
        /// </summary>
        public RelationshipType RelationType { get; set; }

        /// <summary>
        ///     リレーションシップの元となるエンティティ名
        /// </summary>
        public string SourceEntityName { get; set; }

        /// <summary>
        ///     リレーションシップの対象となるエンティティ名
        /// </summary>
        public string TargetEntityName { get; set; }

        /// <summary>
        ///     ソースエンティティのナビゲーションプロパティ名
        /// </summary>
        public string SourceNavigationPropertyName { get; set; }

        /// <summary>
        ///     ターゲットエンティティのナビゲーションプロパティ名（存在する場合）
        /// </summary>
        public string TargetNavigationPropertyName { get; set; }

        /// <summary>
        ///     ソースエンティティの外部キープロパティ名
        /// </summary>
        public string ForeignKeyPropertyName { get; set; }

        /// <summary>
        ///     対象エンティティの主キープロパティ名
        /// </summary>
        public string PrincipalKeyPropertyName { get; set; }

        /// <summary>
        ///     このリレーションシップが必須かどうか（CASCADE DELETEなど）
        /// </summary>
        public bool IsRequired { get; set; }

        /// <summary>
        ///     このリレーションシップの削除時の動作
        /// </summary>
        public DeleteBehavior DeleteBehavior { get; set; }

        /// <summary>
        ///     リレーションシップの説明・コメント
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        ///     このリレーションシップがFluent APIで設定されたかどうか
        /// </summary>
        public bool IsConfiguredByFluentApi { get; set; }

        /// <summary>
        ///     このリレーションシップが多対多の場合、結合テーブル名
        /// </summary>
        public string JoinTableName { get; set; }

        /// <summary>
        ///     多対多の結合テーブルで使用されるカラム
        /// </summary>
        public List<JoinColumnInfo> JoinColumns { get; set; } = new List<JoinColumnInfo>();

        /// <summary>
        ///     これが自己参照リレーションシップかどうか
        /// </summary>
        public bool IsSelfReferencing => SourceEntityName == TargetEntityName;

        /// <summary>
        ///     ToString()のオーバーライド
        /// </summary>
        public override string ToString() =>
            $"{SourceEntityName}.{SourceNavigationPropertyName} -> {TargetEntityName} ({RelationType})";
    }

    /// <summary>
    ///     リレーションシップの種類を表す列挙型
    /// </summary>
    public enum RelationshipType
    {
        /// <summary>
        ///     一対一のリレーションシップ
        /// </summary>
        OneToOne,

        /// <summary>
        ///     一対多のリレーションシップ
        /// </summary>
        OneToMany,

        /// <summary>
        ///     多対一のリレーションシップ
        /// </summary>
        ManyToOne,

        /// <summary>
        ///     多対多のリレーションシップ
        /// </summary>
        ManyToMany
    }

    /// <summary>
    ///     削除時の動作を表す列挙型
    /// </summary>
    public enum DeleteBehavior
    {
        /// <summary>
        ///     カスケード削除（主テーブルのレコードが削除されると、関連する従テーブルのレコードも削除）
        /// </summary>
        Cascade,

        /// <summary>
        ///     関連するレコードを維持
        /// </summary>
        Restrict,

        /// <summary>
        ///     関連するフィールドをnullに設定
        /// </summary>
        SetNull,

        /// <summary>
        ///     関連するフィールドをデフォルト値に設定
        /// </summary>
        SetDefault,

        /// <summary>
        ///     動作なし
        /// </summary>
        NoAction,

        /// <summary>
        ///     クライアント側で処理（EF Coreのデフォルト）
        /// </summary>
        ClientSetNull
    }

    /// <summary>
    ///     多対多リレーションシップの結合テーブルのカラム情報
    /// </summary>
    public class JoinColumnInfo
    {
        /// <summary>
        ///     結合テーブルのカラム名
        /// </summary>
        public string ColumnName { get; set; }

        /// <summary>
        ///     参照先エンティティ名
        /// </summary>
        public string ReferencedEntityName { get; set; }

        /// <summary>
        ///     参照先プロパティ名
        /// </summary>
        public string ReferencedPropertyName { get; set; }

        /// <summary>
        ///     ToString()のオーバーライド
        /// </summary>
        public override string ToString() => $"{ColumnName} -> {ReferencedEntityName}.{ReferencedPropertyName}";
    }
}