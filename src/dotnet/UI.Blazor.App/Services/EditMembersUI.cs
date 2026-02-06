namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Provides utilities for checking member editing capabilities in chats.
/// </summary>
public class EditMembersUI(AppUIHub hub)
{
    private AppUIHub Hub { get; } = hub;

    public async Task<bool> HaveMembersToAdd(Chat.Chat chat)
    {
        if (chat.IsPublicPlaceChat())
            return false;

        var provider = new NewChatMemberSelector(Hub, chat.Id);
        var selected = await provider.ListMemberUserIds(default).ConfigureAwait(false);
        var available = await provider.ListCandidateUserIds(default).ConfigureAwait(false);
        var canAddContacts = available.Except(selected).Any();
        return canAddContacts;
    }

    public bool CanAddMembers(Chat.Chat chat)
    {
        if (!chat.CanInvite())
            return false;

        if (chat.IsPublicPlaceChat())
            return false;

        return true;
    }

    public static bool CanEditMembers(Chat.Chat chat)
    {
        if (!chat.Rules.CanEditMembers())
            return false;

        if (chat.IsPublicPlaceChat())
            return false;

        return true;
    }
}
