using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Runs the <see cref="IPrefetcher"/> named by a <c>data-prefetch</c> attribute when the pointer goes
/// down on the element carrying it, so the round trips the follow-up click needs are already in flight
/// by the time it lands. Everything here is best-effort: a failed prefetch only costs the warm-up.
/// </summary>
public sealed class PrefetchUI : UIServiceBase<UIHub>, IDisposable
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.PrefetchUI.init";
    // A pointer down repeated on the same target within this window is the same intent, not a new one
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(1);

    private readonly DotNetObjectReference<PrefetchUI> _blazorRef;
    private readonly Lock _lock = new();
    private string _lastRef = "";
    private CpuTimestamp _lastRefAt;

    public PrefetchUI(UIHub hub) : base(hub)
        => _blazorRef = DotNetObjectReference.Create(this);

    public void Dispose()
        => _blazorRef.DisposeSilently();

    public Task Initialize()
        => JS.InvokeVoidAsync(JSInitMethod, _blazorRef).AsTask();

    [JSInvokable]
    public void OnPrefetchRequest(string sPrefetchRef)
    {
        if (sPrefetchRef.IsNullOrEmpty() || IsRepeat(sPrefetchRef))
            return;

        if (!PrefetchRef.TryParse(sPrefetchRef, out var prefetchRef)) {
            Log.LogWarning("OnPrefetchRequest: can't parse '{PrefetchRef}'", sPrefetchRef);
            return;
        }

        // Fire-and-forget on purpose: the pointer-down handler must not wait for any of this
        _ = BackgroundTask.Run(
            () => Prefetch(prefetchRef),
            Log,
            $"Prefetch failed for '{sPrefetchRef}'.",
            Hub.StopToken);
    }

    // Private methods

    private Task Prefetch(PrefetchRef prefetchRef)
    {
        if (Services.GetService(prefetchRef.PrefetcherType) is not IPrefetcher prefetcher) {
            Log.LogWarning("Prefetch: {PrefetcherType} isn't registered", prefetchRef.PrefetcherType);
            return Task.CompletedTask;
        }

        return prefetcher.Prefetch(prefetchRef.Arguments, Hub.StopToken);
    }

    private bool IsRepeat(string sPrefetchRef)
    {
        lock (_lock) {
            if (sPrefetchRef == _lastRef && _lastRefAt.Elapsed < RepeatWindow)
                return true;

            _lastRef = sPrefetchRef;
            _lastRefAt = CpuTimestamp.Now;
            return false;
        }
    }
}
