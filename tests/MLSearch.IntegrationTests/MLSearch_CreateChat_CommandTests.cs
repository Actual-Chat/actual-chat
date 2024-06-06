using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.MLSearch.IntegrationTests.Collections;
using ActualChat.Testing.Host;
using ActualChat.Chat;
using OpenSearch.Client;
using AppHostFixture = ActualChat.MLSearch.IntegrationTests.Collections.AppHostFixture;
using ActualChat.Users;

namespace ActualChat.MLSearch.IntegrationTests.MLSearch;

[Trait("Category", "Slow")]
[Collection(nameof(MLSearchCollection))]
public class MLSearch_CreateChat_CommandTests(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task UserCanCreateMultipleMLSearchChats(){
        // Arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var someUserAccount = await tester.SignInAsBob();
        var session = tester.Session;
        var command1 = new MLSearch_CreateChat(session, "Any-title", default);
        var command2 = new MLSearch_CreateChat(session, "Any-title", default);
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChatsBackend>();
        
        // Act
        var chat1 = await commander.Call(command1, default);
        var chat2 = await commander.Call(command2, default);
        // Assert
        Assert.False(chat1.Id.IsNone);
        Assert.False(chat2.Id.IsNone);
        Assert.NotEqual(chat1.Id, chat2.Id);
        (await chats.Get(chat1.Id, default)).Should().NotBeNull();
        (await chats.Get(chat2.Id, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task ItCreates1To1ChatWithAUserAndABotOnly(){
        // Arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var someUserAccount = await tester.SignInAsBob();
        var session = tester.Session;
        var command = new MLSearch_CreateChat(session, "Any-title", default);
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChatsBackend>();
        var authors = tester.AppServices.GetRequiredService<IAuthorsBackend>();
        
        // Act
        var chat = await commander.Call(command, default);
        
        // Assert
        Assert.False(chat.Id.IsNone);
        var chatUsers = await authors.ListUserIds(chat.Id, default);
        Assert.True(chatUsers.Count == 2);
        Assert.Contains(someUserAccount.Id, chatUsers);
        Assert.Contains(Constants.User.MLSearchBot.UserId, chatUsers);
    }

    [Fact]
    public async Task UserCanReadAndWriteIntoTheSearchChat(){
        // Arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var someUserAccount = await tester.SignInAsBob();
        var session = tester.Session;
        var command = new MLSearch_CreateChat(session, "Any-title", default);
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();
        
        // Act
        var chat = await commander.Call(command, default);
        
        // Assert
        var permissions = await chats.GetRules(session, chat.Id, default);
        permissions.CanRead().Should().BeTrue();
        permissions.CanWrite().Should().BeTrue();
    }

    [Fact]
    public async Task UserCanNotKickOutABotFromTheChat(){
        // Arrange
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var someUserAccount = await tester.SignInAsBob();
        var session = tester.Session;
        var command = new MLSearch_CreateChat(session, "Any-title", default);
        var commander = tester.Commander;
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var authors = tester.AppServices.GetRequiredService<IAuthorsBackend>();
        
        // Act
        var chat = await commander.Call(command, default);
        var botAuthor = await authors.GetByUserId(chat.Id, Constants.User.MLSearchBot.UserId, AuthorsBackend_GetAuthorOption.Full, default);
        botAuthor.Should().NotBeNull();
        var result = await commander.Run(new Authors_Exclude(session, botAuthor.Id));
        // Assert
        // Expect System.InvalidOperationException: You can't remove an owner of this chat from chat members.
        var outerException = result.UntypedResultTask.Exception;
        outerException.Should().BeOfType<AggregateException>();
        outerException.InnerExceptions.Should().ContainItemsAssignableTo<InvalidOperationException>();
    }
}