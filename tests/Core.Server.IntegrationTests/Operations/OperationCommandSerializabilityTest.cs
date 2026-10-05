using ActualChat.Flows;
using ActualChat.Serialization.Internal;
using ActualChat.Testing.Host;
using ActualLab.CommandR.Configuration;
using MessagePack;

namespace ActualChat.Core.Server.IntegrationTests.Operations;

// DbOperation stores the command that produced it, so under DataFormat.Bytes every command that
// can open an operation scope has to have a MessagePack formatter. Newtonsoft (DataFormat.Text)
// serialized anything by reflection, which hid the ones that don't.
[Trait("Category", "Slow")]
public sealed class OperationCommandSerializabilityTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(OperationCommandSerializabilityTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 120_000)]
    public async Task EveryOperationCommandShouldHaveAMessagePackFormatter()
    {
        await using var h = await NewAppHost();
        var resolver = AppMessagePackResolver.Instance;

        // act
        var commandTypes = h.Services.GetRequiredService<CommandHandlerRegistry>().Handlers
            .Select(x => x.CommandType)
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Distinct()
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        // These two are deliberately not serializable - Flow and the FlowResumeEvent-s they carry
        // are live objects - and FlowBackend keeps them out of _Operations with StoreMode.None,
        // so nothing ever asks MessagePack for them. Any OTHER command without a formatter would
        // throw on its first commit under DataFormat.Bytes.
        var exempt = new[] { typeof(Flows_Store), typeof(Flows_ScheduleResume) };
        var missing = commandTypes
            .Where(t => !exempt.Contains(t) && !HasFormatter(resolver, t))
            .ToList();
        Out.WriteLine($"{commandTypes.Count} command type(s), {missing.Count} without a formatter:");
        foreach (var type in missing)
            Out.WriteLine($"  {type.FullName}");

        // assert
        missing.Should().BeEmpty(
            "DbOperation serializes the command it was produced by, so DataFormat.Bytes cannot "
            + "store an operation whose command MessagePack has no formatter for");
        return;

        static bool HasFormatter(IFormatterResolver resolver, Type commandType)
        {
            // GetFormatter<T> is generic and the type is only known at run time
            var method = typeof(IFormatterResolver).GetMethod(nameof(IFormatterResolver.GetFormatter))!
                .MakeGenericMethod(commandType);
            try {
                return method.Invoke(resolver, null) is not null;
            }
            catch (Exception) {
                return false;
            }
        }
    }
}
