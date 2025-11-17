using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using IntentExtras = ActualChat.App.Maui.Audio.AudioWidgetForegroundService.IntentExtras;

namespace ActualChat.App.Maui.Audio;

public static class AudioWidgetController
{
    private static Context Context => Platform.AppContext;
    private static bool _shown;
    private static AudioWidgetSession? _audioSession;

    public static void OnAudioSessionStateChanged(AudioWidgetSession audioSession)
    {
        _audioSession = audioSession;
        if (audioSession.State is null)
            Hide();
        else
            Show(audioSession.State);
    }

    public static void Pause()
        => InvokeAction(AudioWidgetSession.Actions.Pause);

    public static void Resume()
        => InvokeAction(AudioWidgetSession.Actions.Resume);

    public static void Stop()
        => InvokeAction(AudioWidgetSession.Actions.Stop);

    private static void Show(AudioWidgetSessionState state)
    {
        var context = Context;
        var intent = new Intent(context, typeof(AudioWidgetForegroundService));
        intent.SetAction(AudioWidgetForegroundService.ActionShow);
        intent.PutExtra(IntentExtras.Mode, (int)state.Mode);
        intent.PutExtra(IntentExtras.ChatId, state.Chat.Id.Value);
        intent.PutExtra(IntentExtras.ChatTitle, state.Chat.Title);
        intent.PutExtra(IntentExtras.ChatPicUri, state.Chat.PicUri);
        intent.PutExtra(IntentExtras.ExtraChatCount, state.Chat.ExtraChatCount);
        intent.PutExtra(IntentExtras.IsPaused, state.IsPaused);
        context.StartForegroundService(intent);
        _shown = true;
    }

    private static void Hide()
    {
        if (!_shown)
            return;

        _shown = false;

        var context = Context;
        var intent = new Intent(context, typeof(AudioWidgetForegroundService));
        context.StopService(intent);
    }

    private static void InvokeAction(string actionName)
        => _audioSession?.InvokeAction(actionName);
}
