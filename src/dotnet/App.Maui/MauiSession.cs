using System.Net;
using System.Security.Authentication;
using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Maui;

public sealed class MauiSession
{
    private static readonly Tracer Tracer = MauiDiagnostics.Tracer[nameof(MauiSession)];
    private static readonly ILogger Log = MauiDiagnostics.LoggerFactory.CreateLogger<MauiSession>();

    public static Task RestoreOrCreate(ClientAppSettings settings)
        => Task.Run(async () => {
            using var _ = Tracer.Region(nameof(RestoreOrCreate));

            const string sessionIdStorageKey = "Fusion.SessionId";
            var session = (Session?)null;

            var storage = SecureStorage.Default;
            try {
                var storedSessionId = await storage.GetAsync(sessionIdStorageKey).ConfigureAwait(false);
                if (!storedSessionId.IsNullOrEmpty()) {
                    session = new Session(storedSessionId);
                    Log.LogInformation("Successfully read stored Session ID");
                }
                else
                    Log.LogInformation("No stored Session ID");
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to read stored Session ID");
                // ignored
                // https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
                // TODO: configure selective backup, to prevent app crashes after re-installing
                // https://learn.microsoft.com/en-us/xamarin/essentials/secure-storage?tabs=android#selective-backup
            }

            if (session == null) {
                session = new SessionFactory().CreateSession();
                await Setup(settings, session, true).ConfigureAwait(false);
                bool isSaved;
                try {
                    if (storage.Remove(sessionIdStorageKey))
                        Log.LogInformation("Removed stored Session ID");
                    await storage.SetAsync(sessionIdStorageKey, session.Id.Value).ConfigureAwait(false);
                    isSaved = true;
                }
                catch (Exception e) {
                    isSaved = false;
                    Log.LogWarning(e, "Failed to store Session ID");
                    // Ignored, see:
                    // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
                }

                if (!isSaved) {
                    Log.LogInformation("Second attempt to store Session ID");
                    try {
                        storage.RemoveAll();
                        await storage.SetAsync(sessionIdStorageKey, session.Id.Value).ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        Log.LogWarning(e, "Failed to store Session ID (second attempt)");
                        // Ignored, see:
                        // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
                    }
                }
            }
            else
                await Setup(settings, session, false).ConfigureAwait(false);

            settings.Session = session;
            return session;
        });

    private static async Task Setup(ClientAppSettings appSettings, Session session, bool isNew)
    {
        var _ = Tracer.Region(nameof(Setup));
        try {
            // Manually configure HTTP client as we don't have it configured globally at DI level
            using var httpClient = new HttpClient(new HttpClientHandler {
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                UseCookies = false,
            }, true) {
                DefaultRequestVersion = HttpVersion.Version30,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
            httpClient.DefaultRequestHeaders.Add("cookie", AppStartup.GetCookieHeader());
            if (!isNew)
                return;

            var authClientLogger = MauiDiagnostics.LoggerFactory.CreateLogger<MobileAuthClient>();
            var authClient = new MobileAuthClient(appSettings, httpClient, authClientLogger);
            await authClient.SetupSession(session).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, $"{nameof(Setup)} failed");
            throw;
        }
    }
}
