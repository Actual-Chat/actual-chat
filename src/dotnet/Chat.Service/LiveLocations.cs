namespace ActualChat.Chat;

public class LiveLocations(IServiceProvider services) : ILiveLocations
{
    private ILiveLocationsBackend Backend { get; } = services.GetRequiredService<ILiveLocationsBackend>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<LiveLocation?> Get(
        Session session,
        ChatId chatId,
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<LiveLocation>> List(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.List(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnReport(LiveLocations_Report command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, point, duration) = command;
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Write);

        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return;

        await Commander.Call(new LiveLocationsBackend_Report(chatId, author.Id, point, duration), true, cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnStop(LiveLocations_Stop command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, chatId) = command;
        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return;

        await Commander.Call(new LiveLocationsBackend_Stop(chatId, author.Id), true, cancellationToken)
            .ConfigureAwait(false);
    }
}
