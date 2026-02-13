namespace ActualChat.Roulette;

/// <summary>
/// Legacy IRouletteProfiles interface for backwards compatibility with old clients.
/// Chat Roulette feature has been removed.
/// Only read methods are stubbed — GetOwnUserSettings is called by NavbarButtons
/// on release clients when the user is an admin.
/// </summary>
[Obsolete("Chat Roulette feature has been removed.")]
public interface ILegacyRouletteProfiles : IComputeService
{
    [ComputeMethod]
    Task<LegacyRouletteUserSettings?> GetOwnUserSettings(Session session, CancellationToken cancellationToken);
}
