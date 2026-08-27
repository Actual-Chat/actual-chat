namespace ActualChat.UI.Blazor.Diagnostics;

/// <summary>
/// Temporary chat-switch instrumentation. <see cref="TryStart"/> opens a trace on every navigation
/// to a chat URL, and every <see cref="Mark(string)"/> until <see cref="Window"/> expires is logged
/// with its offset from that start, so one switch reads as a single ordered timeline.
/// </summary>
public static class ChatSwitchTrace
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(15);
    private static readonly Lock Lock = new();
    private static CpuTimestamp _startedAt;
    private static string _chatSid = "";
    private static int _index;

    public static bool IsEnabled { get; set; } = true;
    private static ILogger Log => field ??= StaticLog.For(typeof(ChatSwitchTrace));

    public static void TryStart(string url, string source)
    {
        if (!IsEnabled)
            return;

        var localUrl = new LocalUrl(url);
        if (!localUrl.IsChat(out var chatId))
            return;

        var chatSid = chatId.Value;
        int index;
        lock (Lock) {
            if (chatSid == _chatSid && _startedAt.Elapsed < TimeSpan.FromSeconds(1))
                return; // A re-navigation to the same chat within one switch isn't a new switch

            index = ++_index;
            _startedAt = CpuTimestamp.Now;
            _chatSid = chatSid;
        }
        Log.LogInformation("CST#{Index} +   0.0ms START ({Source}) -> #{ChatId}", index, source, chatSid);
    }

    public static void Mark(string stage)
        => Mark(stage, null);

    public static void Mark(string stage, string? detail)
    {
        if (!IsEnabled)
            return;

        int index;
        double elapsedMs;
        lock (Lock) {
            if (_index == 0)
                return;

            var elapsed = _startedAt.Elapsed;
            if (elapsed > Window)
                return;

            index = _index;
            elapsedMs = elapsed.TotalMilliseconds;
        }
        Log.LogInformation("CST#{Index} +{Elapsed}ms {Stage} {Detail}",
            index, elapsedMs.ToString("F1").PadLeft(6), stage, detail ?? "");
    }
}
