namespace ActualChat.Users;

public class ChatUsages(IServiceProvider services) : IChatUsages
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChatUsagesBackend Backend { get; } = services.GetRequiredService<IChatUsagesBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<ApiArray<ChatId>> GetRecencyList(Session session, ChatUsageListKind kind, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Backend.GetRecencyList(account.Id, kind, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRegisterUsage(ChatUsages_RegisterUsage command, CancellationToken cancellationToken)
    {
        var (session, kind, chatId, accessTime) = command;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);

        var backendCommand = new ChatUsagesBackend_RegisterUsage(account.Id, kind, chatId, accessTime);
        await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
