using ActualChat.MLSearch.Db;
using ActualLab.Redis;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using StackExchange.Redis;


namespace ActualChat.MLSearch.Bot.Services;

internal interface IChatHistoryCache
{
    Task<ChatHistory> GetOrSetDefault(ChatId chatId, ChatHistory defaultValue, CancellationToken cancellationToken);
    Task Set(ChatId chatId, ChatHistory history, CancellationToken cancellationToken);
}

internal class ChatHistoryCache(
    RedisDb<MLSearchDbContext> redisDb,
    IDataProtectionProvider protectionProvider,
    IOptions<ChatbotServicesSettings> settings
) : IChatHistoryCache
{
    private const string RedisKeyPrefix = $".{nameof(ChatHistoryCache)}.";

    public IDataProtector DataProtector = protectionProvider.CreateProtector(nameof(ChatHistoryCache));
    public async Task<ChatHistory> GetOrSetDefault(ChatId chatId, ChatHistory defaultHistory, CancellationToken cancellationToken)
    {
        var key = GetKey(chatId);
        var value = DataProtector.Protect(JsonSerializer.Serialize(defaultHistory));
        var database = await redisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var wasUpdated = await database.StringSetAsync(key, value, settings.Value.ConversationTtl, false, When.NotExists)
            .ConfigureAwait(false);
        if (wasUpdated)
            return defaultHistory;

        var cachedHistory = DataProtector.Unprotect(
            (await database.StringGetAsync(key).ConfigureAwait(false)).ToString()
        );
        return JsonSerializer.Deserialize<ChatHistory>(cachedHistory) ?? defaultHistory;
    }

    public async Task Set(ChatId chatId, ChatHistory history, CancellationToken cancellationToken)
    {
        var key = GetKey(chatId);
        var value = DataProtector.Protect(JsonSerializer.Serialize(history));
        var database = await redisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        _ = await database.StringSetAsync(key, value, settings.Value.ConversationTtl, false, When.Always)
            .ConfigureAwait(false);
    }

    private static string GetKey(ChatId chatId) => string.Concat(RedisKeyPrefix, chatId);
}
