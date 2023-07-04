import { audioContextSource } from '../../Services/audio-context-source';
import { AudioContextRef, AudioContextRefOptions } from '../../Services/audio-context-ref';
import { FeederState, PlaybackState } from './worklets/feeder-audio-worklet-contract';
import { Disposable } from 'disposable';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { OpusDecoderWorker } from './workers/opus-decoder-worker-contract';
import { catchErrors, PromiseSource, retry } from 'promises';
import { rpcClient, rpcNoWait } from 'rpc';
import { Versioning } from 'versioning';
import { Log } from 'logging';
import { fallbackPlayback } from './fallback-playback';

const { logScope, debugLog, warnLog, errorLog } = Log.get('AudioPlayer');

const EnableFrequentDebugLog = false;

let decoderWorkerInstance: Worker = null;
let decoderWorker: OpusDecoderWorker & Disposable = null;

export class AudioPlayer {
    private static readonly whenInitialized = new PromiseSource<void>();

    private readonly id: string;
    /** How often send offset update event to the blazor, in milliseconds */
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly whenReady: Promise<void>;
    private readonly whenEnded = new PromiseSource<void>();

    private contextRef: AudioContextRef | null = null;
    private isAttached: boolean;
    private playbackState: PlaybackState = 'paused';
    private decoderToFeederWorkletChannel: MessageChannel = null;
    private feederNode?: FeederAudioWorkletNode = null;

    public onPlaybackStateChanged?: (playbackState: PlaybackState) => void;

    public static async init(): Promise<void> {
        if (this.whenInitialized.isCompleted())
            return;

        const decoderWorkerPath = Versioning.mapPath('/dist/opusDecoderWorker.js');
        decoderWorkerInstance = new Worker(decoderWorkerPath);
        decoderWorker = rpcClient<OpusDecoderWorker>(`${logScope}.decoderWorker`, decoderWorkerInstance);
        await decoderWorker.create(Versioning.artifactVersions, { type: 'rpc-timeout', timeoutMs: 20_000 });
        this.whenInitialized.resolve(undefined);
    }

    public static async create(blazorRef: DotNet.DotNetObject, id: string): Promise<AudioPlayer> {
        return new AudioPlayer(blazorRef, id);
    }

    public constructor(blazorRef: DotNet.DotNetObject, id: string) {
        this.blazorRef = blazorRef;
        this.id = id;
        debugLog?.log(`#${this.id}.constructor`);

        const attach = async (context: AudioContext) => {
            debugLog?.log(`#${this.id}.contextRef.attach: context:`, Log.ref(context));

            await AudioPlayer.whenInitialized;
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
                this.id,
                this.decoderToFeederWorkletChannel.port2,
                context,
                'feederWorklet',
                feederNodeOptions,
            );
            feederNode.onStateChanged = this.onFeederStateChanged;

            // Create decoder worker
            await decoderWorker.init(this.id, this.decoderToFeederWorkletChannel.port1);

            if(fallbackPlayback.isRequired)
                await fallbackPlayback.attach(feederNode, context);
            else
                feederNode.connect(context.destination);

            this.isAttached = true;
        };

        const detach = async () => {
            debugLog?.log(`#${this.id}.contextRef.detach`);

            if (this.isAttached)
                this.isAttached = false;

            const decoderToFeederWorkletChannel = this.decoderToFeederWorkletChannel;
            if (decoderToFeederWorkletChannel) {
                await catchErrors(
                    () => decoderWorker.close(this.id),
                    e => warnLog?.log(`#${this.id}.start.detach error:`, e));
                this.decoderToFeederWorkletChannel = null;
                await catchErrors(
                    () => decoderToFeederWorkletChannel?.port1.close(),
                    e => warnLog?.log(`#${this.id}.start.detach error:`, e));
                await catchErrors(
                    () => decoderToFeederWorkletChannel?.port2.close(),
                    e => warnLog?.log(`#${this.id}.start.detach error:`, e));
            }

            const feederNode = this.feederNode;
            if (feederNode) {
                fallbackPlayback.detach();
                this.feederNode = null;
                await catchErrors(
                    () => feederNode.disconnect(),
                    e => warnLog?.log(`#${this.id}.start.detach error:`, e));
                feederNode.onStateChanged = null;
            }
        }

        if (this.contextRef == null) {
            const options: AudioContextRefOptions = {
                attach: context => retry(3, () => attach(context)),
                detach: detach,
                dispose: () => this.end(true),
            }
            this.contextRef = audioContextSource.getRef('playback', options);
        }
        this.whenReady = this.contextRef.whenFirstTimeReady().then(() => {
            debugLog?.log(`#${this.id}.ready`);
        });
    }

    /** Called by Blazor without awaiting the result, so a call can be in the middle of appendAudio  */
    public async frame(bytes: Uint8Array): Promise<void> {
        await this.whenReady;
        if (this.playbackState === 'ended')
            return;

        // debugLog?.log(`#${this.id}.frame, ${bytes.length} byte(s)`);
        void decoderWorker.frame(
            this.id,
            bytes.buffer,
            bytes.byteOffset,
            bytes.length,
            rpcNoWait);
    }

    /** Called by Blazor */
    public async end(mustAbort: boolean): Promise<void> {
        await this.whenReady;
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.id}.end, mustAbort:`, mustAbort);

        // This ensures 'end' hit the feeder processor
        await decoderWorker.end(this.id, mustAbort);
        await this.whenEnded;
    }

    /** Called by Blazor */
    public async pause(): Promise<void> {
        await this.whenReady;
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.id}.pause`);
        await this.feederNode.pause(rpcNoWait);
    }

    /** Called by Blazor */
    public async resume(): Promise<void> {
        await this.whenReady;
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.id}.resume`);
        await this.feederNode.resume(rpcNoWait);
    }

    // Event handlers

    private onFeederStateChanged = async (state: FeederState) => {
        if (this.playbackState === 'ended' || !this.isAttached)
            return;

        if (EnableFrequentDebugLog)
            debugLog?.log(
                `#${this.id}.onFeederStateChanged: ${state.playbackState} @ ${state.playingAt}, ` +
                `buffer: ${state.bufferState} (${state.bufferedDuration}s)`);

        this.playbackState = state.playbackState;
        if (this.playbackState === 'ended') {
            this.whenEnded.resolve(undefined);
            void this.reportEnded();

            // Shutting down the rest
            await catchErrors(
                () => decoderWorker.close(this.id, rpcNoWait),
                e => errorLog?.log(`#${this.id}.end: decoderWorker.close failed:`, e))
            void this.contextRef.disposeAsync();
            this.contextRef = null;
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
                debugLog?.log(`#${this.id}.reportPlaying: ${stateText} @ ${playingAt}, buffer: ${bufferText}`);
            await this.blazorRef.invokeMethodAsync('OnPlaying', playingAt, isPaused, isBufferLow);
        }
        catch (e) {
            warnLog?.log(`#${this.id}.reportPlaying: unhandled error:`, e);
        }
    }

    private reportEnded = async (message: string | null = null) => {
        try {
            debugLog?.log(`#${this.id}.reportEnded:`, message);
            await this.blazorRef.invokeMethodAsync('OnEnded', message);
        }
        catch (e) {
            warnLog?.log(`#${this.id}.reportEnded: unhandled error:`, e);
        }
    }
}
