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
import { catchErrors, PromiseSource } from 'promises';
import { rpcClient, rpcNoWait } from 'rpc';
import { Versioning } from 'versioning';
import { Log, getLogs } from 'logging';
import { ObjectPool } from 'object-pool';
import { Resettable } from 'resettable';
import { AudioInitializer } from '../../Services/audio-initializer';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';
import { AudioVideoSync } from 'audio-video-sync';
import { ServerClock } from 'server-clock';

const { logScope, debugLog, warnLog } = getLogs('AudioPlayer');

const EnableFrequentDebugLog = false;

let decoderWorkerInstance: Worker | undefined;
let decoderWorker: OpusDecoderWorker & Disposable | undefined;

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
    private authorId: string | null = null;
    private recordedAtMs = 0;
    private lastLatencyLogTime = 0;

    public static get isInitialized() {
        return AudioPlayer.whenInitialized.isCompleted();
    }

    public onPlaybackStateChanged?: (playbackState: PlaybackState) => void;

    public static async init(): Promise<void> {
        this.initStarted = true;
        if (this.whenInitialized.isCompleted())
            return;

        if (!decoderWorkerInstance) {
            const decoderWorkerPath = Versioning.mapPath('/dist/opusDecoderWorker.js');
            decoderWorkerInstance = new Worker(decoderWorkerPath, { type: 'module' });
        }
        decoderWorker ??= rpcClient<OpusDecoderWorker>(`${logScope}.decoderWorker`, decoderWorkerInstance);

        await decoderWorker.create(Versioning.assetMap, { type: 'rpc-timeout', timeoutMs: 20_000 });

        this.whenInitialized.resolve(undefined);
    }

    public static async ensureInitialized(): Promise<void> {
        if (AudioPlayer.initStarted)
            await AudioPlayer.whenInitialized;
        else
            await AudioPlayer.init();
    }

    public static terminate(): void {
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
        recordedAtMs: number
    ): Promise<AudioPlayer> {
        await AudioPlayer.init();
        const player = AudioPlayer.pool.get();
        await player.startPlayback(blazorRef, id, preSkip, title, album, authorId, recordedAtMs);
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
        recordedAtMs: number): Promise<void> {

        debugLog?.log(
            `#${this.internalId} -> startPlayback(): authorId=${authorId}, ` +
            `recordedAtMs=${recordedAtMs.toFixed(0)}`);
        this.blazorRef = blazorRef;
        this.authorId = authorId;
        this.recordedAtMs = recordedAtMs;
        this.playbackState = 'paused';
        this.whenEnded = new PromiseSource<void>();

        // Create a ref with the feeder node trait
        this.contextRef = audioContextSource.createRef(this.feederNodeTrait, DemandInteractiveUI.instance);

        // Run the playback action
        this.playingAction = this.contextRef.run(async () => {
            const attachedFeeder = this.contextRef!.getTrait<AttachedFeederNode>(this.feederNodeTrait);
            if (attachedFeeder) {
                await decoderWorker!.resume(this.internalId, rpcNoWait);
                await attachedFeeder.feederNode.resume(preSkip);
            }
        });

        // Wait for context to be ready
        await this.contextRef.whenReady();

        this.setMediaSession(title, album);
        debugLog?.log(`#${this.internalId} <- startPlayback()`);
    }

    public reset(): void {
        debugLog?.log(`#${this.internalId} reset()`);
        if (this.authorId)
            AudioVideoSync.clear(this.authorId);
        const attachedFeeder = this.contextRef?.getTrait<AttachedFeederNode>(this.feederNodeTrait);
        if (attachedFeeder) {
            void attachedFeeder.feederNode.pause(rpcNoWait);
        }
        this.blazorRef = undefined;
        this.authorId = null;
        this.recordedAtMs = 0;
        this.lastLatencyLogTime = 0;
        this.playbackState = 'ended';
        this.playingAction?.dispose();
        this.playingAction = undefined;
        this.contextRef?.dispose();
        this.contextRef = undefined;
        // Remove the feeder node trait so it can be re-registered and re-attached on next play
        audioContextSource.removeTrait(this.feederNodeTrait);
        this.whenEnded?.resolve(undefined);
        resetMediaSessionDebounced();
    }

    /** Called by Blazor without awaiting the result, so a call can be in the middle of appendAudio  */
    public frame(bytes: Uint8Array): void {
        if (this.playbackState === 'ended')
            return;
        if (this.contextRef && !this.contextRef.isReady)
            return; // Skip frames when audio context isn't running (e.g. broken/suspended)

        // @ts-expect-error TODO(AY): fix ts error
        void decoderWorker.frame(this.internalId, bytes.buffer, bytes.byteOffset, bytes.length, rpcNoWait);
    }

    /** Called by Blazor */
    public async end(mustAbort: boolean): Promise<void> {
        if (this.playbackState === 'ended')
            return;

        warnLog?.log(`#${this.internalId}.end, mustAbort:`, mustAbort);

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
            if (attachedFeeder) {
                await attachedFeeder.feederNode.resume(0);
            }
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
                if (this.authorId)
                    AudioVideoSync.clear(this.authorId);
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
            if (this.authorId) {
                AudioVideoSync.update(this.authorId, state.playingAt, this.recordedAtMs, state.playbackState);

                if (state.playbackState === 'playing') {
                    const now = ServerClock.now();
                    if (now - this.lastLatencyLogTime > 10_000) {
                        this.lastLatencyLogTime = now;
                        const recordedAtMs = this.recordedAtMs + state.playingAt * 1000;
                        const latencyMs = now - recordedAtMs;
                        warnLog?.log(
                            `LATENCY: authorId=${this.authorId}, ` +
                            `now=${now.toFixed(0)}, recorded=${recordedAtMs.toFixed(0)} ` +
                            `(recordedAt=${this.recordedAtMs.toFixed(0)}+playingAt=${(state.playingAt * 1000).toFixed(0)}), ` +
                            `latency=${latencyMs.toFixed(0)}ms`);
                    }
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
        try {
            const stateText = isPaused ? 'paused' : 'playing';
            const bufferText = isBufferLow ? 'low' : 'ok';

            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            if (EnableFrequentDebugLog)
                debugLog?.log(`#${this.internalId}.reportPlaying: ${stateText} @ ${playingAt}, buffer: ${bufferText}`);
            await this.blazorRef?.invokeMethodAsync('OnPlaying', playingAt, isPaused, isBufferLow);
        }
        catch (e) {
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
