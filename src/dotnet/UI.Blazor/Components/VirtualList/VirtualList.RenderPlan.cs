using System.Text.Json.Serialization;
using ActualChat.Mathematics;

namespace ActualChat.UI.Blazor.Components;

public partial class VirtualList<TItem>
{
    public sealed class ItemRenderPlan
    {
        public string Key { get; init; } = null!;
        public TItem Item { get; init; } = default!;
        public Range<double> Range { get; set; } = new(-1, -2);
        public double Size => Range.Size();
        public bool IsMeasured => Size >= 0;
    }

    public class RenderPlan
    {
        // JsonIgnores are here solely to make JsonFormatter.Format work
        [JsonIgnore]
        public VirtualList<TItem> VirtualList { get; set; } = null!;
        public long RenderIndex { get; set; } = 0;
        public VirtualListClientSideState? ClientSideState { get; set; }
        [JsonIgnore]
        public VirtualListClientSideState? NextClientSideState { get; set; }
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

        /// <summary> Prefix spacer size. </summary>
        public double SpacerSize { get; set; }

        public bool IsUserScrollDetected { get; set; }
        public bool IsStartAligned { get; set; }
        public bool IsEndAligned { get; set; }

        /// <summary> Indicates whether JS backend must notify Blazor part when it's safe to scroll. </summary>
        public bool NotifyWhenSafeToScroll { get; set; }

        /// <summary> Forces programmatic scroll to the <see cref="Viewport"/>. </summary>
        public bool MustScroll { get; set; }

        // Pre-computed properties
        public Range<double> DisplayedRange
            => DisplayedItems.Count > 0
                ? (DisplayedItems[0].Range.Start, DisplayedItems[^1].Range.End)
                : default;

        protected ILogger Log => VirtualList.Log;
        protected IVirtualListStatistics Statistics => VirtualList.Statistics;

        public RenderPlan() { }
        public RenderPlan(VirtualList<TItem> virtualList)
        {
            VirtualList = virtualList;
            RenderIndex = 0;
            Data = VirtualList.Data;

            SpacerSize = VirtualList.SpacerSize;
            Update(null);
            Viewport = DisplayedRange;
        }

        public virtual RenderPlan Next()
        {
            var plan = (RenderPlan) MemberwiseClone();
            plan.RenderIndex = RenderIndex + 1;
            plan.Data = VirtualList.Data;
            plan.NextClientSideState = null;
            plan.ClientSideState = NextClientSideState;

            plan.IsUserScrollDetected = false;
            plan.NotifyWhenSafeToScroll = false;
            plan.MustScroll = false;
            plan.Update(this);
            return plan;
        }

        public bool IsFullyLoaded(Range<double> viewport)
            => Data.HasAllItems || DisplayedRange.Contains(viewport);

        public void Update(RenderPlan? lastPlan)
        {
            var statistics = Statistics;
            var newItemSizes = ClientSideState?.ItemSizes;
            var prevItemByKey = lastPlan?.ItemByKey;

            LoadedItems = new List<ItemRenderPlan>();
            ItemByKey = new Dictionary<string, ItemRenderPlan>(StringComparer.Ordinal);
            DisplayedItems = new List<ItemRenderPlan>();
            UnmeasuredItems = new List<ItemRenderPlan>();
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
                if (newItem.IsMeasured)
                    DisplayedItems.Add(newItem);
                else
                    UnmeasuredItems.Add(newItem);
            }

            UpdateItemRanges();
            UpdateViewportAndSpacer(lastPlan);
        }

        public void UpdateItemRanges()
        {
            // Recomputing item ranges
            foreach (var (item, range) in GetRanges(DisplayedItems))
                item.Range = range;
        }

