using MemoryPack;

namespace ActualChat.Users;

public interface IEmails : IComputeService
{
    [CommandHandler]
    Task<Moment> OnSendTotp(Emails_SendTotp command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnVerifyEmail(Emails_VerifyEmail command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Emails_SendTotp(
    [property: DataMember, MemoryPackOrder(0)] Session Session
) : ISessionCommand<Moment>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Emails_VerifyEmail(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] int Token
) : ISessionCommand<bool>; // NOTE(AY): Add backend, implement IApiCommand
