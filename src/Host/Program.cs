using ActualChat.Host;

using var appHost = new AppHost();
await appHost.Build();
await appHost.Initialize(true);
await appHost.Run();
