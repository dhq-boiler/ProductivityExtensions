using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace boilersExtensions.TextEditor.SuggestedActions
{
    internal class RemoveUnusedParameterSuggestedAction : ISuggestedAction
    {
        private ITrackingSpan m_span;
        private string m_display;
        private ITextSnapshot m_snapshot;

        public RemoveUnusedParameterSuggestedAction(ITrackingSpan span)
        {
            m_span = span;
            m_snapshot = span.TextBuffer.CurrentSnapshot;
            m_display = $"未使用のパラメーター '{span.GetText(m_snapshot)}' を削除します";
        }

        public async Task<object> GetPreviewAsync(CancellationToken cancellationToken)
        {
            // プレビューのUIを構築
            var textBlock = new TextBlock
            {
                Padding = new Thickness(5),
                MaxWidth = 400
            };

            try
            {
                // パラメーターを含むメソッド宣言全体を取得
                var methodDeclaration = await GetMethodDeclarationAsync(m_span, cancellationToken);
                if (methodDeclaration == null)
                {
                    return null;
                }

                // 変更前のコードを表示
                var beforeText = new TextBlock
                {
                    Padding = new Thickness(5)
                };
                beforeText.Inlines.Add(new Run("変更前:") { FontWeight = FontWeights.Bold });
                beforeText.Inlines.Add(new Run("\n" + methodDeclaration));

                // 変更後のコードを表示
                var afterText = new TextBlock
                {
                    Padding = new Thickness(5, 15, 5, 5)
                };
                afterText.Inlines.Add(new Run("変更後:") { FontWeight = FontWeights.Bold });

                // 削除するパラメーターを除いたメソッド宣言を生成
                var modifiedDeclaration = await GetModifiedMethodDeclarationAsync(methodDeclaration, m_span, cancellationToken);
                afterText.Inlines.Add(new Run("\n" + modifiedDeclaration));

                // StackPanelに配置
                var stackPanel = new StackPanel();
                stackPanel.Children.Add(beforeText);
                stackPanel.Children.Add(afterText);

                return stackPanel;
            }
            catch (Exception ex)
            {
                textBlock.Text = "プレビューを生成できません。";
                return textBlock;
            }
        }

        private async Task<string> GetMethodDeclarationAsync(ITrackingSpan span, CancellationToken cancellationToken)
        {
            var snapshot = span.TextBuffer.CurrentSnapshot;
            var currentLine = snapshot.GetLineFromPosition(span.GetStartPoint(snapshot));

            // メソッド宣言の開始行を探す
            var line = currentLine;
            var methodText = new System.Text.StringBuilder();

            while (line != null && !line.GetText().Contains("{"))
            {
                methodText.AppendLine(line.GetText());
                if (line.LineNumber == 0) break;
                line = snapshot.GetLineFromLineNumber(line.LineNumber - 1);
            }

            return methodText.ToString().Trim();
        }

        private async Task<string> GetModifiedMethodDeclarationAsync(string originalDeclaration, ITrackingSpan parameterSpan, CancellationToken cancellationToken)
        {
            var parameterText = parameterSpan.GetText(parameterSpan.TextBuffer.CurrentSnapshot);
            var snapshot = parameterSpan.TextBuffer.CurrentSnapshot;

            // パラメーターの位置情報を取得
            var paramStart = parameterSpan.GetStartPoint(snapshot);
            var paramEnd = parameterSpan.GetEndPoint(snapshot);

            // パラメーターとそれに関連するカンマを削除
            var modifiedText = originalDeclaration;
            var spanText = parameterSpan.GetText(snapshot);

            if (spanText.StartsWith(","))
            {
                // カンマで始まる場合（後続パラメーターがある場合）
                modifiedText = modifiedText.Replace(spanText, "");
            }
            else if (spanText.EndsWith(","))
            {
                // カンマで終わる場合（前のパラメーターがある場合）
                modifiedText = modifiedText.Replace(spanText, "");
            }
            else
            {
                // 単独のパラメーターの場合
                modifiedText = modifiedText.Replace(spanText, "");
                // 残ったカンマをクリーンアップ
                modifiedText = modifiedText.Replace(",,", ",").Replace("(,", "(").Replace(",)", ")");
            }

            return modifiedText;
        }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<SuggestedActionSet>>(null);
        }

        public bool HasActionSets
        {
            get { return false; }
        }
        public string DisplayText
        {
            get { return m_display; }
        }
        public ImageMoniker IconMoniker
        {
            get { return default(ImageMoniker); }
        }
        public string IconAutomationText
        {
            get
            {
                return null;
            }
        }
        public string InputGestureText
        {
            get
            {
                return null;
            }
        }
        public bool HasPreview
        {
            get { return true; }
        }

        public void Invoke(CancellationToken cancellationToken)
        {
            m_span.TextBuffer.Replace(m_span.GetSpan(m_snapshot), string.Empty);
        }

        public void Dispose()
        {
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            // This is a sample action and doesn't participate in LightBulb telemetry
            telemetryId = Guid.Empty;
            return false;
        }
    }
}
