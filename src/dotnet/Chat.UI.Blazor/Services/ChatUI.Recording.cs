using ActualChat.Audio;
using ActualChat.Audio.UI.Blazor.Components;

namespace ActualChat.Chat.UI.Blazor.Services;

// TODO: extract into a separate class RecordingUI
public partial class ChatUI
{
    private readonly IMutableState<Moment?> _stopRecordingAt;
    private AudioRecorder? _audioRecorder;
    private AudioSettings? _audioSettings;

    private AudioSettings AudioSettings => _audioSettings ??= Services.GetRequiredService<AudioSettings>();
    private AudioRecorder AudioRecorder => _audioRecorder ??= Services.GetRequiredService<AudioRecorder>();

    public IState<Moment?> StopRecordingAt => _stopRecordingAt;

    [ComputeMethod] // Synced
    public virtual Task<ChatId> GetRecordingChatId()
        => Task.FromResult(ActiveChats.Value.FirstOrDefault(c => c.IsRecording).ChatId);

    public ValueTask SetRecordingChatId(ChatId chatId)
        => UpdateActiveChats(activeChats => {
            var oldChat = activeChats.FirstOrDefault(c => c.IsRecording);
            if (oldChat.ChatId == chatId)
                return activeChats;

            if (!oldChat.ChatId.IsNone)
                activeChats = activeChats.AddOrUpdate(oldChat with {
                    IsRecording = false,
                    Recency = Now,
                });
            if (!chatId.IsNone) {
                var newChat = new ActiveChat(chatId, true, true, Now);
                activeChats = activeChats.AddOrUpdate(newChat);
                TuneUI.Play("begin-recording");
            }
            else
                TuneUI.Play("end-recording");

            UICommander.RunNothing();
            return activeChats;
        });

    // Private methods

    private async Task StopRecordingWhenIdle(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var cRecordingChatId = await Computed.Capture(GetRecordingChatId).ConfigureAwait(false);
            cRecordingChatId = await cRecordingChatId.When(x => !x.IsNone, cancellationToken).ConfigureAwait(false);
            using var cts = GetRecordingChatChangedCts(cRecordingChatId, cancellationToken);
            try {
                await StopRecordingWhenIdle(cRecordingChatId.Value, cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) {
                _stopRecordingAt.Value = null;
            }
        }
    }

    private async Task StopRecordingWhenIdle(ChatId chatId, CancellationToken cancellationToken)
    {
        var recordingStartedAt = Clocks.UIClock.Now;
        _stopRecordingAt.Value = null;
        // no need to check last entry since recording has just started
        await Task.Delay(AudioSettings.IdleRecordingTimeoutBeforeCountdown, cancellationToken).ConfigureAwait(false);

        ChatEntry? prevLastEntry = null;
        while (!cancellationToken.IsCancellationRequested) {
            var lastEntry = await GetLastTranscribedEntry(chatId, prevLastEntry?.LocalId, recordingStartedAt, cancellationToken)
                .ConfigureAwait(false);
            var lastEntryAt = lastEntry != null
                ? GetEndsAt(lastEntry)
                : recordingStartedAt;
            lastEntryAt = Moment.Max(lastEntryAt, recordingStartedAt);
            var stopRecordingAt = lastEntryAt + AudioSettings.IdleRecordingTimeout;
            var timeBeforeStop = (stopRecordingAt - Clocks.UIClock.Now).Positive();
            var timeBeforeCountdown =
                (lastEntryAt + AudioSettings.IdleRecordingTimeoutBeforeCountdown - Clocks.UIClock.Now).Positive();
            if (timeBeforeStop == TimeSpan.Zero) {
                // stop recording and countdown
                _stopRecordingAt.Value = null;
                _ = UpdateRecorderState(true, default, cancellationToken).ConfigureAwait(false);
            }
            else if (timeBeforeCountdown == TimeSpan.Zero) {
                // continue counting down
                _stopRecordingAt.Value = stopRecordingAt;
                await Task.Delay(TimeSpanExt.Min(timeBeforeStop, AudioSettings.IdleRecordingCheckInterval), cancellationToken)
                    .ConfigureAwait(false);
            }
            else {
                // reset countdown since there were new messages
                _stopRecordingAt.Value = null;
                await Task.Delay(timeBeforeCountdown, cancellationToken).ConfigureAwait(false);
            }
            prevLastEntry = lastEntry;
        }
    }

