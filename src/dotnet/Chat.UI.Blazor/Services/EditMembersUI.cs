namespace ActualChat.Chat.UI.Blazor.Services;

public class EditMembersUI(ChatUIHub hub)
{
    public ChatUIHub Hub { get; } = hub;
    private Session Session => Hub.Session();
    private IAuthors Authors => Hub.Authors;
    private ChatListUI ChatListUI => Hub.ChatListUI;

    public async Task<bool> HaveMembersToAdd(Chat chat)
    {
        bool canAddContacts;
        if (!chat.Id.IsPlaceChat) {
            var peopleContacts = await ChatListUI.ListPeopleContacts().ConfigureAwait(false);
            canAddContacts = peopleContacts.Count > 0;
        } else {
            if (chat.IsPublic)
                canAddContacts = false;
            else {
                var chatMembers = await Authors.ListUserIds(Session, chat.Id, default).ConfigureAwait(false);
                var placeMembers = await Authors.ListUserIds(Session, chat.Id.PlaceId.ToRootChatId(), default).ConfigureAwait(false);
                canAddContacts = placeMembers.Except(chatMembers).Any();
            }
        }
        return canAddContacts;
    }

    public static bool CanAddMembers(Chat chat)
    {
        if (!chat.CanInvite())
            return false;

        if (chat.IsPublicPlaceChat())
            return false;

        return true;
    }

    public static bool CanEditMembers(Chat chat)
    {
        if (!chat.Rules.CanEditMembers())
            return false;

        if (chat.IsPublicPlaceChat())
            return false;

        return true;
    }
}
