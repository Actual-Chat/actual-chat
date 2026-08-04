namespace ActualChat.Chat.UnitTests;

public static class AuthorExt
{
    public static MentionRef ToMentionId(this Author author)
        => MentionRef.NewAuthor(author.Id);

    public static MentionMarkup ToMentionMarkup(this Author author)
        => MentionMarkup.New(author.ToMentionId(), author.Avatar.Name);
}
