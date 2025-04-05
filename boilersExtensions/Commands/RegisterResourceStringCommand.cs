// Commands/RegisterResourceStringCommand.cs
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;
using EnvDTE;
using Microsoft.VisualStudio.TextManager.Interop;
using System.IO;
using boilersExtensions.Dialogs;
using boilersExtensions.ViewModels;
using boilersExtensions.Helpers;
using boilersExtensions.Views;
using System.Text.RegularExpressions;
using System.Text;
using System.Collections.Generic;

namespace boilersExtensions.Commands
{
    internal sealed class RegisterResourceStringCommand : OleMenuCommand
    {
        public const int CommandId = 0x0100; // Use a unique ID
        public static readonly Guid CommandSet = new Guid("fc96ce15-b963-4ccc-9ff5-df9a98e78f73");

        private static AsyncPackage package;

        private RegisterResourceStringCommand() : base(Execute, new CommandID(CommandSet, CommandId))
        {
            base.BeforeQueryStatus += BeforeQueryStatus;
        }

        public static RegisterResourceStringCommand Instance { get; private set; }
        private static IAsyncServiceProvider ServiceProvider => package;

        public static async Task InitializeAsync(AsyncPackage package)
        {
            RegisterResourceStringCommand.package = package;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new RegisterResourceStringCommand();
            Instance.Text = ResourceService.GetString("RegisterResourceString");
            commandService.AddCommand(Instance);

            Debug.WriteLine("RegisterResourceStringCommand initialized successfully");
        }

        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {

                // Get DTE
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                if (dte?.ActiveDocument == null)
                {
                    ShowDialogMessage("No active document.", icon: OLEMSGICON.OLEMSGICON_INFO);
                    return;
                }

                // Get selected text
                var textSelection = dte.ActiveDocument.Selection as TextSelection;
                if (textSelection == null || textSelection.IsEmpty)
                {
                    ShowDialogMessage("No text selected.", icon: OLEMSGICON.OLEMSGICON_INFO);
                    return;
                }

                string selectedText = textSelection.Text;

                // Show dialog to get resource key and culture
                var viewModel = new RegisterResourceDialogViewModel
                {
                    SelectedText = selectedText,
                    Package = package
                };

                var continueLoop = true;

                while (continueLoop)
                {
                    var dialog = new RegisterResourceDialog { DataContext = viewModel };
                    bool? result = dialog.ShowDialog();

                    if (result == true)
                    {
                        // 選択テキストから補完式を抽出
                        var interpolationExpressions = ExtractInterpolationExpressions(selectedText);

                        // プレースホルダー形式に変換
                        string placeholderText = selectedText;
                        if (interpolationExpressions.Count > 0)
                        {
                            placeholderText = ConvertToPlaceholderFormat(selectedText, interpolationExpressions);
                        }

                        // Add to resource file
                        if (AddToResourceFile(viewModel.ResourceKey.Value, placeholderText, viewModel.SelectedCulture.Value))
                        {
                            // Replace selected text with resource access code
                            string replacementCode = GenerateResourceAccessCode(
                                viewModel.ResourceKey.Value,
                                placeholderText,
                                viewModel.ResourceClassName.Value,
                                interpolationExpressions.ToArray());
                            textSelection.Text = replacementCode;

                            ShowDialogMessage($"Added '{selectedText}' to resources with key '{viewModel.ResourceKey}'");
                            continueLoop = false;
                        }
                        else
                        {
                            //もう一度ダイアログを表示
                        }
                    }
                    else
                    {
                        continueLoop = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Execute: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                ShowDialogMessage($"Error: {ex.Message}", icon: OLEMSGICON.OLEMSGICON_WARNING);
            }
        }

        /// <summary>
        /// 文字列補完パターンをプレースホルダー形式に変換します
        /// </summary>
        /// <param name="value">元の文字列</param>
        /// <param name="extractedExpressions">抽出された補完式のリスト</param>
        /// <returns>プレースホルダー形式に変換された文字列とマッピング情報</returns>
        private static string ConvertToPlaceholderFormat(string value, List<string> extractedExpressions)
        {
            if (string.IsNullOrEmpty(value) || extractedExpressions.Count == 0)
                return value;

            // $"..."形式の文字列のみを処理
            if (!(value.StartsWith("$\"") || value.StartsWith("$@\"")))
                return value;

            // 文字列の先頭の$と最後の"を取り除く
            string content = value.StartsWith("$@\"")
                ? value.Substring(3, value.Length - 4)
                : value.Substring(2, value.Length - 3);

            // エスケープされた{{や}}を一時トークンに置き換え
            string tempToken1 = "##DOUBLE_OPEN_BRACE##";
            string tempToken2 = "##DOUBLE_CLOSE_BRACE##";

            string temp = content
                .Replace("{{", tempToken1)
                .Replace("}}", tempToken2);

            // {式}形式を{n}形式に置換
            for (int i = 0; i < extractedExpressions.Count; i++)
            {
                string expression = extractedExpressions[i];
                // エスケープして正規表現で使用可能にする
                string escapedExpression = Regex.Escape(expression);
                temp = Regex.Replace(temp, $"\\{{{escapedExpression}\\}}", $"{{{i}}}");
            }

            // 元のエスケープ文字を戻す
            return temp
                .Replace(tempToken1, "{{")
                .Replace(tempToken2, "}}");
        }

        /// <summary>
        /// 文字列補完で使用されている式を抽出します
        /// </summary>
        /// <param name="text">解析対象のテキスト</param>
        /// <returns>抽出された補完式のリスト</returns>
        private static List<string> ExtractInterpolationExpressions(string text)
        {
            var result = new List<string>();

            if (string.IsNullOrEmpty(text))
                return result;

            // 文字列補完の式を抽出（$"..."形式の文字列内の{...}を探す）
            if (text.StartsWith("$\"") || text.StartsWith("$@\""))
            {
                // エスケープされた{{や}}を一時的に無視
                string tempText = text.Replace("{{", "##OPEN##").Replace("}}", "##CLOSE##");

                // 補完式を抽出（{...}で囲まれた部分）
                var matches = Regex.Matches(tempText, @"\{([^{}]+)\}");

                foreach (Match match in matches)
                {
                    // 元の式を取得
                    string expression = match.Groups[1].Value.Trim();
                    result.Add(expression);
                }
            }

            return result;
        }

        private static bool AddToResourceFile(string resourceKey, string resourceValue, string culture)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                // Get DTE
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                var solution = dte.Solution;

                // Find resource file in the solution
                string resourceFilename = culture == "default" ?
                    "Resource.resx" : $"Resource.{culture}.resx";

                // Find the resource file in the solution
                var resourceFile = FindResourceFile(solution, resourceFilename);

                if (resourceFile == null)
                {
                    ShowDialogMessage($"Resource file '{resourceFilename}' not found in solution.", icon: OLEMSGICON.OLEMSGICON_INFO);
                    return false;
                }

                // 文字列補完パターンの特別処理 - プレースホルダー形式に変換済みの場合は不要
                if (resourceValue.StartsWith("$\"") || resourceValue.StartsWith("$@\""))
                {
                    // 単一の{を{{に、単一の}を}}に置換
                    resourceValue = EscapeInterpolationBraces(resourceValue);
                }

                // Open the file
                var resourceDoc = resourceFile.Open("{7651A701-06E5-11D1-8EBD-00A0C90F26EA}"); // GUID for vsViewKindCode
                resourceDoc.Activate();

                //resourceValueの値がダブルクォーテーションで囲まれている場合は、取り外す
                if (resourceValue.StartsWith("\"") && resourceValue.EndsWith("\""))
                {
                    resourceValue = resourceValue.Substring(1, resourceValue.Length - 2);
                }

                // Add the resource
                bool success = false;

                // Get the resource editor
                dynamic resXEditor = resourceDoc.Document.Object("VSResourceEditor.ResXFileEditor");
                if (resXEditor != null)
                {
                    // Add the resource
                    resXEditor.AddResource(resourceKey, resourceValue, "System.String");
                    resourceDoc.Document.Save();
                    success = true;
                }
                else
                {
                    // If the resource editor isn't available, we'll need to manually edit the file
                    success = ManuallyAddToResourceFile(resourceFile.Document.FullName, resourceKey, resourceValue);
                }

                resourceDoc.Close();
                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding to resource file: {ex.Message}");
                ShowDialogMessage($"Error adding to resource file: {ex.Message}", icon: OLEMSGICON.OLEMSGICON_WARNING);
                return false;
            }
        }

