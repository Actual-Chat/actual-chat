using Android.Content;

namespace ActualChat.App.Maui;

[BroadcastReceiver(Exported = false)]
public class AlarmReceiver : BroadcastReceiver
{
    private ILogger Log => field ??= StaticLog.For<AlarmReceiver>();

    public override void OnReceive(Context? context, Intent? intent)
    {
        try {
            Log.LogInformation("-> OnReceive");
            if ((intent!.Action ?? "").StartsWith(ChatAttentionService.AlarmActionPrefix))
                ChatAttentionService.Instance.OnHandleIntent(intent);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to process broadcast");
        }
        finally {
            Log.LogInformation("<- OnReceive");
        }
    }
}
