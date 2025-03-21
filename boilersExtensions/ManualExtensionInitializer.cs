using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using boilersExtensions.Commands;
using boilersExtensions.TextEditor.Extensions;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;

namespace boilersExtensions
{
    /// <summary>
    ///     エディタの拡張機能を手動で初期化するためのクラス
    /// </summary>
    public static class ManualExtensionInitializer
    {
        private static bool _initialized;

        private static readonly Dictionary<IWpfTextView, RegionNavigatorExtension> _extensions =
            new Dictionary<IWpfTextView, RegionNavigatorExtension>();

        /// <summary>
        ///     パッケージが読み込まれた際に手動で初期化
        /// </summary>
        public static void Initialize(AsyncPackage package)
        {
            if (_initialized)
            {
                return;
            }

            try
            {
                // UIスレッドに切り替え（非同期のため）
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    Debug.WriteLine("ManualExtensionInitializer: Starting to initialize extensions");

                    // EditorAdaptersFactoryService を取得
                    var componentModel = await package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
                    if (componentModel == null)
                    {
                        Debug.WriteLine("ManualExtensionInitializer: Failed to get SComponentModel service");
                        return;
                    }

                    var editorAdaptersFactoryService = componentModel.GetService<IVsEditorAdaptersFactoryService>();
                    var navigatorService = componentModel.GetService<ITextStructureNavigatorSelectorService>();

                    if (editorAdaptersFactoryService == null || navigatorService == null)
                    {
                        Debug.WriteLine("ManualExtensionInitializer: Required services not available");
                        return;
                    }

                    // イベントハンドラを登録
                    await RegisterEventHandlers(package, editorAdaptersFactoryService, navigatorService);

                    // 現在開いているテキストビューを取得して初期化
                    await InitializeActiveTextView(package, editorAdaptersFactoryService, navigatorService);

                    //コマンドを初期化
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await NavigateGitHubLinesCommand.InitializeAsync(package);
                        await RenameProjectCommand.InitializeAsync(package);
                        await RenameSolutionCommand.InitializeAsync(package);
                        await UpdateGuidCommand.InitializeAsync(package);
                        await BatchUpdateGuidCommand.InitializeAsync(package);
                        await TypeHierarchyCommand.InitializeAsync(package);
                        await RegionNavigatorCommand.InitializeAsync(package);
                        await SyncToSolutionExplorerCommand.InitializeAsync(package);
                    });

                    _initialized = true;
                    Debug.WriteLine("ManualExtensionInitializer: Successfully initialized");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ManualExtensionInitializer: Error during initialization: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        ///     イベントハンドラを登録
        /// </summary>
        private static async Task RegisterEventHandlers(AsyncPackage package,
            IVsEditorAdaptersFactoryService editorAdaptersFactoryService,
            ITextStructureNavigatorSelectorService navigatorService)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // ウィンドウフレームのイベントを監視
                var windowFrameEvents = await package.GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
                if (windowFrameEvents != null)
                {
                    Debug.WriteLine("ManualExtensionInitializer: Registering for window frame events");

                    // ドキュメントウィンドウのイベントを監視する
                    var vsMonitorSelection =
                        await package.GetServiceAsync(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
                    if (vsMonitorSelection != null)
                    {
                        uint cookie = 0;
                        vsMonitorSelection.AdviseSelectionEvents(
                            new SelectionEventHandler(editorAdaptersFactoryService, navigatorService), out cookie);
                        Debug.WriteLine($"ManualExtensionInitializer: Registered selection events, cookie: {cookie}");
                    }
                }

                Debug.WriteLine("ManualExtensionInitializer: Successfully registered event handlers");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ManualExtensionInitializer: Error registering event handlers: {ex.Message}");
            }
        }

