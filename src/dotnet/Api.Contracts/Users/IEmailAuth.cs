namespace ActualChat.Users;

/// <summary>
/// Service for email-based authentication with TOTP codes.
/// </summary>
public interface IEmailAuth : IComputeService
{
    [ComputeMethod]
    Task<string> GetEmailValidationMessage(
        Session session, Email email, TotpPurpose purpose, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> AccountExists(Session session, Email email, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Moment> OnSendTotp(EmailAuth_SendTotp command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnValidateTotp(EmailAuth_ValidateTotp command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnVerifyEmail(EmailAuth_VerifyEmail command, CancellationToken cancellationToken);

    [ComputeMethod, Obsolete("2026.07: Use GetEmailValidationMessage.")]
    Task<string> CheckIfBlocked(Session session, Email email, TotpPurpose purpose, CancellationToken cancellationToken);
    [ComputeMethod, Obsolete("2026.03: Use GetEmailValidationMessage.")]
    Task<string> ValidateCanSendToEmail(Session session, Email email, TotpPurpose purpose, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record EmailAuth_SendTotp(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Email Email,
    [property: DataMember, MemoryPackOrder(2), Key(2)] TotpPurpose Purpose = TotpPurpose.SignInEmail,
    [property: DataMember, MemoryPackOrder(3), Key(3)] string? CaptchaToken = null,
    [property: DataMember, MemoryPackOrder(4), Key(4)] string? CaptchaAction = null
) : ISessionCommand<Moment>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record EmailAuth_ValidateTotp(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Email Email,
    [property: DataMember, MemoryPackOrder(2), Key(2)] int Totp
) : ISessionCommand<bool>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record EmailAuth_VerifyEmail(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Email Email,
    [property: DataMember, MemoryPackOrder(2), Key(2)] int Token
) : ISessionCommand<bool>, IApiCommand; // NOTE(AY): Add backend, implement IApiCommand
