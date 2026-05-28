namespace ActualChat.Maui;

public static class UIApplicationExt
{
    extension(UIApplication application)
    {
        // Hold around suspend-critical work (e.g. SQLite WAL checkpoint) to avoid 0xdead10cc.
        public IDisposable BeginBackgroundTaskScope(string taskName)
            => new BackgroundTaskScope(application, taskName);
    }

    private sealed class BackgroundTaskScope : IDisposable
    {
        private readonly UIApplication _application;
        private readonly Lock _lock = new();
        private readonly nint _taskId;
        private bool _ended;

        public BackgroundTaskScope(UIApplication application, string taskName)
        {
            _application = application;
            _taskId = application.BeginBackgroundTask(taskName, End);
        }

        public void Dispose() => End();

        private void End()
        {
            lock (_lock) {
                if (_ended)
                    return;

                _ended = true;
                if (_taskId != UIApplication.BackgroundTaskInvalid)
                    _application.EndBackgroundTask(_taskId);
            }
        }
    }
}
