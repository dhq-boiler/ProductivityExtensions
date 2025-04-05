using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using boilersExtensions.Helpers;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace boilersExtensions.ViewModels
{
    public class XamlRegisterResourceStringDialogViewModel : ViewModelBase
    {
        // 元のXAML
        public string OriginalXaml { get; set; }

        // 変換後のXAML
        public ReactivePropertySlim<string> ConvertedXaml { get; } = new ReactivePropertySlim<string>();

        // プレビュー用テキスト
        public ReactivePropertySlim<string> PreviewText { get; } = new ReactivePropertySlim<string>();

        // リソース名前空間
        public ReactivePropertySlim<string> ResourceNamespace { get; } = new ReactivePropertySlim<string>("helpers");

        // リソースクラス
        public ReactivePropertySlim<string> ResourceClass { get; } = new ReactivePropertySlim<string>("ResourceService");

        // 選択した文化
        public ReactivePropertySlim<string> SelectedCulture { get; } = new ReactivePropertySlim<string>("default");

        // 処理対象の属性（Text, Content, ToolTip, Header など）
        public ReactivePropertySlim<List<string>> TargetAttributes { get; } = new ReactivePropertySlim<List<string>>(
            new List<string> { "Text", "Content", "ToolTip", "Header" });

        // 利用可能な文化のリスト
        public List<string> AvailableCultures { get; } = new List<string>
        {
            "default",
            "en-US",
            "ja-JP"
            // 必要に応じて追加
        };

        // 検出されたテキスト属性の項目リスト
        public ReactiveCollection<XamlTextItem> DetectedTextItems { get; } = new ReactiveCollection<XamlTextItem>();

        public XamlRegisterResourceStringDialogViewModel()
        {
            // リアクティブプロパティのサブスクリプション
            ResourceNamespace.Subscribe(_ => UpdatePreview()).AddTo(Disposables);
            ResourceClass.Subscribe(_ => UpdatePreview()).AddTo(Disposables);
            SelectedCulture.Subscribe(_ => UpdatePreview()).AddTo(Disposables);
        }

        // 初期化処理
        public void Initialize()
        {
            if (string.IsNullOrEmpty(OriginalXaml))
                return;

            // XAML内のテキスト属性を検出
            DetectTextAttributes();

            // プレビューを更新
            UpdatePreview();
        }

        // XAML内のテキスト属性を検出
        private void DetectTextAttributes()
        {
            try
            {
                DetectedTextItems.Clear();

                // XAML解析用の正規表現パターン
                // 実際の実装ではより堅牢なXML解析が必要かもしれません
                foreach (var attrName in TargetAttributes.Value)
                {
                    var pattern = $"{attrName}=\"([^\"]*)\"";
                    var regex = new Regex(pattern);
                    var matches = regex.Matches(OriginalXaml);

                    foreach (Match match in matches)
                    {
                        string text = match.Groups[1].Value;
                        // 既にリソースバインディングになっているものや空文字は除外
                        if (text.Contains("{Binding") || string.IsNullOrWhiteSpace(text))
                            continue;

                        // 提案するリソースキーを生成
                        string suggestedKey = GenerateSuggestedKey(text);

                        DetectedTextItems.Add(new XamlTextItem
                        {
                            OriginalText = text,
                            AttributeName = attrName,
                            ResourceKey = suggestedKey,
                            IsSelected = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting text attributes: {ex.Message}");
            }
        }

        // プレビューを更新
        public void UpdatePreview()
        {
            if (string.IsNullOrEmpty(OriginalXaml))
                return;

            try
            {
                // 選択された項目のみを変換
                string previewXaml = OriginalXaml;

                foreach (var item in DetectedTextItems.Where(i => i.IsSelected))
                {
                    previewXaml = Regex.Replace(previewXaml,
                        $"{item.AttributeName}=\"([^\"]*)\"",
                        $"{item.AttributeName}=\"{{Binding Source={{x:Static helpers:ResourceService.Current}}, Path=Resource.{item.ResourceKey}, Mode=OneWay}}\"");
                }

                // XAMLの名前空間宣言を確認し、必要なら追加
                previewXaml = EnsureNamespaceDeclaration(previewXaml);

                ConvertedXaml.Value = previewXaml;
                PreviewText.Value = previewXaml;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating preview: {ex.Message}");
                PreviewText.Value = $"Error updating preview: {ex.Message}";
            }
        }

        // 提案するリソースキーを生成
        private string GenerateSuggestedKey(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // 単語分割して最初の数単語を使用してPascalCaseキーを生成
            string[] words = text.Split(' ', '.', ',', ':', ';', '!', '?', '\r', '\n');
            string key = string.Join("", words.Take(3)
                .Where(w => !string.IsNullOrEmpty(w))
                .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));

            // 長さを制限し、無効な文字を除去
            key = new string(key.Take(50).Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

            if (string.IsNullOrEmpty(key))
                key = "Resource" + DateTime.Now.Ticks.ToString().Substring(0, 5);

            return key;
        }

        // XAMLの名前空間宣言を確認し、必要なら追加
        private string EnsureNamespaceDeclaration(string xaml)
        {
            try
            {
                // 名前空間宣言の存在チェック
                string namespaceCheck = $"xmlns:{ResourceNamespace.Value}=";

                if (xaml.Contains(namespaceCheck))
                    return xaml;

                // 名前空間宣言を追加
                int insertPosition = xaml.IndexOf("xmlns");
                if (insertPosition < 0)
                    return xaml;  // XMLではないかもしれない

                // 最後のxmlns宣言を見つける
                int lastXmlnsPos = xaml.LastIndexOf("xmlns");
                int endOfLastXmlns = xaml.IndexOf("\"", xaml.IndexOf("\"", lastXmlnsPos) + 1) + 1;

                if (endOfLastXmlns > 0)
                {
                    // 名前空間を追加
                    string helperNamespace = $"\r\n    xmlns:{ResourceNamespace.Value}=\"clr-namespace:boilersExtensions.Helpers\"";
                    xaml = xaml.Insert(endOfLastXmlns, helperNamespace);
                }

                return xaml;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error ensuring namespace: {ex.Message}");
                return xaml;  // 元のXAMLを返す
            }
        }

        // 選択された項目をリソースに登録し、XAMLを変換する
        public bool ConvertAndRegisterResources()
        {
            try
            {
                // 選択されたアイテムだけをリソースに登録
                foreach (var item in DetectedTextItems.Where(i => i.IsSelected))
                {
                    RegisterResourceString(item.ResourceKey, item.OriginalText, SelectedCulture.Value);
                }

                // プレビューの内容に基づいてXAMLを最終更新
                UpdatePreview();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting and registering resources: {ex.Message}");
                return false;
            }
        }

        // リソース文字列を登録（既存のAddToResourceFileメソッドを再利用）
        private bool RegisterResourceString(string resourceKey, string resourceValue, string culture)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                // Get DTE
                var dte = (DTE)AsyncPackage.GetGlobalService(typeof(DTE));
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
                ShowDialogMessage(string.Format(ResourceService.GetString("ErrorAddingToResourceFile"), ex.Message), icon: OLEMSGICON.OLEMSGICON_WARNING);
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

        private static void ShowMessage(string message)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var dte = (DTE)AsyncPackage.GetGlobalService(typeof(DTE));
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
        private void ShowDialogMessage(string message, string title = "boilersExtensions", OLEMSGICON icon = OLEMSGICON.OLEMSGICON_INFO)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // ダイアログを表示
                    VsShellUtilities.ShowMessageBox(
                        Package,
                        message,
                        title,
                        icon,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                    // ステータスバーにも表示（オプション）
                    var dte = (DTE)AsyncPackage.GetGlobalService(typeof(DTE));
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

    // XAMLテキスト項目クラス
    public class XamlTextItem
    {
        public string OriginalText { get; set; }
        public string AttributeName { get; set; }
        public string ResourceKey { get; set; }
        public bool IsSelected { get; set; }
    }
}