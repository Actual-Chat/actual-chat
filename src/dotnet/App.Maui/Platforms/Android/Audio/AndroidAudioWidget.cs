using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using IntentExtras = ActualChat.App.Maui.Audio.AndroidAudioWidgetForegroundService.IntentExtras;

namespace ActualChat.App.Maui.Audio;

public class AndroidAudioWidget : AudioWidget
{
    private static volatile AndroidAudioWidget? _instance;
    private static bool _isShown;
    private static bool _isWakeOwned;
    private static ILogger? _log;

    private bool _isDisposed;

    private static ILogger Log => _log ??= StaticLog.For(typeof(AndroidAudioWidget));
    private static Context Context => Platform.AppContext;

    public AndroidAudioWidget(AppUIHub hub) : base(hub)
    {
        Interlocked.Exchange(ref _instance, this);
        _ = DispatchToBlazor(_ => {
            if (IsStale())
                return;

            HideImpl();
        });
    }

    public static void Pause()
    {
        // The wake session drives its own playback and offers no pause/resume, only Stop - and
        // nothing headless can re-issue ShowImpl to flip the button back to Play, so acting here
        // would strand the user on a paused stream behind a Pause button.
        if (HeadlessBlazorScope.Current is not null)
            return;

        _instance?.InvokeAction(ActionNames.Pause);
    }

    public static void Resume()
    {
        if (HeadlessBlazorScope.Current is not null)
            return;

        _instance?.InvokeAction(ActionNames.Resume);
    }

    public static void Stop()
    {
        // A headless wake session owns the FGS and the listening state, and can now have an
        // AndroidAudioWidget instance of its own - so the session decides who stops, not the instance.
        if (HeadlessBlazorScope.Current is not null) {
            WalkieTalkieWakeHandler.StopHeadlessSession();
            return;
        }

        _instance?.InvokeAction(ActionNames.Stop);
    }

    public static void Hide() => HideImpl();

    protected override void OnStateChanged(AudioWidgetState? state, AudioWidgetState? oldState)
        => _ = DispatchToBlazor(_ => {
            if (IsStale())
                return;

            if (state is null)
                HideImpl();
            else
                ShowImpl(state);
        });

    public override void Dispose()
    {
        // Published before _instance is cleared: a dispatch parked in a headless scope can resume
        // long after this scope died, and _instance may still point at it.
        Volatile.Write(ref _isDisposed, true);
        Interlocked.CompareExchange(ref _instance, null, this);
        base.Dispose();
    }

    // Protected/internal methods

    internal static void MarkForegroundServiceShown()
    {
        // Ownership is claimable only while nothing is shown: once the WebView widget owns the
        // service, a failing wake must not be able to take it down - nothing would re-show it.
        if (!Volatile.Read(ref _isShown))
            Volatile.Write(ref _isWakeOwned, true);
        Volatile.Write(ref _isShown, true);
    }

    internal static void MarkForegroundServiceHidden()
    {
        Volatile.Write(ref _isShown, false);
        Volatile.Write(ref _isWakeOwned, false);
    }

    internal static bool IsForegroundServiceWakeOwned()
        => Volatile.Read(ref _isWakeOwned);

    // Private methods

    private bool IsStale()
        => Volatile.Read(ref _isDisposed) || _instance != this;

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
        intent.PutExtra(IntentExtras.CanPause, state.CanPause);
        if (AndroidAudioWidgetForegroundService.TryStart(context, intent)) {
            Volatile.Write(ref _isShown, true);
            // The widget's own state drives the service from here on, so a wake failure must not
            // take it down - nothing would ever re-show it.
            Volatile.Write(ref _isWakeOwned, false);
        }
        else
            Log.LogWarning("ShowImpl: couldn't start the FGS (mode={Mode})", state.Mode);
    }

    private static void HideImpl()
    {
        if (!Volatile.Read(ref _isShown))
            return;

        Volatile.Write(ref _isShown, false);
        Volatile.Write(ref _isWakeOwned, false);
        AndroidAudioWidgetForegroundService.Stop(Context);
    }
}
