import { audioContextSource } from 'audio-context-source';
import { AudioContextRef } from 'audio-context-ref';
import { Disposable } from 'disposable';
import { enableChromiumAec, isAecWorkaroundNeeded } from './chromium-echo-cancellation';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { OpusDecoderWorker } from './workers/opus-decoder-worker-contract';
import { rpcClient, rpcNoWait } from 'rpc';
import { Versioning } from 'versioning';
import { Log, LogLevel, LogScope } from 'logging';
import { delayAsync, PromiseSource } from '../../../../nodejs/src/promises';
import { BufferState, PlaybackState } from './worklets/feeder-audio-worklet-contract';

const LogScope: LogScope = 'AudioPlayer';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);
const enableFrequentDebugLog = false;

const isAecWorkaroundUsed = isAecWorkaroundNeeded();
let decoderWorkerInstance: Worker = null;
let decoderWorker: OpusDecoderWorker & Disposable = null;

export class AudioPlayer {
    private static readonly whenInitialized = new PromiseSource<void>();

    private readonly id: string;
    /** How often send offset update event to the blazor, in milliseconds */
    private readonly playingAtUpdatePeriodMs = 200;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly contextRef: AudioContextRef = null;
    private readonly whenReady: Promise<void>;

    private playbackState: PlaybackState = 'paused';
    private bufferState: BufferState = 'enough';
    private decoderToFeederNodeChannel: MessageChannel = null;
    private feederNode?: FeederAudioWorkletNode = null;
    private destinationNode?: MediaStreamAudioDestinationNode = null;
    private reportPlayedToHandle: number = null;

    public onPlaybackStateChanged?: (playbackState: PlaybackState) => void;

    public static async init(): Promise<void> {
        if (this.whenInitialized.isCompleted())
            return;

        const decoderWorkerPath = Versioning.mapPath('/dist/opusDecoderWorker.js');
        decoderWorkerInstance = new Worker(decoderWorkerPath);
        decoderWorker = rpcClient<OpusDecoderWorker>(`${LogScope}.vadWorker`, decoderWorkerInstance);
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
            debugLog?.log(`#${this.id}.contextRef.attach: context:`, context, ', isAecWorkaroundUsed: ', isAecWorkaroundUsed);

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

            if (isAecWorkaroundUsed) {
                this.destinationNode = context.createMediaStreamDestination();
                feederNode.connect(this.destinationNode);
                await enableChromiumAec(this.destinationNode.stream);
            } else {
                feederNode.connect(context.destination);
            }
        };

        const detach = async () => {
            debugLog?.log(`#${this.id}.contextRef.detach`);

            const feederNode = this.feederNode;
            if (!feederNode)
                return;

            this.feederNode = null;
            await decoderWorker.close(this.id);
            feederNode.disconnect();
            feederNode.onStateChanged = null;

            this.decoderToFeederNodeChannel?.port1.close();
            this.decoderToFeederNodeChannel?.port2.close();
            this.decoderToFeederNodeChannel = null;

            const destinationNode = this.destinationNode;
            if (!destinationNode)
                return;

            this.destinationNode = null;
            const tracks = destinationNode.stream.getTracks();
            for (let i = 0; i < tracks.length; ++i) {
                destinationNode.stream.removeTrack(tracks[i]);
            }
            destinationNode.disconnect();
        }

        if (this.contextRef == null)
            this.contextRef = audioContextSource.getRef('playback', attach, detach);
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

    public async end(mustAbort: boolean): Promise<void> {
        await this.whenReady;
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.id}.end, mustAbort:`, mustAbort);
        return decoderWorker.end(this.id, mustAbort);
    }

    public async pause(): Promise<void> {
        await this.whenReady;
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.id}.pause`);
        await this.feederNode.pause();
    }

    public async resume(): Promise<void> {
        await this.whenReady;
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.id}.resume`);
        await this.feederNode.resume();
    }

    // Event handlers

    private onFeederStateChanged = async (playbackState: PlaybackState, bufferState: BufferState) => {
        if (this.playbackState === 'ended')
            return;

        debugLog?.log(`#${this.id}.onFeederStateChanged: ${playbackState}, ${bufferState}`);
        const oldPlaybackState = this.playbackState;
        const oldBufferState = this.bufferState;
        this.playbackState = playbackState;
        this.bufferState = bufferState;
        if (playbackState !== oldPlaybackState) {
            if (playbackState === 'playing')
                this.reportPlayedToHandle = self.setInterval(this.reportPlayingAt, this.playingAtUpdatePeriodMs);
            else {
                self.clearInterval(this.reportPlayedToHandle);
                if (playbackState === 'ended')
                    await this.reportOnEnded();
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
            errorLog?.log(`#${this.id}.reportBufferStateChange: unhandled error:`, e);
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
            errorLog?.log(`#${this.id}.reportPlayingAt: unhandled error:`, e);
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
            errorLog?.log(`#${this.id}.reportPausedAt: unhandled error:`, e);
        }
    }

    private reportOnEnded = async (message: string | null = null) => {
        try {
            await this.blazorRef.invokeMethodAsync('OnEnded', message);
        }
        catch (e) {
            errorLog?.log(`#${this.id}.reportOnEnded: unhandled error:`, e);
        }
    }
}
