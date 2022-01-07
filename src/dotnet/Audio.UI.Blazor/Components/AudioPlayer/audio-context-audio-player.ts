// TODO: implement better audio context pool + cache nodes
// TODO: move the command queue processing inside a web worker
// TODO: combine demuxer / decoder / recorder wasm modules into one

import { AudioContextPool } from 'audio-context-pool';
import { IAudioPlayer } from './IAudioPlayer';
import { FeederAudioWorkletNode, PlaybackState } from './worklets/feeder-audio-worklet-node';
import {
    DecoderWorkerMessage, EndOfStreamCommand,
    InitCommand,
    LoadDecoderCommand,
    PushDataCommand, StopCommand
} from "./workers/opus-decoder-worker-message";

type PlayerState = 'inactive' | 'readyToInit' | 'playing' | 'endOfStream' ;

/** How much seconds do we have in the buffer before we tell to blazor that we have enough data */
const BufferTooMuchThreshold = 5.0;
/**
 * How much seconds do we have in the buffer before we can start to play (from the start or after starving),
 * should be in sync with audio-feeder bufferSize
 */
const BufferEnoughThreshold = 0.1;

export class AudioContextAudioPlayer implements IAudioPlayer {

    public static debug?: {
        debugMode: boolean;
        debugOperations: boolean;
        debugAppendAudioCalls: boolean;
        debugDecoder: boolean;
        debugFeeder: boolean;
        debugFeederStats: boolean;
    } = null;

    public static create(blazorRef: DotNet.DotNetObject, debugMode: boolean): AudioContextAudioPlayer {
        const player = new AudioContextAudioPlayer(blazorRef, debugMode);
        if (debugMode) {
            self["_player"] = player;
        }
        return player;
    }

    /** How often send offset update event to the blazor, in milliseconds */
    private readonly updateOffsetMs = 200;

    private readonly blazorRef: DotNet.DotNetObject;
    private readonly decoderWorker: Worker;
    private readonly decoderChannel: MessageChannel;
    private readonly preInitPromise: Promise<void>;
    private preInitResolve: () => void;
    private readonly initPromise: Promise<void>;
    private initResolve: () => void;
    private state: PlayerState = 'inactive'

    private readonly _debugMode: boolean;
    private readonly _debugOperations: boolean;
    private readonly _debugAppendAudioCalls: boolean;
    private readonly _debugDecoder: boolean;
    private readonly _debugFeeder: boolean;
    private readonly _debugFeederStats: boolean;

    private audioContext: AudioContext;
    private feederNode?: FeederAudioWorkletNode = null;
    private _unblockQueue?: () => void;

    public onStartPlaying?: () => void = null;
    public onInitialized?: () => void = null;

    constructor(blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        this.blazorRef = blazorRef;
        const debugOverride = AudioContextAudioPlayer.debug;
        if (debugOverride === null || debugOverride === undefined) {
            this._debugMode = debugMode;
            this._debugAppendAudioCalls = debugMode && true;
            this._debugOperations = debugMode && false;
            this._debugDecoder = debugMode && false;
            this._debugFeeder = debugMode && true;
            this._debugFeederStats = this._debugFeeder && true;
        }
        else {
            this._debugMode = debugOverride.debugMode;
            this._debugAppendAudioCalls = debugOverride.debugAppendAudioCalls;
            this._debugOperations = debugOverride.debugOperations;
            this._debugDecoder = debugOverride.debugDecoder;
            this._debugFeeder = debugOverride.debugFeeder;
            this._debugFeederStats = debugOverride.debugFeederStats;
        }

        this._unblockQueue = null;
        this.decoderWorker = new Worker('/dist/opusDecoderWorker.js');
        this.decoderChannel = new MessageChannel();
        this.decoderWorker.onmessage = (ev: MessageEvent<DecoderWorkerMessage>) => {
            const decoderMessage = ev.data;
            const { topic } = decoderMessage;

            switch (topic) {
                case 'readyToInit':
                    this.preInitResolve();
                    this.state = 'readyToInit';
                    break;

                case 'initCompleted':
                    this.initResolve();
                    this.state = 'playing';
                    break;
            }
        };
        this.preInitPromise = new Promise<void>(resolve => this.preInitResolve = resolve);
        this.initPromise = new Promise<void>(resolve => this.initResolve = resolve);

        const load = new LoadDecoderCommand();
        this.decoderWorker.postMessage(load, [this.decoderChannel.port1]);
    }

