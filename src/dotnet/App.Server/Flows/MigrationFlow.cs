using ActualChat.Chat.Flows;
using ActualChat.Flows;
using ActualChat.Users.Flows;

namespace ActualChat.App.Server.Flows;

[Flow(DataVersion = 1, DelayQuanta = 1)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class MigrationFlow : Flow<Unit>, IMasterFlow
{
    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        await Apply<AccountMigrationFlow>().ConfigureAwait(false);
        await Apply<ChatEntryMigrationFlow>().ConfigureAwait(false);
        await Apply<ChatEntryMigrationFixupFlow>().ConfigureAwait(false);
    }

    // Private methods

    private async ValueTask Apply<TFlow>(string arguments = "", int minDataVersion = 0)
        where TFlow : Flow
    {
        var cancellationToken = Runtime.CancellationToken;
        var flowId = Hub.NewId<TFlow>(arguments);
        var name = arguments.IsNullOrEmpty() ? flowId.Value.TrimSuffix(":") : flowId.Value;
        var log = Runtime.Log;

        var flow = await Hub.TryGet<TFlow>(flowId, cancellationToken).ConfigureAwait(false);
        if (flow?.UntypedResult != null && flow.DataVersion >= minDataVersion) {
            Console.Log($"{name}: already completed, result: {flow.UntypedResult}");
            log.LogInformation("{Name} console:\n{Console}", name, flow.Console.ToString().Indent("  "));
            return;
        }

        var flowResumeEvent = Hub.NewResumeEvent(flowId);
        var action = flow is null ? "starting" : "resuming";
        if ((flow?.DataVersion ?? int.MaxValue) < minDataVersion) {
            flowResumeEvent.WithReset();
            action += ", must reset";
        }
        Console.Log($"{name}: " + action);
        await flowResumeEvent.Schedule(cancellationToken).ConfigureAwait(false);
    }
}
