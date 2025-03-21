using boilersExtensions.DialogPages;
using boilersExtensions;
using Microsoft.VisualStudio.Shell;
using System.ComponentModel.Design;
using System.Diagnostics;

using System;
using System.Threading.Tasks;

/// <summary>
/// boilersExtensions設定コマンド
/// </summary>
internal sealed class BoilersExtensionsSettingsCommand
{
    public const int CommandId = 0x0100;
    public static readonly Guid CommandSet = new Guid("0A3B7D5F-6D61-4B5E-9A4F-6D0E6F8B3F1C");
    public AsyncPackage Package { get; }

    private BoilersExtensionsSettingsCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        this.Package = package ?? throw new ArgumentNullException(nameof(package));
        commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

        var menuCommandID = new CommandID(CommandSet, CommandId);
        var menuItem = new MenuCommand(ExecuteSettings, menuCommandID);
        commandService.AddCommand(menuItem);
    }

    public static BoilersExtensionsSettingsCommand Instance { get; private set; }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        Instance = new BoilersExtensionsSettingsCommand(package, commandService);
    }

    private void ExecuteSettings(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            Package.ShowOptionPage(typeof(BoilersExtensionsOptionPage));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error opening settings: {ex.Message}");
        }
    }
}