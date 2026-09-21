using Microsoft.EntityFrameworkCore;
using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UsageBackend(IServiceProvider services)
    : ShardedDbServiceBase<UsersDbContext>(services), IUsageBackend
{
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private IContactsBackend ContactsBackend => field ??= Services.GetRequiredService<IContactsBackend>();
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private UsersSettings Settings => field ??= Services.GetRequiredService<UsersSettings>();

    // [ComputeMethod]
    public virtual async Task<UsageSummary> GetSummary(UserId userId, CancellationToken cancellationToken)
    {
        var days = await ListAllDays(userId, cancellationToken).ConfigureAwait(false);
        var friends = await GetFriendCount(userId, cancellationToken).ConfigureAwait(false);
        var summary = new UsageSummary {
            ActiveDays = days.Count,
            Friends = friends,
            FirstActiveDay = days.Count > 0 ? days[0].Day : null,
            LastActiveDay = days.Count > 0 ? days[^1].Day : null,
        };
        foreach (var day in days)
            summary = summary with {
                SpeechMs = summary.SpeechMs + day.SpeechMs,
                SpeechEntries = summary.SpeechEntries + day.SpeechEntries,
                Messages = summary.Messages + day.Messages,
                LiveSessions = summary.LiveSessions + day.LiveSessions,
            };
        return summary;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<UsageDay>> ListDays(
        UserId userId, Range<Moment> range, CancellationToken cancellationToken)
    {
        var days = await ListAllDays(userId, cancellationToken).ConfigureAwait(false);
        return days.Where(d => d.Day >= range.Start && d.Day < range.End).ToApiArray();
    }

    public virtual async Task<ReviewPromptState> GetReviewPromptState(UserId userId, CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return new(false, "No account");

        var summary = await GetSummary(userId, cancellationToken).ConfigureAwait(false);
        var history = await ServerKvasBackend.ForUser(userId).AppReviewPromptState()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var state = ReviewPromptPolicy.Evaluate(
            Settings.ReviewPrompt, Clocks.SystemClock.Now, account.CreatedAt, summary, history);
        UsageMeters.ReviewPromptVerdicts.Add(1,
            new KeyValuePair<string, object?>("verdict", state.CanPrompt ? "eligible" : state.Reason));
        return state;
    }

    // [CommandHandler]
    public virtual async Task OnRecord(UsageBackend_Record command, CancellationToken cancellationToken)
    {
        var (userId, events) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            if (context.Operation.Items.KeylessGet<bool>())
                _ = ListAllDays(userId, default);
            return;
        }

        if (events.Count == 0)
            return;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var sourceIds = events.Select(e => e.SourceId).Distinct().ToList();
        var existing = await dbContext.UsageEvents
            .Where(e => e.UserId == userId.Value && sourceIds.Contains(e.SourceId))
            .Select(e => new { e.Kind, e.SourceId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingKeys = existing.Select(e => (e.Kind, e.SourceId)).ToHashSet();

        var dayIds = events.Select(e => UsageDay.DayOf(e.OccurredAt).ToDateTime()).Distinct().ToList();
        var dbDays = await dbContext.UsageDays.ForUpdate()
            .Where(d => d.UserId == userId.Value && dayIds.Contains(d.Day))
            .ToDictionaryAsync(d => d.Day, cancellationToken)
            .ConfigureAwait(false);

        var hasChanges = false;
        foreach (var usageEvent in events) {
            if (!existingKeys.Add((usageEvent.Kind, usageEvent.SourceId))) {
                UsageMeters.EventsSkipped.Add(1, new KeyValuePair<string, object?>("kind", usageEvent.Kind.ToString()));
                continue;
            }

            dbContext.Add(new DbUsageEvent(userId, usageEvent));
            var day = UsageDay.DayOf(usageEvent.OccurredAt).ToDateTime();
            if (!dbDays.TryGetValue(day, out var dbDay)) {
                dbDay = new DbUsageDay { UserId = userId.Value, Day = day };
                dbDays.Add(day, dbDay);
                dbContext.Add(dbDay);
            }
            dbDay.Apply(usageEvent);
            dbDay.Version = VersionGenerator.NextVersion(dbDay.Version);
            hasChanges = true;
            UsageMeters.EventsRecorded.Add(1, new KeyValuePair<string, object?>("kind", usageEvent.Kind.ToString()));
        }

        if (hasChanges)
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(hasChanges);
    }

    // [CommandHandler]
    public virtual async Task OnRebuildDays(UsageBackend_RebuildDays command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        if (Invalidation.IsActive) {
            _ = ListAllDays(userId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var oldDays = await dbContext.UsageDays.ForUpdate()
            .Where(d => d.UserId == userId.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.RemoveRange(oldDays);

        var dbEvents = await dbContext.UsageEvents
            .Where(e => e.UserId == userId.Value)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var days = new Dictionary<DateTime, DbUsageDay>();
        foreach (var dbEvent in dbEvents) {
            var usageEvent = dbEvent.ToModel();
            var day = UsageDay.DayOf(usageEvent.OccurredAt).ToDateTime();
            if (!days.TryGetValue(day, out var dbDay)) {
                dbDay = new DbUsageDay { UserId = userId.Value, Day = day, Version = VersionGenerator.NextVersion() };
                days.Add(day, dbDay);
                dbContext.Add(dbDay);
            }
            dbDay.Apply(usageEvent);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnChatEntryChangedEvent(
        ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (entry, author, changeKind, oldEntry) = eventCommand;
        if (!IsTrackedUser(author.UserId))
            return;

        var usageEvent = UsageEventSource.FromEntryChange(entry, oldEntry, changeKind);
        if (usageEvent is null)
            return;

        await Commander
            .Call(new UsageBackend_Record(author.UserId, ApiArray.New(usageEvent)), true, cancellationToken)
            .ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnLiveSessionEndedEvent(
        LiveSessionEndedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var chatId = eventCommand.ChatId;
        foreach (var member in eventCommand.Members) {
            var usageEvent = UsageEventSource.FromLiveSessionEnd(eventCommand, member);
            if (usageEvent is null)
                continue;

            var author = await AuthorsBackend
                .Get(chatId, member.AuthorId, RequestedAuthorKind.Default, cancellationToken)
                .ConfigureAwait(false);
            if (author is null || !IsTrackedUser(author.UserId))
                continue;

            await Commander
                .Call(new UsageBackend_Record(author.UserId, ApiArray.New(usageEvent)), true, cancellationToken)
                .ConfigureAwait(false);
            await MarkReviewPromptPending(author.UserId, chatId, cancellationToken).ConfigureAwait(false);
        }
    }

    // [EventHandler]
    public virtual async Task OnContactChangedEvent(
        ContactChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (contact, oldContact, changeKind) = eventCommand;
        var ownerId = contact.OwnerId;
        if (!IsTrackedUser(ownerId))
            return;

        var now = Clocks.SystemClock.Now;
        var usageEvent = UsageEventSource.FromContactChange(contact, oldContact, changeKind, now);
        if (usageEvent is null)
            return;

        await Commander
            .Call(new UsageBackend_Record(ownerId, ApiArray.New(usageEvent)), true, cancellationToken)
            .ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<ApiArray<UsageDay>> ListAllDays(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbDays = await dbContext.UsageDays
            .Where(d => d.UserId == userId.Value)
            .OrderBy(d => d.Day)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbDays.Select(d => d.ToModel()).ToApiArray();
    }

    [ComputeMethod]
    protected virtual async Task<int> GetFriendCount(UserId userId, CancellationToken cancellationToken)
    {
        var contactIds = await ContactsBackend
            .ListPeerContactIds(userId, null, cancellationToken)
            .ConfigureAwait(false);
        var count = 0;
        foreach (var contactId in contactIds) {
            if (contactId.Kind != ContactKind.User)
                continue;

            var contact = await ContactsBackend.Get(userId, contactId, cancellationToken).ConfigureAwait(false);
            if (UsageEventSource.IsFriend(contact))
                count++;
        }
        return count;
    }

    // Private methods

    private async Task MarkReviewPromptPending(
        UserId userId, ChatId chatId, CancellationToken cancellationToken)
    {
        // The session that just ended is already recorded, so the verdict includes it. The pending
        // flag is what the app reacts to; a failure here costs one prompt opportunity, not the session.
        try {
            var state = await GetReviewPromptState(userId, cancellationToken).ConfigureAwait(false);
            if (!state.CanPrompt)
                return;

            var kvas = ServerKvasBackend.ForUser(userId).AppReviewPromptState();
            var history = await kvas.Get(cancellationToken).ConfigureAwait(false);
            var pending = ReviewPromptPolicy.MarkPending(history, chatId, Clocks.SystemClock.Now);
            await kvas.Set(pending, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Failed to mark the review prompt pending for user '{UserId}'", userId);
        }
    }

    private static bool IsTrackedUser(UserId? userId)
        => userId is { IsGuest: false } && !userId.Value.IsNullOrEmpty();
}
