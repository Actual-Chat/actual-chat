using ActualChat.Audio;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Mathematics;
using ActualChat.Playback;
using ActualChat.UI.Blazor.Components;
using Cysharp.Text;
using Microsoft.AspNetCore.Components;
using Stl.Fusion.Blazor;
using Stl.Fusion.UI;
using Stl.Fusion;
using Stl.Mathematics;

namespace ActualChat.Chat.UI.Blazor.Pages;

public partial class ChatPage : ComputedStateComponent<ChatPageModel>
{
    private readonly CancellationTokenSource _watchRealtimeMediaCts = new();

    [Inject]
    protected Session Session { get; set; } = default!;
    [Inject]
    protected UICommandRunner Cmd { get; set; } = default!;
    [Inject]
    protected ChatPageService Service { get; set; } = default!;
    [Inject]
    protected IChatService Chats { get; set; } = default!;
    [Inject]
    protected IAudioSourceStreamer AudioStreamer { get; set; } = default!;
    [Inject]
    protected IMediaPlayerService MediaPlayerService { get; set; } = default!;
    [Inject]
    protected MediaPlayer RealtimePlayer { get; set; } = default!;
    [Inject]
    protected MediaPlayer HistoricalPlayer { get; set; } = default!;
    [Inject]
    protected ILogger<ChatPage> Log { get; set; } = default!;

    public override async ValueTask DisposeAsync()
    {
        _watchRealtimeMediaCts.Cancel();
        RealtimePlayer?.Dispose();
        HistoricalPlayer?.Dispose();
        await base.DisposeAsync();
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return Task.CompletedTask;

        _ = WatchRealtimeMedia(_watchRealtimeMediaCts.Token);
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

    private async Task WatchRealtimeMedia(CancellationToken cancellationToken)
    {
        try {
            var lastChatEntry = 0L;
            var computedMinMax = await Computed.Capture(ct
                => Chats.GetMinMaxId(Session, ChatId, ct), cancellationToken).ConfigureAwait(false);
            while (true) {
                cancellationToken.ThrowIfCancellationRequested();

                if (computedMinMax.IsConsistent()) {
                    var minMax = computedMinMax.Value;
                    var maxEntry = minMax.End;
                    if (lastChatEntry == 0)
                        lastChatEntry = Math.Max(0, maxEntry - 128);

                    var maxEntryTiles = LogCover.Default.Long.GetCoveringTiles(maxEntry).ToList();
                    var lastChatEntryTiles = LogCover.Default.Long.GetCoveringTiles(lastChatEntry);
                    var readRange = maxEntryTiles.Intersect(lastChatEntryTiles).FirstOrDefault(maxEntryTiles.Skip(1).First());

                    var chatEntries = await Chats.GetEntries(Session, ChatId, readRange, cancellationToken).ConfigureAwait(false);
                    foreach (var entry in chatEntries.Where(ce => ce.IsStreaming && ce.ContentType == ChatContentType.Audio)) {
                        if (lastChatEntry >= entry.Id) continue;

                        lastChatEntry = entry.Id;
                        _ = AddRealtimeMediaTrack(entry);
                    }
                }
                await computedMinMax.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                computedMinMax = await computedMinMax.Update(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            Log.LogError("Error watching for new chat entries for audio", e);
            throw;
        }

        // ReSharper disable once FunctionNeverReturns
    }

    private async Task AddRealtimeMediaTrack(ChatEntry entry)
    {
        if (!RealtimePlayer.IsPlaying)
            return;

        try {
            var audioSource = await AudioStreamer.GetAudioSource(entry.StreamId, _watchRealtimeMediaCts.Token);
            var trackId = ZString.Concat("audio:", entry.ChatId, entry.Id);
            var mediaTrack = new MediaTrack(trackId, audioSource, entry.BeginsAt);
            await RealtimePlayer.AddMediaTrack(mediaTrack).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not TaskCanceledException) {
            Log.LogError(
                e,
                "Error reading media stream. Chat: {ChatId}, Entry: {ChatEntryId}, StreamId: {StreamId}",
                entry.ChatId, entry.Id, entry.StreamId);
        }
    }

    private async Task PlayHistoricalMediaTrack(ChatEntry entry)
    {
        try {
            var audioSource = await AudioStreamer.GetAudioSource(entry.StreamId, _watchRealtimeMediaCts.Token);
            var trackId = ZString.Concat("audio:", entry.ChatId, entry.Id);
            var mediaTrack = new MediaTrack(trackId, audioSource, entry.BeginsAt);
            await HistoricalPlayer.Stop();
            await HistoricalPlayer.AddMediaTrack(mediaTrack);
            _ = HistoricalPlayer.Play();
        }
        catch (Exception e) when (e is not TaskCanceledException) {
            Log.LogError(
                e,
                "Error reading media stream. Chat: {ChatId}, Entry: {ChatEntryId}, StreamId: {StreamId}",
                entry.ChatId, entry.Id, entry.StreamId);
        }
    }
}
