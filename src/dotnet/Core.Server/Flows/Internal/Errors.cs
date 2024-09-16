namespace ActualChat.Flows.Internal;

public static class Errors
{
    public static Exception NoStepImplementation(Type flowType, string step)
        => new InvalidOperationException($"Flow '{flowType.GetName()}' has no implementation for step '{step}'.");

    public static Exception NoEvent(Type flowType, string step, Type received, Type expected)
        => new InvalidOperationException(
            $"Flow '{flowType.GetName()}' requires {expected.GetName()} event "
            + $"on step '{step}', but got {received.GetName()}.");

    public static Exception NoEvent(Type flowType, string step, Type received, params Type[] expected)
        => new InvalidOperationException(
            $"Flow '{flowType.GetName()}' requires {expected.Select(x => x.GetName()).ToCommaPhrase("or")} event "
            + $"on step '{step}', but got {received.GetName()}.");

    public static Exception UnhandledEvent(Type flowType, string step, Type eventType)
        => new InvalidOperationException(
            $"Flow '{flowType.GetName()}' didn't mark event of type {eventType.GetName()} as handled on step '{step}'.");
}
