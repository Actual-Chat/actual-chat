using ActualChat.Users;

namespace ActualChat.Chat;

public class Aliases(IServiceProvider services) : IAliases
{
    private IAliasBackend Backend { get; } = services.GetRequiredService<IAliasBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();

    public virtual async Task<AliasTarget?> GetTarget(AliasId aliasId, CancellationToken cancellationToken = default)
    {
        var alias = await Backend.Get(aliasId, cancellationToken).ConfigureAwait(false);
        return alias is null ? null
            : new AliasTarget(alias.Kind, alias.TargetId);
    }

    public virtual Task<PlaceChatId?> GetPlaceChatIdByAlias(PlaceId placeId, AliasId aliasId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placeId);
        ArgumentNullException.ThrowIfNull(aliasId);

        return ChatsBackend.GetPlaceChatIdByAlias(placeId, aliasId, cancellationToken);
    }

    public virtual Task<UserId?> GetUserIdByAlias(AliasId aliasId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aliasId);

        return AccountsBackend.GetIdByAlias(aliasId, cancellationToken);
    }
}
