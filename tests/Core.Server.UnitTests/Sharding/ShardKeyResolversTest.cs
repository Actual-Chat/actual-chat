using ActualChat.Flows;

namespace ActualChat.Core.Server.UnitTests.Sharding;

public class ShardKeyResolversTest
{
    [Fact]
    public void CompositeIdsShouldRouteWithTheirOwningObject()
    {
        // arrange
        var userId = UserId.Parse("abcdef");
        var chatId = ChatId.Parse("ghijkl");
        var entryId = ChatEntryId.New(chatId, 1);
        var deviceId = UserDeviceId.New(userId, "device");
        StringIdentifier[] chatIds = [
            AuthorId.New(chatId, 1), entryId, RoleId.New(chatId, 1), ConversationId.New(chatId, 1),
            TranslationSourceId.New(entryId), TranslationId.New(entryId, Language.Parse("en")),
            MentionRef.NewAuthor(AuthorId.New(chatId, 2)),
        ];
        StringIdentifier[] userIds = [
            ContactId.NewAny(userId, chatId), deviceId, ExternalContactId.New(deviceId, "contact"),
            NotificationId.New(userId, NotificationKind.Message, "message"),
            ExplicitNotificationId.New(userId, ExplicitNotificationKind.NotifyMentionedMembers, "mention"),
        ];

        // act, assert
        foreach (var id in chatIds)
            ShardKeyResolvers.GetUntyped(id.GetType())(id).Should().Be(chatId.ShardKey);
        foreach (var id in userIds)
            ShardKeyResolvers.GetUntyped(id.GetType())(id).Should().Be(userId.ShardKey);
    }

    [Fact]
    public void SymbolIdsShouldUseTheirOwnRoutingRule()
    {
        // arrange
        var first = new FlowId("first", "same-arguments");
        var second = new FlowId("second", "same-arguments");
        var resolver = ShardKeyResolvers.Get<FlowId>();
        var nullableResolver = ShardKeyResolvers.Get<FlowId?>();

        // act, assert
        resolver(first).Should().Be(ShardKey.New(first.Arguments));
        resolver(second).Should().Be(resolver(first));
        nullableResolver(first).Should().Be(resolver(first));
        nullableResolver(null).Should().Be(default);
        resolver(FlowId.None).Should().Be(default);
    }

    [Fact]
    public void InterfaceRoutingShouldTakePrecedenceOverRegisteredBaseRouting()
    {
        // arrange
        ShardKeyResolvers.Register<BaseValue>(_ => new ShardKey(1));
        var value = new RoutedValue(new ShardKey(uint.MaxValue));

        // act
        var key = ShardKeyResolvers.Get<RoutedValue>()(value);

        // assert
        key.Should().Be(value.ShardKey);
        ShardKeyResolvers.Get<RoutedValue>()(null!).Should().Be(default);
    }

    [Fact]
    public void NullableValueTypesShouldUseTheirInterfaceWithoutRegistration()
    {
        // arrange
        var value = new RoutedStruct(new ShardKey(0x80000000));
        var resolver = ShardKeyResolvers.Get<RoutedStruct?>();

        // act, assert
        resolver(value).Should().Be(value.ShardKey);
        resolver(null).Should().Be(default);
        ShardKeyResolvers.GetUntyped(typeof(RoutedStruct?))(value).Should().Be(value.ShardKey);
    }

    [Fact]
    public void RegisteringOverridesForShardKeyProvidersShouldFail()
    {
        // act, assert
        FluentActions.Invoking(() => ShardKeyResolvers.Register<UserId>(_ => default))
            .Should().Throw<Exception>().WithMessage("*IHasShardKey*");
        FluentActions.Invoking(() => ShardKeyResolvers.Register<RoutedStruct?>(_ => default))
            .Should().Throw<Exception>().WithMessage("*IHasShardKey*");
        FluentActions.Invoking(() => ShardKeyResolvers.Register<IStringIdentifier>(_ => default))
            .Should().Throw<Exception>().WithMessage("*IHasShardKey*");
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x7fffffffu)]
    [InlineData(0x80000000u)]
    [InlineData(uint.MaxValue)]
    public void UnsignedKeysShouldPreserveExistingShardAssignments(uint value)
    {
        // arrange
        var scheme = ShardScheme.ChatBackend;
        var key = new ShardKey(value);
        var expected = unchecked((int)value).PositiveModulo(scheme.ShardCount);

        // act, assert
        scheme.GetShardIndex(key).Should().Be(expected);
        scheme.TryGetShardIndex(key).Should().Be(expected);
        MeshRef.Shard(scheme, key).ShardRef.GetShardIndex().Should().Be(expected);
    }

    // Nested types

    private class BaseValue;
    private sealed class RoutedValue(ShardKey shardKey) : BaseValue, IHasShardKey
    {
        public ShardKey ShardKey { get; } = shardKey;
    }

    private readonly record struct RoutedStruct(ShardKey ShardKey) : IHasShardKey;
}
