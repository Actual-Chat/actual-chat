using Cysharp.Text;
using Stl.Fusion.Authentication;
using Stl.Text;

namespace ActualChat.Users
{
    public record Speaker(Symbol Id, string Name = "(unknown)")
    {
        public static Speaker None { get; } = new();

        public Speaker() : this(Symbol.Empty, "(none)") { }
    }
}
