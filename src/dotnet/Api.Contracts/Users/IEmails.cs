namespace ActualChat.Users;

/// <summary>
/// Service for sending email communications.
/// </summary>
public interface IEmails : IComputeService
{
    Task<DigestPreview> GetDigestPreview(Session session, ChatId[] chatIds, DateTime? asOf, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnSendDigest(Emails_SendDigest command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Emails_SendDigest(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session
) : ISessionCommand<Moment>, IApiCommand;
