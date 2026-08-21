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
public sealed partial record NativeAuth_SignInGoogle : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Code { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NativeAuth_SignInApple : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string UserId { get; init; }
    [DataMember(Order = 3), Key(3)] public required string Code { get; init; }
    [DataMember(Order = 4), Key(4)] public required string? Email { get; init; }
    [DataMember(Order = 5), Key(5)] public required string? Name { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NativeAuth_SignOut : ApiCommand<Unit>;
