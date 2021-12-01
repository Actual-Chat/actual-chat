using ActualChat.Mathematics;
using ActualChat.UI.Blazor.Components.Internal;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Stl.Fusion.Blazor;
using Stl.Reflection;

namespace ActualChat.UI.Blazor.Components;

public partial class VirtualList<TItem> : ComputedStateComponent<VirtualListData<TItem>>, IVirtualListBackend
{
    [Inject]
    protected IJSRuntime JS { get; init; } = null!;
    [Inject]
    protected AppBlazorCircuitContext CircuitContext { get; init; } = null!;
    [Inject]
    protected internal ILogger<VirtualList<TItem>> Log { get; init; } = null!;
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected internal bool DebugMode { get; } = Constants.DebugMode.VirtualList;

    protected ElementReference Ref { get; set; }
    protected IJSObjectReference JSRef { get; set; } = null!;
    protected DotNetObjectReference<IVirtualListBackend> BlazorRef { get; set; } = null!;

    // ReSharper disable once StaticMemberInGenericType
    protected internal long NextRenderIndex { get; set; }
    protected long LastAfterRenderRenderIndex { get; set; } = -1;
    protected VirtualListRenderPlan<TItem>? LastPlan { get; set; } = null!;
    protected VirtualListRenderPlan<TItem> Plan { get; set; } = null!;
    protected VirtualListDataQuery? LastQuery { get; set; }
    protected VirtualListDataQuery Query { get; set; } = new(default);
    protected internal VirtualListClientSideState ClientSideState { get; set; } = null!;
    protected internal IVirtualListStatistics Statistics { get; set; } = new VirtualListStatistics();
    protected internal virtual VirtualListData<TItem> Data => State.LatestNonErrorValue ?? new();

    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string Style { get; set; } = "";
    [Parameter, EditorRequired] public RenderFragment<KeyValuePair<string, TItem>> Item { get; set; } = null!;
    [Parameter] public RenderFragment Skeleton { get; set; } = _ => { };
    [Parameter] public int SkeletonCount { get; set; } = 100;
    [Parameter] public double SpacerSize { get; set; } = 8640;
    [Parameter] public double LoadZoneSize { get; set; } = 1080;
    [Parameter] public double BufferZoneSize { get; set; } = 2160;
    [Parameter] public long MaxExpectedExpansion { get; set; } = 1_000_000;
    [Parameter] public VirtualListEdge PreferredTrackingEdge { get; set; } = VirtualListEdge.End;

