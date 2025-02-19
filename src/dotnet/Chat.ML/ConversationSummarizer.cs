namespace ActualChat.Chat.ML;

public interface IConversationSummarizer
{
    Task<(string Title, string Description, string Summary)> Summarize(
        IReadOnlyCollection<TextEntry> chatEntries,
        CancellationToken cancellationToken);
}

public class ConversationSummarizer: IConversationSummarizer
{
    public async Task<(string Title, string Description, string Summary)> Summarize(
        IReadOnlyCollection<TextEntry> chatEntries,
        CancellationToken cancellationToken)
    {
        var firstEntry = chatEntries.FirstOrDefault();
        var lastEntry = chatEntries.LastOrDefault();
        var count = chatEntries.Count;
        // TODO(AK): Implement proper summarization with NLP ChatGPT 4o-mini
        return new ($"Title {firstEntry!.LocalId} - {lastEntry!.LocalId}",
            $"Description {firstEntry.LocalId} - {lastEntry.LocalId}",
            $"Summary {count} entries");
    }

}
