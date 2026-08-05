using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents a #hashtag token; <see cref="TextMarkup.Text"/> includes the leading '#'.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class HashtagMarkup(string text) : TextMarkup(text)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string Tag => Text.Length > 0 ? Text[1..] : "";

    public override TextMarkupKind Kind => TextMarkupKind.Hashtag;

    public override TextMarkup WithText(string text)
        => new HashtagMarkup(text);
}
