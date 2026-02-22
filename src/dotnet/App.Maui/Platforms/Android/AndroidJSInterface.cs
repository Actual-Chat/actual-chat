using Android.Webkit;
using Java.Interop;
using JObject = Java.Lang.Object;

namespace ActualChat.App.Maui;

public class AndroidJSInterface : JObject
{
    private readonly Android.Webkit.WebView _webView;

    public event Action<string>? MessageReceived;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AndroidJSInterface))]
    // ReSharper disable once ConvertToPrimaryConstructor
    public AndroidJSInterface(Android.Webkit.WebView webView)
        => _webView = webView;

    [JavascriptInterface]
    [Export("postMessage")]
    [UnconditionalSuppressMessage("Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "All members of AndroidJSInterface are preserved with DynamicDependencyAttribute on constructor")]
    public void OnPostMessage(string data)
        => _ = MainThread.InvokeOnMainThreadAsync(() => {
            MessageReceived?.Invoke(data);
        });
}
