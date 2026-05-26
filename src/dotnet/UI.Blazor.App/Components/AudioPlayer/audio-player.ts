import {
    audioContextSource,
    AppAudioContext,
    resetMediaSessionDebounced,
    AudioContextRef,
    AudioContextAction,
} from '../../Services/audio-context-source';
import { AudioContextTrait, AttachedAudioContextTrait, DestinationFallbackTrait, DemandInteractiveUI } from '../../Services/audio-context-traits';
import { FeederState, PlaybackState } from './worklets/feeder-audio-worklet-contract';
import { Disposable } from 'disposable';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { OpusDecoderWorker } from './workers/opus-decoder-worker-contract';
import { catchErrors, PromiseSource } from 'actuallab-core';
import { rpcClient, rpcNoWait, rpcSendNoWait } from 'rpc';
import { Versioning } from 'versioning';
import { AC, whenAppConstantsReady } from 'app-constants';
import { Log, getLogs } from 'logging';
import { ObjectPool } from 'object-pool';
import { Resettable } from 'resettable';
import { AudioInitializer } from '../../Services/audio-initializer';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';
import { ServerClock } from 'clocks';
import { SharedSettings } from 'shared-settings';
import { SharedSettingsWorkerSync } from 'shared-settings-worker';

const { logScope, debugLog, infoLog, warnLog, errorLog } = getLogs('AudioPlayer');

const EnableFrequentDebugLog = false;

let decoderWorkerInstance: Worker | undefined;
let decoderWorker: OpusDecoderWorker & Disposable | undefined;
let decoderWorkerSharedSettingsRegistration: Disposable | undefined;

interface AudioContextOutputLatency {
    readonly outputLatency?: number;
}

/** Trait that manages the FeederAudioWorkletNode lifecycle for audio playback */
class FeederNodeTrait implements AudioContextTrait {
    public readonly name: string;
    private readonly player: AudioPlayer;
    private readonly internalId: string;

    constructor(player: AudioPlayer, internalId: string) {
        this.name = `feeder-node-${internalId}`;
        this.player = player;
        this.internalId = internalId;
    }

    public async attach(context: AppAudioContext): Promise<AttachedFeederNode> {
        debugLog?.log(`#${this.internalId}.feederNodeTrait.attach: context:`, Log.ref(context));

        await AudioPlayer.ensureInitialized();

        // Create decoder to feeder channel
        const decoderToFeederWorkletChannel = new MessageChannel();
        const feederNodeOptions: AudioWorkletNodeOptions = {
            channelCount: 1,
            channelCountMode: 'explicit',
            numberOfInputs: 0,
            numberOfOutputs: 1,
            outputChannelCount: [1],
        };

        const feederNode = await FeederAudioWorkletNode.create(
            this.internalId,
            decoderToFeederWorkletChannel.port2,
            context,
            'feederWorklet',
            feederNodeOptions,
        );

        // Initialize decoder worker
        await decoderWorker!.init(this.internalId, decoderToFeederWorkletChannel.port1);

        // Connect to destination
        const destination = DestinationFallbackTrait.getDestination(context);
        feederNode.connect(destination);

        return new AttachedFeederNode(
            this.player,
            this.internalId,
            feederNode,
            decoderToFeederWorkletChannel,
        );
    }
}

/** Attached feeder node that manages the worklet and decoder lifecycle */
class AttachedFeederNode implements AttachedAudioContextTrait {
    private readonly player: AudioPlayer;
    private readonly internalId: string;
    public readonly feederNode: FeederAudioWorkletNode;
    private readonly channel: MessageChannel;

    constructor(
        player: AudioPlayer,
        internalId: string,
        feederNode: FeederAudioWorkletNode,
        channel: MessageChannel,
    ) {
        this.player = player;
        this.internalId = internalId;
        this.feederNode = feederNode;
        this.channel = channel;

        // Wire up state change handler
        this.feederNode.onStateChanged = (state) => void this.player.onFeederStateChanged(state);
    }

    public async onClosed(): Promise<void> {
        debugLog?.log(`#${this.internalId}.attachedFeederNode.onClosed`);

        // Close decoder worker
        await catchErrors(
            () => decoderWorker!.close(this.internalId),
            e => warnLog?.log(`#${this.internalId}.onClosed decoderWorker.close error:`, e));

        // Close channel ports
        await catchErrors(
            () => this.channel.port1.close(),
            e => warnLog?.log(`#${this.internalId}.onClosed port1.close error:`, e));
        await catchErrors(
            () => this.channel.port2.close(),
            e => warnLog?.log(`#${this.internalId}.onClosed port2.close error:`, e));

        // Disconnect feeder node
        this.feederNode.onStateChanged = undefined;
        await catchErrors(
            () => this.feederNode.disconnect(),
            e => warnLog?.log(`#${this.internalId}.onClosed feederNode.disconnect error:`, e));
        this.feederNode.dispose();
    }
}

