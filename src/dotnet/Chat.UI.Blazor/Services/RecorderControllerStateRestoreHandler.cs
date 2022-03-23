using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Blazored.SessionStorage;

namespace ActualChat.Chat.UI.Blazor.Services;

public class RecorderControllerStateRestoreHandler : StateRestoreHandler<string>
{
    private readonly AudioRecorderService _recorderService;
    private readonly InteractionUI _interactionUi;
    private readonly IChats _chats;
    private readonly Session _session;

    public RecorderControllerStateRestoreHandler(
        AudioRecorderService recorderService,
        InteractionUI interactionUi,
        IChats chats,
        Session session,
        IServiceProvider services)
        : base(services)
    {
        _recorderService = recorderService;
        _chats = chats;
        _session = session;
        _interactionUi = interactionUi;
    }

    protected override string StoreItemKey => "audioRecorderChat";

    protected override async Task Restore(string? itemValue)
    {
        var audioRecorderChat = itemValue;
        if (string.IsNullOrEmpty(audioRecorderChat))
            return;

        var permissions = await _chats.GetPermissions(_session, audioRecorderChat, default).ConfigureAwait(false);
        if (!permissions.HasFlag(ChatPermissions.Write))
            return;

        await _interactionUi.RequestInteraction().ConfigureAwait(false);
        await _recorderService.StartRecording(audioRecorderChat).ConfigureAwait(false);
    }

    protected override async Task<string> Compute(CancellationToken cancellationToken)
        => await _recorderService.GetRecordingChat(cancellationToken).ConfigureAwait(false);
}
