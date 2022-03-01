using ActualChat.Audio;
using ActualChat.MediaPlayback;

namespace ActualChat.Chat.UI.Blazor.Services;

public interface IChatPlayerFactory
{
    ChatPlayer Create(Symbol chatId, Symbol userId);
}

public class ChatPlayerFactory : IChatPlayerFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ChatPlayerFactory(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public ChatPlayer Create(Symbol chatId, Symbol userId) => new(
        chatId,
        _serviceProvider.GetRequiredService<IStateFactory>(),
        _serviceProvider.GetRequiredService<Playback>(),
        _serviceProvider.GetRequiredService<AudioDownloader>(),
        _serviceProvider.GetRequiredService<ILogger<ChatPlayer>>(),
        _serviceProvider.GetRequiredService<IChatMediaResolver>(),
        _serviceProvider.GetRequiredService<IAudioStreamer>(),
        _serviceProvider.GetRequiredService<IChatAuthors>(),
        _serviceProvider.GetRequiredService<MomentClockSet>(),
        _serviceProvider.GetRequiredService<Session>(),
        _serviceProvider.GetRequiredService<IChats>()
    );
}