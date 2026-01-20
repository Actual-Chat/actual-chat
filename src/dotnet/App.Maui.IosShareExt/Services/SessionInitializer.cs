using ActualChat.Maui;
using ActualChat.Security;

namespace ActualChat.App.Maui.IosShareExt.Services;

public class SessionInitializer(TrueSessionResolver trueSessionResolver, ILogger<SessionInitializer> log)
{
    public async Task SetSession(CancellationToken cancellationToken = default)
    {
        try {
            var sessionId = await IosSharedSecureStorage.Default.GetAsync("Fusion.SessionId")
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (sessionId.IsNullOrEmpty()) {
                log.LogCritical("No session id found.");
                return;
            }
            trueSessionResolver.Session = new Session(sessionId);
        }
        catch (Exception e) {
            log.LogCritical(e, "Failed to set session.");
        }
    }
}