    private async Task<ChatEntry?> GetLastTranscribedEntry(
        ChatId chatId,
        long? startFrom,
        Moment minEndsAt,
        CancellationToken cancellationToken)
    {
        var idRange = await Chats
            .GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken)
            .ConfigureAwait(false);
        if (startFrom != null)
            idRange = (startFrom.Value, idRange.End);
        var reader = Chats.NewEntryReader(Session, chatId, ChatEntryKind.Text);
        return await reader.GetLastWhile(idRange,
            x => x.HasAudioEntry || x.IsStreaming,
            x => GetEndsAt(x.ChatEntry) >= minEndsAt && x.SkippedCount < 100,
            cancellationToken);
    }

    private CancellationTokenSource GetRecordingChatChangedCts(Computed<ChatId> cRecordingChatId, CancellationToken cancellationToken)
    {
        var cts = cancellationToken.CreateLinkedTokenSource();
        var chatId = cRecordingChatId.Value;
        BackgroundTask.Run(async () => {
            await cRecordingChatId.When(x => x != chatId, cancellationToken);
            cts.Cancel();
        }, cancellationToken);
        return cts;
    }

    private async Task SyncRecordingState(CancellationToken cancellationToken)
    {
        var cRecordingChatId = await Computed
            .Capture(() => SyncRecordingStateImpl(cancellationToken))
            .ConfigureAwait(false);
        // Let's update it continuously -- solely for the side effects of GetRecordingChatId runs
        await cRecordingChatId.When(_ => false, FixedDelayer.ZeroUnsafe, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<Symbol> SyncRecordingStateImpl(CancellationToken cancellationToken)
    {
        // This compute method creates dependencies & gets recomputed on changes by SyncRecordingState.
        // The result it returns doesn't have any value - it runs solely for its own side effects.

        var recordingChatId = await GetRecordingChatId().ConfigureAwait(false);
        var recordingChatIdChanged = recordingChatId != _lastRecordingChatId;
        _lastRecordingChatId = recordingChatId;

        var recorderState = await AudioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        var recorderChatId = recorderState?.ChatId ?? default;
        var recorderChatIdChanged = recorderChatId != _lastRecorderChatId;
        _lastRecorderChatId = recorderChatId;

        if (recordingChatId == recorderChatId) {
            // The state is in sync
            if (recordingChatId.IsNone)
                return default;

            if (await IsRecordingLanguageChanged().ConfigureAwait(false))
                SyncRecorderState(); // We need to toggle the recording in this case
        } else if (recordingChatIdChanged) {
            // The recording was activated or deactivated
            SyncRecorderState();
            if (recordingChatId.IsNone)
                return default;

            // Update _lastRecordingLanguage
            await IsRecordingLanguageChanged().ConfigureAwait(false);
            // Start recording = start realtime playback
            await SetListeningState(recordingChatId, true).ConfigureAwait(false);
        } else if (recorderChatIdChanged) {
            // Something stopped (or started?) the recorder
            await SetRecordingChatId(recorderChatId).ConfigureAwait(false);
        }
        return default;

        async ValueTask<bool> IsRecordingLanguageChanged()
        {
            if (recorderChatId.IsNone)
                return false;

            var language = await LanguageUI.GetChatLanguage(recorderChatId, cancellationToken).ConfigureAwait(false);
            var isLanguageChanged = _lastRecordingLanguage.HasValue && language != _lastRecordingLanguage;
            _lastRecordingLanguage = language;
            return isLanguageChanged;
        }

        void SyncRecorderState()
            => UpdateRecorderState(recorderState != null && recorderChatId != recordingChatId, recordingChatId, cancellationToken);
    }

    private Task UpdateRecorderState(
        bool mustStop,
        ChatId chatIdToStartRecording,
        CancellationToken cancellationToken)
        => BackgroundTask.Run(async () => {
                if (mustStop) {
                    // Recording is running - let's top it first;
                    await AudioRecorder.StopRecording(cancellationToken).ConfigureAwait(false);
                }
                if (!chatIdToStartRecording.IsNone) {
                    // And start the recording if we must
                    if (!InteractiveUI.IsInteractive.Value)
                        await InteractiveUI.Demand("audio recording").ConfigureAwait(false);
                    await AudioRecorder.StartRecording(chatIdToStartRecording, cancellationToken).ConfigureAwait(false);
                }
            },
            Log,
            "Failed to apply new recording state.",
            CancellationToken.None);

    private static Moment GetEndsAt(ChatEntry lastEntry)
        => lastEntry.EndsAt ?? lastEntry.ContentEndsAt ?? lastEntry.BeginsAt;
}
