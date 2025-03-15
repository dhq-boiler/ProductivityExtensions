using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using boilersExtensions.Properties;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Prism.Mvvm;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using MessageBox = System.Windows.MessageBox;
using Process = System.Diagnostics.Process;
using Window = System.Windows.Window;

namespace boilersExtensions.ViewModels
{
    internal class RenameSolutionDialogViewModel : BindableBase, IDisposable
    {
        private readonly CompositeDisposable _compositeDisposable = new CompositeDisposable();

        public RenameSolutionDialogViewModel()
        {
            RenameSolutionCommand = OldSolutionName.CombineLatest(NewSolutionName,
                    (oldName, newName) => oldName != null && newName != null && !oldName.Equals(newName))
                .ToReactiveCommand();
            RenameSolutionCommand.Subscribe(() =>
                {
                    //プロジェクト名変更の手続き...
                    if (!OldSolutionName.Value.Equals(NewSolutionName.Value))
                    {
                        RenameSolution();
                        Window.Close();
                    }
                })
                .AddTo(_compositeDisposable);
            CancelCommand.Subscribe(() =>
                {
                    Window.Close();
                })
                .AddTo(_compositeDisposable);
        }

        public ReactiveCommand RenameSolutionCommand { get; }
        public ReactiveCommand CancelCommand { get; } = new ReactiveCommand();

        public ReactivePropertySlim<string> OldSolutionName { get; } = new ReactivePropertySlim<string>();

        public ReactivePropertySlim<string> NewSolutionName { get; } = new ReactivePropertySlim<string>();

        public ReactivePropertySlim<bool> WillRenameParentDir { get; } = new ReactivePropertySlim<bool>(true);

        public Window Window { get; set; }

        public AsyncPackage Package { get; set; }

        public string Title => Resource.Title_RenameProject;

        public void Dispose()
        {
            _compositeDisposable?.Dispose();
            RenameSolutionCommand?.Dispose();
            CancelCommand?.Dispose();
            OldSolutionName?.Dispose();
            NewSolutionName?.Dispose();
            OldSolutionName?.Dispose();
            NewSolutionName?.Dispose();
            WillRenameParentDir?.Dispose();
        }

        private async Task RenameSolution()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);

