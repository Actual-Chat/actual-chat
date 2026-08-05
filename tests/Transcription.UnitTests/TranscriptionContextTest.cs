namespace ActualChat.Transcription.UnitTests;

public class TranscriptionContextTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void RenderKeepsParticipantsAndNewestEntries()
    {
        // arrange
        var context = NewContext("Croatia trip", ["Alex", "Roza"], ["oldest", "middle", "newest"]);

        // act
        var text = context.Render(1000);

        // assert
        WriteLine(text.Text);
        text.Summary.Should().Be("Croatia trip");
        text.Text.Should().Be("Participants: Alex, Roza\noldest\nmiddle\nnewest");
    }

    [Fact]
    public void RenderDropsOldestEntriesFirst()
    {
        // arrange
        var context = NewContext("", [], ["aaaaaaaaaa", "bbbbbbbbbb", "cccccccccc"]);

        // act
        var text = context.Render(25);

        // assert
        text.Text.Should().Be("bbbbbbbbbb\ncccccccccc");
    }

    [Fact]
    public void RenderTruncatesSummaryFromTheEnd()
    {
        // arrange
        var context = NewContext(new string('s', 500), [], []);

        // act
        var text = context.Render(100);

        // assert
        text.Summary.Length.Should().Be(40);
    }

    [Fact]
    public void RenderYieldsNoneForZeroBudget()
    {
        // act
        var text = NewContext("topic", ["Alex"], ["hi"]).Render(0);

        // assert
        text.IsNone.Should().BeTrue();
    }

    [Fact]
    public void RenderSkipsParticipantsThatDoNotFit()
    {
        // arrange
        var context = NewContext("", ["Averyveryverylongparticipantname"], ["hi"]);

        // act
        var text = context.Render(20);

        // assert
        text.Text.Should().Be("hi");
    }

    // Private methods

    private static TranscriptionContext NewContext(string summary, string[] authors, string[] entries)
        => new() {
            Summary = summary,
            Authors = authors.Select((x, i) => new TranscriptionAuthor(i, x)).ToApiArray(),
            Prefix = entries.Select((x, i) => new TranscriptionContextEntry(i, x)).ToApiArray(),
        };
}
