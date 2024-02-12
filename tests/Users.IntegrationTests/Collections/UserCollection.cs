namespace ActualChat.Users.IntegrationTests;

[CollectionDefinition(nameof(UserCollection))]
public class UserCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture("users", messageSink);
