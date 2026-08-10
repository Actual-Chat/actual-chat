namespace ActualChat.App.AotHelper;

/// <summary>
/// Mirrors what ActualLab's <c>ProxyCodeKeeper</c> retains for every generated proxy method.
/// <para>
/// The proxy generator emits <c>KeepAsyncMethod&lt;TUnwrapped, T0, …&gt;("Name")</c> per method, which
/// fans out into ~35 generic instantiations per result type and an <c>ArgumentList</c> accessor pair
/// per argument type. Those are all created reflectively at run time, so nothing references them from
/// IL and a full R2R build cannot enumerate them — the same reason they show up interpreted on iOS.
/// </para>
/// <para>
/// The name table below mirrors <c>ProxyCodeKeeper.KeepMethodResult</c> and the Fusion / Rpc / Commander
/// <c>IExtension</c> implementations in ActualLab.Fusion. Entries are matched by name suffix rather than
/// by <c>typeof</c> because most are nested or internal; anything that stops resolving is reported
/// rather than silently dropped.
/// </para>
/// </summary>
public static class ProxyKeepDiscovery
{
    // ProxyCodeKeeper.KeepMethodResult + the three IExtension implementations.
    private static readonly string[] ResultTypeNames = [
        "IGenericInstanceFactory`1",
        "Result+NewErrorFactory`1",
        "TaskExt+FromExceptionFactory`1",
        "TaskExt+FromCancelledTaskFactory`1",
        "TaskExt+ToTypedValueTaskFactory`1",
        "TaskExt+ToTypedResultSynchronouslyFactory`1",
        "TaskExt+ToObjectValueTaskFactory`1",
        "TaskExt+ToUntypedResultSynchronouslyFactory`1",
        "TaskExt+GetResultAsObjectSynchronouslyFactory`1",
        "MethodDef+TargetAsyncInvokerFactory`1",
        "MethodDef+InterceptorAsyncInvokerFactory`1",
        "MethodDef+InterceptedAsyncInvokerFactory`1",
        "MethodDef+TargetObjectAsyncInvokerFactory`1",
        "MethodDef+InterceptorObjectAsyncInvokerFactory`1",
        "MethodDef+InterceptedObjectAsyncInvokerFactory`1",
        "MethodDef+UniversalAsyncResultConverterFactory`1",
        "ComputeMethodFunction`1",
        "ConsolidatingComputeMethodFunction`1",
        "RemoteComputeMethodFunction`1",
        "ComputeFunctionExt+CompleteProduceValuePromiseFactory`1",
        "ComputeFunctionExt+CompleteProduceValuePromiseWithSynchronizerFactory`1",
        "RpcOutboundComputeCall`1",
        "RpcInboundComputeCall`1",
        "RpcOutboundCall`1",
        "RpcInboundCall`1",
        "RpcInboundNotFoundCall`1",
        "RpcMiddlewareContext`1",
        "RpcMethodDef+InboundCallServerInvokerFactory`1",
        "RpcMethodDef+InboundCallMiddlewareInvokerFactory`1",
        "CommandContext`1",
        "CommandContextExt+TypedCallFactory`1",
        // Not named by a keeper, but reached through the ones above and missing without them.
        "Computed`1",
        "ComputeMethodComputed`1",
        "RemoteComputed`1",
        "MessagePackByteSerializer`1",
    ];
    // FusionProxyCodeKeeperExtension.KeepMethodArgument + RpcProxyCodeKeeperExtension.KeepMethodArgument.
    private static readonly string[] ArgumentTypeNames = [
        "Completion`1",
        "MessagePackByteSerializer`1",
    ];
    // The generic methods ProxyCodeKeeper.KeepMethodResult calls on a null instance.
    private static readonly (string Type, string Method)[] ResultMethodNames = [
        ("MethodDef", "WrapResult"),
        ("MethodDef", "WrapAsyncInvokerResult"),
        ("MethodDef", "WrapResultOfAsyncMethod"),
        ("MethodDef", "WrapAsyncInvokerResultOfAsyncMethod"),
        ("MethodDef", "SelectAsyncInvoker"),
        ("MethodDef", "GetCachedFunc"),
        ("Interceptor", "CreateTypedHandler"),
    ];
    // ArgumentListCodeKeeper.KeepArgumentListArgument: list.Get<TArg>(0) / list.Set<TArg>(0, default).
    private static readonly string[] ArgumentListTypeNames = [
        "ArgumentList0", "ArgumentListS1", "ArgumentListS2", "ArgumentListS3", "ArgumentListS4",
        "ArgumentListS5", "ArgumentListS6", "ArgumentListS7", "ArgumentListS8", "ArgumentListS9",
        "ArgumentListS10",
    ];
    private static readonly string[] ArgumentListMethodNames = ["Get", "Set"];

