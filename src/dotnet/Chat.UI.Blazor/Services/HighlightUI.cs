namespace ActualChat.Chat.UI.Blazor.Services;

public class HighlightUI
{
    [ComputeMethod]
    public virtual async Task<Range<int>?> GetHighlightRange(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        if (entryId.LocalId == 2285)
            return new Range<int>(8, 25);
        if (entryId.LocalId == 2286)
            return new Range<int>(12, 35);
        return null;
    }
}
