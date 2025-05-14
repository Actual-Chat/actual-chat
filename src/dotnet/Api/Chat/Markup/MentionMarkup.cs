using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MentionMarkup(MentionId id, string name = "") : Markup
{
    public static readonly string NotAvailableName = "(n/a)";
    public static readonly Func<MentionMarkup, string> DefaultFormatter = m => m.Format();
    public static readonly Func<MentionMarkup, string> NameOrNotAvailableFormatter = m => "@" + m.NameOrNotAvailable;
    public static readonly Func<MentionMarkup, string> NameOrIdFormatter = m => "@" + m.NameOrId;

    [DataMember, MemoryPackOrder(0)]
    public MentionId Id { get; } = id;
    [DataMember, MemoryPackOrder(1)]
    public string Name { get; } = name;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string QuotedName => Quote(Name);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string NameOrNotAvailable => Name.NullIfEmpty() ?? NotAvailableName;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string NameOrId => Name.NullIfEmpty() ?? Id.Value;

    public override string Format()
        => Name.IsNullOrEmpty()
            ? "@" + Id
            : string.Concat("@", QuotedName, Id);

    public static string Quote(string name)
        => string.Concat("`", name.OrdinalReplace("`", "``"), "`");
}
