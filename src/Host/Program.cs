using ActualChat.Audio;
using ActualChat.Host;

AudioOrchestrator.SkipAutoStart = false;
using var appHost = new AppHost();
await appHost.Build();
await appHost.Initialize(true);
await appHost.Run();
