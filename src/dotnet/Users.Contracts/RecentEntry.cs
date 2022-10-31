namespace ActualChat.Users;

[DataContract]
public sealed record RecentEntry(
        [property: DataMember] string GroupKey,
        [property: DataMember] string Key,
        [property: DataMember] RecencyScope Scope
    ) : IRequirementTarget
{
    [property: DataMember] public long Version { get; init; }
    [property: DataMember] public Moment UpdatedAt { get; init; }
}

public enum RecencyScope
{
    Contact,
    Chat,
}
