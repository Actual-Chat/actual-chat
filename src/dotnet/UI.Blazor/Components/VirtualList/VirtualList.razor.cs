using ActualChat.Mathematics;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor.Components;

public partial class VirtualList<TItem> : IVirtualListBackend
{
    [Inject]
    private IJSRuntime _js { get; set; } = null!;

    [Inject]
    private AppBlazorCircuitContext _appBlazorCircuitContext { get; set; } = null!;

    [Inject]
    private ILogger<VirtualList<TItem>> _logger { get; set; } = null!;

    /// <inheritdoc />
    protected async override Task OnInitializedAsync()
    {
        Context = CreateRenderContext();
        await base.OnInitializedAsync().ConfigureAwait(true);
        if (State == null) {
            _logger.LogWarning("[OnInitializedAsync] State before update is null");
        }
        await State.Update().ConfigureAwait(true);
        if (State == null) {
            _logger.LogWarning("[OnInitializedAsync] State is null");
        }
        else if (State.Computed.IsConsistent()) {
            _logger.LogWarning("[OnInitializedAsync] State is consistent. Items count: {Count}", State.Value.Items.Count);
        }
    }

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var shouldRender = base.ShouldRender();
        if (shouldRender) {
            /// the method isn't executed the first time the component is created and rendered thus we can
            /// use update <see cref="UpdateRenderContext" /> without checking is <see cref="Context"/> was initialized or not.
            Context = UpdateRenderContext();
        }
        return shouldRender;
    }

    public class ItemRenderInfo
    {
        public string Key { get; init; } = null!;
        public TItem Item { get; init; } = default!;
        public Range<double> Range { get; set; } = new(-1, -2);
        public double Size => Range.Size();
        public bool IsMeasured => Size >= 0;
    }

    public class RenderContext
    {
        public long RenderIndex { get; set; } = 0;
        public VirtualListResponse<TItem> Response { get; set; } = null!;
        public Dictionary<string, ItemRenderInfo> ItemByKey { get; set; } = null!;
        public List<ItemRenderInfo> LoadedItems { get; set; } = null!;
        public List<ItemRenderInfo> UnmeasuredItems { get; set; } = null!;
        public List<ItemRenderInfo> DisplayedItems { get; set; } = null!;
        public double SpacerSize { get; set; } // That's "prefix spacer"
        public Range<double> ViewRange { get; set; } // Relative to the top item's top
        /// <summary>
        /// Should we force scroll after render (<see cref="MustScrollAfterRender">)
        /// if <seealso cref="DisplayedItems" /> isn't empty
        /// </summary>
        public bool MustScrollWhenNonEmpty { get; set; }
        /// <summary>
        /// Forces scroll after a Blazor <seealso cref="OnAfterRenderAsync(bool)" />
        /// </summary>
        public bool MustScrollAfterRender { get; set; }

        /// <summary>
        /// We can't call scroll, if the user is scrolling, <br/>
        /// the flag tracks is it safe to call scroll by code
        /// </summary>
        public bool IsSafeToScroll { get; set; }

        // Pre-computed properties
        public Range<double> DisplayedRange
            => DisplayedItems.Count > 0
                ? (DisplayedItems[0].Range.Start, DisplayedItems[^1].Range.End)
                : default;

        // Computed properties
        public bool IsViewLoaded
            => DisplayedRange.Expand(1).Contains(ViewRange) || Response.HasVeryFirstItem && Response.HasVeryLastItem;
        public bool IsViewingTop
            => Response.HasVeryFirstItem && (DisplayedItems.Count == 0 || ViewRange.Expand(1).Overlaps(DisplayedItems[0].Range));
        public bool IsViewingBottom
            => Response.HasVeryLastItem && (DisplayedItems.Count == 0 || ViewRange.Expand(1).Overlaps(DisplayedItems[^1].Range));

        public void UpdateRanges()
        {
            foreach (var (item, range) in GetRanges(DisplayedItems))
                item.Range = range;
        }
    }

    protected IJSObjectReference JsRef { get; set; } = null!;
    protected DotNetObjectReference<IVirtualListBackend> BlazorRef { get; set; } = null!;

    /// <summary>
    /// The main state of the virtual list. Contains an information to correct render the component. <br />
    /// The js side updates this context.
    /// </summary>
    protected RenderContext Context { get; set; } = null!;
    protected VirtualListQuery? LastQuery { get; set; }
    protected VirtualListQuery NextQuery { get; set; } = new(default);
    protected IVirtualListStatistics Statistics { get; set; } = new VirtualListStatistics();

    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string Style { get; set; } = "";
    [Parameter] public RenderFragment<KeyValuePair<string, TItem>> Item { get; set; } = null!;
    [Parameter] public double SpacerSize { get; set; } = 8640;
    [Parameter] public double LoadZoneSize { get; set; } = 1080;
    [Parameter] public double BufferZoneSize { get; set; } = 2160;
    [Parameter] public double MaxExpandOnQuery { get; set; } = 1_000_000;
    [Parameter] public VirtualListStickyEdge PreferredStickyEdge { get; set; } = VirtualListStickyEdge.Bottom;
    [Parameter]
    public Func<VirtualListQuery, CancellationToken, Task<VirtualListResponse<TItem>>> Provider { get; set; } =
        (_, _) => Task.FromResult(new VirtualListResponse<TItem>());
    [Parameter] public IComparer<string> KeyComparer { get; set; } = StringComparer.InvariantCulture;

    public ElementReference Ref { get; set; }

    public override async ValueTask DisposeAsync()
    {
        if (JsRef != null)
            await JsRef.DisposeSilentAsync("dispose").ConfigureAwait(true);
        BlazorRef?.Dispose();
        await base.DisposeAsync().ConfigureAwait(true);
        GC.SuppressFinalize(this);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) {
            BlazorRef = DotNetObjectReference.Create<IVirtualListBackend>(this);
            JsRef = await _js.InvokeAsync<IJSObjectReference>($"{BlazorUICoreModule.ImportName}.VirtualList.create", Ref, BlazorRef).ConfigureAwait(true);
        }
        if (_appBlazorCircuitContext.IsPrerendering)
            return;
        var ctx = Context;
        if (ctx == null)
            return;
        if (JsRef != null) {
            var wantResizeSpacer = ctx.SpacerSize < SpacerSize / 2.0d || ctx.SpacerSize > SpacerSize * 2.0d;
            _logger.LogInformation("AfterRender: MustScroll: {MustScroll} wantResizeSpacer : {wantResizeSpacer } Spacer:{SpacerSize} | Range:{DisplayedRange}, [ {ViewRange} ]", ctx.MustScrollAfterRender, wantResizeSpacer, ctx.SpacerSize, ctx.DisplayedRange, ctx.ViewRange);
            await JsRef.InvokeVoidAsync("afterRender", ctx.MustScrollAfterRender, ctx.ViewRange.Start, wantResizeSpacer).ConfigureAwait(true);
        }
    }

    protected override async Task<VirtualListResponse<TItem>> ComputeState(CancellationToken cancellationToken)
    {
        var query = NextQuery;
        LastQuery = query;
        var response = await Provider.Invoke(query, cancellationToken).ConfigureAwait(true);

        // Adding statistics
        var startExpansion = response.Items
            .TakeWhile(i => KeyComparer.Compare(i.Key, query.IncludedRange.Start) < 0)
            .Count();
        var oldItemCount = response.Items
            .Skip(startExpansion)
            .TakeWhile(i => KeyComparer.Compare(i.Key, query.IncludedRange.End) <= 0)
            .Count();
        var endExpansion = response.Items.Count - startExpansion - oldItemCount;
        if (query.ExpectedStartExpansion > 0.001 && !response.HasVeryFirstItem)
            Statistics.AddResponse(startExpansion / query.ExpectedStartExpansion);
        if (query.ExpectedEndExpansion > 0.001 && !response.HasVeryLastItem)
            Statistics.AddResponse(endExpansion / query.ExpectedEndExpansion);
        return response;
    }

    protected void TryRecomputeState()
    {
        if (!State.Computed.IsConsistent()) // Already recomputing
            return;
        NextQuery = GetQuery();
        if (LastQuery != NextQuery)
            _ = State.Recompute();
    }

    [JSInvokable]
    public void UpdateClientSideState(IVirtualListBackend.ClientSideState clientSideState)
    {
        var ctx = Context;
        if (ctx == null) {
            _logger.LogWarning("Call UpdateClientSideState with uninitialized render context");
            return;
        }

        ctx.IsSafeToScroll = clientSideState.IsSafeToScroll;

        if (clientSideState.RenderIndex != ctx.RenderIndex) {
            _logger.LogWarning("Skipped UpdateClientSideState due to outdated RenderIndex");
            return;
        }

        var prevIsViewLoaded = ctx.IsViewLoaded;
        var isItemSizeChanged = false;
        foreach (var (key, size) in clientSideState.ItemSizes) {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (!ctx.ItemByKey.TryGetValue(key, out var item) || item.Size == size)
                continue;
            if (item.IsMeasured)
                Statistics.RemoveItem(item.Size);
            Statistics.AddItem(size);
            item.Range = item.Range.Resize(size);
            isItemSizeChanged = true;
        }

        var isViewRangeChanged = clientSideState.ViewRange != ctx.ViewRange;
        ctx.ViewRange = clientSideState.ViewRange;

        void DelayedStateHasChanged()
        {
            _ = Task.Delay(10).ContinueWith(_ => InvokeAsync(StateHasChanged), TaskScheduler.Default);
        }

        if (isItemSizeChanged) {
            ctx.UpdateRanges();
            DelayedStateHasChanged();
        }
        else if (ctx.IsSafeToScroll) {
            DelayedStateHasChanged();
        }
        else if (isViewRangeChanged || !prevIsViewLoaded || !ctx.IsViewLoaded) {
            TryRecomputeState();
        }
    }

    protected virtual VirtualListQuery GetQuery()
    {
        var ri = Context;
        if (ri == null)
            return new VirtualListQuery(default);
        if (ri.UnmeasuredItems.Count != 0) // Let's wait for measurement to complete first
            return LastQuery!;
        if (ri.DisplayedItems.Count == 0) // No entries -> nothing to "align" to
            return LastQuery!;

        var loaderZone = new Range<double>(ri.ViewRange.Start - LoadZoneSize, ri.ViewRange.End + LoadZoneSize);
        var bufferZone = new Range<double>(ri.ViewRange.Start - BufferZoneSize, ri.ViewRange.End + BufferZoneSize);
        // _log.LogInformation("GetQuery: {ViewRange} < {LoaderZone} < {BufferZone} | {DisplayedRange}", ri.ViewRange, loaderZone, bufferZone, ri.DisplayedRange);
        var displayedItems = ri.DisplayedItems;
        var startIndex = -1;
        var endIndex = -1;
        for (var i = 0; i < displayedItems.Count; i++) {
            if (displayedItems[i].Range.IntersectWith(bufferZone).Size() > 0) {
                endIndex = i;
                if (startIndex < 0)
                    startIndex = i;
            }
            else if (startIndex >= 0) {
                break;
            }
        }
        if (startIndex < 0) {
            // No items inside the bufferZone, so we'll take the first or the last item
            startIndex = endIndex = displayedItems[0].Range.End < bufferZone.Start ? 0 : displayedItems.Count - 1;
        }

        var firstItem = displayedItems[startIndex];
        var lastItem = displayedItems[endIndex];
        var keyRange = new Range<string>(firstItem.Key, lastItem.Key);
        var startGap = Math.Max(0, firstItem.Range.Start - loaderZone.Start);
        var endGap = Math.Max(0, loaderZone.End - lastItem.Range.End);
        var itemSize = Statistics.ItemSizeEstimate;
        var responseFulfillmentRatio = Statistics.ResponseFulfillmentRatio;
        var query = new VirtualListQuery(keyRange) {
            ExpandStartBy = Math.Min(MaxExpandOnQuery, Math.Round(startGap / itemSize / responseFulfillmentRatio, 1)),
            ExpandEndBy = Math.Min(MaxExpandOnQuery, Math.Round(endGap / itemSize / responseFulfillmentRatio, 1)),
            ExpectedStartExpansion = Math.Ceiling(startGap / itemSize),
            ExpectedEndExpansion = Math.Ceiling(endGap / itemSize),
        };
        if (ri.IsViewingTop) {
            query = query with {
                ExpandStartBy = MaxExpandOnQuery,
                ExpectedStartExpansion = 0,
            };
        }

        if (ri.IsViewingBottom) {
            query = query with {
                ExpandEndBy = MaxExpandOnQuery,
                ExpectedEndExpansion = 0,
            };
        }

        return query;
    }

    protected virtual RenderContext UpdateRenderContext()
    {
        var ctx = new RenderContext();
        var prev = Context;
        var response = State.LatestNonErrorValue ?? new();
        var viewSize = prev.ViewRange.Size();
        ctx.RenderIndex = prev.RenderIndex + 1;
        ctx.Response = response;
        ctx.ViewRange = prev.ViewRange;
        ctx.SpacerSize = prev.SpacerSize;
        ctx.MustScrollWhenNonEmpty = prev.MustScrollWhenNonEmpty;
        ctx.MustScrollAfterRender = prev.IsSafeToScroll;
        ctx.LoadedItems = new List<ItemRenderInfo>();
        ctx.ItemByKey = new Dictionary<string, ItemRenderInfo>(StringComparer.Ordinal);
        foreach (var (key, item) in response.Items) {
            var newItem = new ItemRenderInfo { Key = key, Item = item };
            if (prev.ItemByKey.TryGetValue(key, out var oldItem1))
                newItem.Range = oldItem1.Range;
            ctx.LoadedItems.Add(newItem);
            ctx.ItemByKey.Add(key, newItem);
        }
        ctx.UnmeasuredItems = ctx.LoadedItems.Where(e => !e.IsMeasured).ToList();
        ctx.DisplayedItems = ctx.LoadedItems.Where(e => e.IsMeasured).ToList();
        ctx.UpdateRanges();
        // Adjust the new spacer size:
        // 1. Find any item that exists in both old & new lists
        var oldItem = prev.DisplayedItems.Find(p => ctx.ItemByKey.ContainsKey(p.Key));
        if (oldItem != null!) {
            // 2. Compute its new range
            var newItem = ctx.DisplayedItems.Single(p => string.Equals(p.Key, oldItem.Key, StringComparison.Ordinal));
            // 3. Update SpacerSize & ViewRange
            var offset = newItem.Range.Start - oldItem.Range.Start;
            ctx.SpacerSize -= offset;
            ctx.ViewRange = ctx.ViewRange.Move(offset);
        }
        else {
            // Everything is new, so let's scroll to the very top
            ctx.ViewRange = new(0, viewSize);
            ctx.MustScrollWhenNonEmpty = true;
        }

        // Checking whether we should scroll to the top or to the bottom
        if (ctx.DisplayedItems.Count > 0 && (prev.IsViewingTop || prev.IsViewingBottom)) {
            var displayedRange = ctx.DisplayedRange;
            var topViewRange = new Range<double>(0, viewSize);
            var bottomViewRange = new Range<double>(displayedRange.End - viewSize, displayedRange.End);
            var newViewRange = PreferredStickyEdge == VirtualListStickyEdge.Bottom
                ? prev.IsViewingBottom ? bottomViewRange : topViewRange
                : prev.IsViewingTop ? topViewRange : bottomViewRange;
            if (Math.Abs(ctx.ViewRange.Start - newViewRange.Start) >= 0.5) {
                ctx.ViewRange = newViewRange;
                ctx.MustScrollWhenNonEmpty = true;
            }
        }
        ScrollLogic(ctx);
        return ctx;
    }

    /// <summary>
    /// At the first call of component render state might be computed, that's why it isn't just new()
    /// (because it starts earlier in <seealso cref="ComputedStateComponent{TState}.OnParametersSetAsync" />)
    /// </summary>
    protected virtual RenderContext CreateRenderContext()
    {
        var response = State.LatestNonErrorValue ?? new();
        var ctx = new RenderContext() {
            RenderIndex = 0,
            Response = response,
        };
        ctx.LoadedItems = response.Items.ConvertAll(i => new ItemRenderInfo { Key = i.Key, Item = i.Value });
        ctx.ItemByKey = ctx.LoadedItems.ToDictionary(e => e.Key, StringComparer.Ordinal);
        ctx.UnmeasuredItems = ctx.LoadedItems.Where(e => !e.IsMeasured).ToList();
        ctx.DisplayedItems = ctx.LoadedItems.Where(e => e.IsMeasured).ToList();
        ctx.UpdateRanges();
        ctx.ViewRange = ctx.DisplayedRange;
        ctx.MustScrollWhenNonEmpty = true;

        ScrollLogic(ctx);
        return ctx;
    }

    /// <summary>
    /// Fixes scroll / pager size for provided <paramref name="ctx" /> render context
    /// </summary>
    private void ScrollLogic(RenderContext ctx)
    {
        if (ctx.MustScrollWhenNonEmpty && ctx.DisplayedItems.Count != 0) {
            ctx.MustScrollAfterRender = true;
            ctx.MustScrollWhenNonEmpty = false;
        }
        if (ctx.MustScrollAfterRender)
            ctx.SpacerSize = SpacerSize;
        if (ctx.Response.HasVeryFirstItem)
            ctx.SpacerSize = 0;
    }

    private static IEnumerable<(ItemRenderInfo Item, Range<double> Range)> GetRanges(List<ItemRenderInfo> items)
    {
        var range = default(Range<double>);
        foreach (var item in items) {
            range = new(range.End, range.End + item.Size);
            yield return (item, range);
        }
    }
}