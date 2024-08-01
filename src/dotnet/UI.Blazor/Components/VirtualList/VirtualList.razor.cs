using System.Diagnostics.CodeAnalysis;
using ActualChat.UI.Blazor.Components.Internal;
using ActualChat.UI.Blazor.Module;
using ActualLab.Fusion.Internal;

namespace ActualChat.UI.Blazor.Components;

public static class VirtualList
{
    public static readonly string JSCreateMethod = $"{BlazorUICoreModule.ImportName}.VirtualList.create";
}

public sealed partial class VirtualList<TItem> : ComputedStateComponent<VirtualListData<TItem>>, IVirtualListBackend
    where TItem : class, IVirtualListItem
{
    private ILogger? _log;

    [Inject] private IJSRuntime JS { get; init; } = null!;
    [Inject] private AppBlazorCircuitContext CircuitContext { get; init; } = null!;
    private ILogger Log => _log ??= CircuitContext.Services.LogFor(GetType());

    private ElementReference Ref { get; set; }
    private IJSObjectReference JSRef { get; set; } = null!;
    private DotNetObjectReference<IVirtualListBackend> BlazorRef { get; set; } = null!;

    private VirtualListDataQuery Query { get; set; } = VirtualListDataQuery.None;
    private VirtualListData<TItem> Data => State.LastNonErrorValue;
    private VirtualListData<TItem> LastData { get; set; } = VirtualListData<TItem>.None;
    private VirtualListItemVisibility LastReportedItemVisibility { get; set; } = VirtualListItemVisibility.Empty;

    private int RenderIndex { get; set; } = 0;

    [Parameter] public string Identity { get; set; } = "";
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string Style { get; set; } = "";

    [Parameter, EditorRequired]
    public IVirtualListDataSource<TItem> DataSource { get; set; } = VirtualListDataSource<TItem>.Empty;
    [Parameter] // NOTE(AY): Putting EditorRequired here triggers a warning in Rider (likely their issue)
    public RenderFragment<TItem> Item { get; set; } = null!;
    [Parameter] public RenderFragment<int> Skeleton { get; set; } = null!;
    [Parameter] public int SkeletonCount { get; set; } = 10;
    [Parameter] public double SpacerSize { get; set; } = 200;
    [Parameter] public VirtualListEdge DefaultEdge { get; set; }
    [Parameter] public double ExpandTriggerMultiplier { get; set; } = 1;
    [Parameter] public double ExpandMultiplier { get; set; } = 1.5;
    [Parameter] public IComparer<string> KeyComparer { get; set; } = StringComparer.Ordinal;
    // This event is intentionally Action vs EventCallback, coz normally it shouldn't
    // trigger StateHasChanged on parent component.
    [Parameter] public Action<VirtualListItemVisibility>? ItemVisibilityChanged { get; set; }

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

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await JSRef.DisposeSilentlyAsync("dispose");
        JSRef = null!;
        BlazorRef.DisposeSilently();
        BlazorRef = null!;
    }

    public async ValueTask Reset()
    {
        RenderIndex = 0;
        Query = VirtualListDataQuery.None;
        LastData = Data;
        LastReportedItemVisibility = VirtualListItemVisibility.Empty;
        StateHasChanged();
        await JSRef.InvokeVoidAsync("reset");
    }

    protected override bool ShouldRender()
        => !ReferenceEquals(Data, LastData) // Data changed
            || RenderIndex == 0 // OR very first sync render without data loaded
            || (RenderIndex == 1 && !Data.IsNone); // OR it's our first render with data;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (CircuitContext.IsPrerendering)
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
        => ComputedStateComponent.GetStateOptions(GetType(),
            static t => new ComputedState<VirtualListData<TItem>>.Options() {
                InitialValue = VirtualListData<TItem>.None,
                UpdateDelayer = FixedDelayer.NextTick,
                Category = ComputedStateComponent.GetStateCategory(t),
            });

    protected override async Task<VirtualListData<TItem>> ComputeState(CancellationToken cancellationToken)
    {
        var query = Query;
        // Use None after reset() call
        var lastData = LastData.Index >= RenderIndex ? VirtualListData<TItem>.None : LastData;
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
    }
}
