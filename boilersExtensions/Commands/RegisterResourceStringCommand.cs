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
                    ShowMessage("No active document.");
                    return;
                }

                // Get selected text
                var textSelection = dte.ActiveDocument.Selection as TextSelection;
                if (textSelection == null || textSelection.IsEmpty)
                {
                    ShowMessage("No text selected.");
                    return;
                }

                string selectedText = textSelection.Text;

                // Show dialog to get resource key and culture
                var viewModel = new RegisterResourceDialogViewModel
                {
                    SelectedText = selectedText,
                    Package = package
                };

                var dialog = new RegisterResourceDialog { DataContext = viewModel };
                bool? result = dialog.ShowDialog();

                if (result == true)
                {
                    // Add to resource file
                    if (AddToResourceFile(viewModel.ResourceKey.Value, selectedText, viewModel.SelectedCulture.Value))
                    {
                        // Replace selected text with resource access code
                        string replacementCode = GenerateResourceAccessCode(viewModel.ResourceKey.Value, viewModel.ResourceClassName.Value);
                        textSelection.Text = replacementCode;

                        ShowMessage($"Added '{selectedText}' to resources with key '{viewModel.ResourceKey}'");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Execute: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                ShowMessage($"Error: {ex.Message}");
            }
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
                    ShowMessage($"Resource file '{resourceFilename}' not found in solution.");
                    return false;
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
                ShowMessage($"Error adding to resource file: {ex.Message}");
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

        private static string GenerateResourceAccessCode(string resourceKey, string resourceClassName)
        {
            // If user specified a custom resource class
            if (!string.IsNullOrEmpty(resourceClassName))
            {
                return $"{resourceClassName}.{resourceKey}";
            }

            // Default to using our ResourceService
            return $"ResourceService.GetString(\"{resourceKey}\")";
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
    }
}