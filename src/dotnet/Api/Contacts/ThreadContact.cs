using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Contacts;

/// <summary>
/// Represents a contact entry for a thread conversation.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record ThreadContact : IHasId<ContactId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(0)] public ContactId Id { get; init; }
    [DataMember, Key(1)] public long Version { get; init; }
    [DataMember, Key(2)] public Moment TouchedAt { get; init; }
    [DataMember, Key(3)] public bool IsPinned { get; init; }

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
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId OwnerId => Id.OwnerId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ThreadChatId ThreadChatId => (ThreadChatId)Id.ChatId;

    public void Deconstruct(out ContactId Id, out long Version)
    {
        Id = this.Id;
        Version = this.Version;
    }
}
