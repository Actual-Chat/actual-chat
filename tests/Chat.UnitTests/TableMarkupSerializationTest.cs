namespace ActualChat.Chat.UnitTests;

public sealed class TableMarkupSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void PassesThroughMessagePack()
    {
        // arrange
        var markup = MarkupParser.ParseRaw("| a | b |\n| :-- | --: |\n| 1 | @u:userId |").Simplify();

        // act
        var result = markup.PassThroughModernSerializers(Out);

        // assert
        var table = result.Should().BeOfType<TableMarkup>().Subject;
        table.ColumnCount.Should().Be(2);
        table.Alignments.Should().Equal(TableColumnAlignment.Left, TableColumnAlignment.Right);
        table.Header.Cells[0].Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("a");
        table.Rows.Length.Should().Be(1);
        table.Rows[0].Cells[1].Content.Should().BeOfType<UserMention>();
        table.Format().Should().Be(markup.Format());
    }
}
