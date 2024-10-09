import { audioContextSource, OverridenAudioContext } from '../../Services/audio-context-source';
import { AudioContextRef, AudioContextRefOptions } from '../../Services/audio-context-ref';
import { FeederState, PlaybackState } from './worklets/feeder-audio-worklet-contract';
import { Disposable } from 'disposable';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { OpusDecoderWorker } from './workers/opus-decoder-worker-contract';
import { catchErrors, debounce, PromiseSource, retry } from 'promises';
import { rpcClient, rpcNoWait } from 'rpc';
import { Versioning } from 'versioning';
import { Log } from 'logging';
import { ObjectPool } from "object-pool";
import { Resettable } from "resettable";
import { AudioInitializer } from "../../Services/audio-initializer";
import { AUDIO_PLAY as AP } from '_constants';

const { logScope, debugLog, warnLog, errorLog } = Log.get('AudioPlayer');

const EnableFrequentDebugLog = false;

let decoderWorkerInstance: Worker = null;
let decoderWorker: OpusDecoderWorker & Disposable = null;

export class AudioPlayer implements Resettable {
    private static readonly pool: ObjectPool<AudioPlayer> = new ObjectPool<AudioPlayer>(() => new AudioPlayer());
    private static whenInitialized = new PromiseSource<void>();
    private static nextInternalId: number = 0;
    private static initStarted = false;

    private readonly internalId: string;
    private readonly contextRef: AudioContextRef;

    private blazorRef?: DotNet.DotNetObject;
    private playing?: Disposable;

