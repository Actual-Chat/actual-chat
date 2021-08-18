using System;
using Stl.Text;

namespace ActualChat.Chat.Markup
{
    public static partial class MarkupTokens
    {
        public abstract record Any(Markup Markup, int Start, int Length)
        {
            public ReadOnlySpan<char> Span => Markup.Text.AsSpan(Start, Length);
            public string Text => new(Span);
        }

        public record PlainText(Markup Markup, int Start, int Length) : Any(Markup, Start, Length)
        { }

        public record UserMention(Markup Markup, int Start, int Length) : Any(Markup, Start, Length)
        {
            public Symbol UserId { get; init; } = Symbol.Empty;
        }
    }
}
