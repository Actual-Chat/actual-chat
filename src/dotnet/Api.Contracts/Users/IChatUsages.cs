using ActualLab.Rpc;

namespace ActualChat.Users;

[Obsolete("2026.10: The search panel's recents and voice samples come from contacts. Old clients only.")]
public interface IChatUsages : IComputeService
{
    [ComputeMethod, LegacyName("GetRecencyList")]
    Task<ChatId[]> GetLegacyRecencyList(
        Session session, ChatUsageListKind kind, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnRegisterUsage(ChatUsages_RegisterUsage command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
[Obsolete("2026.10: The search panel's recents and voice samples come from contacts. Old clients only.")]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatUsages_RegisterUsage : ApiCommand<Unit>, INotDeduplicated
{
    [DataMember(Order = 2), Key(2)] public required ChatUsageListKind Kind { get; init; }
    [DataMember(Order = 3), Key(3)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 4), Key(4)] public DateTime? AccessTime { get; init; }
}
