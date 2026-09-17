using ActualChat.AI;
using ActualChat.Module;

namespace ActualChat.Core.Server.IntegrationTests;

// Both tests call the paid Cloudflare API, so both are manual. RejectionRate is the measurement
// docs/plans/ai-image-suggestions.md asks for before the feature is committed to: the platform's
// NSFW filter is undocumented and false-positives on innocuous text, and how often it does that
// on our own prompts decides whether the design survives.

public class ImageGeneratorTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const int MaxAcceptableRejectionPercent = 10;

    private static readonly string[] ChatTitles = [
        "Weekend hiking crew", "Kitchen renovation", "Book club", "Dog walkers of Elm Street",
        "Fantasy football league", "Mom's 70th birthday", "Startup founders", "Chess club",
        "Sourdough experiments", "Camping trip 2026", "Guitar practice log", "Neighbourhood watch",
        "Ski season", "Wedding planning", "Board game night", "Marathon training",
        "Garden allotment", "Photography walks", "Jazz records", "Cold water swimming",
    ];

    [Fact(Skip = "Manual: calls the paid Cloudflare API. Set CoreSettings__CloudflareAccountId and __CloudflareAIToken.")]
    public async Task GeneratesAnImage()
    {
        // arrange
        var generator = CreateGenerator();

        // act
        var image = await generator.Generate(
            new ImageGenerationRequest("a friendly cartoon fox reading a book, flat vector, pastel palette"),
            CancellationToken.None);

        // assert
        image.Should().NotBeNull(because: "this prompt carries nothing the content filter should object to");
        image!.Data.Length.Should().BeGreaterThan(1024);
        image.ContentType.Should().BeOneOf("image/jpeg", "image/png");
        Out.WriteLine($"{image.ContentType}, {image.Data.Length} bytes");
    }

    [Fact(Skip = "Manual: calls the paid Cloudflare API once per title. Costs ~$0.006 per run.")]
    public async Task RejectionRateShouldBeLow()
    {
        // arrange
        var generator = CreateGenerator();
        var rejected = new List<string>();

        // act
        foreach (var title in ChatTitles) {
            var prompt = BuildPrompt(title);
            var image = await generator.Generate(new ImageGenerationRequest(prompt), CancellationToken.None);
            if (image is null)
                rejected.Add(title);
        }

        // assert
        var rejectionPercent = rejected.Count * 100 / ChatTitles.Length;
        Out.WriteLine($"Rejected {rejected.Count}/{ChatTitles.Length} ({rejectionPercent}%)");
        foreach (var title in rejected)
            Out.WriteLine($"  rejected: {title}");

        rejectionPercent.Should().BeLessThanOrEqualTo(
            MaxAcceptableRejectionPercent,
            because: "a filter that refuses ordinary chat titles makes the suggestion feature unusable");
    }

    // Private methods

    // Approximates what the real pipeline will send: our style, the chat's subject.
    private static string BuildPrompt(string chatTitle)
        => $"A simple app icon illustrating \"{chatTitle}\". Flat vector art, soft pastel palette, "
            + "centered composition, plain background, no text, no letters.";

    private static IImageGenerator CreateGenerator()
    {
        var settings = new CoreServerSettings {
            CloudflareAccountId = Environment.GetEnvironmentVariable("CoreSettings__CloudflareAccountId") ?? "",
            CloudflareAIToken = Environment.GetEnvironmentVariable("CoreSettings__CloudflareAIToken") ?? "",
        };
        var services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton(settings)
            .AddSingleton<IImageGenerator, CloudflareImageGenerator>()
            .BuildServiceProvider();
        var generator = services.GetRequiredService<IImageGenerator>();
        generator.IsAvailable.Should().BeTrue(
            because: "CoreSettings__CloudflareAccountId and __CloudflareAIToken must be set to run this");

        return generator;
    }
}
