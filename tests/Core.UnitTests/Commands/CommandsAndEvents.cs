using ActualChat.Commands;

namespace ActualChat.Core.UnitTests.Commands;

public record TestEvent(string? Error) : IEvent;
public record TestEvent2 : IEvent;
public record TestCommand(string? Error) : ICommand<Unit>;
public record TestCommand2 : ICommand<Unit>;
public record TestCommand3 : ICommand<Unit>;
