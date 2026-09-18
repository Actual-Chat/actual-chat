using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Media;

namespace ActualChat.Chat.UnitTests;

public sealed class PlaceImageSuggestionsTest
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(11, true)]
    public async Task EligibilityShouldRequireTwoReadableChats(int chatCount, bool expected)
    {
        // arrange
        using var scope = new TestScope(chatCount);

        // act
        var result = await scope.Suggestions.CanGenerateForPlace(scope.Session, scope.Place.Id, default);

        // assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task UnreadableChatsShouldNotCountTowardEligibility()
    {
        // arrange
        using var scope = new TestScope(2);
        var unreadable = scope.Chats[0];
        scope.ChatsApi.Setup(x => x.Get(scope.Session, unreadable.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unreadable with { Rules = AuthorRules.None(unreadable.Id) });

        // act
        var result = await scope.Suggestions.CanGenerateForPlace(scope.Session, scope.Place.Id, default);

        // assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(ImageSlot.Picture)]
    [InlineData(ImageSlot.Background)]
    public async Task DescriptionShouldSampleTenOpeningMessagesAcrossTenReadableChats(ImageSlot slot)
    {
        // arrange
        using var scope = new TestScope(12);
        var unreadable = scope.Chats[0];
        scope.ChatsApi.Setup(x => x.Get(scope.Session, unreadable.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Chat?)null);
        IReadOnlyCollection<ChatImageDescriptionSource>? sources = null;
        scope.Describer.Setup(x => x.DescribePlace(scope.Place,
                It.IsAny<IReadOnlyCollection<ChatImageDescriptionSource>>(),
                slot == ImageSlot.Background, It.IsAny<CancellationToken>()))
            .Callback<Place, IReadOnlyCollection<ChatImageDescriptionSource>, bool, CancellationToken>(
                (_, chats, _, _) => sources = chats)
            .ReturnsAsync("");

        // act
        var result = await scope.Suggestions.OnGenerateForPlace(
            new ImageSuggestions_GenerateForPlace(scope.Session, scope.Place.Id, slot, null) { IsExplicit = true },
            default);

        // assert
        result.Should().BeNull();
        sources.Should().NotBeNull().And.HaveCount(10);
        sources!.Select(x => x.Title).Should().Equal(scope.Chats.Skip(1).Take(10).Select(x => x.Title));
        foreach (var source in sources)
            source.Entries.Select(x => x.LocalId).Should().Equal(Enumerable.Range(4, 10).Select(x => (long)x));
    }

    [Fact]
    public async Task NonOwnersShouldNotReadOrModifyPlaceSuggestions()
    {
        // arrange
        using var scope = new TestScope(2);
        scope.Places.Setup(x => x.GetRules(scope.Session, scope.Place.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaceRules(scope.Place.Id, null, null, PlacePermissions.Read));

        // act
        var generate = () => scope.Suggestions.OnGenerateForPlace(
            new ImageSuggestions_GenerateForPlace(scope.Session, scope.Place.Id, ImageSlot.Picture, "a book"),
            default);
        var accept = () => scope.Suggestions.OnAcceptForPlace(
            new ImageSuggestions_AcceptForPlace(scope.Session, scope.Place.Id, ImageSlot.Picture), default);
        var dismiss = () => scope.Suggestions.OnDismissForPlace(
            new ImageSuggestions_DismissForPlace(scope.Session, scope.Place.Id, ImageSlot.Picture), default);

        // assert
        (await scope.Suggestions.GetForPlace(scope.Session, scope.Place.Id, ImageSlot.Picture, default))
            .Should().BeNull();
        (await scope.Suggestions.CanGenerateForPlace(scope.Session, scope.Place.Id, default)).Should().BeFalse();
        await generate.Should().ThrowAsync<Exception>();
        await accept.Should().ThrowAsync<Exception>();
        await dismiss.Should().ThrowAsync<Exception>();
        scope.Describer.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(ImageStyle.Default)]
    [InlineData(ImageStyle.Photo)]
    [InlineData(ImageStyle.FlatVector)]
    public void BackgroundPromptsShouldUseSceneFraming(ImageStyle style)
    {
        // act
        var prompt = string.Format(style.GetPromptTemplate(isBackground: true), "mountain lake");

        // assert
        prompt.Should().StartWith("mountain lake.").And.Contain("camera-style scene")
            .And.NotContain("40px").And.NotContain("app-icon").And.NotContain("plain background");
    }

    [Fact]
    public void PicturePromptsShouldKeepIconFraming()
    {
        // act
        var prompt = ImageStyle.Photo.GetPromptTemplate();

        // assert
        prompt.Should().Contain("40px").And.Contain("plain background");
    }

    // Nested types

    private sealed class TestScope : IDisposable
    {
        private ServiceProvider Services { get; }

        public Session Session { get; } = Session.New();
        public Place Place { get; } = new(PlaceId.New()) { Title = "Community" };
        public Chat[] Chats { get; }
        public Mock<IPlaces> Places { get; } = new(MockBehavior.Strict);
        public Mock<IChats> ChatsApi { get; } = new(MockBehavior.Strict);
        public Mock<IChatImageDescriber> Describer { get; } = new(MockBehavior.Strict);
        public ImageSuggestions Suggestions { get; }

        public TestScope(int chatCount)
        {
            Chats = Enumerable.Range(0, chatCount).Select(i => {
                var id = PlaceChatId.New(Place.Id);
                return new Chat(id) { Title = $"Topic {i}", Rules = new(id, null, null, ChatPermissions.Read) };
            }).ToArray();
            var backend = new Mock<IChatsBackend>(MockBehavior.Strict);
            Places.Setup(x => x.GetRules(Session, Place.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PlaceRules(Place.Id, null, null, PlacePermissions.Owner));
            Places.Setup(x => x.Get(Session, Place.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Place);
            backend.Setup(x => x.ListPlaceChatIds(Place.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Chats.Select(x => (PlaceChatId)x.Id).ToArray());
            foreach (var chat in Chats) {
                ChatsApi.Setup(x => x.Get(Session, chat.Id, It.IsAny<CancellationToken>())).ReturnsAsync(chat);
                backend.Setup(x => x.GetLidRange(chat.Id, false, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new Range<long>(1, 20));
                backend.Setup(x => x.GetTile(chat.Id, It.IsAny<Range<long>>(), false, It.IsAny<CancellationToken>()))
                    .Returns<ChatId, Range<long>, bool, CancellationToken>((_, range, _, _) => {
                        var entries = Enumerable.Range(1, 19).Select(i => new TextEntry(ChatEntryId.New(chat.Id, i)) {
                            Content = i == 1 ? "" : $"message {i}",
                            IsRemoved = i == 2,
                            ContentStreamId = i == 3 ? "stream" : "",
                        }).Where(x => range.Contains(x.LocalId)).Cast<ChatEntry>().ToArray();
                        return Task.FromResult(new ChatTile(range, false, entries));
                    });
            }
            Services = new ServiceCollection()
                .AddSingleton(new ChatSettings())
                .AddSingleton(Places.Object)
                .AddSingleton(ChatsApi.Object)
                .AddSingleton(backend.Object)
                .AddSingleton(Describer.Object)
                .BuildServiceProvider();
            Suggestions = new ImageSuggestions(Services);
        }

        public void Dispose() => Services.Dispose();
    }
}