            // DTE オブジェクトの取得
            var dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));

            var solution = dte.Solution;

            var projects = dte.Solution.Projects;

            var hasProjects = projects.Count != 0;

            var isSameDir = false;

            if (hasProjects)
            {
                isSameDir = BothSolutionDirAndProjectDirIsSame(solution.FileName, projects.Item(1).FileName);
            }

            var oldSolutionPath = solution.FileName;
            var newSolutionPath = RenameSolutionFile(solution);

            if (WillRenameParentDir.Value)
            {
                newSolutionPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(newSolutionPath)),
                    NewSolutionName.Value,
                    Path.GetFileName(newSolutionPath));
            }

            if (isSameDir)
            {
                // Visual Studioのパス（通常はこれですが、環境によって異なる場合があります）
                var vsPath = GetVisualStudio2022Path();

                //// Visual Studioを新しいソリューションで再起動
                var args = new List<string>();
                args.AddRange(vsPath);
                var regex =
                    new Regex(@"^[a-zA-Z]:\\([\p{L}a-zA-Z0-9_ \-]+\\)*[\p{L}a-zA-Z0-9_ \-]+(\.[\p{L}a-zA-Z0-9_]+)?$");
                if (regex.IsMatch(args.Last()))
                {
                    args.Remove(args.Last());
                }

                args.Add(newSolutionPath);
                var arguments = args.Skip(1);
                var argumentsStr = string.Join(" ", arguments);

                // 拡張機能のインストールパスを取得
                var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                var extensionDirectory = Path.GetDirectoryName(assemblyLocation);

                // バッチファイルのパスを構築
                var batchFilePath = Path.Combine(extensionDirectory, "Batches", "BE001.bat");

                // バッチファイルの存在を確認
                if (!File.Exists(batchFilePath))
                {
                    MessageBox.Show($"バッチファイルが見つかりません。{batchFilePath}");
                    return;
                }

                argumentsStr =
                    $"{vsPath[0]} {WillRenameParentDir.Value} \"{Path.GetFileNameWithoutExtension(newSolutionPath)}\" \"{newSolutionPath}\" \"{argumentsStr}\" {Path.GetDirectoryName(oldSolutionPath).Substring(Path.GetDirectoryName(oldSolutionPath).LastIndexOf('\\') + 1)}";

                var startInfo = new ProcessStartInfo
                {
                    FileName = batchFilePath,
                    CreateNoWindow = true, // コマンドウィンドウを開かない
                    WindowStyle = ProcessWindowStyle.Hidden, // ウィンドウを隠す
                    UseShellExecute = false, // シェル実行を使用しない
                    Arguments = argumentsStr
                };
                var process = new Process { StartInfo = startInfo };
                process.Start();

                // Visual Studioを閉じる
                dte.Quit();
            }
            else
            {
                // Visual Studioのパス（通常はこれですが、環境によって異なる場合があります）
                var vsPath = GetVisualStudio2022Path();

                //// Visual Studioを新しいソリューションで再起動
                var args = new List<string>();
                args.AddRange(vsPath);
                var regex =
                    new Regex(@"^[a-zA-Z]:\\([\p{L}a-zA-Z0-9_ \-]+\\)*[\p{L}a-zA-Z0-9_ \-]+(\.[\p{L}a-zA-Z0-9_]+)?$");
                if (regex.IsMatch(args.Last()))
                {
                    args.Remove(args.Last());
                }

                args.Add(newSolutionPath);
                var arguments = args.Skip(1);
                var argumentsStr = string.Join(" ", arguments);

                // 拡張機能のインストールパスを取得
                var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                var extensionDirectory = Path.GetDirectoryName(assemblyLocation);

                // バッチファイルのパスを構築
                var batchFilePath = Path.Combine(extensionDirectory, "Batches", "BE001.bat");

                // バッチファイルの存在を確認
                if (!File.Exists(batchFilePath))
                {
                    MessageBox.Show($"バッチファイルが見つかりません。{batchFilePath}");
                    return;
                }

                argumentsStr =
                    $"{vsPath[0]} {WillRenameParentDir.Value} \"{Path.GetFileNameWithoutExtension(newSolutionPath)}\" \"{newSolutionPath}\" \"{argumentsStr}\" {Path.GetDirectoryName(oldSolutionPath).Substring(Path.GetDirectoryName(oldSolutionPath).LastIndexOf('\\') + 1)}";

                var startInfo = new ProcessStartInfo
                {
                    FileName = batchFilePath,
                    CreateNoWindow = true, // コマンドウィンドウを開かない
                    WindowStyle = ProcessWindowStyle.Hidden, // ウィンドウを隠す
                    UseShellExecute = false, // シェル実行を使用しない
                    Arguments = argumentsStr
                };
                var process = new Process { StartInfo = startInfo };
                process.Start();

                // Visual Studioを閉じる
                dte.Quit();
            }
        }

        private bool BothSolutionDirAndProjectDirIsSame(string solutionFilePath, string projectFilePath)
        {
            var slnDir = Path.GetDirectoryName(solutionFilePath);
            var prjDir = Path.GetDirectoryName(projectFilePath);
            return slnDir == prjDir;
        }

        private string[] GetVisualStudio2022Path() => Environment.GetCommandLineArgs();

        private string RenameParentDirectoryName(string solutionFileName)
        {
            var parentDir = Path.GetDirectoryName(solutionFileName);
            Rename(parentDir, Path.Combine(Path.GetDirectoryName(parentDir), NewSolutionName.Value));
            return Path.Combine(Path.GetDirectoryName(parentDir), NewSolutionName.Value,
                $"{NewSolutionName.Value}.sln");
        }

        private string RenameSolutionFile(Solution solution)
        {
            var ret = Path.Combine(Path.GetDirectoryName(solution.FileName), $"{NewSolutionName.Value}.sln");
            Rename(solution.FileName, ret);
            return ret;
        }

        public void OnDialogOpened(Window window)
        {
            Window = window;
            NewSolutionName.Value = OldSolutionName.Value;
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