using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Media.IntegrationTests;

[Collection(nameof(MediaCollection))]
public class LinkPreviewsBackendTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly RandomStringGenerator RandomStringGenerator = new (5, Alphabet.AlphaNumericLower);
    private static readonly RandomSymbolGenerator IdGenerator = new (length: 5);
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private ILinkPreviewsBackend Backend { get; } = fixture.AppHost.Services.GetRequiredService<ILinkPreviewsBackend>();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task MustUpdateExistingLinkPreview()
    {
        // arrange
        var id = IdGenerator.Next();
        var linkPreview = await Save(new LinkPreview {
            Id = id,
            Title = "Some link title",
        });

        // act
        await Save(linkPreview with { Description = "Some link description" });

        // act
        linkPreview = await Backend.Get(id, false, CancellationToken.None).Require();

        // assert
        linkPreview.Description.Should().Be("Some link description");
    }

    private Task<LinkPreview> Save(LinkPreview linkPreview)
        => Tester.Commander.Call(new LinkPreviewsBackend_Change(linkPreview.Id, null, Change.Upsert(linkPreview))).Require();
}
