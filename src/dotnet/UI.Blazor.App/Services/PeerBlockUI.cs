using ActualChat.Contacts;
using ActualLab.Fusion.UI;

namespace ActualChat.UI.Blazor.App.Services;

public class PeerBlockUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private IContacts Contacts => Hub.Contacts;

    [ComputeMethod]
    public virtual async Task<PeerBlockState> GetState(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.Kind != ChatKind.Peer)
            return PeerBlockState.None;

        var contact = await Contacts.GetForChat(Session, chatId, cancellationToken).ConfigureAwait(false);
        return contact is null
            ? PeerBlockState.None
            : new PeerBlockState(contact.Id, contact.IsBlocked, contact.IsBlockedByPeer);
    }

    public Task<UIActionResult<Unit>> SetBlocked(ContactId contactId, bool isBlocked)
        => UICommander.Run(new Contacts_SetIsBlocked(Session, contactId, isBlocked));
}

public sealed record PeerBlockState(ContactId? ContactId, bool IsBlocked, bool IsBlockedByPeer)
{
    public static readonly PeerBlockState None = new(null, false, false);

    public bool CanBlock => ContactId is not null;
}
