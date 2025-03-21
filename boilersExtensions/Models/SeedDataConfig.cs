using System;
using System.Collections.Generic;
using System.Linq;

namespace boilersExtensions.Models
{
    /// <summary>
    /// シードデータ生成の全体設定を保持するクラス
    /// </summary>
    public class SeedDataConfig
    {
        /// <summary>
        /// シードデータを生成するエンティティの設定リスト
        /// </summary>
        public List<EntityConfigViewModel> EntityConfigs { get; set; } = new List<EntityConfigViewModel>();

        /// <summary>
        /// シード生成時のグローバル設定
        /// </summary>
        public GlobalSeedSettings GlobalSettings { get; set; } = new GlobalSeedSettings();

        /// <summary>
        /// 出力形式の設定
        /// </summary>
        public OutputFormatSettings OutputSettings { get; set; } = new OutputFormatSettings();

        /// <summary>
        /// エンティティ名から対応する設定を取得します
        /// </summary>
        /// <param name="entityName">エンティティ名</param>
        /// <returns>エンティティの設定、または該当がない場合はnull</returns>
        public EntityConfigViewModel GetEntityConfig(string entityName)
        {
            return EntityConfigs.FirstOrDefault(e => string.Equals(e.EntityName, entityName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// エンティティの設定を追加または更新します
        /// </summary>
        /// <param name="config">エンティティ設定</param>
        public void UpdateEntityConfig(EntityConfigViewModel config)
        {
            int index = EntityConfigs.FindIndex(e => string.Equals(e.EntityName, config.EntityName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                EntityConfigs[index] = config;
            }
            else
            {
                EntityConfigs.Add(config);
            }
        }
    }

    /// <summary>
    /// シード生成時のグローバル設定
    /// </summary>
    public class GlobalSeedSettings
    {
        /// <summary>
        /// シードデータの生成に使用する乱数のシード値（再現性のため）
        /// </summary>
        public int? RandomSeed { get; set; }

        /// <summary>
        /// 依存関係を自動的に解決するかどうか
        /// </summary>
        public bool AutoResolveDependencies { get; set; } = true;

        /// <summary>
        /// 循環参照が検出された場合の処理方法
        /// </summary>
        public CircularReferenceHandling CircularReferenceHandling { get; set; } = CircularReferenceHandling.BreakWithNull;

        /// <summary>
        /// リレーションシップの処理方法の共通設定
        /// </summary>
        public RelationshipStrategy DefaultRelationshipStrategy { get; set; } = RelationshipStrategy.OneToOne;

        /// <summary>
        /// 必須プロパティに常に値を設定するかどうか
        /// </summary>
        public bool AlwaysPopulateRequiredProperties { get; set; } = true;

        /// <summary>
        /// 外部キーの参照整合性を維持するかどうか
        /// </summary>
        public bool EnforceReferentialIntegrity { get; set; } = true;

        /// <summary>
        /// Nullable型のプロパティがnull値を取得する確率（0.0〜1.0）
        /// </summary>
        public double NullablePropertyNullProbability { get; set; } = 0.1;
    }

    /// <summary>
    /// 出力形式の設定
    /// </summary>
    public class OutputFormatSettings
    {
        /// <summary>
        /// OnModelCreatingメソッド内に直接挿入するかどうか
        /// </summary>
        public bool InsertDirectlyIntoOnModelCreating { get; set; } = true;

        /// <summary>
        /// エンティティごとに別々のメソッドを生成するかどうか
        /// </summary>
        public bool GenerateSeparateMethodsPerEntity { get; set; } = false;

        /// <summary>
        /// 拡張メソッドとして実装するかどうか
        /// </summary>
        public bool ImplementAsExtensionMethod { get; set; } = false;

        /// <summary>
        /// 出力ファイルパス（新しいファイルに生成する場合）
        /// </summary>
        public string OutputFilePath { get; set; }

        /// <summary>
        /// コードスタイル: インデントにタブを使用するかどうか（falseの場合はスペース）
        /// </summary>
        public bool UseTabsForIndentation { get; set; } = false;

        /// <summary>
        /// コードスタイル: インデントサイズ（スペース数）
        /// </summary>
        public int IndentSize { get; set; } = 4;

        /// <summary>
        /// コメントを含めるかどうか
        /// </summary>
        public bool IncludeComments { get; set; } = true;

        /// <summary>
        /// 列挙型の値をコメントに含めるかどうか
        /// </summary>
        public bool IncludeEnumValuesInComments { get; set; } = true;

        /// <summary>
        /// 生成されたクラス名（新しいクラスを生成する場合）
        /// </summary>
        public string GeneratedClassName { get; set; } = "DbSeedData";

        /// <summary>
        /// 生成されたメソッド名
        /// </summary>
        public string GeneratedMethodName { get; set; } = "SeedData";

        /// <summary>
        /// 名前空間（新しいファイルに生成する場合）
        /// </summary>
        public string Namespace { get; set; }
    }

    /// <summary>
    /// エンティティごとの設定を保持するビューモデル
    /// </summary>
    public class EntityConfigViewModel
    {
        /// <summary>
        /// エンティティの名前
        /// </summary>
        public string EntityName { get; set; }

        /// <summary>
        /// 生成するレコード数
        /// </summary>
        public int RecordCount { get; set; } = 10;

        /// <summary>
        /// プロパティごとの設定リスト
        /// </summary>
        public List<PropertyConfigViewModel> PropertyConfigs { get; set; } = new List<PropertyConfigViewModel>();

        /// <summary>
        /// リレーションシップの設定リスト
        /// </summary>
        public List<RelationshipConfigViewModel> RelationshipConfigs { get; set; } = new List<RelationshipConfigViewModel>();

        /// <summary>
        /// 選択された列挙型のエンティティを生成するかどうか
        /// </summary>
        public bool IsSelected { get; set; } = true;

        /// <summary>
        /// プロパティ名から対応する設定を取得します
        /// </summary>
        /// <param name="propertyName">プロパティ名</param>
        /// <returns>プロパティの設定、または該当がない場合はnull</returns>
        public PropertyConfigViewModel GetPropertyConfig(string propertyName)
        {
            return PropertyConfigs.FirstOrDefault(p => string.Equals(p.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 関連エンティティ名から対応するリレーションシップ設定を取得します
        /// </summary>
        /// <param name="relatedEntityName">関連エンティティ名</param>
        /// <returns>リレーションシップの設定、または該当がない場合はnull</returns>
        public RelationshipConfigViewModel GetRelationshipConfig(string relatedEntityName)
        {
            return RelationshipConfigs.FirstOrDefault(r => string.Equals(r.RelatedEntityName, relatedEntityName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 基本的なプロパティ設定を保持するビューモデル
    /// </summary>
    public class PropertyConfigViewModel
    {
        /// <summary>
        /// プロパティの名前
        /// </summary>
        public string PropertyName { get; set; }

        /// <summary>
        /// このプロパティにカスタム生成戦略を使用するかどうか
        /// </summary>
        public bool UseCustomStrategy { get; set; }

        /// <summary>
        /// カスタム値（文字列形式）
        /// </summary>
        public string CustomValue { get; set; }

        /// <summary>
        /// カスタム開始値（主キーなどの連番に使用）
        /// </summary>
        public int CustomStartValue { get; set; } = 1;

        /// <summary>
        /// シードデータから除外するかどうか
        /// </summary>
        public bool ExcludeFromSeed { get; set; }

        /// <summary>
        /// プロパティの型名
        /// </summary>
        public string PropertyTypeName { get; set; }
    }

    /// <summary>
    /// Enum型プロパティの設定を保持するビューモデル
    /// </summary>
    public class EnumPropertyConfigViewModel : PropertyConfigViewModel
    {
        /// <summary>
        /// Enum値の選択戦略
        /// </summary>
        public EnumValueStrategy Strategy { get; set; } = EnumValueStrategy.UseAll;

        /// <summary>
        /// 選択された値のリスト（UseSpecific戦略の場合）
        /// </summary>
        public List<string> SelectedValues { get; set; } = new List<string>();

        /// <summary>
        /// ランダム値を使用する場合の生成する値の数
        /// </summary>
        public int ValueCount { get; set; } = 1;

        /// <summary>
        /// Flags属性付きEnumの場合に値を組み合わせるかどうか
        /// </summary>
        public bool CombineValues { get; set; }

        /// <summary>
        /// カスタムマッピング（レコードインデックスと値のマッピング）
        /// </summary>
        public Dictionary<int, object> CustomMapping { get; set; } = new Dictionary<int, object>();
    }

    /// <summary>
    /// リレーションシップの設定を保持するビューモデル
    /// </summary>
    public class RelationshipConfigViewModel
    {
        /// <summary>
        /// 関連エンティティの名前
        /// </summary>
        public string RelatedEntityName { get; set; }

        /// <summary>
        /// リレーションシップの戦略
        /// </summary>
        public RelationshipStrategy Strategy { get; set; } = RelationshipStrategy.OneToOne;

        /// <summary>
        /// 親エンティティのレコード数（ManyToOne戦略の場合）
        /// </summary>
        public int ParentRecordCount { get; set; } = 1;

        /// <summary>
        /// 親エンティティのレコードごとの子エンティティの数（OneToMany戦略の場合）
        /// </summary>
        public int ChildrenPerParent { get; set; } = 2;

        /// <summary>
        /// カスタムマッピング（レコードインデックスと外部キー値のマッピング）
        /// </summary>
        public Dictionary<int, int> CustomMapping { get; set; } = new Dictionary<int, int>();

        /// <summary>
        /// 親エンティティのIDを取得（OneToMany戦略の場合）
        /// </summary>
        /// <param name="childIndex">子エンティティのインデックス</param>
        /// <returns>親エンティティのID</returns>
        public string GetParentId(int childIndex)
        {
            // カスタムマッピングがある場合はそれを優先
            if (Strategy == RelationshipStrategy.Custom && CustomMapping.TryGetValue(childIndex, out int customId))
            {
                return customId.ToString();
            }

            // OneToMany戦略の場合
            if (Strategy == RelationshipStrategy.OneToMany && ChildrenPerParent > 0)
            {
                int parentIndex = childIndex / ChildrenPerParent + 1;
                return parentIndex.ToString();
            }

            // デフォルトは1対1マッピング
            return (childIndex + 1).ToString();
        }
    }

    /// <summary>
    /// Enum値の選択戦略
    /// </summary>
    public enum EnumValueStrategy
    {
        /// <summary>
        /// すべての値を順番に使用
        /// </summary>
        UseAll,

        /// <summary>
        /// 特定の値のみを使用
        /// </summary>
        UseSpecific,

        /// <summary>
        /// ランダムな値を使用
        /// </summary>
        Random,

        /// <summary>
        /// カスタム割り当てを使用
        /// </summary>
        Custom
    }

    /// <summary>
    /// リレーションシップの戦略
    /// </summary>
    public enum RelationshipStrategy
    {
        /// <summary>
        /// 1対1のマッピング（同じインデックスのレコード同士をマッピング）
        /// </summary>
        OneToOne,

        /// <summary>
        /// 多対1のマッピング（複数のレコードが同じ親を参照）
        /// </summary>
        ManyToOne,

        /// <summary>
        /// 1対多のマッピング（親レコードごとに複数の子レコード）
        /// </summary>
        OneToMany,

        /// <summary>
        /// カスタムマッピング（明示的に指定）
        /// </summary>
        Custom
    }

    /// <summary>
    /// 循環参照の処理方法
    /// </summary>
    public enum CircularReferenceHandling
    {
        /// <summary>
        /// 一方の参照にnullを設定して循環を破る
        /// </summary>
        BreakWithNull,

        /// <summary>
        /// エラーをスロー
        /// </summary>
        ThrowError,

        /// <summary>
        /// 遅延ロード用のプロキシオブジェクトを使用
        /// </summary>
        UseLazyLoading
    }
}