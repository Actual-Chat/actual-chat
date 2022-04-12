using ActualChat.UI.Blazor.Controls.Internal;
using ActualChat.UI.Blazor.Module;
using Microsoft.AspNetCore.Components;
using Stl.Fusion.Blazor;

namespace ActualChat.UI.Blazor.Controls;

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
    [Parameter, EditorRequired] public RenderFragment<TItem> Item { get; set; } = null!;
    [Parameter] public RenderFragment<int> Skeleton { get; set; } = null!;
    [Parameter] public int SkeletonCount { get; set; } = 32;
    [Parameter] public double SpacerSize { get; set; } = 10000;
    [Parameter] public double LoadZoneSize { get; set; } = 2160;
    [Parameter] public double BufferZoneSize { get; set; } = 4320;
    [Parameter] public long MaxPixelExpandBy { get; set; } = 1_000_000;
    [Parameter] public IMutableState<ImmutableList<string>>? VisibleKeysState { get; set; }
    [Parameter] public string? ScrollToKey { get; set; }

    [Parameter, EditorRequired]
    public Func<VirtualListDataQuery, CancellationToken, Task<VirtualListData<TItem>>> DataSource { get; set; } =
        (_, _) => Task.FromResult(VirtualListData<TItem>.None);
    [Parameter]
    public IComparer<string> KeyComparer { get; set; } = StringComparer.InvariantCulture;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Plan = new(this);
    }

    public override async ValueTask DisposeAsync()
    {
        if (JSRef != null!)
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
                Ref, BlazorRef, plan!.AlignmentEdge.IsEnd(), DebugMode
                ).ConfigureAwait(true);
            VisibleKeysState ??= StateFactory.NewMutable(ImmutableList<string>.Empty);
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
        var newVisibleKeys = clientSideState.VisibleKeys;
        if (newVisibleKeys is { Count: > 0 } && VisibleKeysState != null) {
            var oldVisibleKeys = VisibleKeysState.Value;
            if (oldVisibleKeys.IsEmpty)
                VisibleKeysState.Value = VisibleKeysState.Value.AddRange(newVisibleKeys);
            else {
                var oldSet = oldVisibleKeys.ToHashSet();
                var newSet = newVisibleKeys.ToHashSet();
                var toRemove = oldSet.Except(newSet).ToList();
                var toAdd = newSet.Except(oldSet).ToList();
                var updatedVisibleKeys = oldVisibleKeys;
                if (toRemove.Count > 0)
                    updatedVisibleKeys = updatedVisibleKeys.RemoveRange(toRemove);
                if (toAdd.Count > 0)
                    updatedVisibleKeys = updatedVisibleKeys.AddRange(toAdd);
                if (!ReferenceEquals(updatedVisibleKeys, oldVisibleKeys))
                    VisibleKeysState.Value = updatedVisibleKeys;
            }
        }
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
        if (query.PixelExpandStartBy > 0 && !response.HasVeryFirstItem)
            Statistics.AddResponse(startExpansion, query.PixelExpandStartBy);
        if (query.PixelExpandEndBy > 0 && !response.HasVeryLastItem)
            Statistics.AddResponse(endExpansion, query.PixelExpandEndBy);
        return response;
    }

    protected virtual VirtualListDataQuery GetDataQuery(VirtualListRenderPlan<TItem> plan)
    {
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
            else if (startIndex >= 0) {
                break;
            }
        }
        if (startIndex < 0) {
            DebugLog?.LogWarning(nameof(GetDataQuery) + ": reset (no items inside the buffer zone)");
            // No items inside the bufferZone, so we'll take the first or the last item
            startIndex = endIndex = items[0].Range.End < bufferZone.Start ? 0 : items.Count - 1;
        }

        var itemSize = Statistics.ItemSize;
        var responseFulfillmentRatio = Statistics.ResponseFulfillmentRatio;

        var firstItem = items[startIndex];
        var lastItem = items[endIndex];
        DebugLog?.LogDebug(nameof(GetDataQuery) + ": bufferZone fits {FirstItemId} ... {LastItemId} keys",
            firstItem.Key, lastItem.Key);
        var startGap = Math.Max(0, firstItem.Range.Start - loadZone.Start);
        var endGap = Math.Max(0, loadZone.End - lastItem.Range.End);
        DebugLog?.LogDebug(nameof(GetDataQuery) + ": startGap={StartGap}, endGap={EndGap}", startGap, endGap);

        var pixelExpandStartBy = Math.Clamp((long)Math.Ceiling(startGap / itemSize), 0, MaxPixelExpandBy);
        var pixelExpandEndBy = Math.Clamp((long)Math.Ceiling(endGap / itemSize), 0, MaxPixelExpandBy);
        var keyRange = new Range<string>(firstItem.Key, lastItem.Key);
        var query = new VirtualListDataQuery(keyRange) {
            IsExpansionQuery = true,
            PixelExpandStartBy = pixelExpandStartBy,
            PixelExpandEndBy = pixelExpandEndBy,
            ExpandStartBy = pixelExpandStartBy / responseFulfillmentRatio,
            ExpandEndBy = pixelExpandEndBy / responseFulfillmentRatio,
        };

        DebugLog?.LogDebug(
            nameof(GetDataQuery) + ": itemSize={ItemSize}, responseFulfillmentRatio={ResponseFulfillmentRatio}, query={Query}",
            itemSize, responseFulfillmentRatio, query);
        return query;
    }
}
