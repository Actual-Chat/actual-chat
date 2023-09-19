using ActualChat.UI.Blazor.Services;
using Stl.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public class SelectionUI : WorkerBase, INotifyInitialized, IComputeService
{
    private readonly IMutableState<ImmutableHashSet<ChatEntryId>> _selection;
    private readonly IMutableState<bool> _hasSelection;

    private IAsyncDisposable? _escapistSubscription;

    private Escapist Escapist { get; }
    private ChatUI ChatUI { get; }

    public SelectionUI(IServiceProvider services)
    {
        _selection = services.StateFactory().NewMutable(ImmutableHashSet<ChatEntryId>.Empty);
        _hasSelection = services.StateFactory().NewMutable(false);

        Escapist = services.GetRequiredService<Escapist>();
        ChatUI = services.GetRequiredService<ChatUI>();
    }

    public IState<bool> HasSelection => _hasSelection;
    public IState<ImmutableHashSet<ChatEntryId>> Selection => _selection;

    void INotifyInitialized.Initialized()
        => this.Start();

    protected override async Task OnRun(CancellationToken cancellationToken) {
        await ChatUI.WhenLoaded.ConfigureAwait(false);
        await foreach (var _ in ChatUI.SelectedChatId.Changes(cancellationToken).ConfigureAwait(false))
            Clear();
    }

    public async Task Select(ChatEntryId chatEntryId) {
        _selection.Value = _selection.Value.Add(chatEntryId);
        _hasSelection.Value = true;
        _escapistSubscription ??= await Escapist.SubscribeOnce(Clear, StopToken).ConfigureAwait(false);
    }

    public void Unselect(ChatEntryId chatEntryId) {
        if (_selection.Value.Count == 1) {
            Clear();
            return;
        }

        _selection.Value = _selection.Value.Remove(chatEntryId);
    }

    public void Clear() {
        _selection.Value = ImmutableHashSet<ChatEntryId>.Empty;
        _hasSelection.Value = false;

        var escapistSubscription = Interlocked.Exchange(ref _escapistSubscription, null);
        if (escapistSubscription == null)
            return;

        _ = escapistSubscription.DisposeSilentlyAsync().ConfigureAwait(false);
    }

    public bool IsSelected(ChatEntryId chatEntryId)
        => _selection.Value.Contains(chatEntryId);
}
