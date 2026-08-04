using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents a fenced code block with optional language.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class CodeBlockMarkup(string code, string language = "") : BlockMarkup
{
    [DataMember, Key(0)]
    public string Code { get; } = code ?? throw new ArgumentNullException(nameof(code));
    [DataMember, Key(1)]
    public string Language { get; } = language;

    public override string Format()
        => Code.Length > 0
            ? $"```{Language}\r\n{Code}\r\n```"
            : $"```{Language}\r\n```";
}
