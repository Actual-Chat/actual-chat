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

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NativeAuth_SignInGoogle(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] string Code
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NativeAuth_SignInApple(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] string UserId,
    [property: DataMember, Key(2)] string Code,
    [property: DataMember, Key(3)] string? Email,
    [property: DataMember, Key(4)] string? Name
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NativeAuth_SignOut(
    [property: DataMember, Key(0)] Session Session
) : ISessionCommand<Unit>, IApiCommand;
