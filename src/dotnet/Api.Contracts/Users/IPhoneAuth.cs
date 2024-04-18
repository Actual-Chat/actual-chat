using MemoryPack;

namespace ActualChat.Users;

public interface IPhoneAuth : IComputeService
{
    [ComputeMethod]
    Task<bool> IsEnabled(CancellationToken cancellationToken);
    [CommandHandler]
    Task<Moment> OnSendTotp(PhoneAuth_SendTotp command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnValidateTotp(PhoneAuth_ValidateTotp command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnVerifyPhone(PhoneAuth_VerifyPhone command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record PhoneAuth_SendTotp(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Phone Phone,
    [property: DataMember, MemoryPackOrder(2)] TotpPurpose Purpose = TotpPurpose.SignIn
) : ISessionCommand<Moment>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record PhoneAuth_ValidateTotp(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Phone Phone,
    [property: DataMember, MemoryPackOrder(2)] int Totp
) : ISessionCommand<bool>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record PhoneAuth_VerifyPhone(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Phone Phone,
    [property: DataMember, MemoryPackOrder(2)] int Totp
) : ISessionCommand<bool>; // NOTE(AY): Add backend, implement IApiCommand
