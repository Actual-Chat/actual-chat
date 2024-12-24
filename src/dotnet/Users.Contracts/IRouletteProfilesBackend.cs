using ActualChat.Roulette;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Users;

public interface IRouletteProfilesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ProfileFull?> GetProfile(Symbol profileId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<RouletteUserSettings?> GetUserSettings(UserId userId, CancellationToken cancellationToken);

    // Not ComputeMethod
    Task<ImmutableArray<ProfilePreferences>> FindProfiles(UserId ownUserId, Symbol ownProfileId, Preferences filter, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<RouletteUserSettings?> OnChangeUserSettings(RouletteProfilesBackend_ChangeUserSettings command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ProfilePreferences?> OnChangePrefs(RouletteProfilesBackend_ChangePrefs command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnCreateCompletedChatRoulette(RouletteProfilesBackend_CreateCompletedChatRoulette command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnAvatarChangedEvent(AvatarChangedEvent eventCommand, CancellationToken cancellationToken);

    [EventHandler]
    Task OnChatRouletteCompletedEvent(ChatRouletteCompletedEvent eventCommand, CancellationToken cancellationToken);
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
public sealed partial record RouletteProfilesBackend_ChangeUserSettings(
    [property: DataMember, MemoryPackOrder(0)] UserId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<RouletteUserSettings> Change
) : IBackendCommand<RouletteUserSettings?>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RouletteProfilesBackend_ChangePrefs(
    [property: DataMember, MemoryPackOrder(0)] Symbol ProfileId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<ProfilePreferences> Change
) : IBackendCommand<ProfilePreferences>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RouletteProfilesBackend_CreateCompletedChatRoulette(
    [property: DataMember, MemoryPackOrder(0)] UserId OwnerUserId,
    [property: DataMember, MemoryPackOrder(1)] Symbol OwnerProfileId,
    [property: DataMember, MemoryPackOrder(2)] UserId PeerUserId,
    [property: DataMember, MemoryPackOrder(3)] Symbol PeerProfileId,
    [property: DataMember, MemoryPackOrder(4)] bool IsInitiatedByOwner,
    [property: DataMember, MemoryPackOrder(5)] Moment CompletedAt,
    [property: DataMember, MemoryPackOrder(6)] CompleteChatRouletteReason CompleteReason
) : IBackendCommand;
