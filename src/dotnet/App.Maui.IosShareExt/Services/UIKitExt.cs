using Microsoft.Maui.ApplicationModel;

namespace ActualChat.App.Maui.IosShareExt.Services;

public static class UIKitExt
{
    public static NSExtensionContext ExtensionContext => Platform.GetCurrentUIViewController()
        .Require()
        .ExtensionContext.Require();

    public static Task CloseApp(CancellationToken cancellationToken = default)
        => MainThread.InvokeOnMainThreadAsync(() => ExtensionContext.CompleteRequestAsync([])).WaitAsync(cancellationToken);
}
