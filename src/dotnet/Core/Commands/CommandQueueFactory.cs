using Microsoft.Extensions.Hosting;

namespace ActualChat.Commands;

public class CommandQueueFactory
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServiceProvider _serviceProvider;

    public CommandQueueFactory(IHostApplicationLifetime lifetime, IServiceProvider serviceProvider)
    {
        _lifetime = lifetime;
        _serviceProvider = serviceProvider;
    }

    public CommandQueue<TQueueCommand> Create<TQueueCommand>(ICommandProcessor<TQueueCommand> commandProcessor)
        where TQueueCommand : IQueueCommand
        => new CommandQueue<TQueueCommand>(
            _lifetime,
            commandProcessor);

    public CommandQueue<TQueueCommand> Create<TQueueCommand, TCommandProcessor>()
        where TQueueCommand : IQueueCommand
        where TCommandProcessor : ICommandProcessor<TQueueCommand>
        => new CommandQueue<TQueueCommand>(
            _lifetime,
            _serviceProvider.GetRequiredService<TCommandProcessor>());

    public CommandQueue<TQueueCommand> Create<TQueueCommand, TCommandProcessor>(Channel<CommandExecution> commands)
        where TQueueCommand : IQueueCommand
        where TCommandProcessor : ICommandProcessor<TQueueCommand>
        => new CommandQueue<TQueueCommand>(
            _lifetime,
            _serviceProvider.GetRequiredService<TCommandProcessor>(),
            commands);
}
