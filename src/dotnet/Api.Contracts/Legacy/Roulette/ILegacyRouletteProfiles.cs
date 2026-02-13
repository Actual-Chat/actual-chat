namespace ActualChat.Roulette;

/// <summary>
/// Legacy IRouletteProfiles interface for backwards compatibility with old clients.
/// Chat Roulette feature has been removed.
/// All read methods are stubbed to prevent errors on old clients.
/// </summary>
[Obsolete("Chat Roulette feature has been removed.")]
public interface ILegacyRouletteProfiles : IComputeService
{
    [ComputeMethod]
    Task<Symbol> GetSelectedProfileId(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<LegacyRouletteProfile?> GetOwnProfile(Session session, Symbol profileId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<LegacyRouletteProfile?> GetProfile(Session session, Symbol profileId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<LegacyRouletteUserSettings?> GetOwnUserSettings(Session session, CancellationToken cancellationToken);
}
