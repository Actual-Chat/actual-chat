using ActualChat.Notification.UI.Blazor;
using Android.Gms.Extensions;
using Android.Util;
using Firebase.Messaging;

namespace ActualChat.App.Maui;

internal class AndroidDeviceTokenRetriever : IDeviceTokenRetriever
{
    public async Task<string?> GetDeviceToken(CancellationToken cancellationToken)
    {
        try {
            var javaString = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.String>().ConfigureAwait(true);
            var token = javaString.ToString();
            Log.Debug(AndroidConstants.LogTag, $"FCM token is '{token}'");
            return token;
        }
        catch(Exception e) {
            Log.Warn(AndroidConstants.LogTag, Java.Lang.Throwable.FromException(e), "Failed to get FCM token");
            return null;
        }
    }
}
