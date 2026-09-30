using ActualChat.UI.Blazor.App.Components;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CoachFindingsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static CoachConversation Conversation(CoachBand pace, CoachBand share, CoachBand monologue, CoachBand filler)
        => new (GroupChatId.New(), 1, Moment.EpochStart, Moment.EpochStart, "en", null, 200, 100, 2, 0, 120,
            0.6, 100, new ApiMap<string, int>(), new ApiMap<string, int>()) {
            PaceBand = pace,
            TalkShareBand = share,
            MonologueBand = monologue,
            FillerBand = filler,
            Participants = 3,
        };

    [Fact]
    public void PickShouldLeadWithProblemsThenTheFocusThenGoodNews()
    {
        // arrange
        var c = Conversation(CoachBand.Good, CoachBand.High, CoachBand.Good, CoachBand.Good);

        // act
        var findings = CoachFindings.Pick(c, CoachMetricKind.Fillers, CoachLanguageLevel.Native, isWeeksBest: false);

        // assert
        findings.Select(f => f.Kind).Should().Equal(CoachMetricKind.TurnTaking, CoachMetricKind.Fillers, CoachMetricKind.Pace);
    }

    [Fact]
    public void PickShouldNeverExceedThreeAndNeverRepeat()
    {
        var c = Conversation(CoachBand.High, CoachBand.High, CoachBand.High, CoachBand.High);
        var findings = CoachFindings.Pick(c, CoachMetricKind.Pace, CoachLanguageLevel.Native, false);
        findings.Should().HaveCount(3);
        findings.Select(f => f.Kind).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void PickShouldSkipSkillsWithoutData()
    {
        var c = Conversation(CoachBand.None, CoachBand.None, CoachBand.None, CoachBand.Good);
        CoachFindings.Pick(c, null, CoachLanguageLevel.Native, false).Select(f => f.Kind)
            .Should().Equal(CoachMetricKind.Fillers);
    }

    [Fact]
    public void PickShouldNotJudgeTalkShareOfAConversationWithOneParticipant()
    {
        // arrange
        var c = Conversation(CoachBand.Good, CoachBand.Good, CoachBand.Good, CoachBand.Good) with { Participants = 1 };

        // act
        var findings = CoachFindings.Pick(c, null, CoachLanguageLevel.Native, false);

        // assert
        findings.Select(f => f.Kind).Should().NotContain(CoachMetricKind.TurnTaking, "solo talk has no share to judge");
    }
}
