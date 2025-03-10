using ActualChat.Chat.Db;
using ActualChat.Chat.ML;
using ActualChat.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public class ConversationsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IConversationsBackend
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;

    [field: AllowNull, MaybeNull]
    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();
    [field: AllowNull, MaybeNull]
    private IDbEntityResolver<string, DbConversation> DbChatResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbConversation>>();
    [field: AllowNull, MaybeNull]
    private IConversationSummarizer ConversationSummarizer { get; } = services.GetRequiredService<IConversationSummarizer>();
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();

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
        var (conversationId, _, change) = command;
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

        await dbContext.Conversations.Lock(conversationId, cancellationToken).ConfigureAwait(false);

        var dbConversation = await dbContext.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken)
            .ConfigureAwait(false);
        var oldConversation = dbConversation?.ToModel();
        Conversation conversation;
        if (change.IsCreate(out var update)) {
            if (oldConversation != null)
                return oldConversation;

            // Get existing conversations that overlap with the new one
            var startEntryLid = conversationId.StartEntryLid;
            var endEntryLid = change.Create.Value.EndEntryLid;
            var sConversationIds = await dbContext.Conversations
                .Where(c => c.ChatId == chatId && c.StartEntryLid <= endEntryLid && c.EndEntryLid >= startEntryLid)
                .Select(c => c.Id)
                .OrderBy(c => c)
                .ToHashSetAsync(cancellationToken)
                .ConfigureAwait(false);
            // Remove other overlapping conversations
            await dbContext.Conversations
                .Where(c => sConversationIds.Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            conversation = new Conversation(conversationId);
            conversation = ApplyDiff(conversation, update);
            dbConversation = new DbConversation(conversation);
            dbContext.Add(dbConversation);
        }
        else if (change.IsUpdate(out update)) {
            // TODO(AK): too many version mismatch errors
            // if (expectedVersion != 0)
            //     dbConversation.RequireVersion(expectedVersion);
            // else
            dbConversation.Require();

            // Update existing conversation
            conversation = ApplyDiff(dbConversation.ToModel(), update);
            dbConversation.UpdateFrom(conversation);

            // Get existing conversations that overlap with the new one
            // and remove other overlapping conversations
            var startEntryLid = conversationId.StartEntryLid;
            var endEntryLid = change.Update.Value.EndEntryLid;
            var sConversationIds = await dbContext.Conversations
                .Where(c => c.ChatId == chatId && c.StartEntryLid <= endEntryLid && c.EndEntryLid >= startEntryLid)
                .Select(c => c.Id)
                .OrderBy(c => c)
                .ToHashSetAsync(cancellationToken)
                .ConfigureAwait(false);
            sConversationIds.Remove(conversationId);
            await dbContext.Conversations
                .Where(c => sConversationIds.Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
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
            return default!; // No invalidation there as we call other commands

        var (chatId, entries) = command;
        // No invalidation there as we call other commands
        var delay = command.DelayUntil - Clocks.SystemClock.Now;
        if (delay > TimeSpan.Zero)
            throw StandardError.Postpone(delay.Value);

        var firstEntry = entries[0];
        var lastEntry = entries[^1];
        var startEntryLid = firstEntry.LocalId;
        var endEntryLid = lastEntry.LocalId;
        var conversationId = new ConversationId(chatId, startEntryLid, AssumeValid.Option);
        var existingConversation = await Get(conversationId, cancellationToken).ConfigureAwait(false);
        var expectedVersion = existingConversation?.Version;

        var summaryResult = await ConversationSummarizer.Summarize(entries, cancellationToken).ConfigureAwait(false);
        if (!summaryResult.HasResult)
            throw StandardError.Postpone(summaryResult.Postpone ?? TimeSpan.FromMinutes(10));

        var summary = summaryResult.Summary!;
        var conversation = new Conversation(conversationId) {
            Title = summary.Title,
            Description = summary.Description,
            Summary = summary.Summary,
            MessageCount = entries.Count,
            EndEntryLid = endEntryLid,
            StartsAt = firstEntry.BeginsAt,
            EndsAt = lastEntry.EndsAt ?? lastEntry.BeginsAt,
            AuthorIds = entries
                .GroupBy(a => a.AuthorId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToApiArray()
        };
        var change = existingConversation != null
            ? Change.Update(DiffEngine.Diff<Conversation,ConversationDiff>(existingConversation, conversation))
            : Change.Create(new ConversationDiff(conversation));
        var changeCommand = new ConversationBackend_Change(conversationId, expectedVersion, change);
        return await DbHub.Commander.Call(changeCommand, false, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<Conversation?> OnAppendReply(
        ConversationBackend_AppendReply command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // No invalidation there as we call other commands

        var (chatId, entryLid, replySequence) = command;
        var delay = command.DelayUntil - Clocks.SystemClock.Now;
        if (delay > TimeSpan.Zero)
            throw StandardError.Postpone(delay.Value);

        var existingConversations = await List(chatId, IdTileStack.FirstLayer.GetTile(entryLid).Range, cancellationToken)
            .ConfigureAwait(false);
        if (existingConversations.Count == 0) {
            // Skip the reply as the conversation is not found - entry group was too small for summarization
            Log.LogInformation("Skipping reply as the conversation for {ChatId} and {EntryLid} is not found", chatId, entryLid);
            return null;
        }

        var conversationId = existingConversations[0];
        var conversation = await Get(conversationId, cancellationToken).ConfigureAwait(false);
        conversation.Require();

        var idTiles = IdTileStack.FirstLayer.GetCoveringTiles(conversation.EntryRange);
        var tiles = await idTiles
            .Select(idTile => ChatsBackend.GetTile(chatId,
                ChatEntryKind.Text,
                idTile.Range,
                false,
                cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        var entries = tiles
            .SelectMany(t => t.Entries)
            .Select(c => new TextEntry(c))
            .ToList();
        entries.AddRange(replySequence);
        var summaryResult = await ConversationSummarizer.Summarize(entries, cancellationToken).ConfigureAwait(false);
        if (!summaryResult.HasResult)
            throw StandardError.Postpone(summaryResult.Postpone ?? TimeSpan.FromMinutes(10));

        var summary = summaryResult.Summary!;
        // Do not update EndEntryLid, StartsAt, EndsAt as the conversation is not continuous
        var diff = new ConversationDiff {
            Title = summary.Title,
            Description = summary.Description,
            Summary = summary.Summary,
            MessageCount = entries.Count,
            AuthorIds = entries
                .GroupBy(a => a.AuthorId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .ToApiArray()
        };
        var change = Change.Update(diff);
        var changeCommand = new ConversationBackend_Change(conversationId, conversation.Version, change);
        return await DbHub.Commander.Call(changeCommand, false, cancellationToken).ConfigureAwait(false);
    }
}
