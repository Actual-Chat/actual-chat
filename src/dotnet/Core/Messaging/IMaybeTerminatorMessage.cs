namespace ActualChat.Messaging;

public interface IMaybeTerminatorMessage
{
    bool IsTerminator { get; }
}
