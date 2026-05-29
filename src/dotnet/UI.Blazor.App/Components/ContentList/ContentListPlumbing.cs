using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

// Shared helpers for content-list components (VisualMediaList / FileList / LinkList).
// All three iterate the same period-skeleton + paged-page protocol; only the item DTO
// and the rendering differ.
internal static class ContentListPlumbing
{
    // Window grows by whole blocks (atomic fetch + cache unit), but the stop criterion
    // is in items — the user-visible scale. Adapts to per-kind density without a magic
    // block count: one fat block (≤PageSize=300 items) usually covers the target;
    // sparse Link blocks after client-side filtering trigger more rounds.
    public const int TargetItemCount = 60;

    public sealed record Block(string PeriodKey, int PageIndex);

    public static List<Block> BuildBlocks(IReadOnlyList<ChatContentPeriod> periods)
    {
        var blocks = new List<Block>();
        foreach (var period in periods)
            for (var p = period.PageCount - 1; p >= 0; p--)
                blocks.Add(new Block(period.PeriodKey, p));
        return blocks;
    }

    public static int FindBlockIndex(List<Block> blocks, string key)
    {
        var parts = key.Split(':');
        if (parts.Length < 4 || (parts[0] != "r" && parts[0] != "i"))
            return -1;
        if (!int.TryParse(parts[2], out var pageIndex))
            return -1;

        return blocks.FindIndex(b => b.PeriodKey == parts[1] && b.PageIndex == pageIndex);
    }

    public static (string Key, string Title) GetLocalGroup(
        Moment at,
        ContentGrouping groupBy,
        DateTimeConverter dateTimeConverter)
    {
        var d = dateTimeConverter.ToLocalTime(at);
        return groupBy switch {
            ContentGrouping.Day => ($"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}", d.ToString("MMMM d, yyyy")),
            ContentGrouping.Month => ($"{d.Year:D4}-{d.Month:D2}", d.ToString("MMMM yyyy")),
            _ => ("", ""),
        };
    }

    public static ContentListItem EmptyPlaceholder()
        => new() { Key = "empty", IsEmptyPlaceholder = true };

    public static VirtualListData<ContentListItem> EmptyData()
        => new([EmptyPlaceholder()]) {
            HasVeryFirstItem = true,
            HasVeryLastItem = true,
        };

    public static long SumVersion(IReadOnlyList<IChatContentItem> items)
    {
        long version = 0;
        foreach (var item in items)
            version += item.Version;
        return version;
    }

    // Chunks one page of items into rows preserving group boundaries. Items must be oldest-first
    // (the order returned by the backend). The returned rows are also oldest-first; callers that
    // render newest-at-top iterate them in reverse.
    public static List<(int RowIndex, string GroupKey, IChatContentItem[] Items)> ChunkPage(
        IReadOnlyList<IChatContentItem> visible,
        int chunkSize,
        ContentGrouping groupBy,
        DateTimeConverter dateTimeConverter)
    {
        var rows = new List<(int, string, IChatContentItem[])>();
        var rowIndex = 0;
        var runStart = 0;
        while (runStart < visible.Count) {
            var groupKey = GetLocalGroup(visible[runStart].At, groupBy, dateTimeConverter).Key;
            var runEnd = runStart;
            while (runEnd < visible.Count
                && GetLocalGroup(visible[runEnd].At, groupBy, dateTimeConverter).Key == groupKey)
                runEnd++;

            // visible is oldest-first and BuildItems renders rows bottom-up, so the
            // short row must lead the run to land at the bottom of the group.
            var runLength = runEnd - runStart;
            var leadSize = runLength % chunkSize;
            if (leadSize == 0)
                leadSize = Math.Min(chunkSize, runLength);
            for (var p = runStart; p < runEnd;) {
                var size = Math.Min(p == runStart ? leadSize : chunkSize, runEnd - p);
                var slice = new IChatContentItem[size];
                for (var i = 0; i < size; i++)
                    slice[i] = visible[p + i];
                rows.Add((rowIndex++, groupKey, slice));
                p += size;
            }
            runStart = runEnd;
        }
        return rows;
    }

