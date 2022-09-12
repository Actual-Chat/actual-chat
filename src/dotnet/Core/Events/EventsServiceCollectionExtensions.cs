// using Microsoft.Extensions.DependencyInjection.Extensions;
//
// namespace ActualChat.Events;
//
// public static class EventsServiceCollectionExtensions
// {
//     public static IServiceCollection AddEvent<T>(this IServiceCollection services)
//         where T : class, IEvent
//     {
//         services.TryAddSingleton<IEventPublisher, LocalEventPublisher>();
//         services.AddSingleton<LocalEventHub<T>>();
//         return services;
//     }
//
//     public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
//         where TEvent : class, IEvent
//         where THandler : class, IEventHandler<TEvent>
//     {
//         services.AddSingleton(c => c.GetRequiredService<LocalEventHub<TEvent>>().Reader);
//         services.AddSingleton<IEventHandler<TEvent>, THandler>();
//         services.AddHostedService<EventListener<TEvent>>();
//         return services;
//     }
// }
