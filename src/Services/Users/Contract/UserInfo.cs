using Cysharp.Text;
using Stl.Fusion.Authentication;
using Stl.Text;

namespace ActualChat.Users
{
    public record UserInfo(Symbol Id, string Name = "(unknown)")
    {
        public static UserInfo None { get; } = new();

        public UserInfo() : this(Symbol.Empty, "(none)") { }
    }
}
