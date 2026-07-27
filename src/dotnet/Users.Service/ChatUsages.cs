namespace ActualChat.Users;

/// <summary>
/// Frontend service for tracking recently accessed chats per user.
/// </summary>
public class ChatUsages(IServiceProvider services) : IChatUsages
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IChatUsagesBackend Backend { get; } = services.GetRequiredService<IChatUsagesBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<ChatId[]> GetRecencyList(Session session, ChatUsageListKind kind, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Backend.GetRecencyList(account.Id, kind, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRegisterUsage(ChatUsages_RegisterUsage command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, kind, chatId, _) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return;

        var backendCommand = new ChatUsagesBackend_RegisterUsage(account.Id, kind, chatId, null);
        await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
