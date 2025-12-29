using Microsoft.Maui.ApplicationModel;

namespace ActualChat.App.Maui.IosShareExt.Services;

public static class UIKitExt
{
    public static NSExtensionContext ExtensionContext => WindowStateManager.Default.GetCurrentUIViewController()
        .Require()
        .ExtensionContext.Require();
}
