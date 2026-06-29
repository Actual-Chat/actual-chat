namespace ActualChat.Chat;

public class SharedLocations(IServiceProvider services) : ISharedLocations
{
    private ISharedLocationsBackend Backend { get; } = services.GetRequiredService<ISharedLocationsBackend>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<SharedLocation?> Get(
        Session session,
        ChatId chatId,
        SharedLocationId id,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.Get(chatId, id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<SharedLocation>> List(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.List(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<SharedLocation> OnCreate(SharedLocations_Create command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (session, chatId, id, point, liveDuration) = command;
        var author = await Authors.EnsureJoined(session, chatId, cancellationToken).ConfigureAwait(false);
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Write);

        return await Commander
            .Call(new SharedLocationsBackend_Create(id, chatId, author.Id, point, liveDuration), true, cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnReport(SharedLocations_Report command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, chatId, id, point) = command;
        var author = await ResolveOwner(session, chatId, id, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return;

        await Commander.Call(new SharedLocationsBackend_Report(chatId, id, point), true, cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnStop(SharedLocations_Stop command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, chatId, id) = command;
        var author = await ResolveOwner(session, chatId, id, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return;

        await Commander.Call(new SharedLocationsBackend_Stop(chatId, id), true, cancellationToken)
            .ConfigureAwait(false);
    }

    // Private methods

    private async Task<AuthorFull?> ResolveOwner(
        Session session,
        ChatId chatId,
        SharedLocationId id,
        CancellationToken cancellationToken)
    {
        var sharedLocation = await Backend.Get(chatId, id, cancellationToken).ConfigureAwait(false);
        if (sharedLocation == null)
            return null;

        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        return author != null && author.Id == sharedLocation.AuthorId ? author : null;
    }
}
