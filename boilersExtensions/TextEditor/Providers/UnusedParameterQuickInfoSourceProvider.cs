using boilersExtensions.TextEditor.QuickInfoSources;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace boilersExtensions.TextEditor.Providers
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("Unused Parameter Quick Info Source Provider")]
    [ContentType("any")]
    [Order]
    internal class UnusedParameterQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        [Import]
        internal VisualStudioWorkspace Workspace { get; set; }

        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return new UnusedParameterQuickInfoSource(textBuffer, Workspace);
        }
    }
}
