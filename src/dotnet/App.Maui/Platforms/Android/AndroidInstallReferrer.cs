using ActualChat.UI.Blazor.Services;
using Xamarin.Android.InstallReferrer.Api;

namespace ActualChat.App.Maui;

public sealed class AndroidInstallReferrer : IInstallReferrer
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public async Task<string?> GetQuery(CancellationToken cancellationToken)
    {
        var client = InstallReferrerClient.NewBuilder(Platform.AppContext).Build();
        var listener = new StateListener();
        client.StartConnection(listener);
        try {
            var responseCode = await listener.WhenSetUp
                .WaitAsync(ConnectTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (responseCode != InstallReferrerClient.InstallReferrerResponse.Ok)
                return null;

            return client.InstallReferrer.InstallReferrer;
        }
        finally {
            client.EndConnection();
        }
    }

    // Nested types

    private sealed class StateListener : Java.Lang.Object, IInstallReferrerStateListener
    {
        private readonly TaskCompletionSource<int> _whenSetUp = TaskCompletionSourceExt.New<int>();

        public Task<int> WhenSetUp => _whenSetUp.Task;

        public void OnInstallReferrerSetupFinished(int responseCode)
            => _whenSetUp.TrySetResult(responseCode);

        public void OnInstallReferrerServiceDisconnected()
            => _whenSetUp.TrySetResult(InstallReferrerClient.InstallReferrerResponse.ServiceDisconnected);
    }
}
