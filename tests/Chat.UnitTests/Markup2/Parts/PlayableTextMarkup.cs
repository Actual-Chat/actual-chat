namespace ActualChat.Chat.UnitTests.Markup2;

public sealed record PlayableTextMarkup(string Text, LinearMap TextToTimeMap) : TextMarkup
{
    public PlayableTextMarkup() : this("", default) { }

    public override string ToPlainText()
        => Text;
}
