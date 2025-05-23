using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Contacts;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ThreadContact(
    [property: DataMember, MemoryPackOrder(0)] ContactId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<ContactId>, IHasVersion<long>, IRequirementTarget
{
    public static readonly Requirement<ThreadContact> MustExist = Requirement.New(
        (ThreadContact? c) => c?.Id is not null,
        new(() => StandardError.NotFound<ThreadContact>()));

    [DataMember, MemoryPackOrder(2)] public Moment TouchedAt { get; init; }
    [DataMember, MemoryPackOrder(3)] public bool IsPinned { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId OwnerId => Id.OwnerId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ThreadChatId => Id.ChatId;
}