export class AudioPlayer implements Resettable {
    private static readonly pool: ObjectPool<AudioPlayer> = new ObjectPool<AudioPlayer>(() => new AudioPlayer());
    private static whenInitialized = new PromiseSource<void>();
    private static nextInternalId = 0;
    private static initStarted = false;

    private readonly internalId: string;
    private readonly feederNodeTrait: FeederNodeTrait;

    private blazorRef?: DotNet.DotNetObject;
    private contextRef?: AudioContextRef;
    private playingAction?: AudioContextAction;
    private whenEnded?: PromiseSource<void>;

    private playbackState: PlaybackState = 'paused';
    private targetBufferSizeMs = 0;
    private authorId: string | null = null;
    private recordedAtMs = 0;
    private lastLatencyLogTime = 0;
    private lastLagReportTime = 0;
    private warnedFeederMissingInFrame = false;
    private static readonly LagReportIntervalMs = 500;
    // Dedup state: feeder onStateChanged fires at audio-block rate (≈50 Hz)
    // so without a guard reportPlaying calls Blazor invokeMethodAsync 50×/s
    // even when nothing material changed. Compare against the last attempted
    // tuple; attempts are tracked before awaiting interop so a failing .NET
    // callback cannot turn every audio block into another warning.
    private lastReportAttemptedPlayingAt: number | null = null;
    private lastReportAttemptedIsPaused: boolean | null = null;
    private lastReportAttemptedIsBufferLow: boolean | null = null;
    private lastReportAttemptedAtMs = 0;
    private lastReportFailedAtMs = 0;
    private static readonly ReportPlayingMaxIntervalMs = 1000;

    public static get isInitialized() {
        return AudioPlayer.whenInitialized.isCompleted;
    }

    public onPlaybackStateChanged?: (playbackState: PlaybackState) => void;

    public static async init(): Promise<void> {
        this.initStarted = true;
        if (this.whenInitialized.isCompleted)
            return;

        if (!decoderWorkerInstance) {
            const decoderWorkerPath = Versioning.mapPath('/dist/opusDecoderWorker.js');
            decoderWorkerInstance = new Worker(decoderWorkerPath, { type: 'module' });
        }
        decoderWorker ??= rpcClient<OpusDecoderWorker>(`${logScope}.decoderWorker`, decoderWorkerInstance);

        await whenAppConstantsReady;
        await decoderWorker.create(AC, Versioning.assetMap, SharedSettings.all, { type: 'rpc-timeout', timeoutMs: 20_000 });
        decoderWorkerSharedSettingsRegistration ??= SharedSettingsWorkerSync.register(decoderWorker);

        this.whenInitialized.resolve(undefined);
    }

    public static async ensureInitialized(): Promise<void> {
        if (AudioPlayer.initStarted)
            await AudioPlayer.whenInitialized;
        else
            await AudioPlayer.init();
    }

    public static terminate(): void {
        decoderWorkerSharedSettingsRegistration?.dispose();
        decoderWorkerSharedSettingsRegistration = undefined;
        decoderWorkerInstance?.terminate();
        AudioPlayer.whenInitialized = new PromiseSource<void>();
        AudioInitializer.isPlayerInitialized = false;
        this.initStarted = false;
    }

    /** Called from Blazor */
    public static async create(
        blazorRef: DotNet.DotNetObject,
        id: string,
        preSkip: number,
        title: string,
        album: string,
        authorId: string | null,
        recordedAtMs: number,
        targetBufferSizeMs: number,
    ): Promise<AudioPlayer> {
        await AudioPlayer.init();
        const player = AudioPlayer.pool.get();
        await player.startPlayback(blazorRef, id, preSkip, title, album, authorId, recordedAtMs, targetBufferSizeMs);
        return player;
    }

    public constructor() {
        this.internalId = String(AudioPlayer.nextInternalId++);
        debugLog?.log(`#${this.internalId}.constructor`);
        this.feederNodeTrait = new FeederNodeTrait(this, this.internalId);
    }

