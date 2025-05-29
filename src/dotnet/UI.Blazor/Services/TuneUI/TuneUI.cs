using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class TuneUI : ITuneUIBackend, IDisposable
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.init";
    private static readonly string JSPlayMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.play";
    private static readonly string JSPlayAndWaitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.playAndWait";

#pragma warning disable CA1861 // Prefer 'static readonly' fields over constant array arguments ...
    protected static readonly Dictionary<Tune, TuneInfo> Tunes = new () {
        // General actions
        [Tune.CancelReply] = new ([20] /*, "cancel-reply"*/),
        [Tune.OpenModal] = new ([20] /*, "open-modal"*/),
        [Tune.CloseModal] = new ([20] /*, "close-modal"*/),
        [Tune.SelectNavbarItem] = new ([20] /*, "select-navbar-item"*/),
        [Tune.ShowInputError] = new ([80] /*, "show-input-error"*/),
        [Tune.DragStart] = new ([100] /*, "drag-start"*/),
        [Tune.ChangeToggle] = new ([20] /*, "change-toggle"*/),
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
#pragma warning restore CA1861

    private DotNetObjectReference<ITuneUIBackend> _blazorRef = null!;

    protected virtual bool UseJsVibration => true;

    private UIHub Hub { get; }
    private IJSRuntime JS => Hub.JS;
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Hub.LogFor<TuneUI>();

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TuneUI))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TuneInfo))]
    public TuneUI(UIHub hub)
    {
        Hub = hub;
        _ = Initialize();
    }

    public async ValueTask Initialize()
    {
        try {
            _blazorRef = DotNetObjectReference.Create<ITuneUIBackend>(this);
            await JS.InvokeVoidAsync(JSInitMethod, _blazorRef, Tunes, UseJsVibration).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Initialize failed");
        }
    }

    public virtual void Dispose()
    {
        _blazorRef.DisposeSilently();
        _blazorRef = null!;
    }

    public Task Play(Tune tune)
        => ForegroundTask.Run(() => {
            _ = VibrateNoJs(tune);
            return JS.InvokeVoidAsync(JSPlayMethod, tune).AsTask();
        });

    public ValueTask PlayAndWait(Tune tune)
        => TaskExt.WhenAll(VibrateNoJs(tune), JS.InvokeVoidAsync(JSPlayAndWaitMethod, tune));

    [JSInvokable]
    public ValueTask OnVibrate(Tune tune)
        => Vibrate(tune);

    protected virtual ValueTask Vibrate(Tune tune)
        => ValueTask.CompletedTask;

    private ValueTask VibrateNoJs(Tune tune)
        => !UseJsVibration ? Vibrate(tune) : ValueTask.CompletedTask;
}

internal interface ITuneUIBackend
{
    ValueTask OnVibrate(Tune tune);
}

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
}

public record TuneInfo(int[] Vibration, string Sound = "");
