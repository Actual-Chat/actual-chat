using ActualChat.Live;
using ActualChat.Users;

namespace ActualChat.Chat.UnitTests;

public sealed class SessionRulesTest
{
    [Theory]
    [InlineData(null, true, true)]
    [InlineData(VoiceMode.TextAndVoice, true, true)]
    [InlineData(VoiceMode.JustVoice, false, true)]
    [InlineData(VoiceMode.JustText, true, false)]
    public void OverrideShouldDecideTextAndVoice(VoiceMode? voiceModeOverride, bool hasText, bool hasVoice)
    {
        // arrange
        var rules = SessionRules.Default with { VoiceModeOverride = voiceModeOverride };

        // act + assert
        rules.HasText().Should().Be(hasText);
        rules.HasVoice().Should().Be(hasVoice);
    }

    [Fact]
    public void WithTextShouldToggleTranscriptionAndKeepTheRest()
    {
        // arrange
        var rules = SessionRules.Default with { VideoAllowed = false };

        // act
        var off = rules.WithText(false);
        var on = off.WithText(true);

        // assert
        off.HasText().Should().BeFalse();
        off.HasVoice().Should().BeTrue();
        off.VideoAllowed.Should().BeFalse();
        on.HasText().Should().BeTrue();
        on.HasVoice().Should().BeTrue();
    }

    [Fact]
    public void WithTextShouldKeepTheTranscriptOfAVoicelessSession()
    {
        // arrange
        var rules = SessionRules.Default with { VoiceModeOverride = VoiceMode.JustText };

        // act
        var off = rules.WithText(false);

        // assert
        off.Should().Be(rules, "VoiceMode can't express a session with neither voice nor transcript");
    }
}
