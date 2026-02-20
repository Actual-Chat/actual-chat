using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

#pragma warning disable MA0084

public class PlaybackAndRecordingBackgroundActivityUI(AppUIHub hub) : BackgroundActivityUI(hub)
{
    // [ComputeMethod]
    protected override async Task<bool> MustBeBackgroundActive(CancellationToken cancellationToken)
    {
        // ReSharper disable once LocalVariableHidesPrimaryConstructorParameter
        var hub = (AppUIHub)Hub;
        var replayState = await hub.ChatAudioUI.ReplayState.Use(cancellationToken).ConfigureAwait(false);
        if (replayState is not null)
            return true;

        var activeChats = await hub.ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        foreach (var c in activeChats)
            if (c.IsListening || c.IsRecording)
                return true;

        return false;
    }
}