    [Parameter, EditorRequired]
    public Func<VirtualListDataQuery, CancellationToken, Task<VirtualListData<TItem>>> DataSource { get; set; } =
        (_, _) => Task.FromResult(new VirtualListData<TItem>());
    [Parameter]
    public IComparer<string> KeyComparer { get; set; } = StringComparer.InvariantCulture;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Plan = new(this);
    }

    public override async ValueTask DisposeAsync()
    {
        await JSRef.DisposeSilentlyAsync("dispose").ConfigureAwait(true);
        BlazorRef?.Dispose();
        await base.DisposeAsync().ConfigureAwait(true);
        GC.SuppressFinalize(this);
    }

    protected override bool ShouldRender()
    {
        var isPlanActual = ReferenceEquals(Plan.Data, Data) && ReferenceEquals(Plan.ClientSideState, ClientSideState);
        if (!isPlanActual)
            Plan = Plan.Next();
        return !ReferenceEquals(Plan, LastPlan);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) {
            BlazorRef = DotNetObjectReference.Create<IVirtualListBackend>(this);
            JSRef = await JS.InvokeAsync<IJSObjectReference>(
                $"{BlazorUICoreModule.ImportName}.VirtualList.create",
                Ref, BlazorRef, DebugMode
                ).ConfigureAwait(true);
        }
        var plan = LastPlan;
        if (CircuitContext.IsPrerendering || JSRef == null! || plan == null)
            return;
        if (plan.RenderIndex == LastAfterRenderRenderIndex)
            return; // Nothing new is rendered

        LastAfterRenderRenderIndex = plan.RenderIndex;
        var renderState = new VirtualListRenderState() {
            RenderIndex = plan.RenderIndex,

            SpacerSize = plan.SpacerSize,
            EndSpacerSize = plan.EndSpacerSize,
            ScrollHeight = plan.FullRange.Size(),
            ItemSizes = plan.DisplayedItems.ToDictionary(i => i.Key, i => i.Range.Size(), StringComparer.Ordinal),

            ScrollTop = plan.Viewport.Start + Plan.SpacerSize,
            ClientHeight = plan.Viewport.Size(),

            MustMeasure = plan.UnmeasuredItems.Count != 0,
            MustScroll = plan.MustScroll,
            NotifyWhenSafeToScroll = plan.NotifyWhenSafeToScroll,
        };
        if (!plan.IsFullyLoaded(plan.GetLoadZoneRange()))
            UpdateData();
        DebugLog?.LogDebug("OnAfterRender: #{RenderIndex}", renderState.RenderIndex);
        ClientSideState = plan.ClientSideState ?? ClientSideState;
        await JSRef.InvokeVoidAsync("afterRender", renderState).ConfigureAwait(true);
    }

    [JSInvokable]
    public Task<long> UpdateClientSideState(VirtualListClientSideState clientSideState)
    {
        var lastPlan = LastPlan;
        if (lastPlan == null! || clientSideState.RenderIndex != lastPlan.RenderIndex) {
            DebugLog?.LogDebug(
                "UpdateClientSideState: outdated RenderIndex = {RenderIndex} < {ExpectedRenderIndex}",
                clientSideState.RenderIndex, lastPlan?.RenderIndex);
            return Task.FromResult(lastPlan?.RenderIndex ?? -1);
        }

        // await Task.Delay(1000); // Debug only!
        DebugLog?.LogDebug("UpdateClientSideState: RenderIndex = {RenderIndex}", clientSideState.RenderIndex);
        ClientSideState = clientSideState;
        _ = this.StateHasChangedAsync();
        return Task.FromResult(lastPlan.RenderIndex);
    }

    protected void UpdateData()
    {
        if (!State.Computed.IsConsistent()) // Already recomputing
            return;
        Query = GetDataQuery(Plan);
        if (LastQuery != Query)
            _ = State.Recompute();
    }

    protected override async Task<VirtualListData<TItem>> ComputeState(CancellationToken cancellationToken)
    {
        var query = Query;
        LastQuery = query;
        VirtualListData<TItem> response;
        try {
            response = await DataSource.Invoke(query, cancellationToken).ConfigureAwait(true);
            DebugLog?.LogDebug("ComputeState: {Query} -> keys [{Key0}...{KeyE}] w/ {Range} item(s)",
                query,
                response.Items.FirstOrDefault().Key,
                response.Items.LastOrDefault().Key,
                response.HasAllItems ? "all" : response.HasVeryFirstItem ? "start" : "end");
        }
        catch (Exception e) {
            Log.LogError(e, "DataSource.Invoke(query) failed on query = {Query}", query);
            throw;
        }

        // Adding statistics
        var startExpansion = response.Items
            .TakeWhile(i => KeyComparer.Compare(i.Key, query.InclusiveRange.Start) < 0)
            .Count();
        var oldItemCount = response.Items
            .Skip(startExpansion)
            .TakeWhile(i => KeyComparer.Compare(i.Key, query.InclusiveRange.End) <= 0)
            .Count();
        var endExpansion = response.Items.Count - startExpansion - oldItemCount;
        if (query.ExpectedStartExpansion > 0 && !response.HasVeryFirstItem)
            Statistics.AddResponse(startExpansion, query.ExpectedStartExpansion);
        if (query.ExpectedEndExpansion > 0 && !response.HasVeryLastItem)
            Statistics.AddResponse(endExpansion, query.ExpectedEndExpansion);
        return response;
    }

    protected virtual VirtualListDataQuery GetDataQuery(VirtualListRenderPlan<TItem> plan)
    {
        if (plan.UnmeasuredItems.Count != 0) // Let's wait for measurement to complete first
            return LastQuery!;
        if (plan.DisplayedItems.Count == 0) // No entries -> nothing to "align" the query to
            return LastQuery!;

        var loaderZone = new Range<double>(plan.Viewport.Start - LoadZoneSize, plan.Viewport.End + LoadZoneSize);
        var bufferZone = new Range<double>(plan.Viewport.Start - BufferZoneSize, plan.Viewport.End + BufferZoneSize);
        var displayedItems = plan.DisplayedItems;
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
            DebugLog?.LogWarning("GetDataQuery: reset");
            // No items inside the bufferZone, so we'll take the first or the last item
            startIndex = endIndex = displayedItems[0].Range.End < bufferZone.Start ? 0 : displayedItems.Count - 1;
        }

        var itemSize = Statistics.ItemSize;
        var responseFulfillmentRatio = Statistics.ResponseFulfillmentRatio;

        var firstItem = displayedItems[startIndex];
        var lastItem = displayedItems[endIndex];
        DebugLog?.LogDebug("GetDataQuery: bufferZone fits {FirstItemId} ... {LastItemId} keys",
            firstItem.Key, lastItem.Key);
        var startGap = Math.Max(0, firstItem.Range.Start - loaderZone.Start);
        var endGap = Math.Max(0, loaderZone.End - lastItem.Range.End);
        DebugLog?.LogDebug("GetDataQuery: startGap: {StartGap}, endGap: {EndGap}", startGap, endGap);

        var expectedStartExpansion = Math.Clamp((long) Math.Ceiling(startGap / itemSize), 0, MaxExpectedExpansion);
        var expectedEndExpansion = Math.Clamp((long) Math.Ceiling(endGap / itemSize), 0, MaxExpectedExpansion);
        if (plan.TrackingEdge == VirtualListEdge.Start)
            expectedStartExpansion = MaxExpectedExpansion;
        else if (plan.TrackingEdge == VirtualListEdge.End)
            expectedEndExpansion = MaxExpectedExpansion;
        var keyRange = new Range<string>(firstItem.Key, lastItem.Key);
        var query = new VirtualListDataQuery(keyRange) {
            ExpectedStartExpansion = expectedStartExpansion,
            ExpectedEndExpansion = expectedEndExpansion,
            ExpandStartBy = expectedStartExpansion / responseFulfillmentRatio,
            ExpandEndBy = expectedEndExpansion / responseFulfillmentRatio,
        };

        DebugLog?.LogDebug(
            "GetDataQuery: itemSize: {ItemSize}, responseFulfillmentRatio = {RFR}, query = {Query}",
            itemSize, responseFulfillmentRatio, query);
        return query;
    }
}
