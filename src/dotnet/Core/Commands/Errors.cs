namespace ActualChat.Commands;

public static class Errors
{
    public static Exception EventHandlerHubShouldBeTheLastFilter(Type eventType)
        => new InvalidOperationException($"EventHandlerHub should be the last Filter handler for event '{eventType}'.");

    public static Exception NoHandlerFound(Type commandType)
        => new InvalidOperationException($"No handler is found for event '{commandType}'.");
}
