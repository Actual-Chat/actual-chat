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
        ArgumentNullException.ThrowIfNull(conversationId);

        var dbChat = await DbChatResolver.Get(conversationId.Value, cancellationToken).ConfigureAwait(false);
        var chat = dbChat?.ToModel();
        return chat;
    }

    // [ComputeMethod]
    public virtual async Task<ConversationRangeMeta> GetRangeMeta(
        ChatId chatId,
        long idTileStart,
        CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.LastLayer.AssertIsTileStart(idTileStart);
        var idTileRange = idTile.Range;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var conversationRanges = await dbContext.Conversations
            .Where(c => c.ChatId == chatId.Value && c.StartEntryLid < idTileRange.End && c.EndEntryLid >= idTileRange.Start)
            .OrderBy(c => c.StartEntryLid)
            .Select(c => new Range<long>(c.StartEntryLid, c.EndEntryLid + 1))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var previousConversationRange = await dbContext.Conversations
            .Where(c => c.ChatId == chatId.Value && c.EndEntryLid < idTileRange.Start)
            .OrderByDescending(c => c.StartEntryLid)
            .Select(c => (Range<long>?)new Range<long>(c.StartEntryLid, c.EndEntryLid + 1))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var nextConversationRange = await dbContext.Conversations
            .Where(c => c.ChatId == chatId.Value && c.StartEntryLid >= idTileRange.End)
            .OrderBy(c => c.StartEntryLid)
            .Select(c => (Range<long>?)new Range<long>(c.StartEntryLid, c.EndEntryLid + 1))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ConversationRangeMeta(chatId,
            conversationRanges.ToArray(),
            previousConversationRange,
            nextConversationRange);
    }

    // Commands

    // [CommandHandler]
    public virtual async Task<Conversation> OnChange(ConversationBackend_Change command, CancellationToken cancellationToken)
    {
        var (conversationId, _, change) = command;
        var chatId = conversationId.ChatId;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invConversation = context.Operation.Items.KeylessGet<Conversation>();
            if (invConversation != null) {
                _ = Get(invConversation.Id, default);
                foreach (var idTile in IdTileStack.LastLayer.GetCoveringTiles(invConversation.EntryRange))
                    _ = GetRangeMeta(chatId, idTile.Range.Start, default);
                var previousConversationId = context.Operation.Items.Get<long>(nameof(ConversationRangeMeta.PreviousConversationRange));
                var nextConversationId = context.Operation.Items.Get<long>(nameof(ConversationRangeMeta.NextConversationRange));
                if (previousConversationId != default) {
                    var previousIdTile = IdTileStack.LastLayer.GetTile(previousConversationId);
                    _ = GetRangeMeta(chatId, previousIdTile.Range.Start, default);
                }
                if (nextConversationId != default) {
                    var nextIdTile = IdTileStack.LastLayer.GetTile(nextConversationId);
                    _ = GetRangeMeta(chatId, nextIdTile.Range.Start, default);
                }
            }
            return null!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.Conversations.Lock(conversationId, cancellationToken).ConfigureAwait(false);

        var dbConversation = await dbContext.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId.Value, cancellationToken)
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
                .Where(c => c.ChatId == chatId.Value && c.StartEntryLid < endEntryLid && c.EndEntryLid >= startEntryLid)
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

            await StorePreviousAndNextConversationIds(startEntryLid, endEntryLid).ConfigureAwait(false);
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
                .Where(c => c.ChatId == chatId.Value && c.StartEntryLid < endEntryLid && c.EndEntryLid >= startEntryLid)
                .Select(c => c.Id)
                .OrderBy(c => c)
                .ToHashSetAsync(cancellationToken)
                .ConfigureAwait(false);
            sConversationIds.Remove(conversationId.Value);
            await dbContext.Conversations
                .Where(c => sConversationIds.Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await StorePreviousAndNextConversationIds(startEntryLid, endEntryLid).ConfigureAwait(false);
        }
        else if (change.IsRemove()) {
            dbConversation.Require();
            var startEntryLid = dbConversation.StartEntryLid;
            var endEntryLid = dbConversation.EndEntryLid;

            dbContext.Remove(dbConversation);

            await StorePreviousAndNextConversationIds(startEntryLid, endEntryLid).ConfigureAwait(false);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        conversation = dbConversation.Require().ToModel();
        context.Operation.Items.KeylessSet(conversation);
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

        async Task StorePreviousAndNextConversationIds(long startEntryLid, long? endEntryLid)
        {
            var previousConversationId = await dbContext.Conversations
                .Where(c => c.ChatId == chatId.Value && c.EndEntryLid < startEntryLid)
                .MaxAsync(c => (long?)c.StartEntryLid, cancellationToken)
                .ConfigureAwait(false);
            var nextConversationId = await dbContext.Conversations
                .Where(c => c.ChatId == chatId.Value && c.StartEntryLid >= endEntryLid)
                .MinAsync(c => (long?)c.StartEntryLid, cancellationToken)
                .ConfigureAwait(false);

            if (previousConversationId != 0)
                context.Operation.Items.Set(nameof(ConversationRangeMeta.PreviousConversationRange), previousConversationId);
            if (nextConversationId != 0)
                context.Operation.Items.Set(nameof(ConversationRangeMeta.NextConversationRange), nextConversationId);
        }
    }

    // [CommandHandler]
    public virtual async Task<Conversation> OnSummarize(
        ConversationBackend_Summarize command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!; // No invalidation there as we call other commands

        var (chatId, entryIdRanges) = command;
        if (entryIdRanges.Length == 0)
            throw StandardError.Constraint("ConversationBackend_Summarize.EntryIdRanges should not be empty.");

        var startEntryLid = entryIdRanges[0].Start;
        var endEntryLid = entryIdRanges[^1].End - 1;
        var conversationId = ConversationId.New(chatId, startEntryLid);
        var existingConversation = await Get(conversationId, cancellationToken).ConfigureAwait(false);
        var expectedVersion = existingConversation?.Version;
        var entries = await GetTextEntries(chatId, entryIdRanges, cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
            return default!;

        var firstEntry = entries.First();
        var lastEntry = entries.Last();
        var retryCount = 0;
        var summaryResult = ConversationSummarizerResult.Empty;
        while (!summaryResult.HasResult) {
            summaryResult = await ConversationSummarizer.Summarize(entries, cancellationToken).ConfigureAwait(false);
            if (summaryResult.HasResult)
                break;

            if (retryCount++ > 3)
                throw StandardError.Postpone(TimeSpan.FromMinutes(1));

            var postpone = summaryResult.Postpone;
            if (postpone != null)
                await Clocks.SystemClock.Delay(postpone.Value, cancellationToken).ConfigureAwait(false);
            else
                throw StandardError.Postpone(TimeSpan.FromMinutes(1));
        }

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
                .ToArray()
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
            return null!; // This handler makes changes only via nested commands

        var (chatId, entryLid, replyIdRange) = command;
        var conversationTile = IdTileStack.LastLayer.GetTile(entryLid);
        var conversationRangeMeta = await GetRangeMeta(chatId, conversationTile.Range.Start, cancellationToken)
            .ConfigureAwait(false);
        var existingConversations = conversationRangeMeta.ConversationIds;
        if (existingConversations.Length == 0) {
            // Skip the reply as the conversation is not found - the entry group was too small for summarization
            Log.LogInformation("Skipping reply as the conversation for {ChatId} and {EntryLid} is not found", chatId, entryLid);
            return null;
        }

        var conversationId = existingConversations[0];
        var conversation = await Get(conversationId, cancellationToken).ConfigureAwait(false);
        conversation.Require();

        var entries = await GetTextEntries(chatId, [conversation.EntryRange, replyIdRange], cancellationToken).ConfigureAwait(false);
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
                .ToArray(),
        };
        var change = Change.Update(diff);
        var changeCommand = new ConversationBackend_Change(conversationId, conversation.Version, change);
        return await DbHub.Commander.Call(changeCommand, false, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<IReadOnlyCollection<TextEntry>> GetTextEntries(ChatId chatId, Range<long>[] entryIdRanges, CancellationToken cancellationToken)
    {
        var idTiles = entryIdRanges
            .SelectMany(idRange => IdTileStack.GetOptimalCoveringTiles(idRange))
            .ToList();

        var tiles = await idTiles
            .Select(idTile => ChatsBackend.GetTile(chatId, ChatEntryKind.Text, idTile.Range, false, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        return tiles
            .SelectMany(tile => tile.Entries)
            .Where(e => entryIdRanges.Any(r => r.Contains(e.LocalId)))
            .Select(entry => new TextEntry(entry))
            .ToList();
    }
}
