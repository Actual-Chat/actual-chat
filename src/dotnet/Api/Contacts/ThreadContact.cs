using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Contacts;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ThreadContact : IHasId<ContactId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public ContactId Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public long Version { get; init; }
    [DataMember, MemoryPackOrder(2)] public Moment TouchedAt { get; init; }
    [DataMember, MemoryPackOrder(3)] public bool IsPinned { get; init; }

    public ThreadContact(ContactId Id, long Version = 0)
    {
        if (!Id.ChatId.IsThread())
            throw new ArgumentOutOfRangeException(nameof(Id), "ContactId must refer to ThreadChatId");
        this.Id = Id;
        this.Version = Version;
    }

    public static readonly Requirement<ThreadContact> MustExist = Requirement.New(
        (ThreadContact? c) => c?.Id is not null,
        new(() => StandardError.NotFound<ThreadContact>()));

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId OwnerId => Id.OwnerId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ThreadChatId ThreadChatId => (ThreadChatId)Id.ChatId;

    public void Deconstruct(out ContactId Id, out long Version)
    {
        Id = this.Id;
        Version = this.Version;
    }
}
