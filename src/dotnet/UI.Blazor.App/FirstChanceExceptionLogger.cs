using System.Runtime.ExceptionServices;

namespace ActualChat.UI.Blazor.App;

public static class FirstChanceExceptionLogger
{
    private static readonly ILogger Log = StaticLog.Factory.CreateLogger("FCE");

    public static void Use()
        => AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        var error = e.Exception;
        if (error is OperationCanceledException)
            return; // This one has to be skipped

        var withStackTrace = true;
        // Handles System.IO.FileNotFoundException and Java.IO.FileNotFoundException exceptions as well
        if (OrdinalEquals(error.GetType().Name, nameof(FileNotFoundException)))
            if (error.Message.OrdinalStartsWith("wwwroot/"))
                withStackTrace = false;

        LogInternal(error, withStackTrace);

        // NOTE(DF): Perhaps it's redundant and inner exception has been already logged with FCE earlier.
        // But I saw TargetInvocationException in Crashes reports in 'Google console/Android Vitals'
        //  and wanted to be sure that it's been logged to find details of a crash.
        if (error is TargetInvocationException { InnerException: { } innerException })
            LogInternal(innerException);
    }

    private static void LogInternal(Exception error, bool withStackTrace = true)
    {
        if (!withStackTrace) {
            Log.LogWarning("{Type}, {Message}", error.GetType().Name, error.Message);
            return;
        }

        var stackTrace = error.StackTrace ?? new StackTrace().ToString();
        Log.LogWarning("{Type}, {Message}\r\n{StackTrace}", error.GetType().Name, error.Message, stackTrace);
    }
}
