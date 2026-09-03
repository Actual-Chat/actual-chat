using Microsoft.Maui.Platforms.MacOS.Handlers;
using WebKit;

namespace ActualChat.App.Maui;

// TODO(maui-labs): delete once the labs BlazorWebViewHandler raises a BlazorWebViewInitializing-
// style event - MacOSCustomBlazorWebViewHandler.CreatePlatformView goes with it.
/// <summary>
/// Reflection access to the private pieces of the maui-labs <see cref="BlazorWebViewHandler"/>
/// that a subclassed <c>CreatePlatformView</c> has to reuse. Every lookup fails fast, so a labs
/// package update that renames them is caught at first use rather than as a dead WebView.
/// </summary>
public static class LabsBlazorWebViewHandlerExt
{
    private static readonly Type HandlerType = typeof(BlazorWebViewHandler);
    private static readonly Type ScriptMessageHandlerType = HandlerType
        .GetNestedType("WebViewScriptMessageHandler", BindingFlags.NonPublic)
        ?? throw StandardError.Constraint("No 'WebViewScriptMessageHandler' nested type in BlazorWebViewHandler - maui-labs renamed it?");
    private static readonly Type SchemeHandlerType = HandlerType
        .GetNestedType("SchemeHandler", BindingFlags.NonPublic)
        ?? throw StandardError.Constraint("No 'SchemeHandler' nested type in BlazorWebViewHandler - maui-labs renamed it?");
    private static readonly MethodInfo MessageReceivedMethod = HandlerType
        .GetMethod("MessageReceived", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw StandardError.Constraint("No 'MessageReceived' method in BlazorWebViewHandler - maui-labs renamed it?");

    public static string BlazorInitScript { get; } =
        HandlerType.GetField("BlazorInitScript", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetRawConstantValue() as string
        ?? throw StandardError.Constraint("No 'BlazorInitScript' constant in BlazorWebViewHandler - maui-labs renamed it?");

    public static IWKScriptMessageHandler NewWebViewScriptMessageHandler(this BlazorWebViewHandler handler)
    {
        var messageReceived = MessageReceivedMethod.CreateDelegate<Action<Uri, string>>(handler);
        return (IWKScriptMessageHandler)Activator.CreateInstance(ScriptMessageHandlerType, messageReceived)!;
    }

    public static IWKUrlSchemeHandler NewAppSchemeHandler(this BlazorWebViewHandler handler)
        => (IWKUrlSchemeHandler)Activator.CreateInstance(SchemeHandlerType, handler)!;
}
