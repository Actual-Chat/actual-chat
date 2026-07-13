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
        var location = await Backend.Get(id, cancellationToken).ConfigureAwait(false);
        return location is null || location.ChatId != chatId ? null : location;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<SharedLocation>> ListLive(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.ListLive(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<SharedLocation?> OnChange(SharedLocations_Change command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null; // It just spawns other commands, so nothing to do here

        var (session, chatId, id, change) = command;
        change.RequireValid();

        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        var author = chatRules.Author;
        if (author is not { HasLeft: false })
            throw StandardError.Constraint("Please join the chat before sharing your location.");
        chatRules.Require(ChatPermissions.Write);

        var changeCommand = new SharedLocationsBackend_Change(id, author.Id, change);
        return await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
