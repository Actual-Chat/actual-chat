using System.Globalization;
using ActualChat.Audio;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Playback;
using ActualChat.UI.Blazor.Components;
using Cysharp.Text;
using Microsoft.AspNetCore.Components;
using Stl.Fusion.Blazor;
using Stl.Fusion.UI;
using Stl.Mathematics;

namespace ActualChat.Chat.UI.Blazor.Pages;

public partial class ChatPage : ComputedStateComponent<ChatPageModel>
{
    private readonly CancellationTokenSource _watchRealtimeMediaCts = new ();

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
        await base.DisposeAsync().ConfigureAwait(true);
        GC.SuppressFinalize(this);
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

    private async Task<VirtualListData<ChatEntry>> GetMessages(
        VirtualListDataQuery query,
        CancellationToken cancellationToken)
    {
        var model = await Service.GetChatPageModel(
            Session,
            ChatId.NullIfEmpty() ?? ChatConstants.DefaultChatId,
            cancellationToken);
        var chatId = model.Chat?.Id ?? default;
        if (chatId.IsNone)
            return VirtualListData.New(
                Enumerable.Empty<ChatEntry>(),
                entry => entry.Id.ToString(CultureInfo.InvariantCulture),
                true, true);

        var idLogCover = ChatConstants.IdLogCover;
        var range = await Chats.GetMinMaxId(Session, chatId.Value, cancellationToken);
        if (query.InclusiveRange == default)
            query = query with {
                InclusiveRange = new Range<string>(
                    (range.End - idLogCover.MinTileSize).ToString(CultureInfo.InvariantCulture),
                    range.End.ToString(CultureInfo.InvariantCulture))
            };

        var startId = long.Parse(query.InclusiveRange.Start, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (query.ExpandStartBy > 0)
            startId -= (long)query.ExpandStartBy;
        startId = MathExt.Max(range.Start, startId);

        var endId = long.Parse(query.InclusiveRange.End, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (query.ExpandEndBy > 0)
            endId += (long)query.ExpandEndBy;
        endId = MathExt.Min(range.End, endId);

        var ranges = idLogCover.GetTileCover((startId, endId + 1));
        var entryLists = await Task
            .WhenAll(ranges.Select(r => Chats.GetEntries(Session, chatId.Value, r, cancellationToken)))
            .ConfigureAwait(false);

        var chatEntries = entryLists.SelectMany(entries => entries);
        var result = VirtualListData.New(
            chatEntries.Where(ce => ce.ContentType == ChatContentType.Text),
            entry => entry.Id.ToString(CultureInfo.InvariantCulture),
            startId == range.Start,
            endId == range.End);
        return result;
    }

    private async Task WatchRealtimeMedia(CancellationToken cancellationToken)
    {
        var chatId = ChatId.NullIfEmpty() ?? ChatConstants.DefaultChatId;
        var idLogCover = ChatConstants.IdLogCover;
        try {
            var lastChatEntry = 0L;
            var computedMinMax = await Computed.Capture(ct => Chats.GetMinMaxId(Session, ChatId, ct), cancellationToken)
                .ConfigureAwait(false);
            while (true) {
                cancellationToken.ThrowIfCancellationRequested();

                if (computedMinMax.IsConsistent()) {
                    var minMax = computedMinMax.Value;
                    var maxEntry = minMax.End;
                    if (lastChatEntry == 0)
                        lastChatEntry = Math.Max(0, maxEntry - 128);

                    var ranges = idLogCover.GetTileCover((lastChatEntry, maxEntry + 1));
                    var entryLists = await Task.WhenAll(
                            ranges.Select(r => Chats.GetEntries(Session, chatId, r, cancellationToken)))
                        .ConfigureAwait(false);
                    var chatEntries = entryLists.SelectMany(entries => entries);
                    foreach (var entry in chatEntries.Where(
                        ce => ce.IsStreaming && ce.ContentType == ChatContentType.Audio)) {
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
            Log.LogError(e, "Error watching for new chat entries for audio");
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
                entry.ChatId,
                entry.Id,
                entry.StreamId);
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
                entry.ChatId,
                entry.Id,
                entry.StreamId);
        }
    }
}
