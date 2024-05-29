using System.Linq.Expressions;

namespace ActualChat.MLSearch.UnitTests;

public static class LogMock
{
    public static Mock<ILogger<T>> Create<T>() {
        var logger = new Mock<ILogger<T>>();
        logger
            .Setup(GetLogMethodExpression<T>())
            .Verifiable();
        return logger;
    }

    public static Expression<Action<ILogger<T>>> GetLogMethodExpression<T>(LogLevel? level = default)
    {
        if (level.HasValue) {
            return (ILogger<T> x) => x.Log(
                It.Is<LogLevel>(lvl => lvl==level),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>());
        }
        return (ILogger<T> x) => x.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>());
    }
}

