using MemoryPack;

namespace ActualChat.Users;

public interface IPhoneAuth : IComputeService
{
    [CommandHandler]
    Task<Moment> OnSendTotp(PhoneAuth_SendTotp command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<bool> OnValidateTotp(PhoneAuth_ValidateTotp command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record PhoneAuth_SendTotp(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Phone Phone
) : ISessionCommand<Moment>;


[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record PhoneAuth_ValidateTotp(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Phone Phone,
    [property: DataMember, MemoryPackOrder(2)] int Totp
) : ISessionCommand<bool>;
