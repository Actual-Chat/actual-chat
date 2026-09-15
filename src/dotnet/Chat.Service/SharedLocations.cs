namespace ActualChat.Chat;

public class SharedLocations(IServiceProvider services) : ISharedLocations
{
    private ISharedLocationsBackend Backend { get; } = services.GetRequiredService<ISharedLocationsBackend>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ICommander Commander { get; } = services.Commander();
    private MomentClockSet Clocks { get; } = services.Clocks();

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
    public virtual async Task<SharedLocation?> OnChange(
        SharedLocations_Change command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var chatId = command.ChatId;
        var id = command.Id;
        var change = command.Change;
        change.RequireValid();

        var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        var author = chatRules.Author;
        if (author is not { HasLeft: false })
            throw StandardError.Constraint("Please join the chat before sharing your location.");
        chatRules.Require(ChatPermissions.Write);
        if (change.IsCreate(out var createDiff))
            createDiff.Require(SharedLocationDiff.MustHaveCorrectDuration);
        if (change.IsRemove() && id is { } stoppedId) {
            var stopped = await Backend.Get(stoppedId, cancellationToken).ConfigureAwait(false);
            if (stopped is not null && stopped.AuthorId == author.Id && !stopped.IsLive(Clocks.SystemClock.Now)) {
                // A device stops by the id it holds, which a takeover may have frozen since - and a client too
                // old to notice never learns the live one. A stop of a frozen own share is the user pressing
                // Stop in this chat, so it means "stop my live share here".
                var live = await Backend.ListLive(chatId, cancellationToken).ConfigureAwait(false);
                if (live.FirstOrDefault(x => x.AuthorId == author.Id) is not { } ownLive)
                    return stopped;

                id = ownLive.Id;
            }
        }

        var changeCommand = new SharedLocationsBackend_Change(id, author.Id, change);
        return await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
