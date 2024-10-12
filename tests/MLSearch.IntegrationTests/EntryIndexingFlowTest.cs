using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.MLSearch.Flows;
using ActualChat.Testing.Host;

namespace ActualChat.MLSearch.IntegrationTests;

[Collection(nameof(MLSearchCollection))]
public class EntryIndexingFlowTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IFlows Flows { get; } = fixture.AppHost.Services.GetRequiredService<IFlows>();
    private BlazorTester Tester { get; } = fixture.AppHost.NewBlazorTester(@out);
    private string UniquePart { get; } = UniqueNames.Prefix();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldStartIndexing()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cts.Token;
        var (chatId, _) = await Tester.CreateChat(false);

        var entry1 = await CreateEntry(chatId, "Let's go outside");

        // act
        await Flows.GetOrStart<EntryIndexingFlow>(chatId, cancellationToken);

        // assert
    }

    private async Task<ChatEntry> CreateEntry(ChatId chatId, string text)
        => await Tester.CreateTextEntry(chatId, $"{text} {UniquePart}");
}
