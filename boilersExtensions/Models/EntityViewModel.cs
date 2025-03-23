using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using boilersExtensions.ViewModels;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace boilersExtensions.Models
{
    /// <summary>
    /// エンティティ情報を保持するViewModel（改修版）
    /// </summary>
    public class EntityViewModel : ViewModelBase
    {
        /// <summary>
        /// エンティティ名
        /// </summary>
        public ReactivePropertySlim<string> Name { get; } = new ReactivePropertySlim<string>();

        /// <summary>
        /// 完全修飾名
        /// </summary>
        public ReactivePropertySlim<string> FullName { get; } = new ReactivePropertySlim<string>();

        /// <summary>
        /// 親エンティティを持たない場合の生成レコード数
        /// </summary>
        public ReactivePropertySlim<int> RecordCount { get; } = new ReactivePropertySlim<int>(10);

        /// <summary>
        /// 親エンティティ1件あたりの子レコード数
        /// </summary>
        public ReactivePropertySlim<int> RecordsPerParent { get; } = new ReactivePropertySlim<int>(2);

        /// <summary>
        /// 親エンティティの参照（親がある場合）
        /// </summary>
        public ReactivePropertySlim<EntityViewModel> ParentEntity { get; } = new ReactivePropertySlim<EntityViewModel>(null);

        /// <summary>
        /// 合計レコード数（計算プロパティ）
        /// </summary>
        public ReactivePropertySlim<int> TotalRecordCount { get; } = new ReactivePropertySlim<int>(10);

        /// <summary>
        /// このエンティティを選択するかどうか
        /// </summary>
        public ReactivePropertySlim<bool> IsSelected { get; } = new ReactivePropertySlim<bool>(true);

        /// <summary>
        /// ファイルパス
        /// </summary>
        public ReactivePropertySlim<string> FilePath { get; } = new ReactivePropertySlim<string>();

        /// <summary>
        /// プロパティ一覧
        /// </summary>
        public ObservableCollection<PropertyViewModel> Properties { get; } = new ObservableCollection<PropertyViewModel>();

        /// <summary>
        /// リレーションシップ一覧
        /// </summary>
        public ObservableCollection<RelationshipViewModel> Relationships { get; } = new ObservableCollection<RelationshipViewModel>();

        /// <summary>
        /// プロパティ設定のコレクション
        /// </summary>
        public ObservableCollection<PropertyConfigViewModel> PropertyConfigs { get; } = new ObservableCollection<PropertyConfigViewModel>();

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public EntityViewModel()
        {
            // ReactivePropertyをDisposablesコレクションに追加
            Name.AddTo(Disposables);
            FullName.AddTo(Disposables);
            RecordCount.AddTo(Disposables);
            RecordsPerParent.AddTo(Disposables);
            ParentEntity.AddTo(Disposables);
            TotalRecordCount.AddTo(Disposables);
            IsSelected.AddTo(Disposables);
            FilePath.AddTo(Disposables);

            // 合計レコード数の計算
            // 親がある場合: 親の合計レコード数 × 親1件あたりの件数
            // 親がない場合: 自身のレコード数
            RecordCount.Subscribe(_ => UpdateTotalRecordCount()).AddTo(Disposables);
            RecordsPerParent.Subscribe(_ => UpdateTotalRecordCount()).AddTo(Disposables);
            ParentEntity.Subscribe(_ => UpdateTotalRecordCount()).AddTo(Disposables);
        }

        /// <summary>
        /// 合計レコード数を更新
        /// </summary>
        private void UpdateTotalRecordCount()
        {
            if (ParentEntity.Value != null)
            {
                // 親がある場合は「親の合計レコード数 × 親1件あたりの件数」
                TotalRecordCount.Value = ParentEntity.Value.TotalRecordCount.Value * RecordsPerParent.Value;
            }
            else
            {
                // 親がない場合は自身のレコード数
                TotalRecordCount.Value = RecordCount.Value;
            }
        }

        /// <summary>
        /// プロパティ名からPropertyConfigViewModelを取得します
        /// </summary>
        public PropertyConfigViewModel GetPropertyConfig(string propertyName)
        {
            // プロパティ設定コレクションから検索
            var config = PropertyConfigs.FirstOrDefault(pc => pc.PropertyName == propertyName);

            // 設定が見つからない場合は新しく作成
            if (config == null)
            {
                var property = Properties.FirstOrDefault(p => p.Name.Value == propertyName);
                if (property != null)
                {
                    config = new PropertyConfigViewModel
                    {
                        PropertyName = propertyName,
                        PropertyTypeName = property.Type.Value
                    };

                    PropertyConfigs.Add(config);
                }
            }

            return config;
        }
    }

    /// <summary>
    /// リレーションシップ情報を保持するViewModel
    /// </summary>
    public class RelationshipViewModel : ViewModelBase
    {
        /// <summary>
        /// ソースエンティティ名
        /// </summary>
        public ReactivePropertySlim<string> SourceEntityName { get; } = new ReactivePropertySlim<string>();

        /// <summary>
        /// ターゲットエンティティ名
        /// </summary>
        public ReactivePropertySlim<string> TargetEntityName { get; } = new ReactivePropertySlim<string>();

        /// <summary>
        /// ソースプロパティ名
        /// </summary>
        public ReactivePropertySlim<string> SourceProperty { get; } = new ReactivePropertySlim<string>();

        /// <summary>
        /// ターゲットプロパティ名
        /// </summary>
        public ReactivePropertySlim<string> TargetProperty { get; } = new ReactivePropertySlim<string>();

        /// <summary>
        /// リレーションシップの種類
        /// </summary>
        public ReactivePropertySlim<RelationshipType> RelationType { get; } = new ReactivePropertySlim<RelationshipType>(RelationshipType.OneToMany);

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public RelationshipViewModel()
        {
            // ReactivePropertyをDisposablesコレクションに追加
            SourceEntityName.AddTo(Disposables);
            TargetEntityName.AddTo(Disposables);
            SourceProperty.AddTo(Disposables);
            TargetProperty.AddTo(Disposables);
            RelationType.AddTo(Disposables);
        }
    }
}