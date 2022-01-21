using System.Text.Json.Serialization;
using Stl.Reflection;

namespace ActualChat.UI.Blazor.Controls.Internal;

public class VirtualListRenderPlan<TItem>
    where TItem : IVirtualListItem
{
    public sealed class ItemRenderPlan
    {
        public TItem Item { get; }
        public Symbol Key => Item.Key;
        public Range<double> Range { get; set; } = new(-1, -2);
        public double Size => Range.Size();
        public bool IsMeasured => Size >= 0;

        public ItemRenderPlan(TItem item) => Item = item;
    }

    // JsonIgnores are here solely to make JsonFormatter.Format work
    [JsonIgnore]
    public VirtualList<TItem> VirtualList { get; set; } = null!;
    public long RenderIndex { get; set; }
    [JsonIgnore]
    public VirtualListClientSideState? ClientSideState { get; set; }
    [JsonIgnore]
    public VirtualListData<TItem> Data { get; set; } = null!;
    [JsonIgnore]
    public Dictionary<Symbol, ItemRenderPlan> ItemByKey { get; set; }= null!;
    [JsonIgnore]
    public List<ItemRenderPlan> Items { get; set; } = null!;
    [JsonIgnore]
    public IEnumerable<ItemRenderPlan> ReversedItems {
        get {
            for (var i = Items.Count - 1; i >= 0; i--)
                yield return Items[i];
        }
    }

    /// <summary> Relative to the top item's top! </summary>
    public Range<double>? Viewport { get; set; }
    public Range<double> ItemRange { get; set; }
    public Range<double> FullRange => (-SpacerSize, ItemRange.End + EndSpacerSize);
    public Range<double>? LoadZoneRange => Viewport.HasValue ? GetLoadZoneRange(Viewport.GetValueOrDefault()) : null;

    public bool HasUnmeasuredItems { get; set; }
    public double SpacerSize { get; set; }
    public double EndSpacerSize { get; set; }
    public VirtualListEdge? TrackingEdge { get; set; }
    public bool IsEndAligned => VirtualList.PreferredTrackingEdge == VirtualListEdge.End;

    /// <summary> Indicates whether JS backend must notify Blazor part when it's safe to scroll. </summary>
    public bool NotifyWhenSafeToScroll { get; set; }
    /// <summary> Forces programmatic scroll to the <see cref="Viewport"/>. </summary>
    public bool MustScroll { get; set; }

    protected IVirtualListStatistics Statistics => VirtualList.Statistics;
    protected VirtualListEdge PreferredTrackingEdge => VirtualList.PreferredTrackingEdge;
    protected ILogger Log => VirtualList.Log;
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected bool DebugMode => VirtualList.DebugMode;

    public VirtualListRenderPlan(VirtualList<TItem> virtualList)
    {
        VirtualList = virtualList;
        RenderIndex = 1;
        Data = new VirtualListData<TItem>();
        SpacerSize = VirtualList.SpacerSize;
        EndSpacerSize = VirtualList.SpacerSize;
        TrackingEdge = VirtualList.InitialTrackingEdge;
        Update(null);
    }

    // Misc. helpers
    public bool IsVeryFirstItemMeasured() => Data.HasVeryFirstItem && (Items.Count == 0 || Items[0].IsMeasured);
    public bool IsVeryLastItemMeasured() => Data.HasVeryLastItem && (Items.Count == 0 || Items[^1].IsMeasured);
    public double GetPerfectSpacerSize() => IsVeryFirstItemMeasured() ? 0 : VirtualList.SpacerSize;
    public double GetPerfectEndSpacerSize() => IsVeryLastItemMeasured() ? 0 : VirtualList.SpacerSize;
    public bool IsViewportAtStart(Range<double> viewport) => viewport.Start <= ItemRange.Start + 4;
    public bool IsViewportAtEnd(Range<double> viewport) => viewport.End >= ItemRange.End - 4;
    public Range<double> GetStartViewport(Range<double> viewport) => new(0, viewport.Size());
    public Range<double> GetEndViewport(Range<double> viewport) => new(ItemRange.End - viewport.Size(), ItemRange.End);
    public Range<double> GetInitialViewport(Range<double> viewport) => IsEndAligned ? GetEndViewport(viewport) : GetStartViewport(viewport);
    public Range<double> GetLoadZoneRange(Range<double> viewport)
        => new(
            viewport.Start - (Data.HasVeryFirstItem ? 0 : VirtualList.LoadZoneSize),
            viewport.End + (Data.HasVeryLastItem ? 0 : VirtualList.LoadZoneSize));

    public virtual VirtualListRenderPlan<TItem> Next()
    {
        try {
            var plan = (VirtualListRenderPlan<TItem>) MemberwiseClone();
            plan.RenderIndex++;
            plan.Data = VirtualList.Data;
            plan.ClientSideState = VirtualList.ClientSideState;
            plan.NotifyWhenSafeToScroll = false;
            plan.MustScroll = false;
            plan.Update(this);
            return plan;
        }
        catch (Exception e) {
            Log.LogError(e, "Error while computing the next render plan");
            throw;
        }
    }

    public bool IsFullyLoaded()
    {
        if (Data.HasAllItems)
            return true;
        var loadZoneRange = LoadZoneRange;
        return loadZoneRange.HasValue && ItemRange.Contains(loadZoneRange.GetValueOrDefault());
    }

    // Protected & private methods

    private void Update(VirtualListRenderPlan<TItem>? lastPlan)
    {
        var statistics = Statistics;
        var newItemSizes = ClientSideState?.ItemSizes;
        var prevItemByKey = lastPlan?.ItemByKey;

        ItemByKey = new Dictionary<Symbol, ItemRenderPlan>();
        Items = new List<ItemRenderPlan>();
        HasUnmeasuredItems = false;
        var itemRange = default(Range<double>);
        foreach (var item in Data.Items) {
            var newItem = new ItemRenderPlan(item);
            if (newItemSizes != null && newItemSizes.TryGetValue(item.Key, out var newSize)) {
                statistics.AddItem(newSize, item.CountAs);
                newItem.Range = new Range<double>(0, newSize);
            }
            else if (prevItemByKey != null && prevItemByKey.TryGetValue(item.Key, out var oldItem))
                newItem.Range = oldItem.Range; // Just to copy its size

            Items.Add(newItem);
            ItemByKey.Add(item.Key, newItem);
            if (newItem.IsMeasured) {
                itemRange = new(itemRange.End, itemRange.End + newItem.Size);
                newItem.Range = itemRange;
            }
            else
                HasUnmeasuredItems = true;
        }
        ItemRange = new Range<double>(0, itemRange.End);
        SpacerSize = GetPerfectSpacerSize();
        EndSpacerSize = GetPerfectEndSpacerSize();

        UpdateViewport(lastPlan);
        UpdateClientSideState();
    }

    private void UpdateViewport(VirtualListRenderPlan<TItem>? lastPlan)
    {
        // Let's update the Viewport first
        if (ClientSideState == null) {
            // Never rendered
            DebugLog?.LogDebug("ClientSideState == null");
            return;
        }
        if (lastPlan != null) {
            var itemsChanges = lastPlan.Items.Count != Items.Count;
            if (!itemsChanges) {
                foreach (var oldItem in lastPlan.Items) {
                    var newItem = ItemByKey.GetValueOrDefault(oldItem.Key);
                    if (newItem == null || !newItem.IsMeasured) {
                        itemsChanges = true;
                        break;
                    }
                }
            }
            if (itemsChanges) {
                Viewport = null;
                DebugLog?.LogDebug("Items changed; Viewport = null");
                return;
            }
        }

        if (!TryGetViewport(ClientSideState, out var clientSideViewport)) {
            DebugLog?.LogDebug("ClientSideState: Viewport = n/a (DisplayedRange = {DisplayedRange})", ItemRange);
            return;
        }

        DebugLog?.LogDebug("ClientSideState: Viewport = {ClientSideViewport} (DisplayedRange = {DisplayedRange})",
            clientSideViewport, ItemRange);
        var viewport = clientSideViewport.ScrollInto(FullRange, IsEndAligned);
        if (DebugLog != null && viewport != clientSideViewport)
            DebugLog.LogDebug("Viewport adjustment: {ClientSideViewport} -> {Viewport}", clientSideViewport, viewport);

        if (ClientSideState.IsUserScrollDetected) {
            var isViewportAtStart = IsViewportAtStart(viewport);
            var isViewportAtEnd = IsViewportAtEnd(viewport);
            if (IsVeryFirstItemMeasured() && isViewportAtStart)
                TrackingEdge = VirtualListEdge.Start;
            else if (IsVeryLastItemMeasured() && isViewportAtEnd)
                TrackingEdge = VirtualListEdge.End;
            else
                TrackingEdge = null;
            if (DebugLog != null) {
                var position = isViewportAtStart
                    ? isViewportAtEnd ? "start & end" : "start"
                    : isViewportAtEnd ? "end" : "inside";
                DebugLog.LogDebug("User scroll: now @ {Position}, TrackingEdge={TrackingEdge}", position, TrackingEdge);
            }
        }

        var gotVeryFirstItem = Data.HasVeryFirstItem && !(lastPlan?.Data.HasVeryFirstItem ?? false);
        var gotVeryLastItem = Data.HasVeryLastItem && !(lastPlan?.Data.HasVeryLastItem ?? false);
        var firstItemChanged = Data.HasVeryFirstItem
            && Items.FirstOrDefault()?.Key != lastPlan?.Items.FirstOrDefault()?.Key;
        var lastItemChanged = Data.HasVeryLastItem
            && Items.LastOrDefault()?.Key != lastPlan?.Items.LastOrDefault()?.Key;

        if (TrackingEdge == VirtualListEdge.Start && firstItemChanged) {
            if (TrackingEdge == VirtualListEdge.End && lastItemChanged && PreferredTrackingEdge == VirtualListEdge.End)
                ScrollTo(GetEndViewport(viewport), "tracking + last item changed");
            else
                ScrollTo(GetStartViewport(viewport), "tracking + first item changed");
        }
        else if (TrackingEdge == VirtualListEdge.End && lastItemChanged) {
            ScrollTo(GetEndViewport(viewport), "tracking + last item changed");
        }
        else if (IsEndAligned ? gotVeryLastItem : gotVeryFirstItem) {
            ScrollTo(GetInitialViewport(viewport), "just got very first/last item");
        }
        else {
            Viewport = viewport;
        }
    }

    public void UpdateClientSideState()
    {
        if (ClientSideState == null)
            return;

        var newClientSideState = MemberwiseCloner.Invoke(ClientSideState);
        newClientSideState.SpacerSize = SpacerSize;
        newClientSideState.EndSpacerSize = EndSpacerSize;
        newClientSideState.ScrollHeight = HasUnmeasuredItems ? null : FullRange.Size();
        newClientSideState.ScrollTop = Viewport?.Start;
        newClientSideState.ViewportHeight = Viewport?.Size();
        ClientSideState = newClientSideState;
    }

    private void ScrollTo(Range<double> viewport, string why)
    {
        DebugLog?.LogDebug("ScrollTo: {OldViewport} -> {Viewport}, {Why}", Viewport, viewport, why);
        Viewport = viewport;
        MustScroll = true;
    }

    private bool TryGetViewport(VirtualListClientSideState clientSideState, out Range<double> viewport)
    {
        viewport = default;
        if (clientSideState.ViewportHeight is not { } viewportHeight)
            return false;
        if (clientSideState.ScrollTop is not { } scrollTop)
            return false;
        viewport = (scrollTop, scrollTop + viewportHeight);
        return true;
    }
}
