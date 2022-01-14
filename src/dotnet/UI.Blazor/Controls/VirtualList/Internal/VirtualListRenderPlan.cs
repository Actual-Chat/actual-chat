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
    public List<ItemRenderPlan> LoadedItems { get; set; } = null!;
    [JsonIgnore]
    public List<ItemRenderPlan> UnmeasuredItems { get; set; } = null!;
    [JsonIgnore]
    public List<ItemRenderPlan> DisplayedItems { get; set; } = null!;
    [JsonIgnore]
    public IEnumerable<ItemRenderPlan> ReversedDisplayedItems {
        get {
            for (var i = DisplayedItems.Count - 1; i >= 0; i--)
                yield return DisplayedItems[i];
        }
    }

    /// <summary> Relative to the top item's top! </summary>
    public Range<double> Viewport { get; set; }
    public Range<double> DisplayedRange { get; set; }
    public Range<double> FullRange => (-SpacerSize, DisplayedRange.End + EndSpacerSize);
    public bool WasEverRendered { get; set; }

    public double SpacerSize { get; set; }
    public double EndSpacerSize { get; set; }
    public VirtualListEdge? TrackingEdge { get; set; }
    public bool IsEndAligned => VirtualList.PreferredTrackingEdge == VirtualListEdge.End;
    public double ScrollTop => IsEndAligned
        ? Viewport.End - (DisplayedRange.End + EndSpacerSize)
        : SpacerSize + Viewport.Start;

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
        Data = VirtualList.Data;
        SpacerSize = VirtualList.SpacerSize;
        EndSpacerSize = VirtualList.SpacerSize;
        Viewport = new(0, 128);
        Update(null);
        Viewport = PreferredTrackingEdge == VirtualListEdge.End ? GetEndViewport() : GetStartViewport();
        TrackingEdge = PreferredTrackingEdge;
    }

    // Misc. helpers
    public bool IsViewportAtStart() => Viewport.Start <= DisplayedRange.Start + 4;
    public bool IsViewportAtEnd() => Viewport.End >= DisplayedRange.End - 4;
    public Range<double> GetStartViewport() => new(0, Viewport.Size());
    public Range<double> GetEndViewport() => new(DisplayedRange.End - Viewport.Size(), DisplayedRange.End);
    public bool IsVeryFirstItemMeasured() => Data.HasVeryFirstItem && (LoadedItems.Count == 0 || LoadedItems[0].IsMeasured);
    public bool IsVeryLastItemMeasured() => Data.HasVeryLastItem && (LoadedItems.Count == 0 || LoadedItems[^1].IsMeasured);
    public double GetPerfectSpacerSize() => IsVeryFirstItemMeasured() ? 0 : VirtualList.SpacerSize;
    public double GetPerfectEndSpacerSize() => IsVeryLastItemMeasured() ? 0 : VirtualList.SpacerSize;
    public Range<double> GetLoadZoneRange()
        => new(
            Viewport.Start - (Data.HasVeryFirstItem ? 0 : VirtualList.LoadZoneSize),
            Viewport.End + (Data.HasVeryLastItem ? 0 : VirtualList.LoadZoneSize));

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

    public bool IsFullyLoaded(Range<double> viewRange)
        => Data.HasAllItems || DisplayedRange.Contains(viewRange);

    public void Update(VirtualListRenderPlan<TItem>? lastPlan)
    {
        var statistics = Statistics;
        var newItemSizes = ClientSideState?.ItemSizes;
        var prevItemByKey = lastPlan?.ItemByKey;

        ItemByKey = new Dictionary<Symbol, ItemRenderPlan>();
        LoadedItems = new List<ItemRenderPlan>();
        DisplayedItems = new List<ItemRenderPlan>();
        UnmeasuredItems = new List<ItemRenderPlan>();
        var itemRange = default(Range<double>);
        foreach (var item in Data.Items) {
            var newItem = new ItemRenderPlan(item);
            if (newItemSizes != null && newItemSizes.TryGetValue(item.Key, out var newSize)) {
                statistics.AddItem(newSize, item.CountAs);
                newItem.Range = new Range<double>(0, newSize);
            }
            else if (prevItemByKey != null && prevItemByKey.TryGetValue(item.Key, out var oldItem))
                newItem.Range = oldItem.Range; // Just to copy its size

            LoadedItems.Add(newItem);
            ItemByKey.Add(item.Key, newItem);
            if (newItem.IsMeasured) {
                itemRange = new(itemRange.End, itemRange.End + newItem.Size);
                newItem.Range = itemRange;
                DisplayedItems.Add(newItem);
            }
            else
                UnmeasuredItems.Add(newItem);
        }
        DisplayedRange = new Range<double>(0, itemRange.End);

        UpdateViewportAndSpacer(lastPlan);
        UpdateScrollRelated(lastPlan);
        UpdateClientSideState();
    }

    protected void UpdateViewportAndSpacer(VirtualListRenderPlan<TItem>? lastPlan)
    {
        // Let's update the Viewport first
        if (ClientSideState != null) {
            // Remember that all server-side offsets are relative to the first item's top / spacer top
            var viewport = GetViewport(ClientSideState);
            if (DebugMode && ClientSideState.IsUserScrollDetected) {
                DebugLog?.LogDebug("User scroll: {Viewport} -> {NewViewport}", Viewport, viewport);
                if (Math.Abs(viewport.Start - Viewport.Start) > 3000)
                    DebugLog?.LogWarning("Suspicious scroll detected!");
            }
            Viewport = viewport;
        }

        // Find a measured (visible) item that exists in both old & new plans
        var (item, oldItem) = FindMatchingDisplayedItem(lastPlan);
        if (item != null && oldItem != null) {
            // Update Viewport, SpacerSize & EndSpacerSize
            var itemToBottom = DisplayedRange.End - item.Range.End;
            var oldItemToBottom = lastPlan!.DisplayedRange.End - oldItem.Range.End;
            var topInsertSize = item.Range.Start - oldItem.Range.Start;
            var bottomInsertSize = itemToBottom - oldItemToBottom;
            DebugLog?.LogDebug("Insertion size: top {Top}, bottom {Bottom} @ {Key}", topInsertSize, bottomInsertSize, item.Key);
            Viewport = Viewport.Move(topInsertSize);
            SpacerSize -= topInsertSize;
            EndSpacerSize -= bottomInsertSize;
            if (IsVeryFirstItemMeasured())
                SpacerSize = 0;
            if (IsVeryLastItemMeasured())
                EndSpacerSize = 0;
        }
        else if (!WasEverRendered && DisplayedItems.Count > 0) {
            // First load
            WasEverRendered = true;
            TrackingEdge = VirtualList.PreferredTrackingEdge;
            Viewport = TrackingEdge == VirtualListEdge.Start ? GetStartViewport() : GetEndViewport();
        }
        else if (DisplayedItems.Count > 0) {
            // Full refresh, i.e. no single common item between the old and the new plan
            ApplyMustScroll("full refresh");
        }
    }

    protected void UpdateScrollRelated(VirtualListRenderPlan<TItem>? lastPlan)
    {
        if (lastPlan != null && (ClientSideState?.IsUserScrollDetected ?? true)) {
            if (IsVeryFirstItemMeasured() && IsViewportAtStart())
                TrackingEdge = VirtualListEdge.Start;
            else if (IsVeryLastItemMeasured() && IsViewportAtEnd())
                TrackingEdge = VirtualListEdge.End;
            else
                TrackingEdge = null;
        }
        DebugLog?.LogDebug("Location={Location}, TrackingEdge={TrackingEdge}, Viewport={Viewport} in {DisplayedRange}",
            (IsViewportAtStart() ? "start " : "") + (IsViewportAtEnd() ? "end" : ""),
            TrackingEdge, Viewport, DisplayedRange);

        var gotVeryFirstItem = Data.HasVeryFirstItem && !(lastPlan?.Data.HasVeryFirstItem ?? false);
        var gotVeryLastItem = Data.HasVeryLastItem && !(lastPlan?.Data.HasVeryLastItem ?? false);
        var firstItemChanged = Data.HasVeryFirstItem
            && DisplayedItems.FirstOrDefault()?.Key != lastPlan?.DisplayedItems.FirstOrDefault()?.Key;
        var lastItemChanged = Data.HasVeryLastItem
            && DisplayedItems.LastOrDefault()?.Key != lastPlan?.DisplayedItems.LastOrDefault()?.Key;

        if (TrackingEdge == VirtualListEdge.Start && firstItemChanged) {
            // Start is aligned, so we have to scroll to the top
            if (TrackingEdge == VirtualListEdge.End && lastItemChanged && PreferredTrackingEdge == VirtualListEdge.End)
                // _And_ end is aligned + bottom scroll is preferred,, so we have to scroll to the bottom
                Viewport = GetEndViewport();
            else
                Viewport = GetStartViewport();
            ApplyMustScroll("tracking item changed (1)");
        } else if (TrackingEdge == VirtualListEdge.End && lastItemChanged) {
            // End is aligned, so we have to scroll to the bottom
            Viewport = GetEndViewport();
            ApplyMustScroll("tracking item changed (2)");
        } else if (IsEndAligned ? gotVeryLastItem : gotVeryFirstItem) {
            // Just got the very first/last item, so the spacer size will change to 0 -> we have to scroll
            ApplyMustScroll("just got very first/last item");
        } else if (SpacerSize < 0 || EndSpacerSize < 0) {
            // We've got negative spacer size due to loading of new items @ the top
            ApplyMustScroll("negative spacer size");
        }

        // Finally, we ensure the viewport fits into its new min-max boundaries;
        // Remember, we maybe removed & added some items + applied size changes,
        // so there is no warranty the viewport will actually be fully inside
        // the new FullRange.
        Viewport = Viewport.ScrollInto(FullRange, IsEndAligned);
        return;

        // AY: This part causes weird issues w/ scroll, to be investigated later.
        if (!MustScroll) {
            // 3. We aren't scrolling, but maybe we still want to adjust the spacer...
            var maxSpacerSizeDelta = Math.Max(
                Math.Abs(SpacerSize - GetPerfectSpacerSize()),
                Math.Abs(EndSpacerSize - GetPerfectEndSpacerSize()));
            var mustAdjustSpacerSize = maxSpacerSizeDelta > VirtualList.SpacerSize / 2.0;
            if (!mustAdjustSpacerSize)
                return;

            var isSafeToScroll = ClientSideState?.IsSafeToScroll ?? false;
            if (isSafeToScroll)
                ApplyMustScroll("adjusting spacer size when it's safe to scroll");
            else
                NotifyWhenSafeToScroll = true;
        }
    }

    protected void UpdateClientSideState()
    {
        if (!MustScroll || ClientSideState == null)
            return;
        var newClientSideState = MemberwiseCloner.Invoke(ClientSideState);
        newClientSideState.SpacerSize = SpacerSize;
        newClientSideState.EndSpacerSize = EndSpacerSize;
        newClientSideState.ScrollHeight = FullRange.Size();
        newClientSideState.ScrollTop = ScrollTop;
        ClientSideState = newClientSideState;
    }

    protected void ApplyMustScroll(string why)
    {
        DebugLog?.LogDebug("ApplyMustScroll: {Why}", why);
        MustScroll = true;
        SpacerSize = GetPerfectSpacerSize();
        EndSpacerSize = GetPerfectEndSpacerSize();
    }

    protected (ItemRenderPlan? Item, ItemRenderPlan? OldItem) FindMatchingDisplayedItem(
        VirtualListRenderPlan<TItem>? lastPlan)
    {
        if (lastPlan == null)
            return (null, null);
        var displayedItems = IsEndAligned ? ReversedDisplayedItems : DisplayedItems;
        foreach (var item in displayedItems) {
            var prevItem = lastPlan.ItemByKey.GetValueOrDefault(item.Key);
            if (prevItem is { IsMeasured: true })
                return (item, prevItem);
        }
        return (null, null);
    }

    protected Range<double> GetViewport(VirtualListClientSideState clientSideState)
    {
        Range<double> viewport;
        if (IsEndAligned) {
            var viewportEnd = clientSideState.ScrollHeight + clientSideState.ScrollTop - clientSideState.SpacerSize;
            viewport = (viewportEnd - clientSideState.ClientHeight, viewportEnd);
        } else {
            var viewportStart = clientSideState.ScrollTop - clientSideState.SpacerSize;
            viewport = (viewportStart, viewportStart + clientSideState.ClientHeight);
        }
        return viewport;
    }
}
