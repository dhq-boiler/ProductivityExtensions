using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using boilersExtensions.TextEditor.SuggestedActionsSources;
using Microsoft.VisualStudio.LanguageServices;

namespace boilersExtensions.TextEditor.Providers
{
    [Export(typeof(ISuggestedActionsSourceProvider))]
    [Name("Remove unused parameter")]
    [ContentType("text")]
    internal class UnusedParameterSuggestedActionsSourceProvider : ISuggestedActionsSourceProvider
    {
        [Import(typeof(ITextStructureNavigatorSelectorService))]
        internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }

        [Import]
        internal VisualStudioWorkspace Workspace { get; set; }

        public ISuggestedActionsSource CreateSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer)
        {
            if (textBuffer == null || textView == null)
            {
                return null;
            }
            return new UnusedParameterSuggestedActionsSource(this, textView, textBuffer);
        }
    }
}
