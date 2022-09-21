namespace ActualChat.ScheduledCommands;

public static class Errors
{
    public static Exception EventHandlerHubShouldBeTheLastFilter(Type eventType)
        => new InvalidOperationException($"EventHandlerHub should be the last Filter handler for event '{eventType}'.");
}
