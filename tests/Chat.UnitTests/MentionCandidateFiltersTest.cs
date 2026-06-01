using ActualChat.Search;

namespace ActualChat.Chat.UnitTests;

public class MentionCandidateFiltersTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly UserId AnyUser = UserId.New();

    [Fact]
    public void EmptyQueryMatchesEverything()
    {
        var pool = new[] {
            User("Alice"),
            User("Bob"),
        }.ToApiArray();
        var result = pool.FilterAndRank(MentionCandidateFilters.All, "", 10);
        result.Should().HaveCount(2);
    }

    [Fact]
    public void RanksUsersBeforeChatsBeforeEmojis()
    {
        var pool = new[] {
            Emoji("Smile face"),
            Chat("Smile chat"),
            User("Smile user"),
        }.ToApiArray();
        var result = pool.FilterAndRank(MentionCandidateFilters.All, "smile", 10);
        result.Select(c => c.Id.Kind).Should().Equal(
            MentionKind.User,
            MentionKind.Chat,
            MentionKind.Emoji);
    }

    [Fact]
    public void RanksChatMembersBeforeNonMembers()
    {
        var pool = new[] {
            User("Alex Non") with { IsChatMember = false },
            User("Alex Member") with { IsChatMember = true },
        }.ToApiArray();
        var result = pool.FilterAndRank(MentionCandidateFilters.All, "alex", 10);
        result[0].Title.Should().Be("Alex Member");
        result[1].Title.Should().Be("Alex Non");
    }

    [Fact]
    public void KindFilterNarrowsToSelectedCategory()
    {
        var pool = new[] {
            User("Smiley"),
            Chat("Smile-only chat"),
            Emoji("Smiley face"),
        }.ToApiArray();
        var result = pool.FilterAndRank(MentionCandidateFilters.Chat, "smile", 10);
        result.Should().HaveCount(1);
        result[0].Id.Kind.Should().Be(MentionKind.Chat);
    }

    [Fact]
    public void HigherCoverageRanksHigherWithinKind()
    {
        var pool = new[] {
            User("Alexander"),     // "alex" covers 4 / 9 chars
            User("Al"),             // "alex" doesn't match — too short
            User("Alex"),           // "alex" covers 4 / 4 chars — best
        }.ToApiArray();
        var result = pool.FilterAndRank(MentionCandidateFilters.User, "alex", 10);
        result.Select(c => c.Title).Should().Equal("Alex", "Alexander");
    }

    [Fact]
    public void DoesNotMatchInfix()
    {
        // "ohn" should NOT match "John" — word-prefix only.
        var pool = new[] {
            User("John Bolton"),
        }.ToApiArray();
        var result = pool.FilterAndRank(MentionCandidateFilters.User, "ohn", 10);
        result.Should().BeEmpty();
    }

    [Fact]
    public void PlaceChatIsMatchedByPlaceName()
    {
        // A place chat's MemSearchDocument carries the place name first, so typing the place finds it.
        var pool = new[] {
            Chat("Funny Chat", placeName: "Fusion Place"),
        }.ToApiArray();
        pool.FilterAndRank(MentionCandidateFilters.Chat, "fusion", 10).Should().HaveCount(1);
        pool.FilterAndRank(MentionCandidateFilters.Chat, "fusion fun", 10).Should().HaveCount(1);
        pool.FilterAndRank(MentionCandidateFilters.Chat, "funny", 10).Should().HaveCount(1);
    }

    [Fact]
    public void RecentMatchIsBoostedWithinKindOnTypedQuery()
    {
        // Same kind, same membership, equal coverage for "alex" → recency breaks the tie.
        var recentId = UserId.New();
        var recent = User("Alex Lee", recentId);
        var other = User("Alex Kim", UserId.New());
        var pool = new[] { other, recent }.ToApiArray();

        var scores = new Dictionary<MentionRef, double> { [recent.Id] = 1.0 };
        var result = pool.FilterAndRank(MentionCandidateFilters.User, "alex", 10, scores);
        result[0].Title.Should().Be("Alex Lee");
    }

    [Fact]
    public void StrongerTextMatchStillBeatsWeakRecentBoost()
    {
        // Additive boost, not absolute: a clearly better text match wins over a recent one.
        var recentId = UserId.New();
        var recent = User("Alexander", recentId);  // weaker coverage for "alex"
        var exact = User("Alex", UserId.New());     // exact coverage
        var pool = new[] { recent, exact }.ToApiArray();

        var scores = new Dictionary<MentionRef, double> { [recent.Id] = 1.0 };
        var result = pool.FilterAndRank(MentionCandidateFilters.User, "alex", 10, scores);
        result[0].Title.Should().Be("Alex");
    }

    [Fact]
    public void EmptyQueryShowsRecentsFirstByScore()
    {
        var a = User("Anna", UserId.New());
        var b = User("Boris", UserId.New());
        var c = User("Cleo", UserId.New());
        var pool = new[] { a, b, c }.ToApiArray();

        var scores = new Dictionary<MentionRef, double> { [c.Id] = 2.0, [a.Id] = 1.0 };
        var result = pool.FilterAndRank(MentionCandidateFilters.All, "", 10, scores);
        result.Select(x => x.Title).Take(2).Should().Equal("Cleo", "Anna");
    }

    private static MentionCandidate User(string name)
        => new(
            MentionRef.NewUser(AnyUser),
            name,
            null,
            new SearchDocument(name));

    private static MentionCandidate User(string name, UserId userId, bool isMember = false)
        => new(
            MentionRef.NewUser(userId),
            name,
            null,
            new SearchDocument(name)) {
            IsChatMember = isMember,
        };

    private static MentionCandidate Chat(string name, string? placeName = null)
        => new(
            MentionRef.NewChat(GroupChatId.New()),
            name,
            null,
            new SearchDocument(placeName, name));

    private static MentionCandidate Emoji(string title)
    {
        // EmojiRef wraps a parser-safe id; use the title's first word as the slug so
        // tests don't depend on URL-encoding behavior.
        var slug = title.ToLower().Split(' ')[0];
        return new(
            MentionRef.NewEmoji(EmojiRef.Parse(slug)),
            title,
            null,
            new SearchDocument(title));
    }
}
