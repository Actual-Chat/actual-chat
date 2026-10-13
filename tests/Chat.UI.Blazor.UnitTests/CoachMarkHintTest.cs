using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CoachMarkHintTest
{
    [Theory]
    [InlineData("First. I like like this! Next.", "like", 14, "I like ", "like", " this!")]
    [InlineData("前句。我那个想试试。后句。", "那个", 4, "我", "那个", "想试试。")]
    [InlineData("I KIND OF agree", "kind of", 2, "I ", "KIND OF", " agree")]
    public void ContextShouldHighlightTheSelectedOccurrence(
        string text, string word, int start,
        string before, string selected, string after)
    {
        // arrange
        var span = new SpeechSpan(SpeechSpanKind.Filler, word, start, word.Length, ApiArray<string>.Empty);

        // act
        var context = CoachMarkHint.GetContext(text, span);

        // assert
        context.Should().Be(new CoachMarkHint.Context(before, selected, after));
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(2, 0)]
    [InlineData(2, 20)]
    [InlineData(20, 4)]
    [InlineData(0, 4)]
    public void ContextShouldRejectStaleOrInvalidOffsets(int start, int length)
    {
        // arrange
        var span = new SpeechSpan(SpeechSpanKind.Filler, "like", start, length, ApiArray<string>.Empty);

        // act
        var context = CoachMarkHint.GetContext("I like this", span);

        // assert
        context.Should().BeNull();
    }

    [Fact]
    public void ContextShouldLimitLongMessagesWithoutSplittingEmoji()
    {
        // arrange
        var before = new string('a', 120) + "😀" + new string('b', 99);
        var after = new string('c', 99) + "😀" + new string('d', 120);
        var span = new SpeechSpan(SpeechSpanKind.Filler, "like", before.Length, 4, ApiArray<string>.Empty);

        // act
        var context = CoachMarkHint.GetContext(before + "like" + after, span);

        // assert
        context.Should().Be(new CoachMarkHint.Context("…" + new string('b', 99), "like",
            new string('c', 99) + "…"));
    }

    private static CoachMarkHint NewHint()
        => new (new TestStringLocalizer(new() {
            ["Coach_TipWeakWordTitle"] = "Choose a different word next",
            ["Coach_TipWeakWordBody_Format"] = "The word “{0}” is often overused.",
            ["Coach_TipFillerTitle"] = "Avoid filler words",
            ["Coach_MarkFillerBody_Format"] = "“{0}” carries no meaning here.",
            ["Coach_MarkRepetitionTitle"] = "You said it twice in a row",
            ["Coach_MarkRepetitionBody_Format"] = "“{0}” came twice; once is enough.",
            ["Coach_MarkProfanityTitle"] = "Swear word",
            ["Coach_MarkProfanityBody_Format"] = "“{0}” is a swear word; a milder one may serve better.",
        }));

    [Fact]
    public void WeakWordHintShouldCarryTheSynonyms()
    {
        // arrange
        var span = new SpeechSpan(SpeechSpanKind.Weak, "awesome", 0, 7, ApiArray.New("excellent", "superb"));

        // act
        var hint = NewHint().For(span);

        // assert
        hint.Title.Should().Be("Choose a different word next");
        hint.Body.Should().Be("The word “awesome” is often overused.");
        hint.Synonyms.Should().Equal("excellent", "superb");
    }

    [Theory]
    [InlineData(SpeechSpanKind.Filler, "Avoid filler words", "“like” carries no meaning here.")]
    [InlineData(SpeechSpanKind.FilledPause, "Avoid filler words", "“like” carries no meaning here.")]
    [InlineData(SpeechSpanKind.Repetition, "You said it twice in a row", "“like” came twice; once is enough.")]
    public void FillerAndRepetitionHintsShouldExplainTheMark(SpeechSpanKind kind, string title, string body)
    {
        // arrange
        var span = new SpeechSpan(kind, "like", 0, 4, ApiArray<string>.Empty);

        // act
        var hint = NewHint().For(span);

        // assert
        hint.Title.Should().Be(title);
        hint.Body.Should().Be(body);
        hint.Synonyms.Should().BeEmpty();
    }

    [Fact]
    public void MenuArgumentsShouldRoundTripASpan()
    {
        // arrange
        var span = new SpeechSpan(SpeechSpanKind.Weak, "awesome", 3, 7, ApiArray.New("excellent", "superb"));

        // act
        var arguments = CoachMarkHint.ToArguments(span);
        var back = CoachMarkHint.FromArguments(arguments);

        // assert
        back.Kind.Should().Be(SpeechSpanKind.Weak);
        back.Word.Should().Be("awesome");
        back.Synonyms.Should().Equal("excellent", "superb");
    }

    [Fact]
    public void MenuArgumentsShouldRoundTripTheReplayPoint()
    {
        // arrange
        var span = new SpeechSpan(SpeechSpanKind.Filler, "like", 3, 4, ApiArray<string>.Empty);
        var chatId = ChatId.Parse("dpwo1tm0tw");
        var startAt = new Moment(638_000_000_000_000_000L);

        // act
        var arguments = CoachMarkHint.ToArguments(span, chatId, startAt);
        var point = CoachMarkHint.GetReplayPoint(arguments);

        // assert
        point.Should().Be(new CoachMarkHint.ReplayPoint(chatId, startAt));
        CoachMarkHint.FromArguments(arguments).Word.Should().Be("like");
    }

    [Fact]
    public void ReplayPointShouldBeAbsentWithoutReplayArguments()
    {
        // arrange
        var span = new SpeechSpan(SpeechSpanKind.Filler, "like", 3, 4, ApiArray<string>.Empty);

        // act
        var point = CoachMarkHint.GetReplayPoint(CoachMarkHint.ToArguments(span));

        // assert
        point.Should().BeNull();
    }

    [Fact]
    public void ProfanityHintShouldOfferMilderAlternatives()
    {
        // arrange
        var span = new SpeechSpan(SpeechSpanKind.Profanity, "damn", 0, 4, ApiArray.New("darn", "rats"));

        // act
        var hint = NewHint().For(span);

        // assert
        hint.Title.Should().Be("Swear word");
        hint.Body.Should().Be("“damn” is a swear word; a milder one may serve better.");
        hint.Synonyms.Should().Equal("darn", "rats");
    }
}
