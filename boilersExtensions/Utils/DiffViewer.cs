using System.IO;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace boilersExtensions.Utils
{
    internal class DiffViewer
    {
        private readonly IVsDifferenceService _differenceService;

        public DiffViewer() => _differenceService =
            Package.GetGlobalService(typeof(SVsDifferenceService)) as IVsDifferenceService;

        public IVsWindowFrame ShowDiff(string originalCode, string newCode, bool isReadOnly = true)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 一時ファイルの作成
            var leftFile = CreateTempFile(originalCode, "original.cs");
            var rightFile = CreateTempFile(newCode, "modified.cs");

            // VSDIFFOPTフラグの設定
            var diffOptions = (uint)(
                __VSDIFFSERVICEOPTIONS.VSDIFFOPT_DetectBinaryFiles | // バイナリファイルの検出
                __VSDIFFSERVICEOPTIONS.VSDIFFOPT_LeftFileIsTemporary | // 左側を一時ファイルとして扱う
                __VSDIFFSERVICEOPTIONS.VSDIFFOPT_RightFileIsTemporary // 右側を一時ファイルとして扱う
            );

            if (isReadOnly)
            {
                diffOptions |= (uint)__VSDIFFSERVICEOPTIONS.VSDIFFOPT_RightFileIsTemporary; // 右側も読み取り専用にする場合
            }

            // Diffビューの表示
            var windowFrame = _differenceService.OpenComparisonWindow2(
                leftFile, // 左側（オリジナル）
                rightFile, // 右側（変更後）
                "コード変更の推薦", // キャプション
                "以下の変更を推薦します", // ツールチップ
                null, //left label
                null, //right label
                null, //inline label
                null, //roles
                diffOptions // ビューオプション
            );

            windowFrame.Show();
            return windowFrame;
        }

        private string CreateTempFile(string content, string filename)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), filename);
            File.WriteAllText(tempPath, content);
            return tempPath;
        }

        // 一時ファイルのクリーンアップ
        public void Cleanup(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (IOException)
                {
                    // ファイルが使用中の場合は無視
                }
            }
        }
    }
}