using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.Chat;

public class AuthorsUpgradeBackend : IAuthorsUpgradeBackend
{
    private IAccounts Accounts { get; }
    private IServerKvas ServerKvas { get; }
    private IAuthorsBackend Backend { get; }

    public AuthorsUpgradeBackend(IServiceProvider services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        ServerKvas = services.GetRequiredService<IServerKvas>();
        Backend = services.GetRequiredService<IAuthorsBackend>();
    }

    public async Task<ImmutableArray<Symbol>> ListOwnChatIds(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account != null)
            return await Backend.ListUserChatIds(account.Id, cancellationToken).ConfigureAwait(false);

        var kvas = ServerKvas.GetClient(session);
        var unregisteredAuthorSettings = await kvas.GetUnregisteredUserSettings(cancellationToken).ConfigureAwait(false);
        var chats = unregisteredAuthorSettings.Chats;
        var chatIds = chats.Keys.AsEnumerable();
        if (!chats.ContainsKey(Constants.Chat.AnnouncementsChatId.Value))
            chatIds = chatIds.Append(Constants.Chat.AnnouncementsChatId.Value);
        return chatIds.Select(x => (Symbol) x).ToImmutableArray();
    }
}
