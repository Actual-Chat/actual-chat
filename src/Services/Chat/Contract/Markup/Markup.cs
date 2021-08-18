using System;
using System.Linq;
using Stl.Collections;
using Stl.Serialization;

namespace ActualChat.Chat.Markup
{
    public class Markup
    {
        public string Text { get; init; } = "";
        public MarkupTokens.Any[] Tokens { get; init; } = Array.Empty<MarkupTokens.Any>();

        public Markup() { }
        public Markup(string text, params MarkupTokens.Any[] tokens)
        {
            Text = text;
            Tokens = tokens;
        }

        public override string ToString()
        {
            var sElements = Tokens.Select(e => e.ToString()).ToDelimitedString($",{Environment.NewLine}  ");
            if (!string.IsNullOrEmpty(sElements))
                sElements = $",{Environment.NewLine}  {sElements}";
            return $"{GetType().Name}({SystemJsonSerializer.Default.Write(Text)}{sElements})";
        }
    }
}
