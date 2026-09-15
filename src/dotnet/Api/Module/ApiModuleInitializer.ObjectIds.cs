namespace ActualChat.Module;

public static partial class ApiModuleInitializer
{
    private static void RegisterObjectIds()
    {
        TypedObjectId.Register<UserId>("u");
        TypedObjectId.Register<AuthorId>("a");
        TypedObjectId.Register<ChatId>("c");
        TypedObjectId.Register<PlaceId>("p");
        TypedObjectId.Register<ChatEntryId>("ce");
        TypedObjectId.Register<RoleId>("r");
        TypedObjectId.Register<MediaId>("m");
        TypedObjectId.Register<EmojiRef>("e");
        TypedObjectId.Register<AliasId>("~");
        TypedObjectId.Register<ContactId>("ct");
        TypedObjectId.Register<ConversationId>("conv");
        TypedObjectId.Register<ExternalContactId>("external-contact");
        TypedObjectId.Register<SharedLocationId>("shared-location");
        TypedObjectId.Register<UploadId>("upload");
    }
}
