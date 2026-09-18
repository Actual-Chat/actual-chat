using ActualChat.Media;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ImageSuggestionsCollection))]
public sealed class ImageSuggestionsTest(ImageSuggestionsCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ImageSuggestionsCollection.AppHostFixture>(fixture, @out)
{
    private ImageGeneratorMock Generator => field ??= AppHost.Services.GetRequiredService<ImageGeneratorMock>();
    private ChatImageDescriberMock Describer
        => field ??= AppHost.Services.GetRequiredService<ChatImageDescriberMock>();

    [Fact]
    public async Task OwnerShouldGenerateAndAccept()
    {
        // arrange
        Reset();
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(true);
        await tester.CreateTextEntries(chatId, "hello", 5);
        var suggestions = tester.AppServices.GetRequiredService<IImageSuggestions>();

        // act
        var generated = await tester.Commander.Call(
            new ImageSuggestions_GenerateForChat(tester.Session, chatId, ImageSlot.Picture, null));

        // assert
        generated.Should().NotBeNull();
        Generator.CallCount.Should().Be(1);
        var read = await suggestions.GetForChat(tester.Session, chatId, ImageSlot.Picture, default);
        read.Should().NotBeNull();

        // act - accept
        await tester.Commander.Call(
            new ImageSuggestions_AcceptForChat(tester.Session, chatId, ImageSlot.Picture));

        // assert
        var chat = await tester.Chats.Get(tester.Session, chatId, default);
        chat!.MediaId.Should().Be(generated!.MediaId);
        var afterAccept = await suggestions.GetForChat(tester.Session, chatId, ImageSlot.Picture, default);
        afterAccept.Should().BeNull(because: "an accepted suggestion is no longer pending");
    }

    [Fact]
    public async Task NonOwnerShouldNotGenerate()
    {
        // arrange
        Reset();
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        await using var bobTester = AppHost.NewBlazorTester(Out);
        await aliceTester.SignInAsUniqueAlice();
        var bob = await bobTester.SignInAsUniqueBob();
        var (chatId, inviteId) = await aliceTester.CreateChat(true);
        await aliceTester.CreateTextEntries(chatId, "hello", 5);
        await bobTester.JoinChat(chatId, inviteId);

        // Bob is a member who can read the chat - so what follows is about ownership, not access
        var bobRules = await bobTester.Chats.GetRules(bobTester.Session, chatId, default);
        bobRules.CanRead().Should().BeTrue();
        bobRules.CanEditProperties().Should().BeFalse();

        // act
        var generate = () => bobTester.Commander.Call(
            new ImageSuggestions_GenerateForChat(bobTester.Session, chatId, ImageSlot.Picture, null));

        // assert
        await generate.Should().ThrowAsync<Exception>(
            because: "only someone who can edit the chat's properties may spend money generating a picture");
        Generator.CallCount.Should().Be(0);
        _ = bob;
    }

    [Fact]
    public async Task NonOwnerShouldNotSeeTheSuggestion()
    {
        // arrange
        Reset();
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        await using var bobTester = AppHost.NewBlazorTester(Out);
        await aliceTester.SignInAsUniqueAlice();
        await bobTester.SignInAsUniqueBob();
        var (chatId, inviteId) = await aliceTester.CreateChat(true);
        await aliceTester.CreateTextEntries(chatId, "hello", 5);
        await bobTester.JoinChat(chatId, inviteId);
        await aliceTester.Commander.Call(
            new ImageSuggestions_GenerateForChat(aliceTester.Session, chatId, ImageSlot.Picture, null));

        // Bob can read the chat itself, so a null suggestion below is about ownership, not access
        var bobChat = await bobTester.Chats.Get(bobTester.Session, chatId, default);
        bobChat.Should().NotBeNull();

        // act
        var bobSuggestions = bobTester.AppServices.GetRequiredService<IImageSuggestions>();
        var read = await bobSuggestions.GetForChat(bobTester.Session, chatId, ImageSlot.Picture, default);

        // assert
        read.Should().BeNull(because: "the suggestion is only offered to whoever can accept it");
    }

    [Fact]
    public async Task NonOwnerShouldNotAcceptOrDismiss()
    {
        // arrange
        Reset();
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        await using var bobTester = AppHost.NewBlazorTester(Out);
        await aliceTester.SignInAsUniqueAlice();
        await bobTester.SignInAsUniqueBob();
        var (chatId, inviteId) = await aliceTester.CreateChat(true);
        await aliceTester.CreateTextEntries(chatId, "hello", 5);
        await bobTester.JoinChat(chatId, inviteId);
        await aliceTester.Commander.Call(
            new ImageSuggestions_GenerateForChat(aliceTester.Session, chatId, ImageSlot.Picture, null));

        // act
        var accept = () => bobTester.Commander.Call(
            new ImageSuggestions_AcceptForChat(bobTester.Session, chatId, ImageSlot.Picture));
        var dismiss = () => bobTester.Commander.Call(
            new ImageSuggestions_DismissForChat(bobTester.Session, chatId, ImageSlot.Picture));

        // assert
        await accept.Should().ThrowAsync<Exception>();
        await dismiss.Should().ThrowAsync<Exception>();
        var chat = await aliceTester.Chats.Get(aliceTester.Session, chatId, default);
        chat!.MediaId.Should().BeNull(because: "a non-owner's accept must not change the picture");
    }

    [Fact]
    public async Task EmptyChatShouldStillGenerateWhenAsked()
    {
        // arrange
        Reset();
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(true);

        // act - a brand new chat: no description, no messages. The client decides when to ask,
        // so the server obliges rather than second-guessing it.
        var generated = await tester.Commander.Call(
            new ImageSuggestions_GenerateForChat(tester.Session, chatId, ImageSlot.Picture, null));

        // assert
        generated.Should().NotBeNull();
        Describer.CallCount.Should().Be(1);
        Generator.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ChatTheDescriberCannotDescribeShouldGenerateNothing()
    {
        // arrange
        Reset();
        Describer.Result = "";
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(true);
        await tester.CreateTextEntries(chatId, "hello", 5);

        // act
        var generated = await tester.Commander.Call(
            new ImageSuggestions_GenerateForChat(tester.Session, chatId, ImageSlot.Picture, null));

        // assert
        generated.Should().BeNull();
        Describer.CallCount.Should().Be(1);
        Generator.CallCount.Should().Be(0, because: "there is nothing to put in the prompt");
    }

    [Fact]
    public async Task SuppliedDescriptionShouldSkipTheDescriber()
    {
        // arrange
        Reset();
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(true);

        // act - no entries at all, but the owner typed a description in the Regenerate modal
        var generated = await tester.Commander.Call(
            new ImageSuggestions_GenerateForChat(tester.Session, chatId, ImageSlot.Picture, "a red bicycle") {
                IsExplicit = true,
            });

        // assert
        generated.Should().NotBeNull();
        generated!.ImageDescription.Should().Be("a red bicycle");
        Describer.CallCount.Should().Be(0);
        Generator.LastPrompt.Should().StartWith("a red bicycle.", because: "the house style is appended to it");
    }

    [Fact]
    public async Task DismissShouldRecordAMoment()
    {
        // arrange
        Reset();
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(true);
        await tester.CreateTextEntries(chatId, "hello", 5);
        var suggestions = tester.AppServices.GetRequiredService<IImageSuggestions>();

        // act
        await tester.Commander.Call(
            new ImageSuggestions_DismissForChat(tester.Session, chatId, ImageSlot.Picture));

        // assert
        var dismissedUntil = await suggestions.GetDismissedUntilForChat(
            tester.Session, chatId, ImageSlot.Picture, default);
        dismissedUntil.Should().NotBeNull();
        dismissedUntil!.Value.Should().BeGreaterThan(AppHost.Services.Clocks().SystemClock.Now);
    }

    [Fact]
    public async Task SlotsShouldNotCollide()
    {
        // arrange
        Reset();
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(true);
        var suggestions = tester.AppServices.GetRequiredService<IImageSuggestions>();

        // act
        await tester.Commander.Call(
            new ImageSuggestions_GenerateForChat(tester.Session, chatId, ImageSlot.Picture, "a red bicycle") {
                IsExplicit = true,
            });

        // assert
        var picture = await suggestions.GetForChat(tester.Session, chatId, ImageSlot.Picture, default);
        var background = await suggestions.GetForChat(tester.Session, chatId, ImageSlot.Background, default);
        picture.Should().NotBeNull();
        background.Should().BeNull(because: "the slot is part of the key");
    }

    [Fact]
    public async Task PlaceShouldWaitForTwoChats()
    {
        // arrange
        Reset();
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueAlice();
        var place = await tester.CreatePlace(true);
        var suggestions = tester.AppServices.GetRequiredService<IImageSuggestions>();
        await tester.CreateChat(true, "First", place.Id);

        // act
        var canGenerate = await suggestions.CanGenerateForPlace(tester.Session, place.Id, default);
        var generated = await tester.Commander.Call(
            new ImageSuggestions_GenerateForPlace(tester.Session, place.Id, ImageSlot.Picture, null));

        // assert
        canGenerate.Should().BeFalse();
        generated.Should().BeNull();
        Describer.CallCount.Should().Be(0);
        Generator.CallCount.Should().Be(0);

        // act
        await tester.CreateChat(true, "Second", place.Id);

        // assert
        (await suggestions.CanGenerateForPlace(tester.Session, place.Id, default)).Should().BeTrue();
    }

    [Fact]
    public async Task PlaceShouldUseTheFirstTenMessagesOfAtMostTenChats()
    {
        // arrange
        Reset();
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueAlice();
        var place = await tester.CreatePlace(true);
        for (var i = 0; i < 11; i++) {
            var (chatId, _) = await tester.CreateChat(true, $"Topic {i}", place.Id);
            await tester.CreateTextEntries(chatId, "opening", 10);
            await tester.CreateTextEntries(chatId, "tail excluded", 2);
        }

        // act
        var generated = await tester.Commander.Call(
            new ImageSuggestions_GenerateForPlace(tester.Session, place.Id, ImageSlot.Picture, null));

        // assert
        generated.Should().NotBeNull();
        Describer.CallCount.Should().Be(1);
        Describer.PlaceChats.Should().HaveCount(10);
        Describer.PlaceChats.Should().OnlyContain(x => x.Entries.Count == 10);
        Describer.PlaceChats.SelectMany(x => x.Entries).Should()
            .OnlyContain(x => x.Content.Contains("opening") && !x.Content.Contains("tail excluded"));
    }

    [Fact]
    public async Task PlaceImagesShouldBeAcceptedIndependently()
    {
        // arrange
        Reset();
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueAlice();
        var place = await tester.CreatePlace(true);
        var (chatId, _) = await tester.CreateChat(true, "Books", place.Id);
        await tester.CreateChat(true, "Travel", place.Id);
        var suggestions = tester.AppServices.GetRequiredService<IImageSuggestions>();
        var picture = await tester.Commander.Call(
            new ImageSuggestions_GenerateForPlace(tester.Session, place.Id, ImageSlot.Picture, null));
        var background = await tester.Commander.Call(
            new ImageSuggestions_GenerateForPlace(tester.Session, place.Id, ImageSlot.Background, null));

        // act
        await tester.Commander.Call(
            new ImageSuggestions_AcceptForPlace(tester.Session, place.Id, ImageSlot.Picture));
        await tester.Commander.Call(
            new ImageSuggestions_AcceptForPlace(tester.Session, place.Id, ImageSlot.Background));

        // assert
        var updated = await tester.Places.Get(tester.Session, place.Id, default);
        updated!.MediaId.Should().Be(picture!.MediaId);
        updated.BackgroundMediaId.Should().Be(background!.MediaId);
        (await tester.Chats.Get(tester.Session, chatId, default))!.MediaId.Should().BeNull();
        (await suggestions.GetForPlace(tester.Session, place.Id, ImageSlot.Picture, default)).Should().BeNull();
        (await suggestions.GetForPlace(tester.Session, place.Id, ImageSlot.Background, default)).Should().BeNull();
        Describer.IsBackground.Should().BeTrue();
        Generator.LastPrompt.Should().Contain("camera-style scene").And.NotContain("40px")
            .And.NotContain("plain background");
    }

    [Fact]
    public async Task PlaceDismissalShouldNotDismissOtherSlotsOrChats()
    {
        // arrange
        Reset();
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueAlice();
        var place = await tester.CreatePlace(true);
        var (chatId, _) = await tester.CreateChat(true, "First", place.Id);
        await tester.CreateChat(true, "Second", place.Id);
        var suggestions = tester.AppServices.GetRequiredService<IImageSuggestions>();

        // act
        await tester.Commander.Call(
            new ImageSuggestions_DismissForPlace(tester.Session, place.Id, ImageSlot.Picture));
        var generated = await tester.Commander.Call(
            new ImageSuggestions_GenerateForPlace(tester.Session, place.Id, ImageSlot.Picture, null));

        // assert
        generated.Should().BeNull();
        Generator.CallCount.Should().Be(0);
        (await suggestions.GetDismissedUntilForPlace(tester.Session, place.Id, ImageSlot.Picture, default))
            .Should().NotBeNull();
        (await suggestions.GetDismissedUntilForPlace(tester.Session, place.Id, ImageSlot.Background, default))
            .Should().BeNull();
        (await suggestions.GetDismissedUntilForChat(tester.Session, chatId, ImageSlot.Picture, default))
            .Should().BeNull();
    }

    [Fact]
    public async Task PlaceMemberShouldNotReadGenerateAcceptOrDismissSuggestions()
    {
        // arrange
        Reset();
        await using var owner = AppHost.NewBlazorTester(Out);
        await using var member = AppHost.NewBlazorTester(Out);
        await owner.SignInAsUniqueAlice();
        var bob = await member.SignInAsUniqueBob();
        var place = await owner.CreatePlace(true, "Community", bob);
        await owner.CreateChat(true, "First", place.Id);
        await owner.CreateChat(true, "Second", place.Id);
        await owner.Commander.Call(
            new ImageSuggestions_GenerateForPlace(owner.Session, place.Id, ImageSlot.Picture, null));
        var suggestions = member.AppServices.GetRequiredService<IImageSuggestions>();

        // act
        var generate = () => member.Commander.Call(
            new ImageSuggestions_GenerateForPlace(member.Session, place.Id, ImageSlot.Background, null));
        var accept = () => member.Commander.Call(
            new ImageSuggestions_AcceptForPlace(member.Session, place.Id, ImageSlot.Picture));
        var dismiss = () => member.Commander.Call(
            new ImageSuggestions_DismissForPlace(member.Session, place.Id, ImageSlot.Picture));

        // assert
        (await suggestions.GetForPlace(member.Session, place.Id, ImageSlot.Picture, default)).Should().BeNull();
        (await suggestions.CanGenerateForPlace(member.Session, place.Id, default)).Should().BeFalse();
        await generate.Should().ThrowAsync<Exception>();
        await accept.Should().ThrowAsync<Exception>();
        await dismiss.Should().ThrowAsync<Exception>();
        Generator.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExistingPlaceSuggestionShouldNotBeDescribedAgain()
    {
        // arrange
        Reset();
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueAlice();
        var place = await tester.CreatePlace(true);
        await tester.CreateChat(true, "First", place.Id);
        await tester.CreateChat(true, "Second", place.Id);
        var command = new ImageSuggestions_GenerateForPlace(tester.Session, place.Id, ImageSlot.Picture, null);

        // act
        var first = await tester.Commander.Call(command);
        var second = await tester.Commander.Call(command);

        // assert
        second!.MediaId.Should().Be(first!.MediaId);
        Generator.CallCount.Should().Be(1);
        Describer.CallCount.Should().Be(1);
    }

    // Private methods

    private void Reset()
    {
        Generator.Reset();
        Describer.Reset();
    }
}
