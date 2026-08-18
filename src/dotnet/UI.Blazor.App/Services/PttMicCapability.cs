namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Reference-counted host hook for a PTT reply's microphone. Android answers it by
/// re-issuing its foreground service with the microphone type, which must happen synchronously
/// inside the media-button or gesture callback that opens the reply.
/// </summary>
public static class PttMicCapability
{
    private static readonly Lock Lock = new();
    private static readonly HashSet<object> Holders = new(ReferenceEqualityComparer.Instance);
    private static Action<bool>? _handler;
    private static Action? _blockedHandler;
    private static ILogger Log => field ??= StaticLog.For(typeof(PttMicCapability));

    public static void SetHandler(Action<bool> handler)
        => Volatile.Write(ref _handler, handler);

    public static void ResetHandler(Action<bool> handler)
        => Interlocked.CompareExchange(ref _handler, null, handler);

    public static void SetBlockedHandler(Action handler)
        => Volatile.Write(ref _blockedHandler, handler);

    public static void ResetBlockedHandler(Action handler)
        => Interlocked.CompareExchange(ref _blockedHandler, null, handler);

    public static void ReportBlocked()
    {
        // The host's job, not ours: a permission prompt needs a foregrounded activity, and a
        // headless reply has no way to reach one except through a notification.
        if (Volatile.Read(ref _blockedHandler) is not { } handler)
            return;

        try {
            handler.Invoke();
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't report the blocked microphone");
        }
    }

    public static Task HoldWhile(Func<Task> action)
    {
        // The hold lands before action starts, so the raise still happens inside the caller's own
        // synchronous dispatch - and it covers every failure path action itself returns through.
        var holder = new object();
        Hold(holder);
        return Run();

        async Task Run() {
            try {
                await action.Invoke().ConfigureAwait(false);
            }
            finally {
                Release(holder);
            }
        }
    }

    public static void Hold(object holder)
    {
        bool hasRaised;
        lock (Lock)
            hasRaised = Holders.Add(holder) && Holders.Count == 1;
        if (hasRaised)
            Invoke(true);
    }

    public static void Release(object holder)
    {
        bool hasDropped;
        lock (Lock)
            hasDropped = Holders.Remove(holder) && Holders.Count == 0;
        if (hasDropped)
            Invoke(false);
    }

    // Private methods

    private static void Invoke(bool isMicrophoneNeeded)
    {
        if (Volatile.Read(ref _handler) is not { } handler)
            return;

        try {
            handler.Invoke(isMicrophoneNeeded);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't change the microphone capability to {IsNeeded}", isMicrophoneNeeded);
        }
    }
}