        /// <summary>
        /// 文字列補完パターンで使用される中括弧をエスケープします
        /// </summary>
        /// <param name="value">元の文字列</param>
        /// <returns>エスケープされた文字列</returns>
        private static string EscapeInterpolationBraces(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            // 既にエスケープされている{{や}}をいったん別の文字列に置き換え
            string tempToken1 = "##DOUBLE_OPEN_BRACE##";
            string tempToken2 = "##DOUBLE_CLOSE_BRACE##";

            string temp = value
                .Replace("{{", tempToken1)
                .Replace("}}", tempToken2);

            // 単一の{や}を{{や}}に置き換え
            temp = Regex.Replace(temp, @"(?<!\{)\{(?!\{)", "{{");
            temp = Regex.Replace(temp, @"(?<!\})\}(?!\})", "}}");

            // 元のエスケープ文字を戻す
            return temp
                .Replace(tempToken1, "{{")
                .Replace(tempToken2, "}}");
        }

        private static ProjectItem FindResourceFile(Solution solution, string resourceFilename)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            foreach (Project project in solution.Projects)
            {
                var resourceItem = FindResourceFileInProject(project, resourceFilename);
                if (resourceItem != null)
                {
                    return resourceItem;
                }
            }

            return null;
        }

        private static ProjectItem FindResourceFileInProject(Project project, string resourceFilename)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (project.ProjectItems == null)
                return null;

            foreach (ProjectItem item in project.ProjectItems)
            {
                if (item.Name.Equals(resourceFilename, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }

                // Check if this is a folder
                if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                {
                    var found = SearchProjectItemsRecursively(item, resourceFilename);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static ProjectItem SearchProjectItemsRecursively(ProjectItem item, string resourceFilename)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            foreach (ProjectItem subItem in item.ProjectItems)
            {
                if (subItem.Name.Equals(resourceFilename, StringComparison.OrdinalIgnoreCase))
                {
                    return subItem;
                }

                if (subItem.ProjectItems != null && subItem.ProjectItems.Count > 0)
                {
                    var found = SearchProjectItemsRecursively(subItem, resourceFilename);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static bool ManuallyAddToResourceFile(string filePath, string resourceKey, string resourceValue)
        {
            try
            {
                // Read the existing file
                string content = File.ReadAllText(filePath);

                // Find the appropriate position to insert the new resource
                int insertPosition = content.LastIndexOf("</root>");
                if (insertPosition < 0)
                {
                    return false;
                }

                // Create the XML for the new resource
                string newResource = $@"  <data name=""{resourceKey}"" xml:space=""preserve"">
    <value>{resourceValue}</value>
  </data>
";

                // Insert the new resource
                string newContent = content.Insert(insertPosition, newResource);

                // Write back to the file
                File.WriteAllText(filePath, newContent);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error manually adding to resource file: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// リソースにアクセスするコードを生成します
        /// </summary>
        /// <param name="resourceKey">リソースキー</param>
        /// <param name="resourceValue">リソース値（プレースホルダー解析用）</param>
        /// <param name="formatParameters">プレースホルダーに対応するパラメータ名の配列（オプション）</param>
        /// <returns>生成されたコード</returns>
        private static string GenerateResourceAccessCode(string resourceKey, string resourceValue, string resourceClassName, params string[] formatParameters)
        {
            if (!string.IsNullOrEmpty(resourceClassName))
            {
                return $"{resourceClassName}.{resourceKey}";
            }
            else
            {
                // プレースホルダー（{0}、{1}など）の数を数える
                int placeholderCount = CountPlaceholders(resourceValue);

                if (placeholderCount == 0)
                {
                    // プレースホルダーがない場合は単純に文字列を取得
                    return $"ResourceService.GetString(\"{resourceKey}\")";
                }
                else
                {
                    // プレースホルダーがある場合はstring.Formatを使用
                    StringBuilder formatParams = new StringBuilder();

                    // 提供されたパラメータ名を使用
                    for (int i = 0; i < placeholderCount; i++)
                    {
                        // パラメータ名が提供されている場合はそれを使用、それ以外は汎用名
                        string paramName = (i < formatParameters.Length) ? formatParameters[i] : $"param{i + 1}";

                        if (i > 0) formatParams.Append(", ");
                        formatParams.Append(paramName);
                    }

                    return $"string.Format(ResourceService.GetString(\"{resourceKey}\"), {formatParams})";
                }
            }
        }

        /// <summary>
        /// 文字列内のプレースホルダー（{0}、{1}など）の数を数えます
        /// </summary>
        /// <param name="value">対象の文字列</param>
        /// <returns>プレースホルダーの数</returns>
        private static int CountPlaceholders(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            // エスケープされた{{や}}を一時的に置き換え
            string temp = value.Replace("{{", "##OPEN##").Replace("}}", "##CLOSE##");

            // {n}形式のプレースホルダーを探す
            var matches = Regex.Matches(temp, @"\{(\d+)\}");

            if (matches.Count == 0)
                return 0;

            // 最大のインデックス番号を見つける
            int maxIndex = -1;
            foreach (Match match in matches)
            {
                if (int.TryParse(match.Groups[1].Value, out int index))
                {
                    maxIndex = Math.Max(maxIndex, index);
                }
            }

            // インデックスは0から始まるので、最大値+1がプレースホルダーの数
            return maxIndex + 1;
        }

        private static void BeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand command)
            {
                // Make command visible and enabled only when text is selected
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                var textSelection = dte?.ActiveDocument?.Selection as TextSelection;

                command.Visible = true;
                command.Enabled = textSelection != null && !textSelection.IsEmpty;
            }
        }

        private static void ShowMessage(string message)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                    if (dte != null)
                    {
                        dte.StatusBar.Text = message;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Status bar update error: {ex.Message}");
            }
        }

        /// <summary>
        /// メッセージダイアログを表示します
        /// </summary>
        /// <param name="message">表示するメッセージ</param>
        /// <param name="title">ダイアログのタイトル（省略可）</param>
        /// <param name="icon">表示するアイコン（省略可）</param>
        private static void ShowDialogMessage(string message, string title = "boilersExtensions", OLEMSGICON icon = OLEMSGICON.OLEMSGICON_INFO)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // ダイアログを表示
                    VsShellUtilities.ShowMessageBox(
                        package,
                        message,
                        title,
                        icon,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                    // ステータスバーにも表示（オプション）
                    var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                    if (dte != null)
                    {
                        dte.StatusBar.Text = message;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Dialog display error: {ex.Message}");

                // 最後の砦としてのフォールバック：通常のメッセージだけでも表示を試みる
                try
                {
                    ShowMessage(message);
                }
                catch { }
            }
        }
    }
}