    public static string FileExtension(string fileName)
    {
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < fileName.Length - 1
            ? fileName[(dotIndex + 1)..].ToLowerInvariant()
            : "";
    }

    // Runs the period-based load pipeline shared by VisualMediaList / FileList / LinkList:
    //   skeleton → bidirectional sequential page load → buildItems → VirtualListData.
    // Each component supplies only the typed `loadPage` (which page-method to call) and `buildItems`
    // (which can do extra async work like resolving link entries).
    public static async Task<VirtualListData<ContentListItem>> LoadFromPeriods<TItem>(
        AppUIHub hub,
        Session session,
        ChatId chatId,
        VirtualListDataQuery query,
        VirtualListData<ContentListItem> renderedData,
        Func<Block, CancellationToken, Task<TItem[]>> loadPage,
        Func<IReadOnlyList<Block>, IReadOnlyList<TItem[]>, CancellationToken, Task<List<ContentListItem>>> buildItems,
        CancellationToken cancellationToken)
        where TItem : IChatContentItem
    {
        // Right panel keeps tabs mounted while collapsed (SideNav just CSS-hides them).
        // Defer all compute/fetch while invisible — VirtualList's ComputeState re-runs
        // once IsVisible flips because Use() registers it as a dependency.
        var isVisible = await hub.PanelsUI.Right.IsVisible.Use(cancellationToken).ConfigureAwait(false);
        if (!isVisible)
            return renderedData;

        var kind = ResolveKind<TItem>();
        var periods = new List<ChatContentPeriod>();
        string? cursor = null;
        do {
            var skeleton = await hub.Chats
                .GetContentPeriods(session, chatId, kind, cursor, cancellationToken)
                .ConfigureAwait(false);
            periods.AddRange(skeleton.Periods);
            cursor = skeleton.NextPeriodKey;
        } while (cursor != null);

        var blocks = BuildBlocks(periods);
        if (blocks.Count == 0)
            return EmptyData();

        // Seed window = blocks already covered by VirtualList's KeyRange
        // (or [0..0] for cold-init). `first` = newest block index, `last` = oldest.
        int first, last;
        if (query.IsNone)
            first = last = 0;
        else {
            var s = FindBlockIndex(blocks, query.KeyRange.Start);
            var e = FindBlockIndex(blocks, query.KeyRange.End);
            if (s < 0 || e < 0)
                first = last = 0;
            else {
                first = Math.Min(s, e);
                last = Math.Max(s, e);
            }
        }

        var loaded = new Dictionary<int, TItem[]>();
        var seedItems = 0;
        for (var i = first; i <= last; i++) {
            loaded[i] = await loadPage(blocks[i], cancellationToken).ConfigureAwait(false);
            seedItems += loaded[i].Length;
        }

        // Direction-aware extension driven by MoveRange. VirtualList's contract:
        //   move.Start < 0 → "I need ~N more items BEFORE current first" (toward newer = smaller idx).
        //   move.End   > 0 → "I need ~N more items AFTER current last"  (toward older = larger idx).
        // Grow the relevant side until the request is satisfied — independent of how
        // many items the seed already has. Without this a fat seed block (≥ target)
        // would short-circuit growth and the data source would spin returning the same
        // payload, never reacting to scroll-into-spacer requests.
        var wantBefore = query.IsNone ? 0 : Math.Max(0, -query.MoveRange.Start);
        var wantAfter = query.IsNone ? 0 : Math.Max(0, query.MoveRange.End);
        var gotBefore = 0;
        while (gotBefore < wantBefore && first > 0) {
            first--;
            loaded[first] = await loadPage(blocks[first], cancellationToken).ConfigureAwait(false);
            gotBefore += loaded[first].Length;
        }
        var gotAfter = 0;
        while (gotAfter < wantAfter && last < blocks.Count - 1) {
            last++;
            loaded[last] = await loadPage(blocks[last], cancellationToken).ConfigureAwait(false);
            gotAfter += loaded[last].Length;
        }

        // Cold-init / tiny-seed padding: ensure window covers at least TargetItemCount.
        // Block is the atomic unit of fetch + cache; item count is the stop criterion —
        // adapts to per-kind density without a magic block count.
        var itemCount = seedItems + gotBefore + gotAfter;
        var center = (first + last) / 2;
        while (itemCount < TargetItemCount && (first > 0 || last < blocks.Count - 1)) {
            var canGoOlder = last < blocks.Count - 1;
            var canGoNewer = first > 0;
            int next;
            if (canGoOlder && (!canGoNewer || (last - center) <= (center - first))) {
                last++;
                next = last;
            }
            else {
                first--;
                next = first;
            }
            var items = await loadPage(blocks[next], cancellationToken).ConfigureAwait(false);
            loaded[next] = items;
            itemCount += items.Length;
        }

        var windowBlocks = new List<Block>(last - first + 1);
        var windowContents = new List<TItem[]>(last - first + 1);
        for (var i = first; i <= last; i++) {
            windowBlocks.Add(blocks[i]);
            windowContents.Add(loaded[i]);
        }

        var listItems = await buildItems(windowBlocks, windowContents, cancellationToken).ConfigureAwait(false);
        if (listItems.Count == 0)
            return EmptyData();

        var result = new VirtualListData<ContentListItem>(listItems) {
            Index = renderedData.Index + 1,
            // BuildItems emits newest-first, so the rendered DOM order is newest-at-top.
            // VirtualList treats Items[0] as VeryFirst — keep it aligned with our window.
            HasVeryFirstItem = first == 0,
            HasVeryLastItem = last == blocks.Count - 1,
        };
        var isSame = result.IsSimilarTo(renderedData);
        // Quick window/query diagnostics; warning level guarantees DevLog visibility.
        var log = hub.LogFor(typeof(ContentListPlumbing));
        var winFirst = windowBlocks.Count > 0 ? $"{windowBlocks[0].PeriodKey}:{windowBlocks[0].PageIndex}" : "-";
        var winLast = windowBlocks.Count > 0 ? $"{windowBlocks[^1].PeriodKey}:{windowBlocks[^1].PageIndex}" : "-";
        var itemFirst = listItems.Count > 0 ? listItems[0].Key : "-";
        var itemLast = listItems.Count > 0 ? listItems[^1].Key : "-";
        if (query.IsNone)
            log.LogWarning(
                "CL[{Kind}/{ChatId}] same={IsSame}\n"
                + "  q=<none>\n"
                + "  win={{ range: [{First}..{Last}]/{Total}, first: {WinFirst}, last: {WinLast}, hasFirst: {HasVeryFirst}, hasLast: {HasVeryLast} }}\n"
                + "  items={{ count: {ItemCount}, first: {ItemFirst}, last: {ItemLast} }}",
                kind, chatId, isSame, first, last, blocks.Count, winFirst, winLast,
                result.HasVeryFirstItem, result.HasVeryLastItem,
                listItems.Count, itemFirst, itemLast);
        else
            log.LogWarning(
                "CL[{Kind}/{ChatId}] same={IsSame}\n"
                + "  q={{ keys: {KeyRange}, virt: {VirtualRange}, move: {MoveRange} }}\n"
                + "  win={{ range: [{First}..{Last}]/{Total}, first: {WinFirst}, last: {WinLast}, hasFirst: {HasVeryFirst}, hasLast: {HasVeryLast} }}\n"
                + "  items={{ count: {ItemCount}, first: {ItemFirst}, last: {ItemLast} }}",
                kind, chatId, isSame, query.KeyRange, query.VirtualRange, query.MoveRange,
                first, last, blocks.Count, winFirst, winLast,
                result.HasVeryFirstItem, result.HasVeryLastItem,
                listItems.Count, itemFirst, itemLast);
        return isSame ? renderedData : result;
    }

    private static ChatContentKind ResolveKind<TItem>() where TItem : IChatContentItem
    {
        if (typeof(TItem) == typeof(VisualMediaItem))
            return ChatContentKind.Media;
        if (typeof(TItem) == typeof(FileItem))
            return ChatContentKind.File;
        if (typeof(TItem) == typeof(LinkItem))
            return ChatContentKind.Link;
        throw new ArgumentOutOfRangeException(
            nameof(TItem), typeof(TItem), $"Unknown content item type: {typeof(TItem).Name}");
    }
}
