namespace ActualChat.Flows.Internal;

public static class Errors
{
    public static Exception NoStepImplementation(Type flowType, string step)
        => new InvalidOperationException($"Flow '{flowType.GetName()}' has no implementation for step '{step}'.");

    public static Exception NoEvent(Type flowType, string step, Type eventType)
        => new InvalidOperationException(
            $"Flow '{flowType.GetName()}' requires {eventType.GetName()} event on step '{step}'.");

    public static Exception NoEvent(Type flowType, string step, params Type[] eventTypes)
        => new InvalidOperationException(
            $"Flow '{flowType.GetName()}' requires {eventTypes.Select(x => x.GetName()).ToCommaPhrase("or")} event on step '{step}'.");

    public static Exception UnhandledEvent(Type flowType, string step, Type eventType)
        => new InvalidOperationException(
            $"Flow '{flowType.GetName()}' didn't mark event of type {eventType.GetName()} as handled on step '{step}'.");
}