    public async init(header: Uint8Array): Promise<void> {
        if (this.state !== 'inactive' && this.state !== 'readyToInit') {
            this.logError("init: called in a wrong order");
        }
        if (this.state !== 'readyToInit') {
            await this.preInitPromise;
        }

        const init = new InitCommand(header.buffer, header.byteOffset, header.byteLength);
        this.decoderWorker.postMessage(init, [header.buffer]);

        this.audioContext = await AudioContextPool.get("main") as AudioContext;
        const feederNodeOptions: AudioWorkletNodeOptions = {
            channelCount: 1,
            channelCountMode: 'explicit',
            numberOfInputs: 0,
            numberOfOutputs: 1,
            outputChannelCount: [1],
            processorOptions: {
                enoughToStartPlaying: BufferEnoughThreshold,
                tooMuchBuffered: BufferTooMuchThreshold,
            }
        };
        this.feederNode = new FeederAudioWorkletNode(
            this.audioContext,
            'feederWorklet',
            feederNodeOptions
        );
        this.feederNode.initWorkerPort(this.decoderChannel.port2);
        this.feederNode.onBufferLow = () => this.needMoreData('onBufferLow');
        this.feederNode.onStarving = () => {
            if (this.state === 'endOfStream') {
                this.feederNode.onStarving = null;
                if (this._debugMode)
                    this.log(`audio ended.`);

                const _ = this.onUpdateOffsetTick();
                this.dispose();
                const __ = this.invokeOnPlaybackEnded();
                return;
            }
            this.needMoreData('onStarving');
        };
        this.feederNode.onBufferTooMuch = () => {
            const _ = this.invokeOnChangeReadiness(false, BufferTooMuchThreshold, 4);
        }
        this.feederNode.onStartPlaying = () => {
            if (this.onStartPlaying !== null)
                this.onStartPlaying();
            self.setTimeout(this.onUpdateOffsetTick, this.updateOffsetMs);
            if (this._debugFeeder) {
                this.log("Feeder start playing");
            }
        }
        this.feederNode.connect(this.audioContext.destination);
    }

    /** Called by Blazor without awaiting the result, so a call can be in the middle of appendAudio  */
    public async appendAudio(byteArray: Uint8Array, offset: number): Promise<void> {
        if (this.state !== 'playing') {
            await this.initPromise;
        }

        const pushData = new PushDataCommand(byteArray.buffer, byteArray.byteOffset, byteArray.byteLength);
        this.decoderWorker.postMessage(pushData, [byteArray.buffer]);
    }

    public endOfStream(): void {
        if (this._debugMode) {
            this.log("endOfStream()");
        }
        const endOfStream = new EndOfStreamCommand();
        this.decoderWorker.postMessage(endOfStream);

        this.state = 'endOfStream';
    }

    public stop(error: EndOfStreamError | null = null) {
        if (this._debugMode)
            this.log(`stop(error:${error})`);

        if (this._debugMode)
            this.log("Enqueue 'Abort' operation.");

        if (this.feederNode !== null) {
            this.feederNode.stop();
            this.feederNode.clear();
        }

        const stop = new StopCommand();
        this.decoderWorker.postMessage(stop);

        this.state = 'inactive';
    }

    public dispose(): void {
        if (this._debugMode)
            this.logWarn(`dispose()`);

        this.stop();
        this.feederNode.disconnect();

        const stop = new StopCommand();
        this.decoderWorker.postMessage(stop);

        this.state = 'inactive';
    }

    private onUpdateOffsetTick = async () => {
        const feeder = this.feederNode;
        if (feeder === null || this.state === 'inactive')
            return;

        let state: PlaybackState = await feeder.getState();
        await this.invokeOnPlaybackTimeChanged(state.playbackTime);
        if (this.state === 'playing') {
            self.setTimeout(this.onUpdateOffsetTick, this.updateOffsetMs);
        }
    };

    private needMoreData(source: string): void {
        if (this._debugOperations)
            this.logWarn(`[${source}]: Unblocking queue`);

        const _ = this.invokeOnChangeReadiness(true, /* isn't used */-1, 2);
    }

    private invokeOnPlaybackTimeChanged(time: number): Promise<void> {
        return this.blazorRef.invokeMethodAsync("OnPlaybackTimeChanged", time);
    }

    private invokeOnPlaybackEnded(code: number | null = null, message: string | null = null): Promise<void> {
        return this.blazorRef.invokeMethodAsync("OnPlaybackEnded", code, message);
    }

    private invokeOnChangeReadiness(isBufferReady: boolean, time: number, readyState: number): Promise<void> {
        return this.blazorRef.invokeMethodAsync("OnChangeReadiness", isBufferReady, time, readyState);
    }

    private log(message: string) {
        console.debug(`AudioContextAudioPlayer: ${message}`);
    }

    private logWarn(message: string) {
        console.warn(`AudioContextAudioPlayer: ${message}`);
    }

    private logError(message: string) {
        console.error(`AudioContextAudioPlayer: ${message}`);
    }
}
