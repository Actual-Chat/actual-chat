using Android.Content;
using Android.Webkit;
using Java.Interop;
using JObject = Java.Lang.Object;

namespace ActualChat.App.Maui;

#pragma warning disable CA1822 // Can be static

public class AndroidJSInterface : JObject
{
    private readonly Android.Webkit.WebView _webView;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AndroidJSInterface))]
    // ReSharper disable once ConvertToPrimaryConstructor
    public AndroidJSInterface(Android.Webkit.WebView webView)
        => _webView = webView;

    public event Action<string> MessageReceived = _ => { };

    [JavascriptInterface]
    [Export("postMessage")]
    [UnconditionalSuppressMessage("Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "All members of AndroidJSInterface are preserved with DynamicDependencyAttribute on constructor")]
    public void OnPostMessage(string data)
        => _webView.Post(() => {
            MessageReceived.Invoke(data);
        });

    [JavascriptInterface]
    [Export("writeTextToClipboard")]
    [UnconditionalSuppressMessage("Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "All members of AndroidJSInterface are preserved with DynamicDependencyAttribute on constructor")]
    public void WriteTextToClipboard(string? newClipText)
        // ClipboardManager must be accessed on the main thread
        => _webView.Post(() => {
            var clipboard = (ClipboardManager)_webView.Context!.GetSystemService(Context.ClipboardService)!;
            clipboard.Text = newClipText;
        });

    [JavascriptInterface]
    [Export("readTextFromClipboard")]
    [UnconditionalSuppressMessage("Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "All members of AndroidJSInterface are preserved with DynamicDependencyAttribute on constructor")]
    public string? ReadTextFromClipboard()
    {
        // ClipboardManager must be accessed on the main thread
        var resultSource = new TaskCompletionSource<string?>();
        _webView.Post(() => {
            try {
                var clipboard = (ClipboardManager)_webView.Context!.GetSystemService(Context.ClipboardService)!;
                resultSource.SetResult(clipboard.Text);
            }
            catch (Exception e) {
                resultSource.SetException(e);
            }
        });
        return resultSource.Task.GetAwaiter().GetResult();
    }
}
