using ActualChat.Roulette;

namespace ActualChat.Chat;

#pragma warning disable CS0618 // Obsolete

/// <summary>
/// Legacy IRouletteProfiles implementation for backwards compatibility with old clients.
/// Chat Roulette feature has been removed. All read methods return default/null values.
/// </summary>
[Obsolete("Chat Roulette feature has been removed.")]
public class LegacyRouletteProfiles : ILegacyRouletteProfiles
{
    public virtual Task<Symbol> GetSelectedProfileId(
        Session session,
        CancellationToken cancellationToken)
        => Task.FromResult(Symbol.Empty);

    public virtual Task<LegacyRouletteProfile?> GetOwnProfile(
        Session session,
        Symbol profileId,
        CancellationToken cancellationToken)
        => Task.FromResult<LegacyRouletteProfile?>(null);

    public virtual Task<LegacyRouletteProfile?> GetProfile(
        Session session,
        Symbol profileId,
        CancellationToken cancellationToken)
        => Task.FromResult<LegacyRouletteProfile?>(null);

    public virtual Task<LegacyRouletteUserSettings?> GetOwnUserSettings(
        Session session,
        CancellationToken cancellationToken)
        => Task.FromResult<LegacyRouletteUserSettings?>(null);

    public virtual Task<LegacyRouletteUserSettings?> OnChangeOwnUserSettings(
        RouletteProfiles_ChangeOwnUserSettings command,
        CancellationToken cancellationToken)
        => Task.FromResult<LegacyRouletteUserSettings?>(null);
}
