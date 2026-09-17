using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Media.IntegrationTests;

[Collection(nameof(ImageSuggestionsCollection))]
public class ImageSuggestionsBackendTest(ImageSuggestionsAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ImageSuggestionsAppHostFixture>(fixture, @out)
{
    private static readonly RandomStringGenerator KeyGenerator = new (8, Alphabet.AlphaNumericLower);

    private ImageGeneratorMock Generator => field ??= AppHost.Services.GetRequiredService<ImageGeneratorMock>();
    private IImageSuggestionsBackend Sut => field ??= AppHost.Services.GetRequiredService<IImageSuggestionsBackend>();
    private ICommander Commander => field ??= AppHost.Services.Commander();

    [Fact]
    public async Task GenerateShouldStoreASuggestion()
    {
        // arrange
        Generator.Reset();
        var key = NewKey();

        // act
        var generated = await Generate(key, "a stack of books");

        // assert
        generated.Should().NotBeNull();
        generated!.ImageDescription.Should().Be("a stack of books");
        generated.MediaId.Value.Should().NotBeNullOrEmpty();

        var read = await Sut.Get(key, CancellationToken.None);
        read.Should().NotBeNull();
        read!.MediaId.Should().Be(generated.MediaId);
        read.Media.Should().NotBeNull(because: "Get populates Media the way Chat.Picture is populated");
    }

    [Fact]
    public async Task StyleShouldWrapTheDescription()
    {
        // arrange
        Generator.Reset();
        var plainKey = NewKey();
        var styledKey = NewKey();

        // act
        await Generate(plainKey, "a stack of books");
        var plainPrompt = Generator.LastPrompt;
        await Generate(styledKey, "a stack of books", style: ImageStyle.Anime);

        // assert
        plainPrompt.Should().StartWith("a stack of books.");
        plainPrompt.Should().Contain("fills the frame", because: "framing applies even to ImageStyle.None");
        plainPrompt.Should().NotContain("anime");
        Generator.LastPrompt.Should().Contain("anime key visual");

        // The stored description stays a clean subject, which is what the generation modal shows
        var stored = await Sut.Get(styledKey, CancellationToken.None);
        stored!.ImageDescription.Should().Be("a stack of books");
    }

    [Fact]
    public async Task ImplicitGenerateShouldNotReplaceAnExistingSuggestion()
    {
        // arrange
        Generator.Reset();
        var key = NewKey();
        var first = await Generate(key, "a stack of books");

        // act
        var second = await Generate(key, "something else entirely");

        // assert
        Generator.CallCount.Should().Be(1, because: "an implicit trigger is satisfied by any existing suggestion");
        second.Should().NotBeNull();
        second!.MediaId.Should().Be(first!.MediaId);
    }

    [Fact]
    public async Task ExplicitGenerateShouldReplaceAnExistingSuggestion()
    {
        // arrange
        Generator.Reset();
        var key = NewKey();
        var first = await Generate(key, "a stack of books");

        // act
        var second = await Generate(key, "a pair of hiking boots", isExplicit: true);

        // assert
        Generator.CallCount.Should().Be(2);
        second.Should().NotBeNull();
        second!.ImageDescription.Should().Be("a pair of hiking boots");
        second.MediaId.Should().NotBe(first!.MediaId);
    }

    [Fact]
    public async Task ConcurrentGeneratesShouldProduceOneImage()
    {
        // arrange
        Generator.Reset();
        Generator.Delay = TimeSpan.FromSeconds(1);
        var key = NewKey();

        // act
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => Generate(key, "a stack of books"))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        // assert
        Generator.CallCount.Should().Be(
            1,
            because: "a request arriving while a generation is in flight is rejected, not queued");
        results.Count(x => x is not null).Should().BeGreaterThan(0);
        var stored = await Sut.Get(key, CancellationToken.None);
        stored.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerationStartedAtShouldBeSetOnlyWhileRunning()
    {
        // arrange
        Generator.Reset();
        Generator.Delay = TimeSpan.FromSeconds(2);
        var key = NewKey();

        // act
        var generateTask = Generate(key, "a stack of books");
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        var whileRunning = await Sut.GetGenerationStartedAt(key, CancellationToken.None);
        await generateTask;
        var afterwards = await Sut.GetGenerationStartedAt(key, CancellationToken.None);

        // assert
        whileRunning.Should().NotBeNull();
        afterwards.Should().BeNull(because: "the slot is released in a finally block");
    }

    [Fact]
    public async Task RejectedPromptShouldStoreNothing()
    {
        // arrange
        Generator.Reset();
        Generator.MustRejectPrompt = true;
        var key = NewKey();

        // act
        var generated = await Generate(key, "a stack of books");

        // assert
        generated.Should().BeNull(because: "a content-filter rejection is an ordinary null, not an exception");
        var read = await Sut.Get(key, CancellationToken.None);
        read.Should().BeNull();
    }

    [Fact]
    public async Task UnavailableGeneratorShouldStoreNothing()
    {
        // arrange
        Generator.Reset();
        Generator.IsAvailable = false;
        var key = NewKey();

        // act
        var generated = await Generate(key, "a stack of books");

        // assert
        generated.Should().BeNull();
        Generator.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DismissShouldRecordTheMoment()
    {
        // arrange
        Generator.Reset();
        var key = NewKey();
        await Generate(key, "a stack of books");
        var dismissedUntil = AppHost.Services.Clocks().SystemClock.Now + TimeSpan.FromDays(30);

        // act
        await Commander.Call(new ImageSuggestionsBackend_Dismiss(key, dismissedUntil), CancellationToken.None);

        // assert
        var read = await Sut.GetDismissedUntil(key, CancellationToken.None);
        read.Should().NotBeNull();
        read!.Value.EpochOffset.Should().BeCloseTo(dismissedUntil.EpochOffset, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DismissShouldWorkWithNothingGenerated()
    {
        // arrange
        Generator.Reset();
        var key = NewKey();
        var dismissedUntil = AppHost.Services.Clocks().SystemClock.Now + TimeSpan.FromDays(30);

        // act
        await Commander.Call(new ImageSuggestionsBackend_Dismiss(key, dismissedUntil), CancellationToken.None);

        // assert
        var read = await Sut.GetDismissedUntil(key, CancellationToken.None);
        read.Should().NotBeNull(because: "a dismissal with no suggestion is a valid row");
        var suggestion = await Sut.Get(key, CancellationToken.None);
        suggestion.Should().BeNull();
    }

    [Fact]
    public async Task GenerateShouldClearAnEarlierDismissal()
    {
        // arrange
        Generator.Reset();
        var key = NewKey();
        var dismissedUntil = AppHost.Services.Clocks().SystemClock.Now + TimeSpan.FromDays(30);
        await Commander.Call(new ImageSuggestionsBackend_Dismiss(key, dismissedUntil), CancellationToken.None);

        // act
        await Generate(key, "a stack of books", isExplicit: true);

        // assert
        var read = await Sut.GetDismissedUntil(key, CancellationToken.None);
        read.Should().BeNull(because: "a freshly generated suggestion is a new offer");
    }

    [Fact]
    public async Task ExplicitGenerateShouldDeleteTheReplacedMedia()
    {
        // arrange
        Generator.Reset();
        var key = NewKey();
        var first = await Generate(key, "a stack of books");
        var mediaBackend = AppHost.Services.GetRequiredService<IMediaBackend>();
        (await mediaBackend.Get(first!.MediaId, CancellationToken.None)).Should().NotBeNull();

        // act
        var second = await Generate(key, "a pair of hiking boots", isExplicit: true);

        // assert
        second!.MediaId.Should().NotBe(first.MediaId);
        var replaced = await mediaBackend.Get(first.MediaId, CancellationToken.None);
        replaced.Should().BeNull(because: "a regenerate must not leak the image it replaced");
        (await mediaBackend.Get(second.MediaId, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveShouldDeleteMediaOnlyWhenAsked()
    {
        // arrange
        Generator.Reset();
        var keptKey = NewKey();
        var droppedKey = NewKey();
        var kept = await Generate(keptKey, "a stack of books");
        var dropped = await Generate(droppedKey, "a stack of books");
        var mediaBackend = AppHost.Services.GetRequiredService<IMediaBackend>();

        // act
        await Commander.Call(new ImageSuggestionsBackend_Remove(keptKey, false), CancellationToken.None);
        await Commander.Call(new ImageSuggestionsBackend_Remove(droppedKey, true), CancellationToken.None);

        // assert
        (await mediaBackend.Get(kept!.MediaId, CancellationToken.None)).Should()
            .NotBeNull(because: "an accepted suggestion's media is the content's own picture now");
        (await mediaBackend.Get(dropped!.MediaId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task RemoveShouldDropTheSuggestion()
    {
        // arrange
        Generator.Reset();
        var key = NewKey();
        await Generate(key, "a stack of books");

        // act
        await Commander.Call(new ImageSuggestionsBackend_Remove(key, false), CancellationToken.None);

        // assert
        var read = await Sut.Get(key, CancellationToken.None);
        read.Should().BeNull();
    }

    [Fact]
    public async Task ListStaleShouldFindASuggestion()
    {
        // arrange
        Generator.Reset();
        var key = NewKey();
        await Generate(key, "a stack of books");
        var future = AppHost.Services.Clocks().SystemClock.Now + TimeSpan.FromHours(1);

        // act
        var stale = await Sut.ListStale(future, 10_000, CancellationToken.None);

        // assert
        stale.Should().Contain(key);
    }

    [Fact]
    public async Task ListStaleShouldSkipAnActiveDismissal()
    {
        // arrange
        Generator.Reset();
        var key = NewKey();
        await Generate(key, "a stack of books");
        var now = AppHost.Services.Clocks().SystemClock.Now;
        await Commander.Call(
            new ImageSuggestionsBackend_Dismiss(key, now + TimeSpan.FromDays(30)),
            CancellationToken.None);

        // act
        var stale = await Sut.ListStale(now + TimeSpan.FromHours(1), 10_000, CancellationToken.None);

        // assert
        stale.Should().NotContain(
            key,
            because: "re-offering the moment a dismissal expires is the point, so its row must survive");
    }

    [Fact]
    public async Task ListStaleShouldFindAnExpiredDismissal()
    {
        // arrange
        Generator.Reset();
        var key = NewKey();
        await Generate(key, "a stack of books");
        var now = AppHost.Services.Clocks().SystemClock.Now;
        await Commander.Call(
            new ImageSuggestionsBackend_Dismiss(key, now - TimeSpan.FromDays(1)),
            CancellationToken.None);

        // act
        var stale = await Sut.ListStale(now + TimeSpan.FromHours(1), 10_000, CancellationToken.None);

        // assert
        stale.Should().Contain(key);
    }

    // Private methods

    private Task<ImageSuggestion?> Generate(
        string key,
        string imageDescription,
        bool isExplicit = false,
        ImageStyle style = ImageStyle.None)
    {
        var command = new ImageSuggestionsBackend_Generate(key, imageDescription) {
            IsExplicit = isExplicit,
            Style = style,
        };
        return Commander.Call(command, CancellationToken.None);
    }

    private static string NewKey()
        => $"picture/test:{KeyGenerator.Next()}";
}
