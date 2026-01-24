using Microsoft.Maui.ApplicationModel;

namespace ActualChat.App.Maui.IosShareExt.Services;

public static class UIKitExt
{
    public static NSExtensionContext ExtensionContext => Platform.GetCurrentUIViewController()
        .Require()
        .ExtensionContext.Require();
}
