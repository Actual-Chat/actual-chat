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
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record EmailAuth_SendTotp : ApiCommand<Moment>
{
    [DataMember(Order = 2), Key(2)] public required Email Email { get; init; }
    [DataMember(Order = 3), Key(3)] public TotpPurpose Purpose { get; init; } = TotpPurpose.SignInEmail;
    [DataMember(Order = 4), Key(4)] public string? CaptchaToken { get; init; }
    [DataMember(Order = 5), Key(5)] public string? CaptchaAction { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record EmailAuth_ValidateTotp : ApiCommand<bool>
{
    [DataMember(Order = 2), Key(2)] public required Email Email { get; init; }
    [DataMember(Order = 3), Key(3)] public required int Totp { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record EmailAuth_VerifyEmail : ApiCommand<bool>
{
    [DataMember(Order = 2), Key(2)] public required Email Email { get; init; }
    [DataMember(Order = 3), Key(3)] public required int Token { get; init; }
} // NOTE(AY): Add backend, implement IApiCommand
