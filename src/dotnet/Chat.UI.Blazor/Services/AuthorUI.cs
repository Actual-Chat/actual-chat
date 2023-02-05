using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class AuthorUI
{
    public Session Session { get; }
    public IAccounts Accounts { get; }
    public IAuthors Authors { get; }
    public ModalUI ModalUI { get; }
    public NavigationManager Nav { get; }

    public AuthorUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        Authors = services.GetRequiredService<IAuthors>();
        ModalUI = services.GetRequiredService<ModalUI>();
        Nav = services.GetRequiredService<NavigationManager>();
    }

    public async Task Show(AuthorId authorId, CancellationToken cancellationToken = default)
    {
        if (authorId.IsNone)
            return; // Likely the caller haven't read authorId yet, so we can't do much here

        var ownAccountTask = Accounts.GetOwn(Session, cancellationToken);
        var accountTask = Authors.GetAccount(Session, authorId.ChatId, authorId, cancellationToken);
        var ownAccount = await ownAccountTask.ConfigureAwait(false);
        var account = await accountTask.ConfigureAwait(false);

        var mustShowModal = account == null || account.IsGuestOrNone || ownAccount.IsGuestOrNone || account.Id == ownAccount.Id;
        if (mustShowModal)
            await ModalUI.Show(new AuthorModal.Model(authorId)).ConfigureAwait(false);
        else {
            var peerChatId = new PeerChatId(ownAccount.Id, account!.Id);
            Nav.NavigateTo(Links.Chat(peerChatId));
        }
    }
}
