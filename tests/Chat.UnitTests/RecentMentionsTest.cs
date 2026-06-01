namespace ActualChat.Chat.UnitTests;

public class RecentMentionsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment Now = new(new DateTime(2026, 06, 01, 12, 00, 00, DateTimeKind.Utc));

    [Fact]
    public void UseAddsAndDeduplicates()
    {
        var id = MentionRef.NewUser(UserId.New());
        var recents = new RecentMentions().Use(id, Now);
        recents.Items.Count.Should().Be(1);
        recents.Items[0].Uses.Count.Should().Be(1);

        recents = recents.Use(id, Now + TimeSpan.FromMinutes(1));
        recents.Items.Count.Should().Be(1); // de-duplicated
        recents.Items[0].Uses.Count.Should().Be(2); // both uses kept, newest first
    }

    [Fact]
    public void UseCapsUsesPerItem()
    {
        var id = MentionRef.NewUser(UserId.New());
        var recents = new RecentMentions();
        for (var i = 0; i < RecentMentions.MaxUsesPerItem + 3; i++)
            recents = recents.Use(id, Now + TimeSpan.FromMinutes(i));
        recents.Items[0].Uses.Count.Should().Be(RecentMentions.MaxUsesPerItem);
    }

    [Fact]
    public void UseEvictsLowestScoredBeyondMaxCount()
    {
        var recents = new RecentMentions();
        // Oldest item, used long ago — should be evicted once we exceed MaxCount.
        var stale = MentionRef.NewUser(UserId.New());
        recents = recents.Use(stale, Now - TimeSpan.FromDays(365));
        for (var i = 0; i < RecentMentions.MaxCount; i++)
            recents = recents.Use(MentionRef.NewUser(UserId.New()), Now);

        recents.Items.Count.Should().Be(RecentMentions.MaxCount);
        recents.Items.Select(x => x.Id).Should().NotContain(stale);
    }

    [Fact]
    public void ScoreFavorsMoreRecentUse()
    {
        var recent = new RecentMention { Id = MentionRef.NewUser(UserId.New()), Uses = [Now] };
        var old = new RecentMention {
            Id = MentionRef.NewUser(UserId.New()),
            Uses = [Now - RecentMentions.HalfLife],
        };
        var recentScore = RecentMentions.ComputeScore(recent, Now);
        var oldScore = RecentMentions.ComputeScore(old, Now);

        recentScore.Should().BeApproximately(1.0, 1e-9);
        oldScore.Should().BeApproximately(0.5, 1e-9); // one half-life → halved
        recentScore.Should().BeGreaterThan(oldScore);
    }

    [Fact]
    public void ScoreRewardsFrequency()
    {
        var once = new RecentMention { Id = MentionRef.NewUser(UserId.New()), Uses = [Now] };
        var twice = new RecentMention { Id = MentionRef.NewUser(UserId.New()), Uses = [Now, Now] };
        RecentMentions.ComputeScore(twice, Now)
            .Should().BeGreaterThan(RecentMentions.ComputeScore(once, Now));
    }

    [Fact]
    public void GetScoresMapsEveryItem()
    {
        var id1 = MentionRef.NewUser(UserId.New());
        var id2 = MentionRef.NewChat(GroupChatId.New());
        var recents = new RecentMentions().Use(id1, Now).Use(id2, Now);
        var scores = recents.GetScores(Now);
        scores.Should().ContainKeys(id1, id2);
    }
}
