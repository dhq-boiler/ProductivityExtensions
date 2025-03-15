using System.ComponentModel.Composition;
using boilersExtensions.TextEditor.SuggestedActionsSources;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace boilersExtensions.TextEditor.Providers
{
    [Export(typeof(ISuggestedActionsSourceProvider))]
    [Name("Extract method")]
    [ContentType("text")]
    internal class ExtractMethodSuggestedActionsSourceProvider : ISuggestedActionsSourceProvider
    {
        public ISuggestedActionsSource CreateSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer)
        {
            if (textBuffer == null || textView == null)
            {
                return null;
            }

            return new ExtractMethodSuggestedActionsSource(textView, textBuffer);
        }
    }
}