namespace ActualChat.Chat.UI.Blazor.Components;

public static class ChatMessageModelsBuilder
{
    private const int BlockLengthLimit = 1000;
    private static readonly TimeSpan SplitPauseDuration = TimeSpan.FromSeconds(120);

    internal static IEnumerable<ChatMessageModel> Build(List<ChatEntry> chatEntries, Dictionary<Symbol, Users.Author?> authors)
    {
        var i = 0;
        var blockLength = 0;
        var blockStarts = true;
        var lastIndex = chatEntries.Count - 1;
        
        foreach (var entry in chatEntries) {
            if (blockStarts)
                blockLength = 0;
            var blockEnds = i >= lastIndex
                || ShouldSeparateEntries(entry, chatEntries[i + 1])
                || blockLength >= BlockLengthLimit;
            var model = new ChatMessageModel(entry, authors[entry.AuthorId]!) {
                IsBlockStart = blockStarts,
                IsBlockEnd = blockEnds,
            };
            yield return model;
            i++;
            blockStarts = blockEnds;
            blockLength += entry.Content.Length;
        }
    }

    private static bool ShouldSeparateEntries(ChatEntry? prev, ChatEntry? next)
    {
        if (prev == null || next == null)
            return true;
        if (prev.AuthorId != next.AuthorId)
            return true;
        if (prev.EndsAt + SplitPauseDuration < next.BeginsAt)
            return true;
        return false;
    }
}
