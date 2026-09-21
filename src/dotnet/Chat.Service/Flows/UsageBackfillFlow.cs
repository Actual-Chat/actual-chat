using ActualChat.Live;
using ActualChat.Chat.Db;
using ActualChat.Flows;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Seeds per-user usage from what already exists: speech and messages from text entries, completed
/// calls from materialized call conversations. Ambient sessions of the past leave no trace to count.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MessagePackObject(true)]
public sealed partial class UsageBackfillFlow : Flow<(Moment, long)>
{
    private const int BatchSize = 500;
    private static readonly RandomTimeSpan BatchDelay = TimeSpan.FromSeconds(1).ToRandom(0.25);

    private DbHub<ChatDbContext> DbHub => field ??= Services.DbHub<ChatDbContext>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private ICommander Commander => Hub.Commander;

    [DataMember(Order = 0), Key(0)]
    public string? LastProcessedEntryId { get; set; }
    [DataMember(Order = 1), Key(1)]
    public string? LastProcessedConversationId { get; set; }
    [DataMember(Order = 2), Key(2)]
    public bool AreEntriesDone { get; set; }
    [DataMember(Order = 3), Key(3)]
    public long EventCount { get; set; }
    [DataMember(Order = 4), Key(4)]
    public long ScannedCount { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var isDone = AreEntriesDone
            ? await ProcessConversations(dbContext, cancellationToken).ConfigureAwait(false)
            : await ProcessEntries(dbContext, cancellationToken).ConfigureAwait(false);
        Console.Log($"Progress: {EventCount} events from {ScannedCount} rows"
            + (AreEntriesDone ? " (conversations)" : " (entries)"));
        if (isDone) {
            Console.Log($"Completed: {EventCount} events from {ScannedCount} rows");
            SetResult((Hub.Clocks.SystemClock.Now, EventCount));
            return;
        }

        Runtime.StageResumeIn(BatchDelay.Next());
    }

    // Private methods

    private async Task<bool> ProcessEntries(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var query = dbContext.ChatEntries
            .Where(e => e.Kind == 0 && !e.IsSystemEntry && !e.IsRemoved);
        if (!LastProcessedEntryId.IsNullOrEmpty())
            query = query.Where(e => string.Compare(e.Id, LastProcessedEntryId) > 0);

        var batch = await query
            .OrderBy(e => e.Id)
            .Take(BatchSize)
            .Select(e => new { e.Id, e.ChatId, e.AuthorId, e.BeginsAt, e.EndsAt, e.AudioId, e.IsViaApi })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (batch.Count == 0) {
            AreEntriesDone = true;
            return false;
        }

        var events = new Dictionary<UserId, List<UsageEvent>>();
        foreach (var row in batch) {
            var chatId = ChatId.Parse(row.ChatId);
            var userId = await GetUserId(chatId, row.AuthorId, cancellationToken).ConfigureAwait(false);
            if (userId is null)
                continue;

            var attributes = new UsageEventAttributes { ChatKind = chatId.Kind, IsViaApi = row.IsViaApi };
            UsageEvent? usageEvent;
            if (row.AudioId.IsNullOrEmpty())
                usageEvent = new UsageEvent(UsageEventKind.Message, row.BeginsAt, row.Id, 1, attributes);
            else if (row.EndsAt is { } endsAt && endsAt > row.BeginsAt) {
                var duration = endsAt - row.BeginsAt;
                if (duration > UsageEventSource.MaxSpeechDuration)
                    duration = UsageEventSource.MaxSpeechDuration;
                usageEvent = new UsageEvent(
                    UsageEventKind.Speech, row.BeginsAt, row.Id, (long)duration.TotalMilliseconds, attributes);
            }
            else
                continue;

            events.GetOrAdd(userId, _ => new List<UsageEvent>()).Add(usageEvent);
        }
        await Record(events, cancellationToken).ConfigureAwait(false);

        LastProcessedEntryId = batch[^1].Id;
        ScannedCount += batch.Count;
        return false;
    }

    private async Task<bool> ProcessConversations(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var query = dbContext.Conversations
            .Where(c => c.CallerId != "");
        if (!LastProcessedConversationId.IsNullOrEmpty())
            query = query.Where(c => string.Compare(c.Id, LastProcessedConversationId) > 0);

        var batch = await query
            .OrderBy(c => c.Id)
            .Take(BatchSize)
            .Select(c => new { c.Id, c.ChatId, c.StartsAt, c.EndsAt, c.AuthorIds })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (batch.Count == 0)
            return true;

        var events = new Dictionary<UserId, List<UsageEvent>>();
        foreach (var row in batch) {
            if (row.EndsAt <= row.StartsAt)
                continue;

            var chatId = ChatId.Parse(row.ChatId);
            var authorIds = JsonSerializer.Deserialize<AuthorId[]>(row.AuthorIds) ?? [];
            var ended = new LiveSessionEndedEvent(
                chatId,
                row.StartsAt,
                row.EndsAt,
                LiveSessionKind.Call,
                authorIds.Select(a => new LiveSessionEndedMember(a, row.StartsAt)).ToApiArray());
            foreach (var member in ended.Members) {
                var usageEvent = UsageEventSource.FromLiveSessionEnd(ended, member);
                if (usageEvent is null)
                    continue;

                var userId = await GetUserId(chatId, member.AuthorId.Value, cancellationToken).ConfigureAwait(false);
                if (userId is null)
                    continue;

                events.GetOrAdd(userId, _ => new List<UsageEvent>()).Add(usageEvent);
            }
        }
        await Record(events, cancellationToken).ConfigureAwait(false);

        LastProcessedConversationId = batch[^1].Id;
        ScannedCount += batch.Count;
        return false;
    }

    private async Task Record(Dictionary<UserId, List<UsageEvent>> events, CancellationToken cancellationToken)
    {
        foreach (var (userId, userEvents) in events) {
            await Commander
                .Call(new UsageBackend_Record(userId, userEvents.ToApiArray()), cancellationToken)
                .ConfigureAwait(false);
            EventCount += userEvents.Count;
        }
    }

    private async Task<UserId?> GetUserId(ChatId chatId, string authorSid, CancellationToken cancellationToken)
    {
        if (!AuthorId.TryParse(authorSid, out var authorId))
            return null;

        var author = await AuthorsBackend
            .Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken)
            .ConfigureAwait(false);
        var userId = author?.UserId;
        return userId is { IsGuest: false } && !userId.Value.IsNullOrEmpty() ? userId : null;
    }
}
