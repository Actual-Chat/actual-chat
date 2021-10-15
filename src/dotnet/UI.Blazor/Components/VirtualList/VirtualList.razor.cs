using ActualChat.Mathematics;
using ActualChat.UI.Blazor.Components.Internal;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Stl.Fusion.Blazor;

namespace ActualChat.UI.Blazor.Components;

public partial class VirtualList<TItem> : ComputedStateComponent<VirtualListData<TItem>>, IVirtualListBackend
{
    [Inject]
    protected IJSRuntime JS { get; init; } = null!;
    [Inject]
    protected AppBlazorCircuitContext CircuitContext { get; init; } = null!;
    [Inject]
    protected ILogger<VirtualList<TItem>> Log { get; init; } = null!;

    protected ElementReference Ref { get; set; }
    protected IJSObjectReference JSRef { get; set; } = null!;
    protected DotNetObjectReference<IVirtualListBackend> BlazorRef { get; set; } = null!;

    /// <summary>
    /// The main state of the virtual list. Contains an information to correct render the component. <br />
    /// The js side updates this context.
    /// </summary>
    protected RenderPlan? LastPlan { get; set; } = null!;
    protected RenderPlan Plan { get; set; } = null!;
    protected VirtualListDataQuery? LastQuery { get; set; }
    protected VirtualListDataQuery Query { get; set; } = new(default);
    protected IVirtualListStatistics Statistics { get; set; } = new VirtualListStatistics();
    protected virtual VirtualListData<TItem> Data => State.LatestNonErrorValue ?? new();

    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string Style { get; set; } = "";
    [Parameter] public RenderFragment<KeyValuePair<string, TItem>> Item { get; set; } = null!;
    [Parameter] public RenderFragment Skeleton { get; set; } = null!;
    [Parameter] public int SkeletonCount { get; set; } = 100;
    [Parameter] public double SpacerSize { get; set; } = 8640;
    [Parameter] public double LoadZoneSize { get; set; } = 1080;
    [Parameter] public double BufferZoneSize { get; set; } = 2160;
    [Parameter] public long MaxExpectedExpansion { get; set; } = 1_000_000;
    [Parameter] public VirtualListEdge AutoScrollEdge { get; set; } = VirtualListEdge.End;

    [Parameter]
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
        if (Data != Plan.Data)
            Plan = (LastPlan ?? Plan).Next();
        return !ReferenceEquals(Plan, LastPlan);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) {
            BlazorRef = DotNetObjectReference.Create<IVirtualListBackend>(this);
            JSRef = await JS.InvokeAsync<IJSObjectReference>($"{BlazorUICoreModule.ImportName}.VirtualList.create", Ref, BlazorRef).ConfigureAwait(true);
        }
        var plan = LastPlan;
        if (CircuitContext.IsPrerendering || JSRef == null! || plan == null)
            return;
        await JSRef.InvokeVoidAsync("afterRender",
            plan.RenderIndex,
            plan.MustScroll,
            plan.Viewport.Start + Plan.SpacerSize,
            plan.NotifyWhenSafeToScroll
            ).ConfigureAwait(true);
    }

    [JSInvokable]
    public async Task UpdateClientSideState(VirtualListClientSideState clientSideState)
    {
        var lastPlan = LastPlan;
        if (lastPlan == null! || clientSideState.RenderIndex != lastPlan.RenderIndex) {
            // Log.LogInformation("Dropping outdated update: {RenderIndex} < {ExpectedRenderIndex}",
            //     clientSideState.RenderIndex, lastPlan?.RenderIndex);
            return;
        }

        // await Task.Delay(1000); // Debug only!
        lastPlan.NextClientSideState = clientSideState;
        var hasItemSizeChanges = clientSideState.ItemSizes.Count != 0;

        // Remember that all server-side offsets are relative to the first item's top / spacer top
        var viewportStart = clientSideState.ScrollTop - lastPlan.SpacerSize;
        var viewport = new Range<double>(viewportStart, viewportStart + clientSideState.ClientHeight);
        var isUserScrollDetected = !lastPlan.Viewport.Equals(viewport, 1);

        var mustRender = hasItemSizeChanges || lastPlan.NotifyWhenSafeToScroll && clientSideState.IsSafeToScroll;
        var mustUpdateData = isUserScrollDetected || !lastPlan.IsFullyLoaded(viewport);
        if (!(mustRender || mustUpdateData)) {
            // Log.LogInformation("UpdateClientSideState: no render or update needed");
            return;
        }

        // Log.LogInformation("UpdateClientSideState: mustRender = {MustRender}, mustUpdateData = {MustUpdateData}",
        //     mustRender, mustUpdateData);
        Plan = lastPlan.Next();
        if (mustRender)
            _ = this.StateHasChangedAsync();
        if (mustUpdateData)
            UpdateData();
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
        Log.LogInformation("ComputeState: {Query}", query);
        var response = await DataSource.Invoke(query, cancellationToken).ConfigureAwait(true);

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

    protected virtual VirtualListDataQuery GetDataQuery(RenderPlan plan)
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
            // No items inside the bufferZone, so we'll take the first or the last item
            startIndex = endIndex = displayedItems[0].Range.End < bufferZone.Start ? 0 : displayedItems.Count - 1;
        }

        var itemSize = Statistics.ItemSize;
        var responseFulfillmentRatio = Statistics.ResponseFulfillmentRatio;

        var firstItem = displayedItems[startIndex];
        var lastItem = displayedItems[endIndex];
        var startGap = Math.Max(0, firstItem.Range.Start - loaderZone.Start);
        var endGap = Math.Max(0, loaderZone.End - lastItem.Range.End);

        var expectedStartExpansion = Math.Clamp((long) Math.Ceiling(startGap / itemSize), 0, MaxExpectedExpansion);
        var expectedEndExpansion = Math.Clamp((long) Math.Ceiling(endGap / itemSize), 0, MaxExpectedExpansion);
        if (plan.IsStartAligned)
            expectedStartExpansion = MaxExpectedExpansion;
        if (plan.IsEndAligned)
            expectedEndExpansion = MaxExpectedExpansion;

        var keyRange = new Range<string>(firstItem.Key, lastItem.Key);
        var query = new VirtualListDataQuery(keyRange) {
            ExpectedStartExpansion = expectedStartExpansion,
            ExpectedEndExpansion = expectedEndExpansion,
            ExpandStartBy = expectedStartExpansion / responseFulfillmentRatio,
            ExpandEndBy = expectedEndExpansion / responseFulfillmentRatio,
        };

        Log.LogInformation("GetQuery: {Query}", query);
        // Log.LogInformation("GetQuery: {Viewport} < {LoaderZone} < {BufferZone} | {DisplayedRange}",
        //     plan.Viewport, loaderZone, bufferZone, plan.DisplayedRange);
        return query;
    }
}
