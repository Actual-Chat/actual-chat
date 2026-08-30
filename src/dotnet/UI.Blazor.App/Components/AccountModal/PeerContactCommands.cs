using ActualChat.Contacts;
using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public static class PeerContactCommands
{
    public static Task AddToContacts(AppUIHub hub, Contact contact)
        => RunChange(hub, contact, Change.Create(contact));

    public static Task RemoveFromContacts(AppUIHub hub, Contact contact, string peerName)
    {
        // Dropping the row is what "not in contacts" is: reads fall back to a Temporary contact
        var l = hub.StringLocalizer;
        return hub.ModalUI.Show(new ConfirmModal.Model(
            true,
            l.Account_RemoveFromContactsConfirm_Format(peerName),
            () => _ = RunChange(hub, contact, Change.Remove<Contact>())) {
            Title = l.Account_RemoveFromContacts,
            ConfirmButtonText = l.Common_Remove,
        });
    }

    public static Task SetBlocked(AppUIHub hub, Contact contact, bool isBlocked, string peerName)
    {
        if (!isBlocked)
            return hub.PeerBlockUI.SetBlocked(contact.Id, false);

        var l = hub.StringLocalizer;
        return hub.ModalUI.Show(new ConfirmModal.Model(
            true,
            l.Account_BlockConfirm_Format(peerName),
            () => _ = hub.PeerBlockUI.SetBlocked(contact.Id, true)) {
            Title = l.PeerContact_BlockUser,
            ConfirmButtonText = l.Common_Block,
        });
    }

    // Private methods

    private static Task RunChange(AppUIHub hub, Contact contact, Change<Contact> change)
    {
        var command = new Contacts_Change {
            Session = hub.Session,
            Id = contact.Id,
            ExpectedVersion = null,
            Change = change,
        };
        return hub.UICommander.Run(command);
    }
}
