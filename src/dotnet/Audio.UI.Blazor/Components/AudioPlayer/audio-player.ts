import { audioContextSource } from 'audio-context-source';
import { AudioContextRef } from 'audio-context-ref';
import { Disposable } from 'disposable';
import { enableChromiumAec, isAecWorkaroundNeeded } from './chromium-echo-cancellation';
import { FeederAudioWorkletNode } from './worklets/feeder-audio-worklet-node';
import { OpusDecoderWorker } from './workers/opus-decoder-worker-contract';
import { rpcClient, rpcNoWait } from 'rpc';
import { Versioning } from 'versioning';
import { Log, LogLevel, LogScope } from 'logging';
import { PromiseSource } from '../../../../nodejs/src/promises';

const LogScope: LogScope = 'AudioPlayer';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

const isAecWorkaroundUsed = isAecWorkaroundNeeded();
let decoderWorkerInstance: Worker = null;
let decoderWorker: OpusDecoderWorker & Disposable = null;

export class AudioPlayer {
    private static readonly whenInitialized = new PromiseSource<void>();

    private readonly id: string;
    /** How often send offset update event to the blazor, in milliseconds */
    private readonly updateOffsetMs = 200;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly contextRef: AudioContextRef = null;
    private readonly whenReady: Promise<void>;

    private state: 'uninitialized' | 'initialized' | 'playing' | 'paused' | 'stopped' | 'ended' = 'uninitialized';
    private isBufferTooMuch = false;
    private updateOffsetTickIntervalId: number = null;
    private decoderToFeederNodeChannel: MessageChannel = null;
    private feederNode?: FeederAudioWorkletNode = null;
    private destinationNode?: MediaStreamAudioDestinationNode = null;

