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

    [CommandHandler]
    Task<LegacyRouletteUserSettings?> OnChangeOwnUserSettings(
        RouletteProfiles_ChangeOwnUserSettings command, CancellationToken cancellationToken);
}

[Obsolete("Chat Roulette feature has been removed.")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RouletteProfiles_ChangeOwnUserSettings(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<LegacyRouletteUserSettings> Change
) : ISessionCommand<LegacyRouletteUserSettings?>, IApiCommand;
