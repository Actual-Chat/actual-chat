namespace ActualChat.Module;

public static partial class ApiModuleInitializer
{
    private static void RegisterContentIds()
    {
        ContentRef.Register<UserId>("u");
        ContentRef.Register<AuthorId>("a");
        ContentRef.Register<GroupChatId>("c");
        ContentRef.Register<PeerChatId>("c2");
        ContentRef.Register<PlaceChatId>("cP");
        ContentRef.Register<ThreadChatId>("cT");
        ContentRef.Register<PlaceId>("p");
        ContentRef.Register<ChatEntryId>("e");
        ContentRef.Register<RoleId>("r");
        ContentRef.Register<MediaId>("m");
        ContentRef.Register<ContactId>("C");
        ContentRef.Register<ConversationId>("cnv");
        ContentRef.Register<SharedLocationId>("loc");
    }
}
