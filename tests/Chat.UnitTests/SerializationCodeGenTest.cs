namespace ActualChat.Chat.UnitTests;

public class SerializationCodeGenTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        SerializationCodeGen.ValidateType<Change<string>>();

        // Chat.Contracts
        SerializationCodeGen.ValidateType<ChatEntrySlim>();
        SerializationCodeGen.ValidateType<ChangedAuthorsQuery>();
        SerializationCodeGen.ValidateType<ChangedChatsQuery>();
        SerializationCodeGen.ValidateType<ChangedEntriesQuery>();

        // Api - Chat models
        SerializationCodeGen.ValidateType<Author>();
        SerializationCodeGen.ValidateType<AuthorFull>();
        SerializationCodeGen.ValidateType<Chat>();
        // ChatEntry / ChatNews / ChatTile are MessagePack-only unions; legacy MemoryPack
        // shape lives in LegacyChatEntry / LegacyChatNews / LegacyChatTile.
        SerializationCodeGen.ValidateMessagePackOnlyType<ChatEntry>();
        SerializationCodeGen.ValidateMessagePackOnlyType<ChatNews>();
        SerializationCodeGen.ValidateType<Conversation>();
        SerializationCodeGen.ValidateType<Mention>();
        SerializationCodeGen.ValidateType<Place>();
        SerializationCodeGen.ValidateType<Reaction>();
        SerializationCodeGen.ValidateType<ReactionSummary>();
        SerializationCodeGen.ValidateType<Role>();
        SerializationCodeGen.ValidateType<Translation>();

        // Api - User models
        SerializationCodeGen.ValidateType<Account>();
        SerializationCodeGen.ValidateType<AccountFull>();
        SerializationCodeGen.ValidateType<Avatar>();
        SerializationCodeGen.ValidateType<AvatarFull>();
        SerializationCodeGen.ValidateType<ChatPosition>();

        // Backend events
        SerializationCodeGen.ValidateType<ChatChangedEvent>();
        // Carry ChatEntry — MessagePack-only.
        SerializationCodeGen.ValidateMessagePackOnlyType<ChatEntryChangedEvent>();
        SerializationCodeGen.ValidateMessagePackOnlyType<ReactionChangedEvent>();
        SerializationCodeGen.ValidateType<AccountChangedEvent>();
        SerializationCodeGen.ValidateType<AuthorUpsertedEvent>();
        SerializationCodeGen.ValidateType<PlaceChangedEvent>();
        SerializationCodeGen.ValidateType<ContactChangedEvent>();
    }
}
