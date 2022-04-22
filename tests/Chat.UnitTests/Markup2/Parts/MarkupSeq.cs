using System.Text;
using Cysharp.Text;

namespace ActualChat.Chat.UnitTests.Markup2;

public sealed record MarkupSeq(ImmutableArray<Markup> Items) : Markup
{
    public MarkupSeq() : this(ImmutableArray<Markup>.Empty) { }

    public override string ToPlainText()
    {
        using var sb = ZString.CreateStringBuilder();
        foreach (var item in Items)
            sb.Append(item.ToPlainText());
        return sb.ToString();
    }

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Items = [");
        builder.Append(Items.ToDelimitedString(", "));
        builder.Append("]");
        return true;
    }
}
