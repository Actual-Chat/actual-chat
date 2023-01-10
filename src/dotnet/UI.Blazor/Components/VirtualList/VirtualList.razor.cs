using ActualChat.UI.Blazor.Components.Internal;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Components;

public sealed partial class VirtualList<TItem> : ComputedStateComponent<VirtualListData<TItem>>, IVirtualListBackend
    where TItem : IVirtualListItem
{
    [Inject] private IJSRuntime JS { get; init; } = null!;
    [Inject] private AppBlazorCircuitContext CircuitContext { get; init; } = null!;
    [Inject] private ILogger<VirtualList<TItem>> Log { get; init; } = null!;

    private bool DebugMode => Constants.DebugMode.VirtualList;

    private ElementReference Ref { get; set; }
    private IJSObjectReference JSRef { get; set; } = null!;
    private DotNetObjectReference<IVirtualListBackend> BlazorRef { get; set; } = null!;

    private VirtualListDataQuery LastQuery { get; set; } = VirtualListDataQuery.None;
    private VirtualListDataQuery Query { get; set; } = VirtualListDataQuery.None;
    private VirtualListData<TItem> Data => State.Value;
    private VirtualListData<TItem> LastData { get; set; } = VirtualListData<TItem>.None;

    private int RenderIndex { get; set; } = 0;

    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string Style { get; set; } = "";

    [Parameter, EditorRequired]
    public IVirtualListDataSource<TItem> DataSource { get; set; } = VirtualListDataSource<TItem>.Empty;
    [Parameter] // NOTE(AY): Putting EditorRequired here triggers a warning in Rider (likely their issue)
    public RenderFragment<TItem> Item { get; set; } = null!;
    [Parameter] public RenderFragment<int> Skeleton { get; set; } = null!;
    [Parameter] public RenderFragment? Empty { get; set; }
    [Parameter] public int SkeletonCount { get; set; } = 50;
    [Parameter] public double SpacerSize { get; set; } = 300;
    [Parameter] public IMutableState<List<string>>? VisibleKeysState { get; set; }
    [Parameter] public IMutableState<bool>? IsEndAnchorVisibleState { get; set; }
    [Parameter] public IComparer<string> KeyComparer { get; set; } = StringComparer.Ordinal;

    [JSInvokable]
    public Task RequestData(VirtualListDataQuery query)
    {
        Query = query;
        _ = State.Recompute();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task UpdateVisibleKeys(List<string> visibleKeys, bool isEndAnchorVisible)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        // Do not update state if disposed
        if (JSRef == null)
            return Task.CompletedTask;

        if (visibleKeys.Count > 0 && VisibleKeysState != null)
            VisibleKeysState.Value = visibleKeys;

        if (IsEndAnchorVisibleState != null)
            IsEndAnchorVisibleState.Value = isEndAnchorVisible;

        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        var jsRef = JSRef;
        var blazorRef = BlazorRef;
        JSRef = null!;
        BlazorRef = null!;

        await base.DisposeAsync();
        await jsRef.DisposeSilentlyAsync("dispose");
        blazorRef.DisposeSilently();
    }

    protected override bool ShouldRender()
        => !ReferenceEquals(Data, LastData) || RenderIndex == 0 || (Data.Items.Count > 0 && VisibleKeysState?.Value.Count == 0);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (CircuitContext.IsPrerendering)
            return;

        if (firstRender) {
            BlazorRef = DotNetObjectReference.Create<IVirtualListBackend>(this);
            JSRef = await JS.InvokeAsync<IJSObjectReference>(
                $"{BlazorUICoreModule.ImportName}.VirtualList.create",
                Ref,
                BlazorRef);
            VisibleKeysState ??= StateFactory.NewMutable(new List<string>());
        }
    }

    protected override ComputedState<VirtualListData<TItem>>.Options GetStateOptions()
        => new () {
            UpdateDelayer = FixedDelayer.Instant,
            InitialValue = VirtualListData<TItem>.None,
        };

    protected override async Task<VirtualListData<TItem>> ComputeState(CancellationToken cancellationToken)
    {
        var query = Query;
        VirtualListData<TItem> response;
        try {
            response = await DataSource.GetData(query, LastData, cancellationToken);
            LastQuery = Query = response.Query;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "DataSource.Invoke(query) failed on query = {Query}", query);
            throw;
        }
        return response;
    }
}