    public async startPlayback(
        blazorRef: DotNet.DotNetObject,
        id: string,
        preSkip: number,
        title: string,
        album: string,
        authorId: string | null,
        recordedAtMs: number,
        targetBufferSizeMs: number): Promise<void> {

        debugLog?.log(
            `#${this.internalId} -> startPlayback(): authorId=${authorId}, ` +
            `recordedAtMs=${recordedAtMs.toFixed(0)}, targetBufferSizeMs=${targetBufferSizeMs}`);
        this.blazorRef = blazorRef;
        this.authorId = authorId;
        this.recordedAtMs = recordedAtMs;
        this.targetBufferSizeMs = targetBufferSizeMs;
        this.playbackState = 'paused';
        this.lastReportAttemptedPlayingAt = null;
        this.lastReportAttemptedIsPaused = null;
        this.lastReportAttemptedIsBufferLow = null;
        this.lastReportAttemptedAtMs = 0;
        this.lastReportFailedAtMs = 0;
        this.warnedFeederMissingInFrame = false;
        this.whenEnded = new PromiseSource<void>();

        // Create a ref with the feeder node trait
        this.contextRef = audioContextSource.createRef(this.feederNodeTrait, DemandInteractiveUI.instance);

        // Run the playback action
        const whenPlaybackStarted = new PromiseSource<void>();
        this.playingAction = this.contextRef.run(async (context) => {
            try {
                const attachedFeeder = this.contextRef!.getTrait<AttachedFeederNode>(this.feederNodeTrait);
                if (!attachedFeeder) {
                    errorLog?.log(`#${this.internalId} startPlayback: getTrait returned NULL for feeder, context.state=${context.state}`);
                    throw new Error('Feeder node not attached');
                }

                const outputLatency = (context as AudioContextOutputLatency).outputLatency;
                infoLog?.log(
                    `#${this.internalId}.audioContextLatency: ` +
                    `base=${(context.baseLatency * 1000).toFixed(0)}ms, ` +
                    `output=${outputLatency === undefined ? 'n/a' : `${(outputLatency * 1000).toFixed(0)}ms`}, ` +
                    `sampleRate=${context.sampleRate}, state=${context.state}`);
                await decoderWorker!.setTargetBufferSize(this.internalId, this.targetBufferSizeMs, rpcNoWait);
                await decoderWorker!.resume(this.internalId, this.recordedAtMs);
                await attachedFeeder.feederNode.resume(preSkip);
                whenPlaybackStarted.resolve(undefined);
            }
            catch (e) {
                whenPlaybackStarted.reject(e);
                throw e;
            }
        });

        // Wait until the decoder and feeder have actually resumed before C#
        // starts pushing frames; otherwise a late decoder resume can clear them.
        await Promise.race([
            whenPlaybackStarted,
            this.playingAction.whenDone.then(() => {
                if (!whenPlaybackStarted.isCompleted)
                    throw new Error('Audio playback startup action completed before feeder resume');
            }),
        ]);

        this.setMediaSession(title, album);
        debugLog?.log(`#${this.internalId} <- startPlayback()`);
    }

    public async reset(): Promise<void> {
        debugLog?.log(`#${this.internalId} reset()`);
        const attachedFeeder = this.contextRef?.getTrait<AttachedFeederNode>(this.feederNodeTrait);
        if (attachedFeeder) {
            void attachedFeeder.feederNode.pause(rpcNoWait);
        }
        this.blazorRef = undefined;
        this.authorId = null;
        this.recordedAtMs = 0;
        this.lastLatencyLogTime = 0;
        this.lastLagReportTime = 0;
        this.playbackState = 'ended';
        this.playingAction?.dispose();
        this.playingAction = undefined;
        this.contextRef?.dispose();
        this.contextRef = undefined;
        // Remove the feeder node trait so it can be re-registered and re-attached on next play
        await audioContextSource.removeTrait(this.feederNodeTrait);
        this.whenEnded?.resolve(undefined);
        resetMediaSessionDebounced();
    }

    /** Called by Blazor without awaiting the result, so a call can be in the middle of appendAudio  */
    public frame(bytes: Uint8Array, sourceOffsetMs: number): void {
        if (this.playbackState === 'ended')
            return;
        if (this.contextRef && !this.contextRef.isReady)
            return; // Skip frames when audio context isn't running (e.g. broken/suspended)

        if (!this.warnedFeederMissingInFrame
            && this.contextRef
            && !this.contextRef.getTrait<AttachedFeederNode>(this.feederNodeTrait)) {
            this.warnedFeederMissingInFrame = true;
            warnLog?.log(`#${this.internalId} frame: feeder trait is NULL but contextRef is ready — frames will be discarded by decoder worker`);
        }

        // Hot path — called ~50×/s per player via Blazor interop. Bypass the rpcClient Proxy
        // and go straight to the worker's postMessage to avoid per-call allocations.
        const buf = bytes.buffer as ArrayBuffer;
        rpcSendNoWait(
            decoderWorkerInstance!,
            'frame',
            [this.internalId, buf, bytes.byteOffset, bytes.length, sourceOffsetMs],
            [buf]);
    }

