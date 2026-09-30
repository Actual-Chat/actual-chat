namespace ActualChat.Users.UnitTests.Coach;

public class CoachSkillSetsTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void NativeHeadlineShouldLeadWithFillersAndLearningWithWeakWords()
    {
        CoachSkillSets.Headline(CoachLanguageLevel.Native).Should().Equal(
            CoachMetricKind.Fillers, CoachMetricKind.Pace, CoachMetricKind.TurnTaking, CoachMetricKind.Monologue);
        CoachSkillSets.Headline(CoachLanguageLevel.Learning).Should().Equal(
            CoachMetricKind.WeakWords, CoachMetricKind.Vocabulary,
            CoachMetricKind.SentenceLength, CoachMetricKind.Pace);
    }

    [Fact]
    public void ConversationSkillsShouldNotBeLanguageBound()
    {
        foreach (var kind in CoachSkillSets.Conversation)
            CoachSkillSets.IsLanguageBound(kind).Should().BeFalse(kind.ToString());
        CoachSkillSets.IsLanguageBound(CoachMetricKind.Fillers).Should().BeTrue();
    }
}
