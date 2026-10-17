using ActualChat.Contacts;

namespace ActualChat.Users;

#pragma warning disable CS0618

public class ChatUsages(IServiceProvider services) : IChatUsages
{
    private const int LegacyRecencyListLimit = 100;

    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();

    // [ComputeMethod]
    public virtual async Task<ChatId[]> GetLegacyRecencyList(
        Session session, ChatUsageListKind kind, CancellationToken cancellationToken)
    {
        // Old clients show these lists in the search panel; contacts give them the order new clients use
        Func<ChatId, bool>? filter = kind switch {
            ChatUsageListKind.PeerChatsWroteTo => c => c.Kind == ChatKind.Peer,
            ChatUsageListKind.ViewedGroupChats => c => c.Kind != ChatKind.Peer,
            _ => null,
        };
        if (filter is null)
            return [];

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return [];

        var contacts = await ContactsBackend.ListRecent(account.Id, cancellationToken).ConfigureAwait(false);
        return contacts
            .Select(c => c.ChatId)
            .Where(filter)
            .Take(LegacyRecencyListLimit)
            .ToArray();
    }

    // [CommandHandler]
    public virtual Task OnRegisterUsage(ChatUsages_RegisterUsage command, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
