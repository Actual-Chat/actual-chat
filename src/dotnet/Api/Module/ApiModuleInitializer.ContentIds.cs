namespace ActualChat.Module;

public static partial class ApiModuleInitializer
{
    private static void RegisterContentIds()
    {
        ContentRef.Register<UserId>("u");
        ContentRef.Register<AuthorId>("a");
        ContentRef.Register<ChatId>("c");
        ContentRef.Register<PlaceId>("p");
        ContentRef.Register<ChatEntryId>("ce");
        ContentRef.Register<RoleId>("r");
        ContentRef.Register<MediaId>("m");
        ContentRef.Register<ContactId>("ct");
        ContentRef.Register<ConversationId>("conv");
        ContentRef.Register<SharedLocationId>("shared-location");
    }
}
