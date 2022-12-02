using Stl.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract]
public record struct ChatNews(
    [property: DataMember] Range<long> TextEntryIdRange,
    [property: DataMember] ChatEntry? LastTextEntry = null
    ) : IRequirementTarget, ICanBeNone<ChatNews>
{
    public static ChatNews None { get; } = default;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsNone => this == default;
}
