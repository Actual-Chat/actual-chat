using System.Runtime.CompilerServices;

namespace ActualChat.App.AotHelper;

/// <summary>
/// Names the async plumbing behind every <c>async</c> method in our assemblies:
/// the compiler-generated state machine and the <c>AsyncTaskMethodBuilder&lt;T&gt;.AsyncStateMachineBox&lt;TStateMachine&gt;</c>
/// instantiation that drives it.
/// <para>
/// The box is a value-type-parameterised instantiation created by the runtime on first await, so a
/// full R2R build never sees it. It is the single largest interpreted category on iOS — measured at
/// 1,615 of the 4,513 methods a device recording covers and a keeper-derived profile did not.
/// </para>
/// </summary>
public static class AsyncStateMachineKeepDiscovery
{
    private const string BuilderFieldName = "<>t__builder";
    private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static void Discover(
        ISet<Type> types, ICollection<MethodBase> methods, ICollection<string> unresolved,
        GenericArgumentPool pool)
    {
        var voidResult = TypeResolver.Resolve("System.Threading.Tasks.VoidTaskResult", unresolved);
        var boxDefinition = typeof(AsyncTaskMethodBuilder<>)
            .GetNestedType("AsyncStateMachineBox`1", BindingFlags.Public | BindingFlags.NonPublic);
        if (boxDefinition == null)
            unresolved.Add("AsyncTaskMethodBuilder`1+AsyncStateMachineBox`1");

        foreach (var type in EnumerateOwnTypes()) {
            foreach (var method in EnumerateMethods(type)) {
                var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
                if (stateMachine == null)
                    continue;

                if (!stateMachine.ContainsGenericParameters) {
                    methods.Add(method);
                    Add(types, stateMachine, voidResult, boxDefinition);
                    continue;
                }

                // An async method on a generic type - or a generic async method - has an open state
                // machine, and the runtime builds one instantiation of it per distinct argument set.
                foreach (var instance in pool.Instantiate(stateMachine))
                    Add(types, instance, voidResult, boxDefinition);
            }
        }
    }

    private static void Add(ISet<Type> types, Type stateMachine, Type? voidResult, Type? boxDefinition)
    {
        types.Add(stateMachine);
        var builder = stateMachine.GetField(
            BuilderFieldName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.FieldType;
        if (builder == null)
            return;

        types.Add(builder);
        var result = builder.IsConstructedGenericType
            ? builder.GetGenericArguments()[0]
            : voidResult;
        if (result == null || boxDefinition == null)
            return;

        TryAdd(types, () => typeof(AsyncTaskMethodBuilder<>).MakeGenericType(result));
        TryAdd(types, () => boxDefinition.MakeGenericType(result, stateMachine));
    }

    private static void TryAdd(ISet<Type> types, Func<Type> factory)
    {
        try {
            types.Add(factory.Invoke());
        }
        catch (Exception e) when (e is ArgumentException or TypeLoadException) {
            // Constraint violation - there is no such instantiation to keep.
        }
    }

    private static IEnumerable<Type> EnumerateOwnTypes()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var name = assembly.GetName().Name ?? "";
            if (!name.StartsWith("ActualChat.", StringComparison.Ordinal)
                && !name.StartsWith("ActualLab.", StringComparison.Ordinal))
                continue;

            Type[] assemblyTypes;
            try {
                assemblyTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e) {
                assemblyTypes = e.Types.Where(x => x != null).ToArray()!;
            }
            foreach (var type in assemblyTypes)
                yield return type;
        }
    }

    private static IEnumerable<MethodBase> EnumerateMethods(Type type)
    {
        MethodBase[] methods;
        try {
            methods = type.GetMethods(MemberFlags);
        }
        catch (Exception e) when (e is TypeLoadException or FileNotFoundException or NotSupportedException) {
            yield break;
        }
        foreach (var method in methods)
            yield return method;
    }
}
