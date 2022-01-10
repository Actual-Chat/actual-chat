// TODO: implement better audio context pool + cache nodes
// TODO: move the command queue processing inside a web worker
// TODO: combine demuxer / decoder / recorder wasm modules into one

import { AudioContextPool } from 'audio-context-pool';
import { FeederAudioWorkletNode, PlaybackState } from './worklets/feeder-audio-worklet-node';
import {
    DecoderWorkerMessage,
    EndOfStreamCommand,
    InitCommand,
    LoadDecoderCommand,
    PushDataCommand,
    StopCommand
} from "./workers/opus-decoder-worker-message";

type PlayerState = 'inactive' | 'buffering' | 'playing' | 'endOfStream' ;

/** How much seconds do we have in the buffer before we tell to blazor that we have enough data */
const BUFFER_TOO_MUCH_THRESHOLD = 5.0;
/**
 * How much seconds do we have in the buffer before we can start to play (from the start or after starving),
 * should be in sync with audio-feeder bufferSize
 */
const BUFFER_ENOUGH_THRESHOLD = 0.1;

const playerMap = new Map<string, AudioContextAudioPlayer>();
const decoderWorker = new Worker('/dist/opusDecoderWorker.js');
decoderWorker.postMessage(new LoadDecoderCommand());
decoderWorker.onmessage = (ev: MessageEvent<DecoderWorkerMessage>) => {
    const { playerId, topic } = ev.data;

    const player = playerMap.get(playerId);
    if (player == null) {
        console.error(`decoderWorker.onmessage: can't find player=${playerId}`);
        return;
    }
    switch (topic) {
        case 'initCompleted':
            player.initCompleted();
            break;
    }
};

export interface AudioPlayer {
    onStartPlaying?: () => void;
    onInitialized?: () => void;
    init(byteArray: Uint8Array): Promise<void>;
    appendAudio(byteArray: Uint8Array, offset: number): Promise<void>;
    endOfStream(): void;
    stop(error: EndOfStreamError | null): void;
}

export class AudioContextAudioPlayer implements AudioPlayer {

    public static debug?: {
        debugMode: boolean;
        debugOperations: boolean;
        debugAppendAudioCalls: boolean;
        debugDecoder: boolean;
        debugFeeder: boolean;
        debugFeederStats: boolean;
    } = null;

    public static async create(playerId: string, blazorRef: DotNet.DotNetObject, debugMode: boolean, header: Uint8Array)
        : Promise<AudioContextAudioPlayer> {
        const player = new AudioContextAudioPlayer(playerId, blazorRef, debugMode);
        if (debugMode) {
            self["_player"] = player;
        }
        await player.init(header);

        playerMap.set(playerId, player)
        return player;
    }

    /** How often send offset update event to the blazor, in milliseconds */
    private readonly updateOffsetMs = 200;
    private readonly playerId: string;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly decoderChannel: MessageChannel;

    private initPromise?: Promise<void> = null;
    private initResolve?: () => void = null;
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

    constructor(playerId: string, blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        this.playerId = playerId;
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
        this.decoderChannel = new MessageChannel();
    }

    public async init(header: Uint8Array): Promise<void> {
        if (this.state !== 'inactive') {
            this.logError("init: called in a wrong order");
        }

        this.initPromise = new Promise<void>(resolve => this.initResolve = resolve);
        const init = new InitCommand(this.playerId, header.buffer, header.byteOffset, header.byteLength);
        decoderWorker.postMessage(init, [header.buffer, this.decoderChannel.port1]);

        this.audioContext = await AudioContextPool.get("main") as AudioContext;
        const feederNodeOptions: AudioWorkletNodeOptions = {
            channelCount: 1,
            channelCountMode: 'explicit',
            numberOfInputs: 0,
            numberOfOutputs: 1,
            outputChannelCount: [1],
            processorOptions: {
                enoughToStartPlaying: BUFFER_ENOUGH_THRESHOLD,
                tooMuchBuffered: BUFFER_TOO_MUCH_THRESHOLD,
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
                this.onStop();
                const __ = this.invokeOnPlaybackEnded();
                return;
            }
            this.state = 'buffering';
            this.needMoreData('onStarving');
        };
        this.feederNode.onBufferTooMuch = () => {
            const _ = this.invokeOnChangeReadiness(false, BUFFER_TOO_MUCH_THRESHOLD, 4);
        }
        this.feederNode.onStartPlaying = () => {
            this.state = 'playing'
            if (this.onStartPlaying !== null)
                this.onStartPlaying();
            self.setTimeout(this.onUpdateOffsetTick, this.updateOffsetMs);
            if (this._debugFeeder) {
                this.log("Feeder start playing");
            }
        }
        this.feederNode.connect(this.audioContext.destination);
    }

    public initCompleted(): void {
        const initResolve = this.initResolve;
        if (initResolve != null) {
            initResolve();
            this.initPromise = null;
            this.initResolve = null;
        }
        this.state = 'buffering';
    };

    /** Called by Blazor without awaiting the result, so a call can be in the middle of appendAudio  */
    public async appendAudio(byteArray: Uint8Array, offset: number): Promise<void> {
        if (this.state === 'inactive') {
            const initPromise = this.initPromise;
            if (initPromise != null) {
                await initPromise;
                this.initPromise = null;
                this.initResolve = null;
            }
            else {
                return;
            }
        }

        const pushData = new PushDataCommand(this.playerId, byteArray.buffer, byteArray.byteOffset, byteArray.byteLength);
        decoderWorker.postMessage(pushData, [byteArray.buffer]);
    }

    public endOfStream(): void {
        if (this._debugMode) {
            this.log("Enqueue 'EndOfStream' operation.");
        }
        const endOfStream = new EndOfStreamCommand(this.playerId);
        decoderWorker.postMessage(endOfStream);

        this.state = 'endOfStream';
    }

    public stop(error: EndOfStreamError | null = null) {
        if (this._debugMode)
            this.log(`stop(error:${error})`);

        this.onStop();

        const stop = new StopCommand(this.playerId);
        decoderWorker.postMessage(stop);

    }

    private onStop(): void {
        if (this._debugMode)
            this.log(`onStop()`);

        this.state = 'inactive';

        this.initPromise = null;
        this.initResolve = null;

        if (this.feederNode !== null) {
            this.feederNode.stop();
            this.feederNode.clear();
            this.feederNode.disconnect();
            this.feederNode = null;
        }

        playerMap.delete(this.playerId);
    }

    private onUpdateOffsetTick = async () => {
        const feeder = this.feederNode;
        if (feeder === null || this.state === 'inactive')
            return;

        let state: PlaybackState = await feeder.getState();
        await this.invokeOnPlaybackTimeChanged(state.playbackTime);
        self.setTimeout(this.onUpdateOffsetTick, this.updateOffsetMs);
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
