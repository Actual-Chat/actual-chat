namespace ActualChat.Logging;

public static class LoggerExt
{
    public static ILogger Prefixed(this ILogger log, [CallerMemberName] string operationName = "")
        => new OperationLogger(log, operationName);

    private class OperationLogger : ILogger
    {
        private ILogger Log { get; }
        private string OperationName { get; }

        public OperationLogger(ILogger log, string operationName)
        {
            Log = log;
            OperationName = operationName;
        }

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        // TODO: cache formatter
            => Log.Log(logLevel, eventId, state, exception, (state1, exception1) => $"{OperationName}: {formatter(state1, exception1)}");

        public bool IsEnabled(LogLevel logLevel)
            => Log.IsEnabled(logLevel);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => Log.BeginScope(state);
    }
}
