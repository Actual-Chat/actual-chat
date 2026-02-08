
namespace ActualChat.Users;

/// <summary>
/// Service for sending email communications.
/// </summary>
public interface IEmails : IComputeService
{
    [CommandHandler]
    Task OnSendDigest(Emails_SendDigest command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
// ReSharper disable once InconsistentNaming
public sealed partial record Emails_SendDigest(
    [property: DataMember, MemoryPackOrder(0)] Session Session
) : ISessionCommand<Moment>, IApiCommand;
