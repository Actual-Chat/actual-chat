using ActualChat.UI.Blazor.Components.Internal;
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
    private VirtualListData<TItem>? _initialData;

    private ILogger Log => field ??= Hub.LogFor(GetType());

    protected ElementReference Ref { get; set; }
    protected IJSObjectReference JSRef { get; set; } = null!;
    protected DotNetObjectReference<IVirtualListBackend> BlazorRef { get; set; } = null!;

    protected VirtualListDataQuery Query { get; set; } = VirtualListDataQuery.None;
    // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
    protected VirtualListData<TItem> Data => State?.LastNonErrorValue ?? VirtualListData<TItem>.None;
    protected VirtualListData<TItem> LastData { get; set; } = VirtualListData<TItem>.None;
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
    [Parameter] public double ExpandMultiplier { get; set; } = 2;
    // This event is intentionally Action vs EventCallback, coz normally it shouldn't
    // trigger StateHasChanged on parent component.
    [Parameter] public Action<VirtualListItemVisibility>? ItemVisibilityChanged { get; set; }
    [CascadingParameter] public ScreenSize ScreenSize { get; set; }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await JSRef.DisposeSilentlyAsync("dispose");
        JSRef = null!;
        BlazorRef.DisposeSilently();
        BlazorRef = null!;
        RenderIndex = 0;
        LastData = VirtualListData<TItem>.None;
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
        Query = query;
        while (State == null)
            await Task.Delay(100);
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
        // NOTE: Hub (and other injected services) aren't available yet in the first SetParametersAsync,
        // so we can't use Hub.IsPrerendering here. RendererInfo is set before SetParametersAsync and is
        // per-circuit: IsInteractive is false during prerender SSR, true once the list is interactive.
        var shouldSetInitialData = RendererInfo.IsInteractive && RenderIndex == 0;
        if (shouldSetInitialData)
            _initialData = await DataSource.GetData(VirtualListDataQuery.None,
                VirtualListData<TItem>.None,
                CancellationToken.None);
        else
            _initialData = null;

        if (ReferenceEquals(State, null) && shouldSetInitialData && _initialData != null) {
            var (state, stateOptions) = CreateState();
            SetState(state, stateOptions);
        }
        await base.SetParametersAsync(ParameterView.Empty);
    }

    // Protected methods

    protected abstract ValueTask<IJSObjectReference> CreateJSRef();

    protected override bool ShouldRender()
    {
        var shouldRender = !ReferenceEquals(Data, LastData) // Data changed
            || RenderIndex == 0 // OR very first sync render without data loaded
            || (LastReportedItemVisibility.VisibleKeys.Count == 0 && !Data.HasAllItems);
        if (JSRef != null! && !shouldRender)
            _ = JSRef.InvokeVoidAsync("renderSkipped");

        return shouldRender; // OR there are no visible items
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (CircuitHub.IsPrerendering)
            return;

        if (firstRender) {
            BlazorRef = DotNetObjectReference.Create<IVirtualListBackend>(this);
            JSRef = await CreateJSRef();
        }
    }

    protected override ComputedState<VirtualListData<TItem>>.Options GetStateOptions()
    {
        var initialData = _initialData ?? VirtualListData<TItem>.None;
        return new ComputedState<VirtualListData<TItem>>.Options {
            InitialValue = initialData,
            UpdateDelayer = FixedDelayer.NextTick,
            TryComputeSynchronously = true,
            Category = GetStateCategory(GetType()),
        };
    }

    protected override async Task<VirtualListData<TItem>> ComputeState(CancellationToken cancellationToken)
    {
        var query = Query;
        var lastData = LastData;
        VirtualListData<TItem> data;
        var computed = Computed.GetCurrent();
        try {
            data = await DataSource.GetData(query, lastData, cancellationToken).ConfigureAwait(false);
            if (ComputedImpl.GetDependencies(computed).Any(d => d.IsInvalidated()))
                return lastData; // Current computed is already invalidated, so no reason to waste our time re-rendering right now
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
        return data;
    }
}
