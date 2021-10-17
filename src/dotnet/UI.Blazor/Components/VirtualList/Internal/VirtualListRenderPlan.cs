using System.Text.Json.Serialization;
using ActualChat.Mathematics;

namespace ActualChat.UI.Blazor.Components.Internal;

public class VirtualListRenderPlan<TItem>
{
    public sealed class ItemRenderPlan
    {
        public string Key { get; init; } = null!;
        public TItem Item { get; init; } = default!;
        public Range<double> Range { get; set; } = new(-1, -2);
        public double Size => Range.Size();
        public bool IsMeasured => Size >= 0;
    }

    // JsonIgnores are here solely to make JsonFormatter.Format work
    [JsonIgnore]
    public VirtualList<TItem> VirtualList { get; set; } = null!;
    public long RenderIndex { get; set; } = 0;
    [JsonIgnore]
    public VirtualListClientSideState? ClientSideState { get; set; }
    [JsonIgnore]
    public VirtualListData<TItem> Data { get; set; } = null!;
    [JsonIgnore]
    public Dictionary<string, ItemRenderPlan> ItemByKey { get; set; }= null!;
    [JsonIgnore]
    public List<ItemRenderPlan> LoadedItems { get; set; } = null!;
    [JsonIgnore]
    public List<ItemRenderPlan> UnmeasuredItems { get; set; } = null!;
    [JsonIgnore]
    public List<ItemRenderPlan> DisplayedItems { get; set; } = null!;

    /// <summary> Relative to the top item's top! </summary>
    public Range<double> Viewport { get; set; }
    public Range<double> DisplayedRange { get; set; }
    public Range<double> FullRange => (-SpacerSize, DisplayedRange.Size() + BottomSpacerSize);

    public bool IsViewportAtStart => Viewport.Start <= DisplayedRange.Start + 1;
    public bool IsViewportAtEnd => Viewport.End >= DisplayedRange.End - 1;
    public bool IsTrackingStart { get; set; }
    public bool IsTrackingEnd { get; set; }

    public double SpacerSize { get; set; }
    public double PerfectSpacerSize => Data.HasVeryFirstItem ? 0 : VirtualList.SpacerSize;
    public double BottomSpacerSize => Data.HasVeryLastItem ? 0 : VirtualList.SpacerSize;

    /// <summary> Indicates whether JS backend must notify Blazor part when it's safe to scroll. </summary>
    public bool NotifyWhenSafeToScroll { get; set; }

    /// <summary> Forces programmatic scroll to the <see cref="Viewport"/>. </summary>
    public bool MustScroll { get; set; }

    protected ILogger Log => VirtualList.Log;
    protected IVirtualListStatistics Statistics => VirtualList.Statistics;

    public VirtualListRenderPlan() { }
    public VirtualListRenderPlan(VirtualList<TItem> virtualList)
    {
        VirtualList = virtualList;
        RenderIndex = 0;
        Data = VirtualList.Data;

        SpacerSize = VirtualList.SpacerSize;
        Update(null);
        Viewport = DisplayedRange;
    }

    public virtual VirtualListRenderPlan<TItem> Next()
    {
        try {
            var plan = (VirtualListRenderPlan<TItem>) MemberwiseClone();
            plan.RenderIndex = RenderIndex + 1;
            plan.Data = VirtualList.Data;
            plan.ClientSideState = VirtualList.ClientSideState;

            plan.NotifyWhenSafeToScroll = false;
            plan.MustScroll = false;
            plan.Update(this);
            return plan;
        }
        catch (Exception e) {
            Log.LogError(e, "Error while computing the next RenderPlan");
            throw;
        }
    }

    public bool IsFullyLoaded(Range<double> viewport)
        => Data.HasAllItems || DisplayedRange.Contains(viewport);

