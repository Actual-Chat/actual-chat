using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class AuthorUI
{
    private IAccounts? _accounts;
    private IAuthors? _authors;
    private ModalUI? _modalUI;
    private History? _history;

    private IServiceProvider Services { get; }
    private Session Session { get; }
    private IAccounts Accounts => _accounts ??= Services.GetRequiredService<IAccounts>();
    private IAuthors Authors => _authors ??= Services.GetRequiredService<IAuthors>();
    private ModalUI ModalUI => _modalUI ??= Services.GetRequiredService<ModalUI>();
    private History History => _history ??= Services.GetRequiredService<History>();

    public AuthorUI(IServiceProvider services)
    {
        Services = services;
        Session = services.GetRequiredService<Session>();
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
        var localUrl = Links.Chat(peerChatId);
        _ = History.NavigateTo(localUrl);
    }
}
