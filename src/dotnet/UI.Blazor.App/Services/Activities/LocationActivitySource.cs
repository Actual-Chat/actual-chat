using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class LocationActivitySource(AppUIHub hub) : IActivitySource, IHasDisposeStatus
{
    private LiveLocationReporter LiveLocationReporter
        => field ??= hub.Services.GetRequiredService<LiveLocationReporter>();
    public bool IsDisposed => false;

    [ComputeMethod]
    public virtual async Task<ActivityInfo?> GetActivity(CancellationToken cancellationToken)
    {
        var chatIds = await LiveLocationReporter.GetActiveShareChatIds(cancellationToken).ConfigureAwait(false);
        if (chatIds.IsEmpty)
            return null;

        return new LocationActivity(chatIds[0], chatIds.Length);
    }
}
