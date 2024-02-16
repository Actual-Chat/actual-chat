using Cysharp.Text;
using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record MentionMarkup(
    [property: DataMember, MemoryPackOrder(0)] MentionId Id,
    [property: DataMember, MemoryPackOrder(1)] string Name = ""
    ) : Markup
{
    public static readonly string NotAvailableName = "(n/a)";
    public static readonly Func<MentionMarkup, string> DefaultFormatter = m => m.Format();
    public static readonly Func<MentionMarkup, string> NameOrNotAvailableFormatter = m => "@" + m.NameOrNotAvailable;
    public static readonly Func<MentionMarkup, string> NameOrIdFormatter = m => "@" + m.NameOrId;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string QuotedName => Quote(Name);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string NameOrNotAvailable => Name.NullIfEmpty() ?? NotAvailableName;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string NameOrId => Name.NullIfEmpty() ?? Id;

    public override string Format()
        => Name.IsNullOrEmpty()
            ? "@" + Id
            : ZString.Concat('@', QuotedName, Id);

    public static string Quote(string name)
        => ZString.Concat('`', name.OrdinalReplace("`", "``"), '`');
}
