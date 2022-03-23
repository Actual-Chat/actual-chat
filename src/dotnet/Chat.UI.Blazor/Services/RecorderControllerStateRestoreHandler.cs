using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Blazored.SessionStorage;

namespace ActualChat.Chat.UI.Blazor.Services;

public class RecorderControllerStateRestoreHandler : StateRestoreHandler<string>
{
    private readonly AudioRecorderService _recorderService;
    private readonly InteractionUI _interactionUi;

    public RecorderControllerStateRestoreHandler(
        AudioRecorderService recorderService,
        InteractionUI interactionUi,
        IServiceProvider services)
        : base(services)
    {
        _recorderService = recorderService;
        _interactionUi = interactionUi;
    }

    protected override string StoreItemKey => "audioRecorderChat";

    protected override async Task Restore(string? itemValue)
    {
        var audioRecorderChat = itemValue;
        if (!string.IsNullOrEmpty(audioRecorderChat)) {
            await _interactionUi.RequestInteraction().ConfigureAwait(false);
            await _recorderService.StartRecording(audioRecorderChat).ConfigureAwait(false);
        }
    }

    protected override async Task<string> Compute(CancellationToken cancellationToken)
        => await _recorderService.GetRecordingChat(cancellationToken).ConfigureAwait(false);
}
