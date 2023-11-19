using Prism.Mvvm;
using Prism.Services.Dialogs;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System;
using System.Reactive.Disposables;
using Microsoft.VisualStudio.Shell;
using System.Windows;
using boilersExtensions.Controls;

namespace boilersExtensions
{
    internal class RenameProjectDialogViewModel : BindableBase, IDisposable
    {
        private CompositeDisposable _compositeDisposable = new CompositeDisposable();

        public ReactiveCommand RenameProjectCommand { get; } = new ReactiveCommand();
        public ReactiveCommand CancelCommand { get; } = new ReactiveCommand();

        public ReactivePropertySlim<string> OldProjectName { get; } = new ReactivePropertySlim<string>();

        public ReactivePropertySlim<string> NewProjectName { get; } = new ReactivePropertySlim<string>();

        public Window Window { get; set; }

        public string Title => $"プロジェクトのリネーム";

        public RenameProjectDialogViewModel()
        {
            RenameProjectCommand.Subscribe(() =>
                {
                    //プロジェクト名変更の手続き...

                    Window.Close();
                })
                .AddTo(_compositeDisposable);
            CancelCommand.Subscribe(() =>
                {
                    Window.Close();
                })
                .AddTo(_compositeDisposable);
        }

        public void OnDialogOpened(Window window)
        {
            this.Window = window;
            NewProjectName.Value = OldProjectName.Value;
            (Window.FindName("projectNameTextBox") as EasyEnterTextBox).Focus();
        }

        public void Dispose()
        {
            _compositeDisposable?.Dispose();
            RenameProjectCommand?.Dispose();
            CancelCommand?.Dispose();
            OldProjectName?.Dispose();
            NewProjectName?.Dispose();
        }
    }
}
