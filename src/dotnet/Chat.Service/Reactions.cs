namespace ActualChat.Chat;

internal class Reactions(IServiceProvider services) : IReactions
{
    private IReactionsBackend Backend { get; } = services.GetRequiredService<IReactionsBackend>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<Reaction?> Get(Session session, TextEntryId entryId, CancellationToken cancellationToken)
    {
        var chatAuthor = await Authors.GetOwn(session, entryId.ChatId, cancellationToken).ConfigureAwait(false);
        if (chatAuthor == null)
            return null;

        var chatRules = await Chats.GetRules(session, entryId.ChatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.Get(entryId, chatAuthor.Id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ReactionSummary>> ListSummaries(
        Session session,
        TextEntryId entryId,
        CancellationToken cancellationToken)
    {
        var chatRules = await Chats.GetRules(session, entryId.ChatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.List(entryId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnReact(Reactions_React command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, reaction) = command;
        var chatRules = await Chats.GetRules(session, reaction.EntryId.ChatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Write);

        var author = await Authors.GetOwn(session, reaction.EntryId.ChatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return;

        reaction = reaction with { AuthorId = author.Id };
        await Commander.Call(new ReactionsBackend_React(reaction), cancellationToken).ConfigureAwait(false);
    }
}
