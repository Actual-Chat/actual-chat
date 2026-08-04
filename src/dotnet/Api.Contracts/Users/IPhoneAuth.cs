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
    Task<Moment> OnSendTotp(PhoneAuth_SendTotp command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnValidateTotp(PhoneAuth_ValidateTotp command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnVerifyPhone(PhoneAuth_VerifyPhone command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
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
