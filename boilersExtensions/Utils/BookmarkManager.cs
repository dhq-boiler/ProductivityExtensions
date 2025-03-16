
using System;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace boilersExtensions.Utils
{
    /// <summary>
    /// Visual Studioのブックマーク機能を管理するクラス
    /// </summary>
    public static class BookmarkManager
    {
        /// <summary>
        /// ブックマークをトグルします
        /// </summary>
        public static async Task<bool> ToggleBookmarkAsync(string filePath, int lineNumber)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // DTE(EnvDTE)サービスを取得
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE;
                if (dte == null)
                    return false;

                // ファイルを開く
                var window = dte.ItemOperations.OpenFile(filePath, EnvDTE.Constants.vsViewKindTextView);
                if (window == null)
                    return false;

                // TextDocumentを取得
                var textDocument = window.Document.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                    return false;

                // カーソルを指定行に移動
                textDocument.Selection.GotoLine(lineNumber);

                // ブックマークをトグル
                dte.ExecuteCommand("Edit.ToggleBookmark");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error toggling bookmark: {ex.Message}");
                return false;
            }
        }

        public static bool IsBookmarkSet(string filePath, int lineNumber)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var openDoc = (IVsUIShellOpenDocument)ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShellOpenDocument));
            if (openDoc == null)
            {
                return false;
            }

            // ファイルを開いて IVsWindowFrame を取得
            Guid logicalView = VSConstants.LOGVIEWID_TextView;
            if (openDoc.OpenDocumentViaProject(
                    filePath,  // ファイルのフルパス
                    ref logicalView,
                    out _,
                    out _,               // IVsUIHierarchy（不要）
                    out _,               // itemID（不要）
                    out IVsWindowFrame windowFrame // ウィンドウフレーム取得
                ) != VSConstants.S_OK || windowFrame == null)
            {
                return false;
            }

            // IVsWindowFrame から IVsTextView を取得
            object docView;
            if (windowFrame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out docView) != VSConstants.S_OK || docView == null)
            {
                return false;
            }

            var textView = docView as IVsTextView;
            if (textView == null)
            {
                // ドキュメントエディタの場合は IVsCodeWindow 経由で IVsTextView を取得
                if (docView is IVsCodeWindow codeWindow)
                {
                    codeWindow.GetPrimaryView(out textView);
                }
            }

            if (textView == null)
            {
                return false;
            }

            // IVsTextLines を取得
            if (textView.GetBuffer(out IVsTextLines textLines) != VSConstants.S_OK || textLines == null)
            {
                return false;
            }

            // マーカー列挙を取得
            if (textLines.EnumMarkers(
                    lineNumber-1,  // 開始行
                    0,           // 開始列
                    lineNumber,  // 終了行
                    0,           // 終了列
                    (int)MARKERTYPE.MARKER_BOOKMARK,
                    (uint)ENUMMARKERFLAGS.EM_ALLTYPES,  // すべてのマーカーを対象にする
                    out IVsEnumLineMarkers enumMarkers  // outパラメーター
                ) != VSConstants.S_OK || enumMarkers == null)
            {
                return false;
            }

            // 取得したマーカーをチェック
            IVsTextLineMarker marker;
            uint fetched;
            while (enumMarkers.Next(out marker) == VSConstants.S_OK)
            {
                if (marker != null)
                {
                    marker.GetType(out int markerType);
                    if (markerType == (int)MARKERTYPE.MARKER_BOOKMARK) // ブックマークの種類を判定
                    {
                        return true; // ブックマークあり
                    }
                }
            }

            return false; // ブックマークなし
        }
    }

    /// <summary>
    /// ブックマーク情報を表すクラス
    /// </summary>
    public class BookmarkInfo
    {
        public string FilePath { get; set; }
        public int LineNumber { get; set; }
        public int ColumnNumber { get; set; }
    }
}