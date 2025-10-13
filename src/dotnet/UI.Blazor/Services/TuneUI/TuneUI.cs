namespace ActualChat.UI.Blazor.Services;

public abstract class TuneUI(UIHub hub) : IDisposable
{
    protected static readonly Dictionary<Tune, TuneInfo> Tunes = new () {
        // General actions
        [Tune.CancelReply] = new ([20] /*, "cancel-reply"*/),
        [Tune.OpenModal] = new ([20] /*, "open-modal"*/),
        [Tune.CloseModal] = new ([20] /*, "close-modal"*/),
        [Tune.SelectNavbarItem] = new ([20] /*, "select-navbar-item"*/),
        [Tune.ShowInputError] = new ([80] /*, "show-input-error"*/),
        [Tune.DragStart] = new ([100] /*, "drag-start"*/),
        [Tune.ChangeToggle] = new ([20] /*, "change-toggle"*/),
        [Tune.ClickButton] = new ([20] /*, "click-button"*/),
        // Recording
        [Tune.BeginRecording] = new ([100, 50, 50], "begin-recording"),
        [Tune.ConfirmRecording] = new ([50, 50, 100] /*, "confirm-recording"*/),
        [Tune.EndRecording] = new ([100], "end-recording"),
        [Tune.RemindOfRecording] = new ([20], "remind-of-recording"),
        // Playback
        [Tune.StartRealtimePlayback] = new ([100] /*, "start-realtime-playback"*/),
        [Tune.StartHistoricalPlayback] = new ([100] /*, "start-historical-playback"*/),
        [Tune.StopHistoricalPlayback] = new ([20] /*, "stop-historical-playback"*/),
        [Tune.StopRealtimePlayback] = new ([20] /*, "stop-realtime-playback"*/),
        // Chat UI
        [Tune.PinUnpinChat] = new ([50] /*, "pin-unpin-chat"*/),
        [Tune.NotifyOnNewMessageInApp] = new ([20], "notify-on-new-message-in-app"),
        [Tune.NotifyOnNewAudioMessageAfterDelay] = new ([20, 40, 100], "new-audio-message-after-delay"),
        [Tune.React] = new ([20, 10, 20]),
        // ChatMessageEditor
        [Tune.SendMessage] = new ([50] /*, "send-message"*/),
        [Tune.EditMessage] = new ([20] /*, "edit-message"*/),
        [Tune.ReplyMessage] = new ([20] /*, "reply-message"*/),
        [Tune.ChangeAttachments] = new ([20] /*, "change-attachments"*/),
        [Tune.ChangeLanguage] = new ([20, 20] /*, "change-language"*/),
        [Tune.ShowMenu] = new ([20] /*, "show-menu"*/),
    };

    protected UIHub Hub { get; } = hub;

    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Hub.LogFor(GetType());

    public virtual void Dispose()
    {
        // no-op in base
    }

    public abstract Task Play(Tune tune, CancellationToken cancellationToken = default);

    public abstract Task PlayAndWait(Tune tune, CancellationToken cancellationToken = default);
}

// !!! keep in sync with tune-ui.ts
public enum Tune
{
    None = 0,
    CancelReply,
    OpenModal,
    CloseModal,
    SelectNavbarItem,
    ShowInputError,
    BeginRecording,
    ConfirmRecording,
    EndRecording,
    RemindOfRecording,
    StartRealtimePlayback,
    StartHistoricalPlayback,
    StopHistoricalPlayback,
    StopRealtimePlayback,
    PinUnpinChat,
    NotifyOnNewMessageInApp,
    NotifyOnNewAudioMessageAfterDelay,
    SendMessage,
    EditMessage,
    ReplyMessage,
    ChangeAttachments,
    ChangeLanguage,
    ShowMenu,
    React,
    DragStart,
    ChangeToggle,
    ClickButton,
}

public record TuneInfo(int[] Vibration, string Sound = "");
