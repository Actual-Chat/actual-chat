using MemoryPack;
namespace ActualChat.Roulette;

public interface IRouletteProfiles : IComputeService
{
    [ComputeMethod]
    Task<Symbol> GetSelectedProfileId(Session session, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Profile?> GetOwnProfile(Session session, Symbol profileId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Profile> OnChange(RouletteProfiles_UpsertProfile command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSelectProfile(RouletteProfiles_SelectProfile command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RouletteProfiles_UpsertProfile(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Symbol ProfileId,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<Profile> Change
) : ISessionCommand<Profile>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RouletteProfiles_SelectProfile(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Symbol ProfileId
) : ISessionCommand<Unit>, IApiCommand;
