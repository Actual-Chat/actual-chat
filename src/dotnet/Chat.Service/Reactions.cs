namespace ActualChat.Chat;

internal class Reactions : IReactions
{
    private IReactionsBackend Backend { get; }
    private ICommander Commander { get; }
    private IChats Chats { get; }
    private IChatAuthors ChatAuthors { get; }

    public Reactions(IReactionsBackend backend, ICommander commander, IChats chats, IChatAuthors chatAuthors)
    {
        Backend = backend;
        Commander = commander;
        Chats = chats;
        ChatAuthors = chatAuthors;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ReactionSummary>> List(
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

        var author = await ChatAuthors.Get(session, reaction.ChatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return;

        if (!Emoji.IsAllowed(reaction.Emoji))
            throw StandardError.Constraint($"Emoji '{reaction.Emoji}' is not correct for reaction.");

        reaction = reaction with { AuthorId = author.Id };
        await Commander.Call(new IReactionsBackend.ReactCommand(reaction), cancellationToken).ConfigureAwait(false);
    }
}
