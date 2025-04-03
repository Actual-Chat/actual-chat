using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public partial class LogList : IVirtualListDataSource<LogEntry>
{
    private volatile VirtualListItemVisibility? _visibility;

    public async Task<VirtualListData<LogEntry>> GetData(
        VirtualListDataQuery query,
        VirtualListData<LogEntry> renderedData,
        CancellationToken cancellationToken)
    {
        var visibility = _visibility;
        var fullIdRange = await LogUI.GetIdRange(cancellationToken).ConfigureAwait(false);
        // TODO: use _visibility
        var tiles = await LogUI.GetTiles(GetIdRange(), cancellationToken).ConfigureAwait(false);
        if (tiles.Count == 0)
            return VirtualListData<LogEntry>.None;

        var result = new VirtualListData<LogEntry>(tiles.ToVirtualListTiles()) {
            Index = renderedData.Index + 1,
            HasVeryFirstItem = tiles.ContainStart(fullIdRange.Start),
            HasVeryLastItem = tiles.ContainEnd(fullIdRange.End),
            ItemVisibilityState = visibility,
        };

        // Return the old data if the new one is identical (to prevent re-renders)
        return result.IsSimilarTo(renderedData) ? renderedData : result;

        Range<long> GetIdRange()
        {
            if (query.IsNone) {
                var start = (fullIdRange.End - 40).Clamp(fullIdRange.Start, fullIdRange.End - 1);
                return new (start, fullIdRange.End);
            }

            return query.KeyRange.ToLongRange().Move(query.MoveRange);
        }
    }

    // Private methods

    private void OnItemVisibilityChanged(VirtualListItemVisibility visibility)
        => _visibility = visibility;
}
