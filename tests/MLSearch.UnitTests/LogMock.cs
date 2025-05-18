using System.Linq.Expressions;

namespace ActualChat.MLSearch.UnitTests;

public static class LogMock
{
    public static Mock<ILogger<T>> Create<T>()
    {
        var logger = new Mock<ILogger<T>>(MockBehavior.Loose);
        logger
            .Setup(GetLogMethodExpression<T>())
            .Verifiable();
        return logger;
    }

    private static readonly Expression<Func<LogLevel, bool>> anyLogLevel = level => true;

    public static Expression<Action<ILogger<T>>> GetLogMethodExpression<T>(LogLevel? level = default)
    {
        var logLevelCheck = level is null ? anyLogLevel : lvl => lvl == level;
        return (ILogger<T> x) => x.Log(
            It.Is(logLevelCheck),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()
        );
    }
}
