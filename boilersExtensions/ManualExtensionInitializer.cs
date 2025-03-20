using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Shell.Interop;

namespace boilersExtensions
{
    /// <summary>
    /// 拡張機能を手動で初期化するためのヘルパークラス
    /// </summary>
    internal static class ManualExtensionInitializer
    {
        private static Dictionary<IWpfTextView, TextEditor.Extensions.RegionNavigatorExtension> _extensions = 
            new Dictionary<IWpfTextView, TextEditor.Extensions.RegionNavigatorExtension>();

        /// <summary>
        /// 拡張機能を手動で初期化する
        /// </summary>
        /// <param name="package">拡張機能のパッケージ</param>
        public static void Initialize(AsyncPackage package)
        {
            try
            {
                Debug.WriteLine("ManualExtensionInitializer.Initialize: Starting manual extension initialization");

                // ここで特別な初期化が必要な拡張機能を初期化する
                // 現在は特に何もしないが、将来的に必要になった場合のために骨組みを用意

                Debug.WriteLine("ManualExtensionInitializer.Initialize: Finished manual extension initialization");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ManualExtensionInitializer.Initialize: Error during initialization - {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// 現在アクティブなドキュメントに拡張機能を適用
        /// </summary>
        public static void InitializeForActiveDocument(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Debug.WriteLine("InitializeForActiveDocument called");

            try
            {
                // テキストマネージャーからアクティブなビューを取得
                var textManager = (IVsTextManager)serviceProvider.GetService(typeof(SVsTextManager));
                if (textManager == null)
                {
                    Debug.WriteLine("IVsTextManager is null");
                    return;
                }

                textManager.GetActiveView(1, null, out IVsTextView vsTextView);
                if (vsTextView == null)
                {
                    Debug.WriteLine("Active IVsTextView is null");
                    return;
                }

                // コンポーネントモデルからエディタサービスを取得
                var componentModel = (IComponentModel)serviceProvider.GetService(typeof(SComponentModel));
                if (componentModel == null)
                {
                    Debug.WriteLine("IComponentModel is null");
                    return;
                }

                var editorFactory = componentModel.GetService<IVsEditorAdaptersFactoryService>();
                if (editorFactory == null)
                {
                    Debug.WriteLine("IVsEditorAdaptersFactoryService is null");
                    return;
                }

                // WPFテキストビューを取得
                var wpfTextView = editorFactory.GetWpfTextView(vsTextView);
                if (wpfTextView == null)
                {
                    Debug.WriteLine("WpfTextView is null");
                    return;
                }

                // 拡張機能が既に適用されているか確認
                if (_extensions.ContainsKey(wpfTextView))
                {
                    Debug.WriteLine("Extension already applied to this view");
                    return;
                }

                // ナビゲーションサービスを取得
                var navigatorService = componentModel.GetService<ITextStructureNavigatorSelectorService>();
                if (navigatorService == null)
                {
                    Debug.WriteLine("ITextStructureNavigatorSelectorService is null");
                    return;
                }

                // 拡張機能を作成して登録
                var extension = new TextEditor.Extensions.RegionNavigatorExtension(wpfTextView, navigatorService);
                _extensions[wpfTextView] = extension;

                // テキストビューが閉じられたときに拡張機能を解放するためのイベントを登録
                wpfTextView.Closed += (sender, args) =>
                {
                    if (_extensions.ContainsKey(wpfTextView))
                    {
                        _extensions.Remove(wpfTextView);
                        Debug.WriteLine("Extension removed for closed text view");
                    }
                };

                Debug.WriteLine("Extension successfully applied to active document");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in InitializeForActiveDocument: {ex.Message}");
            }
        }

        /// <summary>
        /// 実行中ドキュメントテーブルのイベントを処理するクラス
        /// </summary>
        private class RunningDocTableEvents : IVsRunningDocTableEvents
        {
            private readonly IServiceProvider _serviceProvider;

            public RunningDocTableEvents(IServiceProvider serviceProvider)
            {
                _serviceProvider = serviceProvider;
            }

            public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
            {
                return 0;
            }

            public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
            {
                return 0;
            }

            public int OnAfterSave(uint docCookie)
            {
                return 0;
            }

            public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
            {
                return 0;
            }

            public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
            {
                if (fFirstShow != 0)
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        InitializeForActiveDocument(_serviceProvider);
                    });
                }
                return 0;
            }

            public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
            {
                return 0;
            }
        }
    }
}