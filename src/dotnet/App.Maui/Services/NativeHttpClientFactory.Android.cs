using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using Android.App;
using Java.Util.Concurrent;
using Xamarin.Android.Net;

namespace ActualChat.App.Maui.Services;

public partial class NativeHttpClientFactory
{
    private partial HttpMessageHandler? CreatePlatformMessageHandler()
        => new CronetMessageHandler(Services.GetRequiredService<IExecutorService>());
}
