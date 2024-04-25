using System.Collections.Concurrent;
using ActualChat.Queues;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Rpc;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

public interface IScheduledCommandTestService : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<int> GetProcessedEventCount(CancellationToken cancellationToken);

    [CommandHandler]
    Task ProcessTestCommand(TestCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task ProcessTestCommand2(TestCommand2 command, CancellationToken cancellationToken);
    [CommandHandler]
    Task ProcessTestCommand3(TestCommand3 command, CancellationToken cancellationToken);

    [EventHandler]
    Task ProcessTestEvent(TestEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task ProcessTestEvent2(TestEvent2 eventCommand, CancellationToken cancellationToken);
}

public class ScheduledCommandTestService(IServiceProvider services)
    : DbServiceBase<UsersDbContext>(services), IScheduledCommandTestService
{
    public readonly ConcurrentQueue<IEventCommand> ProcessedEvents = new();

    [ComputeMethod]
    public virtual Task<int> GetProcessedEventCount(CancellationToken cancellationToken)
        => Task.FromResult(ProcessedEvents.Count);

    [CommandHandler]
    public virtual async Task ProcessTestCommand(TestCommand command, CancellationToken cancellationToken)
    {
        if (InvalidationMode.IsOn)
            return;

        var context = CommandContext.GetCurrent();
        // CommandDbContext is required to enqueue events
        await using var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        context.Operation.AddEvent(new TestEvent(command.Error));
    }

    [CommandHandler]
    public virtual async Task ProcessTestCommand2(TestCommand2 command, CancellationToken cancellationToken)
    {
        if (InvalidationMode.IsOn)
            return;

        var context = CommandContext.GetCurrent();
        // CommandDbContext is required to enqueue events
        await using var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        context.Operation.AddEvent(new TestEvent(null));
        context.Operation.AddEvent(new TestEvent2());
    }

    [CommandHandler]
    public virtual async Task ProcessTestCommand3(TestCommand3 command, CancellationToken cancellationToken)
    {
        if (InvalidationMode.IsOn)
            return;

        var context = CommandContext.GetCurrent();
        // CommandDbContext is required to enqueue events
        await using var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        context.Operation.AddEvent(new TestEvent(null));
        context.Operation.AddEvent(new TestEvent2()); // Same as above, actually, but for UserId.None
    }

    [EventHandler]
    public virtual Task ProcessTestEvent(TestEvent eventCommand, CancellationToken cancellationToken)
    {
        if (InvalidationMode.IsOn) {
            _ = GetProcessedEventCount(default);
            return Task.CompletedTask;
        }

        if (eventCommand.Error != null)
            throw new InvalidOperationException(eventCommand.Error);

        ProcessedEvents.Enqueue(eventCommand);
        return Task.CompletedTask;
    }

    [EventHandler]
    public virtual Task ProcessTestEvent2(TestEvent2 eventCommand, CancellationToken cancellationToken)
    {
        if (InvalidationMode.IsOn) {
            _ = GetProcessedEventCount(default);
            return Task.CompletedTask;
        }

        ProcessedEvents.Enqueue(eventCommand);
        return Task.CompletedTask;
    }
}
