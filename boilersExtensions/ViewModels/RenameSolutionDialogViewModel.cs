using boilersExtensions.Properties;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Prism.Mvvm;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace boilersExtensions.ViewModels
{
    internal class RenameSolutionDialogViewModel : BindableBase, IDisposable
    {
        private CompositeDisposable _compositeDisposable = new CompositeDisposable();

        public ReactiveCommand RenameSolutionCommand { get; }
        public ReactiveCommand CancelCommand { get; } = new ReactiveCommand();

        public ReactivePropertySlim<string> OldSolutionName { get; } = new ReactivePropertySlim<string>();

        public ReactivePropertySlim<string> NewSolutionName { get; } = new ReactivePropertySlim<string>();

        public ReactivePropertySlim<bool> WillRenameParentDir { get; } = new ReactivePropertySlim<bool>();

        public System.Windows.Window Window { get; set; }

        public AsyncPackage Package { get; set; }

        public string Title => Resource.Title_RenameProject;

        public RenameSolutionDialogViewModel()
        {
            RenameSolutionCommand = OldSolutionName.CombineLatest(NewSolutionName, (oldName, newName) => oldName != null && newName != null && !oldName.Equals(newName))
                                                 .ToReactiveCommand();
            RenameSolutionCommand.Subscribe(() =>
                {
                    //プロジェクト名変更の手続き...
                    if (!OldSolutionName.Value.Equals(NewSolutionName.Value))
                    {
                        RenameSolution();
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

        private async Task RenameSolution()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
            
            // DTE オブジェクトの取得
            DTE dte = (DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE));

            Solution solution = dte.Solution;

            var oldSolutionPath = solution.FileName;
            var newSolutionPath = RenameSolutionFile(solution);

            if (WillRenameParentDir.Value)
            {
                newSolutionPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(newSolutionPath)), NewSolutionName.Value,
                    Path.GetFileName(newSolutionPath));
            }

            // Visual Studioのパス（通常はこれですが、環境によって異なる場合があります）
            string[] vsPath = GetVisualStudio2022Path();

            //// Visual Studioを新しいソリューションで再起動
            var args = new List<string>();
            args.AddRange(vsPath);
            args.Add(newSolutionPath);
            var arguments = args.Skip(1);
            var argumentsStr = string.Join(" ", arguments);

            // 拡張機能のインストールパスを取得
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string extensionDirectory = Path.GetDirectoryName(assemblyLocation);

            // バッチファイルのパスを構築
            string batchFilePath = Path.Combine(extensionDirectory, "Batches", "BE001.bat");

            // バッチファイルの存在を確認
            if (!File.Exists(batchFilePath))
            {
                throw new FileNotFoundException("バッチファイルが見つかりません。", batchFilePath);
            }

            argumentsStr =
                $"{vsPath[0]} {WillRenameParentDir.Value} \"{Path.GetFileNameWithoutExtension(newSolutionPath)}\" \"{newSolutionPath}\" \"{argumentsStr}\", {Path.GetDirectoryName(oldSolutionPath).Substring(Path.GetDirectoryName(oldSolutionPath).LastIndexOf('\\') + 1)}";

            var startInfo = new ProcessStartInfo
            {
                FileName = batchFilePath,
                CreateNoWindow = true, // コマンドウィンドウを開かない
                WindowStyle = ProcessWindowStyle.Hidden, // ウィンドウを隠す
                UseShellExecute = false, // シェル実行を使用しない
                Arguments = argumentsStr
            };
            var process = new System.Diagnostics.Process
            {
                StartInfo = startInfo
            };
            process.Start();

            // Visual Studioを閉じる
            dte.Quit();
        }

        private string[] GetVisualStudio2022Path()
        {
            return Environment.GetCommandLineArgs();
        }

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

        #region https://qiita.com/soi/items/18d5b10b20f5e221efca
        /// <summary>
        /// 確実にファイル／ディレクトリの名前を変更する
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
        /// 確実にディレクトリの名前を変更する
        /// </summary>
        /// <param name="sourceFilePath">変更元ファイルパス</param>
        /// <param name="outputFilePath">変更後ファイルパス</param>
        public static void RenameDirectory(string sourceFilePath, string outputFilePath)
        {
            //Directory.Moveはなぜか、大文字小文字だけの変更だとエラーする
            //なので、大文字小文字だけの変更の場合は一度別のファイル名に変更する
            if ((String.Compare(sourceFilePath, outputFilePath, true) == 0))
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
        /// 指定したファイルパスが他のファイルパスとかぶらなくなるまで"_"を足して返す
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

        public void OnDialogOpened(System.Windows.Window window)
        {
            this.Window = window;
            NewSolutionName.Value = OldSolutionName.Value;
        }

        public void Dispose()
        {
            _compositeDisposable?.Dispose();
            RenameSolutionCommand?.Dispose();
            CancelCommand?.Dispose();
            OldSolutionName?.Dispose();
            NewSolutionName?.Dispose();
        }
    }
}
