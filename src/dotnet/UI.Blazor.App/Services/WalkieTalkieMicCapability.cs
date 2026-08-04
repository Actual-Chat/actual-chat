namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Host hook raised around a walkie-talkie reply's microphone. Android answers it by re-issuing
/// its foreground service with the microphone type, which must happen synchronously inside the
/// media-button or gesture callback that opens the reply.
/// </summary>
public static class WalkieTalkieMicCapability
{
    private static Action<bool>? _handler;
    private static ILogger Log => field ??= StaticLog.For(typeof(WalkieTalkieMicCapability));

    public static void SetHandler(Action<bool> handler)
        => Volatile.Write(ref _handler, handler);

    public static void ResetHandler(Action<bool> handler)
        => Interlocked.CompareExchange(ref _handler, null, handler);

    public static void Request(bool isMicrophoneNeeded)
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
