using System.Globalization;
using ActualChat.Flows;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record SomeEvent(
    [property: DataMember, MemoryPackOrder(1)] string SomeKey,
    [property: DataMember, MemoryPackOrder(2)] string SomeData
) : EventCommand, IHasShardKey<string>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string ShardKey => SomeKey;
}

public class SomeEventSource : IBackendService
{
    public async Task EnqueueEvent(string key, string data)
    {
        var someEvent = new SomeEvent(key, data);
        var context = CommandContext.GetCurrent();
        context.Operation.AddEvent(someEvent);
    }
    /*
    [EventHandler]
    public Task OnEvent(SomeEvent eventCommand, CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }
    */
}
