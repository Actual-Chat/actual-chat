using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ConversationsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IConversationsBackend
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;

    [field: AllowNull, MaybeNull]
    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();
    [field: AllowNull, MaybeNull]
    private IDbEntityResolver<string, DbConversation> DbChatResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbConversation>>();

    // [ComputeMethod]
    public virtual async Task<Conversation?> Get(ConversationId conversationId, CancellationToken cancellationToken)
    {
        if (conversationId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(conversationId));

        var dbChat = await DbChatResolver.Get(conversationId, cancellationToken).ConfigureAwait(false);
        var chat = dbChat?.ToModel();
        return chat;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ConversationId>> List(
        ChatId chatId,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var sConversationIds = await dbContext.Conversations
            .Where(c => c.ChatId == chatId && c.StartEntryLid <= idTileRange.End && c.EndEntryLid >= idTileRange.Start)
            .Select(c => c.Id)
            .OrderBy(c => c)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return sConversationIds
            .Select(s => new ConversationId(s, ParseOrNone.Option))
            .ToApiArray();
    }

    // Commands

    // [CommandHandler]
    public virtual async Task<Conversation> OnChange(ConversationBackend_Change command, CancellationToken cancellationToken)
    {
        var (conversationId, expectedVersion, change) = command;
        var chatId = conversationId.ChatId;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invConversation = context.Operation.Items.Get<Conversation>();
            if (invConversation != null) {
                _ = Get(invConversation.Id, default);
                foreach (var idTile in IdTileStack.FirstLayer.GetCoveringTiles(invConversation.EntryRange))
                    _ = List(chatId, idTile.Range, default);
            }
            return null!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbConversation = chatId.IsNone ? null :
            await dbContext.Conversations.ForUpdate()
                // ReSharper disable once AccessToModifiedClosure
                .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken)
                .ConfigureAwait(false);
        var oldConversation = dbConversation?.ToModel();
        Conversation conversation;
        if (change.IsCreate(out var update)) {
            oldConversation.RequireNull();

            conversation = new Conversation(conversationId);
            conversation = ApplyDiff(conversation, update);
            dbConversation = new DbConversation(conversation);
            dbContext.Add(dbConversation);
        }
        else if (change.IsUpdate(out update)) {
            dbConversation.RequireVersion(expectedVersion);

            conversation = ApplyDiff(dbConversation.ToModel(), update);
            dbConversation.UpdateFrom(conversation);
        }
        else if (change.IsRemove()) {
            dbConversation.Require();

            dbContext.Remove(dbConversation);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        conversation = dbConversation.Require().ToModel();
        context.Operation.Items.Set(conversation);
        return conversation;

        Conversation ApplyDiff(Conversation originalConversation, ConversationDiff? diff) {
            // Update
            var newConversation = DiffEngine.Patch(originalConversation, diff) with {
                Version = VersionGenerator.NextVersion(originalConversation.Version),
            };
            if (newConversation.EntryRange.Start != originalConversation.EntryRange.Start)
                throw StandardError.Constraint("EntryRange.Start can't be changed.");

            // Validation
            if (newConversation.Title.IsNullOrEmpty())
                throw StandardError.Constraint("Conversation title cannot be empty.");
            if (newConversation.Description.IsNullOrEmpty())
                throw StandardError.Constraint("Conversation description cannot be empty.");
            if (newConversation.Summary.IsNullOrEmpty())
                throw StandardError.Constraint("Conversation summary cannot be empty.");
            if (newConversation.MessageCount <= 0)
                throw StandardError.Constraint("Conversation message count should be greater than zero.");

            return newConversation;
        }
    }

    // [CommandHandler]
    public virtual async Task<Conversation> OnSummarize(
        ConversationBackend_Summarize command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default; // No invalidation there as we call other commands

        var (chatId, entries) = command;
        // No invalidation there as we call other commands
        var delay = command.DelayUntil - Clocks.SystemClock.Now;
        if (delay > TimeSpan.Zero)
            throw StandardError.Postpone(delay.Value);

        var range = new Range<long>(entries[0].Id.LocalId, entries[^1].Id.LocalId);
        var coveringTiles = IdTileStack.FirstLayer.GetCoveringTiles(range);
        // Take tile not on the edge, otherwise it can find more than one conversation
        var someTile = coveringTiles.Length > 1 ? coveringTiles[1] : coveringTiles[0];
        var existingConversations = await List(chatId, someTile.Range, cancellationToken)
            .ConfigureAwait(false);
        if (existingConversations.Count != 0) {
            // Update existing conversation with the new entries
            var conversationId = existingConversations[0];
            var conversation = await Get(conversationId, cancellationToken).ConfigureAwait(false);
            conversation.Require();
        }

        // var summary = conversation.Summarize(entries);
        // var diff = conversation.Diff(summary);
        // var change = new Change<ConversationDiff>(diff);
        // var changeCommand = new ConversationBackend_Change(conversationId, conversation.Version, change);
        // return await DbHub.Commander.Call(changeCommand, false, cancellationToken).ConfigureAwait(false);
        throw new NotImplementedException();
    }

    // [CommandHandler]
    public virtual async Task<Conversation> OnAppendReply(
        ConversationBackend_AppendReply command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default; // No invalidation there as we call other commands

        var (conversationId, entryLid, replySequence) = command;
        var chatId = conversationId.ChatId;

        var delay = command.DelayUntil - Clocks.SystemClock.Now;
        if (delay > TimeSpan.Zero)
            throw StandardError.Postpone(delay.Value);

        Conversation? conversation;
        if (!conversationId.IsNone)
            conversation = await Get(conversationId, cancellationToken).ConfigureAwait(false);
        else {
            var existingConversations = await List(chatId, IdTileStack.FirstLayer.GetTile(entryLid).Range, cancellationToken)
                    .ConfigureAwait(false);
            if (existingConversations.Count == 0)
                throw StandardError.Internal("Conversation not found.");

            conversationId = existingConversations[0];
            conversation = await Get(conversationId, cancellationToken).ConfigureAwait(false);
            conversation.Require();
        }
        // Get summarization and call OnChange
        throw new NotImplementedException();
    }
}
