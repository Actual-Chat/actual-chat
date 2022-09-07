namespace ActualChat.Chat;

public sealed record Mention(
    string Id,
    string Name = ""
    ) : Markup
{
    public Mention() : this("") { }

    public override string Format()
        => Name.IsNullOrEmpty()
            ? $"@{Id}"
            : $"@`{Name.OrdinalReplace("`", "``")}`{Id}";

    public string Format(MentionFormat format)
        => format switch {
            MentionFormat.NameOnly => $"@{Name.NullIfEmpty() ?? Id}",
            MentionFormat.Full => Format(),
            MentionFormat.PreferNameOnly when Name.IsNullOrEmpty() => Format(),
            _ => $"@`{Name.OrdinalReplace("`", "``")}`",
        };
}
