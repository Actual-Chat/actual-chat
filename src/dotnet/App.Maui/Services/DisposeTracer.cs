namespace ActualChat.App.Maui.Services;

internal class DisposeTracer : IDisposable
{
    public void Dispose()
    {
        var stackTrace = Environment.StackTrace;

#if ANDROID
        // I am trying to figure out when container is disposed.
        Android.Util.Log.Debug(AndroidConstants.LogTag, $"Blazor app scoped container is being disposed. StackTrace:" + Environment.NewLine + stackTrace);
#endif
    }
}
