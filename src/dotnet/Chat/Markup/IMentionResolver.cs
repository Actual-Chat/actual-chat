namespace ActualChat.Chat;

public interface IMentionResolver<T>
    where T : notnull
{
    ValueTask<T?> Resolve(MentionMarkup mention, CancellationToken cancellationToken);
}
