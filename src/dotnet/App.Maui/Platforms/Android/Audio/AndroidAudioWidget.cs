using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using IntentExtras = ActualChat.App.Maui.Audio.AndroidAudioWidgetForegroundService.IntentExtras;

namespace ActualChat.App.Maui.Audio;

public class AndroidAudioWidget(IServiceProvider services) : AudioWidget(services)
{
    private static AndroidAudioWidget? _instance;

    private static Context Context => Platform.AppContext;
    private bool _isShown;

    public static void Pause() => _instance?.InvokeAction(ActionNames.Pause);
    public static void Resume() => _instance?.InvokeAction(ActionNames.Resume);
    public static void Stop() => _instance?.InvokeAction(ActionNames.Stop);
    public static void Hide() => _instance?.HideImpl();

    protected override void OnStateChanged(AudioWidgetState? state, AudioWidgetState? oldState)
    {
        _instance = this;
        if (state is null)
            HideImpl();
        else
            ShowImpl(state);
    }

    // Private methods

    private void ShowImpl(AudioWidgetState state)
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
        _isShown = true;
    }

    private void HideImpl()
    {
        if (!_isShown)
            return;

        _isShown = false;
        var context = Context;
        var intent = new Intent(context, typeof(AndroidAudioWidgetForegroundService));
        context.StopService(intent);
    }
}
