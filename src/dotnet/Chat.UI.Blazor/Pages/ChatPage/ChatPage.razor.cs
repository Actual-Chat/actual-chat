using ActualChat.Audio;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Playback;
using ActualChat.UI.Blazor.Components;
using Cysharp.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Fusion.Blazor;
using Stl.Fusion.UI;
using Stl.Mathematics;

namespace ActualChat.Chat.UI.Blazor.Pages;

public partial class ChatPage : ComputedStateComponent<ChatPageModel>
{
    private readonly CancellationTokenSource _cts = new();
    private AsyncServiceScope? _scope;
    private IMediaPlayer _mediaPlayer = null!;
    private ChannelWriter<MediaTrack> _mediaTrackWriter = null!;

    [Inject]
    protected IServiceScopeFactory ScopeFactory { get; set; } = default!;

    [Inject]
    protected ChatPageService Service { get; set; } = default!;

    [Inject]
    protected IChatService Chats { get; set; } = default!;

    [Inject]
    protected Session Session { get; set; } = default!;

    [Inject]
    protected UICommandRunner Cmd { get; set; } = default!;

    [Inject]
    protected IAudioSourceStreamer AudioStreamer { get; set; } = default!;

    [Inject]
    protected ILogger<ChatPage> Log { get; set; } = default!;

    protected IServiceProvider ScopedServices
    {
        get
        {
            if (ScopeFactory == null)
                throw new InvalidOperationException("Services cannot be accessed before the component is initialized.");

            _scope ??= ScopeFactory.CreateAsyncScope();
            return _scope.Value.ServiceProvider;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        _mediaTrackWriter.TryComplete();
        _cts.Cancel();
        if (_scope != null)
            await ((IAsyncDisposable)_scope).DisposeAsync();

        await base.DisposeAsync();
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return Task.CompletedTask;

        var channel = Channel.CreateBounded<MediaTrack>(new BoundedChannelOptions(20) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true
        });
        _mediaTrackWriter = channel.Writer;
        _mediaPlayer = ScopedServices.GetRequiredService<IMediaPlayer>();
        // temp switch
        if (ListenToStream)
            _ = _mediaPlayer.Play(channel.Reader.ReadAllAsync(_cts.Token), _cts.Token);

        return Task.CompletedTask;
    }

    protected override Task OnParametersSetAsync()
    {
        if (ChatId.IsNullOrEmpty())
            _nav.NavigateTo($"/chat/{ChatConstants.DefaultChatId}");
        return base.OnParametersSetAsync();
    }

    protected override Task<ChatPageModel> ComputeState(CancellationToken cancellationToken)
        => Service.GetChatPageModel(Session, ChatId.NullIfEmpty() ?? ChatConstants.DefaultChatId, cancellationToken);

    private async Task<VirtualListResponse<ChatEntry>> GetMessages(VirtualListQuery query, CancellationToken cancellationToken)
    {
        var model = await Service.GetChatPageModel(Session, ChatId.NullIfEmpty() ?? ChatConstants.DefaultChatId, cancellationToken);
        var chatId = model.Chat?.Id ?? default;
        if (chatId.IsNone)
            return VirtualListResponse.New(Enumerable.Empty<ChatEntry>(), entry => entry.Id.ToString(), true, true);

        var idLogCover = ChatConstants.IdLogCover;
        var range = await Chats.GetIdRange(Session, chatId.Value, cancellationToken);
        if (query.IncludedRange == default) {
            query = query with {
                IncludedRange = new Range<string>((range.End - idLogCover.MinTileSize).ToString(), range.End.ToString())
            };
        }

        var startId = long.Parse(query.IncludedRange.Start);
        if (query.ExpandStartBy > 0)
            startId -= (long) query.ExpandStartBy;
        startId = MathExt.Max(range.Start, startId);

        var endId = long.Parse(query.IncludedRange.End);
        if (query.ExpandEndBy > 0)
            endId += (long) query.ExpandEndBy;
        endId = MathExt.Min(range.End, endId);

        var ranges = idLogCover.GetTileCover((startId, endId + 1));
        var entryLists = await Task.WhenAll(
            ranges.Select(r => Chats.GetEntries(Session, chatId.Value, r, cancellationToken)));

        var chatEntries = entryLists.SelectMany(entries => entries).ToList();
        foreach (var chatEntry in chatEntries.Where(ce => ce.IsStreaming && ce.ContentType == ChatContentType.Audio)) {
            _ = EnqueueAudioChatEntryForListening(chatEntry);
        }
        var result = VirtualListResponse.New(
            chatEntries.Where(ce =>ce.ContentType == ChatContentType.Text),
            entry => entry.Id.ToString(),
            startId == range.Start,
            endId == range.End);
        return result;
    }

    private async Task EnqueueAudioChatEntryForListening(ChatEntry entry)
    {
        try {
            var audioSource = await AudioStreamer.GetAudioSource(entry.StreamId, _cts.Token);
            var trackId = ZString.Concat("audio:", entry.ChatId, entry.Id);
            var mediaTrack = new MediaTrack(trackId, audioSource, entry.BeginsAt);
            await _mediaTrackWriter.WriteAsync(mediaTrack).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not TaskCanceledException) {
            Log.LogError(
                e,
                "Error reading media stream. Chat: {ChatId}, Entry: {ChatEntryId}, StreamId: {StreamId}",
                entry.ChatId, entry.Id, entry.StreamId);
        }
    }
}
