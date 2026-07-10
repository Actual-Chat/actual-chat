using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using IntentExtras = ActualChat.App.Maui.Audio.AndroidAudioWidgetForegroundService.IntentExtras;

namespace ActualChat.App.Maui.Audio;

public class AndroidAudioWidget : AudioWidget
{
    private static volatile AndroidAudioWidget? _instance;
    private static volatile bool _isShown;

    private static Context Context => Platform.AppContext;

    public AndroidAudioWidget(AppUIHub hub) : base(hub)
    {
        Interlocked.Exchange(ref _instance, this);
        _ = DispatchToBlazor(_ => HideImpl());
    }

    public static void Pause() => _instance?.InvokeAction(ActionNames.Pause);
    public static void Resume() => _instance?.InvokeAction(ActionNames.Resume);
    public static void Stop() => _instance?.InvokeAction(ActionNames.Stop);
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
        context.StartForegroundService(intent);
        AndroidAudioWidgetForegroundService.OnStartRequested();
        _isShown = true;
    }

    private static void HideImpl()
    {
        if (!_isShown)
            return;

        _isShown = false;
        AndroidAudioWidgetForegroundService.Stop(Context);
    }
}
