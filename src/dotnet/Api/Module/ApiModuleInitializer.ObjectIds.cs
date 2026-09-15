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
        TypedObjectId.Register<AliasId>("alias");
        TypedObjectId.Register<ContactId>("contact");
        TypedObjectId.Register<ConversationId>("conversation");
        TypedObjectId.Register<ContentId>("content");
        TypedObjectId.Register<Country>("country");
        TypedObjectId.Register<Email>("email");
        TypedObjectId.Register<Emoji>("emoji");
        TypedObjectId.Register<ExplicitNotificationId>("explicit-notification");
        TypedObjectId.Register<ExternalContactId>("external-contact");
        TypedObjectId.Register<Interest>("interest");
        TypedObjectId.Register<Language>("language");
        TypedObjectId.Register<MentionRef>("mention");
        TypedObjectId.Register<NotificationId>("notification");
        TypedObjectId.Register<Phone>("phone");
        TypedObjectId.Register<SharedLocationId>("shared-location");
        TypedObjectId.Register<StreamId>("stream");
        TypedObjectId.Register<TranscriberId>("transcriber");
        TypedObjectId.Register<TranslationId>("translation");
        TypedObjectId.Register<TranslationSourceId>("translation-source");
        TypedObjectId.Register<UploadId>("upload");
        TypedObjectId.Register<UserDeviceId>("user-device");
    }
}
