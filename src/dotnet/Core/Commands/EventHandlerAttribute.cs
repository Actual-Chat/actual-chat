namespace ActualChat;

/// <summary>
/// Marks a method as an event handler that processes <see cref="EventCommand"/> instances.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class EventHandlerAttribute : CommandHandlerAttribute
{ }
