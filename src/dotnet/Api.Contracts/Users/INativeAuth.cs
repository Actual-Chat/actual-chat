namespace ActualChat.Users;

/// <summary>
/// Service for native (iOS/Android) OAuth sign-in flows.
/// </summary>
public interface INativeAuth : IComputeService
{
    [CommandHandler]
    Task OnSignInGoogle(NativeAuth_SignInGoogle command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSignInApple(NativeAuth_SignInApple command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSignOut(NativeAuth_SignOut command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NativeAuth_SignInGoogle(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string Code
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NativeAuth_SignInApple(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] string UserId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] string Code,
    [property: DataMember, MemoryPackOrder(3), Key(3)] string? Email,
    [property: DataMember, MemoryPackOrder(4), Key(4)] string? Name
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NativeAuth_SignOut(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session
) : ISessionCommand<Unit>, IApiCommand;
