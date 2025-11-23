using ActualChat.Queues;

namespace ActualChat.MLSearch.UnitTests;

public static class QueuesMock
{
    public static Mock<IQueues> Create(Func<QueuedCommand, CancellationToken, Task>? action = null)
    {
        var queueRefResolver = new Mock<IQueueRefResolver>(MockBehavior.Loose);
        queueRefResolver
            .Setup(x => x.GetQueueShardRef(It.IsAny<ICommand>()))
            .Returns(new QueueShardRef(ShardScheme.EventQueue, 1));
        var services = new Mock<IServiceProvider>(MockBehavior.Loose);
        services
            .Setup(x => x.GetService(typeof(IQueueRefResolver)))
            .Returns(queueRefResolver.Object);
        var queues = new Mock<IQueues>(MockBehavior.Loose);
        queues
            .SetupGet(x => x.Services)
            .Returns(services.Object);

        var queueSender = new Mock<IQueueSender>(MockBehavior.Loose);
        queueSender
            .Setup(x => x.Enqueue(
                It.IsAny<QueueShardRef>(),
                It.IsAny<QueuedCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns<QueueShardRef, QueuedCommand, CancellationToken>(
                (_, queuedCommand, ct) => {
                    return action?.Invoke(queuedCommand, ct) ?? Task.CompletedTask;
                }
            );
        queues
            .Setup(x => x.GetSender(It.IsAny<QueueRef>()))
            .Returns(queueSender.Object);
        return queues;
    }
}
