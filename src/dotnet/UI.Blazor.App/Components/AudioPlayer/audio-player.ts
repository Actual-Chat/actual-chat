import {
    audioContextSource,
    AppAudioContext,
    resetMediaSessionDebounced,
    AudioContextRef,
    AudioContextAction,
} from '../../Services/audio-context-source';
import { AudioContextTrait, AttachedAudioContextTrait, DestinationFallbackTrait } from '../../Services/audio-context-traits';
import { FeederState, PlaybackState } from './worklets/feeder-audio-worklet-contract';
import { Disposable } from 'disposable';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { OpusDecoderWorker } from './workers/opus-decoder-worker-contract';
import { catchErrors, PromiseSource } from 'promises';
import { rpcClient, rpcNoWait } from 'rpc';
import { Versioning } from 'versioning';
import { Log } from 'logging';
import { ObjectPool } from 'object-pool';
import { Resettable } from 'resettable';
import { AudioInitializer } from '../../Services/audio-initializer';
import { BrowserInfo } from '../../../UI.Blazor/Services/BrowserInfo/browser-info';

const { logScope, debugLog, warnLog } = Log.get('AudioPlayer');

const EnableFrequentDebugLog = false;

let decoderWorkerInstance: Worker;
let decoderWorker: OpusDecoderWorker & Disposable;

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
        await decoderWorker.init(this.internalId, decoderToFeederWorkletChannel.port1);

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
        this.feederNode.onStateChanged = (state) => this.player.onFeederStateChanged(state);
    }

    public async onClosed(): Promise<void> {
        debugLog?.log(`#${this.internalId}.attachedFeederNode.onClosed`);

        // Close decoder worker
        await catchErrors(
            () => decoderWorker.close(this.internalId),
            e => warnLog?.log(`#${this.internalId}.onClosed decoderWorker.close error:`, e));

        // Close channel ports
        await catchErrors(
            () => this.channel?.port1.close(),
            e => warnLog?.log(`#${this.internalId}.onClosed port1.close error:`, e));
        await catchErrors(
            () => this.channel?.port2.close(),
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
    private static nextInternalId: number = 0;
    private static initStarted = false;

    private readonly internalId: string;
    private readonly feederNodeTrait: FeederNodeTrait;

    private blazorRef?: DotNet.DotNetObject;
    private contextRef?: AudioContextRef;
    private playingAction?: AudioContextAction;
    private whenEnded?: PromiseSource<void>;

    private playbackState: PlaybackState = 'paused';

    public static get isInitialized() {
        return AudioPlayer.whenInitialized && AudioPlayer.whenInitialized.isCompleted();
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
        if (!decoderWorker)
            decoderWorker = rpcClient<OpusDecoderWorker>(`${logScope}.decoderWorker`, decoderWorkerInstance);

        await decoderWorker.create(Versioning.assetMap, {type: 'rpc-timeout', timeoutMs: 20_000});

        this.whenInitialized.resolve(undefined);
    }

    public static async ensureInitialized(): Promise<void> {
        if (AudioPlayer.initStarted)
            await AudioPlayer.whenInitialized;
        else
            await AudioPlayer.init();
    }

    public static async terminate(): Promise<void> {
        decoderWorkerInstance.terminate();
        AudioPlayer.whenInitialized = new PromiseSource<void>();
        AudioInitializer.isPlayerInitialized = false;
        this.initStarted = false;
    }

    /** Called from Blazor */
    public static async create(blazorRef: DotNet.DotNetObject, id: string, preSkip: number, title: string, album: string): Promise<AudioPlayer> {
        await AudioPlayer.init();
        const player = AudioPlayer.pool.get();
        await player.startPlayback(blazorRef, id, preSkip, title, album);
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
        album: string): Promise<void> {

        debugLog?.log(`#${this.internalId} -> startPlayback()`);
        this.blazorRef = blazorRef;
        this.playbackState = 'paused';
        this.whenEnded = new PromiseSource<void>();

        // Create a ref with the feeder node trait
        this.contextRef = audioContextSource.createRef(this.feederNodeTrait);

        // Run the playback action
        this.playingAction = this.contextRef.run(async () => {
            const attachedFeeder = this.contextRef!.getTrait<AttachedFeederNode>(this.feederNodeTrait);
            if (attachedFeeder) {
                await decoderWorker.resume(this.internalId, rpcNoWait);
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
        const attachedFeeder = this.contextRef?.getTrait<AttachedFeederNode>(this.feederNodeTrait);
        if (attachedFeeder) {
            void attachedFeeder.feederNode.pause(rpcNoWait);
        }
        this.blazorRef = undefined;
        this.playbackState = 'ended';
        this.playingAction?.dispose();
        this.playingAction = undefined;
        this.contextRef?.dispose();
        this.contextRef = undefined;
        this.whenEnded?.resolve(undefined);
        resetMediaSessionDebounced();
    }

    /** Called by Blazor without awaiting the result, so a call can be in the middle of appendAudio  */
    public async frame(bytes: Uint8Array): Promise<void> {
        if (this.playbackState === 'ended')
            return;

        // Wait for context to be ready
        if (this.contextRef && !this.contextRef.isReady) {
            await this.contextRef.whenReady();
        }

        void decoderWorker.frame(
            this.internalId,
            bytes.buffer,
            bytes.byteOffset,
            bytes.length,
            rpcNoWait);
    }

    /** Called by Blazor */
    public async end(mustAbort: boolean): Promise<void> {
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.internalId}.end, mustAbort:`, mustAbort);

        // Wait for context to be ready
        if (this.contextRef && !this.contextRef.isReady) {
            await this.contextRef.whenReady();
        }

        // This ensures 'end' hit the feeder processor which in turn sends feeder status back and resolves this.whenEnded
        await decoderWorker.end(this.internalId, mustAbort);
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
    public async resume(): Promise<void> {
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.internalId}.resume`);

        // Create new ref and action for resumed playback
        this.contextRef = audioContextSource.createRef(this.feederNodeTrait);
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
                this.playingAction?.dispose();
                this.playingAction = undefined;
                this.contextRef?.dispose();
                this.contextRef = undefined;
                this.whenEnded?.resolve(undefined);
                AudioPlayer.pool.release(this);
            }
        }
        else {
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
