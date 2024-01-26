using System.Diagnostics.CodeAnalysis;
using ActualChat.Commands.Internal;
using ActualLab.CommandR.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NATS.Client.Hosting;

namespace ActualChat.Commands;

public static class ServiceCollectionExt
{
    [RequiresUnreferencedCode(UnreferencedCode.Commander)]
    public static IServiceCollection AddNatsCommandQueues(
        this IServiceCollection services,
        Func<IServiceProvider, NatsCommandQueues.Options>? optionsBuilder = null)
    {
        services.AddCommander();
        services.AddNats(
            poolSize: 4,
            opts => opts with {
                // AuthOpts =
                // Url =
               // TlsOpts =
            });
        if (!services.HasService<ICommandQueues>()) {
            services.AddSingleton<ICommandQueues, NatsCommandQueues>();
            services.AddSingleton<ICommandQueueIdProvider>(c => new ShardCommandQueueIdProvider());
            services.AddSingleton<ShardCommandQueueScheduler>();
            services.AddHostedService<ShardCommandQueueScheduler>();
            services.AddSingleton<ShardCommandQueueScheduler.Options>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationCompletionListener, EnqueueOnCompletionProcessor>());
        }
        services.AddSingleton(optionsBuilder ?? (static _ => new NatsCommandQueues.Options()));
        return services;
    }
}
