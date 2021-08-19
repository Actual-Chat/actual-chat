using System;
using System.Linq;
using Stl.Collections;
using Stl.Serialization;

namespace ActualChat.Chat.Markup
{
    public class Markup
    {
        public string Text { get; init; } = "";
        public MarkupParts.Any[] Parts { get; init; } = Array.Empty<MarkupParts.Any>();

        public Markup() { }
        public Markup(string text, MarkupParts.Any[] parts)
        {
            Text = text;
            Parts = parts;
        }

        public override string ToString()
        {
            var sParts = Parts.Select(e => e.ToString()).ToDelimitedString($",{Environment.NewLine}  ");
            if (!string.IsNullOrEmpty(sParts))
                sParts = $",{Environment.NewLine}  {sParts}";
            return $"{GetType().Name}({SystemJsonSerializer.Default.Write(Text)}{sParts})";
        }
    }
}
