namespace ActualChat.Chat;

internal class Reactions : IReactions
{
    private IReactionsBackend Backend { get; }
    private ICommander Commander { get; }
    private IChats Chats { get; }
    private IAuthors Authors { get; }

    public Reactions(IReactionsBackend backend, ICommander commander, IChats chats, IAuthors authors)
    {
        Backend = backend;
        Commander = commander;
        Chats = chats;
        Authors = authors;
    }

    // [ComputeMethod]
    public virtual async Task<Reaction?> Get(Session session, Symbol chatEntryId, CancellationToken cancellationToken)
    {
        var parsedChatEntryId = new ParsedChatEntryId(chatEntryId);
        var chatAuthor = await Authors.GetOwn(session, parsedChatEntryId.ChatId, cancellationToken).ConfigureAwait(false);
        if (chatAuthor == null)
            return null;

        var chatRules = await Chats.GetRules(session, parsedChatEntryId.ChatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.Get(chatEntryId, chatAuthor.Id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ReactionSummary>> ListSummaries(
        Session session,
        Symbol chatEntryId,
        CancellationToken cancellationToken)
    {
        var parsedChatEntryId = new ParsedChatEntryId(chatEntryId);
        var chatRules = await Chats.GetRules(session, parsedChatEntryId.ChatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Read);
        return await Backend.List(chatEntryId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task React(IReactions.ReactCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, reaction) = command;
        var chatRules = await Chats.GetRules(session, reaction.ChatId, cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Write);

        var author = await Authors.GetOwn(session, reaction.ChatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return;

        if (!Emoji.IsAllowed(reaction.Emoji))
            throw StandardError.Constraint($"Emoji '{reaction.Emoji}' is not correct for reaction.");

        reaction = reaction with { AuthorId = author.Id };
        await Commander.Call(new IReactionsBackend.ReactCommand(reaction), cancellationToken).ConfigureAwait(false);
    }
}