        protected void UpdateViewportAndSpacer(RenderPlan? lastPlan)
        {
            // First, let's update the Viewport and IsUserScrollDetected
            if (ClientSideState != null) {
                // Remember that all server-side offsets are relative to the first item's top / spacer top
                var viewportStart = ClientSideState.ScrollTop - SpacerSize; // SpacerSize == lastPlan.SpacerSize here
                var viewport = new Range<double>(viewportStart, viewportStart + ClientSideState.ClientHeight);
                IsUserScrollDetected = !Viewport.Equals(viewport, 0.01);
                if (IsUserScrollDetected)
                    Viewport = viewport;
            }

            // Find a measured (visible) item that exists in both old & new plans
            var (item, oldItem) = FindMatchingDisplayedItem(lastPlan);
            if (item != null && oldItem != null) {
                // Update Viewport & SpacerSize
                var topExpansion = item.Range.Start - oldItem.Range.Start;
                // Log.LogInformation("Top expansion: {TopExpansion} [{KS} ... {KE}] of [{KS1} ... {KE1}]",
                //     topExpansion,
                //     DisplayedItems.FirstOrDefault()?.Key, DisplayedItems.LastOrDefault()?.Key,
                //     LoadedItems.FirstOrDefault()?.Key, LoadedItems.LastOrDefault()?.Key);
                Viewport = Viewport.Move(topExpansion);
                if (topExpansion <= SpacerSize)
                    SpacerSize -= topExpansion;
                else {
                    // Log.LogInformation("Scroll is needed to adjust for top expansion");
                    MustScroll = true; // This will force SpacerSize update
                }
            }
            else {
                // Full refresh, i.e. no single common item between the old and the new plan
                Viewport = new(0, Viewport.Size());
                MustScroll = true; // This will force SpacerSize update
                // Log.LogInformation("Everything is new, scrolling to the very top");
            }

            UpdateScrollRelated(lastPlan);
        }

        protected void UpdateScrollRelated(RenderPlan? lastPlan)
        {
            var displayedRange = DisplayedRange;
            var viewportSize = Viewport.Size();
            var topViewport = new Range<double>(0, viewportSize);
            var bottomViewport = new Range<double>(displayedRange.End - viewportSize, displayedRange.End);
            var idealSpacerSize = Data.HasVeryFirstItem ? 0 : VirtualList.SpacerSize;

            var justGotVeryFirstItem = Data.HasVeryFirstItem && !(lastPlan?.Data.HasVeryFirstItem ?? false);
            if (IsUserScrollDetected) {
                IsStartAligned = Data.HasVeryFirstItem && 8 >= Math.Abs(Viewport.Start);
                IsEndAligned = Data.HasVeryLastItem && 8 >= Math.Abs(DisplayedRange.End - Viewport.End);
            }

            // 1. Deciding if we must scroll no matter what
            MustScroll |= justGotVeryFirstItem || IsStartAligned || IsEndAligned;
            if (IsStartAligned) {
                Viewport = topViewport;
                if (IsEndAligned && VirtualList.PreferredAutoScrollEdge == VirtualListEdge.End)
                    Viewport = bottomViewport;
            } else if (IsEndAligned)
                Viewport = bottomViewport;

            // 2. Are we scrolling? Let's resize the spacer to its ideal size
            if (MustScroll)
                SpacerSize = idealSpacerSize;
            else {
                // 3. We aren't scrolling, but maybe we still want to adjust the spacer...
                var mustAdjustSpacerSize = Math.Abs(SpacerSize - idealSpacerSize) < VirtualList.SpacerSize / 2.0;
                var isSafeToScroll = ClientSideState?.IsSafeToScroll ?? false;
                if (mustAdjustSpacerSize && isSafeToScroll) {
                    // 3a. We can resize it right now
                    SpacerSize = idealSpacerSize;
                    MustScroll = true;
                }
                else {
                    // 3b. We can ask JS backend to notify us when it's ok to do this
                    NotifyWhenSafeToScroll = true;
                }
            }
        }

        protected (ItemRenderPlan? Item, ItemRenderPlan? OldItem) FindMatchingDisplayedItem(RenderPlan? lastPlan)
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

        protected static IEnumerable<(ItemRenderPlan Item, Range<double> Range)> GetRanges(IEnumerable<ItemRenderPlan> items)
        {
            var range = default(Range<double>);
            foreach (var item in items) {
                range = new(range.End, range.End + item.Size);
                yield return (item, range);
            }
        }
    }
}
