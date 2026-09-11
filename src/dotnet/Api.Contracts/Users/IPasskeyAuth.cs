namespace ActualChat.Users;

/// <summary>
/// WebAuthn (passkey) registration and sign-in. Options and responses are the standard
/// WebAuthn JSON shapes, so every client - web, Android, Apple - speaks the same contract.
/// </summary>
public interface IPasskeyAuth : IComputeService
{
    [ComputeMethod]
    Task<bool> IsEnabled(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<string> GetRpId(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<Passkey>> ListOwn(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task<string> OnBeginRegistration(PasskeyAuth_BeginRegistration command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Passkey> OnCompleteRegistration(PasskeyAuth_CompleteRegistration command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<string> OnBeginSignIn(PasskeyAuth_BeginSignIn command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnCompleteSignIn(PasskeyAuth_CompleteSignIn command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRename(PasskeyAuth_Rename command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDelete(PasskeyAuth_Delete command, CancellationToken cancellationToken);
}

public enum PasskeyPurpose
{
    AddToAccount = 0,
    SignUp, // Reserved for passkey-first signup; refused until that ships
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeyAuth_BeginRegistration : ApiCommand<string>, INotDeduplicated
{
    [DataMember(Order = 2), Key(2)] public PasskeyPurpose Purpose { get; init; } = PasskeyPurpose.AddToAccount;
    [DataMember(Order = 3), Key(3)] public string? Name { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeyAuth_CompleteRegistration : ApiCommand<Passkey>
{
    [DataMember(Order = 2), Key(2)] public required string AttestationJson { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeyAuth_BeginSignIn : ApiCommand<string>, INotDeduplicated;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeyAuth_CompleteSignIn : ApiCommand<bool>
{
    [DataMember(Order = 2), Key(2)] public required string AssertionJson { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeyAuth_Rename : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Id { get; init; }
    [DataMember(Order = 3), Key(3)] public required string Name { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PasskeyAuth_Delete : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required string Id { get; init; }
}
