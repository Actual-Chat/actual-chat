namespace ActualChat;

public interface IApiCommand : IDelegatingCommand;

public interface IApiCommand<TResult> : IDelegatingCommand<TResult>, IApiCommand;
