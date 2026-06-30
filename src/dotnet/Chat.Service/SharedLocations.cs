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
    public virtual async Task<SharedLocation> OnReport(SharedLocations_Report command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (session, chatId, id, point, liveDuration) = command;
        if (id is null) {
            var chatRules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
            var author = chatRules.Author;
            if (author is not { HasLeft: false })
                throw StandardError.Constraint("Please join the chat before sharing your location.");
            chatRules.Require(ChatPermissions.Write);

            if (liveDuration > TimeSpan.Zero) {
                // One-shot sends (zero duration) aren't live shares, so they don't count toward the cap.
                var liveShares = await Backend.ListLive(chatId, cancellationToken).ConfigureAwait(false);
                var alreadySharing = liveShares.Any(x => x.AuthorId == author.Id);
                if (!alreadySharing && liveShares.Count >= Constants.Location.MaxSharingAuthorsPerChat)
                    throw StandardError.Constraint(
                        $"This chat already has the maximum of {Constants.Location.MaxSharingAuthorsPerChat} "
                        + "people sharing their live location.");
            }

            var newId = SharedLocationId.New();
            var createReport = new SharedLocationsBackend_Report(newId, author.Id, point, liveDuration);
            return await Commander.Call(createReport, true, cancellationToken).ConfigureAwait(false);
        }

        var existing = await Backend.Get(chatId, id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return null!;

        var own = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (own is null || own.Id != existing.AuthorId)
            return existing;

        var updateReport = new SharedLocationsBackend_Report(id, existing.AuthorId, point, liveDuration);
        return await Commander.Call(updateReport, true, cancellationToken).ConfigureAwait(false);
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