    private playbackState: PlaybackState = 'paused';
    private decoderToFeederWorkletChannel: MessageChannel = null;
    private feederNode?: FeederAudioWorkletNode = null;

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
            decoderWorkerInstance = new Worker(decoderWorkerPath);
        }
        if (!decoderWorker)
            decoderWorker = rpcClient<OpusDecoderWorker>(`${logScope}.decoderWorker`, decoderWorkerInstance);

        await decoderWorker.create(Versioning.artifactVersions, {type: 'rpc-timeout', timeoutMs: 20_000});

        this.whenInitialized.resolve(undefined);
    }

    private static async ensureInitialized(): Promise<void> {
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

        const attach = async (context: OverridenAudioContext) => {
            debugLog?.log(`#${this.internalId}.contextRef.attach: context:`, Log.ref(context));

            await AudioPlayer.ensureInitialized();
            this.playbackState = 'paused';

            // Create whatever isn't created
            this.decoderToFeederWorkletChannel = new MessageChannel();
            const feederNodeOptions: AudioWorkletNodeOptions = {
                channelCount: 1,
                channelCountMode: 'explicit',
                numberOfInputs: 0,
                numberOfOutputs: 1,
                outputChannelCount: [1],
            };
            let feederNode = this.feederNode = await FeederAudioWorkletNode.create(
                this.internalId,
                this.decoderToFeederWorkletChannel.port2,
                context,
                'feederWorklet',
                feederNodeOptions,
            );
            feederNode.onStateChanged = this.onFeederStateChanged;

            // Create decoder worker
            await decoderWorker.init(this.internalId, this.decoderToFeederWorkletChannel.port1);

            const destinationOverride = context.destinationOverride ?? context.destination;
            feederNode.connect(destinationOverride);
        };

        const detach = async () => {
            debugLog?.log(`#${this.internalId}.contextRef.detach`);

            const decoderToFeederWorkletChannel = this.decoderToFeederWorkletChannel;
            if (decoderToFeederWorkletChannel) {
                await catchErrors(
                    () => decoderWorker.close(this.internalId),
                    e => warnLog?.log(`#${this.internalId}.start.detach error:`, e));
                this.decoderToFeederWorkletChannel = null;
                await catchErrors(
                    () => decoderToFeederWorkletChannel?.port1.close(),
                    e => warnLog?.log(`#${this.internalId}.start.detach error:`, e));
                await catchErrors(
                    () => decoderToFeederWorkletChannel?.port2.close(),
                    e => warnLog?.log(`#${this.internalId}.start.detach error:`, e));
            }

            const feederNode = this.feederNode;
            if (feederNode) {
                this.feederNode.disconnect();
                this.feederNode = null;
                await catchErrors(
                    () => feederNode.disconnect(),
                    e => warnLog?.log(`#${this.internalId}.start.detach error:`, e));
                feederNode.onStateChanged = null;
            }
        }

        const options: AudioContextRefOptions = {
            attach: attach,
            detach: detach,
            dispose: () => this.end(true),
        }
        this.contextRef = audioContextSource.getRef('playback', options);
    }

    public async startPlayback(
        blazorRef: DotNet.DotNetObject,
        id: string,
        preSkip: number,
        title: string,
        album: string): Promise<void> {

        debugLog?.log(`#${this.internalId} -> startPlayback()`);
        this.blazorRef = blazorRef;
        this.playing = this.contextRef.use(async () => {
            if (this.playbackState === 'ended')
                await decoderWorker.resume(this.internalId, rpcNoWait);
            await this.feederNode.resume(preSkip);
            this.playbackState = 'paused';
        });
        this.setMediaSession(title, album);
        debugLog?.log(`#${this.internalId} <- startPlayback()`);
    }

    public reset(): void {
        debugLog?.log(`#${this.internalId} reset()`);
        this.blazorRef = null;
        this.playbackState = 'ended';
        this.playing?.dispose();
        this.playing = null;
        this.resetMediaSessionDebounced();
        try {
            if (navigator.mediaSession) {
                navigator.mediaSession.metadata = null;
                navigator.mediaSession.playbackState = 'none';
            }
        }
        catch (e) {
            warnLog?.log(`#${this.internalId} reset(): error clearing metadata:`, e);
        }
    }

    /** Called by Blazor without awaiting the result, so a call can be in the middle of appendAudio  */
    public async frame(bytes: Uint8Array): Promise<void> {
        // debugLog?.log(`#${this.internalId} frame()`, this.playbackState);
        if (this.playbackState === 'ended')
            return;

        // debugLog?.log(`#${this.internalId}.frame, ${bytes.length} byte(s)`);
        await this.contextRef.whenReady();

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
        await this.contextRef.whenReady();

        // This ensures 'end' hit the feeder processor which in turn sends feeder status back and resolves this.whenEnded
        await decoderWorker.end(this.internalId, mustAbort);
        this.resetMediaSessionDebounced();
    }

    /** Called by Blazor */
    public async pause(): Promise<void> {
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.internalId}.pause`);
        await this.contextRef.whenReady();
        await this.feederNode.pause(rpcNoWait);
        this.playing?.dispose();
        this.playing = null;
        this.playbackState = 'paused';

        this.setMediaSessionState('paused');
    }

    /** Called by Blazor */
    public async resume(): Promise<void> {
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.internalId}.resume`);
        this.playing = this.contextRef.use(async () => {
            await this.feederNode.resume(0);
            this.playbackState = 'playing';
        });

        this.setMediaSessionState('playing');
    }

    // Private methods

    private setMediaSessionState(playbackState: MediaSessionPlaybackState): void {
        try {
            if (navigator.mediaSession)
                navigator.mediaSession.playbackState = playbackState;
        }
        catch (e) {
            warnLog?.log(`#${this.internalId} pause(): error settings playback state:`, e);
        }
    }

    private setMediaSession(title: string, album: string): void {
        this.resetMediaSessionDebounced.reset();
        try {
            if (navigator.mediaSession) {
                navigator.mediaSession.metadata = new MediaMetadata({
                    title: `${title} @ ${album}`,
                    album: album,
                    artist: 'Actual Chat',
                    artwork: [{ src: '/_applogo-dark.svg' }],
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

    private resetMediaSessionDebounced = debounce(() => this.resetMediaSession(), AP.MEDIA_SESSION_RESET_DEBOUNCE_MS);
    private resetMediaSession(): void {
        try {
            if (navigator.mediaSession) {
                navigator.mediaSession.metadata = null;
                navigator.mediaSession.playbackState = 'none';
            }
        }
        catch (e) {
            warnLog?.log(`#${this.internalId} resetMediaSession(): error clearing metadata:`, e);
        }
    }

    // Event handlers

    private onFeederStateChanged = async (state: FeederState) => {
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
                this.playing?.dispose();
                this.playing = null;
                AudioPlayer.pool.release(this);
            }
        }
        else {
            const isPaused = state.playbackState === 'paused';
            const isBufferLow = state.bufferState !== 'ok';
            void this.reportPlaying(state.playingAt, isPaused, isBufferLow);
        }
    }

    // Backend invocation methods

    private reportPlaying = async (playingAt: number, isPaused: boolean, isBufferLow: boolean) => {
        try {
            const stateText = isPaused ? 'paused' : 'playing';
            const bufferText = isBufferLow ? 'low' : 'ok';

            if (EnableFrequentDebugLog)
                debugLog?.log(`#${this.internalId}.reportPlaying: ${stateText} @ ${playingAt}, buffer: ${bufferText}`);
            await this.blazorRef.invokeMethodAsync('OnPlaying', playingAt, isPaused, isBufferLow);
        }
        catch (e) {
            warnLog?.log(`#${this.internalId}.reportPlaying: unhandled error:`, e);
        }
    }

    private reportEnded = async (message: string | null = null) => {
        try {
            debugLog?.log(`#${this.internalId}.reportEnded:`, message);
            await this.blazorRef.invokeMethodAsync('OnEnded', message);
        }
        catch (e) {
            warnLog?.log(`#${this.internalId}.reportEnded: unhandled error:`, e);
        }
    }
}
