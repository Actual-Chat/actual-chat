using ActualChat.MediaPlayback;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class ChatEntryPlayer : ProcessorBase
{
    private AppUIHub Hub { get; }
    private ILogger Log { get; }

    public ChatId ChatId { get; }
    public Playback Playback { get; }

    public ChatEntryPlayer(
        AppUIHub hub,
        ChatId chatId,
        Playback playback,
        CancellationToken cancellationToken)
        : base(cancellationToken.CreateLinkedTokenSource())
    {
        Hub = hub;
        ChatId = chatId;
        Playback = playback;
        Log = Hub.LogFor(GetType());
    }

    protected override Task DisposeAsyncCore()
        => Abort(); // Never throws

    public Task WhenDonePlaying()
        => Task.CompletedTask;

    public async Task Abort()
    {
        try {
            await Playback.Abort().WhenCompleted.ConfigureAwait(false);
        }
        catch (Exception e) {
            if (e is not (OperationCanceledException or ObjectDisposedException))
                Log.LogError(e, "Failed to abort playback in chat #{ChatId}", ChatId);
        }
    }
}
