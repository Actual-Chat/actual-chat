using ActualChat.Roulette;

namespace ActualChat.Chat;

#pragma warning disable CS0618 // Obsolete

/// <summary>
/// Legacy IRouletteProfiles implementation for backwards compatibility with old clients.
/// Chat Roulette feature has been removed. Returns null for GetOwnUserSettings.
/// </summary>
[Obsolete("Chat Roulette feature has been removed.")]
public class LegacyRouletteProfiles : ILegacyRouletteProfiles
{
    public virtual Task<LegacyRouletteUserSettings?> GetOwnUserSettings(
        Session session,
        CancellationToken cancellationToken)
        => Task.FromResult<LegacyRouletteUserSettings?>(null);
}
