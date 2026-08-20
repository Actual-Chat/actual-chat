using ActualChat.Invite;

namespace ActualChat.Chat.UnitTests;

public sealed class InviteSelectionTest
{
    private static readonly Moment Now = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    private static readonly TimeSpan MinLifespan = TimeSpan.FromHours(1);
    private static readonly ChatId TestChatId = ChatId.Parse("r5IbjdG7Cq");

    [Fact]
    public void SkipsExpiredInvite()
    {
        // arrange
        var invites = new[] { NewInvite("expired", -TimeSpan.FromMinutes(1)) };

        // act
        var invite = Invites.ChooseInvite(invites, Now, MinLifespan);

        // assert
        invite.Should().BeNull();
    }

    [Fact]
    public void SkipsInviteExpiringSoonerThanMinLifespan()
    {
        // arrange
        var invites = new[] { NewInvite("expiring-soon", TimeSpan.FromMinutes(30)) };

        // act
        var invite = Invites.ChooseInvite(invites, Now, MinLifespan);

        // assert
        invite.Should().BeNull();
    }

    [Fact]
    public void RequiresLifespanGreaterThanMinLifespan()
    {
        // arrange
        var atLimit = new[] { NewInvite("at-limit", MinLifespan) };
        var aboveLimit = new[] { NewInvite("above-limit", MinLifespan + TimeSpan.FromSeconds(1)) };

        // act
        var atLimitInvite = Invites.ChooseInvite(atLimit, Now, MinLifespan);
        var aboveLimitInvite = Invites.ChooseInvite(aboveLimit, Now, MinLifespan);

        // assert
        atLimitInvite.Should().BeNull();
        aboveLimitInvite!.Id.Should().Be(new Symbol("above-limit"));
    }

    [Fact]
    public void PicksLatestExpiringEligibleInvite()
    {
        // arrange
        var invites = new[] {
            NewInvite("expired", -TimeSpan.FromMinutes(1)),
            NewInvite("expiring-soon", TimeSpan.FromMinutes(30)),
            NewInvite("expiring-in-2h", TimeSpan.FromHours(2)),
            NewInvite("expiring-in-5h", TimeSpan.FromHours(5)),
        };

        // act
        var invite = Invites.ChooseInvite(invites, Now, MinLifespan);

        // assert
        invite!.Id.Should().Be(new Symbol("expiring-in-5h"));
    }

    [Fact]
    public void SkipsUsedUpInvite()
    {
        // arrange
        var invites = new[] {
            NewInvite("used-up", TimeSpan.FromHours(5), 0),
            NewInvite("usable", TimeSpan.FromHours(2)),
        };

        // act
        var invite = Invites.ChooseInvite(invites, Now, MinLifespan);

        // assert
        invite!.Id.Should().Be(new Symbol("usable"));
    }

    [Fact]
    public void CanUseOnlyBeforeExpiry()
    {
        // arrange
        var expiredOneSecondAgo = NewInvite("expired-one-second-ago", -TimeSpan.FromSeconds(1));
        var expiringNow = NewInvite("expiring-now", TimeSpan.Zero);
        var expiringInOneSecond = NewInvite("expiring-in-one-second", TimeSpan.FromSeconds(1));

        // act
        var canUseExpiredOneSecondAgo = expiredOneSecondAgo.CanUse(Now);
        var canUseExpiringNow = expiringNow.CanUse(Now);
        var canUseExpiringInOneSecond = expiringInOneSecond.CanUse(Now);

        // assert
        canUseExpiredOneSecondAgo.Should().BeFalse();
        canUseExpiringNow.Should().BeFalse();
        canUseExpiringInOneSecond.Should().BeTrue();
    }

    [Fact]
    public void CanUseInviteWithoutExpiry()
    {
        // arrange
        var invite = NewInvite("without-expiry", TimeSpan.Zero) with { ExpiresOn = default };

        // act
        var canUse = invite.CanUse(Now);

        // assert
        canUse.Should().BeTrue();
    }

    [Fact]
    public void ChoosesInviteWithoutExpiry()
    {
        // arrange
        var invites = new[] { NewInvite("without-expiry", TimeSpan.Zero) with { ExpiresOn = default } };

        // act
        var invite = Invites.ChooseInvite(invites, Now, MinLifespan);

        // assert
        invite!.Id.Should().Be(new Symbol("without-expiry"));
    }

    [Fact]
    public void ChatInviteGrantsItsOwnChat()
    {
        // arrange
        var invite = NewInvite("chat", TimeSpan.FromHours(2));
        var otherChatId = ChatId.Parse("mYZuMtCUnJ");

        // act
        var grantsOwnChat = invite.Grants(TestChatId);
        var grantsOtherChat = invite.Grants(otherChatId);

        // assert
        grantsOwnChat.Should().BeTrue();
        grantsOtherChat.Should().BeFalse();
    }

    [Fact]
    public void PlaceChatInviteGrantsItsChatAndPlaceRootChat()
    {
        // arrange
        var placeId = PlaceId.New();
        var placeChatId = PlaceChatId.New(placeId);
        var invite = NewInvite("place-chat", TimeSpan.FromHours(2)) with { ChatId = placeChatId };

        // act
        var grantsOwnChat = invite.Grants(placeChatId);
        var grantsRootChat = invite.Grants(placeId.RootChatId);
        var grantsOtherPlaceChat = invite.Grants(PlaceChatId.New(PlaceId.New()));

        // assert
        grantsOwnChat.Should().BeTrue();
        grantsRootChat.Should().BeTrue();
        grantsOtherPlaceChat.Should().BeFalse();
    }

    [Fact]
    public void PlaceInviteGrantsOnlyPlaceRootChat()
    {
        // arrange
        var placeId = PlaceId.New();
        var invite = new PlaceInvite("place") { PlaceId = placeId, Remaining = 10 };

        // act
        var grantsRootChat = invite.Grants(placeId.RootChatId);
        var grantsPlaceChat = invite.Grants(PlaceChatId.New(placeId));
        var grantsOtherPlaceRootChat = invite.Grants(PlaceId.New().RootChatId);

        // assert
        grantsRootChat.Should().BeTrue();
        grantsPlaceChat.Should().BeFalse();
        grantsOtherPlaceRootChat.Should().BeFalse();
    }

    // Private methods

    private static ChatInvite NewInvite(string id, TimeSpan lifespan, int remaining = 10)
        => new(id) {
            CreatedAt = Now,
            ExpiresOn = Now + lifespan,
            Remaining = remaining,
            ChatId = TestChatId,
        };
}
