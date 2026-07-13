using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using IntentExtras = ActualChat.App.Maui.Audio.AndroidAudioWidgetForegroundService.IntentExtras;

namespace ActualChat.App.Maui.Audio;

public class AndroidAudioWidget : AudioWidget
{
    private static volatile AndroidAudioWidget? _instance;
    private static volatile bool _isShown;
    private static ILogger? _log;

    private static ILogger Log => _log ??= StaticLog.For(typeof(AndroidAudioWidget));
    private static Context Context => Platform.AppContext;

    public AndroidAudioWidget(AppUIHub hub) : base(hub)
    {
        Interlocked.Exchange(ref _instance, this);
        _ = DispatchToBlazor(_ => HideImpl());
    }

    public static void Pause() => _instance?.InvokeAction(ActionNames.Pause);
    public static void Resume() => _instance?.InvokeAction(ActionNames.Resume);
    public static void Stop()
    {
        // In a headless wake session no AndroidAudioWidget instance exists - the wake
        // handler owns the FGS and the listening state.
        if (_instance is { } instance)
            instance.InvokeAction(ActionNames.Stop);
        else
            WalkieTalkieWakeHandler.StopHeadlessSession();
    }
    public static void Hide() => HideImpl();

    protected override void OnStateChanged(AudioWidgetState? state, AudioWidgetState? oldState)
        => _ = DispatchToBlazor(_ => {
            if (_instance != this)
                return;

            if (state is null)
                HideImpl();
            else
                ShowImpl(state);
        });

    public override void Dispose()
    {
        Interlocked.CompareExchange(ref _instance, null, this);
        base.Dispose();
    }

    // Protected/internal methods

    internal static void MarkForegroundServiceShown() => _isShown = true;
    internal static void MarkForegroundServiceHidden() => _isShown = false;

    // Private methods

    private static void ShowImpl(AudioWidgetState state)
    {
        var context = Context;
        var intent = new Intent(context, typeof(AndroidAudioWidgetForegroundService));
        intent.SetAction(AndroidAudioWidgetForegroundService.ActionShow);
        intent.PutExtra(IntentExtras.Mode, (int)state.Mode);
        intent.PutExtra(IntentExtras.ChatId, state.Chat.Id.Value);
        intent.PutExtra(IntentExtras.ChatTitle, state.Chat.Title);
        intent.PutExtra(IntentExtras.ChatPicUri, state.Chat.PicUrl);
        intent.PutExtra(IntentExtras.ExtraChatCount, state.Chat.ExtraChatCount);
        intent.PutExtra(IntentExtras.IsPaused, state.IsPaused);
        try {
            context.StartForegroundService(intent);
            AndroidAudioWidgetForegroundService.OnStartRequested();
            _isShown = true;
        }
        catch (Exception e) {
            // Starting a mic FGS from the background is blocked (ForegroundServiceStartNotAllowedException):
            // this surfaces if the accept-over-lock-screen path lacks a foreground-visible activity.
            Log.LogError(e, "StartForegroundService failed (mode={Mode})", state.Mode);
        }
    }

    private static void HideImpl()
    {
        if (!_isShown)
            return;

        _isShown = false;
        AndroidAudioWidgetForegroundService.Stop(Context);
    }
}
