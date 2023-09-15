using ActualChat.UI.Blazor.Module;
using Stl.Interception;

namespace ActualChat.UI.Blazor.Services;

public class TuneUI : ITuneUIBackend, INotifyInitialized, IDisposable
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.init";
    private static readonly string JSPlayMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.play";
    private static readonly string JSPlayAndWaitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.playAndWait";
    private readonly DotNetObjectReference<ITuneUIBackend> _blazorRef;

    protected static readonly Dictionary<Tune, TuneInfo> Tunes = new () {
        // General actions
        { Tune.CancelReply, new (new[] { 20 }/*, "cancel-reply"*/) },
        { Tune.OpenModal, new (new[] { 20 }/*, "open-modal"*/) },
        { Tune.CloseModal, new (new[] { 20 }/*, "close-modal"*/) },
        { Tune.SelectNavbarItem, new (new[] { 20 }/*, "select-navbar-item"*/) },
        { Tune.ShowInputError, new (new[] { 80 }/*, "show-input-error"*/) },
        // Recording
        { Tune.BeginRecording, new (new[] { 100, 50, 50 }, "begin-recording") },
        { Tune.EndRecording, new (new[] { 100 }, "end-recording") },
        { Tune.RemindOfRecording, new (new[] { 20 }, "remind-of-recording") },
        // Playback
        { Tune.StartRealtimePlayback, new (new[] { 100 }/*, "start-realtime-playback"*/) },
        { Tune.StartHistoricalPlayback, new (new[] { 100 }/*, "start-historical-playback"*/) },
        { Tune.StopHistoricalPlayback, new (new[] { 20 }/*, "stop-historical-playback"*/) },
        { Tune.StopRealtimePlayback, new (new[] { 20 }/*, "stop-realtime-playback"*/) },
        // Chat UI
        { Tune.PinUnpinChat, new (new[] { 50 }/*, "pin-unpin-chat"*/) },
        // ChatMessageEditor
        { Tune.SendMessage, new (new[] { 50 }/*, "send-message"*/) },
        { Tune.EditMessage, new (new[] { 20 }/*, "edit-message"*/) },
        { Tune.ReplyMessage, new (new[] { 20 }/*, "reply-message"*/) },
        { Tune.ChangeAttachments, new (new[] { 20 }/*, "change-attachments"*/) },
        { Tune.SelectPrimaryLanguage, new (new[] { 50, 50, 50 }/*, "select-primary-language"*/) },
        { Tune.SelectSecondaryLanguage, new (new[] { 50 }/*, "select-secondary-language"*/) },
        { Tune.ShowMenu, new (new[] { 20 }/*, "show-menu"*/) },
    };

    public TuneUI(IServiceProvider services)
    {
        JS = services.JSRuntime();
        Log = services.LogFor<TuneUI>();
        _blazorRef = DotNetObjectReference.Create<ITuneUIBackend>(this);
    }

    protected virtual bool UseJsVibration => true;
    private IJSRuntime JS { get; }
    private ILogger Log { get; }

    private async ValueTask InvokeInit()
    {
        try
        {
            await JS.InvokeVoidAsync(JSInitMethod, _blazorRef, Tunes, UseJsVibration);
        }
        catch (Exception e)
        {
            Log.LogError(e, "Failed to init TuneUI");
        }
    }

    void INotifyInitialized.Initialized()
        => _ = InvokeInit();

    public virtual void Dispose()
        => _blazorRef.DisposeSilently();

    public virtual ValueTask Play(Tune tune, bool vibrate = true)
        => JS.InvokeVoidAsync(JSPlayMethod, tune, vibrate);

    public virtual ValueTask PlayAndWait(Tune tune, bool vibrate = true)
        => JS.InvokeVoidAsync(JSPlayAndWaitMethod, tune, vibrate);

    [JSInvokable]
    public virtual ValueTask OnVibrate(Tune tune)
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
    SendMessage,
    EditMessage,
    ReplyMessage,
    ChangeAttachments,
    SelectPrimaryLanguage,
    SelectSecondaryLanguage,
    ShowMenu,
}

public record TuneInfo(int[] Vibration, string Sound = "");
