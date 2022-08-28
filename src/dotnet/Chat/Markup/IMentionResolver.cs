namespace ActualChat.Chat;

public interface IMentionResolver<T>
    where T : notnull
{
    ValueTask<T?> Resolve(Mention mention, CancellationToken cancellationToken);
}