    /** Called by Blazor */
    public skipUntil(sourceOffsetMs: number): void {
        void decoderWorker!.skipUntil(this.internalId, sourceOffsetMs, rpcNoWait);
    }

    /** Called by Blazor */
    public speedUpUntil(sourceOffsetMs: number, dropEveryNFrames: number): void {
        void decoderWorker!.speedUpUntil(this.internalId, sourceOffsetMs, dropEveryNFrames, rpcNoWait);
    }

    private getAudioContextLatencyMs(): number {
        const context = this.contextRef?.context;
        if (!context)
            return 0;

        const outputLatency = (context as AudioContextOutputLatency).outputLatency ?? 0;
        return (context.baseLatency + outputLatency) * 1000;
    }

    /** Called by Blazor */
    public async end(mustAbort: boolean): Promise<void> {
        if (this.playbackState === 'ended')
            return;

        infoLog?.log(`#${this.internalId}.end, mustAbort:`, mustAbort);

        // Wait for context to be ready
        if (this.contextRef && !this.contextRef.isReady) {
            await this.contextRef.whenReady();
        }

        // This ensures 'end' hit the feeder processor which in turn sends feeder status back and resolves this.whenEnded
        await decoderWorker!.end(this.internalId, mustAbort);
        await this.whenEnded;
        this.playingAction?.dispose();
        this.playingAction = undefined;
        this.contextRef?.dispose();
        this.contextRef = undefined;
        resetMediaSessionDebounced();
    }

