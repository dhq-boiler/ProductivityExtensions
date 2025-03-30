using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading.Tasks;
using boilersExtensions;
using boilersExtensions.DialogPages;
using boilersExtensions.Helpers;
using boilersExtensions.Properties;
using boilersExtensions.Utils;
using Microsoft.VisualStudio.Shell;

/// <summary>
///     boilersExtensions設定コマンド
/// </summary>
internal sealed class BoilersExtensionsSettingsCommand
{
    public const int CommandId = 0x0100;
    public static readonly Guid CommandSet = new Guid("0A3B7D5F-6D61-4B5E-9A4F-6D0E6F8B3F1C");

    private static AsyncPackage _package;

    /// <summary>
    /// シングルトンインスタンス
    /// </summary>
    public static BoilersExtensionsSettingsCommand Instance { get; private set; }

    /// <summary>
    /// パッケージへの参照を提供
    /// </summary>
    public AsyncPackage Package => _package;

    /// <summary>
    /// サービスプロバイダーへの参照
    /// </summary>
    private static IAsyncServiceProvider ServiceProvider => _package;

    /// <summary>
    /// コマンドを初期化
    /// </summary>
    public static async Task InitializeAsync(AsyncPackage package)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        Instance = new BoilersExtensionsSettingsCommand();

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
        {
            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new OleMenuCommand(ShowSettings, menuCommandID);
            menuItem.Text = ResourceService.GetString("Preferences");
            MenuTextUpdater.RegisterCommand(menuItem, "Preferences");
            commandService.AddCommand(menuItem);
        }
    }

    /// <summary>
    /// 設定ダイアログを表示
    /// </summary>
    private static void ShowSettings(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            // 拡張機能の設定ページを表示
            _package.ShowOptionPage(typeof(BoilersExtensionsOptionPage));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error showing settings: {ex.Message}");
        }
    }
}