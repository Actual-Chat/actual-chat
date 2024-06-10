using Android.Content;

namespace ActualChat.App.Maui;

[BroadcastReceiver(Exported = false)]
public class AlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent!.Action.OrdinalStartsWith(ChatAttentionService.AlarmActionPrefix))
            ChatAttentionService.Instance.OnHandleIntent(intent);
    }
}
