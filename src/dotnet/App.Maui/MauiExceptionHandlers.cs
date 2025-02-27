using ActualChat.UI.Blazor.App;
using Android.Runtime;

namespace ActualChat.App.Maui;

public static class MauiExceptionHandlers
{
    [field: AllowNull, MaybeNull]
    private static ILogger Log => field ??= StaticLog.For(typeof(MauiExceptionHandlers));

    public static void Use()
    {
        // Useful: list of all MAUI exception handling endpoints:
        // - https://gist.github.com/mattjohnsonpint/7b385b7a2da7059c4a16562bc5ddb3b7

#if true
        // Enable FCE in Release to add breadcrumbs to crashlytics. It's also enabled for Debug build from ClientStartup.Initialize.
        FirstChanceExceptionLogger.Use();
#endif

        AppDomain.CurrentDomain.UnhandledException += (sender, args) => {
            var logLevel = args.IsTerminating ? LogLevel.Critical : LogLevel.Error;
            if (args.ExceptionObject is Exception e)
                Log.Log(logLevel, e, "OnUnhandledException: {Title}, IsTerminating: {IsTerminating}",
                    e.GetType().GetName(),
                    args.IsTerminating);
            else
                Log.Log(logLevel, "OnUnhandledException: {Title}, IsTerminating: {IsTerminating}\n{Exception}",
                    args.ExceptionObject.GetType().GetName(),
                    args.IsTerminating,
                    args.ExceptionObject);
        };

#if ANDROID
        AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) => {
            // There is a number of bugs in MAUI causing it to use Java object proxies after they were disconnected,
            // see e.g. https://github.com/dotnet/maui/issues/28051
            // And typically it's fully safe to simply suppress such exceptions.
            // Or at least, it's definitely better than seeing an app crash.
            var e = args.Exception;
            if (e is ObjectDisposedException) {
                var stackTrace = e.StackTrace ?? "";
                if (stackTrace.Contains("Java.Interop.JniPeerMembers.AssertSelf"))
                    args.Handled = true;
            }
            var logLevel = args.Handled ? LogLevel.Error : LogLevel.Critical;
            Log.Log(logLevel, e,
                "AndroidEnvironment.UnhandledExceptionRaiser: {Title}, Handled: {Handled}",
                e.GetType().GetName(),
                args.Handled);
        };
#endif
    }
}
