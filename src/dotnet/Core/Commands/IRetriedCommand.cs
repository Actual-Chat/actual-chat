namespace ActualChat;

/// <summary>
/// A command the client holds until it's connected and retries on timeouts until it gets through.
/// Only for commands whose repeated delivery changes nothing.
/// </summary>
public interface IRetriedCommand : ICommand;
