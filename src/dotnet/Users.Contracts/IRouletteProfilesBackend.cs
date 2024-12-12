using ActualChat.Roulette;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Users;

public interface IRouletteProfilesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ProfileFull?> GetProfile(Symbol profileId, CancellationToken cancellationToken);

    // Not ComputeMethod
    Task<ImmutableArray<ProfilePreferences>> FindProfiles(Preferences filter, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<ProfilePreferences?> OnChangePrefs(RouletteProfilesBackend_ChangePrefs command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnAvatarChangedEvent(AvatarChangedEvent eventCommand, CancellationToken cancellationToken);
}

public record ProfileFull(
    [property: DataMember, MemoryPackOrder(2)]
    UserId UserId,
    Symbol Id) : Profile(Id)
{
    public Profile ToProfile()
        => new (Id) {
            Avatar = Avatar,
            Preferences = Preferences
        };
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RouletteProfilesBackend_ChangePrefs(
    [property: DataMember, MemoryPackOrder(0)] Symbol ProfileId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ProfilePreferences> Change
) : IBackendCommand<ProfilePreferences>;
