using ActualChat.UI.Blazor.Module;
using Stl.Interception;

namespace ActualChat.UI.Blazor.Services;

public class TuneUI(IServiceProvider services) : ITuneUIBackend, INotifyInitialized, IDisposable
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.init";
    private static readonly string JSPlayMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.play";
    private static readonly string JSPlayAndWaitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.playAndWait";
    private DotNetObjectReference<ITuneUIBackend> _blazorRef = null!;

    protected static readonly Dictionary<Tune, TuneInfo> Tunes = new () {
        // General actions
        [Tune.CancelReply] = new (new[] { 20 }/*, "cancel-reply"*/),
        [Tune.OpenModal] = new (new[] { 20 }/*, "open-modal"*/),
        [Tune.CloseModal] = new (new[] { 20 }/*, "close-modal"*/),
        [Tune.SelectNavbarItem] = new (new[] { 20 }/*, "select-navbar-item"*/),
        [Tune.ShowInputError] = new (new[] { 80 }/*, "show-input-error"*/),
        // Recording
        [Tune.BeginRecording] = new (new[] { 100, 50, 50 }, "begin-recording"),
        [Tune.EndRecording] = new (new[] { 100 }, "end-recording"),
        [Tune.RemindOfRecording] = new (new[] { 20 }, "remind-of-recording"),
        // Playback
        [Tune.StartRealtimePlayback] = new (new[] { 100 }/*, "start-realtime-playback"*/),
        [Tune.StartHistoricalPlayback] = new (new[] { 100 }/*, "start-historical-playback"*/),
        [Tune.StopHistoricalPlayback] = new (new[] { 20 }/*, "stop-historical-playback"*/),
        [Tune.StopRealtimePlayback] = new (new[] { 20 }/*, "stop-realtime-playback"*/),
        // Chat UI
        [Tune.PinUnpinChat] = new (new[] { 50 }/*, "pin-unpin-chat"*/),
        [Tune.NotifyOnNewMessageInApp] = new (new[] { 20 }, "notify-on-new-message-in-app"),
        // ChatMessageEditor
        [Tune.SendMessage] = new (new[] { 50 }/*, "send-message"*/),
        [Tune.EditMessage] = new (new[] { 20 }/*, "edit-message"*/),
        [Tune.ReplyMessage] = new (new[] { 20 }/*, "reply-message"*/),
        [Tune.ChangeAttachments] = new (new[] { 20 }/*, "change-attachments"*/),
        [Tune.SelectPrimaryLanguage] = new (new[] { 50, 50, 50 }/*, "select-primary-language"*/),
        [Tune.SelectSecondaryLanguage] = new (new[] { 50 }/*, "select-secondary-language"*/),
        [Tune.ShowMenu] = new (new[] { 20 }/*, "show-menu"*/),
    };

    protected virtual bool UseJsVibration => true;
    private IJSRuntime JS { get; } = services.JSRuntime();
    private ILogger Log { get; } = services.LogFor<TuneUI>();

    private async ValueTask InvokeInit()
    {
        try {
            _blazorRef = DotNetObjectReference.Create<ITuneUIBackend>(this);
            await JS.InvokeVoidAsync(JSInitMethod, _blazorRef, Tunes, UseJsVibration).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to init TuneUI");
        }
    }

    void INotifyInitialized.Initialized()
        => _ = InvokeInit();

    public virtual void Dispose()
        => _blazorRef.DisposeSilently();

    public ValueTask Play(Tune tune)
    {
        if (!UseJsVibration)
            _ = Vibrate(tune);
        return JS.InvokeVoidAsync(JSPlayMethod, tune);
    }

    public ValueTask PlayAndWait(Tune tune)
    {
        var vibrateTask = UseJsVibration ? Task.CompletedTask : Vibrate(tune).AsTask();
        return Task.WhenAll(JS.InvokeVoidAsync(JSPlayAndWaitMethod, tune).AsTask(), vibrateTask).ToValueTask();
    }

    [JSInvokable]
    public ValueTask OnVibrate(Tune tune)
        => Vibrate(tune);

    protected virtual ValueTask Vibrate(Tune tune)
        => ValueTask.CompletedTask;
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
    EndRecording,
    RemindOfRecording,
    StartRealtimePlayback,
    StartHistoricalPlayback,
    StopHistoricalPlayback,
    StopRealtimePlayback,
    PinUnpinChat,
    NotifyOnNewMessageInApp,
    SendMessage,
    EditMessage,
    ReplyMessage,
    ChangeAttachments,
    SelectPrimaryLanguage,
    SelectSecondaryLanguage,
    ShowMenu,
}

public record TuneInfo(int[] Vibration, string Sound = "");
