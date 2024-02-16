using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Chat;

public static class AuthorExt
{
    [return: NotNullIfNotNull(nameof(author))]
    public static Author? ToAuthor(this AuthorFull? author)
    {
        if (author == null)
            return null;

        return new Author(author.Id, author.Version) {
            AvatarId = author.AvatarId,
            Avatar = author.Avatar,
            HasLeft = author.HasLeft,
            IsAnonymous = author.IsAnonymous,
        };
    }

    public static AuthorFull RequireValid(this AuthorFull? author)
    {
        author.Require();
        if (!author.ChatId.IsPeerChat(out var peerChatId))
            return author;

        if (author.LocalId is not (1 or 2))
            throw StandardError.Constraint($"Peer chat authors should have LocalId = 1 or 2, but found '{author}'.");

        var peerIndex = peerChatId.IndexOf(author.UserId);
        if (author.LocalId != peerIndex + 1)
            throw StandardError.Constraint($"Peer chat authors' LocalId should indicate position at peer chat id, but found '{author}'.");

        return author;
    }
}
