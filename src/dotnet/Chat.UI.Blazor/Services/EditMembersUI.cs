namespace ActualChat.Chat.UI.Blazor.Services;

public class EditMembersUI(ChatHub chatHub)
{
    public ChatHub ChatHub { get; } = chatHub;

    public async Task<bool> HaveMembersToAdd(Chat chat)
    {
        bool canAddContacts;
        if (!chat.Id.IsPlaceChat) {
            var peopleContacts = await ChatHub.ChatListUI.ListPeopleContacts();
            canAddContacts = peopleContacts.Count > 0;
        } else {
            if (chat.IsPublic)
                canAddContacts = false;
            else {
                var chatMembers = await ChatHub.Authors.ListUserIds(ChatHub.Session, chat.Id, default);
                var placeMembers = await ChatHub.Authors.ListUserIds(ChatHub.Session, chat.Id.PlaceId.ToRootChatId(), default);
                canAddContacts = placeMembers.Except(chatMembers).Any();
            }
        }
        return canAddContacts;
    }
}
