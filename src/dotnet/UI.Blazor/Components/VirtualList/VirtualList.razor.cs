using ActualChat.UI.Blazor.Components.Internal;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Components;

public static class VirtualList
{
    public static readonly string JSCreateMethod = $"{BlazorUICoreModule.ImportName}.VirtualList.create";
}

public sealed partial class VirtualList<TItem> : ComputedStateComponent<VirtualListData<TItem>>, IVirtualListBackend
    where TItem : IVirtualListItem
{
    [Inject] private IJSRuntime JS { get; init; } = null!;
    [Inject] private AppBlazorCircuitContext CircuitContext { get; init; } = null!;
    [Inject] private ILogger<VirtualList<TItem>> Log { get; init; } = null!;

    private ElementReference Ref { get; set; }
    private IJSObjectReference JSRef { get; set; } = null!;
    private DotNetObjectReference<IVirtualListBackend> BlazorRef { get; set; } = null!;

    private VirtualListDataQuery Query { get; set; } = VirtualListDataQuery.None;
    private VirtualListData<TItem> Data => State.Value;
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
    [Parameter] public RenderFragment? Empty { get; set; }
    [Parameter] public Func<VirtualListData<TItem>, bool>? IsEmptyTest { get; set; }
    [Parameter] public int SkeletonCount { get; set; } = 10;
    [Parameter] public double SpacerSize { get; set; } = 200;
    [Parameter] public IComparer<string> KeyComparer { get; set; } = StringComparer.Ordinal;
    // This event is intentionally Action vs EventCallback, coz normally it shouldn't
    // trigger StateHasChanged on parent component.
    [Parameter] public Action<VirtualListItemVisibility>? ItemVisibilityChanged { get; set; }

    [JSInvokable]
    public Task RequestData(VirtualListDataQuery query)
    {
        Query = query;
        _ = State.Recompute();
        return Task.CompletedTask;
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
        var jsRef = JSRef;
        var blazorRef = BlazorRef;
        JSRef = null!;
        BlazorRef = null!;

        await base.DisposeAsync();
        await jsRef.DisposeSilentlyAsync("dispose");
        blazorRef.DisposeSilently();
    }

    protected override bool ShouldRender()
        => !ReferenceEquals(Data, LastData) // Data changed
            || RenderIndex == 0 // OR it's our very first render
            || (Data.Items.Count > 0 && LastReportedItemVisibility.IsEmpty); // Or there are items to display & no reported item visibility yet

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (CircuitContext.IsPrerendering)
            return;

        if (firstRender) {
            BlazorRef = DotNetObjectReference.Create<IVirtualListBackend>(this);
            JSRef = await JS.InvokeAsync<IJSObjectReference>(VirtualList.JSCreateMethod, Ref, BlazorRef, Identity);
        }
    }

    protected override ComputedState<VirtualListData<TItem>>.Options GetStateOptions()
        => new () {
            InitialValue = VirtualListData<TItem>.None,
            UpdateDelayer = FixedDelayer.Instant,
            Category = GetStateCategory(),
        };

    protected override async Task<VirtualListData<TItem>> ComputeState(CancellationToken cancellationToken)
    {
        var query = Query;
        VirtualListData<TItem> response;
        try {
            response = await DataSource.GetData(query, LastData, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "DataSource.Invoke(query) failed on query = {Query}", query);
            throw;
        }
        return response;
    }

    private bool IsEmpty(VirtualListData<TItem> data)
    {
        if (IsEmptyTest != null)
            return IsEmptyTest(data);
        if (data == VirtualListData<TItem>.None)
            return false;
        return data.Items.Count == 0;
    }
}
