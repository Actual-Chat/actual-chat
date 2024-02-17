using System.Text;

namespace ActualChat.Chat;

public sealed record CodeBlockMarkup(
    string Code,
    string Language = ""
    ) : Markup
{
    public CodeBlockMarkup() : this("") { }

    public override string Format()
        => $"```{Language}\r\n{Code}```";

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append(nameof(Code));
        builder.Append(" = ```");
        builder.Append(Language);
        builder.Append("\r\n");
        builder.Append(Code);
        builder.Append("```");
        return true; // Indicates there is no comma / tail "}" must be prefixed with space
    }
}
