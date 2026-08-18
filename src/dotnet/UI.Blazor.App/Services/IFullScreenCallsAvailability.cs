namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// What currently stops an incoming call from taking over the screen, if anything.
/// The two gates are independent: a Xiaomi device on Android 14+ can be blocked by either.
/// </summary>
public enum CallScreenGate
{
    None = 0,
    // MIUI/HyperOS app op, the only gate below Android 14
    LockScreenWindow,
    // Android 14+ USE_FULL_SCREEN_INTENT special app access
    FullScreenIntent,
}

/// <summary>
/// Whether an incoming call may take over the screen, and where to send the user to fix it.
/// </summary>
public interface IFullScreenCallsAvailability
{
    Task<CallScreenGate> GetBlockedGate(CancellationToken cancellationToken = default);
    Task OpenSettings(CallScreenGate gate, CancellationToken cancellationToken = default);
}

public sealed class DefaultFullScreenCallsAvailability : IFullScreenCallsAvailability
{
    public Task<CallScreenGate> GetBlockedGate(CancellationToken cancellationToken = default)
        => Task.FromResult(CallScreenGate.None);

    public Task OpenSettings(CallScreenGate gate, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
