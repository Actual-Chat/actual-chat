using System.Collections.Immutable;
using System.Linq;
using ActualChat.Users;
using Stl.Collections;

namespace ActualChat.Chat
{
    public record ParsedMessage
    {
        public ImmutableList<MessageFragment> Fragments { get; init; } = ImmutableList<MessageFragment>.Empty;
        public string Text { get; init; } = "";

        public ParsedMessage() { }

        public ParsedMessage(ImmutableList<MessageFragment> fragments)
        {
            Fragments = fragments;
            Text = fragments.ToDelimitedString("");
        }

        public ParsedMessage(params MessageFragment[] fragments)
        {
            Fragments = ImmutableList<MessageFragment>.Empty.AddRange(fragments);
            Text = fragments.ToDelimitedString("");
        }

        public override string ToString() => Format();
        public virtual string Format(bool editable = false)
            => editable
                ? Fragments.Select(f => f.Format(true)).ToDelimitedString("")
                : Fragments.ToDelimitedString("");
    }

    public abstract record MessageFragment
    {
        public override string ToString() => Format();
        public abstract string Format(bool editable = false);
    }

    public record PlainText(string Text) : MessageFragment
    {
        public PlainText() : this("") { }
        public override string ToString() => Format();
        public override string Format(bool editable = false)
            => Text.Replace("@", "@@");
    }

    public record UserMention(UserInfo User) : MessageFragment
    {
        public UserMention() : this(UserInfo.None) { }
        public override string ToString() => Format();
        public override string Format(bool editable = false)
            => editable ? "@" + User.Name : $"@user[{User.Id}]";
    }
}
