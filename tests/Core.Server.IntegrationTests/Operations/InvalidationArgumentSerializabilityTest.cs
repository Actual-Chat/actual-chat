using System.Reflection;
using ActualChat.Serialization.Internal;
using ActualChat.Testing.Host;
using ActualLab.Fusion.Interception;
using ActualLab.Interception.Serialization;
using MessagePack;

namespace ActualChat.Core.Server.IntegrationTests.Operations;

// A deferred invalidation block is recorded as ServiceCall-s, and each one carries its arguments
// through ServiceCall.ByteArgumentListSerializer - a ByteArgumentListSerializer over
// MessagePackByteSerializer.Default, whose options resolve through AppMessagePackResolver
// (CoreModuleInitializer). So every argument of every invalidatable method has to be resolvable
// there, or the call throws when the operation commits.
//
// Enumerating [ComputeMethod]-s rather than the Defer blocks themselves: those methods are exactly
// what a block may invalidate, it includes the protected Pseudo*/\*Internal ones blocks call, and
// it stays true for blocks nobody has written yet.
[Trait("Category", "Slow")]
public sealed class InvalidationArgumentSerializabilityTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(InvalidationArgumentSerializabilityTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 120_000)]
    public async Task EveryComputeMethodArgumentShouldBeMessagePackSerializable()
    {
        await using var h = await NewAppHost();
        var backendServiceDefs = h.Services.GetRequiredService<BackendServiceDefs>();

        // Backend services only: those are the ones whose deferred blocks are recorded as
        // ServiceCall-s and serialized. A UI or API compute service resolves to Local and
        // invalidates in-process, so none of its arguments ever reaches a serializer.
        // act
        var computeMethods = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && (a.FullName ?? "").StartsWith("ActualChat", StringComparison.Ordinal))
            .SelectMany(GetTypes)
            .Where(t => backendServiceDefs.Contains(t))
            .SelectMany(t => t.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.IsDefined(typeof(ComputeMethodAttribute), false))
            .ToList();

        var offenders = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var method in computeMethods)
            foreach (var parameter in method.GetParameters()) {
                var type = parameter.ParameterType;
                if (type == typeof(CancellationToken))
                    continue; // Never serialized - ArgumentList carries it as a slot, not a value
                if (ArgumentListSerializer.IsPolymorphic(type))
                    continue; // Written type-decorated, so the concrete value's formatter is what matters
                if (HasFormatter(type))
                    continue;

                var owner = $"{method.DeclaringType!.GetName()}.{method.Name}";
                if (!offenders.TryGetValue(type.GetName(), out var owners))
                    offenders[type.GetName()] = owners = new SortedSet<string>(StringComparer.Ordinal);
                owners.Add(owner);
            }

        Out.WriteLine($"{computeMethods.Count} [ComputeMethod](s) checked, "
            + $"{offenders.Count} argument type(s) without a MessagePack formatter:");
        foreach (var (type, owners) in offenders)
            Out.WriteLine($"  {type}  <- {string.Join(", ", owners.Take(4))}");

        // assert
        computeMethods.Count.Should().BeGreaterThan(100, "every backend service should have been scanned");
        offenders.Should().BeEmpty(
            "a deferred invalidation call serializes its arguments with MessagePack, so an "
            + "unresolvable argument type makes that invalidation fail at commit time");
        return;

        static IEnumerable<Type> GetTypes(Assembly assembly)
        {
            try {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e) {
                return e.Types.Where(t => t is not null)!;
            }
        }

        static bool HasFormatter(Type type)
        {
            var method = typeof(IFormatterResolver).GetMethod(nameof(IFormatterResolver.GetFormatter))!
                .MakeGenericMethod(type);
            try {
                return method.Invoke(AppMessagePackResolver.Instance, null) is not null;
            }
            catch (Exception) {
                return false;
            }
        }
    }
}
