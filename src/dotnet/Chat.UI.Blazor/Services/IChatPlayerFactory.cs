using ActualChat.Audio;
using ActualChat.MediaPlayback;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Chat.UI.Blazor.Services;

/// <summary> Must be scoped service. </summary>
public interface IChatPlayerFactory
{
    ChatPlayer Create(Symbol chatId);
}

/// <inheritdoc cref="IChatPlayerFactory"/>
internal class ChatPlayerFactory : IChatPlayerFactory
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IPlaybackFactory _playbackFactory;
    private readonly AudioDownloader _audioDownloader;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IChatMediaResolver _mediaResolver;
    private readonly IAudioStreamer _audioStreamer;
    private readonly IChatAuthors _chatAuthors;
    private readonly MomentClockSet _clockSet;
    private readonly Session _session;
    private readonly IChats _chats;

    public ChatPlayerFactory(
        IPlaybackFactory playbackFactory,
        AudioDownloader audioDownloader,
        ILoggerFactory loggerFactory,
        IChatMediaResolver chatMediaResolver,
        IAudioStreamer audioStreamer,
        IChatAuthors chatAuthors,
        MomentClockSet clockSet,
        Session session,
        IChats chats,
        IHostApplicationLifetime lifetime)
    {
        _playbackFactory = playbackFactory;
        _audioDownloader = audioDownloader;
        _loggerFactory = loggerFactory;
        _mediaResolver = chatMediaResolver;
        _audioStreamer = audioStreamer;
        _chatAuthors = chatAuthors;
        _clockSet = clockSet;
        _session = session;
        _chats = chats;
        _lifetime = lifetime;
    }

    public ChatPlayer Create(Symbol chatId) => new(
        chatId,
        _lifetime,
        _playbackFactory,
        _audioDownloader,
        _loggerFactory.CreateLogger<ChatPlayer>(),
        _mediaResolver,
        _audioStreamer,
        _chatAuthors,
        _clockSet,
        _session,
        _chats
    );
}
