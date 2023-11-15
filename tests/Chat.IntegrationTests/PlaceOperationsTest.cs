using ActualChat.Contacts;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

public class PlaceOperationsTest : AppHostTestBase
{
    private const string PlaceTitle = "AC Place";
    private const string ChatTitle = "General";

    public PlaceOperationsTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task TryGetNonExistingPlace()
    {
        using var appHost = await NewAppHost();
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var services = tester.AppServices;
        var places = services.GetRequiredService<IPlaces>();
        var place = await places.Get(session, new PlaceId("UnknownPlaceId"), default);
        place.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateNewPlace(bool isPublicPlace)
    {
        using var appHost = await NewAppHost();
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var services = tester.AppServices;
        var places = services.GetRequiredService<IPlaces>();
        var commander = tester.Commander;

        var place = await commander.Call(new Places_Change(session, default, null, new() {
            Create = new PlaceDiff {
                Title = PlaceTitle,
                IsPublic = isPublicPlace,
            },
        }));
        place.Should().NotBeNull();

        await TestExt.WhenMetAsync(
            async () => {
                place = await places.Get(session, place.Id, default);
                place.Should().NotBeNull();
            },
            TimeSpan.FromSeconds(1));

        place.Title.Should().Be(PlaceTitle);
        place.IsPublic.Should().Be(isPublicPlace);

        var contacts = services.GetRequiredService<IContacts>();
        await TestExt.WhenMetAsync(
            async () => {
                var placeIds = await contacts.ListPlaceIds(session, default);
                placeIds.Count.Should().Be(1);
                placeIds.Should().Contain(place.Id);
            },
            TimeSpan.FromSeconds(3));

        await using var tester2 = appHost.NewBlazorTester();
        var anotherSession = tester2.Session;
        await tester2.SignInAsAlice();

        await TestExt.WhenMetAsync(
            async () => {
                var place2 = await places.Get(anotherSession, place.Id, default);
                if (isPublicPlace)
                    place2.Should().NotBeNull();
                else
                    place2.Should().BeNull();
            },
            TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CreatePlaceChat(bool isPublicPlace, bool isPublicChat)
    {
        using var appHost = await NewAppHost();
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var services = tester.AppServices;
        var chats = services.GetRequiredService<IChats>();
        var commander = tester.Commander;

        var place = await commander.Call(new Places_Change(session, default, null, new() {
            Create = new PlaceDiff {
                Title = PlaceTitle,
                IsPublic = isPublicPlace,
            },
        }));

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = ChatTitle,
                IsPublic = isPublicChat,
                PlaceId = place.Id,
            },
        }));
        chat.Should().NotBeNull();

        await TestExt.WhenMetAsync(
            async () => {
                chat = await chats.Get(session, chat.Id, default);
                chat.Should().NotBeNull();
            },
            TimeSpan.FromSeconds(1));

        chat.Title.Should().Be(ChatTitle);
        chat.IsPublic.Should().Be(isPublicChat);
        chat.Kind.Should().Be(ChatKind.Place);
        chat.Id.IsPlaceChat(out var chatPlaceId);
        chatPlaceId.PlaceId.Should().Be(place.Id);

        var contacts = services.GetRequiredService<IContacts>();
        await Task.Delay(100); // Let's wait events are processed
        await TestExt.WhenMetAsync(
            async () => {
                var contactIds = await contacts.ListIds(session, place.Id, default);
                var chatIds = (await contactIds.Select(id => contacts.Get(session, id, default))
                        .Collect())
                    .SkipNullItems()
                    .Select(c => c.ChatId)
                    .ToArray();
                chatIds.Length.Should().Be(1);
                chatIds.Should().Contain(chat.Id);
            },
            TimeSpan.FromSeconds(3));
    }

    [Theory]
    //[InlineData(false)]
    [InlineData(true)]
    public async Task JoinPlace(bool isPublicPlace)
    {
        using var appHost = await NewAppHost();
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var services = tester.AppServices;
        var places = services.GetRequiredService<IPlaces>();
        var commander = tester.Commander;

        var place = await commander.Call(new Places_Change(session, default, null, new() {
            Create = new PlaceDiff {
                Title = PlaceTitle,
                IsPublic = isPublicPlace,
            },
        }));

        await using var tester2 = appHost.NewBlazorTester();
        var anotherSession = tester2.Session;
        await tester2.SignInAsAlice();
        var contacts = services.GetRequiredService<IContacts>();

        {
            var placeIds = await contacts.ListPlaceIds(anotherSession, default);
            placeIds.Should().BeEmpty();
        }

        if (!isPublicPlace) {
            // TODO(DF): Somehow active possibility to join. Invite code?
            throw new NotImplementedException();
        }

        await commander.Call(new Places_Join(anotherSession, place.Id));

        await TestExt.WhenMetAsync(
            async () => {
                var placeIds = await contacts.ListPlaceIds(anotherSession, default);
                placeIds.Count.Should().Be(1);
                placeIds.Should().Contain(place.Id);
            },
            TimeSpan.FromSeconds(3));
    }

    [Theory]
    //[InlineData(false, false)]
    //[InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task JoinPlaceChat(bool isPublicPlace, bool isPublicChat)
    {
        using var appHost = await NewAppHost();
        await using var tester = appHost.NewBlazorTester();
        var session = tester.Session;
        await tester.SignInAsBob();

        var services = tester.AppServices;
        var places = services.GetRequiredService<IPlaces>();
        var commander = tester.Commander;

        var place = await commander.Call(new Places_Change(session, default, null, new() {
            Create = new PlaceDiff {
                Title = PlaceTitle,
                IsPublic = isPublicPlace,
            },
        }));

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = ChatTitle,
                IsPublic = isPublicChat,
                PlaceId = place.Id,
            },
        }));

        await using var tester2 = appHost.NewBlazorTester();
        var anotherSession = tester2.Session;
        await tester2.SignInAsAlice();
        var contacts = services.GetRequiredService<IContacts>();
        {
            var placeIds = await contacts.ListPlaceIds(anotherSession, default);
            placeIds.Should().BeEmpty();
        }

        if (!isPublicPlace) {
            // TODO(DF): Somehow active possibility to join. Invite code?
            throw new NotImplementedException();
        }

        await commander.Call(new Places_Join(anotherSession, place.Id));

        // Assert user can see the Place.
        await TestExt.WhenMetAsync(
            async () => {
                var placeIds = await contacts.ListPlaceIds(anotherSession, default);
                placeIds.Count.Should().Be(1);
                placeIds.Should().Contain(place.Id);
            },
            TimeSpan.FromSeconds(3));

        if (!isPublicChat) {
            // TODO(DF): Somehow active possibility to join chat. Invite code?
            return;
        }

        await commander.Call(new Authors_Join(anotherSession, chat.Id));

        // Asset user can see the Chat.
        await TestExt.WhenMetAsync(
            async () => {
                var contactIds = await contacts.ListIds(anotherSession, place.Id, default);
                var chatIds = (await contactIds.Select(id => contacts.Get(anotherSession, id, default))
                        .Collect())
                    .SkipNullItems()
                    .Select(c => c.ChatId)
                    .ToArray();
                chatIds.Length.Should().Be(1);
                chatIds.Should().Contain(chat.Id);
            },
            TimeSpan.FromSeconds(3));
    }
}
