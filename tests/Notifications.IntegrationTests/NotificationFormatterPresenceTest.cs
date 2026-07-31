using ActualChat.Serialization.Internal;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace ActualChat.Notifications.IntegrationTests;

/// <summary>
/// Guards the <see cref="Notification"/> union against regressing to a Reflection.Emit-only
/// formatter: the app resolver chain minus its dynamic resolvers - i.e. what Native AOT sees -
/// must resolve and round-trip the union base and every one of its subtypes.
/// </summary>
public class NotificationFormatterPresenceTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly UserId TestUserId = UserId.Parse("9EV1f3");
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly Moment TestMoment = new(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));

    [Fact]
    public void UnionHierarchyResolvesWithoutDynamicCode()
    {
        // The intermediate ChatNotification & Co. are deliberately out: they're abstract, aren't
        // union bases, and nothing serializes through them - only the leaves hit the wire.

        // act
        var missing = new List<string>();
        foreach (var type in UnionSubtypes().Prepend(typeof(Notification))) {
            var formatter = GetFormatter(type);
            Out.WriteLine($"{type.Name}: {formatter?.GetType().FullName ?? "<null>"}");
            if (formatter is null)
                missing.Add(type.FullName ?? type.Name);
        }

        // assert
        missing.Should().BeEmpty(
            "every type of the Notification union must have a source-generated or hand-written "
            + "MessagePack formatter; the listed ones resolve only via DynamicUnionResolver / "
            + "DynamicObjectResolver, which need Reflection.Emit and thus fail under Native AOT");
    }

    [Fact]
    public void EveryUnionSubtypeRoundtripsWithoutDynamicCode()
    {
        // arrange
        var options = new MessagePackSerializerOptions(NoDynamicResolver.Instance);
        var samples = Samples().ToList();
        samples.Select(x => x.GetType()).Should().BeEquivalentTo(UnionSubtypes());

        // act & assert
        foreach (var notification in samples) {
            var bytes = MessagePackSerializer.Serialize(notification, options);
            Out.WriteLine($"{notification.GetType().Name}: {Convert.ToHexString(bytes)}");
            var deserialized = MessagePackSerializer.Deserialize<Notification>(bytes, options);
            var because = $"{notification.GetType().Name} must round-trip through the no-dynamic-code resolver chain";
            // Re-serialization is the fidelity check here: ApiArray equality is by reference,
            // so record equality can never hold across a round-trip.
            deserialized.GetType().Should().Be(notification.GetType(), because);
            MessagePackSerializer.Serialize(deserialized, options).Should().Equal(bytes, because);
        }
    }

    // Private methods

    private static object? GetFormatter(Type type)
    {
        var getFormatter = typeof(IFormatterResolver).GetMethod(nameof(IFormatterResolver.GetFormatter))!;
        return getFormatter.MakeGenericMethod(type).Invoke(NoDynamicResolver.Instance, null);
    }

    private static Type[] UnionSubtypes()
        => typeof(Notification)
            .GetCustomAttributes<UnionAttribute>()
            .Select(x => x.SubType)
            .ToArray();

    private static IEnumerable<Notification> Samples()
    {
        var entryId = ChatEntryId.New(TestChatId, 7);
        var authorId = AuthorId.New(TestChatId, 5);
        var conversationId = ConversationId.New(TestChatId, 2067);
        Notification[] notifications = [
            MessageNotification.New(TestUserId, TestChatId, entryId.LocalId, authorId),
            ReplyNotification.New(TestUserId, TestChatId, entryId.LocalId, authorId),
            InvitationNotification.New(TestUserId, TestChatId, authorId),
            MentionNotification.New(TestUserId, entryId, authorId),
            ReactionNotification.New(TestUserId, entryId, authorId),
            AttentionNotification.New(TestUserId, entryId, authorId),
            ThreadNotification.New(TestUserId, TestChatId, entryId.LocalId, authorId),
            ConversationNotification.New(TestUserId, conversationId, 2100),
            CallNotification.New(TestUserId, conversationId, authorId, true),
        ];
        foreach (var notification in notifications)
            yield return notification with {
                Version = 3,
                Title = "Title",
                Text = "Text",
                IconUrl = "https://x/y.png",
                CreatedAt = TestMoment,
                SentAt = TestMoment,
                HandledAt = TestMoment,
                Actions = ApiArray.New(new NotificationAction(NotificationActionKind.Open, "Open", "/chat/1")),
            };
    }

    // Nested types

    private sealed class NoDynamicResolver : IFormatterResolver
    {
        public static readonly IFormatterResolver Instance = new NoDynamicResolver();
        private static readonly AppMessagePackResolverSettings Settings = new() {
            Resolvers = AppMessagePackResolverSettings.StandardResolvers
                .Where(x => x is not DynamicObjectResolver and not DynamicUnionResolver)
                .ToArray(),
            ExtraFormatters = AppMessagePackResolver.Settings.ExtraFormatters,
        };
        private static readonly ConcurrentDictionary<Assembly, AppMessagePackResolverAssemblyPlan> Plans = new();

        public IMessagePackFormatter<T>? GetFormatter<T>()
        {
            var plan = Plans.GetOrAdd(typeof(T).Assembly, static x => Settings.BuildAssemblyPlan(x));
            if (plan.TryGetFormatter(typeof(T)) is IMessagePackFormatter<T> custom)
                return custom;

            foreach (var resolver in plan.Resolvers) {
                var formatter = resolver.GetFormatter<T>();
                if (formatter is not null)
                    return formatter;
            }
            return null;
        }
    }
}
