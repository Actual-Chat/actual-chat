using System.Collections.Concurrent;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Rpc;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

public interface IScheduledCommandTestService : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<int> GetProcessedEventCount(CancellationToken cancellationToken);

    [CommandHandler]
    Task OnAddTestEvent1Command(AddTestEvent1Command command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnAddBothTestEventsCommand(AddBothTestEventsCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnAddBothTestEventsCommandWithShardKey(AddBothTestEventsCommandWithShardKey command, CancellationToken cancellationToken);

    [EventHandler]
    Task ProcessTestEvent1(TestEvent1 eventCommand, CancellationToken cancellationToken);
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
    public virtual async Task OnAddTestEvent1Command(AddTestEvent1Command command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var context = CommandContext.GetCurrent();
        // CommandDbContext is required to enqueue events
        await using var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        context.Operation.AddEvent(new TestEvent1(command.Error));
    }

    [CommandHandler]
    public virtual async Task OnAddBothTestEventsCommand(
        AddBothTestEventsCommand command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var context = CommandContext.GetCurrent();
        // CommandDbContext is required to enqueue events
        await using var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        context.Operation.AddEvent(new TestEvent1(null));
        context.Operation.AddEvent(new TestEvent2());
    }

    [CommandHandler]
    public virtual async Task OnAddBothTestEventsCommandWithShardKey(
        AddBothTestEventsCommandWithShardKey command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var context = CommandContext.GetCurrent();
        // CommandDbContext is required to enqueue events
        await using var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        context.Operation.AddEvent(new TestEvent1(null));
        context.Operation.AddEvent(new TestEvent2()); // Same as above, actually, but for UserId.None
    }

    [EventHandler]
    public virtual Task ProcessTestEvent1(TestEvent1 eventCommand, CancellationToken cancellationToken)
    {
        if (eventCommand.Error != null)
            throw new InvalidOperationException(eventCommand.Error);

        ProcessedEvents.Enqueue(eventCommand);
        using (Invalidation.Begin())
            _ = GetProcessedEventCount(default);
        return Task.CompletedTask;
    }

    [EventHandler]
    public virtual Task ProcessTestEvent2(TestEvent2 eventCommand, CancellationToken cancellationToken)
    {
        ProcessedEvents.Enqueue(eventCommand);
        using (Invalidation.Begin())
            _ = GetProcessedEventCount(default);
        return Task.CompletedTask;
    }
}
