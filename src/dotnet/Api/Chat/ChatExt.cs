namespace ActualChat.Chat;

public static class ChatExt
{
    public static bool IsMember(this Chat chat)
        => chat.Rules.IsMember();

    public static bool IsMember(this AuthorRules authorRules)
        => authorRules.Author is { HasLeft: false };

    public static bool RequiresOwner(this ChatDiff diff)
        => diff.Kind.HasValue
            || diff.IsPublic.HasValue
            || diff.IsTemplate.HasValue
            || diff.TemplateId.HasValue
            || diff.TemplatedForUserId.HasValue
            || diff.AllowGuestAuthors.HasValue
            || diff.AllowAnonymousAuthors.HasValue
            || diff.SystemTag.HasValue
            || diff.IsArchived.HasValue
            || diff.IsSummarized.HasValue
            || diff.PttEnabledAt.HasValue
            || diff.PlaceId is not null
            || diff.AliasId is not null;

    public static bool RequiresOwner(this PlaceDiff diff)
        => diff.IsPublic.HasValue
            || diff.AliasId is not null;
}