        /// <summary>
        ///     現在アクティブなテキストビューを初期化
        /// </summary>
        private static async Task InitializeActiveTextView(AsyncPackage package,
            IVsEditorAdaptersFactoryService editorAdaptersFactoryService,
            ITextStructureNavigatorSelectorService navigatorService)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // アクティブなテキストビューを取得
                var textManager = await package.GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
                if (textManager != null)
                {
                    textManager.GetActiveView(1, null, out var vsTextView);
                    if (vsTextView != null)
                    {
                        // TextViewを取得してRegionNavigatorExtensionを追加
                        var wpfTextView = editorAdaptersFactoryService.GetWpfTextView(vsTextView);
                        if (wpfTextView != null)
                        {
                            AttachToTextView(wpfTextView, navigatorService);
                            Debug.WriteLine("ManualExtensionInitializer: Attached to active text view");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ManualExtensionInitializer: Error initializing active text view: {ex.Message}");
            }
        }

        /// <summary>
        ///     テキストビューに拡張機能をアタッチ
        /// </summary>
        public static void AttachToTextView(IWpfTextView textView,
            ITextStructureNavigatorSelectorService navigatorService)
        {
            try
            {
                if (textView == null || navigatorService == null)
                {
                    Debug.WriteLine("AttachToTextView: TextView or NavigatorService is null");
                    return;
                }

                // すでにアタッチされているかチェック
                if (_extensions.ContainsKey(textView))
                {
                    Debug.WriteLine("AttachToTextView: Extension already attached to this TextView");
                    return;
                }

                // TextView.Propertiesで確認
                if (textView.Properties.ContainsProperty(typeof(RegionNavigatorExtension)))
                {
                    Debug.WriteLine("AttachToTextView: Extension already exists in TextView properties");
                    return;
                }

                // 新しいRegionNavigatorExtensionをテキストビューにアタッチ
                var extension = new RegionNavigatorExtension(textView, navigatorService);

                // 辞書に追加して追跡
                _extensions[textView] = extension;

                // テキストビューのプロパティにも追加
                textView.Properties.AddProperty(typeof(RegionNavigatorExtension), extension);

                // テキストビューが閉じられたときにクリーンアップするためのハンドラ
                textView.Closed += (s, e) =>
                {
                    if (_extensions.ContainsKey(textView))
                    {
                        _extensions.Remove(textView);
                        Debug.WriteLine("TextView Closed: Removed extension from tracking dictionary");
                    }
                };

                Debug.WriteLine(
                    $"Successfully attached new RegionNavigatorExtension to TextView (hash: {textView.GetHashCode()})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error attaching extension to TextView: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        ///     ドキュメント選択イベントを処理するためのクラス
        /// </summary>
        private class SelectionEventHandler : IVsSelectionEvents
        {
            private readonly IVsEditorAdaptersFactoryService _editorAdaptersFactoryService;
            private readonly ITextStructureNavigatorSelectorService _navigatorService;

            public SelectionEventHandler(
                IVsEditorAdaptersFactoryService editorAdaptersFactoryService,
                ITextStructureNavigatorSelectorService navigatorService)
            {
                _editorAdaptersFactoryService = editorAdaptersFactoryService;
                _navigatorService = navigatorService;
            }

            public int OnCmdUIContextChanged(uint dwCmdUICookie, int fActive) => VSConstants.S_OK;

            public int OnElementValueChanged(uint elementid, object varValueOld, object varValueNew)
            {
                if (elementid == (uint)VSConstants.VSSELELEMID.SEID_DocumentFrame)
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        try
                        {
                            if (varValueNew is IVsWindowFrame frame)
                            {
                                // ドキュメントのテキストビューを取得
                                frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView);

                                if (docView is IVsTextView vsTextView)
                                {
                                    var wpfTextView = _editorAdaptersFactoryService.GetWpfTextView(vsTextView);
                                    if (wpfTextView != null)
                                    {
                                        AttachToTextView(wpfTextView, _navigatorService);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error in OnElementValueChanged: {ex.Message}");
                        }
                    });
                }

                return VSConstants.S_OK;
            }

            public int OnSelectionChanged(IVsHierarchy pHierOld, uint itemidOld, IVsMultiItemSelect pMISOld,
                ISelectionContainer pSCOld, IVsHierarchy pHierNew, uint itemidNew, IVsMultiItemSelect pMISNew,
                ISelectionContainer pSCNew) =>
                VSConstants.S_OK;
        }
    }
}