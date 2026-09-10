using Microsoft.Extensions.Localization;

namespace ActualChat.Localization;

/// <summary>
/// Names a chat the app created by itself - Notes, the announcements chat, an onboarding group, a place's
/// Welcome chat - in the reader's language, for as long as it still carries its default English title.
/// </summary>
public static class ChatTitleLocalizerExt
{
    extension(IStringLocalizer l)
    {
        public Chat.Chat LocalizeTitle(Chat.Chat chat)
        {
            var title = l.GetSystemChatTitle(chat);
            return title is null ? chat : chat with { Title = title };
        }

        // Null for a chat the user named, including a system chat its owner renamed.
        public string? GetSystemChatTitle(Chat.Chat chat)
        {
            if (chat.SystemDefaultTitle is null)
                return null;
            if (chat.Id == Constants.Chat.AnnouncementsChatId)
                return l.SystemChat_Announcements_Format(CoreConstants.AppName);

            if (chat.SystemTag == Constants.Chat.System.Notes)
                return l.SystemChat_Notes;
            if (chat.SystemTag == Constants.Chat.System.Family)
                return l.Onboarding_ChatFamily;
            if (chat.SystemTag == Constants.Chat.System.Friends)
                return l.Onboarding_ChatFriends;
            if (chat.SystemTag == Constants.Chat.System.ClassmatesAlumni)
                return l.Onboarding_ChatClassmates;
            if (chat.SystemTag == Constants.Chat.System.Coworkers)
                return l.Onboarding_ChatCoworkers;
            if (chat.SystemTag == Constants.Chat.System.Welcome)
                return l.SystemChat_Welcome;

            return null;
        }
    }
}
