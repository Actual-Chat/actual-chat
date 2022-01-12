import { AudioContextPool } from 'audio-context-pool';
import { FeederAudioWorkletNode, PlaybackState } from './worklets/feeder-audio-worklet-node';
import {
    DataDecoderMessage,
    DecoderWorkerMessage, EndDecoderMessage, InitCompletedDecoderWorkerMessage, InitDecoderMessage, LoadDecoderMessage, StopDecoderMessage,
} from "./workers/opus-decoder-worker-message";

type PlayerState = 'inactive' | 'buffering' | 'playing' | 'endOfStream';

const playerMap = new Map<string, AudioPlayer>();
const decoderWorker = new Worker('/dist/opusDecoderWorker.js');
// TODO: try to remove load message
decoderWorker.postMessage({ type: 'load' });
decoderWorker.onmessage = (ev: MessageEvent<DecoderWorkerMessage>) => {
    const msg = ev.data;
    switch (msg.type) {
        case 'initCompleted':
            onInitCompleted(msg as InitCompletedDecoderWorkerMessage);
            break;
        default:
            throw new Error(`Unsupported message from the decoder worker. Message type: ${msg.type}`);
    }
};

function onInitCompleted(message: InitCompletedDecoderWorkerMessage) {
    const { playerId } = message;
    const player = playerMap.get(playerId);
    if (player == null) {
        console.error(`decoderWorker.onmessage: can't find player=${playerId}`);
        return;
    }
    player.initCompleted();
}

export class AudioPlayer {

    public static debug?: {
        debugMode: boolean;
        debugOperations: boolean;
        debugFeeder: boolean;
    } = null;

    public static async create(playerId: string, blazorRef: DotNet.DotNetObject, debugMode: boolean, header: Uint8Array)
        : Promise<AudioPlayer> {
        const player = new AudioPlayer(playerId, blazorRef, debugMode);
        if (debugMode) {
            self["_player"] = player;
        }
        await player.init(header);

        playerMap.set(playerId, player);
        return player;
    }

    /** How often send offset update event to the blazor, in milliseconds */
    private readonly updateOffsetMs = 200;
    private readonly playerId: string;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly decoderChannel: MessageChannel;

    private initPromise?: Promise<void> = null;
    private initResolve?: () => void = null;
    private state: PlayerState = 'inactive';

    private readonly _debugMode: boolean;
    private readonly _debugOperations: boolean;
    private readonly _debugFeeder: boolean;

    private audioContext: AudioContext;
    private feederNode?: FeederAudioWorkletNode = null;

    public onStartPlaying?: () => void = null;
    public onInitialized?: () => void = null;

    constructor(playerId: string, blazorRef: DotNet.DotNetObject, debugMode: boolean) {
        this.playerId = playerId;
        this.blazorRef = blazorRef;
        const debugOverride = AudioPlayer.debug;

        if (debugOverride === null || debugOverride === undefined) {
            this._debugMode = debugMode;
            this._debugOperations = debugMode && false;
            this._debugFeeder = debugMode && true;
        }
        else {
            this._debugMode = debugOverride.debugMode;
            this._debugOperations = debugOverride.debugOperations;
            this._debugFeeder = debugOverride.debugFeeder;
        }

        this.decoderChannel = new MessageChannel();
    }

    public async init(header: Uint8Array): Promise<void> {
        if (this.state !== 'inactive') {
            this.logError("init: called in a wrong order");
        }

        this.initPromise = new Promise<void>(resolve => this.initResolve = resolve);
        const init: InitDecoderMessage = {
            type: 'init',
            playerId: this.playerId,
            buffer: header.buffer,
            offset: header.byteOffset,
            length: header.byteLength,
            workletPort: this.decoderChannel.port1
        };
        decoderWorker.postMessage(init, [header.buffer, this.decoderChannel.port1]);

        this.audioContext = await AudioContextPool.get("main") as AudioContext;
        const feederNodeOptions: AudioWorkletNodeOptions = {
            channelCount: 1,
            channelCountMode: 'explicit',
            numberOfInputs: 0,
            numberOfOutputs: 1,
            outputChannelCount: [1],
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
            const _ = this.invokeOnChangeReadiness(false, 10, 4);
        };
        this.feederNode.onStartPlaying = () => {
            this.state = 'playing';
            if (this.onStartPlaying !== null)
                this.onStartPlaying();
            self.setTimeout(this.onUpdateOffsetTick, this.updateOffsetMs);
            if (this._debugFeeder) {
                this.log("Feeder start playing");
            }
        };
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

        const pushDataMsg: DataDecoderMessage = {
            type: 'data',
            playerId: this.playerId,
            buffer: byteArray.buffer,
            offset: byteArray.byteOffset,
            length: byteArray.byteLength,
        };
        decoderWorker.postMessage(pushDataMsg, [byteArray.buffer]);
    }

    public endOfStream(): void {
        if (this._debugMode) {
            this.log("Enqueue 'EndOfStream' operation.");
        }
        const msg: EndDecoderMessage = { type: 'end', playerId: this.playerId };
        decoderWorker.postMessage(msg);
        this.state = 'endOfStream';
    }

    public stop(error: EndOfStreamError | null = null) {
        if (this._debugMode)
            this.log(`stop(error:${error})`);

        this.onStop();
        const msg: StopDecoderMessage = { type: 'stop', playerId: this.playerId };
        decoderWorker.postMessage(msg);
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
        if (this._debugMode) {
            this.log(`onUpdateOffsetTick: playbackTime = ${state.playbackTime}, bufferedTime = ${state.bufferedTime}`);
        }

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
        console.debug(`AudioPlayer: ${message}`);
    }

    private logWarn(message: string) {
        console.warn(`AudioPlayer: ${message}`);
    }

    private logError(message: string) {
        console.error(`AudioPlayer: ${message}`);
    }
}
