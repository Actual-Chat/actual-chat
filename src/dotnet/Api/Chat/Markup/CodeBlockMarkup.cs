using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class CodeBlockMarkup(string code, string language = "") : Markup
{
    public string Code { get; } = code ?? throw new ArgumentNullException(nameof(code));
    public string Language { get; } = language;

    public override string Format()
        => $"```{Language}\r\n{Code}```";
}
