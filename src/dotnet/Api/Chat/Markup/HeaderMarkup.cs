using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents a Markdown-style header (#, ##, ###) with inline content.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class HeaderMarkup : BlockMarkup
{
    public const int MinLevel = 1;
    public const int MaxLevel = 3;

    [DataMember, Key(0)]
    public int Level { get; }
    [DataMember, Key(1)]
    public Markup Content { get; }

    public HeaderMarkup(int level, Markup content)
    {
        if (level is < MinLevel or > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Level must be in [{MinLevel}, {MaxLevel}].");
        if (content.IsBlockMarkup())
            throw new ArgumentException("Content must not be a block markup", nameof(content));

        Level = level;
        Content = content;
    }

    public override string Format()
        => new string('#', Level) + " " + Content.Format();

    public override Markup Simplify()
    {
        var simplified = Content.Simplify();
        return ReferenceEquals(simplified, Content) ? this : new HeaderMarkup(Level, simplified);
    }
}
