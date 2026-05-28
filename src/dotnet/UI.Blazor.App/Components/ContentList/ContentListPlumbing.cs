using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

// Shared helpers for content-list components (VisualMediaList / FileList / LinkList).
// All three iterate the same period-skeleton + paged-page protocol; only the item DTO
// and the rendering differ.
internal static class ContentListPlumbing
{
    public const int GridColumns = 3;
    // Window sizes are in raw period-items (pre-filter). For Link kind a significant
    // fraction of items can be dropped by ChunkPage when the link preview didn't resolve
    // (stale/archived entries), so the window must be roomy enough for the survivors to
    // still fill the viewport after filtering.
    public const int InitialWindow = 200;
    public const int MinWindow = 120;

    public sealed record Block(string PeriodKey, int PageIndex, int ItemCount);

    public static List<Block> BuildBlocks(IReadOnlyList<ChatContentPeriod> periods)
    {
        var pageSize = ChatContentPeriod.PageSize;
        var blocks = new List<Block>();
        foreach (var period in periods) {
            var pageCount = Math.Max(1, (period.ItemCount + pageSize - 1) / pageSize);
            for (var p = pageCount - 1; p >= 0; p--) {
                var itemCount = p == pageCount - 1 ? period.ItemCount - p * pageSize : pageSize;
                blocks.Add(new Block(period.PeriodKey, p, itemCount));
            }
        }
        return blocks;
    }

    public static (int First, int Last) GetWindow(VirtualListDataQuery query, List<Block> blocks)
    {
        int first, last;
        if (query.IsNone)
            (first, last) = (0, 0);
        else {
            var s = FindBlockIndex(blocks, query.KeyRange.Start);
            var e = FindBlockIndex(blocks, query.KeyRange.End);
            if (s < 0 || e < 0)
                (first, last) = (0, 0);
            else {
                first = Math.Max(0, Math.Min(s, e) - 1);
                last = Math.Min(blocks.Count, Math.Max(s, e) + 2);
            }
        }

        var targetCount = first == last ? InitialWindow : MinWindow;
        var sum = 0;
        for (var i = first; i < last; i++)
            sum += blocks[i].ItemCount;
        // Bidirectional expansion: when at the boundary (oldest or newest), we'd otherwise
        // stop with too few items in the window. Keep growing in whichever direction is
        // still available until the window reaches targetCount or we run out of blocks.
        while (sum < targetCount && (last < blocks.Count || first > 0)) {
            if (last < blocks.Count)
                sum += blocks[last++].ItemCount;
            else
                sum += blocks[--first].ItemCount;
        }
        if (last == first)
            last = Math.Min(blocks.Count, first + 1);
        return (first, last);
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
    //   skeleton → window → typed pages → buildItems → VirtualListData with spacer counts.
    // Each component supplies only the typed `loadPage` (which page-method to call) and `buildItems`
    // (which can do extra async work like resolving link entries).
    public static async Task<VirtualListData<ContentListItem>> LoadFromPeriods<TItem>(
        AppUIHub hub,
        Session session,
        ChatId chatId,
        ChatContentKind kind,
        VirtualListDataQuery query,
        VirtualListData<ContentListItem> renderedData,
        Func<string, int, CancellationToken, Task<TItem[]>> loadPage,
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

        var periods = await hub.Chats.GetContentPeriods(session, chatId, kind, cancellationToken).ConfigureAwait(false);
        if (periods.Length == 0)
            return EmptyData();

        var blocks = BuildBlocks(periods);
        var (first, last) = GetWindow(query, blocks);
        var windowBlocks = blocks.GetRange(first, last - first);
        var contents = await windowBlocks
            .Select(b => loadPage(b.PeriodKey, b.PageIndex, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var listItems = await buildItems(windowBlocks, contents, cancellationToken).ConfigureAwait(false);
        if (listItems.Count == 0)
            return EmptyData();

        // BeforeCount/AfterCount let VirtualList size the spacers proportionally to the
        // unloaded blocks instead of falling back to defaultSpacerSize (1000px each).
        // Without these, scrollHeight overshoots the actual data: scrolling to the bottom
        // lands the viewport inside the bottom spacer (empty), and a subsequent scroll up
        // can leave the query KeyRange anchored to the old bottom window.
        var beforeCount = 0;
        for (var b = 0; b < first; b++)
            beforeCount += blocks[b].ItemCount;
        var afterCount = 0;
        for (var b = last; b < blocks.Count; b++)
            afterCount += blocks[b].ItemCount;

        var result = new VirtualListData<ContentListItem>(listItems) {
            Index = renderedData.Index + 1,
            // BuildItems emits newest-first, so the rendered DOM order is newest-at-top.
            // VirtualList treats Items[0] as VeryFirst — keep it aligned with our window.
            HasVeryFirstItem = first == 0,
            HasVeryLastItem = last == blocks.Count,
            BeforeCount = beforeCount,
            AfterCount = afterCount,
        };
        return result.IsSimilarTo(renderedData) ? renderedData : result;
    }
}
