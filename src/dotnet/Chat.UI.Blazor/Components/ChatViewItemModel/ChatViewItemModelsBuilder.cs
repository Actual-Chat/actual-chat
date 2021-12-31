namespace ActualChat.Chat.UI.Blazor.Components;

public static class ChatViewItemModelsBuilder
{
    private const int BlockLengthLimit = 1000;
    private static readonly TimeSpan SplitPauseDuration = TimeSpan.FromSeconds(120);

    internal static IEnumerable<IChatViewItemModel> Build(List<ChatEntry> chatEntries, Dictionary<Symbol, Users.Author?> authors)
    {
        var i = 0;
        var blockLength = 0;
        var blockStarts = true;
        var lastIndex = chatEntries.Count - 1;
        var lastDate = new DateOnly(1, 1, 1);
        
        foreach (var entry in chatEntries) {
            var id = entry.Id.ToString(CultureInfo.InvariantCulture);
            var currentDate = DateOnly.FromDateTime(entry.BeginsAt.ToDateTime().ToLocalTime());
            if (currentDate > lastDate)
                yield return new ChatDateSeparatorModel(id + $";date-separator:{currentDate.ToString(CultureInfo.InvariantCulture)};", currentDate);

            if (blockStarts)
                blockLength = 0;
            var blockEnds = i >= lastIndex
                || ShouldSeparateEntries(entry, chatEntries[i + 1])
                || blockLength >= BlockLengthLimit;
            var model = new ChatMessageModel(id, entry, authors[entry.AuthorId]!) {
                IsBlockStart = blockStarts,
                IsBlockEnd = blockEnds,
            };
            yield return model;
            i++;
            blockStarts = blockEnds;
            blockLength += entry.Content.Length;
            lastDate = currentDate;
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
