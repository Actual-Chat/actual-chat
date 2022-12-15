namespace ActualChat.Chat;

[DataContract]
public record SystemEntry : IUnionRecord<SystemEntryOption?>
{
    // Union options
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public SystemEntryOption? Option { get; init; }

    [DataMember]
    public MembersChangedOption? MembersChanged {
        get => Option as MembersChangedOption;
        init => Option = value;
    }

    public static implicit operator SystemEntry(SystemEntryOption option) => new() { Option = option };
}

public abstract record SystemEntryOption : IRequirementTarget
{
    public abstract string ToMarkup();
}

[DataContract]
public record MembersChangedOption(
    [property: DataMember] AuthorId AuthorId,
    [property: DataMember] bool HasLeft
    ) : SystemEntryOption
{
    public override string ToMarkup()
        => $"@a:{AuthorId} has {(HasLeft ? "left" : "joined")} the chat.";
}
