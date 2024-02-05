using ActualChat.Commands;

namespace ActualChat.Core.UnitTests.Commands;

public record TestEvent(string? Error) : EventCommand, IHasShardKey<Unit>
{
    public Unit ShardKey => Unit.Default;
}
public record TestEvent2 : EventCommand, IHasShardKey<Unit>
{
    public Unit ShardKey => Unit.Default;
}

public record TestCommand(string? Error) : ICommand<Unit>;
public record TestCommand2 : ICommand<Unit>;
public record TestCommand3 : ICommand<Unit>;
