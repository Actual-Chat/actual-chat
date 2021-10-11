using ActualChat.Audio;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Playback;
using ActualChat.UI.Blazor.Components;
using Cysharp.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Blazor;
using Stl.Fusion.UI;
using Stl.Mathematics;

namespace ActualChat.Chat.UI.Blazor.Pages;

public partial class ChatPage : ComputedStateComponent<ChatPageModel>
{
    private readonly CancellationTokenSource _listenForNewChatEntriesCts = new();
    private AsyncServiceScope? _scope;
    private IMediaPlayer _mediaPlayer = null!;

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
        _listenForNewChatEntriesCts.Cancel();
        if (_scope != null)
            await ((IAsyncDisposable)_scope).DisposeAsync();

        await base.DisposeAsync();
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return Task.CompletedTask;

        _mediaPlayer = ScopedServices.GetRequiredService<IMediaPlayer>();
        _ = ListenForNewChatEntries(_listenForNewChatEntriesCts.Token);

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
        var range = await Chats.GetMinMaxId(Session, chatId.Value, cancellationToken);
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

        var chatEntries = entryLists.SelectMany(entries => entries);
        var result = VirtualListResponse.New(
            chatEntries.Where(ce =>ce.ContentType == ChatContentType.Text),
            entry => entry.Id.ToString(),
            startId == range.Start,
            endId == range.End);
        return result;
    }

    private async Task ListenForNewChatEntries(CancellationToken cancellationToken)
    {
        try {
            var minMax = await Chats.GetMinMaxId(Session, ChatId, cancellationToken).ConfigureAwait(false);
            var startReadingFrom = minMax.End & ~0b11111;

            while (true) {
                cancellationToken.ThrowIfCancellationRequested();
                var readRange = new Range<long>(startReadingFrom, startReadingFrom + 64);
                var entries = await Chats.GetEntries(Session, ChatId, readRange, cancellationToken).ConfigureAwait(false);
                foreach (var entry in entries.Where(ce => ce.IsStreaming && ce.ContentType == ChatContentType.Audio)) {
                    if (startReadingFrom < entry.Id)
                        startReadingFrom = entry.Id;
                    _ = EnqueueAudioChatEntryForListening(entry);
                }

                startReadingFrom &= ~0b11111;

                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            Console.WriteLine(e);
            throw;
        }

        // ReSharper disable once FunctionNeverReturns
    }

    private async Task EnqueueAudioChatEntryForListening(ChatEntry entry)
    {
        try {
            var audioSource = await AudioStreamer.GetAudioSource(entry.StreamId, _listenForNewChatEntriesCts.Token);
            var trackId = ZString.Concat("audio:", entry.ChatId, entry.Id);
            var mediaTrack = new MediaTrack(trackId, audioSource, entry.BeginsAt);
            await _mediaPlayer.EnqueueTrack(mediaTrack).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not TaskCanceledException) {
            Log.LogError(
                e,
                "Error reading media stream. Chat: {ChatId}, Entry: {ChatEntryId}, StreamId: {StreamId}",
                entry.ChatId, entry.Id, entry.StreamId);
        }
    }

    private async Task PlayMediaTrack(ChatEntry entry)
    {
        try {
            var audioSource = await AudioStreamer.GetAudioSource(entry.StreamId, _listenForNewChatEntriesCts.Token);
            var trackId = ZString.Concat("audio:", entry.ChatId, entry.Id);
            var mediaTrack = new MediaTrack(trackId, audioSource, entry.BeginsAt);
            await _mediaPlayer.Play(mediaTrack, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not TaskCanceledException) {
            Log.LogError(
                e,
                "Error reading media stream. Chat: {ChatId}, Entry: {ChatEntryId}, StreamId: {StreamId}",
                entry.ChatId, entry.Id, entry.StreamId);
        }
    }
}
