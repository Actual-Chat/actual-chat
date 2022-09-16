using Cysharp.Text;

namespace ActualChat.Chat;

[DataContract]
public sealed record MentionMarkup(
    [property: DataMember] string Id,
    [property: DataMember] string Name = ""
    ) : Markup
{
    public static readonly Func<MentionMarkup, string> DefaultFormatter = m => m.Format();
    public static readonly Func<MentionMarkup, string> NameOrNotAvailableFormatter = m => "@" + m.NameOrNotAvailable;
    public static readonly Func<MentionMarkup, string> NameOrIdFormatter = m => "@" + m.NameOrId;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string QuotedName => Quote(Name);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string NameOrNotAvailable => Name.NullIfEmpty() ?? "(n/a)";
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string NameOrId => Name.NullIfEmpty() ?? Id;

    public MentionMarkup() : this("") { }

    public override string Format()
        => Name.IsNullOrEmpty()
            ? "@" + Id
            : ZString.Concat('@', QuotedName, Id);

    public static string Quote(string name)
        => ZString.Concat('`', name.OrdinalReplace("`", "``"), '`');
}
