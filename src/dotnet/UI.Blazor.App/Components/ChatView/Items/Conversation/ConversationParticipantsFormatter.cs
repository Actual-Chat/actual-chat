namespace ActualChat.UI.Blazor.App.Components;

public static class ConversationParticipantsFormatter
{
    public static async Task<string> GetText(
        IAuthors authors,
        Session session,
        ChatId chatId,
        IReadOnlyList<AuthorId> authorIds,
        CancellationToken cancellationToken)
    {
        var names = new List<string>();
        foreach (var authorId in authorIds.Take(3)) {
            var author = await authors.Get(session, chatId, authorId, cancellationToken).ConfigureAwait(false);
            if (author?.Avatar.Name is { } name && !name.IsNullOrEmpty())
                names.Add(Abbreviate(name));
        }
        if (names.Count == 0)
            return "";

        if (names.Count == 1)
            return names[0];

        var total = authorIds.Count;
        if (total > 3)
            return $"{names[0]}, {names[1]} and {total - 2} {"other".Pluralize(total - 2)}";

        if (names.Count == 2)
            return $"{names[0]} and {names[1]}";

        return $"{names[0]}, {names[1]} and {names[2]}";
    }

    private static string Abbreviate(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? name : $"{parts[0]} {parts[1][0]}.";
    }
}
