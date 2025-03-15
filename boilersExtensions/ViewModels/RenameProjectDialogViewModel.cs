using System;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using boilersExtensions.Properties;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Prism.Mvvm;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Window = System.Windows.Window;

namespace boilersExtensions.ViewModels
{
    internal class RenameProjectDialogViewModel : BindableBase, IDisposable
    {
        private readonly CompositeDisposable _compositeDisposable = new CompositeDisposable();

        public RenameProjectDialogViewModel()
        {
            RenameProjectCommand = OldProjectName.CombineLatest(NewProjectName,
                    (oldName, newName) => oldName != null && newName != null && !oldName.Equals(newName))
                .ToReactiveCommand();
            RenameProjectCommand.Subscribe(() =>
                {
                    //プロジェクト名変更の手続き...
                    if (!OldProjectName.Value.Equals(NewProjectName.Value))
                    {
                        RenameProject();
                    }

                    Window.Close();
                })
                .AddTo(_compositeDisposable);
            CancelCommand.Subscribe(() =>
                {
                    Window.Close();
                })
                .AddTo(_compositeDisposable);
        }

        public ReactiveCommand RenameProjectCommand { get; }
        public ReactiveCommand CancelCommand { get; } = new ReactiveCommand();

        public ReactivePropertySlim<string> OldProjectName { get; } = new ReactivePropertySlim<string>();

        public ReactivePropertySlim<string> NewProjectName { get; } = new ReactivePropertySlim<string>();

        public Window Window { get; set; }

        public AsyncPackage Package { get; set; }

        public string Title => Resource.Title_RenameProject;

        public void Dispose()
        {
            _compositeDisposable?.Dispose();
            RenameProjectCommand?.Dispose();
            CancelCommand?.Dispose();
            OldProjectName?.Dispose();
            NewProjectName?.Dispose();
        }

        private async Task RenameProject()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);

            // DTE オブジェクトの取得
            var dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));

            // アクティブなプロジェクトの配列を取得
            var activeSolutionProjects = dte.ActiveSolutionProjects as Array;

            // 配列から最初のプロジェクトを取得（通常、アクティブなプロジェクト）
            var activeProject = activeSolutionProjects?.GetValue(0) as Project;

            var projects = dte.Solution.Projects;

            var hasProjects = projects.Count != 0;

            var isSameDir = false;

            if (hasProjects)
            {
                isSameDir = BothSolutionDirAndProjectDirIsSame(dte.Solution.FileName, projects.Item(1).FileName);
            }

            RenameRootNamespace(activeProject);
            await RenameCSharpFiles(dte.Solution.FileName);
            RenameCsproj(activeProject);

            if (!isSameDir)
            {
                RenameCsprojDir(activeProject);
            }

            RenameInSolutionFile(dte.Solution.FileName, OldProjectName.Value, NewProjectName.Value);
        }

        private bool BothSolutionDirAndProjectDirIsSame(string solutionFilePath, string projectFilePath)
        {
            var slnDir = Path.GetDirectoryName(solutionFilePath);
            var prjDir = Path.GetDirectoryName(projectFilePath);
            return slnDir == prjDir;
        }

        private static void RenameInSolutionFile(string solutionFilePath, string oldProjectName, string newProjectName)
        {
            var text = File.ReadAllText(solutionFilePath);
            text = Regex.Replace(text, oldProjectName, newProjectName);
            File.WriteAllText(solutionFilePath, text);
        }

        private void RenameCsprojDir(Project activeProject)
        {
            var csprojFileName = activeProject?.FileName;
            var parentDir = Path.GetDirectoryName(csprojFileName);
            Rename(parentDir, Path.Combine(Path.GetDirectoryName(parentDir), NewProjectName.Value));
        }

        private async Task RenameCSharpFiles(string solutionFileName) =>
            await NamespaceRenamer.RenameNamespaceAsync(solutionFileName, OldProjectName.Value, NewProjectName.Value);

        private void RenameRootNamespace(Project activeProject)
        {
            var csprojFileName = activeProject?.FileName;
            var root = XElement.Load(csprojFileName);
            var propertyGroups = root.Descendants("PropertyGroup");
            foreach (var propertyGroup in propertyGroups)
            {
                var rootNamespaceElm = propertyGroup.Descendants("RootNamespace").FirstOrDefault();
                if (rootNamespaceElm != null)
                {
                    rootNamespaceElm.Value = NewProjectName.Value;
                }
            }
        }

        private void RenameCsproj(Project activeProject)
        {
            var csprojFileName = activeProject?.FileName;
            Rename(csprojFileName,
                Path.Combine(Path.GetDirectoryName(csprojFileName), NewProjectName.Value) +
                Path.GetExtension(csprojFileName));
        }

        public void OnDialogOpened(Window window)
        {
            Window = window;
            NewProjectName.Value = OldProjectName.Value;
        }

        #region https: //qiita.com/soi/items/18d5b10b20f5e221efca

        /// <summary>
        ///     確実にファイル／ディレクトリの名前を変更する
        /// </summary>
        /// <param name="sourceFilePath">変更元ファイルパス</param>
        /// <param name="outputFilePath">変更後ファイルパス</param>
        public static void Rename(string sourceFilePath, string outputFilePath)
        {
            var fileInfo = new FileInfo(sourceFilePath);

            if (fileInfo.Attributes.HasFlag(FileAttributes.Directory))
            {
                RenameDirectory(sourceFilePath, outputFilePath);
            }
            else
            {
                fileInfo.MoveTo(outputFilePath);
            }
        }

        /// <summary>
        ///     確実にディレクトリの名前を変更する
        /// </summary>
        /// <param name="sourceFilePath">変更元ファイルパス</param>
        /// <param name="outputFilePath">変更後ファイルパス</param>
        public static void RenameDirectory(string sourceFilePath, string outputFilePath)
        {
            //Directory.Moveはなぜか、大文字小文字だけの変更だとエラーする
            //なので、大文字小文字だけの変更の場合は一度別のファイル名に変更する
            if (string.Compare(sourceFilePath, outputFilePath, true) == 0)
            {
                var tempPath = GetSafeTempName(outputFilePath);

                Directory.Move(sourceFilePath, tempPath);
                Directory.Move(tempPath, outputFilePath);
            }
            else
            {
                Directory.Move(sourceFilePath, outputFilePath);
            }
        }

        /// <summary>
        ///     指定したファイルパスが他のファイルパスとかぶらなくなるまで"_"を足して返す
        /// </summary>
        private static string GetSafeTempName(string outputFilePath)
        {
            outputFilePath += "_";
            while (File.Exists(outputFilePath))
            {
                outputFilePath += "_";
            }

            return outputFilePath;
        }

        #endregion //https://qiita.com/soi/items/18d5b10b20f5e221efca
    }
}