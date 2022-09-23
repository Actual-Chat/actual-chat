using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Commands;

public static class FusionBuilderExt
{
    public static FusionBuilder AddLocalEventScheduler(this FusionBuilder builder)
    {
        var operationCompletionSinkDescriptor = new ServiceDescriptor(
            typeof(IOperationCompletionListener),
            typeof(CommandCompletionCommandSink),
            ServiceLifetime.Singleton);
        if (!builder.Services.Contains(operationCompletionSinkDescriptor, new ServiceDescriptorComparer()))
            builder.Services.Add(operationCompletionSinkDescriptor);
        builder.Services.TryAddSingleton<LocalCommandQueue>();
        builder.Services.TryAddSingleton<CommandGateway>();
        builder.Services.AddHostedService<LocalCommandScheduler>();
        return builder;
    }
}
