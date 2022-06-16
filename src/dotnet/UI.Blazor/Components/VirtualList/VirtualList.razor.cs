using ActualChat.UI.Blazor.Components.Internal;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Components;

public delegate Task<VirtualListData<TItem>> VirtualListDataSource<TItem>(
    VirtualListDataQuery query,
    CancellationToken cancellationToken) where TItem : IVirtualListItem;

public partial class VirtualList<TItem> : ComputedStateComponent<VirtualListData<TItem>>, IVirtualListBackend
    where TItem : IVirtualListItem
{
    [Inject] protected IJSRuntime JS { get; init; } = null!;
    [Inject] protected AppBlazorCircuitContext CircuitContext { get; init; } = null!;
    [Inject] protected internal ILogger<VirtualList<TItem>> Log { get; init; } = null!;

    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected internal bool DebugMode => Constants.DebugMode.VirtualList;

    protected ElementReference Ref { get; set; }
    protected IJSObjectReference JSRef { get; set; } = null!;
    protected DotNetObjectReference<IVirtualListBackend> BlazorRef { get; set; } = null!;

    // ReSharper disable once StaticMemberInGenericType
    protected VirtualListRenderPlan<TItem>? LastPlan { get; set; } = null!;
    protected VirtualListRenderPlan<TItem> Plan { get; set; } = null!;
    protected VirtualListDataQuery LastQuery { get; set; } = VirtualListDataQuery.None;
    protected VirtualListDataQuery Query { get; set; } = VirtualListDataQuery.None;
    protected internal VirtualListClientSideState ClientSideState { get; set; } = null!;
    protected internal IVirtualListStatistics Statistics { get; set; } = new VirtualListStatistics();
    protected internal virtual VirtualListData<TItem> Data => State.LatestNonErrorValue ?? VirtualListData<TItem>.None;

    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string Style { get; set; } = "";
    [Parameter] public VirtualListEdge AlignmentEdge { get; set; } = VirtualListEdge.Start;

    [Parameter]
    [EditorRequired]
    public VirtualListDataSource<TItem> DataSource { get; set; } =
        (_, _) => Task.FromResult(VirtualListData<TItem>.None);

    [Parameter]
    [EditorRequired]
    public RenderFragment<TItem> Item { get; set; } = null!;

    [Parameter] public RenderFragment<int> Skeleton { get; set; } = null!;
    [Parameter] public int SkeletonCount { get; set; } = 32;
    [Parameter] public double SpacerSize { get; set; } = 10000;
    [Parameter] public double LoadZoneSize { get; set; } = 2160;
    [Parameter] public double BufferZoneSize { get; set; } = 4320;
    [Parameter] public long MaxExpandBy { get; set; } = 256;
    [Parameter] public IMutableState<List<string>>? VisibleKeysState { get; set; }
    [Parameter] public IComparer<string> KeyComparer { get; set; } = StringComparer.Ordinal;

    [JSInvokable]
    public Task<long> UpdateClientSideState(VirtualListClientSideState clientSideState)
    {
        var plan = LastPlan;
        var expectedRenderIndex = plan?.RenderIndex ?? 0;
        if (clientSideState.RenderIndex != expectedRenderIndex) {
            DebugLog?.LogDebug(
                "UpdateClientSideState: outdated RenderIndex = {RenderIndex} != {ExpectedRenderIndex}",
                clientSideState.RenderIndex,
                expectedRenderIndex);
            return Task.FromResult(expectedRenderIndex);
        }

        DebugLog?.LogDebug("UpdateClientSideState: RenderIndex = {RenderIndex}", clientSideState.RenderIndex);
        ClientSideState = clientSideState;
        var newVisibleKeys = clientSideState.VisibleKeys;
        if (newVisibleKeys is { Count: > 0 } && VisibleKeysState != null)
            VisibleKeysState.Value = newVisibleKeys;

        _ = this.StateHasChangedAsync();
        return Task.FromResult(expectedRenderIndex);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(true);
        await JSRef.DisposeSilentlyAsync("dispose").ConfigureAwait(true);
        JSRef = null!;
        BlazorRef.DisposeSilently();
        BlazorRef = null!;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Plan = new VirtualListRenderPlan<TItem>(this);
    }

    protected override bool ShouldRender()
    {
        if (LastPlan == null) {
            DebugLog?.LogDebug(nameof(ShouldRender) + ": true (no LastPlan)");
            return true;
        }
        var isSameState = ReferenceEquals(LastPlan.Data, Data)
            && ReferenceEquals(LastPlan.ClientSideState, ClientSideState);
        if (isSameState) {
            DebugLog?.LogDebug(nameof(ShouldRender) + ": false (same state)");
            return false;
        }
        Plan = LastPlan.Next();
        DebugLog?.LogDebug(nameof(ShouldRender) + ": true");
        return true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (CircuitContext.IsPrerendering)
            return;

        var plan = LastPlan;
        if (firstRender) {
            BlazorRef = DotNetObjectReference.Create<IVirtualListBackend>(this);
            JSRef = await JS.InvokeAsync<IJSObjectReference>(
                    $"{BlazorUICoreModule.ImportName}.VirtualList.create",
                    Ref,
                    BlazorRef,
                    plan!.AlignmentEdge.IsEnd(),
                    DebugMode
                )
                .ConfigureAwait(true);
            VisibleKeysState ??= StateFactory.NewMutable(new List<string>());
        }
    }

    protected void RequestDataUpdate()
    {
        if (!State.Computed.IsConsistent()) // Already recomputing
            return;

        if (Plan.IsFullyLoaded() == true && Data.ScrollToKey.IsNullOrEmpty())
            return;

        Query = GetDataQuery(Plan);
        if (Query.IsSimilarTo(LastQuery))
            return;

        if (!LastQuery.IsNone)
            // Data update
            _ = State.Recompute();
        LastQuery = Query;
    }

    protected override ComputedState<VirtualListData<TItem>>.Options GetStateOptions()
        => new () { UpdateDelayer = UpdateDelayer.MinDelay };

    protected override async Task<VirtualListData<TItem>> ComputeState(CancellationToken cancellationToken)
    {
        var query = Query;
        VirtualListData<TItem> response;
        try {
            response = await DataSource.Invoke(query, cancellationToken).ConfigureAwait(true);
            LastQuery = Query = response.Query;
            DebugLog?.LogDebug(
                nameof(ComputeState)
                + ": query={Query} -> keys [{Key0}...{KeyE}], has {First} {Last}, scroll to {ScrollTo}",
                query,
                response.Items.FirstOrDefault()?.Key,
                response.Items.LastOrDefault()?.Key,
                response.HasVeryFirstItem ? "first " : "",
                response.HasVeryLastItem ? "last" : "",
                response.ScrollToKey);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "DataSource.Invoke(query) failed on query = {Query}", query);
            throw;
        }

        // Updating statistics
        var startExpansion = response.Items
            .TakeWhile(i => KeyComparer.Compare(i.Key, query.InclusiveRange.Start) < 0)
            .Sum(i => i.CountAs);
        var endExpansion = response.Items
            .SkipWhile(i => KeyComparer.Compare(i.Key, query.InclusiveRange.End) <= 0)
            .Sum(i => i.CountAs);

        // we can use value for statistics as it hasn't been recomputed yet since the query creation
        var responseFulfillmentRatio = Statistics.ResponseFulfillmentRatio;
        if (query.ExpandStartBy > 0 && !response.HasVeryFirstItem)
            Statistics.AddResponse(startExpansion, (long)(query.ExpandStartBy * responseFulfillmentRatio));
        if (query.ExpandEndBy > 0 && !response.HasVeryLastItem)
            Statistics.AddResponse(endExpansion, (long)(query.ExpandEndBy * responseFulfillmentRatio));
        return response;
    }

    protected virtual VirtualListDataQuery GetDataQuery(VirtualListRenderPlan<TItem> plan)
    {
        var itemSize = Statistics.ItemSize;
        var responseFulfillmentRatio = Statistics.ResponseFulfillmentRatio;

        if (plan.ClientSideState?.ScrollAnchorKey != null) {
            // we should load data near the last available item on scroll into skeleton area
            var key = plan.ClientSideState?.ScrollAnchorKey!;
            var avgItemsPerLoadZone = LoadZoneSize / itemSize;
            var anchorKeyRange = new Range<string>(key, key);
            var anchorQuery = new VirtualListDataQuery(anchorKeyRange) {
                ExpandStartBy = avgItemsPerLoadZone / responseFulfillmentRatio,
                ExpandEndBy = avgItemsPerLoadZone / responseFulfillmentRatio,
            };
            DebugLog?.LogDebug(
                nameof(GetDataQuery)
                + ": itemSize={ItemSize}, responseFulfillmentRatio={ResponseFulfillmentRatio}, anchorQuery={Query}",
                itemSize,
                responseFulfillmentRatio,
                anchorQuery);
            return anchorQuery;
        }
        if (plan.HasUnmeasuredItems) // Let's wait for measurement to complete first
            return LastQuery;
        if (plan.Items.Count == 0) // No entries -> nothing to "align" the query to
            return LastQuery;
        if (plan.Viewport is not { } viewport)
            return LastQuery;

        var loadZone = new Range<double>(viewport.Start - LoadZoneSize, viewport.End + LoadZoneSize);
        var bufferZone = new Range<double>(viewport.Start - BufferZoneSize, viewport.End + BufferZoneSize);
        var startIndex = -1;
        var endIndex = -1;
        var items = plan.Items;
        for (var i = 0; i < items.Count; i++) {
            var item = items[i];
            if (item.IsMeasured && item.Range.IntersectWith(bufferZone).Size() > 0) {
                endIndex = i;
                if (startIndex < 0)
                    startIndex = i;
            }
            else if (startIndex >= 0)
                break;
        }
        if (startIndex < 0) {
            DebugLog?.LogWarning(nameof(GetDataQuery) + ": reset (no items inside the buffer zone)");
            // No items inside the bufferZone, so we'll take the first or the last item
            startIndex = endIndex = items[0].Range.End < bufferZone.Start ? 0 : items.Count - 1;
        }

        var firstItem = items[startIndex];
        var lastItem = items[endIndex];
        DebugLog?.LogDebug(nameof(GetDataQuery) + ": bufferZone fits {FirstItemId} ... {LastItemId} keys",
            firstItem.Key,
            lastItem.Key);
        var startGap = Math.Max(0, firstItem.Range.Start - loadZone.Start);
        var endGap = Math.Max(0, loadZone.End - lastItem.Range.End);
        DebugLog?.LogDebug(nameof(GetDataQuery) + ": startGap={StartGap}, endGap={EndGap}", startGap, endGap);

        var expandStartBy = plan.Data.HasVeryFirstItem
            ? 0
            : Math.Clamp((long)Math.Ceiling(startGap / itemSize), 0, MaxExpandBy);
        var expandEndBy = plan.Data.HasVeryLastItem
            ? 0
            : Math.Clamp((long)Math.Ceiling(endGap / itemSize), 0, MaxExpandBy);
        var keyRange = new Range<string>(firstItem.Key, lastItem.Key);
        var query = new VirtualListDataQuery(keyRange) {
            ExpandStartBy = expandStartBy / responseFulfillmentRatio,
            ExpandEndBy = expandEndBy / responseFulfillmentRatio,
        };

        DebugLog?.LogDebug(
            nameof(GetDataQuery)
            + ": itemSize={ItemSize}, responseFulfillmentRatio={ResponseFulfillmentRatio}, query={Query}",
            itemSize,
            responseFulfillmentRatio,
            query);
        return query;
    }
}
