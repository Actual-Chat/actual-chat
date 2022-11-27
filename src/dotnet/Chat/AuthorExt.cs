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

}
