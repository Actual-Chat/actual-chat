// using Stl.Reflection;
//
// namespace ActualChat.UI.Blazor.Components.Internal;
//
// public class VirtualListRenderPlan<TItem>
//     where TItem : IVirtualListItem
// {
//     public long RenderIndex { get; set; }
//
//     /// <summary> Relative to the top item's top! </summary>
//     public Range<double>? Viewport { get; set; }
//
//     public Range<double>? ItemRange { get; set; }
//     public Range<double>? FullRange => ItemRange is { } r ? (-SpacerSize, r.End + EndSpacerSize) : null;
//     public Range<double>? TrimmedLoadZoneRange => Viewport is { } v ? GetTrimmedLoadZoneRange(v) : null;
//     public bool HasUnmeasuredItems => !ItemRange.HasValue;
//     public bool UseSmoothScroll { get; set; }
//
//     // JsonIgnores are here solely to make JsonFormatter.Format work
//     [JsonIgnore] public VirtualList<TItem> VirtualList { get; set; }
//
//     [JsonIgnore] public VirtualListClientSideState? ClientSideState { get; set; }
//
//     [JsonIgnore] public VirtualListData<TItem> Data { get; set; }
//
//     [JsonIgnore] public Dictionary<Symbol, ItemRenderPlan> ItemByKey { get; set; } = null!;
//
//     [JsonIgnore] public List<ItemRenderPlan> Items { get; set; } = null!;
//
//     [JsonIgnore]
//     public IEnumerable<ItemRenderPlan> ReversedItems {
//         get {
//             for (var i = Items.Count - 1; i >= 0; i--)
//                 yield return Items[i];
//         }
//     }
//
//     [JsonIgnore] public bool IsDataChanged { get; set; }
//
//     [JsonIgnore] public VirtualListEdge AlignmentEdge => VirtualList.AlignmentEdge;
//
//     [JsonIgnore] public double SpacerSize => Data.HasVeryFirstItem ? 0 : VirtualList.SpacerSize;
//
//     [JsonIgnore] public double EndSpacerSize => Data.HasVeryLastItem ? 0 : VirtualList.SpacerSize;
//
//     [JsonIgnore] public VirtualListStickyEdgeState? StickyEdge => ClientSideState?.StickyEdge;
//
//     // protected IVirtualListStatistics Statistics => VirtualList.Statistics;
//     protected ILogger Log => VirtualList.Log;
//     protected ILogger? DebugLog => DebugMode ? Log : null;
//     protected bool DebugMode => VirtualList.DebugMode;
//
//     public VirtualListRenderPlan(VirtualList<TItem> virtualList)
//     {
//         VirtualList = virtualList;
//         RenderIndex = 1;
//         Data = VirtualListData<TItem>.None;
//         IsDataChanged = true;
//         Update(null);
//     }
//
//     public virtual VirtualListRenderPlan<TItem> Next()
//     {
//         try {
//             var plan = (VirtualListRenderPlan<TItem>)MemberwiseClone();
//
//             plan.RenderIndex++;
//             plan.Data = VirtualList.Data;
//             plan.ClientSideState = VirtualList.ClientSideState;
//             plan.Update(this);
//             return plan;
//         }
//         catch (Exception e) {
//             Log.LogError(e, "Error while computing the next render plan");
//             throw;
//         }
//     }
//
//     // Misc. helpers
//     public Range<double> GetTrimmedLoadZoneRange(Range<double> viewport)
//         => new (
//             viewport.Start - (Data.HasVeryFirstItem ? 0 : VirtualList.LoadZoneSize),
//             viewport.End + (Data.HasVeryLastItem ? 0 : VirtualList.LoadZoneSize));
//
//     public bool? IsFullyLoaded()
//     {
//         if (ItemRange is not { } itemRange || TrimmedLoadZoneRange is not { } loadZoneRange)
//             return null;
//
//         return Data.HasAllItems || itemRange.Contains(loadZoneRange);
//     }
//
//     // Protected & private methods
//
//     private void Update(VirtualListRenderPlan<TItem>? lastPlan)
//     {
//         var statistics = Statistics;
//         var newItemSizes = ClientSideState?.ItemSizes;
//         var prevItemByKey = lastPlan?.ItemByKey;
//
//         IsDataChanged = lastPlan?.Data != Data;
//         ItemByKey = new Dictionary<Symbol, ItemRenderPlan>();
//         Items = new List<ItemRenderPlan>();
//         var hasUnmeasuredItems = false;
//         var itemRange = default(Range<double>);
//         foreach (var item in Data.Items) {
//             var newItem = new ItemRenderPlan(item);
//             if (newItemSizes != null && newItemSizes.TryGetValue(item.Key, out var newSize)) {
//                 statistics.AddItem(newSize, item.CountAs);
//                 newItem.Range = new Range<double>(0, newSize);
//             }
//             else if (prevItemByKey != null && prevItemByKey.TryGetValue(item.Key, out var oldItem))
//                 newItem.Range = oldItem.Range; // Copying old item size
//
//             Items.Add(newItem);
//             ItemByKey.Add(item.Key, newItem);
//             if (newItem.IsMeasured) {
//                 itemRange = new Range<double>(itemRange.End, itemRange.End + newItem.Size);
//                 newItem.Range = itemRange;
//             }
//             else
//                 hasUnmeasuredItems = true;
//         }
//         ItemRange = hasUnmeasuredItems ? null : new Range<double>(0, itemRange.End);
//         UpdateViewport();
//         UpdateClientSideState();
//     }
//
//     private void UpdateViewport()
//     {
//         if (!TryGetClientSideViewport(ClientSideState, out var clientSideViewport)) {
//             DebugLog?.LogDebug("Viewport: ClientSideState doesn't contain viewport info");
//             if (!Viewport.HasValue) {
//                 DebugLog?.LogDebug("Viewport: null (no saved viewport)");
//                 return;
//             }
//             if (HasUnmeasuredItems) {
//                 DebugLog?.LogDebug("Viewport: null (HasItemSizeChanges | HasUnmeasuredItems)");
//                 Viewport = null;
//                 return;
//             }
//             DebugLog?.LogDebug("Viewport: {Viewport} (saved viewport)", Viewport);
//         }
//         else {
//             DebugLog?.LogDebug("Viewport: ClientSideState viewport: {ClientSideStateViewport}", clientSideViewport);
//             if (FullRange is not { } fullRange) {
//                 DebugLog?.LogDebug("Viewport: null (HasUnmeasuredItems)");
//                 Viewport = null;
//                 return;
//             }
//             Viewport = clientSideViewport.ScrollInto(fullRange, AlignmentEdge.IsEnd());
//             DebugLog?.LogDebug("Viewport: {Viewport} (new viewport)", Viewport);
//         }
//     }
//
//     private void UpdateClientSideState()
//     {
//         if (ClientSideState == null)
//             return;
//
//         var newClientSideState = MemberwiseCloner.Invoke(ClientSideState);
//         newClientSideState.SpacerSize = SpacerSize;
//         newClientSideState.EndSpacerSize = EndSpacerSize;
//         newClientSideState.ScrollHeight = FullRange.HasValue && Viewport.HasValue
//             ? Math.Max(FullRange.Value.Size(), Viewport.Value.Size())
//             : null;
//         newClientSideState.ScrollTop = Viewport?.Start;
//         newClientSideState.ViewportHeight = Viewport?.Size();
//         ClientSideState = newClientSideState;
//     }
//
//     private bool TryGetClientSideViewport(VirtualListClientSideState? clientSideState, out Range<double> viewport)
//     {
//         viewport = default;
//         if (clientSideState is not { ViewportHeight: { } viewportHeight, ScrollTop: { } scrollTop })
//             return false;
//
//         viewport = (scrollTop, scrollTop + viewportHeight);
//         return true;
//     }
//
//     public sealed class ItemRenderPlan
//     {
//         public TItem Item { get; }
//         public Symbol Key => Item.Key;
//         public Range<double> Range { get; set; } = new (-1, -2);
//         public double Size => Range.Size();
//         public bool IsMeasured => Size >= 0;
//
//         public ItemRenderPlan(TItem item) => Item = item;
//     }
// }
