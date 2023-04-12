namespace ActualChat.Chat.UnitTests;

public static class AuthorExt
{
    public static MentionId ToMentionId(this Author author)
        => new (author.Id, AssumeValid.Option);

    public static MentionMarkup ToMentionMarkup(this Author author)
        => new (author.ToMentionId(), author.Avatar.Name);
}
