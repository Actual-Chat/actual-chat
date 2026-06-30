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
        return await Backend.Get(id, cancellationToken).ConfigureAwait(false);
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

        if (id is null) {
            if (!change.IsCreate(out var diff))
                throw StandardError.Constraint("A new shared location requires a Create change.");

            var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
            var author = chatRules.Author;
            if (author is not { HasLeft: false })
                throw StandardError.Constraint("Please join the chat before sharing your location.");
            chatRules.Require(ChatPermissions.Write);

            if ((diff.LiveDuration ?? TimeSpan.Zero) > TimeSpan.Zero) {
                // One-shot sends (zero duration) aren't live shares, so they don't count toward the cap.
                var liveShares = await Backend.ListLive(chatId, cancellationToken).ConfigureAwait(false);
                var alreadySharing = liveShares.Any(x => x.AuthorId == author.Id);
                if (!alreadySharing && liveShares.Count >= Constants.Location.MaxSharingAuthorsPerChat)
                    throw StandardError.Constraint(
                        $"This chat already has the maximum of {Constants.Location.MaxSharingAuthorsPerChat} "
                        + "people sharing their live location.");
            }

            var createCommand = new SharedLocationsBackend_Change(SharedLocationId.New(), author.Id, change);
            return await Commander.Call(createCommand, true, cancellationToken).ConfigureAwait(false);
        }

        var existing = await Backend.Get(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return null;

        var own = await Authors.GetOwn(session, existing.ChatId, cancellationToken).ConfigureAwait(false);
        if (own is null || own.Id != existing.AuthorId)
            return existing;

        var changeCommand = new SharedLocationsBackend_Change(id, existing.AuthorId, change);
        return await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
