/* eslint-disable @typescript-eslint/ban-types */

import { Log, LogLevel, LogScope } from 'logging';
import { FeederAudioNode, FeederAudioWorklet, PlaybackState, ProcessorState } from './feeder-audio-worklet-contract';
import { rpcClientServer, RpcNoWait } from 'rpc';
import { Disposable } from 'disposable';

const LogScope: LogScope = 'FeederNode';
const errorLog = Log.get(LogScope, LogLevel.Error);

/** Part of the feeder that lives in main global scope. It's the counterpart of FeederAudioWorkletProcessor */
export class FeederAudioWorkletNode extends AudioWorkletNode implements FeederAudioNode {

    private readonly feederWorklet: FeederAudioWorklet & Disposable = null;

    public onStartPlaying?: () => void = null;
    public onBufferLow?: () => void = null;
    public onBufferTooMuch?: () => void = null;
    public onStarving?: () => void = null;
    public onPaused?: () => void = null;
    public onResumed?: () => void = null;
    /** If playing was started and now it's stopped */
    public onStopped?: () => void = null;
    /** Called at the end of the queue, even if the playing wasn't started */
    public onEnded?: () => void = null;

    private constructor(context: BaseAudioContext, name: string, options?: AudioWorkletNodeOptions) {
        super(context, name, options);
        this.onprocessorerror = this.onProcessorError;
        this.feederWorklet = rpcClientServer<FeederAudioWorklet>(`${LogScope}.feederWorklet`, this.port, this);
    }

    public static async create(
        decoderWorkerPort: MessagePort,
        context: BaseAudioContext,
        name: string,
        options?: AudioWorkletNodeOptions
    ): Promise<FeederAudioWorkletNode> {

        const node = new FeederAudioWorkletNode(context, name, options);
        await node.feederWorklet.init(decoderWorkerPort);
        return node;
    }

    public onStateUpdated(state: ProcessorState, noWait?: RpcNoWait): Promise<void> {
        switch (state) {
            case 'playing':
                this.onStartPlaying?.();
                break;
            case 'playingWithLowBuffer':
                this.onBufferLow?.();
                break;
            case 'playingWithTooMuchBuffer':
                this.onBufferTooMuch?.();
                break;
            case 'starving':
                this.onStarving?.();
                break;
            case 'paused':
                this.onPaused?.();
                break;
            case 'resumed':
                this.onResumed?.();
                break;
            case 'stopped':
                this.onStopped?.();
                break;
            case 'ended':
                this.onEnded?.();
                break;
        }
        return Promise.resolve(undefined);
    }

    public stop(): Promise<void> {
        return this.feederWorklet.stop();
    }

    public pause(): Promise<void> {
        return this.feederWorklet.pause();
    }

    public resume(): Promise<void> {
        return this.feederWorklet.resume();
    }

    public getState(): Promise<PlaybackState> {
        return this.feederWorklet.getState();
    }

    private onProcessorError = (ev: Event) => {
        errorLog?.log(`onProcessorError: unhandled error:`, ev);
    };
}
