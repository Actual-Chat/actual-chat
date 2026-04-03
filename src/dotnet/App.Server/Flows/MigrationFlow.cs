using ActualChat.Chat.Flows;
using ActualChat.Flows;
using ActualChat.Users.Flows;

namespace ActualChat.App.Server.Flows;

[Flow(DataVersion = 1, DelayQuanta = 1)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class MigrationFlow : Flow<Unit>, IMasterFlow
{
    private TimeSpan DependencyCheckPeriod
        => TimeSpan.FromMinutes(Runtime.Hub.HostInfo.IsDevelopmentInstance ? 1 : 5);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        try {
            await Apply<AccountMigrationFlow>().ConfigureAwait(false);
            // Suppressed: ChatEntryMigrationFixupFlow is a superset of ChatEntryMigrationFlow
            // await Apply<ChatEntryMigrationFlow>().ConfigureAwait(false);
            await Apply<ChatEntryMigrationFixupFlow>().ConfigureAwait(false);
            await Apply<ChatEntryEndsAtFixupFlow>(
                dependsOn: [typeof(ChatEntryMigrationFixupFlow)]).ConfigureAwait(false);
        }
        catch (DependencyNotMetException e) {
            var dependencyCheckPeriod = DependencyCheckPeriod;
            Console.Log($"{e.Message} Will retry in {dependencyCheckPeriod}");
            Runtime.StageResumeIn(dependencyCheckPeriod);
        }
    }

    // Private methods

    private async ValueTask Apply<TFlow>(
        string arguments = "", int minDataVersion = 0, Type[]? dependsOn = null)
        where TFlow : Flow
    {
        var cancellationToken = Runtime.CancellationToken;
        var flowId = Hub.NewId<TFlow>(arguments);
        var name = arguments.IsNullOrEmpty() ? flowId.Value.TrimSuffix(":") : flowId.Value;
        var log = Runtime.Log;

        // Check dependencies
        if (dependsOn is { Length: > 0 }) {
            foreach (var dependencyType in dependsOn) {
                var dependencyId = Hub.NewId(dependencyType, "");
                var dependencyData = await Hub.Backend.TryGetData(dependencyId, cancellationToken).ConfigureAwait(false);
                if (dependencyData is not { IsCompleted: true })
                    throw new DependencyNotMetException(typeof(TFlow), dependencyType);
            }
        }

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

    // Nested types

    private sealed class DependencyNotMetException : Exception
    {
        public Type? FlowType { get; }
        public Type? DependsOnType { get; }

        public DependencyNotMetException(Type flowType, Type dependsOnType)
            : base($"{flowType.GetName()} expects {dependsOnType.GetName()} to be completed first.")
        {
            FlowType = flowType;
            DependsOnType = dependsOnType;
        }

        public DependencyNotMetException() : base()
        { }

        public DependencyNotMetException(string? message) : base(message)
        { }

        public DependencyNotMetException(string? message, Exception? innerException) : base(message, innerException)
        { }
    }
}
