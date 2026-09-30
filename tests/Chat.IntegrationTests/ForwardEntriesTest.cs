using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ForwardEntriesTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ForwardWithUploadingFilesShouldFailWithoutPosting()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var session = tester.Session;
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var (sourceChatId, _) = await tester.CreateChat(true);
        var (destinationChatId, _) = await tester.CreateChat(true);
        var readyEntry = await tester.CreateTextEntry(sourceChatId, "Ready");
        var reservedMediaId = await tester.Commander.Call(new Media_ReserveMedia {
            Session = session,
            Scope = sourceChatId.Value,
        });
        var uploadingEntry = await tester.Commander.Call(new Chats_UpsertEntry {
            Session = session,
            ChatId = sourceChatId,
            LocalId = null,
            Text = "",
            Attachments = [new ChatEntryAttachment { MediaId = reservedMediaId }],
            HasUploadingAttachments = true,
        });
        var rangeBefore = await chats.GetIdRange(session, destinationChatId, CancellationToken.None);

        // act
        var forward = () => tester.Commander.Call(new Chats_ForwardEntries {
            Session = session,
            ChatId = sourceChatId,
            ChatEntries = [readyEntry.Id, uploadingEntry.Id],
            DestinationChatIds = [destinationChatId],
        });

        // assert
        await forward.Should().ThrowAsync<Exception>().WithMessage("*can't be forwarded.");
        var rangeAfter = await chats.GetIdRange(session, destinationChatId, CancellationToken.None);
        rangeAfter.Should().Be(rangeBefore);
    }
}
