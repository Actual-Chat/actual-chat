using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CoachMarkHintTest
{
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

