import { audioContextSource } from 'audio-context-source';
import { AudioContextRef, AudioContextRefOptions } from 'audio-context-ref';
import { BufferState, PlaybackState } from './worklets/feeder-audio-worklet-contract';
import { Disposable } from 'disposable';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { OpusDecoderWorker } from './workers/opus-decoder-worker-contract';
import { PromiseSource, retry } from 'promises';
import { rpcClient, rpcNoWait } from 'rpc';
import { Versioning } from 'versioning';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'AudioPlayer';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);
const enableFrequentDebugLog = false;

let decoderWorkerInstance: Worker = null;
let decoderWorker: OpusDecoderWorker & Disposable = null;

export class AudioPlayer {
    private static readonly whenInitialized = new PromiseSource<void>();

    private readonly id: string;
    /** How often send offset update event to the blazor, in milliseconds */
    private readonly playingAtUpdatePeriodMs = 200;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly whenReady: Promise<void>;

    private contextRef: AudioContextRef | null = null;
    private isAttached: boolean;
    private playbackState: PlaybackState = 'paused';
    private bufferState: BufferState = 'enough';
    private decoderToFeederNodeChannel: MessageChannel = null;
    private feederNode?: FeederAudioWorkletNode = null;
    private reportPlayedToHandle: number = null;

    public onPlaybackStateChanged?: (playbackState: PlaybackState) => void;

    public static async init(): Promise<void> {
        if (this.whenInitialized.isCompleted())
            return;

        const decoderWorkerPath = Versioning.mapPath('/dist/opusDecoderWorker.js');
        decoderWorkerInstance = new Worker(decoderWorkerPath);
        decoderWorker = rpcClient<OpusDecoderWorker>(`${LogScope}.decoderWorker`, decoderWorkerInstance);
        await decoderWorker.init(Versioning.artifactVersions);
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
            this.bufferState = 'enough';

            // Create whatever isn't created
            this.decoderToFeederNodeChannel = new MessageChannel();
            const feederNodeOptions: AudioWorkletNodeOptions = {
                channelCount: 1,
                channelCountMode: 'explicit',
                numberOfInputs: 0,
                numberOfOutputs: 1,
                outputChannelCount: [1],
            };
            let feederNode = this.feederNode = await FeederAudioWorkletNode.create(
                this.id,
                this.decoderToFeederNodeChannel.port2,
                context,
                'feederWorklet',
                feederNodeOptions,
            );
            feederNode.onStateChanged = this.onFeederStateChanged;

            // Create decoder worker
            await decoderWorker.create(this.id, this.decoderToFeederNodeChannel.port1);

            feederNode.connect(context.destination);

            this.isAttached = true;
            const feederState = await feederNode.getState();
            void this.onFeederStateChanged(feederState.playbackState, feederState.bufferState);
        };

        const detach = async () => {
            debugLog?.log(`#${this.id}.contextRef.detach`);

            if (this.isAttached) {
                void this.onFeederStateChanged('paused', 'enough');
                this.isAttached = false;
            }

            const decoderToFeederNodeChannel = this.decoderToFeederNodeChannel;
            if (decoderToFeederNodeChannel) {
                void decoderWorker.close(this.id, rpcNoWait);
                this.decoderToFeederNodeChannel = null;
                decoderToFeederNodeChannel?.port1.close();
                decoderToFeederNodeChannel?.port2.close();
            }

            const feederNode = this.feederNode;
            if (feederNode) {
                this.feederNode = null;
                feederNode.disconnect();
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

        // Report that we started playback as soon as we can
        if (this.playbackState === 'paused')
            void this.onFeederStateChanged('playing', 'starving');
    }

    /** Called by Blazor */
    public async end(mustAbort: boolean): Promise<void> {
        await this.whenReady;
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.id}.end, mustAbort:`, mustAbort);
        return decoderWorker.end(this.id, mustAbort);
    }

    /** Called by Blazor */
    public async pause(): Promise<void> {
        await this.whenReady;
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.id}.pause`);
        await this.feederNode.pause();
    }

    /** Called by Blazor */
    public async resume(): Promise<void> {
        await this.whenReady;
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.id}.resume`);
        await this.feederNode.resume();
    }

    // Helpers

    private startReportingPlayingTo() {
        if (this.reportPlayedToHandle)
            return;

        this.reportPlayedToHandle = self.setInterval(this.reportPlayingAt, this.playingAtUpdatePeriodMs);
    }

    private stopReportingPlayingTo() {
        if (!this.reportPlayedToHandle)
            return;

        clearInterval(this.reportPlayedToHandle);
        this.reportPlayedToHandle = null;
    }

    // Event handlers

    private onFeederStateChanged = async (playbackState: PlaybackState, bufferState: BufferState) => {
        if (this.playbackState === 'ended' || !this.isAttached)
            return;

        debugLog?.log(`#${this.id}.onFeederStateChanged: ${playbackState}, ${bufferState}`);
        const oldPlaybackState = this.playbackState;
        const oldBufferState = this.bufferState;
        this.playbackState = playbackState;
        this.bufferState = bufferState;
        if (playbackState !== oldPlaybackState) {
            if (playbackState === 'playing')
                this.startReportingPlayingTo();
            else {
                this.stopReportingPlayingTo();
                if (playbackState === 'ended') {
                    await decoderWorker.close(this.id, rpcNoWait);
                    await this.contextRef.disposeAsync();
                    this.contextRef = null;
                    await this.reportOnEnded();
                }
                else
                    await this.reportPausedAt();
            }
            this.onPlaybackStateChanged?.(playbackState);
        }
        if (playbackState === 'playing') {
            if (bufferState !== oldBufferState)
                await this.reportBufferStateChange();
        }
    }

    // Backend invocation methods

    private reportBufferStateChange = async () => {
        try {
            debugLog?.log(`#${this.id}.reportBufferStateChange:`, this.bufferState);
            await this.blazorRef.invokeMethodAsync('OnBufferStateChange', this.bufferState !== 'enough');
        }
        catch (e) {
            warnLog?.log(`#${this.id}.reportBufferStateChange: unhandled error:`, e);
        }
    }

    private reportPlayingAt = async () => {
        try {
            if (this.playbackState === 'playing') {
                const state = await this.feederNode.getState();
                if (enableFrequentDebugLog)
                    debugLog?.log(
                        `#${this.id}.reportPlayingAt:`,
                        `playbackTime:`, state.playingAt,
                        `bufferedTime:`, state.bufferedDuration);
                await this.blazorRef.invokeMethodAsync('OnPlayingAt', state.playingAt);
            }
        }
        catch (e) {
            warnLog?.log(`#${this.id}.reportPlayingAt: unhandled error:`, e);
        }
    };

    private reportPausedAt = async () => {
        try {
            const state = await this.feederNode.getState();
            debugLog?.log(
                `#${this.id}.reportPausedAt:`,
                `playbackTime:`, state.playingAt,
                `bufferedTime:`, state.bufferedDuration);
            await this.blazorRef.invokeMethodAsync('OnPausedAt', state.playingAt);
        }
        catch (e) {
            warnLog?.log(`#${this.id}.reportPausedAt: unhandled error:`, e);
        }
    }

    private reportOnEnded = async (message: string | null = null) => {
        try {
            debugLog?.log(`#${this.id}.reportOnEnded:`, message);
            await this.blazorRef.invokeMethodAsync('OnEnded', message);
        }
        catch (e) {
            warnLog?.log(`#${this.id}.reportOnEnded: unhandled error:`, e);
        }
    }
}
