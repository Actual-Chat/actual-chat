using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class AuthorUI
{
    public Session Session { get; }
    public IAccounts Accounts { get; }
    public IAuthors Authors { get; }
    public ModalUI ModalUI { get; }
    public History History { get; }

    public AuthorUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        Authors = services.GetRequiredService<IAuthors>();
        ModalUI = services.GetRequiredService<ModalUI>();
        History = services.GetRequiredService<History>();
    }

    public async Task Show(AuthorId authorId, CancellationToken cancellationToken = default)
    {
        if (authorId.IsNone)
            return; // Likely the caller haven't read authorId yet, so we can't do much here

        await ModalUI.Show(new AuthorModal.Model(authorId)).ConfigureAwait(false);
    }

    public async Task<bool> CanStartPeerChat(AuthorId authorId, CancellationToken cancellationToken = default)
    {
        if (authorId.IsNone)
            return false;

        var ownAccountTask = Accounts.GetOwn(Session, cancellationToken);
        var accountTask = Authors.GetAccount(Session, authorId.ChatId, authorId, cancellationToken);
        var ownAccount = await ownAccountTask.ConfigureAwait(false);
        var account = await accountTask.ConfigureAwait(false);
        var canStartPeerChat = account != null
            && !account.IsGuestOrNone
            && !ownAccount.IsGuestOrNone
            && account.Id != ownAccount.Id;
        return canStartPeerChat;
    }

    public async Task StartPeerChat(AuthorId authorId, CancellationToken cancellationToken = default)
    {
        if (authorId.IsNone)
            return;

        var ownAccountTask = Accounts.GetOwn(Session, cancellationToken);
        var accountTask = Authors.GetAccount(Session, authorId.ChatId, authorId, cancellationToken);
        var ownAccount = await ownAccountTask.ConfigureAwait(false);
        var account = await accountTask.ConfigureAwait(false);
        var peerChatId = new PeerChatId(ownAccount.Id, account!.Id);
        _ = History.NavigateTo(Links.Chat(peerChatId));
    }
}
