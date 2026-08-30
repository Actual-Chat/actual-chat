namespace ActualChat.UI.Blazor.App.Services;

public static class ClientCommandQueueExt
{
    public static void AddClientCommandQueue(this FusionBuilder fusion)
    {
        // Singletons on purpose: CommandContext resolves handlers from a DI scope it creates per
        // outermost command, and the queue's lane, entries and re-dispatch flag are instance state.
        // A scoped queue would hand every re-dispatch a fresh, empty instance, which doesn't
        // recognize the command as its own and queues it again - forever.
        fusion.AddService<ClientCommandQueueTriggers>(ServiceLifetime.Singleton);
        fusion.Services.AddSingleton<ClientCommandQueue>();
    }
}
