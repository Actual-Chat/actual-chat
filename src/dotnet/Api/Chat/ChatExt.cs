namespace ActualChat.Chat;

public static class ChatExt
{
    public static bool IsMember(this Chat chat)
        => chat.Rules.IsMember();

    public static bool IsMember(this AuthorRules authorRules)
        => authorRules.Author is { HasLeft: false };

    extension(Chat chat)
    {
        // Null for a chat the app didn't name itself - and for a system chat its owner renamed:
        // the localized name applies only while the stored title is still the default one.

        public string? SystemDefaultTitle
        {
            get
            {
                var defaultTitle = chat.Id == Constants.Chat.AnnouncementsChatId
                    ? Constants.Chat.AnnouncementsChatTitle
                    : Constants.Chat.System.Get(chat.SystemTag)?.DefaultTitle;
                return defaultTitle == chat.Title ? defaultTitle : null;
            }
        }

        public bool IsNotes => chat.SystemTag == Constants.Chat.System.Notes.Tag;

        public bool IsWelcome => chat.SystemTag == Constants.Chat.System.Welcome.Tag;
    }

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
