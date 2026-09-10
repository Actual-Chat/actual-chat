using ActualChat.UI.Blazor.Components.Internal;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Services;
using ActualLab.Fusion.Internal;

namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// Shared plumbing for the two virtualized lists — <see cref="FiniteList{TItem}"/> and
/// <see cref="InfiniteList{TItem}"/>: the data-source round trip, the JS bridge and item
/// visibility. All geometry belongs to the derived component.
/// </summary>
public abstract class VirtualList<TItem> : ComputedStateComponent<UIHub, VirtualListData<TItem>>, IVirtualListBackend
    where TItem : class, IVirtualListItem
{
    private VirtualListDataQuery _pendingQuery = VirtualListDataQuery.None;

    private ILogger Log => field ??= Hub.LogFor(GetType());

    protected ElementReference Ref { get; set; }
    protected IJSObjectReference JSRef { get; set; } = null!;
    protected DotNetObjectReference<IVirtualListBackend> BlazorRef { get; set; } = null!;

    // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
    protected VirtualListData<TItem> Data => State?.LastNonErrorValue ?? VirtualListData<TItem>.None;
    protected VirtualListData<TItem> RenderedData { get; set; } = VirtualListData<TItem>.None;

    protected VirtualListItemVisibility LastReportedItemVisibility { get; set; } = VirtualListItemVisibility.Empty;

    protected int RenderIndex { get; set; }

    [Parameter] public string Identity { get; set; } = "";
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string Style { get; set; } = "";

    [Parameter, EditorRequired]
    public IVirtualListDataSource<TItem> DataSource { get; set; } = VirtualListDataSource<TItem>.Empty;
    [Parameter] // NOTE(AY): Putting EditorRequired here triggers a warning in Rider (likely their issue)
    public RenderFragment<TItem> Item { get; set; } = null!;
    [Parameter] public RenderFragment<int>? Skeleton { get; set; }
    [Parameter] public RenderFragment<int>? SkeletonBatch { get; set; }
    [Parameter] public int SkeletonCount { get; set; } = 10;
    // Opt-in: most lists sit in no swap area at all. Set it where the enclosing area should hold
    // until this list has its content placed - see ContentSwap and virtual-list.ts.
    [Parameter] public bool IsContentSwapDependency { get; set; }
    // Presence names - see presence-tracker.ts. Child is what the list root declares itself as;
    // GroupChildren / ItemChildren are what the group / item elements count. Passed in rather than
    // hardcoded so this component keeps no vocabulary from the app that uses it.
    [Parameter] public string Child { get; set; } = "";
    [Parameter] public string GroupChildren { get; set; } = "";
    [Parameter] public string ItemChildren { get; set; } = "";
    [Parameter] public double ExpandMultiplier { get; set; } = 2;
    // This event is intentionally Action vs EventCallback, coz normally it shouldn't
    // trigger StateHasChanged on parent component.
    [Parameter] public Action<VirtualListItemVisibility>? ItemVisibilityChanged { get; set; }
    [CascadingParameter] public ScreenSize ScreenSize { get; set; }
    [CascadingParameter] private ContentSwapContext? ContentSwapContext { get; set; }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await JSRef.DisposeSilentlyAsync("dispose");
        JSRef = null!;
        BlazorRef.DisposeSilently();
        BlazorRef = null!;
        RenderIndex = 0;
        RenderedData = VirtualListData<TItem>.None;
        // A list that goes away has to retract what it last reported, because nothing else will: a place
        // or filter switching to an empty result destroys this component rather than rendering it with no
        // rows, so the JS side is gone before it could say so, and the consumer would go on acting on keys
        // for rows that are gone. Retracting last also covers a report that landed while the awaits above
        // ran - UpdateItemVisibility takes them until JSRef is null. And it keeps the identity, because
        // consumers route by it: ChatView drops anything that isn't its own chat id.
        if (LastReportedItemVisibility.VisibleKeys.Count == 0)
            return;

        var retraction = LastReportedItemVisibility with { VisibleKeys = ImmutableHashSet<string>.Empty };
        LastReportedItemVisibility = retraction;
        ItemVisibilityChanged?.Invoke(retraction);
    }

    [JSInvokable]
    public async Task RequestData(VirtualListDataQuery query)
    {
        if (ContentSwapContext?.IsLayerActive == false)
            return;

        ChatSwitchTracer.Mark("VirtualList.RequestData (from JS)", Identity);
        Volatile.Write(ref _pendingQuery, query);
        while (State == null)
            await Task.Delay(50);
        _ = State.Recompute();
    }

    [JSInvokable]
    public Task UpdateItemVisibility(
        string identity, HashSet<string> visibleKeys, bool isEndAnchorVisible, bool isPinnedToEnd)
    {
        if (JSRef == null!) // The component is disposed
            return Task.CompletedTask;

        if (identity != Identity) {
            Log.LogWarning("Expected JS identity to be {Identity}, but has {ActualIdentity}", Identity, identity);
            return Task.CompletedTask;
        }
        LastReportedItemVisibility = new VirtualListItemVisibility(
            identity, visibleKeys, isEndAnchorVisible, isPinnedToEnd);
        ItemVisibilityChanged?.Invoke(LastReportedItemVisibility);
        return Task.CompletedTask;
    }

    public override async Task SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        // Before the base rather than left to it: its init flow calls StateHasChanged before it creates
        // the State, and that render can reach ShouldRender - which needs the State - with none
        if (ReferenceEquals(State, null)) {
            var (state, stateOptions) = CreateState();
            SetState(state, stateOptions);
        }
        await base.SetParametersAsync(ParameterView.Empty);
    }

    // Protected methods

    protected abstract ValueTask<IJSObjectReference> CreateJSRef();

    protected override bool ShouldRender()
    {
        if (ContentSwapContext?.IsLayerActive == false)
            return false;

        var shouldRender = !ReferenceEquals(Data, RenderedData) // Data changed
            || RenderIndex == 0 // OR very first sync render without data loaded
            || (LastReportedItemVisibility.VisibleKeys.Count == 0 && !Data.HasAllItems); // OR no visible items
        if (!shouldRender) {
            _ = JSRef?.InvokeVoidAsync("renderSkipped");
            return false;
        }

        // The base gates on State consistency and RenderDelayer. Neither is a skip the JS side must hear
        // about: an inconsistent State is already being recomputed, and a postponed render is resumed
        return base.ShouldRender();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (CircuitHub.IsPrerendering)
            return;

        if (firstRender) {
            ChatSwitchTracer.Mark("VirtualList: first render done, creating JS side", Identity);
            BlazorRef = DotNetObjectReference.Create<IVirtualListBackend>(this);
            JSRef = await CreateJSRef();
            ChatSwitchTracer.Mark("VirtualList: JS side created", Identity);
        }
    }

    protected override ComputedState<VirtualListData<TItem>>.Options GetStateOptions()
    {
        return new ComputedState<VirtualListData<TItem>>.Options {
            InitialValue = VirtualListData<TItem>.None,
            UpdateDelayer = FixedDelayer.NextTick,
            TryComputeSynchronously = false, // The list renders its skeletons until the first GetData lands
            Category = GetStateCategory(GetType()),
        };
    }

    protected override async Task<VirtualListData<TItem>> ComputeState(CancellationToken cancellationToken)
    {
        if (ContentSwapContext?.IsLayerActive == false)
            return RenderedData;

        var query = Interlocked.Exchange(ref _pendingQuery, VirtualListDataQuery.None);
        var renderedData = RenderedData;
        var dataSource = DataSource;
        var computed = Computed.GetCurrent();
        var isAnswered = false;
        try {
            var data = await dataSource.GetData(query, renderedData, cancellationToken).ConfigureAwait(false);
            if (ComputedImpl.GetDependencies(computed).Any(d => d.IsInvalidated()))
                return renderedData; // Already invalidated - a render of this data would be wasted

            if (!data.IsNone && data.Count == 0 && !data.HasAllItems) {
                // An unresolved empty window has no keys for the browser to request around.
                // Keep the previous window and leave its query pending for a timed recompute.
                computed.Invalidate(TimeSpan.FromSeconds(1));
                return renderedData;
            }

            isAnswered = true;
            return data;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            // Identity (the chat id for the chat view) is what lets this be correlated with the
            // server-side stack: an RPC error reaches the client as ExceptionInfo - type and
            // message only - so the client stack never says which call actually threw.
            Log.LogError(e,
                "DataSource.GetData failed for {Identity} on query = {Query}",
                Identity, query);
            throw;
        }
        finally {
            // A query nothing rendered stays pending for the recompute that follows - unless a newer one arrived
            if (!isAnswered && !query.IsNone)
                Interlocked.CompareExchange(ref _pendingQuery, query, VirtualListDataQuery.None);
        }
    }
}
