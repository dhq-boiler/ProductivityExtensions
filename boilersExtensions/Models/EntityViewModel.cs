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
    public class EntityViewModel : ViewModelBase
    {
        public ReactivePropertySlim<string> Name { get; } = new ReactivePropertySlim<string>();
        public ReactivePropertySlim<string> FullName { get; } = new ReactivePropertySlim<string>();
        public ReactivePropertySlim<int> RecordCount { get; } = new ReactivePropertySlim<int>(10);
        public ReactivePropertySlim<bool> IsSelected { get; } = new ReactivePropertySlim<bool>(true);
        public ReactivePropertySlim<string> FilePath { get; } = new ReactivePropertySlim<string>();

        public ObservableCollection<PropertyViewModel> Properties { get; } = new ObservableCollection<PropertyViewModel>();
        public ObservableCollection<RelationshipViewModel> Relationships { get; } = new ObservableCollection<RelationshipViewModel>();

        // プロパティ設定のコレクションを追加
        public ObservableCollection<PropertyConfigViewModel> PropertyConfigs { get; } = new ObservableCollection<PropertyConfigViewModel>();

        public EntityViewModel()
        {
            // ReactivePropertyをDisposablesコレクションに追加
            Name.AddTo(Disposables);
            FullName.AddTo(Disposables);
            RecordCount.AddTo(Disposables);
            IsSelected.AddTo(Disposables);
            FilePath.AddTo(Disposables);
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

    public class RelationshipViewModel : ViewModelBase
    {
        public ReactivePropertySlim<string> SourceEntityName { get; } = new ReactivePropertySlim<string>();
        public ReactivePropertySlim<string> TargetEntityName { get; } = new ReactivePropertySlim<string>();
        public ReactivePropertySlim<string> SourceProperty { get; } = new ReactivePropertySlim<string>();
        public ReactivePropertySlim<string> TargetProperty { get; } = new ReactivePropertySlim<string>();
        public ReactivePropertySlim<RelationshipType> RelationType { get; } = new ReactivePropertySlim<RelationshipType>(RelationshipType.OneToMany);

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