    public void Update(VirtualListRenderPlan<TItem>? lastPlan)
    {
        var statistics = Statistics;
        var newItemSizes = ClientSideState?.ItemSizes;
        var prevItemByKey = lastPlan?.ItemByKey;

        LoadedItems = new List<ItemRenderPlan>();
        ItemByKey = new Dictionary<string, ItemRenderPlan>(StringComparer.Ordinal);
        DisplayedItems = new List<ItemRenderPlan>();
        UnmeasuredItems = new List<ItemRenderPlan>();
        var itemRange = default(Range<double>);
        foreach (var (key, item) in Data.Items) {
            var newItem = new ItemRenderPlan { Key = key, Item = item };
            if (newItemSizes != null && newItemSizes.TryGetValue(key, out var newSize)) {
                statistics.AddItem(newSize);
                newItem.Range = new Range<double>(0, newSize);
            }
            else if (prevItemByKey != null && prevItemByKey.TryGetValue(key, out var oldItem))
                newItem.Range = oldItem.Range; // Just to copy its size

            LoadedItems.Add(newItem);
            ItemByKey.Add(key, newItem);
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
    }

    protected void UpdateViewportAndSpacer(VirtualListRenderPlan<TItem>? lastPlan)
    {
        // Let's update the Viewport first
        if (ClientSideState != null) {
            // Remember that all server-side offsets are relative to the first item's top / spacer top
            var viewportStart = ClientSideState.ScrollTop - ClientSideState.SpacerSize;
            var viewport = new Range<double>(viewportStart, viewportStart + ClientSideState.ClientHeight);
            if (ClientSideState.IsUserScrollDetected) {
                Log.LogInformation("User scroll: {VP} -> {NewVP}", Viewport, viewport);
                if (Math.Abs(viewport.Start - Viewport.Start) > 2000)
                    Log.LogWarning("Suspicious scroll detected!");
            }
            Viewport = viewport;
        }

        // Find a measured (visible) item that exists in both old & new plans
        var (item, oldItem) = FindMatchingDisplayedItem(lastPlan);
        if (item != null && oldItem != null) {
            // Update Viewport & SpacerSize
            var topExpansion = item.Range.Start - oldItem.Range.Start;
            Log.LogInformation("Top expansion: {TopExpansion} @ {Key}, [{KS} ... {KE}] of [{KS1} ... {KE1}]",
                topExpansion,
                item.Key,
                DisplayedItems.FirstOrDefault()?.Key, DisplayedItems.LastOrDefault()?.Key,
                LoadedItems.FirstOrDefault()?.Key, LoadedItems.LastOrDefault()?.Key);
            Viewport = Viewport.Move(topExpansion);
            SpacerSize -= topExpansion;
        }
        else {
            // Full refresh, i.e. no single common item between the old and the new plan
            ApplyMustScroll();
        }
    }

    protected void UpdateScrollRelated(VirtualListRenderPlan<TItem>? lastPlan)
    {
        var displayedRange = DisplayedRange;
        var viewportSize = Viewport.Size();
        var topViewport = new Range<double>(0, viewportSize);
        var bottomViewport = new Range<double>(displayedRange.End - viewportSize, displayedRange.End);

        if (ClientSideState?.IsUserScrollDetected ?? true) {
            if (lastPlan == null) { // First frame
                IsTrackingStart = VirtualList.PreferredTrackingEdge == VirtualListEdge.Start;
                IsTrackingEnd = !IsTrackingStart;
            }
            else {
                IsTrackingStart = Data.HasVeryFirstItem && IsViewportAtStart;
                IsTrackingEnd = Data.HasVeryLastItem && IsViewportAtEnd;
            }
            // Log.LogInformation("Position: {Position}",
            //     (IsViewportAtStart ? "start " : "") + (IsViewportAtEnd ? "end" : ""));
            // Log.LogInformation("Tracking: {Tracking}",
            //     (IsTrackingStart ? "start " : "") + (IsTrackingEnd ? "end" : ""));
        }

        var firstItemChanged = Data.HasVeryFirstItem
            && !StringComparer.Ordinal.Equals(DisplayedItems.FirstOrDefault()?.Key, lastPlan?.DisplayedItems.FirstOrDefault()?.Key);
        var lastItemChanged = Data.HasVeryLastItem
            && !StringComparer.Ordinal.Equals(DisplayedItems.LastOrDefault()?.Key, lastPlan?.DisplayedItems.LastOrDefault()?.Key);

        if (IsTrackingStart && firstItemChanged) {
            // Start is aligned, so we have to scroll to the top
            if (IsTrackingEnd && lastItemChanged && VirtualList.PreferredTrackingEdge == VirtualListEdge.End)
                // _And_ end is aligned + bottom scroll is preferred,, so we have to scroll to the bottom
                Viewport = bottomViewport;
            else
                Viewport = topViewport;
            ApplyMustScroll();
        } else if (IsTrackingEnd && lastItemChanged) {
            // End is aligned, so we have to scroll to the bottom
            Viewport = bottomViewport;
            ApplyMustScroll();
        } else if (Data.HasVeryFirstItem && !(lastPlan?.Data.HasVeryFirstItem ?? false)) {
            // Just got the very first item, so top spacer size will change to 0 -> we have to scroll
            ApplyMustScroll();
        } else if (SpacerSize < 0) {
            // We've got negative spacer size due to loading of new items @ the top
            ApplyMustScroll();
        }

        // Finally, we ensure the viewport fits into its new min-max boundaries;
        // Remember, we maybe removed & added some items + applied size changes,
        // so there is no warranty the viewport will actually be fully inside
        // the new FullRange.
        Viewport = Viewport.FitInto(FullRange);

        if (!MustScroll) {
            // 3. We aren't scrolling, but maybe we still want to adjust the spacer...
            var mustAdjustSpacerSize = Math.Abs(SpacerSize - PerfectSpacerSize) > VirtualList.SpacerSize / 2.0;
            if (!mustAdjustSpacerSize)
                return;

            var isSafeToScroll = ClientSideState?.IsSafeToScroll ?? false;
            if (isSafeToScroll)
                ApplyMustScroll();
            else
                NotifyWhenSafeToScroll = true;
        }
    }

    protected void ApplyMustScroll()
    {
        MustScroll = true;
        SpacerSize = PerfectSpacerSize;
    }

    protected (ItemRenderPlan? Item, ItemRenderPlan? OldItem) FindMatchingDisplayedItem(
        VirtualListRenderPlan<TItem>? lastPlan)
    {
        if (lastPlan == null)
            return (null, null);
        foreach (var item in DisplayedItems) {
            var prevItem = lastPlan.ItemByKey.GetValueOrDefault(item.Key);
            if (prevItem != null && prevItem.IsMeasured)
                return (item, prevItem);
        }
        return (null, null);
    }
}
