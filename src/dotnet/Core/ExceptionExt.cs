using System.Runtime.ExceptionServices;

namespace ActualChat;

public static class ExceptionExt
{
    public static Exception LogError(this Exception error, ILogger? log)
        => error.LogError(log, error);
    public static T LogError<T>(this Exception error, ILogger? log, T replacement)
    {
        ExceptionDispatchInfo.SetCurrentStackTrace(error);
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        log?.LogError(error, error.Message);
        return replacement;
    }

    public static Exception LogWarning(this Exception error, ILogger? log)
        => error.LogWarning(log, error);
    public static T LogWarning<T>(this Exception error, ILogger? log, T result)
    {
        ExceptionDispatchInfo.SetCurrentStackTrace(error);
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        log?.LogWarning(error, error.Message);
        return result;
    }
}
