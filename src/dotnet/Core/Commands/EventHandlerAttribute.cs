namespace ActualChat.Commands;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class EventHandlerAttribute : CommandHandlerAttribute
{ }
