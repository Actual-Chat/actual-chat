namespace ActualChat;

/// <summary>
/// Marker interface for API commands that can be called from clients.
/// </summary>
public interface IApiCommand : IDelegatingCommand;

/// <summary>
/// Marker interface for API commands with typed results.
/// </summary>
public interface IApiCommand<TResult> : IDelegatingCommand<TResult>, IApiCommand;
