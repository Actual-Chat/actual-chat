namespace ActualChat;

#pragma warning disable SYSLIB0051

[Serializable]
public class WrongShardException : Exception // Must not be ITransientException!
{
    private const string DefaultMessage = "Wrong shard.";

    public WrongShardException() : base(DefaultMessage) { }
    public WrongShardException(string? message) : base(message ?? DefaultMessage) { }
    public WrongShardException(string? message, Exception? innerException) : base(message ?? DefaultMessage, innerException) { }
    protected WrongShardException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
