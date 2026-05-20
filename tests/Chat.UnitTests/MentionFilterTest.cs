namespace ActualChat.Chat.UnitTests;

public class MentionFilterTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly UserId AnyUser = UserId.New();

    [Fact]
    public void NormalizeLowercasesAndSpacePrefixesTokens()
    {
        MentionFilter.Normalize("John Bolton").Should().Be(" john bolton");
        MentionFilter.Normalize("bob-john").Should().Be(" bob john");
        MentionFilter.Normalize("first.last_name").Should().Be(" first last name");
        MentionFilter.Normalize("  spaced   out  ").Should().Be(" spaced out");
        MentionFilter.Normalize("").Should().Be("");
        MentionFilter.Normalize((string?)null).Should().Be("");
        // Multi-part: place name first, then chat title.
        MentionFilter.Normalize("Fusion Place", "Funny Chat").Should().Be(" fusion place funny chat");
    }

    [Fact]
    public void GetPrefixesSpacePrefixesEveryQueryToken()
    {
        MentionFilter.GetPrefixes("J B").Should().Equal(" j", " b");
        MentionFilter.GetPrefixes("JOHN").Should().Equal(" john");
        MentionFilter.GetPrefixes("").Should().BeEmpty();
        MentionFilter.GetPrefixes(null).Should().BeEmpty();
    }

    [Fact]
    public void MatchesRequiresEveryPrefixToHitAWordStart()
    {
        // Per spec: "J B" matches John Bolton, Bolton John, Bob Johnson — but not Alice Bolton.
        var jb = MentionFilter.GetPrefixes("J B");
        MentionFilter.Matches(MentionFilter.Normalize("John Bolton"), jb).Should().BeTrue();
        MentionFilter.Matches(MentionFilter.Normalize("Bolton John"), jb).Should().BeTrue();
        MentionFilter.Matches(MentionFilter.Normalize("Bob Johnson"), jb).Should().BeTrue();
        MentionFilter.Matches(MentionFilter.Normalize("Alice Bolton"), jb).Should().BeFalse();
    }

    [Fact]
    public void MatchesIsCaseInsensitive()
    {
        var q = MentionFilter.GetPrefixes("J B");
        MentionFilter.Matches(MentionFilter.Normalize("John Bolton"), q).Should().BeTrue();
        MentionFilter.Matches(MentionFilter.Normalize("JOHN BOLTON"), q).Should().BeTrue();
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
        result.Select(c => c.Title).Should().Equal("Alex", "Alexander");
    }

    [Fact]
    public void DoesNotMatchInfix()
    {
        // "ohn" should NOT match "John" — word-prefix only.
        var pool = new[] {
            User("John Bolton"),
        };
        var result = MentionFilter.FilterAndRank(pool, "ohn", MentionKindFilter.User, 10);
        result.Should().BeEmpty();
    }

    [Fact]
    public void PlaceChatIsMatchedByPlaceName()
    {
        // A place chat's SearchText carries the place name first, so typing the place finds it.
        var pool = new[] {
            Chat("Funny Chat", placeName: "Fusion Place"),
        };
        MentionFilter.FilterAndRank(pool, "fusion", MentionKindFilter.Chat, 10).Should().HaveCount(1);
        MentionFilter.FilterAndRank(pool, "fusion fun", MentionKindFilter.Chat, 10).Should().HaveCount(1);
        MentionFilter.FilterAndRank(pool, "funny", MentionKindFilter.Chat, 10).Should().HaveCount(1);
    }

    private static MentionCandidate User(string name)
        => new(
            MentionId.NewUser(AnyUser),
            MentionCandidateKind.User,
            name,
            null,
            MentionFilter.Normalize(name));

    private static MentionCandidate Chat(string name, string? placeName = null)
        => new(
            MentionId.NewChat(GroupChatId.New()),
            MentionCandidateKind.Chat,
            name,
            null,
            MentionFilter.Normalize(placeName, name));

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
            MentionFilter.Normalize(title));
    }
}
