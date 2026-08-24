namespace ActualChat.Users;

/// <summary>
/// Service for phone-based authentication with TOTP codes.
/// </summary>
public interface IPhoneAuth : IComputeService
{
    [ComputeMethod]
    Task<bool> IsEnabled(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<string> CheckIfBlocked(Session session, Phone phone, TotpPurpose purpose, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> AccountExists(Session session, Phone phone, CancellationToken cancellationToken);
    [CommandHandler]
    Task<TotpSendResult> OnSendCode(PhoneAuth_SendCode command, CancellationToken cancellationToken);
    [Obsolete("2026.08: Use PhoneAuth_SendCode. Old clients only.")]
    [CommandHandler]
    Task<Moment> OnSendTotp(PhoneAuth_SendTotp command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnValidateTotp(PhoneAuth_ValidateTotp command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnVerifyPhone(PhoneAuth_VerifyPhone command, CancellationToken cancellationToken);
}

// Channel is the channel of the code that's currently live, not proof that this call sent one:
// a throttled call reports the channel of the previous send. Null means the channel is unknown -
// a predefined code was used, or the call was throttled before anything had ever been sent.

[DataContract, MessagePackObject]
public sealed partial record TotpSendResult(
    [property: DataMember, Key(0)] Moment NextSendAt,
    [property: DataMember, Key(1)] TotpChannel? Channel
);

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PhoneAuth_SendCode(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] Phone Phone,
    [property: DataMember, Key(2)] TotpPurpose Purpose = TotpPurpose.SignInPhone,
    [property: DataMember, Key(3)] string? CaptchaToken = null,
    [property: DataMember, Key(4)] string? CaptchaAction = null,
    // Reserved: the channel is picked server-side, the handler ignores whatever a client sends here
    [property: DataMember, Key(5)] TotpChannel? Channel = null
) : ISessionCommand<TotpSendResult>, IApiCommand;

[DataContract, MessagePackObject]
[Obsolete("2026.08: Use PhoneAuth_SendCode. Old clients only.")]
// ReSharper disable once InconsistentNaming
public sealed partial record PhoneAuth_SendTotp(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] Phone Phone,
    [property: DataMember, Key(2)] TotpPurpose Purpose = TotpPurpose.SignInPhone,
    [property: DataMember, Key(3)] string? CaptchaToken = null,
    [property: DataMember, Key(4)] string? CaptchaAction = null
) : ISessionCommand<Moment>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PhoneAuth_ValidateTotp(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] Phone Phone,
    [property: DataMember, Key(2)] int Totp
) : ISessionCommand<bool>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PhoneAuth_VerifyPhone(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] Phone Phone,
    [property: DataMember, Key(2)] int Totp
) : ISessionCommand<bool>, IApiCommand; // NOTE(AY): Add backend, implement IApiCommand
