using System;
using ActualChat.Users;
using Stl.Text;

namespace ActualChat.Chat
{
    public static partial class MarkupParts
    {
        public abstract record Part(int Start, int Length)
        {
            public ReadOnlySpan<char> GetSourceSpan(Markup markup)
                => markup.Text.AsSpan(Start, Length);
        }

        public abstract record Text(int Start, int Length) : Part(Start, Length)
        {
            public abstract ReadOnlySpan<char> GetText(Markup markup);
        }

        public record RawText(int Start, int Length) : Text(Start, Length)
        {
            public override ReadOnlySpan<char> GetText(Markup markup)
                => GetSourceSpan(markup);
        }

        public record EscapedSymbol(int Start, int Length) : Text(Start, Length)
        {
            public override ReadOnlySpan<char> GetText(Markup markup)
                => GetSourceSpan(markup)[1..];
        }

        public record UserMention(int Start, int Length, UserInfo? User) : Part(Start, Length)
        { }
    }
}
