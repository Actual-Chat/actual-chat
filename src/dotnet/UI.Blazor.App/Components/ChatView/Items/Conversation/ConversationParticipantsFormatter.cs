using ActualChat.UI.Blazor.Resources;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Components;

public static class ConversationParticipantsFormatter
{
    public static async Task<string> GetText(
        IAuthors authors,
        IStringLocalizer l,
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
        // total > 3 puts at least 2 behind the "and N others" tail, so it never reads "1 others"
        if (total > 3)
            return l.Conversation_NamesAndOthers_Format(names[0], names[1], total - 2);

        return names.Count == 2
            ? l.Conversation_TwoNames_Format(names[0], names[1])
            : l.Conversation_ThreeNames_Format(names[0], names[1], names[2]);
    }

    private static string Abbreviate(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? name : $"{parts[0]} {parts[1][0]}.";
    }
}
