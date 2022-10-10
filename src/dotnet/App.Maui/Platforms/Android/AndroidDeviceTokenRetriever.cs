namespace ActualChat.App.Maui;

internal class AndroidDeviceTokenRetriever : IDeviceTokenRetriever
{
    public async Task<string?> GetDeviceToken(CancellationToken cancellationToken)
    {
        var javaString = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.String>().ConfigureAwait(true);
        return javaString.ToString();
    }
}
