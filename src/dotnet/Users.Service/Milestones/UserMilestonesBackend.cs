using Microsoft.EntityFrameworkCore;
using ActualChat.Flows;
using ActualChat.Users.Db;
using ActualChat.Users.Flows;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserMilestonesBackend(IServiceProvider services)
    : ShardedDbServiceBase<UsersDbContext>(services), IUserMilestonesBackend
{
    // A conversation is another human among the authors of the chat's last entries
    private const int ConversationEntryCount = 64;
    private static readonly TimeSpan Day2ReturnWindow = TimeSpan.FromHours(48);

    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IUsageBackend UsageBackend => field ??= Services.GetRequiredService<IUsageBackend>();
    private FlowHub FlowHub => field ??= Services.FlowHub();

    // [ComputeMethod]
    public virtual async Task<ApiArray<UserMilestone>> List(UserId userId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(userId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbMilestones = await dbContext.UserMilestones
            .Where(m => m.UserId == userId.Value)
            .OrderBy(m => m.ReachedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbMilestones.Select(m => m.ToModel()).ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<MilestoneDelivery>> ListDeliveries(
        UserId userId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(userId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbDeliveries = await dbContext.UserMilestoneDeliveries
            .Where(d => d.UserId == userId.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbDeliveries.Select(d => d.ToModel()).ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<UserAnalyticsClient>> ListAnalyticsClients(
        UserId userId, CancellationToken cancellationToken)
    {
        await ShardOwner.RequireShardOwnership(userId, addDependency: true, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbClients = await dbContext.UserAnalyticsClients
            .Where(c => c.UserId == userId.Value)
            .OrderByDescending(c => c.LastSeenAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbClients.Select(c => c.ToModel()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<UserMilestone?> OnReach(
        UserMilestonesBackend_Reach command, CancellationToken cancellationToken)
    {
        var (userId, kind, reachedAt) = command;
        if (userId.IsGuest)
            return null;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var isReached = await dbContext.UserMilestones
            .AnyAsync(m => m.UserId == userId.Value && m.Kind == kind, cancellationToken)
            .ConfigureAwait(false);
        if (isReached)
            return null;

        var latestAttributionId = await dbContext.UserAttributions
            .Where(a => a.UserId == userId.Value && a.IsLatest)
            .Select(a => (long?)a.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var dbMilestone = new DbUserMilestone {
            UserId = userId.Value,
            Kind = kind,
            ReachedAt = reachedAt.ToDateTimeClamped(),
            AttributionId = latestAttributionId,
        };
        dbContext.Add(dbMilestone);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidation.Defer(() => _ = List(userId, default));
        CommandContext.GetCurrent().Operation.AddEvent(FlowHub.NewResumeEvent<MilestoneDeliveryFlow>(userId.Value));
        return dbMilestone.ToModel();
    }

    // [CommandHandler]
    public virtual async Task OnSetDeliveries(
        UserMilestonesBackend_SetDeliveries command, CancellationToken cancellationToken)
    {
        var (userId, deliveries) = command;
        if (deliveries.Count == 0)
            return;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var milestoneIds = deliveries.Select(d => d.MilestoneId).Distinct().ToList();
        var dbDeliveries = await dbContext.UserMilestoneDeliveries.ForUpdate()
            .Where(d => d.UserId == userId.Value && milestoneIds.Contains(d.MilestoneId))
            .ToDictionaryAsync(d => (d.MilestoneId, d.Target), cancellationToken)
            .ConfigureAwait(false);
        foreach (var delivery in deliveries) {
            if (!dbDeliveries.TryGetValue((delivery.MilestoneId, delivery.Target), out var dbDelivery)) {
                dbDelivery = new DbUserMilestoneDelivery { UserId = userId.Value };
                dbDeliveries.Add((delivery.MilestoneId, delivery.Target), dbDelivery);
                dbContext.Add(dbDelivery);
            }
            dbDelivery.UpdateFrom(delivery);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidation.Defer(() => _ = ListDeliveries(userId, default));
    }

    // [CommandHandler]
    public virtual async Task OnSetAnalyticsClient(
        UserMilestonesBackend_SetAnalyticsClient command, CancellationToken cancellationToken)
    {
        var (userId, kind, clientId, seenAt) = command;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbClient = await dbContext.UserAnalyticsClients.ForUpdate()
            .FirstOrDefaultAsync(c => c.UserId == userId.Value && c.Kind == kind, cancellationToken)
            .ConfigureAwait(false);
        if (dbClient is null) {
            dbClient = new DbUserAnalyticsClient { UserId = userId.Value, Kind = kind };
            dbContext.Add(dbClient);
        }
        var isNewClientId = dbClient.ClientId != clientId;
        dbClient.ClientId = clientId;
        dbClient.LastSeenAt = seenAt.ToDateTimeClamped();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Invalidation.Defer(() => _ = ListAnalyticsClients(userId, default));
        // Milestones waiting for a client id can go out now
        if (isNewClientId)
            CommandContext.GetCurrent().Operation.AddEvent(FlowHub.NewResumeEvent<MilestoneDeliveryFlow>(userId.Value));
    }

    // [EventHandler]
    public virtual async Task OnChatEntryChangedEvent(
        ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        // Only ids, kinds and times are read here, never the content
        var (entry, author, changeKind, oldEntry) = eventCommand;
        // A voice entry is created while it streams, so it is posted on the update that ends the stream
        var isPosted = changeKind switch {
            ChangeKind.Create => !entry.IsContentStreaming,
            ChangeKind.Update => oldEntry?.IsContentStreaming == true && !entry.IsContentStreaming,
            _ => false,
        };
        if (!isPosted || eventCommand.MustSkipNotification
            || entry.IsSystemEntry || entry.IsImported || entry.IsViaApi)
            return;

        var userId = await GetHumanUserId(author, cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return;

        var chatId = entry.ChatId;
        var kindChatId = chatId is ThreadChatId threadChatId ? threadChatId.ParentChatId : chatId;
        var chat = await ChatsBackend.Get(kindChatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return;

        var now = Clocks.SystemClock.Now;
        await TryReachDay2Return(userId, now, cancellationToken).ConfigureAwait(false);
        if (chat.IsNotes)
            return;

        await TryReach(userId, MilestoneKind.Message, now, cancellationToken).ConfigureAwait(false);
        if (kindChatId is PeerChatId)
            await TryReach(userId, MilestoneKind.PeerChat, now, cancellationToken).ConfigureAwait(false);

        var authorIds = await ChatsBackend
            .GetLastEntryAuthors(chatId, ConversationEntryCount, cancellationToken)
            .ConfigureAwait(false);
        var otherUserIds = new HashSet<UserId>();
        foreach (var authorId in authorIds) {
            if (authorId == author.Id)
                continue;

            var otherAuthor = await AuthorsBackend
                .Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken)
                .ConfigureAwait(false);
            if (otherAuthor is null)
                continue;

            var otherUserId = await GetHumanUserId(otherAuthor, cancellationToken).ConfigureAwait(false);
            if (otherUserId is { } vOtherUserId && vOtherUserId != userId)
                otherUserIds.Add(vOtherUserId);
        }
        if (otherUserIds.Count == 0)
            return;

        await TryReach(userId, MilestoneKind.Conversation, now, cancellationToken).ConfigureAwait(false);
        foreach (var otherUserId in otherUserIds)
            await TryReach(otherUserId, MilestoneKind.Conversation, now, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task TryReachDay2Return(UserId userId, Moment now, CancellationToken cancellationToken)
    {
        // Posting on two UTC days within 48 h of sign-up; the current post may not be in the days yet
        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        if (account is null || now - account.CreatedAt > Day2ReturnWindow)
            return;

        var range = new Range<Moment>(UsageDay.DayOf(account.CreatedAt), account.CreatedAt + Day2ReturnWindow);
        var days = await UsageBackend.ListDays(userId, range, cancellationToken).ConfigureAwait(false);
        var postDays = days
            .Where(d => d.Messages + d.SpeechEntries > 0)
            .Select(d => d.Day)
            .Append(UsageDay.DayOf(now))
            .ToHashSet();
        if (postDays.Count >= 2)
            await TryReach(userId, MilestoneKind.Day2Return, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task TryReach(UserId userId, MilestoneKind kind, Moment now, CancellationToken cancellationToken)
    {
        var milestones = await List(userId, cancellationToken).ConfigureAwait(false);
        if (milestones.Any(m => m.Kind == kind))
            return;

        var reachCmd = new UserMilestonesBackend_Reach(userId, kind, now);
        await Commander.Call(reachCmd, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UserId?> GetHumanUserId(AuthorFull author, CancellationToken cancellationToken)
    {
        if (Bots.IsBot(author.Id))
            return null;

        var userId = author.UserId;
        if (userId.IsGuest || userId.Value.IsNullOrEmpty() || Constants.User.SystemUserIds.Contains(userId))
            return null;

        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        return account is null || account.IsBot ? null : userId;
    }
}