    public static void Discover(
        ISet<Type> types, ICollection<MethodBase> methods, ICollection<string> unresolved)
    {
        var proxyInterface = TypeResolver.Resolve("ActualLab.Interception.IProxy", unresolved);
        if (proxyInterface == null)
            return;

        var resultTypes = new HashSet<Type>();
        var argumentTypes = new HashSet<Type>();
        foreach (var proxy in EnumerateProxies(proxyInterface)) {
            types.Add(proxy);
            foreach (var method in proxy.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                if (method.IsSpecialName || method.ContainsGenericParameters)
                    continue;
                resultTypes.Add(Unwrap(method.ReturnType));
                foreach (var parameter in method.GetParameters())
                    argumentTypes.Add(parameter.ParameterType);
            }
        }

        foreach (var resultType in resultTypes) {
            if (resultType == typeof(void))
                continue;
            AddInstantiations(types, ResultTypeNames, resultType, unresolved);
            AddNested(types, resultType, unresolved);
            AddGenericMethods(methods, resultType, unresolved);
        }
        foreach (var argumentType in argumentTypes) {
            AddInstantiations(types, ArgumentTypeNames, argumentType, unresolved);
            AddArgumentListAccessors(methods, argumentType, unresolved);
        }
    }

    private static IEnumerable<Type> EnumerateProxies(Type proxyInterface)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            Type[] assemblyTypes;
            try {
                assemblyTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e) {
                assemblyTypes = e.Types.Where(x => x != null).ToArray()!;
            }
            foreach (var type in assemblyTypes) {
                if (!type.IsAbstract && !type.ContainsGenericParameters && proxyInterface.IsAssignableFrom(type))
                    yield return type;
            }
        }
    }

    private static Type Unwrap(Type returnType)
    {
        if (!returnType.IsConstructedGenericType)
            return returnType;

        var definition = returnType.GetGenericTypeDefinition();
        return definition == typeof(Task<>) || definition == typeof(ValueTask<>)
            ? returnType.GetGenericArguments()[0]
            : returnType;
    }

    private static void AddInstantiations(
        ISet<Type> types, string[] names, Type argument, ICollection<string> unresolved)
    {
        foreach (var name in names) {
            var definition = TypeResolver.ResolveBySuffix(name, unresolved);
            if (definition != null)
                TypeResolver.TryInstantiate(types, definition, argument);
        }
    }

    private static void AddNested(ISet<Type> types, Type argument, ICollection<string> unresolved)
    {
        types.Add(typeof(Task<>).MakeGenericType(argument));
        types.Add(typeof(ValueTask<>).MakeGenericType(argument));
        var result = TypeResolver.ResolveBySuffix("Result`1", unresolved);
        if (result == null)
            return;

        TypeResolver.TryInstantiate(types, result, argument);
        TypeResolver.TryInstantiate(types, result, typeof(Task<>).MakeGenericType(argument));
        TypeResolver.TryInstantiate(types, result, typeof(ValueTask<>).MakeGenericType(argument));
    }

    private static void AddGenericMethods(
        ICollection<MethodBase> methods, Type argument, ICollection<string> unresolved)
    {
        foreach (var (typeName, methodName) in ResultMethodNames) {
            var type = TypeResolver.ResolveBySuffix(typeName, unresolved);
            if (type == null)
                continue;
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                if (method.Name != methodName || !method.IsGenericMethodDefinition)
                    continue;
                if (method.GetGenericArguments().Length != 1)
                    continue;
                TypeResolver.TryInstantiate(methods, method, argument);
            }
        }
    }

    private static void AddArgumentListAccessors(
        ICollection<MethodBase> methods, Type argument, ICollection<string> unresolved)
    {
        foreach (var typeName in ArgumentListTypeNames) {
            var type = TypeResolver.ResolveBySuffix(typeName, unresolved);
            if (type == null)
                continue;
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
                if (!ArgumentListMethodNames.Contains(method.Name, StringComparer.Ordinal))
                    continue;
                if (!method.IsGenericMethodDefinition || method.GetGenericArguments().Length != 1)
                    continue;
                TypeResolver.TryInstantiate(methods, method, argument);
            }
        }
    }
}
