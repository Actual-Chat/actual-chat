using Android.Content;

namespace ActualChat.App.Maui;

[BroadcastReceiver(Exported = false)]
public class AlarmReceiver : BroadcastReceiver
{
    private ILogger? _log;
    private ILogger Log => _log ??= StaticLog.For<AlarmReceiver>();

    public override void OnReceive(Context? context, Intent? intent)
    {
        try {
            Log.LogInformation("-> OnReceive");
            if (intent!.Action.OrdinalStartsWith(ChatAttentionService.AlarmActionPrefix))
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
