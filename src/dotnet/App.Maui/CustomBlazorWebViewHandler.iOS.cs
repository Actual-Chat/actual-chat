using ObjCRuntime;
using UIKit;
using WebKit;

namespace ActualChat.App.Maui;

public partial class CustomBlazorWebViewHandler
{
    private static int _accessoryViewSwizzled;

    static CustomBlazorWebViewHandler()
        => BlazorWebViewMapper.AppendToMapping(nameof(MauiSettings.SplashBackgroundColor), (handler, _) => {
            var webView = handler.PlatformView;
            webView.Opaque = false;
            webView.BackgroundColor = UIColor.Clear;
            webView.ScrollView.BackgroundColor = UIColor.Clear;
            if (OperatingSystem.IsIOSVersionAtLeast(15))
                webView.UnderPageBackgroundColor = UIColor.FromRGB(
                    MauiSettings.SplashBackgroundColor.Red,
                    MauiSettings.SplashBackgroundColor.Green,
                    MauiSettings.SplashBackgroundColor.Blue);
        });

    protected override WKWebView CreatePlatformView()
    {
        var webView = base.CreatePlatformView();
        TryRemoveInputAccessoryView();
        return webView;
    }

    // WKWebView attaches an inputAccessoryView (prev/next/Done bar) above the
    // system keyboard via its private WKContentView class. There is no public
    // API to disable it, so we override the getter at the ObjC runtime level
    // to return nil. Safe to run multiple times; the swizzle is process-wide.
    private void TryRemoveInputAccessoryView()
    {
        if (Interlocked.Exchange(ref _accessoryViewSwizzled, 1) == 1)
            return;

        try {
            var contentClass = Class.GetHandle("WKContentView");
            if (contentClass == IntPtr.Zero) {
                Log.LogWarning("WKContentView class not found; input accessory view not removed");
                return;
            }

            var selector = Selector.GetHandle("inputAccessoryView");
            unsafe {
                delegate* unmanaged<IntPtr, IntPtr, IntPtr> imp = &GetNullInputAccessoryView;
                class_replaceMethod(contentClass, selector, (IntPtr)imp, "@@:");
            }
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to remove WKWebView input accessory view");
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr GetNullInputAccessoryView(IntPtr self, IntPtr selector) => IntPtr.Zero;

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "class_replaceMethod")]
    private static extern IntPtr class_replaceMethod(IntPtr cls, IntPtr name, IntPtr imp, string types);
}
