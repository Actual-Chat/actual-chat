using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class TuneUI(IServiceProvider services)
{
    private static readonly string JSPlayMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.play";
    private static readonly string JSPlayAndWaitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.playAndWait";
    private static readonly Dictionary<Tune, string> Tunes = new () {
        [Tune.BeginRecording] = "begin-recording",
        [Tune.EndRecording] = "end-recording",
        [Tune.RemindOfRecording] = "remind-of-recording",
        [Tune.StartHistoricalPlayback] = "start-historical-playback",
        [Tune.StopHistoricalPlayback] = "stop-historical-playback",
        [Tune.StartRealtimePlayback] = "start-realtime-playback",
        [Tune.StopRealtimePlayback] = "stop-realtime-playback",
        [Tune.ReplyMessage] = "reply-message",
        [Tune.EditMessage] = "edit-message",
        [Tune.CancelReply] = "cancel-reply",
        [Tune.OpenModal] = "open-modal",
        [Tune.CloseModal] = "close-modal",
        [Tune.ShowInputError] = "show-input-error",
        [Tune.PinUnpinChat] = "pin-unpin-chat",
        [Tune.SelectPrimaryLanguage] = "select-primary-language",
        [Tune.SelectSecondaryLanguage] = "select-secondary-language",
        [Tune.SelectNavbarItem] = "select-navbar-item",
        [Tune.SendMessage] = "send-message",
    };

    private IJSRuntime JS { get; } = services.JSRuntime();

    public ValueTask Play(Tune tune)
        => JS.InvokeVoidAsync(JSPlayMethod, Tunes.GetValueOrDefault(tune));

    public ValueTask PlayAndWait(Tune tune)
        => JS.InvokeVoidAsync(JSPlayAndWaitMethod, Tunes.GetValueOrDefault(tune));
}

public enum Tune
{
    None = 0,
    BeginRecording,
    EndRecording,
    RemindOfRecording,
    StartHistoricalPlayback,
    StopHistoricalPlayback,
    StartRealtimePlayback,
    StopRealtimePlayback,
    ReplyMessage,
    EditMessage,
    CancelReply,
    OpenModal,
    CloseModal,
    ShowInputError,
    PinUnpinChat,
    SelectPrimaryLanguage,
    SelectSecondaryLanguage,
    SelectNavbarItem,
    SendMessage,
}
