using ActualChat.Notification.UI.Blazor;
using Android.Gms.Extensions;
using Firebase.Messaging;

namespace ActualChat.App.Maui;

internal class AndroidDeviceTokenRetriever : IDeviceTokenRetriever
{
    private ILogger<AndroidDeviceTokenRetriever> Log { get; }

    public AndroidDeviceTokenRetriever(ILogger<AndroidDeviceTokenRetriever> log)
        => Log = log;

    public async Task<string?> GetDeviceToken(CancellationToken cancellationToken)
    {
        try {
            var javaString = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.String>().ConfigureAwait(true);
            var token = javaString.ToString();
            Log.LogDebug("FCM token is \'{Token}\'", token);
            return token;
        }
        catch(Exception e) {
            Log.LogWarning(e, "Failed to get FCM token");
            return null;
        }
    }
}