    /** Called by Blazor */
    public async pause(): Promise<void> {
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.internalId}.pause`);

        // Wait for context to be ready
        if (this.contextRef && !this.contextRef.isReady) {
            await this.contextRef.whenReady();
        }

        const attachedFeeder = this.contextRef?.getTrait<AttachedFeederNode>(this.feederNodeTrait);
        if (attachedFeeder) {
            await attachedFeeder.feederNode.pause(rpcNoWait);
        }
        this.playingAction?.dispose();
        this.playingAction = undefined;
        this.contextRef?.dispose();
        this.contextRef = undefined;
        this.playbackState = 'paused';

        this.setMediaSessionState('paused');
    }

    /** Called by Blazor */
    public resume(): void {
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.internalId}.resume`);

        // Create new ref and action for resumed playback
        this.contextRef = audioContextSource.createRef(this.feederNodeTrait, DemandInteractiveUI.instance);
        this.playingAction = this.contextRef.run(async () => {
            const attachedFeeder = this.contextRef!.getTrait<AttachedFeederNode>(this.feederNodeTrait);
            if (!attachedFeeder)
                throw new Error('Feeder node not attached');

            await attachedFeeder.feederNode.resume(0);
        });
        this.playbackState = 'paused';

        this.setMediaSessionState('playing');
    }

    // Event handler for feeder state changes (called by AttachedFeederNode)
    public onFeederStateChanged = async (state: FeederState) => {
        if (this.playbackState === 'ended')
            return;

        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (EnableFrequentDebugLog)
            debugLog?.log(
                `#${this.internalId}.onFeederStateChanged: ${state.playbackState} @ ${state.playingAt}, ` +
                `buffer: ${state.bufferState} (${state.bufferedDuration}s)`);

        this.playbackState = state.playbackState;
        if (this.playbackState === 'ended') {
            try {
                await this.reportEnded();
            }
            finally {
                this.authorId = null;
                this.recordedAtMs = 0;
                this.lastLatencyLogTime = 0;
                this.playingAction?.dispose();
                this.playingAction = undefined;
                this.contextRef?.dispose();
                this.contextRef = undefined;
                this.whenEnded?.resolve(undefined);
                AudioPlayer.pool.release(this);
            }
        }
        else {
            if (this.authorId && state.playbackState === 'playing') {
                // The decoder reports presentation lag from the source offset
                // of the decoded frame it handed to the feeder, using the
                // feeder's current target buffer delay.
                const sysNow = Date.now();
                if (
                    this.blazorRef
                    && state.presentationLagMs !== null
                    && sysNow - this.lastLagReportTime >= AudioPlayer.LagReportIntervalMs
                ) {
                    this.lastLagReportTime = sysNow;
                    void this.blazorRef.invokeMethodAsync(
                        'OnPresentationLag',
                        state.presentationLagMs + this.getAudioContextLatencyMs())
                        .catch(() => { /* ignore */ });
                }
                const serverNow = ServerClock.now();
                if (serverNow - this.lastLatencyLogTime > 10_000) {
                    this.lastLatencyLogTime = serverNow;
                    const serverRecordedAtMs = this.recordedAtMs + state.playingAt * 1000;
                    const serverLatencyMs = serverNow - serverRecordedAtMs;
                    infoLog?.log(
                        `onFeederStateChanged: authorId=${this.authorId}, ` +
                        `now=${serverNow.toFixed(0)}, recorded=${serverRecordedAtMs.toFixed(0)} ` +
                        `(recordedAt=${this.recordedAtMs.toFixed(0)}+playingAt=${(state.playingAt * 1000).toFixed(0)}), ` +
                        `latency=${serverLatencyMs.toFixed(0)}ms`);
                }
            }
            const isPaused = state.playbackState === 'paused';
            const isBufferLow = state.bufferState !== 'ok';
            void this.reportPlaying(state.playingAt, isPaused, isBufferLow);
        }
    }

    // Private methods

    private setMediaSessionState(playbackState: MediaSessionPlaybackState): void {
        try {
            if ('mediaSession' in navigator)
                navigator.mediaSession.playbackState = playbackState;
        }
        catch (e) {
            warnLog?.log(`#${this.internalId} pause(): error settings playback state:`, e);
        }
    }

    private setMediaSession(title: string, album: string): void {
        resetMediaSessionDebounced.reset();
        try {
            if ('mediaSession' in navigator) {
                navigator.mediaSession.metadata = new MediaMetadata({
                    title: `${title} @ ${album}`,
                    album: album,
                    artist: 'Voxt',
                    artwork: [{ src: '/_applogo-dark_voxt.svg' }],
                });
                navigator.mediaSession.playbackState = 'playing';
                navigator.mediaSession.setPositionState({
                    playbackRate: 1,
                    position: 0,
                    duration: 0,
                });
            }
        } catch (e) {
            warnLog?.log(`#${this.internalId}.startPlayback: error setting metadata:`, e);
        }
    }

    // Backend invocation methods

    private reportPlaying = async (playingAt: number, isPaused: boolean, isBufferLow: boolean) => {
        // Skip if nothing material changed since the last attempted dispatch,
        // unless ReportPlayingMaxIntervalMs has elapsed (heartbeat so the server
        // knows we're still alive). playingAt advances continuously; quantize to
        // 100 ms so micro-jitter doesn't defeat the dedup.
        const playingAtBucket = Math.round(playingAt * 10);
        const lastBucket = this.lastReportAttemptedPlayingAt === null
            ? null
            : Math.round(this.lastReportAttemptedPlayingAt * 10);
        const nowMs = performance.now();
        const heartbeatDue = nowMs - this.lastReportAttemptedAtMs >= AudioPlayer.ReportPlayingMaxIntervalMs;
        const statusChanged = isPaused !== this.lastReportAttemptedIsPaused
            || isBufferLow !== this.lastReportAttemptedIsBufferLow;
        const positionChanged = playingAtBucket !== lastBucket;
        const retryBackoffActive = this.lastReportFailedAtMs > 0
            && nowMs - this.lastReportFailedAtMs < AudioPlayer.ReportPlayingMaxIntervalMs;
        if (!statusChanged && (!positionChanged || retryBackoffActive) && !heartbeatDue)
            return;

        this.lastReportAttemptedPlayingAt = playingAt;
        this.lastReportAttemptedIsPaused = isPaused;
        this.lastReportAttemptedIsBufferLow = isBufferLow;
        this.lastReportAttemptedAtMs = nowMs;
        try {
            const stateText = isPaused ? 'paused' : 'playing';
            const bufferText = isBufferLow ? 'low' : 'ok';

            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            if (EnableFrequentDebugLog)
                debugLog?.log(`#${this.internalId}.reportPlaying: ${stateText} @ ${playingAt}, buffer: ${bufferText}`);
            await this.blazorRef?.invokeMethodAsync('OnPlaying', playingAt, isPaused, isBufferLow);
            this.lastReportFailedAtMs = 0;
        }
        catch (e) {
            this.lastReportFailedAtMs = performance.now();
            warnLog?.log(`#${this.internalId}.reportPlaying: unhandled error:`, e);
        }
    }

    private reportEnded = async (message: string | null = null) => {
        try {
            debugLog?.log(`#${this.internalId}.reportEnded:`, message);
            await this.blazorRef?.invokeMethodAsync('OnEnded', message);
        }
        catch (e) {
            warnLog?.log(`#${this.internalId}.reportEnded: unhandled error:`, e);
        }
    }
}

if (BrowserInfo.hostKind !== 'MauiApp')
    void AudioPlayer.init();
