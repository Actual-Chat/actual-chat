using ActualChat.Chat;
using ActualChat.Streaming.Services;

namespace ActualChat.Streaming.UnitTests;

public class VoiceSampleBuilderTest
{
    private static readonly ChatId ChatId = ChatId.Parse("the-actual-one");
    private static readonly Moment Now = new(new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void LongestEntriesShouldComeFirstUpToTheMaxDuration()
    {
        // arrange
        var entries = new[] {
            Entry(1, seconds: 10),
            Entry(2, seconds: 25),
            Entry(3, seconds: 15),
            Entry(4, seconds: 20),
            Entry(5, seconds: 8),
        };

        // act
        var selected = VoiceSampleBuilder.SelectEntries(entries, Now);

        // assert
        selected.Select(x => x.LocalId).Should().Equal([2, 4, 3],
            "the longest come first and nothing more is taken once 60 s are covered");
        selected.Sum(x => x.Duration!.Value).Should().BeGreaterThanOrEqualTo(60);
    }

    [Fact]
    public void ShortRemovedStreamingAndOldEntriesShouldBeSkipped()
    {
        // arrange
        var entries = new[] {
            Entry(1, seconds: 4),
            Entry(2, seconds: 40) with { IsRemoved = true },
            Entry(3, seconds: 40, ageDays: 91),
            Entry(4, seconds: 40, ageDays: 89),
            Entry(5, seconds: 40) with { Audio = null },
            Entry(6, seconds: 40, isStreaming: true),
            Entry(7, seconds: 5),
        };

        // act
        var selected = VoiceSampleBuilder.SelectEntries(entries, Now);

        // assert
        selected.Select(x => x.LocalId).Should().Equal([4, 7],
            "only stored audio of at least 5 s from the last 90 days qualifies");
    }

    [Fact]
    public void SelectionShouldStopOnceTheMaxDurationIsReached()
    {
        // arrange
        var entries = Enumerable.Range(1, 10).Select(i => Entry(i, seconds: 30)).ToArray();

        // act
        var selected = VoiceSampleBuilder.SelectEntries(entries, Now);

        // assert
        selected.Should().HaveCount(2, "two 30 s entries already cover the 60 s cap");
    }

    [Fact]
    public void TotalDurationShouldBeCappedAtTheMaxDuration()
    {
        // act & assert
        VoiceSampleBuilder.TotalDuration([Entry(1, seconds: 50), Entry(2, seconds: 50)])
            .Should().Be(Constants.Audio.VoiceSampleMaxDuration);
        VoiceSampleBuilder.TotalDuration([Entry(1, seconds: 20)])
            .Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void HashShouldDependOnTheOrderedIdListOnly()
    {
        // arrange
        var ids = new[] { ChatEntryId.New(ChatId, 1), ChatEntryId.New(ChatId, 2) };

        // act
        var hash = VoiceSampleBuilder.HashOf(ids);
        var again = VoiceSampleBuilder.HashOf(ids.ToList());
        var reversed = VoiceSampleBuilder.HashOf(ids.Reverse());

        // assert
        hash.IsNone.Should().BeFalse();
        again.Should().Be(hash, "the same list hashes the same");
        reversed.Should().NotBe(hash, "the order is part of the selection");
        VoiceSampleBuilder.ShortHashOf(hash).Should().HaveLength(8).And.MatchRegex("^[A-Za-z0-9]+$",
            "it names a blob and a Soniox voice");
    }

    [Fact]
    public void SelectionShouldBeDeterministicForEqualDurations()
    {
        // arrange
        var entries = Enumerable.Range(1, 6).Select(i => Entry(i, seconds: 12)).ToArray();

        // act
        var first = VoiceSampleBuilder.SelectEntries(entries, Now);
        var second = VoiceSampleBuilder.SelectEntries(entries.Reverse(), Now);

        // assert
        first.Select(x => x.Id).Should().Equal(second.Select(x => x.Id),
            "ties are broken by id, not by the input order");
        VoiceSampleBuilder.HashOf(first.Select(x => x.Id))
            .Should().Be(VoiceSampleBuilder.HashOf(second.Select(x => x.Id)));
    }

    // Private methods

    private static TextEntry Entry(long lid, double seconds, double ageDays = 1, bool isStreaming = false)
    {
        var beginsAt = Now - TimeSpan.FromDays(ageDays);
        var endsAt = beginsAt + TimeSpan.FromSeconds(seconds);
        return new TextEntry(ChatEntryId.New(ChatId, lid)) {
            AuthorId = AuthorId.New(ChatId, 10),
            BeginsAt = beginsAt,
            EndsAt = endsAt,
            Audio = new ChatEntryAudio {
                BlobId = $"audio-record/blob-{lid}.webm",
                StreamId = isStreaming ? $"stream-{lid}" : "",
                BeginsAt = beginsAt,
                EndsAt = endsAt,
            },
        };
    }
}
