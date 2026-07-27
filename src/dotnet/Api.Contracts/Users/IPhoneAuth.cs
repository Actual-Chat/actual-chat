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

    [ComputeMethod, Obsolete("2026.03: Removed in favor of CheckIfBlocked")]
    Task<string> ValidateCanSendToPhone(Session session, Phone phone, TotpPurpose purpose, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PhoneAuth_SendTotp(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Phone Phone,
    [property: DataMember, MemoryPackOrder(2), Key(2)] TotpPurpose Purpose = TotpPurpose.SignInPhone,
    [property: DataMember, MemoryPackOrder(3), Key(3)] string? CaptchaToken = null,
    [property: DataMember, MemoryPackOrder(4), Key(4)] string? CaptchaAction = null
) : ISessionCommand<Moment>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PhoneAuth_ValidateTotp(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Phone Phone,
    [property: DataMember, MemoryPackOrder(2), Key(2)] int Totp
) : ISessionCommand<bool>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record PhoneAuth_VerifyPhone(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Phone Phone,
    [property: DataMember, MemoryPackOrder(2), Key(2)] int Totp
) : ISessionCommand<bool>, IApiCommand; // NOTE(AY): Add backend, implement IApiCommand
