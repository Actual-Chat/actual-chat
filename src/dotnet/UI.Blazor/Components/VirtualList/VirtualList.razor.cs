using ActualChat.UI.Blazor.Components.Internal;
using ActualChat.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services;
using ActualLab.Fusion.Internal;

namespace ActualChat.UI.Blazor.Components;

public static class VirtualList
{
    public static readonly string JSCreateMethod = $"{BlazorUICoreModule.ImportName}.VirtualList.create";
    public static bool IsNonFirstRender = false;
}

public sealed partial class VirtualList<TItem> : ComputedStateComponent<UIHub, VirtualListData<TItem>>, IVirtualListBackend
    where TItem : class, IVirtualListItem
{
    private VirtualListData<TItem>? _initialData;

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Hub.LogFor(GetType());

    private ElementReference Ref { get; set; }
    private IJSObjectReference JSRef { get; set; } = null!;
    private DotNetObjectReference<IVirtualListBackend> BlazorRef { get; set; } = null!;

    private VirtualListDataQuery Query { get; set; } = VirtualListDataQuery.None;
    // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
    private VirtualListData<TItem> Data => State?.LastNonErrorValue ?? VirtualListData<TItem>.None;
    private VirtualListData<TItem> LastData { get; set; } = VirtualListData<TItem>.None;
    private VirtualListItemVisibility LastReportedItemVisibility { get; set; } = VirtualListItemVisibility.Empty;

    private int RenderIndex { get; set; } = 0;

    [Parameter] public bool RequestDataOnParameterSet { get; set; }
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
    [Parameter] public double SpacerSize { get; set; } = 200;
    [Parameter] public VirtualListEdge DefaultEdge { get; set; }
    [Parameter] public double ExpandTriggerMultiplier { get; set; } = 2;
    [Parameter] public double ExpandMultiplier { get; set; } = 2;
    [Parameter] public int  MinExpand { get; set; } = 20;
    // This event is intentionally Action vs EventCallback, coz normally it shouldn't
    // trigger StateHasChanged on parent component.
    [Parameter] public Action<VirtualListItemVisibility>? ItemVisibilityChanged { get; set; }
    [CascadingParameter] public ScreenSize ScreenSize { get; set; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VirtualList<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VirtualListData<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VirtualListDataQuery))]
    public VirtualList() { }

    [JSInvokable]
    public async Task RequestData(VirtualListDataQuery query)
    {
        Query = query;
        while (State == null)
            await Task.Delay(100);
        _ = State.Recompute();
    }

    [JSInvokable]
    public Task UpdateItemVisibility(string identity, HashSet<string> visibleKeys, bool isEndAnchorVisible)
    {
        if (JSRef == null!) // The component is disposed
            return Task.CompletedTask;

        if (!OrdinalEquals(identity, Identity)) {
            Log.LogWarning("Expected JS identity to be {Identity}, but has {ActualIdentity}", Identity, identity);
            return Task.CompletedTask;
        }
        LastReportedItemVisibility = new VirtualListItemVisibility(identity, visibleKeys, isEndAnchorVisible);
        ItemVisibilityChanged?.Invoke(LastReportedItemVisibility);
        return Task.CompletedTask;
    }

    public override async Task SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var shouldSetInitialData = RequestDataOnParameterSet && VirtualList.IsNonFirstRender;
        if (shouldSetInitialData)
            _initialData = await DataSource.GetData(VirtualListDataQuery.None,
                VirtualListData<TItem>.None,
                CancellationToken.None);
        else
            _initialData = null;

        LastData = VirtualListData<TItem>.None;
        if (ReferenceEquals(State, null) && shouldSetInitialData && _initialData != null) {
            var (state, stateOptions) = CreateState();
            SetState(state, stateOptions);
        }
        await base.SetParametersAsync(ParameterView.Empty);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await JSRef.DisposeSilentlyAsync("dispose");
        JSRef = null!;
        BlazorRef.DisposeSilently();
        BlazorRef = null!;
        RenderIndex = 0;
        LastData = VirtualListData<TItem>.None;
    }

    public async ValueTask Reset()
    {
        RenderIndex = 0;
        Query = VirtualListDataQuery.None;
        LastData = VirtualListData<TItem>.None;
        LastReportedItemVisibility = VirtualListItemVisibility.Empty;
        StateHasChanged();
        await JSRef.InvokeVoidAsync("reset");
    }

    protected override bool ShouldRender()
    {
        var shouldRender = !Data.IsSimilarTo(LastData) // Data changed
            || RenderIndex == 0 // OR very first sync render without data loaded
            || LastReportedItemVisibility.VisibleKeys.Count == 0;
        if (JSRef != null! && !shouldRender)
            _ = JSRef.InvokeVoidAsync("renderSkipped");

        return shouldRender; // OR there are no visible items
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        VirtualList.IsNonFirstRender = true;
        if (CircuitHub.IsPrerendering)
            return;

        if (firstRender) {
            BlazorRef = DotNetObjectReference.Create<IVirtualListBackend>(this);
            JSRef = await JS.InvokeAsync<IJSObjectReference>(VirtualList.JSCreateMethod,
                Ref,
                BlazorRef,
                Identity,
                DefaultEdge,
                SpacerSize,
                ExpandTriggerMultiplier,
                ExpandMultiplier
                );
        }
    }

    protected override ComputedState<VirtualListData<TItem>>.Options GetStateOptions()
    {
        var initialData = _initialData ?? VirtualListData<TItem>.None;
        return new ComputedState<VirtualListData<TItem>>.Options {
            InitialValue = initialData,
            UpdateDelayer = FixedDelayer.NextTick,
            Category = GetStateCategory(GetType()),
        };
    }

    protected override async Task<VirtualListData<TItem>> ComputeState(CancellationToken cancellationToken)
    {
        var query = EnsureMinExpandSet(Query);

        var lastData = LastData;
        VirtualListData<TItem> data;
        var computed = Computed.GetCurrent();
        try {
            data = await DataSource.GetData(query, lastData, cancellationToken).ConfigureAwait(false);
            if (ComputedImpl.GetDependencies(computed).Any(d => d.IsInvalidated()))
                return lastData; // Current computed is already invalidated, so no reason to waste our time re-rendering right now
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "DataSource.Invoke(query) failed on query = {Query}", query);
            throw;
        }
        return data;


        VirtualListDataQuery EnsureMinExpandSet(VirtualListDataQuery query1)
        {
            if (query1.MoveRange is { Start: >= 0, End: <= 0 })
                return query1;

            var start = query1.MoveRange.Start;
            var end = query1.MoveRange.End;
            var startExpand = start < 0 ? Math.Min(start, -MinExpand) : start;
            var endExpand = end > 0 ? Math.Max(end, MinExpand) : end;
            return new VirtualListDataQuery(query1.KeyRange, query1.VirtualRange, new Range<int>(startExpand, endExpand)) {
                ExpectedCount = query1.ExpectedCount,
            };
        }
    }
}
