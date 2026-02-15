using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Plays haptic feedback and sound effects for UI interactions.
/// </summary>
public abstract class TuneUI : IDisposable
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
        [Tune.StartListening] = new ([100] /*, "start-listening"*/),
        [Tune.StopListening] = new ([20] /*, "stop-listening"*/),
        [Tune.StartReplay] = new ([100] /*, "start-replay"*/),
        [Tune.StopReplay] = new ([20] /*, "stop-replay"*/),
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
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.init";
    private DotNetObjectReference<TuneUI>? _backendRef;

    protected TuneUI(UIHub hub)
    {
        Hub = hub;
        _ = Initialize();
    }

    protected UIHub Hub { get; }

    protected ILogger Log => field ??= Hub.LogFor(GetType());

    private async ValueTask Initialize()
    {
        try {
            _backendRef ??= DotNetObjectReference.Create(this);
            await Hub.JS.InvokeVoidAsync(JSInitMethod, _backendRef, Tunes).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Initialize failed");
        }
    }

    public virtual void Dispose()
        => _backendRef.DisposeSilently();

    public abstract Task Play(Tune tune);

    public abstract Task PlayAndWait(Tune tune);
}

/// <summary>
/// Identifies specific UI feedback sounds and haptic patterns.
/// </summary>
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
    StartListening,
    StartReplay,
    StopReplay,
    StopListening,
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

/// <summary>
/// Defines vibration pattern and optional sound for a <see cref="Tune"/>.
/// </summary>
public record TuneInfo(int[] Vibration, string Sound = "");
