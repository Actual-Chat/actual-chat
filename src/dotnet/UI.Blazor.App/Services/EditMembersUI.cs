﻿namespace ActualChat.UI.Blazor.App.Services;

public class EditMembersUI(ChatUIHub hub)
{
    private ChatUIHub Hub { get; } = hub;

    public async Task<bool> HaveMembersToAdd(Chat.Chat chat)
    {
        if (chat.IsPublicPlaceChat())
            return false;

        var provider = new NewChatMemberSelector(Hub, chat.Id);
        var selected = await provider.ListPreSelectedUserIds(default).ConfigureAwait(false);
        var available = await provider.ListUserIds(default).ConfigureAwait(false);
        var canAddContacts = available.Except(selected).Any();
        return canAddContacts;
    }

    public static bool CanAddMembers(Chat.Chat chat)
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
