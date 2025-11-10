// filepath: d:\Projects\actual-chat\src\dotnet\Api.Contracts\Users\IEmailAuth.cs
using MemoryPack;

namespace ActualChat.Users;

public interface IEmailAuth : IComputeService
{
    [CommandHandler]
    Task<Moment> OnSendTotp(EmailAuth_SendTotp command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnVerifyEmail(EmailAuth_VerifyEmail command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record EmailAuth_SendTotp(
    [property: DataMember, MemoryPackOrder(0)] Session Session
) : ISessionCommand<Moment>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record EmailAuth_VerifyEmail(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] int Token
) : ISessionCommand<bool>; // NOTE(AY): Add backend, implement IApiCommand

