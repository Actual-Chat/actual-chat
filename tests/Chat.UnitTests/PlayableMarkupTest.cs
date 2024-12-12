namespace ActualChat.Chat.UnitTests;

public class PlayableMarkupTest
{
    [Fact]
    public void PlayableTextMarkup_ShouldNotAddSpaces()
    {
        var text = " Я бы сказал, что, наверное, не стоит То есть, это мы должны всё-таки решать.";
        var timeMap = new LinearMap(0,0,1,0.35999998f,2,0.35999998f,4,0.71999997f,18,1.12f,31,1.76f,36,2.53f,38,2.53f,40,3.03f,41,3.49f,47,3.65f,51,4.05f,53,4.4399996f,61,4.6899996f,77,5.57f);
        var m = new PlayableTextMarkup(text, timeMap);
        m.Text.Should().Be(text);
        var words = m.Words;
        words.Count.Should().Be(14);
        words[^1].Value.Should().Be("решать.");
    }
}
