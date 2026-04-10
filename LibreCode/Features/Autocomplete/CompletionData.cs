using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace LibreCode.Features.Autocomplete;

/// <summary>
/// Single entry in the code completion popup.
/// Replaces the typed prefix with the full keyword on insertion.
/// </summary>
public sealed class CompletionData : ICompletionData
{
    public CompletionData(string text)
    {
        Text = text;
    }

    public IImage? Image => null;

    public string Text { get; }

    public object Content => Text;

    public object Description => Text;

    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }
}
