namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Keeps unsolicited UI away while the user is busy with something they came for.
/// Ends on <see cref="Dispose"/>, once the user leaves <see cref="LeaveTarget"/>, or after its max duration.
/// </summary>
public sealed class AttentionHold : IDisposable
{
    public static readonly TimeSpan TargetTimeout = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _stopCts = new();
    private readonly AttentionUI _owner;

    public string Reason { get; }
    public LocalUrl? LeaveTarget { get; }

    internal AttentionHold(AttentionUI owner, string reason, LocalUrl? leaveTarget)
    {
        _owner = owner;
        Reason = reason;
        LeaveTarget = leaveTarget;
    }

    public void Dispose()
    {
        if (_owner.RemoveHold(this))
            Stop();
    }

    public override string ToString()
        => $"{nameof(AttentionHold)}({Reason}, {LeaveTarget})";

    // Protected/internal methods

    internal void Stop()
        => _stopCts.CancelAndDisposeSilently();

    internal async Task Run(UIHub hub, TimeSpan maxDuration, ILogger log)
    {
        var cancellationToken = _stopCts.Token;
        try {
            var whenReleased = LeaveTarget is { } target
                ? WhenLeft(hub, target, cancellationToken)
                : Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            await whenReleased.WaitAsync(maxDuration, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            log.LogWarning("Hold '{Reason}' is dropped: it outlived {MaxDuration}", Reason, maxDuration);
        }
        catch (Exception e) when (e.IsCancellationOf(cancellationToken)) {
            return;
        }
        catch (Exception e) {
            log.LogError(e, "Hold '{Reason}' failed to track its release", Reason);
        }
        Dispose();
    }

    internal static bool IsSameTarget(LocalUrl url, LocalUrl target)
    {
        // A jump to an entry changes the query or the fragment of a chat URL, not the chat itself
        if (target.IsChat(out var targetChatId))
            return url.IsChat(out var chatId) && chatId == targetChatId;

        return GetPath(url.Value) == GetPath(target.Value);
    }

    // Private methods

    private static async Task WhenLeft(UIHub hub, LocalUrl target, CancellationToken cancellationToken)
    {
        var cIsAtTarget = await Computed
            .New(hub.Services, ct => IsAtTarget(hub, target, ct))
            .Update(cancellationToken)
            .ConfigureAwait(false);
        using (var targetCts = cancellationToken.CreateLinkedTokenSource(TargetTimeout)) {
            try {
                await cIsAtTarget.When(x => x, targetCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                return; // The target never opened, so there is nothing to wait for
            }
        }
        await cIsAtTarget.When(x => !x, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsAtTarget(UIHub hub, LocalUrl target, CancellationToken cancellationToken)
    {
        var item = await hub.History.State.Use(cancellationToken).ConfigureAwait(false);
        if (!IsSameTarget(new LocalUrl(item.Url), target))
            return false;
        if (!target.IsChat(out _))
            return true;

        // On a narrow screen the chat list slides over the chat and the URL stays the same
        var panels = hub.PanelsUI;
        if ((await panels.ScreenSize.Use(cancellationToken).ConfigureAwait(false)).IsWide())
            return true;

        return !await panels.Left.IsVisible.Use(cancellationToken).ConfigureAwait(false);
    }

    private static string GetPath(string url)
    {
        var end = url.AsSpan().IndexOfAny('?', '#');
        return end < 0 ? url : url[..end];
    }
}