    public onStartedPlaying?: () => void;

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
                this.decoderToFeederNodeChannel.port2,
                context,
                'feederWorklet',
                feederNodeOptions,
            );
            // Initialize worker
            await decoderWorker.create(this.id, this.decoderToFeederNodeChannel.port1);

            feederNode.onBufferLow = this.onBufferLow;
            feederNode.onStartPlaying = this.onStartPlaying;
            feederNode.onBufferTooMuch = this.onBufferTooMuch;
            feederNode.onStarving = this.onStarving;
            feederNode.onPaused = this.onPaused;
            feederNode.onResumed = this.onResumed;
            feederNode.onStopped = this.onStopped;
            feederNode.onEnded = this.onEnded;

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
            await feederNode.stop();
            feederNode.disconnect();
            feederNode.onBufferLow = null;
            feederNode.onStartPlaying = null;
            feederNode.onBufferTooMuch = null;
            feederNode.onStarving = null;
            feederNode.onPaused = null;
            feederNode.onResumed = null;
            feederNode.onStopped = null;
            feederNode.onEnded = null;

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
            this.state = 'initialized';
        });
    }

    /** Called by Blazor without awaiting the result, so a call can be in the middle of appendAudio  */
    public async frame(bytes: Uint8Array): Promise<void> {
        await this.whenReady;
        // debugLog?.log(`#${this.id}.frame, ${bytes.length} byte(s)`);
        void decoderWorker.onFrame(
            this.id,
            bytes.buffer,
            bytes.byteOffset,
            bytes.length,
            rpcNoWait);
    }

    public async end(): Promise<void> {
        await this.whenReady;
        debugLog?.log(`#${this.id}.end`);
        return decoderWorker.end(this.id);
    }

    public async stop(): Promise<void> {
        await this.whenReady;
        debugLog?.log(`#${this.id}.stop`);
        await decoderWorker.stop(this.id);
        await this.feederNode.stop();
    }

    public async pause(): Promise<void> {
        await this.whenReady;
        debugLog?.log(`#${this.id}.pause`);
        await this.feederNode.pause();
        await this.onUpdatePause();
    }

    public async resume(): Promise<void> {
        await this.whenReady;
        debugLog?.log(`#${this.id}.resume`);
        await this.feederNode.resume();
    }

    // Event handlers

    private onBufferLow = async () => {
        if (!this.isBufferTooMuch)
            return;

        this.isBufferTooMuch = false;
        debugLog?.log(`#${this.id}.onBufferLow`);
        await this.invokeOnChangeReadiness(true);
    }

    private onBufferTooMuch = async () => {
        if (this.isBufferTooMuch)
            return;

        this.isBufferTooMuch = true;
        debugLog?.log(`#${this.id}.onBufferTooMuch`);
        await this.invokeOnChangeReadiness(false);
    }

    private onStartPlaying = () => {
        debugLog?.log(`#${this.id}.onStartPlaying`);
        if (this.state === 'playing') {
            warnLog?.log(`#${this.id}.onStartPlaying: already in playing state`);
            return;
        }

        this.state = 'playing';
        this?.onStartedPlaying();
        this.updateOffsetTickIntervalId = self.setInterval(this.onUpdateOffsetTick, this.updateOffsetMs);
    }

    private onPaused = async () => {
        debugLog?.log(`#${this.id}.onPaused`);
        if (this.state !== 'playing') {
            warnLog?.log(`#${this.id}.onPaused: already in non-playing state: ${this.state}`);
            return;
        }

        this.state = 'paused';
        // self.clearInterval(this.updateOffsetTickIntervalId);
    }

    private onResumed = async () => {
        debugLog?.log(`#${this.id}.onResumed`);
        if (this.state !== 'paused') {
            warnLog?.log(`#${this.id}.onResumed: already in non-paused state: ${this.state}`);
            return;
        }

        this.state = 'playing';
        // this.updateOffsetTickIntervalId = self.setInterval(this.onUpdateOffsetTick, this.updateOffsetMs);
    }

    private onStopped = async () => {
        debugLog?.log(`#${this.id}.onStopped`);
        if (this.state === 'stopped')
            warnLog?.log(`#${this.id}.onStopped: already in stopped state`);

        this.state = 'stopped';
        await this.onUpdateOffsetTick();
        if (this.updateOffsetTickIntervalId)
            self.clearInterval(this.updateOffsetTickIntervalId);
    }

    private onStarving = async () => {
        debugLog?.log(`#${this.id}.onStarving`);
        if (!this.isBufferTooMuch)
            return;

        this.isBufferTooMuch = false;
        warnLog?.log(`#${this.id}.onStarving: starving!`);
        await this.invokeOnChangeReadiness(true);
    }

    private onEnded = async () => {
        debugLog?.log(`#${this.id}.onEnded`);
        if (this.updateOffsetTickIntervalId)
            self.clearInterval(this.updateOffsetTickIntervalId);

        this.state = 'ended';
        await Promise.all([decoderWorker.close(this.id), this.invokeOnPlayEnded()])
    }

    private onUpdateOffsetTick = async () => {
        try {
            if (this.state === 'playing') {
                const state = await this.feederNode.getState();
                debugLog?.log(
                    `#${this.id}.onUpdateOffsetTick:`,
                        `playbackTime:`, state.playbackTime,
                        `bufferedTime:`, state.bufferedTime);
                if (this.state !== 'playing')
                    return;

                await this.invokeOnPlayTimeChanged(state.playbackTime);
            }
        }
        catch (error) {
            errorLog?.log(`#${this.id}.onUpdateOffsetTick: unhandled error:`, error);
        }
    };

    private onUpdatePause = async () => {
        try {
            const state = await this.feederNode.getState();
            debugLog?.log(
                `#${this.id}.onUpdatePause:`,
                `playbackTime:`, state.playbackTime,
                `bufferedTime:`, state.bufferedTime);
            await this.invokeOnPausedAt(state.playbackTime);
        }
        catch (error) {
            errorLog?.log(`#${this.id}.onUpdatePause: unhandled error:`, error);
        }
    }

    // Backend methods

    private invokeOnPlayTimeChanged(time: number): Promise<void> {
        return this.blazorRef.invokeMethodAsync('OnPlayTimeChanged', time);
    }

    private invokeOnPausedAt(time: number): Promise<void> {
        return this.blazorRef.invokeMethodAsync('OnPausedAt', time);
    }

    private invokeOnPlayEnded(message: string | null = null): Promise<void> {
        return this.blazorRef.invokeMethodAsync('OnPlayEnded', message);
    }

    private invokeOnChangeReadiness(isBufferReady: boolean): Promise<void> {
        return this.blazorRef.invokeMethodAsync('OnChangeReadiness', isBufferReady);
    }
}
