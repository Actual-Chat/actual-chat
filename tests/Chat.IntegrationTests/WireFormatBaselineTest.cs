using ActualLab.IO;

namespace ActualChat.Chat.IntegrationTests;

// Skipped by default: this is a migration instrument, not a regression test. It needs a
// baseline captured from a *previous* build, which is gitignored and therefore never present
// in CI, so leaving it enabled only ever produces a no-op pass.
//
// What it solved: the MemoryPack removal, the Markup [Union] rework, and the ShardKey/JsonIgnore
// sweep all had to be format-neutral for MessagePack. Round-trip tests can't show that — a type
// that serializes and deserializes consistently is still free to have changed its wire layout.
// This captured every [MessagePackObject] type's bytes before each change and asserted they were
// byte-identical after, which is how the Markup rework was confirmed not to move the format while
// deliberately changing JSON output for 28 types.
//
// When to reuse: any change that touches serialization attributes, member order, or type shape —
// see docs/architecture/serialization.md for what each serializer honors. To use it,
// drop the Skip, run with ActualChat_WriteWireFormatBaseline=true on the pre-change build, make
// the change, then run it again with the variable unset. MessagePack differences fail; JSON
// differences are only reported, since some refactors are meant to move JSON.

/// <summary>
/// Guards the serialized format of every <c>[MessagePackObject]</c> type against refactors that
/// are meant to be format-neutral. The baseline is generated locally from the pre-refactor build
/// and is never committed, so its absence means "nothing to guard yet".
/// </summary>
public sealed class WireFormatBaselineTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string BaselineDirName = "wire-format-all";

    [Fact(Skip = "Migration instrument — see the comment above for when to re-enable it.")]
    public void RunTest()
    {
        // arrange
        var dir = BaselineDir();
        var types = SerializableTypes();

        // act
        if (MustWriteBaseline())
            Write(dir, types);

        // assert
        if (!Directory.Exists(dir)) {
            WriteLine($"No baseline in {dir}.");
            WriteLine("Set ActualChat_WriteWireFormatBaseline=true and re-run to create one.");
            return;
        }

        Verify(dir, types);
    }

    // Private methods

    private static bool MustWriteBaseline()
        => Environment.GetEnvironmentVariable("ActualChat_WriteWireFormatBaseline") is "true" or "1";

    private static FilePath BaselineDir([CallerFilePath] string? callerFilePath = null)
        => FilePath.New(callerFilePath).DirectoryPath & "data" & BaselineDirName;

    private static List<Type> SerializableTypes()
        => AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("ActualChat") == true)
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetCustomAttribute<MessagePackObjectAttribute>(false) is not null)
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false })
            .OrderBy(t => t.FullName)
            .ToList();

    private void Write(FilePath dir, List<Type> types)
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);

        Directory.CreateDirectory(dir);
        var builder = TestValueBuilder.New();
        var written = 0;
        var failures = new List<string>();
        foreach (var type in types) {
            var value = Build(builder, type, failures);
            if (value is null)
                continue;

            try {
                using var buffer = Serializers.MessagePack.Write(value, type);
                var bytes = buffer.WrittenMemory.ToArray();
                File.WriteAllBytes(dir & (type.FullName + ".bin"), bytes);
                File.WriteAllText(dir & (type.FullName + ".mp.json"), ToJson(bytes));
                File.WriteAllText(dir & (type.FullName + ".stj.json"), Serializers.SystemJson.Write(value, type));
                File.WriteAllText(dir & (type.FullName + ".nsj.json"), Serializers.NewtonsoftJson.Write(value, type));
                written++;
            }
            catch (Exception e) {
                failures.Add($"{type.FullName}: {Describe(e)}");
            }
        }

        WriteLine($"Wrote {written}/{types.Count} types to {dir}");
        Report("not serializable", failures);
    }

    private void Verify(FilePath dir, List<Type> types)
    {
        var builder = TestValueBuilder.New();
        var ignored = new List<string>();
        var verified = 0;
        var errors = new List<string>();
        var jsonChanges = new List<string>();
        foreach (var type in types) {
            // Every type is rebuilt, even one with no baseline file, so the builder's counter
            // advances exactly as it did during generation and the values line up.
            var value = Build(builder, type, ignored);
            var path = dir & (type.FullName + ".bin");
            if (value is null || !File.Exists(path))
                continue;

            var expected = File.ReadAllBytes(path);
            try {
                Serializers.MessagePack.Read(expected, type, out _);
                using var buffer = Serializers.MessagePack.Write(value, type);
                var actual = buffer.WrittenMemory.ToArray();
                if (!actual.AsSpan().SequenceEqual(expected))
                    errors.Add($"{type.FullName}: MessagePack layout changed\n"
                        + $"      was: {ToJson(expected)}\n      now: {ToJson(actual)}");
                else
                    verified++;
            }
            catch (Exception e) {
                errors.Add($"{type.FullName}: {Describe(e)}");
                continue;
            }

            CompareJson(dir, type, ".stj.json", Serializers.SystemJson.Write(value, type), jsonChanges);
            CompareJson(dir, type, ".nsj.json", Serializers.NewtonsoftJson.Write(value, type), jsonChanges);
        }

        WriteLine($"Verified {verified} types against the baseline in {dir}");
        Report("changed their MessagePack wire format", errors);

        // JSON is reported, not asserted: dropping [DataContract] moves Newtonsoft's output by
        // design, whereas the MessagePack bytes must not move at all.
        Report("changed their JSON output", jsonChanges);
        errors.Count.Should().Be(0,
            "the baseline was written by a build whose wire format this build must still produce");
    }

    private static void CompareJson(
        FilePath dir,
        Type type,
        string suffix,
        string actual,
        List<string> changes)
    {
        var path = dir & (type.FullName + suffix);
        if (!File.Exists(path))
            return;

        var expected = File.ReadAllText(path);
        if (expected != actual)
            changes.Add($"{type.FullName}{suffix}\n      was: {expected}\n      now: {actual}");
    }

    private static string ToJson(byte[] bytes)
        => MessagePackSerializer.ConvertToJson(bytes, MessagePackByteSerializer.DefaultOptions);

    private object? Build(TestValueBuilder builder, Type type, List<string> failures)
    {
        try {
            var value = builder.Build(type);
            if (value is null)
                failures.Add($"{type.FullName}: not buildable");

            return value;
        }
        catch (Exception e) {
            failures.Add($"{type.FullName}: {Describe(e)}");
            return null;
        }
    }

    private void Report(string what, List<string> items)
    {
        if (items.Count == 0)
            return;

        WriteLine($"{items.Count} type(s) {what}:");
        foreach (var item in items)
            WriteLine("  " + item);
    }

    private static string Describe(Exception e)
        => $"{e.GetBaseException().GetType().Name}: {e.GetBaseException().Message.Split('\n')[0]}";
}
