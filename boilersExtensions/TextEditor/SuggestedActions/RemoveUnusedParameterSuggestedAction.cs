using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Differencing;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace boilersExtensions.TextEditor.SuggestedActions
{
    internal class RemoveUnusedParameterSuggestedAction : ISuggestedAction
    {
        private ITrackingSpan m_span;
        private readonly IDifferenceBufferFactoryService diffBufferFactory;
        private readonly ITextDifferencingSelectorService diffSelector;
        private readonly IWpfDifferenceViewerFactoryService wpfDifferenceViewerFactoryService;
        private ITextSnapshot m_snapshot;
        
        public RemoveUnusedParameterSuggestedAction(ITrackingSpan span, IDifferenceBufferFactoryService diffBufferFactory, ITextDifferencingSelectorService diffSelector, IWpfDifferenceViewerFactoryService wpfDifferenceViewerFactoryService)
        {

            m_snapshot = span.TextBuffer.CurrentSnapshot;            m_span = span;
            this.diffBufferFactory = diffBufferFactory;
            this.diffSelector = diffSelector;
            this.wpfDifferenceViewerFactoryService = wpfDifferenceViewerFactoryService;
            DisplayText = $"未使用のパラメーター '{span.GetText(m_snapshot)}' を削除します";
        }

        public async Task<object> GetPreviewAsync(CancellationToken cancellationToken)
        {
            return null;
        }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<SuggestedActionSet>>(null);
        }

        public bool HasActionSets => false;

        public string DisplayText { get; }

        public ImageMoniker IconMoniker => default;

        public string IconAutomationText => null;

        public string InputGestureText => null;

        public bool HasPreview => false;

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
