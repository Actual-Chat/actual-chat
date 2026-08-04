using System.Reflection;
using ActualChat.Contacts;
using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.Hashing;

namespace ActualChat.Testing;

/// <summary>
/// Builds deterministic, fully-populated instances for serialization tests: every scalar gets a
/// distinct value, so comparing the result across serializers stays discriminating.
/// Returns null when a type cannot be built; callers report those rather than failing.
/// </summary>
public sealed class TestValueBuilder
{
    private const int MaxDepth = 6;
    private readonly Dictionary<Type, Func<TestValueBuilder, Type, object?>> _providers = [];
    private int _counter;

    public static TestValueBuilder New()
        => new TestValueBuilder().AddDefaults();

    public int NextInt()
        => ++_counter;

    public string NextString()
        => "v" + ++_counter;

    public TestValueBuilder Set(Type type, Func<TestValueBuilder, Type, object?> provider)
    {
        _providers[type] = provider;

        return this;
    }

    public object? Build(Type type)
        => Build(type, 0);

    public TestValueBuilder AddDefaults()
    {
        Set(typeof(Symbol), (b, _) => new Symbol(b.NextString()));
        Set(typeof(Moment), (b, _) => new Moment(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            + TimeSpan.FromSeconds(b.NextInt()));
        Set(typeof(DateTime), (b, _) => new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddSeconds(b.NextInt()));
        Set(typeof(DateTimeOffset), (b, _) => new DateTimeOffset(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)).AddSeconds(b.NextInt()));
        Set(typeof(TimeSpan), (b, _) => TimeSpan.FromSeconds(b.NextInt()));
        Set(typeof(Guid), (b, _) => new Guid(b.NextInt(), 0, 0, new byte[8]));
        Set(typeof(byte[]), (b, _) => new[] { (byte)b.NextInt() });
        Set(typeof(HashString), (b, _) => new HashString(
            HashAlgorithm.SHA256, HashEncoding.Base16, b.NextString()));

        // Identifiers validate their format, so they are built through their own factories
        // rather than from generated strings.
        Set(typeof(ChatId), (_, _) => ChatId.Parse("the-actual-one"));
        Set(typeof(UserId), (_, _) => UserId.Parse("testUser1"));
        Set(typeof(ConversationId), (b, _) => ConversationId.New(ChatId.Parse("the-actual-one"), b.NextInt()));
        Set(typeof(ContactId), (_, _) => ContactId.NewAny(UserId.Parse("testUser1"), ChatId.Parse("the-actual-one")));
        Set(typeof(ContactSubset), (_, _) => ContactSubset.All());
        Set(typeof(StreamId), (b, _) => StreamId.New(new NodeRef("testnode"), b.NextString()));

        // Roots whose own invariants reflection can't satisfy: a thread contact needs a
        // ContactId over a thread chat, and FlowResumeEvent has no public constructor.
        Set(typeof(ThreadContact), (b, _) => new ThreadContact(
            ContactId.NewAny(UserId.Parse("testUser1"), ThreadChatId.Parse("the-actual-one-1")), b.NextInt()));
        Set(typeof(HeaderMarkup), (b, _) => new HeaderMarkup(
            1 + (b.NextInt() % HeaderMarkup.MaxLevel), new PlainTextMarkup(b.NextString())));
        Set(typeof(FlowResumeEvent), (_, t) => t
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(FlowId)])!
            .Invoke([new FlowId("test", "args")]));

        return this;
    }

    // Private methods

    private object? Build(Type type, int depth)
    {
        if (_providers.TryGetValue(type, out var provider))
            return provider.Invoke(this, type);
        if (type.IsGenericType
            && _providers.TryGetValue(type.GetGenericTypeDefinition(), out var genericProvider))
            return genericProvider.Invoke(this, type);

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return Build(underlying, depth);
        if (type == typeof(string))
            return NextString();
        if (type.IsEnum)
            return BuildEnum(type);
        if (type.IsPrimitive || type == typeof(decimal))
            return BuildPrimitive(type);
        if (depth >= MaxDepth)
            return null;
        if (type.IsArray)
            return BuildArray(type, depth);

        return BuildComplex(type, depth);
    }

    private object? BuildEnum(Type type)
    {
        var values = Enum.GetValues(type);

        return values.Length == 0 ? Activator.CreateInstance(type) : values.GetValue(NextInt() % values.Length);
    }

    private object BuildPrimitive(Type type)
    {
        var n = NextInt();
        if (type == typeof(bool))
            return n % 2 == 0;
        if (type == typeof(char))
            return (char)('a' + (n % 26));
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
            return Convert.ChangeType(n + 0.5, type);

        return Convert.ChangeType(n % 100, type);
    }

    private object? BuildArray(Type type, int depth)
    {
        var itemType = type.GetElementType()!;
        var item = Build(itemType, depth + 1);
        var array = Array.CreateInstance(itemType, item is null ? 0 : 1);
        if (item is not null)
            array.SetValue(item, 0);

        return array;
    }

    private object? BuildComplex(Type type, int depth)
    {
        if (type.IsAbstract || type.IsInterface) {
            var concrete = FirstUnionCase(type);

            return concrete is null ? null : Build(concrete, depth);
        }

        return TryConstructors(type, depth, BindingFlags.Public | BindingFlags.Instance);
    }

    private object? TryConstructors(Type type, int depth, BindingFlags flags)
    {
        // The single-parameter copy constructor is excluded: it needs an instance of the very
        // type being built.
        var ctors = type.GetConstructors(flags)
            .Where(c => c.GetParameters() is not [{ ParameterType: var p }] || p != type)
            .OrderByDescending(c => c.GetParameters().Length);
        foreach (var ctor in ctors) {
            var value = TryConstruct(ctor, depth);
            if (value is null)
                continue;

            Populate(value, type, depth);

            return value;
        }

        return null;
    }

    private object? TryConstruct(ConstructorInfo ctor, int depth)
    {
        try {
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                args[i] = Build(parameters[i].ParameterType, depth + 1);

            return ctor.Invoke(args);
        }
        catch {
            return null;
        }
    }

    private void Populate(object value, Type type, int depth)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (!property.CanWrite || property.GetIndexParameters().Length != 0)
                continue;

            try {
                var current = property.GetValue(value);
                if (current is not null && current != GetDefault(property.PropertyType))
                    continue;

                property.SetValue(value, Build(property.PropertyType, depth + 1));
            }
            catch {
                // A property that rejects the generated value keeps whatever the constructor set.
            }
        }
    }

    private static object? GetDefault(Type type)
        => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static Type? FirstUnionCase(Type type)
        => type.GetCustomAttributes()
            .Where(a => a.GetType().FullName == "MessagePack.UnionAttribute")
            .Select(a => a.GetType().GetProperty("SubType")?.GetValue(a) as Type)
            .FirstOrDefault(t => t is not null);
}
