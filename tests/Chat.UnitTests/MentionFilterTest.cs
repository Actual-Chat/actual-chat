namespace ActualChat.Chat.UnitTests;

public class MentionFilterTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly UserId AnyUser = UserId.New();

    [Fact]
    public void TokenizesOnWhitespaceAndPunctuation()
    {
        MentionFilter.Tokenize("John Bolton").Should().BeEquivalentTo("john", "bolton");
        MentionFilter.Tokenize("bob-john").Should().BeEquivalentTo("bob", "john");
        MentionFilter.Tokenize("first.last_name").Should().BeEquivalentTo("first", "last", "name");
        MentionFilter.Tokenize("  spaced   out  ").Should().BeEquivalentTo("spaced", "out");
        MentionFilter.Tokenize("").Should().BeEmpty();
        MentionFilter.Tokenize(null).Should().BeEmpty();
    }

    [Fact]
    public void MatchesAllRequiresEveryQueryTokenToHitSomeWord()
    {
        // Per spec: "J B" matches John Bolton, Bolton John, Bob Johnson — but not Alice Bolton.
        MentionFilter.MatchesAll(["j", "b"], ["john", "bolton"]).Should().BeTrue();
        MentionFilter.MatchesAll(["j", "b"], ["bolton", "john"]).Should().BeTrue();
        MentionFilter.MatchesAll(["j", "b"], ["bob", "johnson"]).Should().BeTrue();
        MentionFilter.MatchesAll(["j", "b"], ["alice", "bolton"]).Should().BeFalse();
    }

    [Fact]
    public void MatchesAllIsCaseInsensitive()
    {
        // Caller lowercases via Tokenize; verify direct comparison stays case-sensitive
        // (caller's contract) but the full pipeline lowercases.
        var q = MentionFilter.Tokenize("J B");
        MentionFilter.MatchesAll(q, MentionFilter.Tokenize("John Bolton")).Should().BeTrue();
        MentionFilter.MatchesAll(q, MentionFilter.Tokenize("JOHN BOLTON")).Should().BeTrue();
    }

    [Fact]
    public void EmptyQueryMatchesEverything()
    {
        var pool = new[] {
            User("Alice"),
            User("Bob"),
        };
        var result = MentionFilter.FilterAndRank(pool, "", MentionKindFilter.All, 10);
        result.Should().HaveCount(2);
    }

    [Fact]
    public void RanksUsersBeforeChatsBeforeEmojis()
    {
        var pool = new[] {
            Emoji("Smile face"),
            Chat("Smile chat"),
            User("Smile user"),
        };
        var result = MentionFilter.FilterAndRank(pool, "smile", MentionKindFilter.All, 10);
        result.Select(c => c.Kind).Should().Equal(
            MentionCandidateKind.User,
            MentionCandidateKind.Chat,
            MentionCandidateKind.Emoji);
    }

    [Fact]
    public void RanksChatMembersBeforeNonMembers()
    {
        var pool = new[] {
            User("Alex Non") with { IsChatMember = false },
            User("Alex Member") with { IsChatMember = true },
        };
        var result = MentionFilter.FilterAndRank(pool, "alex", MentionKindFilter.All, 10);
        result[0].PrimaryName.Should().Be("Alex Member");
        result[1].PrimaryName.Should().Be("Alex Non");
    }

    [Fact]
    public void KindFilterNarrowsToSelectedCategory()
    {
        var pool = new[] {
            User("Smiley"),
            Chat("Smile-only chat"),
            Emoji("Smiley face"),
        };
        var result = MentionFilter.FilterAndRank(pool, "smile", MentionKindFilter.Chat, 10);
        result.Should().HaveCount(1);
        result[0].Kind.Should().Be(MentionCandidateKind.Chat);
    }

    [Fact]
    public void HigherCoverageRanksHigherWithinKind()
    {
        var pool = new[] {
            User("Alexander"),     // "alex" covers 4 / 9 chars
            User("Al"),             // "alex" doesn't match — too short
            User("Alex"),           // "alex" covers 4 / 4 chars — best
        };
        var result = MentionFilter.FilterAndRank(pool, "alex", MentionKindFilter.User, 10);
        result.Select(c => c.PrimaryName).Should().Equal("Alex", "Alexander");
    }

    [Fact]
    public void DoesNotMatchInfix()
    {
        // "ohn" should NOT match "John" — prefix only.
        var pool = new[] {
            User("John Bolton"),
        };
        var result = MentionFilter.FilterAndRank(pool, "ohn", MentionKindFilter.User, 10);
        result.Should().BeEmpty();
    }

    private static MentionCandidate User(string name)
        => new(
            MentionId.NewUser(AnyUser),
            MentionCandidateKind.User,
            name,
            null,
            null,
            MentionFilter.Tokenize(name));

    private static MentionCandidate Chat(string name)
        => new(
            MentionId.NewChat(GroupChatId.New()),
            MentionCandidateKind.Chat,
            name,
            null,
            null,
            MentionFilter.Tokenize(name));

    private static MentionCandidate Emoji(string title)
    {
        // EmojiRef wraps a parser-safe id; use the title's first word as the slug so
        // tests don't depend on URL-encoding behavior.
        var slug = title.ToLowerInvariant().Split(' ')[0];
        return new(
            MentionId.NewEmoji(EmojiRef.Parse(slug)),
            MentionCandidateKind.Emoji,
            title,
            null,
            null,
            MentionFilter.Tokenize(title));
    }
}
