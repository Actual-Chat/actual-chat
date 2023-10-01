﻿using ActualChat.Chat.Module;

namespace ActualChat.Testing.Host;

public static class ServicesCollectionExt
{
    public static IServiceCollection AddChatDbDataInitialization(
        this IServiceCollection services,
        Action<ChatDbInitializer.InitializeDataOptions> setupAction)
    {
        services.AddSingleton(setupAction);
        return services;
    }
}
