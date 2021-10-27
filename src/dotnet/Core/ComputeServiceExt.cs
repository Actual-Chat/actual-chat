using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Stl.CommandR.Interception;
using Stl.DependencyInjection;
using Stl.Fusion.Interception;
using Stl.Interception.Interceptors;

namespace ActualChat;

public static class ComputeServiceExt
{
    private static readonly ConcurrentDictionary<Type, Func<object, IServiceProvider>> CachedServiceProviderGetters = new();

    public static IServiceProvider GetServiceProvider(this IComputeService computeService)
        => GetServiceProviderFromProxy(computeService);

    public static IServiceProvider GetServiceProvider(this ICommandService commandService)
        => GetServiceProviderFromProxy(commandService);

    private static IServiceProvider GetServiceProviderFromProxy(object proxy)
        => CachedServiceProviderGetters.GetOrAdd(
            proxy.GetType(),
            type => {
                var fInterceptors = type.GetField("__interceptors", BindingFlags.Instance | BindingFlags.NonPublic);
                var pObj = Expression.Parameter(typeof(object), "obj");
                var interceptorsGetter = Expression.Lambda<Func<object, object>>(
                    Expression.Field(
                        Expression.ConvertChecked(pObj, type),
                        fInterceptors!),
                    pObj
                    ).Compile();
                return proxy1 => {
                    var interceptors = (IEnumerable<InterceptorBase>) interceptorsGetter.Invoke(proxy1);
                    return interceptors.First().Services;
                };
            }).Invoke(proxy);
}
