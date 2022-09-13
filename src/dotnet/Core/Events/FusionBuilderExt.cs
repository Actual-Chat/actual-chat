using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Events;

public static class FusionBuilderExt
{
    public static FusionBuilder AddLocalEventScheduler(this FusionBuilder builder)
    {
        builder.Services.AddSingleton<IOperationCompletionListener, CommandCompletionEventSink>();
        builder.Services.TryAddSingleton<LocalEventQueue>();
        builder.Services.TryAddSingleton<EventGateway>();
        builder.Services.AddHostedService<LocalEventScheduler>();
        return builder;
    }
}
