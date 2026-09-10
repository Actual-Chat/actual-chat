namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// An <see cref="ErrorBoundary"/> that keeps its error content when several children fail in one render:
/// the renderer queues an empty render of the boundary per error, while the boundary's own re-render is
/// deduplicated, so without a follow-up render the later empty renders would leave it blank.
/// </summary>
public sealed class ErrorBarrierBoundary : ErrorBoundary
{
    private bool _isRerenderScheduled;
    [Parameter] public Action<Exception>? Activated { get; set; }

    protected override async Task OnErrorAsync(Exception exception)
    {
        // Called before CurrentException is set, so it's null only for the first error since Recover
        if (CurrentException is null)
            Activated?.Invoke(exception);
        await base.OnErrorAsync(exception).ConfigureAwait(true);
        if (_isRerenderScheduled)
            return;

        _isRerenderScheduled = true;
        await Task.Yield(); // Lets the current render batch, with all its queued empty renders, finish
        _isRerenderScheduled = false;
        this.NotifyStateHasChanged();
    }
}
