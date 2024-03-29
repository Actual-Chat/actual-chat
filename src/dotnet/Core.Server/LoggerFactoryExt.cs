namespace ActualChat;

public static class LoggerFactoryExt
{
    public static string GetCategoryName(Type type)
    {
        var categoryExtractor = (CategoryExtractorLogger)CategoryExtractorLoggerFactory.Instance.CreateLogger(type);
        return categoryExtractor.CategoryName;
    }

    public static ILogger CreateLogger(this ILoggerFactory loggerFactory, Type type, string suffix)
    {
        var prefix = GetCategoryName(type);
        return loggerFactory.CreateLogger(prefix + suffix);
    }

    // Nested types

    private class CategoryExtractorLoggerFactory : ILoggerFactory
    {
        public static readonly CategoryExtractorLoggerFactory Instance = new();

        public void Dispose()
        { }

        public ILogger CreateLogger(string categoryName)
            => new CategoryExtractorLogger(categoryName);

        public void AddProvider(ILoggerProvider provider)
        { }
    }

    private class CategoryExtractorLogger(string categoryName) : ILogger
    {
        public string CategoryName { get; } = categoryName;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        { }

        public bool IsEnabled(LogLevel logLevel)
            => false;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => null;
    }
}
