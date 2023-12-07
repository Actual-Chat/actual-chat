namespace ActualChat.Chat.UI.Blazor.Services;

public class EditMembersUI(ChatHub chatHub)
{
    public ChatHub ChatHub { get; } = chatHub;

    public async Task<bool> HaveMembersToAdd(Chat chat)
    {
        bool canAddContacts;
        if (!chat.Id.IsPlaceChat) {
            var peopleContacts = await ChatHub.ChatListUI.ListPeopleContacts().ConfigureAwait(false);
            canAddContacts = peopleContacts.Count > 0;
        } else {
            if (chat.IsPublic)
                canAddContacts = false;
            else {
                var chatMembers = await ChatHub.Authors.ListUserIds(ChatHub.Session, chat.Id, default).ConfigureAwait(false);
                var placeMembers = await ChatHub.Authors.ListUserIds(ChatHub.Session, chat.Id.PlaceId.ToRootChatId(), default).ConfigureAwait(false);
                canAddContacts = placeMembers.Except(chatMembers).Any();
            }
        }
        return canAddContacts;
    }

    public bool CanAddMembers(Chat chat)
    {
        if (!chat.CanInvite())
            return false;

        if (chat.IsPublicPlaceChat())
            return false;

        return true;
    }

    public bool CanEditMembers(Chat chat)
    {
        if (chat.Rules.CanEditMembers())
            return false;

        if (chat.IsPublicPlaceChat())
            return false;

        return true;
    }
}
