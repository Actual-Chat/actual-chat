namespace ActualChat.Chat;

/// <summary>
/// Resolves <see cref="MentionMarkup"/> to its target entity.
/// </summary>
public interface IMentionResolver<T>
    where T : notnull
{
    ValueTask<T?> Resolve(MentionMarkup mention, CancellationToken cancellationToken);
}
