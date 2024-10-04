namespace ActualChat.Chat.ML;

public static class ServiceCollectionExt
{
    public static void AddChatMLServices(this IServiceCollection services)
    {
        services.AddSingleton<IChatDialogFormatter, ChatDialogFormatter>();
        services.AddSingleton<IChatDigestSummarizer, ChatDigestSummarizer>();
    }
}
