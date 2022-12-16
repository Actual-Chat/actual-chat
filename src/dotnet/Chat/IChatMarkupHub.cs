namespace ActualChat.Chat;

public interface IChatMarkupHub : IHasServices
{
    ChatId ChatId { get; }

    IMarkupParser Parser { get; }
    MentionNamer MentionNamer { get; }
    IChatMentionResolver MentionResolver { get; }
}
