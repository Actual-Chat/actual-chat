using ActualChat.Chat.Db;
using ActualLab.Fusion.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

// Read + write side of the chat content index (Photo/Video/File/Link tabs in the
// right panel). Lives separately from the rest of ChatsBackend because the
// surface area is self-contained: the period skeleton + paged page-load read
// path on top, the index-update commands + invalidation in the middle, and the
// shared helpers below.
public partial class ChatsBackend
{
    private IFusionTime FusionTime => field ??= Services.GetRequiredService<IFusionTime>();

    // [ComputeMethod]
    public virtual async Task<ChatContentSkeleton> GetContentPeriods(
        ChatId chatId,
        ChatContentKind kind,
        string? beforePeriodKey,
        CancellationToken cancellationToken)
    {
        // Skeleton is paged one calendar year at a time. First call
        // (beforePeriodKey=null) routes through GetCurrentYear so that crossing
        // midnight Jan 1 cascades invalidations into this entry without a manual
        // cron. Subsequent calls send back the previous NextPeriodKey to advance
        // one calendar year into the past.
        int year;
        if (beforePeriodKey != null) {
            var (start, _) = ParseUtcMonthRange(beforePeriodKey);
            year = start.Year - 1;
        }
        else {
            year = await GetCurrentYear().ConfigureAwait(false);
        }
        return await GetContentPeriodsByYear(chatId, kind, year, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<VisualMediaItem[]> GetVisualMediaPeriod(
        ChatId chatId,
        string periodKey,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbItems = await QueryPeriodPage(
                dbContext.ChatVisualMediaItems, chatId.Value, periodKey, pageIndex, cancellationToken)
            .ConfigureAwait(false);
        return dbItems.Select(x => x.ToModel()).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<FileItem[]> GetFilePeriod(
        ChatId chatId,
        string periodKey,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbItems = await QueryPeriodPage(
                dbContext.ChatFileItems, chatId.Value, periodKey, pageIndex, cancellationToken)
            .ConfigureAwait(false);
        return dbItems.Select(x => x.ToModel()).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<LinkItem[]> GetLinkPeriod(
        ChatId chatId,
        string periodKey,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbItems = await QueryPeriodPage(
                dbContext.ChatLinkItems, chatId.Value, periodKey, pageIndex, cancellationToken)
            .ConfigureAwait(false);
        if (dbItems.Count == 0)
            return [];
        return await dbItems
            .Select(x => ResolveLinkItem(x.ToModel(), cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual Task OnUpdateChatVisualMediaIndex(
        ChatsBackend_UpdateChatVisualMediaIndex command,
        CancellationToken cancellationToken)
    {
        var (chatId, entryIds, items) = command;
        return UpdateContentIndex<VisualMediaItem, DbChatVisualMediaItem>(
            ChatContentKind.Media,
            chatId,
            entryIds,
            items,
            db => db.ChatVisualMediaItems,
            x => new DbChatVisualMediaItem(x with { Version = VersionGenerator.NextVersion() }),
            cancellationToken);
    }

    // [CommandHandler]
    public virtual Task OnUpdateChatFileIndex(
        ChatsBackend_UpdateChatFileIndex command,
        CancellationToken cancellationToken)
    {
        var (chatId, entryIds, items) = command;
        return UpdateContentIndex<FileItem, DbChatFileItem>(
            ChatContentKind.File,
            chatId,
            entryIds,
            items,
            db => db.ChatFileItems,
            x => new DbChatFileItem(x with { Version = VersionGenerator.NextVersion() }),
            cancellationToken);
    }

    // [CommandHandler]
    public virtual Task OnUpdateChatLinkIndex(
        ChatsBackend_UpdateChatLinkIndex command,
        CancellationToken cancellationToken)
    {
        var (chatId, entryIds, items) = command;
        return UpdateContentIndex<LinkItem, DbChatLinkItem>(
            ChatContentKind.Link,
            chatId,
            entryIds,
            items,
            db => db.ChatLinkItems,
            x => new DbChatLinkItem(x with { Version = VersionGenerator.NextVersion() }),
            cancellationToken);
    }

    // Protected members

    // Year of the current UTC moment, exposed as a compute method so consumers
    // (chiefly GetContentPeriods(..., null)) can hang an invalidation chain on
    // it. IFusionTime.Now ticks on its own update period; ConsolidationDelay
    // engages Fusion's ConsolidatingComputed<T> wrapper — when the recomputed
    // year equals the previous one (the common case), the wrapper stays valid
    // and the cascade stops here. The year-flip is the only tick that actually
    // propagates.
    [ComputeMethod(ConsolidationDelay = 1)]
    protected virtual async Task<int> GetCurrentYear()
    {
        var now = await FusionTime.Now(TimeSpan.FromMinutes(10)).ConfigureAwait(false);
        return now.ToDateTime().Year;
    }

    [ComputeMethod]
    protected virtual async Task<ChatContentSkeleton> GetContentPeriodsByYear(
        ChatId chatId,
        ChatContentKind kind,
        int year,
        CancellationToken cancellationToken)
    {
        var lowerBoundInclusive = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var upperBoundExclusive = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var chatSid = chatId.Value;
        var months = kind switch {
            ChatContentKind.Media => await QueryPeriodCounts(
                dbContext.ChatVisualMediaItems, chatSid, lowerBoundInclusive, upperBoundExclusive, cancellationToken)
                .ConfigureAwait(false),
            ChatContentKind.File => await QueryPeriodCounts(
                dbContext.ChatFileItems, chatSid, lowerBoundInclusive, upperBoundExclusive, cancellationToken)
                .ConfigureAwait(false),
            ChatContentKind.Link => await QueryPeriodCounts(
                dbContext.ChatLinkItems, chatSid, lowerBoundInclusive, upperBoundExclusive, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var periods = months
            .Select(m => new ChatContentPeriod {
                PeriodKey = FormatUtcMonthKey(m.Year, m.Month),
                PageCount = (m.Count + ChatContentPeriod.PageSize - 1) / ChatContentPeriod.PageSize,
            })
            .OrderByDescending(p => p.PeriodKey)
            .ToArray();
        var hasOlder = await HasContentBeforeYear(chatId, kind, year, cancellationToken).ConfigureAwait(false);
        var nextPeriodKey = hasOlder ? FormatUtcMonthKey(year, 1) : null;
        return new ChatContentSkeleton { Periods = periods, NextPeriodKey = nextPeriodKey };
    }

    [ComputeMethod]
    protected virtual async Task<bool> HasContentBeforeYear(
        ChatId chatId,
        ChatContentKind kind,
        int year,
        CancellationToken cancellationToken)
    {
        var boundary = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var chatSid = chatId.Value;
        return kind switch {
            ChatContentKind.Media => await QueryHasContentBefore(
                dbContext.ChatVisualMediaItems, chatSid, boundary, cancellationToken).ConfigureAwait(false),
            ChatContentKind.File => await QueryHasContentBefore(
                dbContext.ChatFileItems, chatSid, boundary, cancellationToken).ConfigureAwait(false),
            ChatContentKind.Link => await QueryHasContentBefore(
                dbContext.ChatLinkItems, chatSid, boundary, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    // Private members

    private async Task<LinkItem> ResolveLinkItem(LinkItem item, CancellationToken cancellationToken)
    {
        if (item.LinkPreviewId.IsEmpty)
            return item;

        var linkPreview = await LinkPreviewsBackend.Get(item.LinkPreviewId, false, cancellationToken).ConfigureAwait(false);
        return item with { LinkPreview = linkPreview };
    }

    private async Task UpdateContentIndex<TItem, TDbItem>(
        ChatContentKind kind,
        ChatId chatId,
        ChatEntryId[] entryIds,
        TItem[] items,
        Func<ChatDbContext, DbSet<TDbItem>> getTable,
        Func<TItem, TDbItem> toDbItem,
        CancellationToken cancellationToken)
        where TItem : IChatContentItem
        where TDbItem : class, IDbChatContentItem
    {
        if (entryIds.Length == 0)
            return;

        var commandContext = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invPageCounts = commandContext.Operation.Items.KeylessGet<ContentIndexPageCounts>();
            InvalidateContentIndex(kind, chatId, invPageCounts);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var table = getTable(dbContext);
        var entrySids = entryIds.Select(x => x.Value).Distinct().ToList();
        var deletedAts = await table
            .Where(x => entrySids.Contains(x.EntryId))
            .Select(x => x.At)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        await table
            .Where(x => entrySids.Contains(x.EntryId))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        foreach (var item in items)
            dbContext.Add(toDbItem(item));
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var chatSid = chatId.Value;
        var affectedMonths = CollectAffectedMonths(deletedAts, items);
        var pageCounts = new Dictionary<string, int>();
        foreach (var monthKey in affectedMonths) {
            var (periodStart, periodEnd) = ParseUtcMonthRange(monthKey);
            pageCounts[monthKey] = await table
                .Where(x => x.ChatId == chatSid && x.At >= periodStart && x.At < periodEnd)
                .CountAsync(cancellationToken).ConfigureAwait(false);
        }
        commandContext.Operation.Items
            .KeylessSet(new ContentIndexPageCounts(pageCounts));
    }

    private static HashSet<string> CollectAffectedMonths<TItem>(
        IEnumerable<DateTime> deletedAts,
        IEnumerable<TItem> insertedItems)
        where TItem : IChatContentItem
    {
        var months = new HashSet<string>();
        foreach (var at in deletedAts)
            months.Add(FormatUtcMonthKey(at.Year, at.Month));
        foreach (var item in insertedItems) {
            var at = item.At.ToDateTime();
            months.Add(FormatUtcMonthKey(at.Year, at.Month));
        }
        return months;
    }

    private void InvalidateContentIndex(ChatContentKind kind, ChatId chatId, ContentIndexPageCounts? pageCounts)
    {
        if (pageCounts == null) {
            // Write phase always puts a ContentIndexPageCounts into Operation.Items,
            // and it round-trips via _Operations.ItemsJson — so reaching this branch
            // means the serialization round-trip broke (e.g. type rename without a
            // backwards-compatibility shim). Bail to the conservative path that at
            // least keeps the public router fresh; the LogError surfaces the
            // breakage in DevLog.
            Log.LogError(
                "InvalidateContentIndex: missing ContentIndexPageCounts for {ChatId}/{Kind} — falling back to skeleton-only invalidation",
                chatId, kind);
            _ = GetContentPeriods(chatId, kind, null, default);
            return;
        }

        // Skeleton: each affected month sits in exactly one calendar-year
        // bucket — invalidate GetContentPeriodsByYear(year). The public
        // GetContentPeriods(beforeKey=...) cascade-invalidates via its
        // dependency on this method.
        var affectedYears = new HashSet<int>();
        foreach (var monthKey in pageCounts.PageCounts.Keys) {
            var (monthStart, _) = ParseUtcMonthRange(monthKey);
            affectedYears.Add(monthStart.Year);
        }
        foreach (var year in affectedYears)
            _ = GetContentPeriodsByYear(chatId, kind, year, default);

        // NextPeriodKey of every skeleton page comes from HasContentBeforeYear(y),
        // where y = page lower bound year. An item at month m flips
        // HasContentBeforeYear(y) iff y > m.Year. Cap at the current calendar
        // year — boundaries beyond it aren't in any current cache entry.
        var currentYear = Clocks.SystemClock.Now.ToDateTime().Year;
        var boundaryYears = new HashSet<int>();
        foreach (var year in affectedYears)
            for (var y = year + 1; y <= currentYear; y++)
                boundaryYears.Add(y);
        foreach (var y in boundaryYears)
            _ = HasContentBeforeYear(chatId, kind, y, default);

        foreach (var (monthKey, count) in pageCounts.PageCounts) {
            var pageCount = Math.Max(1, (count + ChatContentPeriod.PageSize - 1) / ChatContentPeriod.PageSize);
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++) {
                switch (kind) {
                case ChatContentKind.Media:
                    _ = GetVisualMediaPeriod(chatId, monthKey, pageIndex, default);
                    break;
                case ChatContentKind.File:
                    _ = GetFilePeriod(chatId, monthKey, pageIndex, default);
                    break;
                case ChatContentKind.Link:
                    _ = GetLinkPeriod(chatId, monthKey, pageIndex, default);
                    break;
                }
            }
        }
    }

    // Aggregates content items into per-month counts for the given UTC window.
    // Shared by Media/File/Link — tables implement IDbChatContentItem so the
    // same LINQ shape compiles for all three.
    private static async Task<List<(int Year, int Month, int Count)>>
        QueryPeriodCounts<TDbItem>(
            IQueryable<TDbItem> table,
            string chatSid,
            DateTime lowerBoundInclusive,
            DateTime upperBoundExclusive,
            CancellationToken cancellationToken)
        where TDbItem : class, IDbChatContentItem
        => (await table
                .Where(x => x.ChatId == chatSid && x.At >= lowerBoundInclusive && x.At < upperBoundExclusive)
                .GroupBy(x => new { x.At.Year, x.At.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(m => (m.Year, m.Month, m.Count))
            .ToList();

    private static Task<bool> QueryHasContentBefore<TDbItem>(
        IQueryable<TDbItem> table,
        string chatSid,
        DateTime boundaryUtc,
        CancellationToken cancellationToken)
        where TDbItem : class, IDbChatContentItem
        => table.AnyAsync(x => x.ChatId == chatSid && x.At < boundaryUtc, cancellationToken);

    // Loads a paged slice of chat content items for the given UTC month, oldest-first
    // by (At, EntryLocalId, LocalIndex). Shared by Get{VisualMedia,File,Link}Period.
    private static async Task<List<TDbItem>> QueryPeriodPage<TDbItem>(
        IQueryable<TDbItem> table,
        string chatSid,
        string periodKey,
        int pageIndex,
        CancellationToken cancellationToken)
        where TDbItem : class, IDbChatContentItem
    {
        var (periodStart, periodEnd) = ParseUtcMonthRange(periodKey);
        return await table
            .Where(x => x.ChatId == chatSid && x.At >= periodStart && x.At < periodEnd)
            .OrderBy(x => x.At)
            .ThenBy(x => x.EntryLocalId)
            .ThenBy(x => x.LocalIndex)
            .Skip(pageIndex * ChatContentPeriod.PageSize)
            .Take(ChatContentPeriod.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string FormatUtcMonthKey(int year, int month)
        => $"{year:D4}-{month:D2}";

    private static (DateTime Start, DateTime End) ParseUtcMonthRange(string periodKey)
    {
        var year = int.Parse(periodKey.AsSpan(0, 4));
        var month = int.Parse(periodKey.AsSpan(5, 2));
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(1));
    }
}
