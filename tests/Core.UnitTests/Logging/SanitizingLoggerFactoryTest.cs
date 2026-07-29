using ActualChat.Logging;

namespace ActualChat.Core.UnitTests.Logging;

public class SanitizingLoggerFactoryTest
{
    [Fact]
    public void SensitiveArgIsMasked()
    {
        var (loggerFactory, messages) = CreateLoggerFactory(useSanitizing: true);
        var log = loggerFactory.CreateLogger("Test");

        log.LogInformation("Text: {Text}", "Hello world".ToPrivate());

        messages.Should().ContainSingle()
            .Which.Should().Contain("<<He* [8-15]>>");
    }

    [Fact]
    public void SensitiveArgPassesThroughWithoutSanitizing()
    {
        var (loggerFactory, messages) = CreateLoggerFactory(useSanitizing: false);
        var log = loggerFactory.CreateLogger("Test");

        log.LogInformation("Text: {Text}", "Hello world".ToPrivate());

        messages.Should().ContainSingle()
            .Which.Should().Contain("Hello world");
    }

    [Fact]
    public void NonSensitiveArgIsUnchanged()
    {
        var (loggerFactory, messages) = CreateLoggerFactory(useSanitizing: true);
        var log = loggerFactory.CreateLogger("Test");

        log.LogInformation("Id: {Id}", 42);

        messages.Should().ContainSingle()
            .Which.Should().Contain("42");
    }

    [Fact]
    public void SanitizerIsNotActiveOutsideLogCall()
    {
        var (loggerFactory, _) = CreateLoggerFactory(useSanitizing: true);
        var log = loggerFactory.CreateLogger("Test");

        Sanitizer.IsActive.Should().BeFalse();
        log.LogInformation("test");
        Sanitizer.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsEnabledDelegatesToInner()
    {
        var (loggerFactory, _) = CreateLoggerFactory(useSanitizing: true, minLevel: LogLevel.Warning);
        var log = loggerFactory.CreateLogger("Test");

        log.IsEnabled(LogLevel.Information).Should().BeFalse();
        log.IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public void FactoryCreatesDistinctLoggers()
    {
        var innerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new InMemoryLoggerProvider()));
        var factory = new SanitizingLoggerFactory(innerFactory);

        var log1 = factory.CreateLogger("Cat1");
        var log2 = factory.CreateLogger("Cat2");

        log1.Should().NotBeSameAs(log2);
    }

    [Fact]
    public void LegacyUsageIncludesClientVersionAtWarning()
    {
        // arrange
        var (loggerFactory, messages) = CreateLoggerFactory(useSanitizing: false, minLevel: LogLevel.Warning);
        var log = loggerFactory.CreateLogger("Test");
        var oldMustLog = LoggerExt.MustLogLegacyApiUsage;
        LoggerExt.MustLogLegacyApiUsage = true;

        // act
        try {
            log.LogLegacyApiUsage(
                "ILegacyChats.GetNews",
                Session.New(),
                "AndroidApp/2.7.3 Mozilla/5.0");
        }
        finally {
            LoggerExt.MustLogLegacyApiUsage = oldMustLog;
        }

        // assert
        messages.Should().ContainSingle()
            .Which.Should().Contain("ILegacyChats.GetNews")
            .And.Contain("AndroidApp/2.7.3");
    }

    // Helpers

    private static (ILoggerFactory Factory, List<string> Messages) CreateLoggerFactory(
        bool useSanitizing,
        LogLevel minLevel = LogLevel.Debug)
    {
        var messages = new List<string>();
        var innerProvider = new InMemoryLoggerProvider(messages, minLevel);
        var innerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(minLevel)
            .AddProvider(innerProvider));
        ILoggerFactory factory = useSanitizing
            ? new SanitizingLoggerFactory(innerFactory)
            : innerFactory;
        return (factory, messages);
    }

    // Nested types

    private sealed class InMemoryLoggerProvider(
        List<string>? messages = null,
        LogLevel minLevel = LogLevel.Debug)
        : ILoggerProvider
    {
        private readonly List<string> _messages = messages ?? [];

        public ILogger CreateLogger(string categoryName)
            => new InMemoryLogger(_messages, minLevel);

        public void Dispose() { }
    }

    private sealed class InMemoryLogger(List<string> messages, LogLevel minLevel) : ILogger
    {
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            messages.Add(formatter(state, exception));
        }

        public bool IsEnabled(LogLevel logLevel)
            => logLevel >= minLevel;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => null;
    }
}
