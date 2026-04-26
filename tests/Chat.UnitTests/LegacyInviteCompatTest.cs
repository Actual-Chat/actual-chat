using ActualChat.Invite;
using InviteRecord = ActualChat.Invite.Invite;

namespace ActualChat.Chat.UnitTests;

/// <summary>
/// Pins down the v2.7 wire-frozen <see cref="LegacyInvite"/> /
/// <see cref="LegacyInviteDetails"/> shapes and the modern -> legacy projection used by
/// <see cref="LegacyInvites"/>, plus the legacy -> modern upgrade used in
/// <c>OnLegacyGenerate</c>.
/// </summary>
public class LegacyInviteCompatTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment TestMoment = new(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void LegacyInvite_ChatInvite_RoundTripsViaMemoryPack()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        var legacy = new LegacyInvite("invite-1", 1) {
            CreatedBy = "admin",
            CreatedAt = TestMoment,
            ExpiresOn = TestMoment + TimeSpan.FromDays(7),
            Remaining = 10,
            Details = new LegacyInviteDetails { Option = new LegacyChatInviteOption(chatId) },
        };

        var s = legacy.PassThroughMemoryPackByteSerializer(Out);
        s.Id.Should().Be(legacy.Id);
        s.CreatedBy.Should().Be(legacy.CreatedBy);
        s.Remaining.Should().Be(legacy.Remaining);
        s.Details.Chat.Should().NotBeNull();
        s.Details.Chat!.ChatId.Should().Be(chatId);
    }

    [Fact]
    public void LegacyInvite_PlaceInvite_RoundTripsViaMemoryPack()
    {
        var placeId = PlaceId.New();
        var legacy = new LegacyInvite("invite-2", 1) {
            Remaining = 5,
            Details = new LegacyInviteDetails { Option = new LegacyPlaceInviteOption(placeId) },
        };

        var s = legacy.PassThroughMemoryPackByteSerializer(Out);
        s.Details.Place.Should().NotBeNull();
        s.Details.Place!.PlaceId.Should().Be(placeId);
    }

    [Fact]
    public void LegacyInvite_UserInvite_RoundTripsViaMemoryPack()
    {
        var legacy = new LegacyInvite("invite-3", 1) {
            Remaining = 1,
            Details = new LegacyInviteDetails { Option = new LegacyUserInviteOption() },
        };

        var s = legacy.PassThroughMemoryPackByteSerializer(Out);
        s.Details.User.Should().NotBeNull();
    }

    [Fact]
    public void ChatInvite_From_PreservesAllFields()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        InviteRecord modern = new ChatInvite("invite-x", 5) {
            ChatId = chatId,
            CreatedBy = "admin",
            CreatedAt = TestMoment,
            ExpiresOn = TestMoment + TimeSpan.FromDays(7),
            Remaining = 3,
        };

        var legacy = LegacyInvite.From(modern);
        legacy.Id.Should().Be((Symbol)"invite-x");
        legacy.Version.Should().Be(5);
        legacy.CreatedBy.Should().Be("admin");
        legacy.Remaining.Should().Be(3);
        legacy.Details.Chat.Should().NotBeNull();
        legacy.Details.Chat!.ChatId.Should().Be(chatId);
    }

    [Fact]
    public void PlaceInvite_From_PreservesAllFields()
    {
        var placeId = PlaceId.New();
        InviteRecord modern = new PlaceInvite("invite-y", 2) {
            PlaceId = placeId,
            Remaining = 1,
        };

        var legacy = LegacyInvite.From(modern);
        legacy.Details.Place.Should().NotBeNull();
        legacy.Details.Place!.PlaceId.Should().Be(placeId);
    }

    [Fact]
    public void UserInvite_From_PreservesAllFields()
    {
        InviteRecord modern = new UserInvite("invite-z", 1) { Remaining = 7 };
        var legacy = LegacyInvite.From(modern);
        legacy.Details.User.Should().NotBeNull();
        legacy.Remaining.Should().Be(7);
    }

    [Fact]
    public void LegacyInvite_ToModern_ChatInviteRoundTrip()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        var legacy = new LegacyInvite("invite-x", 5) {
            CreatedBy = "admin",
            CreatedAt = TestMoment,
            ExpiresOn = TestMoment + TimeSpan.FromDays(7),
            Remaining = 3,
            Details = new LegacyInviteDetails { Option = new LegacyChatInviteOption(chatId) },
        };

        var modern = legacy.ToModern();
        modern.Should().BeOfType<ChatInvite>();
        var chatInvite = (ChatInvite)modern;
        chatInvite.Id.Should().Be((Symbol)"invite-x");
        chatInvite.Version.Should().Be(5);
        chatInvite.CreatedBy.Should().Be("admin");
        chatInvite.Remaining.Should().Be(3);
        chatInvite.ChatId.Should().Be(chatId);
    }

    [Fact]
    public void LegacyInvite_ModernRoundTripPreservesAllFields()
    {
        var placeId = PlaceId.New();
        InviteRecord modern1 = new PlaceInvite("invite-q", 9) {
            PlaceId = placeId,
            CreatedBy = "admin",
            CreatedAt = TestMoment,
            ExpiresOn = TestMoment + TimeSpan.FromDays(1),
            Remaining = 4,
        };

        var modern2 = LegacyInvite.From(modern1).ToModern();
        modern2.Should().BeOfType<PlaceInvite>();
        ((PlaceInvite)modern2).PlaceId.Should().Be(placeId);
        modern2.Id.Should().Be(modern1.Id);
        modern2.Version.Should().Be(modern1.Version);
        modern2.CreatedBy.Should().Be(modern1.CreatedBy);
        modern2.Remaining.Should().Be(modern1.Remaining);
    }

    [Fact]
    public void GetSearchKey_LegacyAndModernAgree()
    {
        var chatId = ChatId.Parse("r5IbjdG7Cq");
        InviteRecord modern = new ChatInvite("invite-x") { ChatId = chatId };
        var modernKey = modern.GetSearchKey();
        var staticKey = ChatInvite.GetSearchKey(chatId);
        modernKey.Should().Be(staticKey);
        // Search key must remain stable across the renames so existing DB rows
        // continue to be reachable through DbInvite.SearchKey.
        modernKey.Should().Be("ChatInviteOption:" + chatId);
    }
}
