using ActualChat.UI.Blazor.Controls.Internal;
using ActualChat.UI.Blazor.Module;
using Microsoft.AspNetCore.Components;
using Stl.Fusion.Blazor;

namespace ActualChat.UI.Blazor.Controls;

public partial class VirtualList<TItem> : ComputedStateComponent<VirtualListData<TItem>>, IVirtualListBackend
    where TItem : IVirtualListItem
{
    [Inject]
    protected IJSRuntime JS { get; init; } = null!;
    [Inject]
    protected AppBlazorCircuitContext CircuitContext { get; init; } = null!;
    [Inject]
    protected internal ILogger<VirtualList<TItem>> Log { get; init; } = null!;
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected internal bool DebugMode => Constants.DebugMode.VirtualList;

    protected ElementReference Ref { get; set; }
    protected IJSObjectReference JSRef { get; set; } = null!;
    protected DotNetObjectReference<IVirtualListBackend> BlazorRef { get; set; } = null!;

    // ReSharper disable once StaticMemberInGenericType
    protected VirtualListRenderPlan<TItem>? LastPlan { get; set; } = null!;
    protected VirtualListRenderPlan<TItem> Plan { get; set; } = null!;
    protected VirtualListDataQuery? LastQuery { get; set; }
    protected VirtualListDataQuery Query { get; set; } = new(default);
    protected internal VirtualListClientSideState ClientSideState { get; set; } = null!;
    protected internal IVirtualListStatistics Statistics { get; set; } = new VirtualListStatistics();
    protected internal virtual VirtualListData<TItem> Data => State.LatestNonErrorValue ?? new();

    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string Style { get; set; } = "";
    [Parameter, EditorRequired] public RenderFragment<TItem> Item { get; set; } = null!;
    [Parameter] public RenderFragment<int> Skeleton { get; set; } = null!;
    [Parameter] public int SkeletonCount { get; set; } = 32;
    [Parameter] public double SpacerSize { get; set; } = 10000;
    [Parameter] public double LoadZoneSize { get; set; } = 2160;
    [Parameter] public double BufferZoneSize { get; set; } = 4320;
    [Parameter] public long MaxExpectedExpansion { get; set; } = 1_000_000;
    [Parameter] public VirtualListEdge PreferredTrackingEdge { get; set; } = VirtualListEdge.End;
    [Parameter] public VirtualListEdge? InitialTrackingEdge { get; set; }

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
        if (LastPlan == null) {
            DebugLog?.LogDebug(nameof(ShouldRender) + ": true (no LastPlan)");
            return true;
        }
        var isSameState = ReferenceEquals(LastPlan.Data, Data) && ReferenceEquals(LastPlan.ClientSideState, ClientSideState);
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
                Ref, BlazorRef, plan!.IsEndAligned, DebugMode
                ).ConfigureAwait(true);
        }
    }

    [JSInvokable]
    public Task<long> UpdateClientSideState(VirtualListClientSideState clientSideState)
    {
        var plan = LastPlan;
        var expectedRenderIndex = plan?.RenderIndex ?? 0;
        if (clientSideState.RenderIndex != expectedRenderIndex) {
            DebugLog?.LogDebug(
                "UpdateClientSideState: outdated RenderIndex = {RenderIndex} != {ExpectedRenderIndex}",
                clientSideState.RenderIndex, expectedRenderIndex);
            return Task.FromResult(expectedRenderIndex);
        }

        DebugLog?.LogDebug("UpdateClientSideState: RenderIndex = {RenderIndex}", clientSideState.RenderIndex);
        ClientSideState = clientSideState;
        _ = this.StateHasChangedAsync();
        return Task.FromResult(expectedRenderIndex);
    }

    protected void UpdateData()
    {
        if (!State.Computed.IsConsistent()) // Already recomputing
            return;
        Query = GetDataQuery(Plan);
        if (LastQuery != Query)
            _ = State.Recompute();
    }

    protected override ComputedState<VirtualListData<TItem>>.Options GetStateOptions()
        => new() { UpdateDelayer = UpdateDelayer.MinDelay };

    protected override async Task<VirtualListData<TItem>> ComputeState(CancellationToken cancellationToken)
    {
        var query = Query;
        LastQuery = query;
        VirtualListData<TItem> response;
        try {
            response = await DataSource.Invoke(query, cancellationToken).ConfigureAwait(true);
            DebugLog?.LogDebug(
                nameof(ComputeState) + ": query={Query} -> keys [{Key0}...{KeyE}], has {First} {Last}",
                query,
                response.Items.FirstOrDefault()?.Key,
                response.Items.LastOrDefault()?.Key,
                response.HasVeryFirstItem ? "first " : "",
                response.HasVeryLastItem ? "last" : "");
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
        if (!plan.Viewport.HasValue)
            return LastQuery!;

        var viewport = plan.Viewport.GetValueOrDefault();
        var loaderZone = new Range<double>(viewport.Start - LoadZoneSize, viewport.End + LoadZoneSize);
        var bufferZone = new Range<double>(viewport.Start - BufferZoneSize, viewport.End + BufferZoneSize);
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
            DebugLog?.LogWarning(nameof(GetDataQuery) + ": reset");
            // No items inside the bufferZone, so we'll take the first or the last item
            startIndex = endIndex = displayedItems[0].Range.End < bufferZone.Start ? 0 : displayedItems.Count - 1;
        }

        var itemSize = Statistics.ItemSize;
        var responseFulfillmentRatio = Statistics.ResponseFulfillmentRatio;

        var firstItem = displayedItems[startIndex];
        var lastItem = displayedItems[endIndex];
        DebugLog?.LogDebug(nameof(GetDataQuery) + ": bufferZone fits {FirstItemId} ... {LastItemId} keys",
            firstItem.Key, lastItem.Key);
        var startGap = Math.Max(0, firstItem.Range.Start - loaderZone.Start);
        var endGap = Math.Max(0, loaderZone.End - lastItem.Range.End);
        DebugLog?.LogDebug(nameof(GetDataQuery) + ": startGap={StartGap}, endGap={EndGap}", startGap, endGap);

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
            nameof(GetDataQuery) + ": itemSize={ItemSize}, responseFulfillmentRatio={ResponseFulfillmentRatio}, query={Query}",
            itemSize, responseFulfillmentRatio, query);
        return query;
    }
}
