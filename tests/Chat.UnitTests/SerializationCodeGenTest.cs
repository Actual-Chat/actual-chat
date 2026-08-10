namespace ActualChat.Chat.UnitTests;

public class SerializationCodeGenTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        SerializationCodeGen.ValidateType<Change<string>>();

        // Chat.Contracts
        SerializationCodeGen.ValidateMemoryPackType<ChatEntrySlim>();
        SerializationCodeGen.ValidateType<ChangedAuthorsQuery>();
        SerializationCodeGen.ValidateType<ChangedChatsQuery>();
        SerializationCodeGen.ValidateType<ChangedEntriesQuery>();

        // Api - Chat models
        SerializationCodeGen.ValidateType<Author>();
        SerializationCodeGen.ValidateType<AuthorFull>();
        SerializationCodeGen.ValidateType<Chat>();
        SerializationCodeGen.ValidateType<ChatEntry>();
        SerializationCodeGen.ValidateType<ChatNews>();
        SerializationCodeGen.ValidateType<Conversation>();
        SerializationCodeGen.ValidateType<Mention>();
        SerializationCodeGen.ValidateType<Place>();
        SerializationCodeGen.ValidateType<Reaction>();
        SerializationCodeGen.ValidateType<ReactionSummary>();
        SerializationCodeGen.ValidateType<Role>();
        SerializationCodeGen.ValidateType<Translation>();
        SerializationCodeGen.ValidateType<ChatPinnedEntries>();

        // Api - Chat commands
        SerializationCodeGen.ValidateType<Chats_SetPinned>();

        // Api - User models
        SerializationCodeGen.ValidateType<Account>();
        SerializationCodeGen.ValidateType<AccountFull>();
        SerializationCodeGen.ValidateType<Avatar>();
        SerializationCodeGen.ValidateType<AvatarFull>();
        SerializationCodeGen.ValidateType<ChatPosition>();

        // Backend events
        SerializationCodeGen.ValidateType<ChatChangedEvent>();
        SerializationCodeGen.ValidateType<ChatEntryChangedEvent>();
        SerializationCodeGen.ValidateType<ReactionChangedEvent>();
        SerializationCodeGen.ValidateType<AccountChangedEvent>();
        SerializationCodeGen.ValidateType<AuthorUpsertedEvent>();
        SerializationCodeGen.ValidateType<PlaceChangedEvent>();
        SerializationCodeGen.ValidateType<ContactChangedEvent>();
    }
}
